﻿using _Microsoft.Android.Resource.Designer;
using Android.Content;
using AndroidX.AppCompat.App;
using Seeker.Chatroom;
using Seeker.Helpers;
using Seeker.Main;
using Seeker.Managers;
using Seeker.Settings;
using Soulseek;

namespace Seeker.Utils;

public static class SharedPreferencesUtils
{
    // the Bundle can be SLOWER than the SHARED PREFERENCES if SHARED PREFERENCES was saved in a different activity.
    // The best example being DAYNIGHTMODE
    public static void RestoreSeekerState(Context context)
    {
        // day night mode sets the static, saves to shared preferences the new value,
        // sets appcompat value, which recreates everything and calls restoreSeekerState(bundle)
        // where the bundle was older than shared prefs
        // because saveSeekerState was not called in the meantime...
        var prefs = SeekerState.SharedPreferences;
        if (prefs == null)
        {
            return;
        }
        
        SeekerState.currentlyLoggedIn = prefs.GetBoolean(KeyConsts.M_CurrentlyLoggedIn, false);
        SeekerState.Username = prefs.GetString(KeyConsts.M_Username, string.Empty);
        SeekerState.Password = prefs.GetString(KeyConsts.M_Password, string.Empty);
        SeekerState.SaveDataDirectoryUri = prefs.GetString(ResourceConstant.String.key_data_directory_uri, string.Empty);
        SeekerState.SaveDataDirectoryUriIsFromTree = prefs.GetBoolean(KeyConsts.M_SaveDataDirectoryUriIsFromTree, true);
        SeekerState.DayNightMode = ThemeUtils.NightModeOptionToInt(context, prefs.GetString(ResourceConstant.String.key_app_theme, context.GetString(ResourceConstant.String.key_app_theme_system)));
        SeekerState.Language = prefs.GetString(KeyConsts.M_Lanuage, SeekerState.FieldLangAuto);
        SeekerState.AutoClearCompleteUploads = prefs.GetBoolean(ResourceConstant.String.key_auto_clear_complete_uploads, false);
        SeekerState.ShowRecentUsers = prefs.GetBoolean(ResourceConstant.String.key_remember_recent_users, true);

        SeekerState.TransferViewShowSizes = prefs.GetBoolean(KeyConsts.M_TransfersShowSizes, true);
        SeekerState.TransferViewShowSpeed = prefs.GetBoolean(KeyConsts.M_TransfersShowSpeed, true);

        SeekerState.SpeedLimitUploadOn = prefs.GetBoolean(KeyConsts.M_UploadLimitEnabled, false);
        SeekerState.SpeedLimitDownloadOn = prefs.GetBoolean(KeyConsts.M_DownloadLimitEnabled, false);
        SeekerState.SpeedLimitUploadIsPerTransfer = prefs.GetBoolean(KeyConsts.M_UploadPerTransfer, true);
        SeekerState.SpeedLimitDownloadIsPerTransfer = prefs.GetBoolean(KeyConsts.M_DownloadPerTransfer, true);
        SeekerState.SpeedLimitUploadBytesSec = prefs.GetInt(KeyConsts.M_UploadSpeedLimitBytes, 4 * 1024 * 1024);
        SeekerState.SpeedLimitDownloadBytesSec = prefs.GetInt(KeyConsts.M_DownloadSpeedLimitBytes, 4 * 1024 * 1024);

        SeekerState.DisableDownloadToastNotification = !prefs.GetBoolean(ResourceConstant.String.key_notify_on_file_complete, false);
        SearchFragment.FilterSticky = prefs.GetBoolean(KeyConsts.M_FilterSticky, false);
        SearchFragment.FilterStickyString = prefs.GetString(KeyConsts.M_FilterStickyString, string.Empty);
        SearchFragment.SetSearchResultStyle(prefs.GetInt(KeyConsts.M_SearchResultStyle, 1));
        SeekerState.UploadSpeed = prefs.GetInt(KeyConsts.M_UploadSpeed, -1);

        UploadDirectoryManager.RestoreFromSavedState(prefs);

        SeekerState.SharingOn = prefs.GetBoolean(KeyConsts.M_SharingOn, false);
        UserListManager.UserList = UserListManager.FromString(prefs.GetString(KeyConsts.M_UserList, string.Empty));

        SeekerState.RecentUsersManager = RecentUserManager.FromXmlString(prefs.GetString(KeyConsts.M_RecentUsersList, string.Empty));
        SeekerState.IgnoreUserList = UserListManager.FromString(prefs.GetString(KeyConsts.M_IgnoreUserList, string.Empty));
        SeekerState.AllowPrivateRoomInvitations = prefs.GetBoolean(ResourceConstant.String.key_allow_private_room_invites, false);
        
        SeekerState.RestoreSmartFilterState(prefs);

        SeekerState.UserInfoBio = prefs.GetString(KeyConsts.M_UserInfoBio, string.Empty);
        SeekerState.UserInfoPictureName = prefs.GetString(KeyConsts.M_UserInfoPicture, string.Empty);

        SeekerState.UserNotes = SerializationHelper.RestoreUserNotesFromString(prefs.GetString(KeyConsts.M_UserNotes, string.Empty));
        SeekerState.UserOnlineAlerts = SerializationHelper.RestoreUserOnlineAlertsFromString(prefs.GetString(KeyConsts.M_UserOnlineAlerts, string.Empty));

        SeekerState.AutoAwayOnInactivity = prefs.GetBoolean(ResourceConstant.String.key_away_on_inactivity, false);
        SeekerState.AutoRetryBackOnline = prefs.GetBoolean(ResourceConstant.String.key_auto_retry_failed_downloads, true);

        SeekerState.NotifyOnFolderCompleted = prefs.GetBoolean(ResourceConstant.String.key_notify_on_folder_complete, true);
        SeekerState.AllowUploadsOnMetered = prefs.GetBoolean(KeyConsts.M_AllowUploadsOnMetered, true);

        UserListActivity.UserListSortOrder = (UserListActivity.SortOrder)(prefs.GetInt(KeyConsts.M_UserListSortOrder, 0));
        SeekerState.DefaultSearchResultSortAlgorithm = (SearchResultSorting)(prefs.GetInt(KeyConsts.M_DefaultSearchResultSortAlgorithm, 0));

        SimultaneousDownloadsGatekeeper.Initialize(prefs.GetBoolean(KeyConsts.M_LimitSimultaneousDownloads, false), prefs.GetInt(KeyConsts.M_MaxSimultaneousLimit, 1));

        SearchTabHelper.RestoreHeadersFromSharedPreferences();
        SettingsActivity.RestoreAdditionalDirectorySettingsFromSharedPreferences();

        ChatroomActivity.ShowStatusesView = prefs.GetBoolean(KeyConsts.M_ShowStatusesView, true);
        ChatroomActivity.ShowTickerView = prefs.GetBoolean(KeyConsts.M_ShowTickerView, false);
        ChatroomController.SortChatroomUsersBy = (ChatroomController.SortOrderChatroomUsers)(prefs.GetInt(KeyConsts.M_RoomUserListSortOrder, 2)); //default is 2 = alphabetical..
        ChatroomController.PutFriendsOnTop = prefs.GetBoolean(KeyConsts.M_RoomUserListShowFriendsAtTop, false);

        DiagnosticFile.Enabled = prefs.GetBoolean(KeyConsts.M_LOG_DIAGNOSTICS, false);


        if (TransfersFragment.TransferItemManagerDL != null)
        {
            return;
        }
        
        TransfersFragment.RestoreDownloadTransferItems(prefs);
        TransfersFragment.RestoreUploadTransferItems(prefs);
        TransfersFragment.TransferItemManagerWrapped = new TransferItemManagerWrapper(
            TransfersFragment.TransferItemManagerUploads, TransfersFragment.TransferItemManagerDL);
    }
    
