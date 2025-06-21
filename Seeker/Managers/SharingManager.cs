using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Android.Widget;
using AndroidX.DocumentFile.Provider;
using Seeker.Exceptions;
using Seeker.Helpers;
using Seeker.Utils;
using Soulseek;

namespace Seeker.Managers;

// TODO: Rename this class into 'Sharing' and rename its child entities accordingly
public static class SharingManager
{
    public enum SharingIcons
    {
        Off = 0,
        Error = 1,
        On = 2,
        CurrentlyParsing = 3,
        OffDueToNetwork = 4,
    }
    
    // TODO: Give this event a more self-explanatory name
    public static event EventHandler<TransferItem> TransferAddedUINotify;
    
    /// <summary>
    /// Inform server the number of files we are sharing or 0,0 if not sharing...
    /// it looks like people typically report all including locked files. lets not report hidden files though.
    /// </summary>
    public static void InformServerOfSharedFiles()
    {
        try
        {
            if (SeekerState.SoulseekClient != null
                && SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.LoggedIn))
            {
                if (MeetsCurrentSharingConditions())
                {
                    if (SeekerState.SharedFileCache != null)
                    {
                        Logger.Debug("Tell server we are sharing "
                                     + SeekerState.SharedFileCache.DirectoryCount
                                     + " dirs and "
                                     + SeekerState.SharedFileCache.GetNonHiddenFileCountForServer()
                                     + " files");

                        SeekerState.SoulseekClient.SetSharedCountsAsync(
                            SeekerState.SharedFileCache.DirectoryCount,
                            SeekerState.SharedFileCache.GetNonHiddenFileCountForServer() != -1
                                ? SeekerState.SharedFileCache.GetNonHiddenFileCountForServer()
                                : SeekerState.SharedFileCache.FileCount
                        );
                    }
                    else
                    {
                        Logger.Debug("We would tell server but we are not successfully set up yet.");
                    }
                }
                else
                {
                    Logger.Debug("Tell server we are sharing 0 dirs and 0 files");
                    SeekerState.SoulseekClient.SetSharedCountsAsync(0, 0);
                }

                SeekerState.NumberOfSharedDirectoriesIsStale = false;
            }
            else
            {
                if (MeetsCurrentSharingConditions())
                {
                    if (SeekerState.SharedFileCache != null)
                    {
                        Logger.Debug("We need to Tell server we are sharing "
                                     + SeekerState.SharedFileCache.DirectoryCount
                                     + " dirs and "
                                     + SeekerState.SharedFileCache.GetNonHiddenFileCountForServer()
                                     + " files on next log in");
                    }
                    else
                    {
                        Logger.Debug("we meet sharing conditions " + 
                                     "but our shared file cache is not successfully set up");
                    }
                }
                else
                {
                    Logger.Debug("We need to Tell server we are sharing 0 dirs" +
                                 " and 0 files on next log in");
                }

                SeekerState.NumberOfSharedDirectoriesIsStale = true;
            }
        }
        catch (Exception e)
        {
            Logger.Debug("Failed to InformServerOfSharedFiles " + e.Message + e.StackTrace);
            Logger.FirebaseDebug("Failed to InformServerOfSharedFiles " + e.Message + e.StackTrace);
        }
    }
    
    /// <summary>
    /// Has set things up properly and has sharing on + their network settings currently allow it.
    /// </summary>
    /// <returns></returns>
    public static bool MeetsCurrentSharingConditions()
    {
        return MeetsSharingConditions() && SeekerState.IsNetworkPermitting();
    }

    public static bool IsSharingSetUpSuccessfully()
    {
        return SeekerState.SharedFileCache != null && SeekerState.SharedFileCache.SuccessfullyInitialized;
    }
    
    public static Tuple<SharingIcons, string> GetSharingMessageAndIcon(out bool isParsing)
    {
        isParsing = false;
        if (MeetsSharingConditions() && IsSharingSetUpSuccessfully())
        {
            // try to parse this into a path: SeekerState.ShareDataDirectoryUri
            if (MeetsCurrentSharingConditions())
            {
                return new Tuple<SharingIcons, string>(
                    SharingIcons.On,
                    SeekerState.ActiveActivityRef.GetString(Resource.String.success_sharing)
                );
            }
            else
            {
                // TODO: Use a string resource here
                return new Tuple<SharingIcons, string>(
                    SharingIcons.OffDueToNetwork,
                    "Sharing disabled on metered connection"
                );
            }
        }
        else if (MeetsSharingConditions() && !IsSharingSetUpSuccessfully())
        {
            if (SeekerState.SharedFileCache == null)
            {
                return new Tuple<SharingIcons, string>(SharingIcons.Off, "Not yet initialized.");
            }
            else
            {
                return new Tuple<SharingIcons, string>(
                    SharingIcons.Error,
                    SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_disabled_share_not_set)
                );
            }
        }
        else if (!SeekerState.SharingOn)
        {
            return new Tuple<SharingIcons, string>(
                SharingIcons.Off,
                SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_off)
            );
        }
        else if (SeekerState.IsParsing)
        {
            isParsing = true;
            return new Tuple<SharingIcons, string>(
                SharingIcons.CurrentlyParsing,
                SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_currently_parsing)
            );
        }
        else if (SeekerState.FailedShareParse)
        {
            return new Tuple<SharingIcons, string>(
                SharingIcons.Error,
                SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_disabled_failure_parsing)
            );
        }
        else if (UploadDirectoryManager.UploadDirectories.Count == 0)
        {
            return new Tuple<SharingIcons, string>(
                SharingIcons.Error,
                SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_disabled_share_not_set)
            );
        }
        else if (UploadDirectoryManager.AreAllFailed())
        {
            // TODO: get error
            return new Tuple<SharingIcons, string>(
                SharingIcons.Error,
                SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_disabled_error)
            );
        }
        else
        {
            return new Tuple<SharingIcons, string>(
                SharingIcons.Error,
                SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_disabled_error)
            );
        }
    }
    
    /// <summary>
    /// Has set things up properly and has sharing on.
    /// </summary>
    /// <returns></returns>
    public static bool MeetsSharingConditions()
    {
        return SeekerState.SharingOn
               && UploadDirectoryManager.UploadDirectories.Count != 0
               && !SeekerState.IsParsing
               && !UploadDirectoryManager.AreAllFailed();
    }
    
    public static void TurnOnSharing()
    {
        SeekerState.SoulseekClient.Options.SetSharedHandlers(
            BrowseResponseResolver,
            SearchResponseResolver,
            DirectoryContentsResponseResolver,
            EnqueueDownloadAction
        );
    }
    
    public static void TurnOffSharing()
    {
        SeekerState.SoulseekClient.Options.NullSharedHandlers();
    }
    
    public static void SetUpSharing(Action uiUpdateAction = null)
    {
        Action setUpSharedFileCache = () =>
        {
            string errorMessage = string.Empty;
            bool success = false;
            Logger.Debug("We meet sharing conditions, lets set up the sharedFileCache for 1st time.");
            
            try
            {
                // we check the cache which has ALL of the parsed results in it. much different from rescanning.
                success = SharedCacheManager
                    .InitializeDatabase(true, out errorMessage);
            }
            catch (Exception e)
            {
                Logger.Debug("Error setting up sharedFileCache for 1st time." + e.Message + e.StackTrace);
                SetUnsetSharingBasedOnConditions(false, true);

                if (!(e is DirectoryAccessFailure))
                {
                    Logger.FirebaseDebug("MainActivity error parsing: " + e.Message + "  " + e.StackTrace);
                }

                SeekerState.ActiveActivityRef.RunOnUiThread(new Action(() =>
                {
                    Toast.MakeText(
                        SeekerState.ActiveActivityRef,
                        SeekerState.ActiveActivityRef.GetString(Resource.String.error_sharing),
                        ToastLength.Long
                    ).Show();
                }));
            }

            if (success
                && SeekerState.SharedFileCache != null
                && SeekerState.SharedFileCache.SuccessfullyInitialized)
            {
                Logger.Debug("database full initialized.");
                SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                {
                    Toast.MakeText(
                        SeekerState.ActiveActivityRef,
                        SeekerState.ActiveActivityRef.GetString(Resource.String.success_sharing),
                        ToastLength.Short
                    )?.Show();
                });

                try
                {
                    // setup soulseek client with handlers if all conditions met
                    SetUnsetSharingBasedOnConditions(false);
                }
                catch (Exception e)
                {
                    Logger.FirebaseDebug("MainActivity error setting handlers: "
                                         + e.Message + "  " + e.StackTrace);
                }
            }
            else if (!success)
            {
                SeekerState.ActiveActivityRef.RunOnUiThread(new Action(() =>
                {
                    if (string.IsNullOrEmpty(errorMessage))
                    {
                        Toast.MakeText(
                            SeekerState.ActiveActivityRef,
                            SeekerState.ActiveActivityRef.GetString(Resource.String.error_sharing),
                            ToastLength.Short
                        ).Show();
                    }
                    else
                    {
                        Toast.MakeText(SeekerState.ActiveActivityRef, errorMessage, ToastLength.Short)
                            ?.Show();
                    }
                }));
            }

            if (uiUpdateAction != null)
            {
                SeekerState.ActiveActivityRef.RunOnUiThread(uiUpdateAction);
            }

            SeekerState.AttemptedToSetUpSharing = true;
        };
        ThreadPool.QueueUserWorkItem(_ => { setUpSharedFileCache(); });
    }
    
    /// <summary>
    /// Do this on any changes (like in Settings) but also on Login.
    /// </summary>
    /// <param name="informServerOfChangeIfThereIsAChange"></param>
    /// <param name="force">force if we are chaning the upload directory...</param>
    public static void SetUnsetSharingBasedOnConditions(
        bool informServerOfChangeIfThereIsAChange,
        bool force = false)
    {
        // when settings gets recreated can get nullref here.
        bool wasShared = SeekerState.SoulseekClient.Options.SearchResponseResolver != null;
        if (SharingManager.MeetsCurrentSharingConditions())
        {
            SharingManager.TurnOnSharing();
            if (!wasShared || force)
            {
                Logger.Debug("sharing state changed to ON");
                SharingManager.InformServerOfSharedFiles();
            }
        }
        else
        {
            SharingManager.TurnOffSharing();
            if (wasShared)
            {
                Logger.Debug("sharing state changed to OFF");
                SharingManager.InformServerOfSharedFiles();
            }
        }
    }
    
    /// <summary>
    /// Creates and returns an <see cref="IEnumerable{T}"/> of <see cref="Soulseek.Directory"/>
    /// in response to a remote request.
    /// </summary>
    /// <param name="username">The username of the requesting user.</param>
    /// <param name="endpoint">The IP endpoint of the requesting user.</param>
    /// <returns>A Task resolving an IEnumerable of Soulseek.Directory.</returns>
    private static Task<BrowseResponse> BrowseResponseResolver(string username, IPEndPoint endpoint)
    {
        if (SeekerApplication.IsUserInIgnoreList(username))
        {
            return Task.FromResult(new BrowseResponse(Enumerable.Empty<Directory>()));
        }

        return Task.FromResult(SeekerState.SharedFileCache.GetBrowseResponseForUser(username));
    }
    
    /// <summary>
    ///     Creates and returns a <see cref="Soulseek.Directory"/> in response to a remote request.
    /// </summary>
    /// <param name="username">The username of the requesting user.</param>
    /// <param name="endpoint">The IP endpoint of the requesting user.</param>
    /// <param name="token">The unique token for the request, supplied by the requesting user.</param>
    /// <param name="directory">The requested directory.</param>
    /// <returns>A Task resolving an instance of Soulseek.Directory
    /// containing the contents of the requested directory.</returns>
    private static Task<Directory> DirectoryContentsResponseResolver(
        string username,
        IPEndPoint endpoint,
        int token,
        string directory)
    {
        // the directory is the presentable name.
        // the old EndsWith(dir) fails if the directory is not unique
        // i.e. document structure of Soulseek Complete > some dirs and files,
        // Soulseek Complete > more dirs and files..
        Tuple<string, string> fullDirUri = SeekerState.SharedFileCache.FriendlyDirNameToUriMapping
            .Where((Tuple<string, string> t) => { return t.Item1 == directory; })
            .FirstOrDefault(); // TODO: DICTIONARY>>>>>

        if (fullDirUri == null)
        {
            // as fallback safety.  I dont think this will ever happen.....
            fullDirUri = SeekerState.SharedFileCache.FriendlyDirNameToUriMapping
                .Where((Tuple<string, string> t) => { return t.Item1.EndsWith(directory); })
                .FirstOrDefault();
        }

        DocumentFile fullDir = null;
        if (SeekerState.PreOpenDocumentTree() || !UploadDirectoryManager.IsFromTree()) //todo
        {
            fullDir = DocumentFile.FromFile(new Java.IO.File(Android.Net.Uri.Parse(fullDirUri.Item2).Path));
        }
        else
        {
            fullDir =
                DocumentFile.FromTreeUri(SeekerState.ActiveActivityRef, Android.Net.Uri.Parse(fullDirUri.Item2));
        }

        var slskDir = fullDir.ToSoulseekDirectory(StorageUtils
            .GetVolumeName(fullDir.Uri.LastPathSegment, false, out _));

        slskDir = new Directory(directory, slskDir.Files);
        return Task.FromResult(slskDir);
    }
    
     /// <summary>
    /// Creates and returns a <see cref="SearchResponse"/> in response to the given <paramref name="query"/>.
    /// </summary>
    /// <param name="username">The username of the requesting user.</param>
    /// <param name="token">The search token.</param>
    /// <param name="query">The search query.</param>
    /// <returns>A Task resolving a SearchResponse, or null.</returns>
    private static Task<SearchResponse> SearchResponseResolver(string username, int token, SearchQuery query)
    {
        var defaultResponse = Task.FromResult<SearchResponse>(null);

        // some bots continually query for very common strings.  blacklist known names here.
        var blacklist = new[] { "Lola45", "Lolo51", "rajah" };
        if (blacklist.Contains(username))
        {
            return defaultResponse;
        }

        if (SeekerApplication.IsUserInIgnoreList(username))
        {
            return defaultResponse;
        }

        // some bots and perhaps users search for very short terms.
        // only respond to queries >= 3 characters.  sorry, U2 fans.
        if (query.Query.Length < 5)
        {
            return defaultResponse;
        }

        if (SeekerState.Username == null
            || SeekerState.Username == string.Empty
            || SeekerState.SharedFileCache == null)
        {
            return defaultResponse;
        }

        var results =
            SeekerState.SharedFileCache.Search(query, username, out IEnumerable<File> lockedResults);

        if (results.Any() || lockedResults.Any())
        {
            int ourUploadSpeed = 1024 * 256;
            if (SeekerState.UploadSpeed > 0)
            {
                ourUploadSpeed = SeekerState.UploadSpeed;
            }

            return Task.FromResult(new SearchResponse(
                SeekerState.Username,
                token,
                freeUploadSlots: 1,
                uploadSpeed: ourUploadSpeed,
                queueLength: 0,
                fileList: results,
                lockedFileList: lockedResults));
        }

        // if no results, either return null or an instance of SearchResponse with a fileList of length 0
        // in either case, no response will be sent to the requestor.
        return Task.FromResult<SearchResponse>(null);
    }
     
    /// <summary>
    /// Invoked upon a remote request to download a file.
    /// THE ORIGINAL BUT WITHOUT ITRANSFERTRACKER!!!!
    /// </summary>
    /// <param name="username">The username of the requesting user.</param>
    /// <param name="endpoint">The IP endpoint of the requesting user.</param>
    /// <param name="filename">The filename of the requested file.</param>
    /// <param name="tracker">(for example purposes) the ITransferTracker used to track progress.</param>
    /// <returns>A Task representing the asynchronous operation.</returns>
    /// <exception cref="DownloadEnqueueException">
    /// Thrown when the download is rejected.
    /// The Exception message will be passed to the remote user.
    /// </exception>
    /// <exception cref="Exception">
    /// Thrown on any other Exception other than a rejection.
    /// A generic message will be passed to the remote user for security reasons.
    /// </exception>
    private static Task EnqueueDownloadAction(string username, IPEndPoint endpoint, string filename)
    {
        if (SeekerApplication.IsUserInIgnoreList(username))
        {
            return Task.CompletedTask;
        }

        // the filename is basically "the key"
        _ = endpoint;
        string errorMsg = null;
        Tuple<long, string, Tuple<int, int, int, int>, bool, bool> ourFileInfo =
            SeekerState.SharedFileCache.GetFullInfoFromSearchableName(filename, out errorMsg);

        if (ourFileInfo == null)
        {
            Logger.FirebaseDebug("ourFileInfo is null: " + ourFileInfo + " " + errorMsg);
            throw new DownloadEnqueueException($"File not found.");
        }

        DocumentFile ourFile = null;
        Android.Net.Uri ourUri = Android.Net.Uri.Parse(ourFileInfo.Item2);

        if (ourFileInfo.Item4 || ourFileInfo.Item5)
        {
            // locked or hidden (hidden shouldnt happen but just in case, it should still be userlist only)
            // CHECK USER LIST
            if (!SlskHelp.CommonHelpers.UserListChecker.IsInUserList(username))
            {
                throw new DownloadEnqueueException($"File not shared");
            }
        }

        if (SeekerState.PreOpenDocumentTree() || !UploadDirectoryManager.IsFromTree()) // IsFromTree method!
        {
            ourFile = DocumentFile.FromFile(new Java.IO.File(ourUri.Path));
        }
        else
        {
            ourFile = DocumentFile.FromTreeUri(SeekerState.ActiveActivityRef, ourUri);
        }

        if (!ourFile.Exists())
        {
            throw new DownloadEnqueueException($"File not found.");
        }

        // create a new cancellation token source so that we can cancel the upload from the UI.
        var cts = new CancellationTokenSource();

        TransferItem transferItem = new TransferItem();
        transferItem.Username = username;
        transferItem.FullFilename = filename;
        transferItem.Filename = CommonHelpers.GetFileNameFromFile(filename);
        transferItem.FolderName = Common.Helpers.GetFolderNameFromFile(filename);
        transferItem.CancellationTokenSource = cts;
        transferItem.Size = ourFile.Length();
        transferItem.isUpload = true;
        transferItem = TransfersFragment.TransferItemManagerUploads
            .AddIfNotExistAndReturnTransfer(transferItem, out bool exists);

        if (!exists) // else the state will simply be updated a bit later. 
        {
            TransferAddedUINotify?.Invoke(null, transferItem);
        }

        // accept all download requests, and begin the upload immediately.
        // normally there would be an internal queue, and uploads would be handled separately.
        Task.Run(async () =>
        {
            CancellationTokenSource oldCts = null;
            try
            {
                // outputstream.CanRead is false...
                using var stream = SeekerState.MainActivityRef.ContentResolver.OpenInputStream(ourFile.Uri);

                TransfersFragment.SetupCancellationToken(transferItem, cts, out oldCts);

                // THE FILENAME THAT YOU PASS INTO HERE MUST MATCH EXACTLY
                // ELSE THE CLIENT WILL REJECT IT.  //MUST MATCH EXACTLY THE ONE THAT WAS REQUESTED THAT IS.
                await SeekerState.SoulseekClient.UploadAsync(
                    username,
                    filename,
                    transferItem.Size,
                    stream,
                    options: new TransferOptions(governor: SpeedLimitHelper.OurUploadGoverner),
                    cancellationToken: cts.Token
                );
            }
            catch (DuplicateTransferException dup) //not tested
            {
                Logger.Debug("UPLOAD DUPL - " + dup.Message);

                // if there is a duplicate you do not want to overwrite the good cancellation token
                // with a meaningless one. so restore the old one.
                TransfersFragment.SetupCancellationToken(transferItem, oldCts, out _);
            }
            catch (DuplicateTokenException dup)
            {
                Logger.Debug("UPLOAD DUPL - " + dup.Message);

                // if there is a duplicate you do not want to overwrite the good cancellation token
                // with a meaningless one. so restore the old one.
                TransfersFragment.SetupCancellationToken(transferItem, oldCts, out _);
            }
        }).ContinueWith(t => { }, TaskContinuationOptions.NotOnRanToCompletion); // fire and forget

        // return a completed task so that the invoking code can respond to the remote client.
        return Task.CompletedTask;
    }
}