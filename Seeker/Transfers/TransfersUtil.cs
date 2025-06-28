using Soulseek;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _Microsoft.Android.Resource.Designer;
using Seeker.Helpers;
using Seeker.Main;
using Seeker.Managers;
using Seeker.Models;
using Seeker.Utils;

namespace Seeker.Transfers
{
    public static class TransfersUtil
    {    
        public static Task CreateDownloadAllTask(FullFileInfo[] files, bool queuePaused, string username)
        {
            if (username == SeekerState.Username)
            {
                SeekerApplication.ApplicationContext.ShowLongToast(ResourceConstant.String.cannot_download_from_self);
                
                // since we call start on the task, if we call Task.Completed or Task.Delay(0) it will crash...
                return new Task(() => { });
            }

            var task = new Task(() =>
            {
                EnqueueFiles(files, queuePaused, username);
            });

            return task;
        }

        public static async Task EnqueueFiles(FullFileInfo[] files, bool queuePaused, string username)
        {
            var allExist = true; // only show the transfer exists if all transfers in question do already exist
            var isSingle = files.Count() == 1;
            var downloadInfos = new List<DownloadInfo>();
            
            foreach (var file in files)
            {
                var dlInfo = AddTransfer(
                    username,
                    file.FullFileName,
                    file.Size,
                    int.MaxValue,
                    file.Depth,
                    queuePaused, 
                    file.wasFilenameLatin1Decoded, 
                    file.wasFolderLatin1Decoded,
                    isSingle,
                    out var transferExists
                );
                
                downloadInfos.Add(dlInfo);
                if (!transferExists)
                {
                    allExist = false;
                }
            }

            var toastMessage = allExist
                ? ResourceConstant.String.error_duplicate
                : queuePaused
                    ? ResourceConstant.String.QueuedForDownload
                    : ResourceConstant.String.download_is_starting;
            SeekerApplication.ApplicationContext.ShowShortToast(toastMessage);

            if (!allExist && !queuePaused)
            {
                await DownloadFiles(downloadInfos, files, username);
            }
        }

        /// <remarks>
        /// Previously we would fireoff DownloadFileAsync tasks one after another.
        /// This would cause files do download out of order and other side effects.
        /// Update the logic to be more similar to slskd.
        /// </remarks>
        private static async Task DownloadFiles(List<DownloadInfo> dlInfos, FullFileInfo[] files, string username)
        {
            for (var i = 0; i < dlInfos.Count; i++)
            {
                var dlInfo = dlInfos[i];
                var file = files[i];
                var dlTask = DownloadFileAsync(username, file.FullFileName, file.Size, dlInfo.CancellationTokenSource, out Task waitForNext, file.Depth, file.wasFilenameLatin1Decoded, file.wasFolderLatin1Decoded);
                var e = new DownloadAddedEventArgs(dlInfo);
                var continuationActionSaveFile = DownloadQueue.DownloadContinuationActionUi(e);
                dlTask.ContinueWith(continuationActionSaveFile);
        
                // wait for current download to update to queued / initialized or dltask to throw exception before kicking off next 
                await waitForNext;
            }
        }
        
        /// <summary>Adds a transfer to the database. Does not</summary>
        public static DownloadInfo AddTransfer(string username, string fname, long size, int queueLength, int depth, bool queuePaused, bool wasLatin1Decoded, bool wasFolderLatin1Decoded, bool isSingle, out bool errorExists)
        {
            errorExists = false;
            Task dlTask = null;
            var cancellationTokenSource = new CancellationTokenSource();
            var exists = false;
            TransferItem transferItem = null;
            DownloadInfo downloadInfo = null;
            CancellationTokenSource oldCts = null;
            
            try
            {

                downloadInfo = new DownloadInfo(username, fname, size, dlTask, cancellationTokenSource, queueLength, 0, depth);

                transferItem = new TransferItem();
                transferItem.Filename = CommonHelpers.GetFileNameFromFile(downloadInfo.fullFilename);
                transferItem.FolderName = Common.Helpers.GetFolderNameFromFile(downloadInfo.fullFilename, depth);
                transferItem.Username = downloadInfo.username;
                transferItem.FullFilename = downloadInfo.fullFilename;
                transferItem.Size = downloadInfo.Size;
                transferItem.QueueLength = downloadInfo.QueueLength;
                transferItem.WasFilenameLatin1Decoded = wasLatin1Decoded;
                transferItem.WasFolderLatin1Decoded = wasFolderLatin1Decoded;
                
                if (isSingle && SeekerState.NoSubfolderForSingle.Value)
                {
                    transferItem.TransferItemExtra = TransferItemExtras.NoSubfolder;
                }

                if (!queuePaused)
                {
                    try
                    {
                        // if its already there we don't add it...
                        TransfersFragment.SetupCancellationToken(transferItem, downloadInfo.CancellationTokenSource,
                            out oldCts);
                    }
                    catch (Exception errr)
                    {
                        // I think this is fixed by changing to concurrent dict but just in case...
                        Logger.FirebaseDebug("concurrency issue: " + errr);
                    }
                }
                
                transferItem = TransfersFragment.TransferItemManagerDL
                    .AddIfNotExistAndReturnTransfer(transferItem, out exists);
                
                Logger.Debug($"Adding Transfer To Database: {transferItem.Filename}");
                downloadInfo.TransferItemReference = transferItem;

                if (queuePaused)
                {
                    transferItem.State = TransferStates.Cancelled;
                    
                    // otherwise the ui will not refresh.
                    MainActivity.InvokeDownloadAddedUiNotify(new DownloadAddedEventArgs(null));
                }
                else
                {
                    var e = new DownloadAddedEventArgs(downloadInfo);
                    MainActivity.InvokeDownloadAddedUiNotify(e);
                }
            }
            catch (Exception e)
            {
                if (!exists)
                {
                    // if it did not previously exist then remove it...
                    TransfersFragment.TransferItemManagerDL.Remove(transferItem);
                }
                else
                {
                    errorExists = exists;
                }
                
                if (oldCts != null)
                {
                    // put it back...
                    TransfersFragment.SetupCancellationToken(transferItem, oldCts, out _);
                }
            }
            return downloadInfo;
        }

