/*
 * Copyright 2021 Seeker
 *
 * This file is part of Seeker
 *
 * Seeker is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * Seeker is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with Seeker. If not, see <http://www.gnu.org/licenses/>.
 */

using Seeker.Chatroom;
using Seeker.Helpers;
using Seeker.Managers;
using Seeker.Messages;
using Seeker.Search;
using Seeker.UPnP;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Core.App;
using AndroidX.DocumentFile.Provider;
using Soulseek;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using _Microsoft.Android.Resource.Designer;
using AndroidX.Core.Net;
using Seeker.Main;
using Seeker.Models;
using Seeker.Utils;
using SlskHelp;
using static Android.Net.ConnectivityManager;

namespace Seeker
{
    [Application]
    // ReSharper disable once ClassNeverInstantiated.Global - used in manifest
    public class SeekerApplication(IntPtr javaReference, JniHandleOwnership transfer)
        : Application(javaReference, transfer)
    {
        public static readonly object SharedPrefLock = new();
        public const string ACTION_SHUTDOWN = "SeekerApplication_AppShutDown";
        public new static Application ApplicationContext;
        public static readonly List<WeakReference<ThemeableActivity>> Activities = [];
        
        private const bool AUTO_CONNECT_ON = true;

        public override void OnCreate()
        {
            base.OnCreate();
            ApplicationContext = this;
            
#if !IzzySoft
            var app = Firebase.FirebaseApp.InitializeApp(this);
            if (app == null)
            {
                Logger.CrashlyticsEnabled = false;
            }
#endif
            PrivilegesManager.Initialize(this);
            
            RegisterActivityLifecycleCallbacks(new ForegroundLifecycleTracker());
            
            // TODO: This call is only reachable on Android 21
            RegisterReceiver(new ConnectionReceiver(), new IntentFilter(ConnectivityAction));
            
            SeekerState.SharedPreferences = GetSharedPreferences("SoulSeekPrefs", 0);;
            
            SharedPreferencesUtils.RestoreSeekerState();
            SharedPreferencesUtils.RestoreListeningState();
            UPnpManager.RestoreUpnpState();

            SeekerState.OffsetFromUtcCached = CommonHelpers.GetDateTimeNowSafe().Subtract(DateTime.UtcNow);
            
            // TODO: This call is only reachable on Android 21
            SeekerState.SystemLanguage = Resources!.Configuration!.Locale!.ToVariantAwareString();
            
            LanguageUtils.SetLanguageBasedOnPlatformSupport(this);

            // though setting it to -1 does not seem to recreate the activity or have any negative side effects...
            // this does not restart Android.App.Application. so putting it here is a much better place...
            // in MainActivity.OnCreate it would restart the activity every time.
            if (AppCompatDelegate.DefaultNightMode != SeekerState.DayNightMode)
            {
                AppCompatDelegate.DefaultNightMode = SeekerState.DayNightMode;
            }
            
            SeekerKeepAliveService.CpuKeepAlive_FullService ??=
                ((PowerManager)GetSystemService(PowerService))!.NewWakeLock(WakeLockFlags.Partial,
                    "Seeker Keep Alive Service Cpu");

            SeekerKeepAliveService.WifiKeepAlive_FullService ??= 
                ((Android.Net.Wifi.WifiManager)GetSystemService(WifiService))!
                .CreateWifiLock(WifiMode.FullHighPerf, "Seeker Keep Alive Service Wifi");

            SetNetworkState(this);

            if (SeekerState.SoulseekClient == null)
            {
                // need search response and enqueue download action...
                var options = new SoulseekClientOptions(
                    minimumDiagnosticLevel: DiagnosticFile.Enabled
                        ? Soulseek.Diagnostics.DiagnosticLevel.Debug
                        : Soulseek.Diagnostics.DiagnosticLevel.Info,
                    messageTimeout: 30000,
                    enableListener: SeekerState.ListenerEnabled,
                    autoAcknowledgePrivateMessages: false,
                    acceptPrivateRoomInvitations: SeekerState.AllowPrivateRoomInvitations,
                    listenPort: SeekerState.ListenerPort,
                    userInfoResponseResolver: UserInfoResponseHandler
                );
                SeekerState.SoulseekClient = new SoulseekClient(options);
                DiagnosticFile.UpdateDiagnosticState();
                
                SeekerState.SoulseekClient.UserDataReceived += SoulseekClient_UserDataReceived;
                SeekerState.SoulseekClient.UserStatusChanged += SoulseekClient_UserStatusChanged_Deduplicator;
                UserStatusChangedDeDuplicated += SoulseekClient_UserStatusChanged;
                SeekerState.SoulseekClient.TransferStateChanged += Upload_TransferStateChanged;

                SeekerState.SoulseekClient.TransferProgressUpdated += SoulseekClient_TransferProgressUpdated;
                SeekerState.SoulseekClient.TransferStateChanged += SoulseekClient_TransferStateChanged;

                SeekerState.SoulseekClient.Connected += SoulseekClient_Connected;
                SeekerState.SoulseekClient.StateChanged += SoulseekClient_StateChanged;
                SeekerState.SoulseekClient.LoggedIn += SoulseekClient_LoggedIn;
                SeekerState.SoulseekClient.Disconnected += SoulseekClient_Disconnected;
                SeekerState.SoulseekClient.ServerInfoReceived += SoulseekClient_ServerInfoReceived;
                SeekerState.BrowseResponseReceived += BrowseFragment.SeekerState_BrowseResponseReceived;

                SeekerState.SoulseekClient.PrivilegedUserListReceived += SoulseekClient_PrivilegedUserListReceived;
                SeekerState.SoulseekClient.ExcludedSearchPhrasesReceived += 
                    SoulseekClient_ExcludedSearchPhrasesReceived;

                MessageController.Initialize();
                ChatroomController.Initialize();

                SoulseekClient.OnTransferSizeMismatchFunc = OnTransferSizeMismatchFunc;
                SoulseekClient.ErrorLogHandler += MainActivity.SoulseekClient_ErrorLogHandler;

                SoulseekClient.DebugLogHandler += MainActivity.DebugLogHandler;

                SoulseekClient.DownloadAddedRemovedInternal += SoulseekClient_DownloadAddedRemovedInternal;
                SoulseekClient.UploadAddedRemovedInternal += SoulseekClient_UploadAddedRemovedInternal;
            }

            UPnpManager.Context = this;
            UPnpManager.Instance.SearchAndSetMappingIfRequired();
            SlskHelp.CommonHelpers.STRINGS_KBS = Resources.GetString(ResourceConstant.String.kilobytes_per_second);
            SlskHelp.CommonHelpers.STRINGS_KHZ = Resources.GetString(ResourceConstant.String.kilohertz);

            SlskHelp.CommonHelpers.UserListChecker = new UserListManager.UserListChecker();
        }

        private static void SoulseekClient_ExcludedSearchPhrasesReceived(object sender, 
            IReadOnlyCollection<string> excludedPhrasesList)
        {
            SearchUtil.ExcludedSearchPhrases = excludedPhrasesList;
        }
        
        /// <returns>true if changed</returns>
        public static bool SetNetworkState(Context context)
        {
            try
            {
                var cm = (ConnectivityManager)context.GetSystemService(ConnectivityService);

                if (cm?.ActiveNetworkInfo is not { IsConnected: true })
                {
                    return false;
                }
                
                var oldState = SeekerState.CurrentConnectionIsUnmetered;
                SeekerState.CurrentConnectionIsUnmetered = !ConnectivityManagerCompat.IsActiveNetworkMetered(cm);
                Logger.Debug("SetNetworkState is metered " + !SeekerState.CurrentConnectionIsUnmetered);
                    
                return oldState != SeekerState.CurrentConnectionIsUnmetered;
            }
            catch (Exception e)
            {
                Logger.FirebaseDebug("SetNetworkState" + e.Message + e.StackTrace);
                return false;
            }
        }

        /// <summary>
        /// This is the one we should be hooking up to.
        /// This is due to the fact that the server sends us the same user update multiple times if they are of multiple interests.
        /// i.e. if we have Added Them, we are in Chatroom A, B, and C with them, then we get 4 status updates.
        /// </summary>
        public static EventHandler<UserStatusChangedEventArgs> UserStatusChangedDeDuplicated;

        private void SoulseekClient_Disconnected(object sender, SoulseekClientDisconnectedEventArgs e)
        {
            Logger.Debug("disconnected");
            lock (SoulseekConnection.OurCurrentLoginTaskSyncObject)
            {
                SoulseekConnection.OurCurrentLoginTask = null;
            }
        }

        public static bool TransfersDownloadsCompleteStale = false; //whether a dl completes since we have last saved transfers to disk.
        public static DateTime TransfersLastSavedTime = DateTime.MinValue; //whether a dl completes since we have last saved transfers to disk.


        public static volatile int UPLOAD_COUNT = -1; // a hack see below

        private void SoulseekClient_UploadAddedRemovedInternal(object sender, SoulseekClient.TransferAddedRemovedInternalEventArgs e)
        {
            var abortAll = 
                DateTimeOffset.Now.ToUnixTimeMilliseconds() - SeekerState.AbortAllWasPressedDebouncer < 750;
            
            if (e.Count == 0 || abortAll)
            {
                UPLOAD_COUNT = -1;
                var uploadServiceIntent = new Intent(this, typeof(UploadForegroundService));
                Logger.Debug("Stop Service");
                StopService(uploadServiceIntent);
                SeekerState.UploadKeepAliveServiceRunning = false;
            }
            else switch (SeekerState.UploadKeepAliveServiceRunning)
            {
                case false:
                {
                    UPLOAD_COUNT = e.Count;
                    var uploadServiceIntent = new Intent(this, typeof(UploadForegroundService));
                    if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                    {
                        var isForeground = SeekerState.ActiveActivityRef?.IsResumed();
                        if (isForeground ?? false)
                        {
                            StartService(uploadServiceIntent); // this will throw if the app is in background.
                        }
                    }
                    else
                    {
                        // even when targetting and compiling for api 31, old devices can still do this just fine.
                        // this will throw if the app is in background.
                        StartService(uploadServiceIntent);
                    }
                    SeekerState.UploadKeepAliveServiceRunning = true;
                    break;
                }
                case true when e.Count != 0:
                {
                    UPLOAD_COUNT = e.Count;
                    
                    //for two downloads, this notification will go up before the service is started...
                    var rawMessageString = e.Count == 1
                        ? UploadForegroundService.SingularUploadRemaining
                        : UploadForegroundService.PluralUploadsRemaining;
                    var message = string.Format(rawMessageString, e.Count);
                    var notification = UploadForegroundService.CreateNotification(this, message);
                    var manager = GetSystemService(NotificationService) as NotificationManager;
                    manager?.Notify(UploadForegroundService.NOTIF_ID, notification);
                    break;
                }
            }
        }


        public static volatile int DL_COUNT = -1; // a hack see below

        //it works in the case of successfully finished, cancellation token used, etc.
        private void SoulseekClient_DownloadAddedRemovedInternal(object sender, SoulseekClient.TransferAddedRemovedInternalEventArgs e)
        {
            //even with them all going onto same thread here you will still have (ex) a 16 count coming in after a 0 count sometimes.
            Logger.Debug("SoulseekClient_DownloadAddedRemovedInternal with count:" + e.Count);
            Logger.Debug("the thread is: " + Thread.CurrentThread.ManagedThreadId);

            var cancelAndClear = DateTimeOffset.Now.ToUnixTimeMilliseconds() - SeekerState.CancelAndClearAllWasPressedDebouncer < 750;
            Logger.Debug("SoulseekClient_DownloadAddedRemovedInternal cancel and clear:" + cancelAndClear);
            if (e.Count == 0 || cancelAndClear)
            {
                DL_COUNT = -1;
                Intent downloadServiceIntent = new Intent(this, typeof(DownloadForegroundService));
                Logger.Debug("Stop Service");
                StopService(downloadServiceIntent);
                SeekerState.DownloadKeepAliveServiceRunning = false;
            }
            else if (!SeekerState.DownloadKeepAliveServiceRunning)
            {
                DL_COUNT = e.Count;
                var downloadServiceIntent = new Intent(this, typeof(DownloadForegroundService));
                if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                {
                    bool? isForeground = SeekerState.ActiveActivityRef?.IsResumed();

                    if (isForeground ?? false)
                    {
                        StartService(downloadServiceIntent); //this will throw if the app is in background.
                    }
                }
                else
                {
                    // even when targetting and compiling for api 31, old devices can still do this just fine.
                    StartService(downloadServiceIntent); //this will throw if the app is in background.
                }
                SeekerState.DownloadKeepAliveServiceRunning = true;
            }
            else if (SeekerState.DownloadKeepAliveServiceRunning && e.Count != 0)
            {
                DL_COUNT = e.Count;
                //for two downloads, this notification will go up before the service is started...

                //requires run on ui thread? NOPE
                string msg = string.Empty;
                if (e.Count == 1)
                {
                    msg = string.Format(DownloadForegroundService.SingularDownloadRemaining, e.Count);
                }
                else
                {
                    msg = string.Format(DownloadForegroundService.PluralDownloadsRemaining, e.Count);
                }
                var notif = DownloadForegroundService.CreateNotification(this, msg);
                var manager = GetSystemService(NotificationService) as NotificationManager;
                manager.Notify(DownloadForegroundService.NOTIF_ID, notif);
            }
        }

        public static EventHandler<TransferItem> StateChangedForItem;
        public static EventHandler<ProgressUpdatedUIEventArgs> ProgressUpdated;

        private void SoulseekClient_TransferStateChanged(object sender, TransferStateChangedEventArgs e)
        {
            KeepAlive.RestartInactivityKillTimer();
            
            var isUpload = e.Transfer.Direction == TransferDirection.Upload;
            if (!isUpload && e.Transfer.State.HasFlag(TransferStates.UserOffline))
            {
                // user offline.
                TransfersFragment.AddToUserOffline(e.Transfer.Username);
            }

            var relevantItem = TransfersFragment.TransferItemManagerWrapped.GetTransferItemWithIndexFromAll(
                e.Transfer?.Filename, e.Transfer?.Username, isUpload, out _);
            
            if (relevantItem == null)
            {
                Logger.FirebaseInfo("relevantItem==null. state: " + e.Transfer?.State);
            }
            
            if (relevantItem != null)
            {
                // if the incoming transfer is not cancelled,
                // i.e. requested, then we replace the state (the user retried).
                if (e.Transfer.State.HasFlag(TransferStates.Cancelled) && relevantItem.State.HasFlag(TransferStates.FallenFromQueue))
                {
                    Logger.Debug("fallen from queue");
                    
                    // the state is good as is.  do not add cancelled to it,
                    // since we used cancelled to mean "user cancelled" i.e. paused.
                    relevantItem.Failed = true;
                    relevantItem.Progress = 100;
                }
                else
                {
                    relevantItem.State = e.Transfer.State;
                }
                
                relevantItem.IncompleteParentUri = e.IncompleteParentUri;
                if (!relevantItem.State.HasFlag(TransferStates.Requested))
                {
                    relevantItem.InProcessing = true;
                }
                
                if (relevantItem.State.HasFlag(TransferStates.Succeeded))
                {
                    relevantItem.IncompleteParentUri = null; // not needed anymore.
                }
            }
            
            // TODO: Continue formatting from around here
            
            if (e.Transfer!.State.HasFlag(TransferStates.Errored) 
                || e.Transfer.State.HasFlag(TransferStates.TimedOut)
                || e.Transfer.State.HasFlag(TransferStates.Rejected))
            {
                SpeedLimitHelper.RemoveDownloadUser(e.Transfer.Username);
                if (relevantItem == null)
                {
                    return;
                }
                
                relevantItem.Failed = true;
                StateChangedForItem?.Invoke(null, relevantItem);
            }
            else if (e.Transfer.State.HasFlag(TransferStates.Queued))
            {
                if (relevantItem == null)
                {
                    return;
                }
                
                if (!relevantItem.IsUpload())
                {
                    // TODO: why is queue length max value
                    if (relevantItem.QueueLength != 0)
                    {
                        // this means that it probably came from a search response where we know the users queuelength
                        // ***BUT THAT IS NEVER THE ACTUAL QUEUE LENGTH*** its always much shorter...
                        DownloadQueue.GetDownloadPlaceInQueue(e.Transfer.Username, e.Transfer.Filename, true, true, relevantItem, null);
                    }
                    else
                    {
                        // this means that it came from a browse response
                        // where we may not know the users initial queue length...
                        // or if its unexpectedly queued.
                        DownloadQueue.GetDownloadPlaceInQueue(e.Transfer.Username, e.Transfer.Filename, true, true, relevantItem, null);
                    }
                }
                
                StateChangedForItem?.Invoke(null, relevantItem);
            }
            else if (e.Transfer.State.HasFlag(TransferStates.Initializing))
            {
                if (relevantItem == null)
                {
                    return;
                }
                
                // clear queued flag...
                relevantItem.QueueLength = int.MaxValue;
                StateChangedForItem?.Invoke(null, relevantItem);
            }
            else if (e.Transfer.State.HasFlag(TransferStates.Completed))
            {
                SpeedLimitHelper.RemoveDownloadUser(e.Transfer.Username);
                if (relevantItem == null)
                {
                    return;
                }
                if (!e.Transfer.State.HasFlag(TransferStates.Cancelled))
                {
                    // clear queued flag...
                    TransfersDownloadsCompleteStale = true;
                    TransfersFragment.SaveTransferItems(SeekerState.SharedPreferences, false, 120);
                    relevantItem.Progress = 100;
                    StateChangedForItem?.Invoke(null, relevantItem);
                }
                else //if it does have state cancelled we still want to update UI! (assuming we arent also clearing it)
                {
                    if (!relevantItem.CancelAndClearFlag)
                    {
                        StateChangedForItem?.Invoke(null, relevantItem);
                    }
                }

                if (e.Transfer.State.HasFlag(TransferStates.Succeeded))
                {
                    if (SeekerState.NotifyOnFolderCompleted && !isUpload)
                    {
                        if (TransfersFragment.TransferItemManagerDL.IsFolderNowComplete(relevantItem, false))
                        {
                            //relevantItem.TransferItemExtra // if single then change the notif text.
                            // RetryDL is on completed Succeeded dl?
                            ShowNotificationForCompletedFolder(relevantItem.FolderName, relevantItem.Username);
                        }
                    }
                }
            }
            else
            {
                if (relevantItem == null && (e.Transfer.State == TransferStates.Requested || e.Transfer.State == TransferStates.Aborted))
                {
                    return; //TODO sometimes this can happen too fast.  this is okay thouugh bc it will soon go to another state.
                }
                if (relevantItem == null && e.Transfer.State == TransferStates.InProgress)
                {
                    //THIS SHOULD NOT HAPPEN now that the race condition is resolved....
                    Logger.FirebaseDebug("relevantItem==null. state: " + e.Transfer.State.ToString());
                    return;
                }
                StateChangedForItem?.Invoke(null, relevantItem);
            }
        }

        public static bool OnTransferSizeMismatchFunc(System.IO.Stream fileStream, string fullFilename, string username, long startOffset, long oldSize, long newSize, string incompleteUriString, out System.IO.Stream newStream)
        {
            newStream = null;
            try
            {
                var relevantItem = TransfersFragment.TransferItemManagerWrapped.GetTransferItemWithIndexFromAll(fullFilename, username, false, out _);
                if (startOffset == 0)
                {
                    // all we need to do is update the size.
                    relevantItem.Size = newSize;
                    Logger.Debug("updated the size");
                }
                else
                {
                    // we need to truncate the incomplete file and set our progress back to 0.
                    relevantItem.Size = newSize;

                    fileStream.Close();
                    var useDownloadDir = SeekerState.CreateCompleteAndIncompleteFolders 
                                         && !SettingsActivity.UseIncompleteManualFolder();

                    var incompleteUri = Android.Net.Uri.Parse(incompleteUriString);

                    // this is the only time we do legacy.
                    var isLegacyCase = SeekerState.UseLegacyStorage() 
                                       && (SeekerState.RootDocumentFile == null && useDownloadDir);
                    if (isLegacyCase)
                    {
                        newStream = new System.IO.FileStream(incompleteUri!.Path!, 
                            System.IO.FileMode.Truncate, System.IO.FileAccess.Write, System.IO.FileShare.None);
                    }
                    else
                    {

                        newStream = SeekerState.MainActivityRef.ContentResolver!
                            .OpenOutputStream(incompleteUri!, "wt");
                    }

                    relevantItem.Progress = 0;
                    Logger.Debug("truncated the file and updated the size");
                }

            }
            catch (Exception exception)
            {
                Logger.Debug("OnTransferSizeMismatchFunc: " + exception);
                return false;
            }
            return true;
        }
        
        private void SoulseekClient_TransferProgressUpdated(object sender, TransferProgressUpdatedEventArgs e)
        {
            // It's possible to get a nullref here IF the system orientation changes.
            // throttle this maybe...
            KeepAlive.RestartInactivityKillTimer();
            
            TransferItem relevantItem;
            if (TransfersFragment.TransferItemManagerDL == null)
            {
                Logger.Debug("transferItems Null " + e.Transfer.Filename);
                return;
            }

            var isUpload = e.Transfer.Direction == TransferDirection.Upload;
            relevantItem = TransfersFragment.TransferItemManagerWrapped
                .GetTransferItemWithIndexFromAll(
                    e.Transfer.Filename,
                    e.Transfer.Username, 
                    e.Transfer.Direction == TransferDirection.Upload,
                    out _
                );

            if (relevantItem == null)
            {
                //this happens on Clear and Cancel All.
                Logger.Debug("Relevant Item Null " + e.Transfer.Filename);
                Logger.Debug("transferItems.IsEmpty " + TransfersFragment.TransferItemManagerDL.IsEmpty());
                return;
            }

            var fullRefresh = false;
            var percentComplete = e.Transfer.PercentComplete;
            relevantItem.Progress = (int)percentComplete;
            relevantItem.RemainingTime = e.Transfer.RemainingTime;
            relevantItem.AvgSpeed = e.Transfer.AverageSpeed;

            // int indexRemoved = -1;
            // if 100% complete and autoclear
            // todo: autoclear on upload
            if (((SeekerState.AutoClearCompleteDownloads && !isUpload) 
                 || (SeekerState.AutoClearCompleteUploads && isUpload)) 
                && Math.Abs(percentComplete - 100) < .001) 
            {

                var action = () =>
                {
                    TransfersFragment.UpdateBatchSelectedItemsIfApplicable(relevantItem);
                    // TODO: shouldn't we do the corresponding Adapter.NotifyRemoveAt.
                    //       this one doesnt need cleaning up, its successful..
                    TransfersFragment.TransferItemManagerWrapped.Remove(relevantItem);
                };
                
                if (SeekerState.ActiveActivityRef != null)
                {
                    SeekerState.ActiveActivityRef?.RunOnUiThread(action);
                }

                fullRefresh = true;
            }
            else if (Math.Abs(percentComplete - 100) < .001)
            {
                fullRefresh = true;
            }

            var wasFailed = false;
            if (percentComplete != 0)
            {
                if (relevantItem.Failed)
                {
                    wasFailed = true;
                    relevantItem.Failed = false;
                }
            }

            var args = new ProgressUpdatedUIEventArgs(relevantItem, wasFailed, fullRefresh,
                percentComplete, e.Transfer.AverageSpeed);
            ProgressUpdated?.Invoke(null, args);
        }

        public static string GetVersionString()
        {
            try
            {
                var pInfo = SeekerState.ActiveActivityRef.PackageManager!
                    .GetPackageInfo(SeekerState.ActiveActivityRef.PackageName!, 0);
                return pInfo!.VersionName;
            }
            catch (Exception e)
            {
                Logger.FirebaseDebug("GetVersionString: " + e.Message);
                return string.Empty;
            }
        }
        
        public static Task<UserInfo> UserInfoResponseHandler(string uname, IPEndPoint ipEndPoint)
        {
            if (IsUserInIgnoreList(uname))
            {
                return Task.FromResult(new UserInfo(
                    string.Empty, 
                    0, 
                    0, 
                    false));
            }
            
            var bio = SeekerState.UserInfoBio ?? string.Empty;
            var picture = GetUserInfoPicture();
            var uploadSlots = 1;
            var queueLength = 0;
            
            // in my experience even if someone is sharing nothing they say 1 upload slot and yes free slots..
            // but I don't know maybe 0 and no makes more sense??
            if (SeekerState.SharingOn)
            {
                return Task.FromResult(new UserInfo(bio, picture, uploadSlots, queueLength, true));
            }

            uploadSlots = 0;
            queueLength = 0;

            return Task.FromResult(new UserInfo(bio, picture, uploadSlots, queueLength, true));
        }

        private static byte[] GetUserInfoPicture()
        {
            if (string.IsNullOrEmpty(SeekerState.UserInfoPictureName))
            {
                return null;
            }
            
            var userInfoPicDir = new Java.IO.File(ApplicationContext.FilesDir, EditUserInfoActivity.USER_INFO_PIC_DIR);
            if (!userInfoPicDir.Exists())
            {
                Logger.FirebaseDebug("!userInfoPicDir.Exists()");
                return null;
            }

            var userInfoPic = new Java.IO.File(userInfoPicDir, SeekerState.UserInfoPictureName);
            if (!userInfoPic.Exists())
            {
                // I could imagine a race condition causing this...
                Logger.FirebaseDebug("!userInfoPic.Exists()");
                return null;
            }
            
            var documentFile = DocumentFile.FromFile(userInfoPic);
            var imageStream = ApplicationContext.ContentResolver!.OpenInputStream(documentFile.Uri);
            var picFile = new byte[imageStream!.Length];
            imageStream.Read(picFile, 0, (int)imageStream.Length);
            
            return picFile;
        }

        private void SoulseekClient_PrivilegedUserListReceived(object sender, IReadOnlyCollection<string> e)
        {
            PrivilegesManager.Instance.SetPrivilegedList(e);
        }

        private void SoulseekClient_ServerInfoReceived(object sender, ServerInfo e)
        {
            if (e.WishlistInterval.HasValue)
            {
                WishlistController.SearchIntervalMilliseconds = e.WishlistInterval.Value;
                WishlistController.Initialize();
            }
            else
            {
                Logger.Debug("wishlist interval is null");
            }
        }

        // I only care about the Connected, LoggedIn to Disconnecting case.  
        // the next case is Disconnecting to Disconnected.

        // then for failed retries you get
        // disconnected to connecting
        // connecting to disconnecting
        // disconnecting to disconnected.
        // so be wary of the disconnected event...

        private void SoulseekClient_StateChanged(object sender, SoulseekClientStateChangedEventArgs e)
        {
            Logger.Debug("Prev: " + e.PreviousState + " Next: " + e.State);
            if (e.PreviousState.HasFlag(SoulseekClientStates.LoggedIn)
                && e.State.HasFlag(SoulseekClientStates.Disconnecting))
            {
                Logger.Debug("!! changing from connected to disconnecting");
                
                switch (e.Exception)
                {
                    case KickedFromServerException:
                        Logger.Debug("Kicked Kicked Kicked");
                        this.ShowLongToast(ResourceConstant.String.kicked_due_to_other_client);
                        return; // DO NOT RETRY!!! or will do an infinite loop!
                    case ObjectDisposedException:
                        return; // DO NOT RETRY!!! we are shutting down
                }

                // this is a "true" connected to disconnected
                ChatroomController.ClearAndCacheJoined();
                Logger.Debug("disconnected " + DateTime.UtcNow);
                if (SeekerState.logoutClicked)
                {
                    SeekerState.logoutClicked = false;
                }
                else if (AUTO_CONNECT_ON && SeekerState.currentlyLoggedIn)
                {
                    var reconnectRetryThread = new Thread(ReconnectSteppedBackOffThreadTask);
                    reconnectRetryThread.Start();
                }
            }
            else if (e.PreviousState.HasFlag(SoulseekClientStates.Disconnected))
            {
                Logger.Debug("!! changing from disconnected to trying to connect");
            }
            else if (e.State.HasFlag(SoulseekClientStates.LoggedIn) && e.State.HasFlag(SoulseekClientStates.Connected))
            {
                Logger.Debug("!! changing trying to connect to successfully connected");
            }
        }

        private void SoulseekClient_LoggedIn(object sender, EventArgs e)
        {
            lock (SoulseekConnection.OurCurrentLoginTaskSyncObject)
            {
                SoulseekConnection.OurCurrentLoginTask = null;
            }
            
            ChatroomController.JoinAutoJoinRoomsAndPreviousJoined();
            Logger.Debug("logged in " + DateTime.UtcNow);
            Logger.Debug("Listening State: " + SeekerState.SoulseekClient.GetListeningState());

            if (!SeekerState.ListenerEnabled || SeekerState.SoulseekClient.GetListeningState())
            {
                return;
            }
            
            if (SeekerState.ActiveActivityRef == null)
            {
                Logger.FirebaseDebug("SeekerState.ActiveActivityRef null SoulseekClient_LoggedIn");
            }
            else
            {
                this.ShowShortToast(ResourceConstant.String.port_already_in_use);
            }
        }

        private void SoulseekClient_Connected(object sender, EventArgs e)
        {
            Logger.Debug("connected " + DateTime.UtcNow);
        }

        // TODO: Remove this with other static references to context
        public static void ShowToast(string msg, ToastLength toastLength)
        {
            if (SeekerState.ActiveActivityRef == null)
            {
                Logger.Debug("cant show toast, active activity ref is null");
            }
            else
            {
                SeekerState.ActiveActivityRef.RunOnUiThread(() => { Toast.MakeText(SeekerState.ActiveActivityRef, msg, toastLength).Show(); });
            }
        }

        public static bool ShouldWeTryToConnect()
        {
            if (!SeekerState.currentlyLoggedIn)
            {
                // we logged out on purpose
                return false;
            }

            if (SeekerState.SoulseekClient == null)
            {
                // too early
                return false;
            }

            return !SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.Connected) 
                   || !SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.LoggedIn);
        }

        /// <summary>Tis is the number of seconds after the last try.</summary>
        private static readonly int[] retrySeconds = [1, 2, 4, 10, 20];
        private const int MAX_TRIES = 5;
        
        // if the reconnect stepped backoff thread is in progress but a change occurred that makes us
        // want to trigger it immediately, then we can just set this event.
        public static AutoResetEvent ReconnectAutoResetEvent = new(false);
        public static volatile bool ReconnectSteppedBackOffThreadIsRunning;
        
        public static void ReconnectSteppedBackOffThreadTask()
        {
            try
            {
                ReconnectSteppedBackOffThreadIsRunning = true;
                for (int i = 0; i < MAX_TRIES; i++)
                {
                    if (!ShouldWeTryToConnect())
                    {
                        return; //our work here is done
                    }

                    bool isDueToAutoReset = ReconnectAutoResetEvent.WaitOne(retrySeconds[i] * 1000);
                    if (isDueToAutoReset)
                    {
                        Logger.Debug("is woken due to auto reset");
                    }

                    try
                    {
                        // a general note for connecting:
                        // whenever you reconnect if you want the server to tell you
                        // the status of users on your user list
                        // you have to re-AddUser them.  This is what SoulSeekQt does
                        // (wireshark message code 5 for each user in list).
                        // and what Nicotine does (userlist.server_login()).
                        // and reconnecting means every single time, including toggling from wifi to data / vice versa.
                        var task = SoulseekConnection
                            .ConnectAndPerformPostConnectTasks(SeekerState.Username, SeekerState.Password);
                        task.Wait();
                        if (task.IsCompletedSuccessfully)
                        {
                            Logger.Debug("RETRY " + i + "SUCCEEDED");
                            return; // our work here is done
                        }
                    }
                    catch (Exception)
                    {
                        // Intentional no-op
                    }

                    // if we got here we failed.. so try again shortly...
                    Logger.Debug("RETRY " + i + "FAILED");
                }
            }
            finally
            {
                ReconnectSteppedBackOffThreadIsRunning = false;
            }
        }

        public static void AddToIgnoreListFeedback(Context context, string username)
        {
            var stringId = AddToIgnoreList(username)
                ? ResourceConstant.String.added_to_ignore
                : ResourceConstant.String.already_added_to_ignore;
            context.ShowShortToast(string.Format(context.GetString(stringId), username));
        }
        
        public static Android.Graphics.Drawables.Drawable? GetDrawableFromAttribute(Context context, int attr)
        {
            var typedValue = new TypedValue();
            context.Theme?.ResolveAttribute(attr, typedValue, true);
            var drawableRes = (typedValue.ResourceId != 0) ? typedValue.ResourceId : typedValue.Data;
            return (int)Build.VERSION.SdkInt >= 21 
                ? context.Resources?.GetDrawable(drawableRes, SeekerState.ActiveActivityRef.Theme) 
                : context.Resources?.GetDrawable(drawableRes);
        }

        public static void RecreateActivities()
        {
            foreach (var weakRef in Activities)
            {
                if (weakRef.TryGetTarget(out var themeableActivity))
                {
                    themeableActivity.Recreate();
                }
            }
        }

        public static void SetActivityTheme(Activity activity)
        {
            // useless returns the same thing every time
            activity.SetTheme(activity.Resources!.Configuration!.UiMode.HasFlag(Android.Content.Res.UiMode.NightYes)
                ? ThemeHelper.ToNightThemeProper(SeekerState.NightModeVarient)
                : ThemeHelper.ToDayThemeProper(SeekerState.DayModeVarient));
        }
        
        /// <summary>Add To User List and save user list to shared prefs.  false if already added</summary>
        public static bool AddToIgnoreList(string username)
        {
            // typically its tough to add a user to ignore list from the UI if they are in the User List.
            // but for example if you ignore a user based on their message.
            // User List and Ignore List and mutually exclusive so if you ignore someone,
            // they will be removed from user list.
            if (UserListManager.UserListContainsUser(username))
            {
                UserListManager.UserListRemoveUser(username);
            }

            lock (SeekerState.IgnoreUserList)
            {
                if (SeekerState.IgnoreUserList.Exists(userListItem => userListItem.Username == username))
                {
                    return false;
                }

                SeekerState.IgnoreUserList.Add(new UserListItem(username, UserRole.Ignored));
            }
            
            lock (SharedPrefLock)
            {
                SeekerState.SharedPreferences.Edit()!
                    .PutString(KeyConsts.M_IgnoreUserList, UserListManager.AsString())!
                    .Commit();
            }
            
            return true;
        }

        public static void RemoveFromIgnoreListFeedback(Context context, string username)
        {
            if (RemoveFromIgnoreList(username))
            {
                var rawString = context.GetString(ResourceConstant.String.removed_user_from_ignored_list);
                context.ShowShortToast(string.Format(rawString, username));
            }
        }

        /// <summary>Remove From User List and save user list to shared prefs.  false if not found</summary>
        public static bool RemoveFromIgnoreList(string username)
        {
            lock (SeekerState.IgnoreUserList)
            {
                if (!SeekerState.IgnoreUserList.Exists(userListItem => userListItem.Username == username))
                {
                    return false;
                }

                SeekerState.IgnoreUserList =
                    SeekerState.IgnoreUserList.Where(userListItem => userListItem.Username != username).ToList();
            }
            
            lock (SharedPrefLock)
            {
                SeekerState.SharedPreferences.Edit()!
                    .PutString(KeyConsts.M_IgnoreUserList, UserListManager.AsString())!
                    .Commit();
            }
            
            return true;
        }

        public static bool IsUserInIgnoreList(string username)
        {
            lock (SeekerState.IgnoreUserList)
            {
                return SeekerState.IgnoreUserList.Exists(userListItem => userListItem.Username == username);
            }
        }

        public static Dictionary<string, NotificationInfo> NotificationUploadTracker = new();

        /// <summary>
        /// this is for global uploading event handling only.
        /// TabPageAdapter is the one for downloading... and for upload TransferPage specific events
        /// </summary>
        private static void Upload_TransferStateChanged(object sender, TransferStateChangedEventArgs e)
        {
            if (e.Transfer == null || e.Transfer.Direction == TransferDirection.Download)
            {
                return;
            }
            if (e.Transfer.State == TransferStates.InProgress)
            {
                // uploading file to user...
                Logger.Debug("transfer state changed to in progress" + e.Transfer.Filename);
            }

            // todo rethink upload notifications....
            if (!e.Transfer.State.HasFlag(TransferStates.Succeeded))
            {
                return;
            }
            
            Logger.Debug("transfer state changed to completed" + e.Transfer.Filename);
                
            if (e.Transfer.AverageSpeed <= 0 || (int)e.Transfer.AverageSpeed == 0)
            {
                Logger.Debug("avg speed <= 0" + e.Transfer.Filename);
                return;
            }
            
            Logger.Debug("sending avg speed of " + e.Transfer.AverageSpeed);
            SeekerState.SoulseekClient.SendUploadSpeedAsync((int)e.Transfer.AverageSpeed);
            
            try
            {
                CommonHelpers.CreateNotificationChannel(
                    SeekerState.MainActivityRef, 
                    MainActivity.UPLOADS_CHANNEL_ID, 
                    MainActivity.UPLOADS_CHANNEL_NAME, 
                    // TODO: The call is only reachable on Android 21
                    NotificationImportance.High);
                    
                NotificationInfo notificationInfo;
                var directory = Common.Helpers.GetFolderNameFromFile(e.Transfer.Filename.Replace("/", @"\"));
                if (NotificationUploadTracker.TryGetValue(e.Transfer.Username, out var value))
                {
                    notificationInfo = value;
                    if (!notificationInfo.DirNames.Contains(directory))
                    {
                        notificationInfo.DirNames.Add(directory);
                    }
                        
                    notificationInfo.FilesUploadedToUser++;
                }
                else
                {
                    notificationInfo = new NotificationInfo(directory);
                    NotificationUploadTracker.Add(e.Transfer.Username, notificationInfo);
                }

                var notification = MainActivity.CreateUploadNotification(
                    SeekerState.MainActivityRef,
                    e.Transfer.Username,
                    notificationInfo.DirNames, 
                    notificationInfo.FilesUploadedToUser);
                    
                var manager = NotificationManagerCompat.From(SeekerState.MainActivityRef);
                manager.Notify(e.Transfer.Username.GetHashCode(), notification);
            }
            catch (Exception err)
            {
                Logger.FirebaseDebug("Upload notification failed" + err.Message + err.StackTrace);
            }
        }
        
        /// <summary>this is for getting additional information (status updates) from already added users</summary>
        private static bool UserListAddIfContainsUser(string username, UserData userData, UserStatus userStatus)
        {
            UserPresence? prevStatus = UserPresence.Offline;
            var found = false;
            lock (UserListManager.UserList)
            {
                foreach (var item in UserListManager.UserList.Where(item => item.Username == username))
                {
                    found = true;
                    if (userData != null)
                    {
                        item.UserData = userData;
                    }
                    
                    if (userStatus != null)
                    {
                        prevStatus = item.UserStatus?.Presence ?? UserPresence.Offline;
                        item.UserStatus = userStatus;
                    }
                    
                    break;
                }
            }
            
            // if user was previously offline and now they are not offline, then do the notification.
            // note - this method does not get called when first adding users. which I think is ideal for notifications.
            // if not in our user list, then this is likely a result of GetUserInfo!, so dont do any of this..
            if (found 
                && prevStatus.Value == UserPresence.Offline 
                && userStatus != null
                && userStatus.Presence != UserPresence.Offline)
            {
                Logger.Debug("from offline to online " + username);
                if (SeekerState.UserOnlineAlerts != null && SeekerState.UserOnlineAlerts.ContainsKey(username))
                {
                    // show notification.
                    ShowNotificationForUserOnlineAlert(username);
                }
            }
            else
            {
                Logger.Debug("NOT from offline to online (or not in user list)" + username);
            }
            
            return found;
        }

        public static View GetViewForSnackbar()
        {
            var useDownloadDialogFragment = false;
            View v = null;
            if (SeekerState.ActiveActivityRef is MainActivity mar)
            {
                var f = mar.SupportFragmentManager.FindFragmentByTag("tag_download_test");
                
                // this is the only one we have..
                // tho obv a more generic way would be to see if s/t is a dialog fragmnet.
                // but arent a lot of just simple alert dialogs etc dialog fragment??
                // maybe explicitly checking is the best way.
                if (f != null && f.IsVisible)
                {
                    useDownloadDialogFragment = true;
                    v = f.View;
                }
            }
            
            if (!useDownloadDialogFragment)
            {
                v = SeekerState.ActiveActivityRef.FindViewById<ViewGroup>(Android.Resource.Id.Content);
            }
            
            return v;
        }

        public const string CHANNEL_ID_USER_ONLINE = "User Online Alerts ID";
        public const string CHANNEL_NAME_USER_ONLINE = "User Online Alerts";
        public const string FromUserOnlineAlert = "FromUserOnlineAlert";
        public static void ShowNotificationForUserOnlineAlert(string username)
        {
            SeekerState.ActiveActivityRef.RunOnUiThread(() =>
            {
                try
                {
                    CommonHelpers.CreateNotificationChannel(SeekerState.ActiveActivityRef, CHANNEL_ID_USER_ONLINE, CHANNEL_NAME_USER_ONLINE, NotificationImportance.High); //only high will "peek"
                    Intent notifIntent = new Intent(SeekerState.ActiveActivityRef, typeof(UserListActivity));
                    notifIntent.AddFlags(ActivityFlags.SingleTop);
                    notifIntent.PutExtra(FromUserOnlineAlert, true);
                    PendingIntent pendingIntent =
                        PendingIntent.GetActivity(SeekerState.ActiveActivityRef, username.GetHashCode(), notifIntent, CommonHelpers.AppendMutabilityIfApplicable(PendingIntentFlags.UpdateCurrent, true));
                    Notification n = CommonHelpers.CreateNotification(SeekerState.ActiveActivityRef, pendingIntent, CHANNEL_ID_USER_ONLINE, "User Online", string.Format(SeekerState.ActiveActivityRef.Resources.GetString(Resource.String.user_X_is_now_online), username), false);
                    NotificationManagerCompat notificationManager = NotificationManagerCompat.From(SeekerState.ActiveActivityRef);
                    // notificationId is a unique int for each notification that you must define
                    notificationManager.Notify(username.GetHashCode(), n);
                }
                catch (System.Exception e)
                {
                    Logger.FirebaseDebug("ShowNotificationForUserOnlineAlert failed: " + e.Message + e.StackTrace);
                }
            });
        }


        public const string CHANNEL_ID_FOLDER_ALERT = "Folder Finished Downloading Alerts ID";
        public const string CHANNEL_NAME_FOLDER_ALERT = "Folder Finished Downloading Alerts";
        public const string FromFolderAlert = "FromFolderAlert";
        public const string FromFolderAlertUsername = "FromFolderAlertUsername";
        public const string FromFolderAlertFoldername = "FromFolderAlertFoldername";
        
        public static void ShowNotificationForCompletedFolder(string foldername, string username)
        {
            SeekerState.ActiveActivityRef.RunOnUiThread(() =>
            {
                try
                {
                    CommonHelpers.CreateNotificationChannel(SeekerState.ActiveActivityRef, CHANNEL_ID_FOLDER_ALERT, CHANNEL_NAME_FOLDER_ALERT, NotificationImportance.High); //only high will "peek"
                    Intent notifIntent = new Intent(SeekerState.ActiveActivityRef, typeof(MainActivity));
                    notifIntent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ReorderToFront); //otherwise if another activity is in front then this intent will do nothing...
                    notifIntent.PutExtra(FromFolderAlert, 2);
                    notifIntent.PutExtra(FromFolderAlertUsername, username);
                    notifIntent.PutExtra(FromFolderAlertFoldername, foldername);
                    PendingIntent pendingIntent =
                        PendingIntent.GetActivity(SeekerState.ActiveActivityRef, (foldername + username).GetHashCode(), notifIntent, CommonHelpers.AppendMutabilityIfApplicable(PendingIntentFlags.UpdateCurrent, true));
                    Notification n = CommonHelpers.CreateNotification(SeekerState.ActiveActivityRef, pendingIntent, CHANNEL_ID_FOLDER_ALERT, ApplicationContext.GetString(Resource.String.FolderFinishedDownloading), string.Format(SeekerState.ActiveActivityRef.Resources.GetString(Resource.String.folder_X_from_user_Y_finished), foldername, username), false);
                    NotificationManagerCompat notificationManager = NotificationManagerCompat.From(SeekerState.ActiveActivityRef);
                    // notificationId is a unique int for each notification that you must define
                    notificationManager.Notify((foldername + username).GetHashCode(), n);
                }
                catch (System.Exception e)
                {
                    Logger.FirebaseDebug("ShowNotificationForCompletedFolder failed: " + e.Message + e.StackTrace);
                }
            });
        }
        
        public static void SetupRecentUserAutoCompleteTextView(AutoCompleteTextView actv, bool forAddingUser = false)
        {
            if (SeekerState.ShowRecentUsers)
            {
                if (forAddingUser)
                {
                    //dont show people that we have already added...
                    var recents = SeekerState.RecentUsersManager.GetRecentUserList();
                    lock (UserListManager.UserList)
                    {
                        foreach (var uli in UserListManager.UserList)
                        {
                            recents.Remove(uli.Username);
                        }
                    }
                    actv.Adapter = new ArrayAdapter<string>(SeekerState.ActiveActivityRef, Resource.Layout.autoSuggestionRow, recents);
                }
                else
                {
                    actv.Adapter = new ArrayAdapter<string>(SeekerState.ActiveActivityRef, Resource.Layout.autoSuggestionRow, SeekerState.RecentUsersManager.GetRecentUserList());
                }
            }
        }

        public static void RestoreSmartFilterState(ISharedPreferences sharedPreferences)
        {
            SeekerState.SmartFilterOptions = new SeekerState.SmartFilterState();
            SeekerState.SmartFilterOptions.KeywordsEnabled = sharedPreferences.GetBoolean(KeyConsts.M_SmartFilter_KeywordsEnabled, true);
            SeekerState.SmartFilterOptions.KeywordsOrder = sharedPreferences.GetInt(KeyConsts.M_SmartFilter_KeywordsOrder, 0);
            SeekerState.SmartFilterOptions.FileTypesEnabled = sharedPreferences.GetBoolean(KeyConsts.M_SmartFilter_TypesEnabled, true);
            SeekerState.SmartFilterOptions.FileTypesOrder = sharedPreferences.GetInt(KeyConsts.M_SmartFilter_TypesOrder, 1);
            SeekerState.SmartFilterOptions.NumFilesEnabled = sharedPreferences.GetBoolean(KeyConsts.M_SmartFilter_CountsEnabled, true);
            SeekerState.SmartFilterOptions.NumFilesOrder = sharedPreferences.GetInt(KeyConsts.M_SmartFilter_CountsOrder, 2);
        }

        public static void SaveSmartFilterState()
        {
            lock (SharedPrefLock)
            {
                var editor = SeekerState.SharedPreferences.Edit();
                editor.PutBoolean(KeyConsts.M_SmartFilter_KeywordsEnabled, SeekerState.SmartFilterOptions.KeywordsEnabled);
                editor.PutBoolean(KeyConsts.M_SmartFilter_TypesEnabled, SeekerState.SmartFilterOptions.FileTypesEnabled);
                editor.PutBoolean(KeyConsts.M_SmartFilter_CountsEnabled, SeekerState.SmartFilterOptions.NumFilesEnabled);
                editor.PutInt(KeyConsts.M_SmartFilter_KeywordsOrder, SeekerState.SmartFilterOptions.KeywordsOrder);
                editor.PutInt(KeyConsts.M_SmartFilter_TypesOrder, SeekerState.SmartFilterOptions.FileTypesOrder);
                editor.PutInt(KeyConsts.M_SmartFilter_CountsOrder, SeekerState.SmartFilterOptions.NumFilesOrder);
                editor.Commit();
            }
        }

        public static void RestoreRecentUsersManagerFromString(string xmlRecentUsersList)
        {
            //if empty then this is the first time creating it.  initialize it with our list of added users.
            SeekerState.RecentUsersManager = new RecentUserManager();
            if (xmlRecentUsersList == string.Empty)
            {
                int count = UserListManager.UserList?.Count ?? 0;
                if (count > 0)
                {
                    SeekerState.RecentUsersManager.SetRecentUserList(UserListManager.UserList.Select(uli => uli.Username).ToList());
                }
                else
                {
                    SeekerState.RecentUsersManager.SetRecentUserList(new List<string>());
                }
            }
            else
            {
                List<string> recentUsers = new List<string>();
                using (var stream = new System.IO.StringReader(xmlRecentUsersList))
                {
                    var serializer = new System.Xml.Serialization.XmlSerializer(recentUsers.GetType()); //this happens too often not allowing new things to be properly stored..
                    SeekerState.RecentUsersManager.SetRecentUserList(serializer.Deserialize(stream) as List<string>);
                }
            }
        }

        public static void SaveRecentUsers()
        {
            string recentUsersStr;
            List<string> recentUsers = SeekerState.RecentUsersManager.GetRecentUserList();
            using (var writer = new System.IO.StringWriter())
            {
                var serializer = new System.Xml.Serialization.XmlSerializer(recentUsers.GetType());
                serializer.Serialize(writer, recentUsers);
                recentUsersStr = writer.ToString();
            }
            lock (SharedPrefLock)
            {
                var editor = SeekerState.SharedPreferences.Edit();
                editor.PutString(KeyConsts.M_RecentUsersList, recentUsersStr);
                editor.Commit();
            }
        }
        
        /// <summary>
        /// This is from the server after sending it a UserData request.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SoulseekClient_UserDataReceived(object sender, UserData e)
        {
            Logger.Debug("User Data Received: " + e.Username);
            if (e.Username == SeekerState.Username)
            {
                SeekerState.UploadSpeed = e.AverageSpeed; //bytes
            }
            else
            {
                if (UserListManager.UserList == null)
                {
                    Logger.FirebaseDebug("UserList is null on user data receive");
                }
                else
                {
                    UserListAddIfContainsUser(e.Username, e, null);
                }

                RequestedUserInfoHelper.AddIfRequestedUser(e.Username, e, null, null);
            }
        }

        private static string DeduplicateUsername;
        private static UserPresence DeduplicateStatus = UserPresence.Offline;
        private void SoulseekClient_UserStatusChanged_Deduplicator(object sender, UserStatusChangedEventArgs e)
        {

            if (DeduplicateUsername == e.Username && DeduplicateStatus == e.Status)
            {
                Logger.Debug($"throwing away {e.Username} status changed");
                return;
            }

            Logger.Debug($"handling {e.Username} status changed");
            DeduplicateUsername = e.Username;
            DeduplicateStatus = e.Status;
            UserStatusChangedDeDuplicated?.Invoke(sender, e);
        }


        public static EventHandler<string> UserStatusChangedUIEvent;
        private void SoulseekClient_UserStatusChanged(object sender, UserStatusChangedEventArgs e)
        {
            if (e.Username == SeekerState.Username)
            {
                return;
            }
            
            //we get user status changed for those we are in the same room as us
            if (UserListManager.UserList != null)
            {
                var status = new UserStatus(e.Status, e.IsPrivileged);
                if (UserListAddIfContainsUser(e.Username, null, status))
                {
                    Logger.Debug("friend status changed " + e.Username);
                    UserStatusChangedUIEvent?.Invoke(null, e.Username);
                }
            }

            SoulseekConnection.ProcessPotentialUserOfflineChangedEvent(e.Username, e.Status);

        }
    }
}
