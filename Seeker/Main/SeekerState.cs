﻿using Android.Content;
using Android.OS;
using AndroidX.AppCompat.App;
using AndroidX.Fragment.App;
using Common;
using Soulseek;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using _Microsoft.Android.Resource.Designer;
using Seeker.Helpers;
using Seeker.Main;
using Seeker.Managers;
using Seeker.Models;

namespace Seeker
{
    public static class SeekerState
    {
        static SeekerState()
        {
            downloadInfoList = new List<DownloadInfo>();
        }

        // Misc
        public static bool InDarkModeCache = false;
        public static bool currentlyLoggedIn = false;
        public static bool logoutClicked = false; // TODO hack


        // Settings
        public static PersistentValue<bool> AutoClearCompleteDownloads;
        public static bool AutoClearCompleteUploads = false;

        public static bool NotifyOnFolderCompleted = true;

        public static PersistentValue<bool> FreeUploadSlotsOnly;
        public static bool DisableDownloadToastNotification = true;
        public const bool AutoRetryDownload = true;

        public static PersistentValue<bool> HideLockedResultsInSearch;
        public static PersistentValue<bool> HideLockedResultsInBrowse;

        public static bool TransferViewShowSizes = false;
        public static bool TransferViewShowSpeed = false;

        public static PersistentValue<bool> FileBackedDownloads;
        
        // this is for downloads that fail with the condition "User is Offline".
        // this will also autodownload when we first log in as well.
        public static bool AutoRetryBackOnline = true;

        public static bool AutoRequeueDownloadsAtStartup = true;

        public static PersistentValue<int> NumberSearchResults;
        public static int DayNightMode = AppCompatDelegate.ModeNightFollowSystem;
        public static PersistentValue<bool> RememberSearchHistory;
        public static SoulseekClient SoulseekClient = null;
        public static String Username = null;
        public static String Password = null;
        public static bool SharingOn = false;
        public static bool AllowPrivateRoomInvitations = false;
        public static PersistentValue<bool> StartServiceOnStartup;
        public static bool IsStartUpServiceCurrentlyRunning = false;
        public static PersistentValue<bool> NoSubfolderForSingle;

        public static bool AllowUploadsOnMetered = true;
        public static bool CurrentConnectionIsUnmetered = true;
        public static bool IsNetworkPermitting()
        {
            return AllowUploadsOnMetered || CurrentConnectionIsUnmetered;
        }

        public static SearchResultSorting DefaultSearchResultSortAlgorithm = SearchResultSorting.Available;

        public static String SaveDataDirectoryUri = null;
        public static bool SaveDataDirectoryUriIsFromTree = true;
        
        // Consts
        public static string Language = FieldLangAuto;
        public const string FieldLangAuto = "Auto";
        
        public static String ManualIncompleteDataDirectoryUri = null;
        public static bool ManualIncompleteDataDirectoryUriIsFromTree = true;

        public static bool SpeedLimitDownloadOn = false;
        public static bool SpeedLimitUploadOn = false;
        public static int SpeedLimitDownloadBytesSec = 4 * 1024 * 1024;//1048576;
        public static int SpeedLimitUploadBytesSec = 4 * 1024 * 1024;
        public static bool SpeedLimitDownloadIsPerTransfer = true;
        public static bool SpeedLimitUploadIsPerTransfer = true;

        public static PersistentValue<bool> ShowSmartFilters;
        public static SmartFilterState SmartFilterOptions;

        public static volatile bool DownloadKeepAliveServiceRunning = false;
        public static volatile bool UploadKeepAliveServiceRunning = false;

        public static TimeSpan OffsetFromUtcCached = TimeSpan.Zero;
        
        public static SlskHelp.SharedFileCache SharedFileCache = null;
        public static int UploadSpeed = -1; //bytes
        public static bool FailedShareParse = false;
        private static volatile bool isParsing = false;

        public static bool NumberOfSharedDirectoriesIsStale = true;
        public static bool AttemptedToSetUpSharing = false;