        /// <summary>
        /// takes care of resuming incomplete downloads, switching between mem and file backed, creating the incompleteUri dir.
        /// its the same as the old SeekerState.SoulseekClient.DownloadAsync but with a few bells and whistles...
        /// </summary>
        /// <param name="username"></param>
        /// <param name="fullfilename"></param>
        /// <param name="size"></param>
        /// <param name="cts"></param>
        /// <param name="incompleteUri"></param>
        /// <returns></returns>
        public static Task DownloadFileAsync(string username, string fullfilename, long? size, CancellationTokenSource cts, out Task waitForNext, int depth = 1, bool isFileDecodedLegacy = false, bool isFolderDecodedLegacy = false) //an indicator for how much of the full filename to use...
        {
            var waitUntilEnqueue = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            Logger.Debug($"DownloadFileAsync: {fullfilename}");
            Task dlTask;
            Action<TransferStateChangedEventArgs> updateForEnqueue = args =>
            {
                if (args.Transfer.State.HasFlag(TransferStates.Queued) 
                    || args.Transfer.State == TransferStates.Initializing)
                {
                    Logger.Debug($"Queued / Init: {fullfilename} We can proceed to download next file.");
                    waitUntilEnqueue.TrySetResult(true);
                }
            };
            
            if (SeekerState.MemoryBackedDownload)
            {
                dlTask =
                    SeekerState.SoulseekClient.DownloadAsync(
                        username: username,
                        filename: fullfilename,
                        size: size,
                        options: new TransferOptions(governor: SpeedLimitHelper.OurDownloadGoverner, stateChanged: updateForEnqueue),
                        cancellationToken: cts.Token,
                        isLegacy: isFileDecodedLegacy,
                        isFolderDecodedLegacy: isFolderDecodedLegacy);
            }
            else
            {
                long partialLength = 0;

                dlTask = SeekerState.SoulseekClient.DownloadAsync(
                        username: username,
                        filename: fullfilename,
                        null,
                        size: size,
                        startOffset: partialLength, //this will get populated
                        options: new TransferOptions(disposeOutputStreamOnCompletion: true, governor: SpeedLimitHelper.OurDownloadGoverner, stateChanged: updateForEnqueue),
                        cancellationToken: cts.Token,
                        streamTask: GetStreamTask(username, fullfilename, depth),
                        isFilenameDecodedLegacy: isFileDecodedLegacy,
                        isFolderDecodedLegacy: isFolderDecodedLegacy);
            }
            waitForNext = Task.WhenAny(waitUntilEnqueue.Task, dlTask);
            return dlTask;
        }

        public static Task<Tuple<System.IO.Stream, long, string, string>> GetStreamTask(string username, string fullfilename, int depth = 1) //there has to be something extra here for args, bc we need to denote just how much of the fullFilename to use....
        {
            Task<Tuple<System.IO.Stream, long, string, string>> task = new Task<Tuple<System.IO.Stream, long, string, string>>(
                () =>
                {
                    long partialLength = 0;
                    Android.Net.Uri incompleteUri = null;
                    Android.Net.Uri incompleteUriDirectory = null;
                    System.IO.Stream streamToWriteTo = MainActivity.GetIncompleteStream(username, fullfilename, depth, out incompleteUri, out incompleteUriDirectory, out partialLength); //something here to denote...
                    return new Tuple<System.IO.Stream, long, string, string>(streamToWriteTo, partialLength, incompleteUri.ToString(), incompleteUriDirectory.ToString());
                });
            return task;
        }

    }
}
