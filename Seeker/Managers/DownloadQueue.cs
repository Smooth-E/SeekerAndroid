using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using _Microsoft.Android.Resource.Designer;
using Seeker.Exceptions;
using Seeker.Main;
using Seeker.Models;
using Seeker.Transfers;
using Seeker.Utils;
using Soulseek;

namespace Seeker.Managers;

public static class DownloadQueue
{
    // for transferItemPage to update its recyclerView
    public static EventHandler<TransferItem> TransferItemQueueUpdated;
    
    public static void GetDownloadPlaceInQueue(
        string username,
        string fullFileName,
        bool addIfNotAdded,
        bool silent,
        TransferItem transferItemInQuestion = null,
        Func<TransferItem, object> actionOnComplete = null)
    {

        if (SeekerState.CurrentlyLoggedInButDisconnectedState())
        {
            if (!SoulseekConnection.ShowMessageAndCreateReconnectTask(SeekerState.ActiveActivityRef, false, out var reconnectTask))
            {
                reconnectTask.ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        SeekerState.ActiveActivityRef.ShowLongToast(ResourceConstant.String.failed_to_connect);
                        return;
                    }

                    SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                    {
                        GetDownloadPlaceInQueueLogic(
                            username,
                            fullFileName,
                            addIfNotAdded,
                            silent,
                            transferItemInQuestion,
                            actionOnComplete
                        );
                    });
                });
            }
        }
        else
        {
            GetDownloadPlaceInQueueLogic(
                username,
                fullFileName,
                addIfNotAdded,
                silent,
                transferItemInQuestion,
                actionOnComplete
            );
        }
    }
    
    private static void GetDownloadPlaceInQueueLogic(
        string username,
        string fullFileName,
        bool addIfNotAdded,
        bool silent,
        TransferItem transferItemInQuestion = null,
        Func<TransferItem, object> actionOnComplete = null)
    {

        var updateTask = new Action<Task<int>>(
            taask =>
            {
                if (taask.IsFaulted)
                {
                    bool transitionToNextState = false;
                    TransferStates state = TransferStates.Errored;
                    if (taask.Exception?.InnerException is UserOfflineException)
                    {
                        // Nicotine always immediately transitions from queued to user offline
                        // the second the user goes offline. We dont do it immediately but on next check.
                        // for QT you always are in "Queued" no matter what.
                        transitionToNextState = true;

                        state = TransferStates.Errored
                                | TransferStates.UserOffline
                                | TransferStates.FallenFromQueue;

                        if (!silent)
                        {
                            var userIsOfflineString = SeekerApplication.GetString(Resource.String.UserXIsOffline);
                            var formattedString = string.Format(userIsOfflineString, username);
                            MainActivity.ToastUiWithDebouncer(formattedString, "_6_", username);
                        }
                    }
                    else if (taask.Exception?.InnerException?.Message != null
                             && taask.Exception.InnerException.Message.Contains(
                                 SoulseekClient.FailedToEstablishDirectOrIndirectStringLower,
                                 StringComparison.CurrentCultureIgnoreCase))
                    {
                        // Nicotine transitions from Queued to Cannot Connect
                        // IF you pause and resume. Otherwise you stay in Queued.
                        // Here if someone explicitly retries (i.e. silent = false) then we will transition states.
                        // otherwise, its okay, lets just stay in Queued.
                        // for QT you always are in "Queued" no matter what.

                        transitionToNextState = !silent;

                        state = TransferStates.Errored
                                | TransferStates.CannotConnect
                                | TransferStates.FallenFromQueue;

                        if (!silent)
                        {
                            var cannotConnectString =
                                SeekerApplication.GetString(Resource.String.CannotConnectUserX);

                            MainActivity.ToastUiWithDebouncer(string.Format(cannotConnectString, username), "_7_", username);
                        }
                    }
                    else if (taask.Exception?.InnerException?.Message != null &&
                             taask.Exception.InnerException is System.TimeoutException)
                    {
                        // they may just not be sending queue position messages.
                        // that is okay, we can still connect to them just fine for download time.
                        if (!silent)
                        {
                            var messageString = SeekerApplication.GetString(ResourceConstant.String.TimeoutQueueUserX);
                            MainActivity.ToastUiWithDebouncer(string.Format(messageString, username), "_8_", username, 6);
                        }
                    }
                    else if (taask.Exception?.InnerException?.Message != null
                             && taask.Exception.InnerException.Message.Contains("underlying Tcp connection is closed"))
                    {
                        // can be server connection (get user endpoint) or peer connection.
                        if (!silent)
                        {
                            var formattedString =
                                $"Failed to get queue position for {username}: Connection was unexpectedly closed.";
                            MainActivity.ToastUiWithDebouncer(formattedString, "_9_", username, 6);
                        }
                    }
                    else
                    {
                        if (!silent)
                        {
                            MainActivity.ToastUiWithDebouncer($"Error getting queue position from {username}", "_9_", username);
                        }

                        Logger.FirebaseDebug("GetDownloadPlaceInQueue" + taask.Exception);
                    }

                    if (!transitionToNextState)
                    {
                        return;
                    }
                    
                    // update the transferItem array
                    transferItemInQuestion ??= TransfersFragment.TransferItemManagerDL
                        .GetTransferItemWithIndexFromAll(fullFileName, username, out _);

                    if (transferItemInQuestion == null)
                    {
                        return;
                    }

                    try
                    {
                        transferItemInQuestion.CancellationTokenSource.Cancel();
                    }
                    catch (Exception err)
                    {
                        Logger.FirebaseDebug("cancellation token src issue: " + err.Message);
                    }

                    transferItemInQuestion.State = state;
                }
                else
                {
                    bool queuePositionChanged;

                    // update the transferItem array
                    transferItemInQuestion ??= TransfersFragment.TransferItemManagerDL
                        .GetTransferItemWithIndexFromAll(fullFileName, username, out _);

                    if (transferItemInQuestion == null)
                    {
                        return;
                    }

                    queuePositionChanged = transferItemInQuestion.QueueLength != taask.Result;
                    transferItemInQuestion.QueueLength = taask.Result >= 0 ? taask.Result : int.MaxValue;

                    Logger.Debug(queuePositionChanged
                        ? $"Queue Position of {fullFileName} has changed to {taask.Result}"
                        : $"Queue Position of {fullFileName} is still {taask.Result}");

                    if (actionOnComplete != null)
                    {
                        SeekerState.ActiveActivityRef?.RunOnUiThread(() =>
                        {
                            actionOnComplete(transferItemInQuestion);
                        });
                    }
                    else
                    {
                        if (queuePositionChanged)
                        {
                            // if the transfer item fragment is bound then we update it..
                            TransferItemQueueUpdated?.Invoke(null, transferItemInQuestion);
                        }
                    }

                }
            }
        );

        Task<int> getDownloadPlace;
        try
        {
            getDownloadPlace = SeekerState.SoulseekClient.GetDownloadPlaceInQueueAsync(
                username,
                fullFileName,
                null,
                transferItemInQuestion!.ShouldEncodeFileLatin1(),
                transferItemInQuestion.ShouldEncodeFolderLatin1()
            );
        }
        catch (TransferNotFoundException)
        {
            if (addIfNotAdded)
            {
                // it is not downloading... therefore retry the download...
                transferItemInQuestion ??= TransfersFragment.TransferItemManagerDL
                    .GetTransferItemWithIndexFromAll(fullFileName, username, out _);

                var cancellationTokenSource = new CancellationTokenSource();
                try
                {
                    transferItemInQuestion.QueueLength = int.MaxValue;

                    // else when you go to cancel you are cancelling an already cancelled useless token!!
                    TransfersFragment
                        .SetupCancellationToken(transferItemInQuestion, cancellationTokenSource, out _);

                    var task = TransfersUtil.DownloadFileAsync(
                        transferItemInQuestion.Username,
                        transferItemInQuestion.FullFilename,
                        transferItemInQuestion.GetSizeForDL(),
                        cancellationTokenSource,
                        out _,
                        isFileDecodedLegacy: transferItemInQuestion.ShouldEncodeFileLatin1(),
                        isFolderDecodedLegacy: transferItemInQuestion.ShouldEncodeFolderLatin1()
                    );

                    task.ContinueWith(DownloadContinuationActionUi(
                        new DownloadAddedEventArgs(
                            new DownloadInfo(
                                transferItemInQuestion.Username,
                                transferItemInQuestion.FullFilename,
                                transferItemInQuestion.Size, task,
                                cancellationTokenSource,
                                transferItemInQuestion.QueueLength,
                                0,
                                transferItemInQuestion.GetDirectoryLevel()
                            ) { TransferItemReference = transferItemInQuestion }
                        )
                    ), cancellationTokenSource.Token);
                }
                catch (DuplicateTransferException)
                {
                    // happens due to button mashing...
                    return;
                }
                catch (Exception error)
                {
                    var a = () =>
                    {
                        // TODO: Logging errors through toasts isn't a good practice
                        var activityReference = SeekerState.ActiveActivityRef;
                        var prefix = activityReference.GetString(ResourceConstant.String.error_);
                        activityReference.ShowLongToast(prefix + error.Message);
                    };

                    if (!error.Message.Contains("must be connected and logged"))
                    {
                        Logger.FirebaseDebug(error.Message + " OnContextItemSelected");
                    }

                    if (!silent)
                    {
                        SeekerState.ActiveActivityRef.RunOnUiThread(a);
                    }

                    return; // otherwise null ref with task!
                }

                //TODO: THIS OCCURS TO SOON, ITS NOT gaurentted for the transfer to be in downloads yet...
                try
                {
                    getDownloadPlace = SeekerState.SoulseekClient.GetDownloadPlaceInQueueAsync(
                        username,
                        fullFileName,
                        null,
                        transferItemInQuestion.ShouldEncodeFileLatin1(),
                        transferItemInQuestion.ShouldEncodeFolderLatin1()
                    );

                    getDownloadPlace.ContinueWith(updateTask);
                }
                catch (Exception e)
                {
                    Logger.FirebaseDebug("you likely called getdownloadplaceinqueueasync too soon..." + e.Message);
                }

                return;
            }

            Logger.Debug("Transfer Item we are trying to get queue position " +
                         "of is not currently being downloaded.");
            return;
        }
        catch (Exception)
        {
            return;
        }

        getDownloadPlace.ContinueWith(updateTask);
    }
    
    private static void GetDownloadPlaceInQueueBatchLogic(List<TransferItem> transferItems, bool addIfNotAdded)
    {
        foreach (var transferItem in transferItems)
        {
            GetDownloadPlaceInQueueLogic(
                transferItem.Username,
                transferItem.FullFilename,
                addIfNotAdded,
                true,
                transferItem
            );
        }
    }
    
    public static void GetDownloadPlaceInQueueBatch(List<TransferItem> transferItems, bool addIfNotAdded)
    {
        if (SeekerState.CurrentlyLoggedInButDisconnectedState())
        {
            if (!SoulseekConnection.ShowMessageAndCreateReconnectTask(SeekerState.ActiveActivityRef, false, out var reconnectTask))
            {
                reconnectTask.ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        return;
                    }

                    SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                    {
                        GetDownloadPlaceInQueueBatchLogic(transferItems, addIfNotAdded);
                    });
                });
            }
        }
        else
        {
            GetDownloadPlaceInQueueBatchLogic(transferItems, addIfNotAdded);
        }
    }
    
    /// <summary>This RETURNS the task for ContinueWith</summary>
    public static Action<Task> DownloadContinuationActionUi(DownloadAddedEventArgs e)
    {
        // TODO: Pass context to this action for displaying toasts
        Action<Task> continuationActionSaveFile = task =>
        {
            try
            {
                Action action = null;
                if (task.IsCanceled)
                {
                    Logger.Debug((DateTimeOffset.Now.ToUnixTimeMilliseconds()
                                  - SeekerState.TaskWasCancelledToastDebouncer).ToString());

                    if ((DateTimeOffset.Now.ToUnixTimeMilliseconds()
                         - SeekerState.TaskWasCancelledToastDebouncer) > 1000)
                    {
                        SeekerState.TaskWasCancelledToastDebouncer = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    }

                    // if we pressed "Retry Download" and it was in progress so we first had to cancel...
                    if (e.dlInfo.TransferItemReference.CancelAndRetryFlag)
                    {
                        e.dlInfo.TransferItemReference.CancelAndRetryFlag = false;
                        try
                        {
                            // retry download.
                            var cancellationTokenSource = new CancellationTokenSource();

                            // else when you go to cancel you are cancelling an already cancelled useless token!!
                            TransfersFragment.SetupCancellationToken(e.dlInfo.TransferItemReference,
                                cancellationTokenSource, out _);

                            var retryTask = TransfersUtil.DownloadFileAsync(
                                e.dlInfo.username,
                                e.dlInfo.fullFilename,
                                e.dlInfo.TransferItemReference.Size,
                                cancellationTokenSource,
                                out _,
                                1,
                                e.dlInfo.TransferItemReference.ShouldEncodeFileLatin1(),
                                e.dlInfo.TransferItemReference.ShouldEncodeFolderLatin1()
                            );

                            retryTask.ContinueWith(DownloadContinuationActionUi(
                                new DownloadAddedEventArgs(
                                    new DownloadInfo(
                                        e.dlInfo.username,
                                        e.dlInfo.fullFilename,
                                        e.dlInfo.TransferItemReference.Size,
                                        retryTask,
                                        cancellationTokenSource,
                                        e.dlInfo.QueueLength,
                                        0,
                                        task.Exception,
                                        e.dlInfo.Depth
                                    )
                                )
                            ), cancellationTokenSource.Token);
                        }
                        catch (Exception exception)
                        {
                            // disconnected error
                            if (exception is System.InvalidOperationException
                                && exception.Message.ToLower()
                                    .Contains("server connection must be connected and logged in"))
                            {
                                action = () =>
                                {
                                    MainActivity.ToastUiWithDebouncer(
                                        SeekerApplication.GetString(Resource.String.MustBeLoggedInToRetryDL),
                                        "_16_"
                                    );
                                };
                            }
                            else
                            {
                                Logger.FirebaseDebug("cancel and retry creation failed: "
                                                     + exception.Message + exception.StackTrace);
                            }

                            if (action != null)
                            {
                                SeekerState.ActiveActivityRef.RunOnUiThread(action);
                            }
                        }
                    }

                    if (e.dlInfo.TransferItemReference.CancelAndClearFlag)
                    {
                        Logger.Debug("continue with cleanup activity: " + e.dlInfo.fullFilename);
                        e.dlInfo.TransferItemReference.CancelAndRetryFlag = false;
                        e.dlInfo.TransferItemReference.InProcessing = false;

                        // this way we are sure that the stream is closed.
                        TransferItemManagerWrapper.PerformCleanupItem(e.dlInfo.TransferItemReference);
                    }

                    return;
                }
                else if (task.Status == TaskStatus.Faulted)
                {
                    bool retriable = false;
                    bool forceRetry = false;

                    // in the cases where there is mojibake, and you undo it, you still cannot download from Nicotine older client.
                    // reason being: the shared cache and disk do not match.
                    // so if you send them the filename on disk they will say it is not in the cache.
                    // and if you send them the filename from cache they will say they could not find it on disk.

                    bool resetRetryCount = false;
                    var transferItem = e.dlInfo.TransferItemReference;

                    if (task.Exception.InnerException is TimeoutException)
                    {
                        action = () =>
                        {
                            // TODO: Do not use static Context references
                            SeekerState.ActiveActivityRef.ShowLongToast(ResourceConstant.String.timeout_peer);
                        };
                    }
                    else if (task.Exception.InnerException is TransferSizeMismatchException sizeException)
                    {
                        // THIS SHOULD NEVER HAPPEN. WE FIX THE TRANSFER SIZE MISMATCH INLINE.

                        // update the size and rerequest.
                        // if we have partially downloaded the file already
                        // we need to delete it to prevent corruption.
                        Logger.Debug($"OLD SIZE {transferItem.Size} NEW SIZE {sizeException.RemoteSize}");
                        transferItem.Size = sizeException.RemoteSize;
                        e.dlInfo.Size = sizeException.RemoteSize;
                        retriable = true;
                        forceRetry = true;
                        resetRetryCount = true;

                        if (!string.IsNullOrEmpty(transferItem.IncompleteParentUri))
                        {
                            try
                            {
                                TransferItemManagerWrapper.PerformCleanupItem(transferItem);
                            }
                            catch (Exception ex)
                            {
                                var exceptionString = 
                                    "Failed to delete incomplete file on TransferSizeMismatchException: " + ex;

                                Logger.Debug(exceptionString);
                                Logger.FirebaseDebug(exceptionString);
                            }
                        }
                    }
                    else if (task.Exception.InnerException is DownloadDirectoryNotSetException
                             || task.Exception?.InnerException?.InnerException is DownloadDirectoryNotSetException)
                    {
                        action = () =>
                        {
                            var messageString =
                                SeekerState.ActiveActivityRef.GetString(Resource.String
                                    .FailedDownloadDirectoryNotSet);

                            MainActivity.ToastUiWithDebouncer(messageString, "_17_");
                        };
                    }
                    else if
                        (task.Exception
                             .InnerException is Soulseek.TransferRejectedException
                         tre) //derived class of TransferException...
                    {
                        // we go here when trying to download a locked file...
                        // (the exception only gets thrown on rejected with "not shared")
                        bool isFileNotShared = tre.Message.ToLower().Contains("file not shared");

                        // if we request a file from a soulseek NS client such as eÌe.jpg which when encoded
                        // in UTF fails to be decoded by Latin1
                        // soulseek NS will send TransferRejectedException "File Not Shared."
                        // with our filename (the filename will be identical).
                        // when we retry lets try a Latin1 encoding.  If no special characters this will not make
                        // any difference and it will be just a normal retry.
                        // we only want to try this once. and if it fails reset
                        // it to normal and do not try it again.
                        // if we encode the same way we decode, then such a thing will not occur.

                        // in the nicotine 3.1.1 and earlier, if we request a file such as
                        // "fÃ¶r", nicotine will encode it in Latin1.  We will
                        // decode it as UTF8, encode it back as UTF8 and then they will decode it as UTF-8
                        // resulting in för".  So even though we encoded and decoded
                        // in the same way there can still be an issue.  If we force legacy it will be fixed.


                        // always set this since it only shows if we DO NOT retry
                        if (isFileNotShared)
                        {
                            action = () =>
                            {
                                var messageString =
                                    SeekerState.ActiveActivityRef.GetString(Resource.String
                                        .transfer_rejected_file_not_shared);

                                MainActivity.ToastUiWithDebouncer(messageString, "_2_");
                            }; // needed
                        }
                        else
                        {
                            action = () =>
                            {
                                var messageString =
                                    SeekerState.ActiveActivityRef.GetString(Resource.String.transfer_rejected);
                                MainActivity.ToastUiWithDebouncer(messageString, "_2_");
                            }; // needed
                        }

                        Logger.Debug("rejected. is not shared: " + isFileNotShared);
                    }
                    else if (task.Exception.InnerException is Soulseek.TransferException)
                    {
                        action = () =>
                        {
                            MainActivity.ToastUiWithDebouncer(
                                string.Format(SeekerState.ActiveActivityRef
                                        .GetString(Resource.String.failed_to_establish_connection_to_peer),
                                    e.dlInfo.username),
                                "_1_",
                                e?.dlInfo?.username ?? string.Empty
                            );
                        };
                    }
                    else if (task.Exception.InnerException is Soulseek.UserOfflineException)
                    {
                        action = () =>
                        {
                            MainActivity.ToastUiWithDebouncer(
                                task.Exception.InnerException.Message,
                                "_3_",
                                e?.dlInfo?.username ?? string.Empty
                            );
                        }; // needed. "User x appears to be offline"
                    }
                    else if (task.Exception.InnerException is Soulseek.SoulseekClientException
                             && task.Exception.InnerException.Message != null
                             && task.Exception.InnerException.Message.ToLower()
                                 .Contains(Soulseek.SoulseekClient.FailedToEstablishDirectOrIndirectStringLower))
                    {
                        Logger.Debug("Task Exception: " + task.Exception.InnerException.Message);
                        action = () =>
                        {
                            MainActivity.ToastUiWithDebouncer(
                                SeekerState.ActiveActivityRef
                                    .GetString(Resource.String.failed_to_establish_direct_or_indirect),
                                "_4_"
                            );
                        };
                    }
                    else if (task.Exception.InnerException.Message != null
                             && task.Exception.InnerException.Message.ToLower()
                                 .Contains("read error: remote connection closed"))
                    {
                        retriable = true;
                        Logger.Debug("Unhandled task exception: " + task.Exception.InnerException.Message);
                        action = () =>
                        {
                            SeekerState.ActiveActivityRef.ShowLongToast(ResourceConstant.String.remote_conn_closed);
                        };

                        if (NetworkHandoffDetector.HasHandoffOccuredRecently())
                        {
                            resetRetryCount = true;
                        }
                    }
                    else if (task.Exception.InnerException.Message != null
                             && task.Exception.InnerException.Message.ToLower()
                                 .Contains("network subsystem is down"))
                    {
                        // if we have internet again by the time we get here then its retriable.
                        // this is often due to handoff. handoff either causes this or "remote connection closed"
                        if (ConnectionReceiver.DoWeHaveInternet())
                        {
                            Logger.Debug("we do have internet");
                            action = () =>
                            {
                                SeekerState.ActiveActivityRef.ShowLongToast(ResourceConstant.String.remote_conn_closed);
                            };

                            retriable = true;
                            if (NetworkHandoffDetector.HasHandoffOccuredRecently())
                            {
                                resetRetryCount = true;
                            }
                        }
                        else
                        {
                            action = () =>
                            {
                                SeekerState.ActiveActivityRef.ShowLongToast(ResourceConstant.String.network_down);
                            };
                        }

                        Logger.Debug("Unhandled task exception: " + task.Exception.InnerException.Message);

                    }
                    else if (task.Exception.InnerException.Message != null && task.Exception.InnerException.Message
                                 .ToLower().Contains("reported as failed by"))
                    {
                        // if we request a file from a soulseek NS client such as eÌÌÌe.jpg which when
                        // encoded in UTF fails to be decoded by Latin1
                        // soulseek NS will send UploadFailed with our filename (the filename will be identical).
                        // when we retry lets try a Latin1 encoding.  If no special characters this will not make
                        // any difference and it will be just a normal retry.
                        // we only want to try this once. and if it fails reset it to normal and
                        // do not try it again.

                        retriable = true;
                        Logger.Debug("Unhandled task exception: " + task.Exception.InnerException.Message);
                        action = () =>
                        {
                            SeekerState.ActiveActivityRef.ShowLongToast(ResourceConstant.String.reported_as_failed);
                        };
                    }
                    else if (task.Exception.InnerException.Message != null
                             && task.Exception.InnerException.Message.ToLower()
                                 .Contains(Soulseek.SoulseekClient.FailedToEstablishDirectOrIndirectStringLower))
                    {
                        Logger.Debug("Unhandled task exception: " + task.Exception.InnerException.Message);
                        action = () =>
                        {
                            MainActivity.ToastUiWithDebouncer(
                                SeekerState.ActiveActivityRef
                                    .GetString(Resource.String.failed_to_establish_direct_or_indirect),
                                "_5_"
                            );
                        };
                    }
                    else
                    {
                        retriable = true;

                        // the server connection task.Exception.InnerException.Message.Contains("The server connection was closed unexpectedly")
                        // this seems to be retry able
                        // or task.Exception.InnerException.InnerException.Message.Contains("The server connection was closed unexpectedly""
                        // or task.Exception.InnerException.Message.Contains("Transfer failed: Read error: Object reference not set to an instance of an object

                        bool unknownException = true;
                        if (task.Exception != null && task.Exception.InnerException != null)
                        {
                            // I get a lot of null refs from task.Exception.InnerException.Message
                            Logger.Debug("Unhandled task exception: " + task.Exception.InnerException.Message);

                            // is thrown by Stream.Close()
                            if (task.Exception.InnerException.Message.StartsWith("Disk full."))
                            {
                                action = () =>
                                {
                                    SeekerState.ActiveActivityRef.ShowLongToast(ResourceConstant.String.error_no_space);
                                };
                                unknownException = false;
                            }

                            if (task.Exception.InnerException.InnerException != null && unknownException)
                            {
                                if (task.Exception.InnerException.InnerException.Message
                                        .Contains("ENOSPC (No space left on device)")
                                    || task.Exception.InnerException.InnerException.Message
                                        .Contains("Read error: Disk full."))
                                {
                                    action = () =>
                                    {
                                        SeekerState.ActiveActivityRef
                                            .ShowLongToast(ResourceConstant.String.error_no_space);
                                    };
                                    unknownException = false;
                                }

                                // 1.983 - Non-fatal Exception: java.lang.Throwable: InnerInnerException: Transfer failed: Read error: Object reference not set to an instance of an object  at Soulseek.SoulseekClient.DownloadToStreamAsync (System.String username, System.String filename, System.IO.Stream outputStream, System.Nullable`1[T] size, System.Int64 startOffset, System.Int32 token, Soulseek.TransferOptions options, System.Threading.CancellationToken cancellationToken) [0x00cc2] in <bda1848b50e64cd7b441e1edf9da2d38>:0 
                                if (task.Exception.InnerException.InnerException.Message
                                    .Contains(SoulseekClient.FailedToEstablishDirectOrIndirectStringLower, 
                                        StringComparison.CurrentCultureIgnoreCase))
                                {
                                    unknownException = false;
                                }

                                if (unknownException)
                                {
                                    Logger.FirebaseDebug("InnerInnerException: "
                                                         + task.Exception.InnerException.InnerException.Message
                                                         + task.Exception.InnerException
                                                             .InnerException.StackTrace);
                                }

                                // this is to help with the collection was modified
                                if (task.Exception.InnerException.InnerException.InnerException != null
                                    && unknownException)
                                {
                                    Logger.FirebaseInfo("InnerInnerException: "
                                                        + task.Exception.InnerException
                                                            .InnerException.Message
                                                        + task.Exception.InnerException
                                                            .InnerException.StackTrace);

                                    var innerInner = task.Exception.InnerException.InnerException.InnerException;

                                    //1.983 - Non-fatal Exception: java.lang.Throwable: InnerInnerException: Transfer failed: Read error: Object reference not set to an instance of an object  at Soulseek.SoulseekClient.DownloadToStreamAsync (System.String username, System.String filename, System.IO.Stream outputStream, System.Nullable`1[T] size, System.Int64 startOffset, System.Int32 token, Soulseek.TransferOptions options, System.Threading.CancellationToken cancellationToken) [0x00cc2] in <bda1848b50e64cd7b441e1edf9da2d38>:0 
                                    Logger.FirebaseDebug("Innerx3_Exception: " + innerInner.Message
                                                                               + innerInner.StackTrace);
                                }
                            }

                            if (unknownException)
                            {
                                if (task.Exception.InnerException.StackTrace
                                    .Contains("System.Xml.Serialization.XmlSerializationWriterInterpreter"))
                                {
                                    if (task.Exception.InnerException.StackTrace.Length > 1201)
                                    {
                                        Logger.FirebaseDebug("xml Unhandled task exception 2nd part: "
                                                             + task.Exception.InnerException.StackTrace
                                                                 .Skip(1000).ToString());
                                    }

                                    Logger.FirebaseDebug("xml Unhandled task exception: "
                                                         + task.Exception.InnerException.Message
                                                         + task.Exception.InnerException.StackTrace);
                                }
                                else
                                {
                                    Logger.FirebaseDebug("dlcontaction Unhandled task exception: "
                                                         + task.Exception.InnerException.Message
                                                         + task.Exception.InnerException.StackTrace);
                                }
                            }
                        }
                        else if (task.Exception != null && unknownException)
                        {
                            Logger.FirebaseDebug("Unhandled task exception (little info): "
                                                 + task.Exception.Message);

                            Logger.Debug("Unhandled task exception (little info):" + task.Exception.Message);
                        }
                    }


                    if (forceRetry
                        || ((resetRetryCount || e.dlInfo.RetryCount == 0)
                            && (SeekerState.AutoRetryDownload)
                            && retriable))
                    {
                        Logger.Debug("!! retry the download " + e.dlInfo.fullFilename);
                        try
                        {
                            // retry download.
                            var cancellationTokenSource = new CancellationTokenSource();

                            // else when you go to cancel you are cancelling an already cancelled useless token!!
                            TransfersFragment.SetupCancellationToken(
                                e.dlInfo.TransferItemReference,
                                cancellationTokenSource,
                                out _);

                            var retryTask = TransfersUtil.DownloadFileAsync(
                                e.dlInfo.username,
                                e.dlInfo.fullFilename,
                                e.dlInfo.Size,
                                cancellationTokenSource,
                                out _,
                                1,
                                e.dlInfo.TransferItemReference.ShouldEncodeFileLatin1(),
                                e.dlInfo.TransferItemReference.ShouldEncodeFolderLatin1());

                            retryTask.ContinueWith(DownloadContinuationActionUi(
                                new DownloadAddedEventArgs(
                                    new DownloadInfo(
                                        e.dlInfo.username,
                                        e.dlInfo.fullFilename,
                                        e.dlInfo.Size,
                                        retryTask,
                                        cancellationTokenSource,
                                        e.dlInfo.QueueLength,
                                        resetRetryCount ? 0 : 1, task.Exception,
                                        e.dlInfo.Depth
                                    )
                                )
                            ), cancellationTokenSource.Token);

                            return; // i.e. don't toast anything just retry.
                        }
                        catch (System.Exception e)
                        {
                            // if this happens at least log the normal message....
                            Logger.FirebaseDebug("retry creation failed: " + e.Message + e.StackTrace);
                        }

                    }

                    if (e.dlInfo.RetryCount == 1 && e.dlInfo.PreviousFailureException != null)
                    {
                        Logger.FirebaseDebug("auto retry failed: prev exception: "
                                             + e.dlInfo.PreviousFailureException.InnerException?.Message?.ToString()
                                             + "new exception: "
                                             + task.Exception?.InnerException?.Message?.ToString());
                    }

                    action ??= () =>
                    {
                        SeekerState.ActiveActivityRef.ShowLongToast(ResourceConstant.String.error_unspecified);
                    };

                    SeekerState.ActiveActivityRef.RunOnUiThread(action);
                    return;
                }

                // failed downloads return before getting here...

                if (e.dlInfo.RetryCount == 1 && e.dlInfo.PreviousFailureException != null)
                {
                    Logger.FirebaseDebug("auto retry succeeded: prev exception: "
                                         + e.dlInfo.PreviousFailureException.InnerException?.Message);
                }

                if (!SeekerState.DisableDownloadToastNotification)
                {
                    action = () =>
                    {
                        var message = SeekerApplication.GetString(ResourceConstant.String.FinishedDownloading);
                        var filename = CommonHelpers.GetFileNameFromFile(e.dlInfo.fullFilename);
                        SeekerState.ActiveActivityRef.ShowLongToast($"{filename} {message}");
                    };

                    SeekerState.ActiveActivityRef.RunOnUiThread(action);
                }

                var finalUri = string.Empty;
                if (task is Task<byte[]> tbyte)
                {
                    var noSubfolder = e.dlInfo.TransferItemReference.TransferItemExtra.HasFlag(TransferItemExtras.NoSubfolder);

                    var path = StorageUtils.SaveToFile(
                        e.dlInfo.fullFilename,
                        e.dlInfo.username,
                        tbyte.Result,
                        null,
                        null,
                        true,
                        e.dlInfo.Depth,
                        noSubfolder,
                        out finalUri,
                        // TODO: Do not use static references of Android Context entities
                        SeekerState.ActiveActivityRef);

                    StorageUtils.SaveFileToMediaStore(path);
                }
                else if (task is Task<Tuple<string, string>> tString)
                {
                    // move file...
                    var noSubfolder = e.dlInfo.TransferItemReference.TransferItemExtra.HasFlag(TransferItemExtras.NoSubfolder);

                    var path = StorageUtils.SaveToFile(
                        e.dlInfo.fullFilename,
                        e.dlInfo.username,
                        null,
                        Android.Net.Uri.Parse(tString.Result.Item1),
                        Android.Net.Uri.Parse(tString.Result.Item2),
                        false,
                        e.dlInfo.Depth,
                        noSubfolder,
                        out finalUri);

                    StorageUtils.SaveFileToMediaStore(path);
                }
                else
                {
                    Logger.FirebaseDebug("Very bad. Task is not the right type.....");
                }

                e.dlInfo.TransferItemReference.FinalUri = finalUri;
            }
            finally
            {
                e.dlInfo.TransferItemReference.InProcessing = false;
            }
        };

        return continuationActionSaveFile;
    }
}