        public static bool OurCurrentStatusIsAway = false; //bool because it can only be online or away. we set this after we successfully change the status.
        //NOTE: 
        //If we end the connection abruptly (i.e. airplane mode, kill app, turn phone off) then our status will not be changed to offline. (at least after waiting for 20 mins, not sure when it would have)
        //  only if we close the tcp connection properly (FIN, ACK) (i.e. menu > Shut Down) does the server update our status properly to offline.
        //The server does not remember your old status.  So if you log in again after setting your status to away, then your status will be online.  You must set it to away again if desired.
        //There is some weirdness where we only get "GetStatus" (7) messages when we go from online to away.  Otherwise, we dont get anything.  So its not reliable for determining what our status is.
        public enum PendingStatusChange
        {
            NothingPending = 0,
            AwayPending = 1,
            OnlinePending = 2,
        }
        public static PendingStatusChange PendingStatusChangeToAwayOnline = PendingStatusChange.NothingPending;


        public static List<UserListItem> IgnoreUserList = new List<UserListItem>();
        public static RecentUserManager RecentUsersManager = null;
        public static System.Collections.Concurrent.ConcurrentDictionary<string, string> UserNotes = null;
        /// <summary>
        /// There is no concurrent hashset so concurrent dictionary is used. the value is pointless so its only 1 byte.
        /// </summary>
        public static System.Collections.Concurrent.ConcurrentDictionary<string, byte> UserOnlineAlerts = null;
        public static bool ShowRecentUsers = true;

        public static string UserInfoBio = string.Empty;
        public static string UserInfoPictureName = string.Empty; //filename only. The picture will be in (internal storage) FilesDir/user_info_pic/filename.

        public static bool ListenerEnabled = true;
        public static volatile int ListenerPort = 33939;
        public static bool ListenerUPnpEnabled = true;

        public static PersistentValue<bool> CreateCompleteAndIncompleteFolders;
        public static PersistentValue<bool> CreateUsernameSubfolders;
        public static PersistentValue<bool> OverrideDefaultIncompleteLocations;

        public static bool PerformDeepMetadataSearch = true;

        public static bool AutoAwayOnInactivity = false;

        public static EventHandler<EventArgs> DirectoryUpdatedEvent;
        public static EventHandler<EventArgs> SharingStatusChangedEvent;

        /// <summary>
        /// This is only for showing toasts.  The logic is as follows.  If we showed a cancelled toast 
        /// notification <1000ms ago then dont keep showing them. if >1s ago then its okay to show.
        /// They all come in super fast
        /// </summary>
        public static long TaskWasCancelledToastDebouncer = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();

        /// <summary>
        /// This is for when the cancelAndClear button was last pressed.  It is because of the massive amount of cancellation
        /// events all occuring on different threads that all go to affect the service.
        /// </summary>
        public static long CancelAndClearAllWasPressedDebouncer = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();
        public static long AbortAllWasPressedDebouncer = DateTimeOffset.MinValue.ToUnixTimeMilliseconds();

        public static void Initialize(Context context)
        {
            CreateCompleteAndIncompleteFolders = new PersistentValue<bool>(context,
                ResourceConstant.String.key_create_complete_and_incomplete_folders, true);
            CreateUsernameSubfolders = new PersistentValue<bool>(context,
                ResourceConstant.String.key_create_username_subfolders, false);
            NoSubfolderForSingle = new PersistentValue<bool>(context,
                ResourceConstant.String.key_create_subfolders_for_single_downloads, false);
            OverrideDefaultIncompleteLocations = new PersistentValue<bool>(context,
                ResourceConstant.String.key_use_manual_incomplete_directory_uri, false);
            FileBackedDownloads = new PersistentValue<bool>(context,
                ResourceConstant.String.key_file_backed_downloads, true);
            ShowSmartFilters = new PersistentValue<bool>(context,
                ResourceConstant.String.key_show_smart_filters, true);
            NumberSearchResults = new PersistentValue<int>(context,
                ResourceConstant.String.key_max_search_results, MainActivity.DEFAULT_SEARCH_RESULTS);
            FreeUploadSlotsOnly = new PersistentValue<bool>(context,
                ResourceConstant.String.key_free_upload_slots_only, true);
            HideLockedResultsInSearch = new PersistentValue<bool>(context,
                ResourceConstant.String.key_hide_locked_in_search, true);
            HideLockedResultsInBrowse = new PersistentValue<bool>(context,
                ResourceConstant.String.key_hide_locked_in_browse, true);
            RememberSearchHistory = new PersistentValue<bool>(context,
                ResourceConstant.String.key_remember_search_history, true);
            StartServiceOnStartup = new PersistentValue<bool>(context,
                ResourceConstant.String.key_start_seeker_service_on_startup, true);
            AutoClearCompleteDownloads = new PersistentValue<bool>(context,
                ResourceConstant.String.key_auto_clear_complete_downloads, false);
        }
        