    public static void SaveSpeedLimitState()
    {
        lock (SeekerApplication.SharedPrefLock)
        {
            SeekerState.SharedPreferences.Edit()!
                .PutBoolean(KeyConsts.M_DownloadLimitEnabled, SeekerState.SpeedLimitDownloadOn)!
                .PutBoolean(KeyConsts.M_DownloadPerTransfer, SeekerState.SpeedLimitDownloadIsPerTransfer)!
                .PutInt(KeyConsts.M_DownloadSpeedLimitBytes, SeekerState.SpeedLimitDownloadBytesSec)!
                .PutBoolean(KeyConsts.M_UploadLimitEnabled, SeekerState.SpeedLimitUploadOn)!
                .PutBoolean(KeyConsts.M_UploadPerTransfer, SeekerState.SpeedLimitUploadIsPerTransfer)!
                .PutInt(KeyConsts.M_UploadSpeedLimitBytes, SeekerState.SpeedLimitUploadBytesSec)!
                .Commit();
        }
    }
    
    public static void SaveListeningState()
    {
        lock (SeekerApplication.SharedPrefLock)
        {
            SeekerState.SharedPreferences.Edit()!
                .PutBoolean(KeyConsts.M_ListenerEnabled, SeekerState.ListenerEnabled)!
                .PutInt(KeyConsts.M_ListenerPort, SeekerState.ListenerPort)!
                .PutBoolean(KeyConsts.M_ListenerUPnpEnabled, SeekerState.ListenerUPnpEnabled)!
                .Commit();
        }
    }
    
    public static void RestoreListeningState()
    {
        var sharedPreferences = SeekerState.SharedPreferences;
        lock (SeekerApplication.SharedPrefLock)
        {
            SeekerState.ListenerEnabled = sharedPreferences.GetBoolean(KeyConsts.M_ListenerEnabled, true);
            SeekerState.ListenerPort = sharedPreferences.GetInt(KeyConsts.M_ListenerPort, 33939);
            SeekerState.ListenerUPnpEnabled = sharedPreferences.GetBoolean(KeyConsts.M_ListenerUPnpEnabled, true);
        }
    }

    public static bool GetBoolean(this ISharedPreferences preferences, int keyId, bool defaultValue)
    {
        var key = SeekerApplication.ApplicationContext.GetString(keyId);
        return preferences.GetBoolean(key, defaultValue);
    }

    public static ISharedPreferencesEditor PutBoolean(this ISharedPreferencesEditor preferencesEditor, 
        int keyId, bool value)
    {
        var key = SeekerApplication.ApplicationContext.GetString(keyId);
        return preferencesEditor.PutBoolean(key, value);
    }

    public static string GetString(this ISharedPreferences preferences, int keyId, string defaultValue)
    {
        var key = SeekerApplication.ApplicationContext.GetString(keyId);
        return preferences.GetString(key, defaultValue);
    }

    public static ISharedPreferencesEditor PutString(this ISharedPreferencesEditor preferencesEditor, int keyId,
        string value)
    {
        var key = SeekerApplication.ApplicationContext.GetString(keyId);
        return preferencesEditor.PutString(key, value);
    }

    public static int GetInt(this ISharedPreferences preferences, int keyId, int defaultValue)
    {
        var key = SeekerApplication.ApplicationContext.GetString(keyId);
        return preferences.GetInt(key, defaultValue);
    }

    public static ISharedPreferencesEditor PutInt(this ISharedPreferencesEditor preferencesEditor, int keyId, int value)
    {
        var key = SeekerApplication.ApplicationContext.GetString(keyId);
        return preferencesEditor.PutInt(key, value);
    }
}