        // TODOORG seperateclass models
        public struct SmartFilterState
        {
            public bool KeywordsEnabled;
            public int KeywordsOrder;
            public bool NumFilesEnabled;
            public int NumFilesOrder;
            public bool FileTypesEnabled;
            public int FileTypesOrder;
            public List<ChipType> GetEnabledOrder()
            {
                List<Tuple<ChipType, int>> tuples = new List<Tuple<ChipType, int>>();
                if (KeywordsEnabled)
                {
                    tuples.Add(new Tuple<ChipType, int>(ChipType.Keyword, KeywordsOrder));
                }
                if (NumFilesEnabled)
                {
                    tuples.Add(new Tuple<ChipType, int>(ChipType.FileCount, NumFilesOrder));
                }
                if (FileTypesEnabled)
                {
                    tuples.Add(new Tuple<ChipType, int>(ChipType.FileType, FileTypesOrder));
                }
                tuples.Sort((t1, t2) => t1.Item2.CompareTo(t2.Item2));
                return tuples.Select(t1 => t1.Item1).ToList();
            }

            public List<ConfigureChipItems> GetAdapterItems()
            {
                List<Tuple<string, int, bool>> tuples = new List<Tuple<string, int, bool>>();
                tuples.Add(new Tuple<string, int, bool>(GetNameFromEnum(ChipType.Keyword), KeywordsOrder, KeywordsEnabled));
                tuples.Add(new Tuple<string, int, bool>(GetNameFromEnum(ChipType.FileCount), NumFilesOrder, NumFilesEnabled));
                tuples.Add(new Tuple<string, int, bool>(GetNameFromEnum(ChipType.FileType), FileTypesOrder, FileTypesEnabled));
                tuples.Sort((t1, t2) => t1.Item2.CompareTo(t2.Item2));
                return tuples.Select(t1 => new ConfigureChipItems() { Name = t1.Item1, Enabled = t1.Item3 }).ToList();
            }

            public void FromAdapterItems(List<ConfigureChipItems> chipItems)
            {
                for (int i = 0; i < chipItems.Count; i++)
                {
                    ChipType ct = GetEnumFromName(chipItems[i].Name);
                    bool enabled = chipItems[i].Enabled;
                    switch (ct)
                    {
                        case ChipType.Keyword:
                            SeekerState.SmartFilterOptions.KeywordsEnabled = enabled;
                            SeekerState.SmartFilterOptions.KeywordsOrder = i;
                            break;
                        case ChipType.FileType:
                            SeekerState.SmartFilterOptions.FileTypesEnabled = enabled;
                            SeekerState.SmartFilterOptions.FileTypesOrder = i;
                            break;
                        case ChipType.FileCount:
                            SeekerState.SmartFilterOptions.NumFilesEnabled = enabled;
                            SeekerState.SmartFilterOptions.NumFilesOrder = i;
                            break;
                        default:
                            throw new Exception("unknown option");
                    }
                }
            }

            public const string DisplayNameKeyword = "Keywords";
            public const string DisplayNameType = "File Types";
            public const string DisplayNameCount = "# Files";

            public string GetNameFromEnum(ChipType chipType)
            {
                switch (chipType)
                {
                    case ChipType.Keyword:
                        return DisplayNameKeyword;
                    case ChipType.FileType:
                        return DisplayNameType;
                    case ChipType.FileCount:
                        return DisplayNameCount;
                    default:
                        throw new Exception("unknown enum");
                }
            }

            public ChipType GetEnumFromName(string name)
            {
                switch (name)
                {
                    case DisplayNameKeyword:
                        return ChipType.Keyword;
                    case DisplayNameType:
                        return ChipType.FileType;
                    case DisplayNameCount:
                        return ChipType.FileCount;
                    default:
                        throw new Exception("unknown enum");
                }
            }
        }


        public static void ClearSearchHistoryEventsFromTarget(object target)
        {
            if (ClearSearchHistory == null)
            {
                return;
            }
            else
            {
                foreach (Delegate d in ClearSearchHistory.GetInvocationList())
                {
                    if (d.Target.GetType() == target.GetType())
                    {
                        ClearSearchHistory -= (EventHandler<EventArgs>)d;
                    }
                }
            }
        }

        public static void ClearSearchHistoryInvoke()
        {
            ClearSearchHistory?.Invoke(null, null);
        }

        public static event EventHandler<EventArgs> ClearSearchHistory;
        public static List<DownloadInfo> downloadInfoList;
        /// <summary>
        /// Context of last created activity
        /// </summary>
        public static volatile FragmentActivity ActiveActivityRef = null;
        public static ISharedPreferences SharedPreferences;
        public static volatile MainActivity MainActivityRef;

        public static bool IsParsing
        {
            get
            {
                return isParsing;
            }
            set
            {
                isParsing = value;
                NumberParsed = 0; //reset
            }
        }
        
        public static int NumberParsed = 0;

        // TODO utils
        public static bool RequiresEitherOpenDocumentTreeOrManageAllFiles()
        {
            //29 does has the requestExternalStorage workaround.
            return Android.OS.Build.VERSION.SdkInt >= BuildVersionCodes.R;
        }

        public static bool UseLegacyStorage()
        {
            return Android.OS.Build.VERSION.SdkInt < BuildVersionCodes.Q;
        }

        public static bool PreOpenDocumentTree()
        {
            return Android.OS.Build.VERSION.SdkInt < BuildVersionCodes.Lollipop;
        }

        public static bool PreMoveDocument()
        {
            return Android.OS.Build.VERSION.SdkInt < BuildVersionCodes.N;
        }

        public static bool IsLowDpi()
        {
            return Android.Content.Res.Resources.System.DisplayMetrics.WidthPixels < 768;
        }
        // TODO utils



        // previously this was on the loginfragment but
        // it would get recreated every time so there were lost instances with threads waiting forever....
        // TODO hack?
        public static ManualResetEvent ManualResetEvent = new ManualResetEvent(false); 
        
        public static AndroidX.DocumentFile.Provider.DocumentFile DiagnosticTextFile = null;
        public static System.IO.StreamWriter DiagnosticStreamWriter = null;

        public static event EventHandler<BrowseResponseEvent> BrowseResponseReceived;
        public static AndroidX.DocumentFile.Provider.DocumentFile RootDocumentFile = null;
        public static AndroidX.DocumentFile.Provider.DocumentFile RootIncompleteDocumentFile = null; //only gets set if can write the dir...
        
        public static void OnBrowseResponseReceived(BrowseResponse origBR, TreeNode<Directory> rootTree, string fromUsername, string startingLocation)
        {
            BrowseResponseReceived(null, new BrowseResponseEvent(origBR, rootTree, fromUsername, startingLocation));
        }
        
        // TODO: Move this into ConnectionManager
        public static bool CurrentlyLoggedInButDisconnectedState()
        {
            return currentlyLoggedIn && 
                   (SoulseekClient.State.HasFlag(SoulseekClientStates.Disconnected) 
                    || SoulseekClient.State.HasFlag(SoulseekClientStates.Disconnecting));
        }
        
        public static void SaveSmartFilterState()
        {
            lock (SeekerApplication.SharedPrefLock)
            {
                SharedPreferences.Edit()!
                    .PutBoolean(SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.key_smart_filters_keywords_enabled), SmartFilterOptions.KeywordsEnabled)!
                    .PutBoolean(SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.key_smart_filters_file_types_enabled), SmartFilterOptions.FileTypesEnabled)!
                    .PutBoolean(SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.key_smart_filters_file_count_enabled), SmartFilterOptions.NumFilesEnabled)!
                    .PutInt(SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.key_smart_filters_keywords_order), SmartFilterOptions.KeywordsOrder)!
                    .PutInt(SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.key_smart_filters_file_types_order), SmartFilterOptions.FileTypesOrder)!
                    .PutInt(SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.key_smart_filters_file_count_order), SmartFilterOptions.NumFilesOrder)!
                    .Commit();
            }
        }
        
        public static void RestoreSmartFilterState(ISharedPreferences sharedPreferences)
        {
            SmartFilterOptions = new SmartFilterState
            {
                KeywordsEnabled = sharedPreferences.GetBoolean(SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.key_smart_filters_keywords_enabled), true),
                FileTypesEnabled = sharedPreferences.GetBoolean(SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.key_smart_filters_file_types_enabled), true),
                NumFilesEnabled = sharedPreferences.GetBoolean(SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.key_smart_filters_file_count_enabled), true),
                KeywordsOrder = sharedPreferences.GetInt(SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.key_smart_filters_keywords_order), 0),
                FileTypesOrder = sharedPreferences.GetInt(SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.key_smart_filters_file_types_order), 1),
                NumFilesOrder = sharedPreferences.GetInt(SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.key_smart_filters_file_count_order), 2)
            };
        }
    }
}
