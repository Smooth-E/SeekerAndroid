﻿using System;
using System.Linq;
using _Microsoft.Android.Resource.Designer;
using Android.App;
using Android.Content;
using Android.OS;
using Android.Views;
using AndroidX.AppCompat.App;
using AndroidX.DocumentFile.Provider;
using AndroidX.Preference;
using AndroidX.RecyclerView.Widget;
using Seeker.Components;
using Seeker.Managers;
using Seeker.Utils;
using AlertDialog = AndroidX.AppCompat.App.AlertDialog;
using Environment = Android.OS.Environment;
using Uri = Android.Net.Uri;

namespace Seeker.Settings;

public class SettingsFragment : PreferenceFragmentCompat
{
    private SettingsActivity settingsActivity;
    
    private Preference dataDirectoryUriPreference;
    private Preference incompleteDirectoryUriPreference;
    
    private TwoIconPreference startStopService;

    private SwitchPreferenceCompat allowPrivateRoomInvites;
    private SwitchPreferenceCompat autoClearCompleteDownloads;
    private SwitchPreferenceCompat autoClearCompleteUploads;
    private SwitchPreferenceCompat folderCompleteNotifications;
    private SwitchPreferenceCompat fileCompleteNotifications;
    private SwitchPreferenceCompat rememberRecentUsers;
    private Preference clearRecentUsers;
    private SwitchPreferenceCompat autoRetryFailedDownloads;
    private SwitchPreferenceCompat awayOnInactivity;

    private TwoIconPreference perAppLanguage;
    private DropDownPreference appLanguage;
    private DropDownPreference appTheme;

    public override void OnCreatePreferences(Bundle savedInstanceState, string rootKey)
    {
        settingsActivity = RequireActivity() as SettingsActivity;

        SetPreferencesFromResource(ResourceConstant.Xml.seeker_preferences, rootKey);

        dataDirectoryUriPreference = FindPreference<Preference>(ResourceConstant.String.key_data_directory_uri);
        dataDirectoryUriPreference.PreferenceChange += (_, _) => UpdateDataDirectoryUriPreferenceSummary();
        dataDirectoryUriPreference.PreferenceClick += (_, _) =>
            settingsActivity.ShowDirSettings(SeekerState.SaveDataDirectoryUri, DirectoryType.Download);
        UpdateDataDirectoryUriPreferenceSummary();

        SeekerState.CreateCompleteAndIncompleteFolders.ValueChanged += _ =>
            UpdateIncompleteDirectoryUriPreferenceSummary();

        SeekerState.OverrideDefaultIncompleteLocations.ValueChanged += _ =>
        {
            settingsActivity.SetIncompleteDirectoryState();
            UpdateIncompleteDirectoryUriPreferenceSummary();
        };

        incompleteDirectoryUriPreference =
            FindPreference<Preference>(ResourceConstant.String.key_manual_incomplete_directory_uri);
        incompleteDirectoryUriPreference.PreferenceClick += (_, _) =>
            settingsActivity.ShowDirSettings(SeekerState.ManualIncompleteDataDirectoryUri, DirectoryType.Incomplete);
        incompleteDirectoryUriPreference.PreferenceChange += (_, _) =>
            UpdateIncompleteDirectoryUriPreferenceSummary();
        UpdateIncompleteDirectoryUriPreferenceSummary();

        FindPreference<Preference>(ResourceConstant.String.key_clear_incomplete_folder)
            .PreferenceClick += (_, _) => ClearIncompleteFolder();
        
        SeekerState.FileBackedDownloads.ValueChanged += _ => UpdateIncompleteDirectoryUriPreferenceSummary();

        FindPreference<Preference>(ResourceConstant.String.key_about_file_backed_downloads)
            .PreferenceClick += (_, _) => ShowAboutFileBackedDownloadsDialog();

        FindPreference<Preference>(ResourceConstant.String.key_configure_smart_filters)
            .PreferenceClick += (_, _) => ShowSmartFiltersConfigurationDialog();

        FindPreference<Preference>(ResourceConstant.String.key_clear_search_history)
            .PreferenceClick += (_, _) => SeekerState.ClearSearchHistoryInvoke();

        startStopService = FindPreference<TwoIconPreference>(
                ResourceConstant.String.key_start_stop_seeker_service);
        startStopService.PreferenceClick += (_, _) => OnStartStopPreferenceClicked();
        UpdateStartStopServicePreference();

        FindPreference<Preference>(ResourceConstant.String.key_about_seeker_service)
            .PreferenceClick += (_, _) => OnAboutSeekerServicePreferenceClicked();

        allowPrivateRoomInvites = FindPreference<SwitchPreferenceCompat>(
            ResourceConstant.String.key_allow_private_room_invites);
        allowPrivateRoomInvites.PreferenceClick += (_, _) => OnAllowPrivateRoomInvitationsClicked();

        autoClearCompleteUploads = FindPreference<SwitchPreferenceCompat>(
            ResourceConstant.String.key_auto_clear_complete_uploads);
        autoClearCompleteUploads.PreferenceChange += (_, args) =>
            SeekerState.AutoClearCompleteUploads = Convert.ToBoolean(args.NewValue);

        folderCompleteNotifications = FindPreference<SwitchPreferenceCompat>(
            ResourceConstant.String.key_notify_on_folder_complete);
        folderCompleteNotifications.PreferenceChange += (_, args) =>
            SeekerState.NotifyOnFolderCompleted = Convert.ToBoolean(args.NewValue);

        fileCompleteNotifications = FindPreference<SwitchPreferenceCompat>(
            ResourceConstant.String.key_notify_on_file_complete);
        fileCompleteNotifications.PreferenceChange += (_, args) =>
            SeekerState.DisableDownloadToastNotification = !Convert.ToBoolean(args.NewValue);

        rememberRecentUsers = FindPreference<SwitchPreferenceCompat>(
            ResourceConstant.String.key_remember_recent_users);
        rememberRecentUsers.PreferenceChange += (_, args) =>
            SeekerState.ShowRecentUsers = Convert.ToBoolean(args.NewValue);

        clearRecentUsers = FindPreference<Preference>(ResourceConstant.String.key_clear_recent_users);
        clearRecentUsers.PreferenceClick += (_, _) => ClearRecentUsers();

        autoRetryFailedDownloads = FindPreference<SwitchPreferenceCompat>(
            ResourceConstant.String.key_auto_retry_failed_downloads);
        autoRetryFailedDownloads.PreferenceChange += (_, args) =>
            SeekerState.AutoRetryBackOnline = Convert.ToBoolean(args.NewValue);

        awayOnInactivity = FindPreference<SwitchPreferenceCompat>(ResourceConstant.String.key_away_on_inactivity);
        awayOnInactivity.PreferenceChange += (_, args) =>
            SeekerState.AutoAwayOnInactivity = Convert.ToBoolean(args.NewValue);

        perAppLanguage = FindPreference<TwoIconPreference>(ResourceConstant.String.key_per_app_language);
        appLanguage = FindPreference<DropDownPreference>(ResourceConstant.String.key_language);

        var hasPerAppLanguageSupport = AndroidPlatform.HasProperPerAppLanguageSupport();
        
        perAppLanguage.Visible = hasPerAppLanguageSupport;
        appLanguage.Visible = !hasPerAppLanguageSupport;
        
        perAppLanguage.PreferenceClick += (_, _) =>
        {
            var intent = new Intent(Android.Provider.Settings.ActionApplicationDetailsSettings)
                .SetData(Uri.FromParts("package", RequireActivity().PackageName, null));
            StartActivity(intent);
        };

        appLanguage.PreferenceChange += (_, args) =>
            LanguageUtils.ApplyLanguageSettings(RequireActivity(), Convert.ToString(args.NewValue));

        appTheme = FindPreference<DropDownPreference>(ResourceConstant.String.key_app_theme);
        appTheme.PreferenceChange += (_, args) =>
        {
            var option = Convert.ToString(args.NewValue);
            ThemeUtils.UpdateNightModePreference(RequireActivity(), option);
            SeekerState.DayNightMode = AppCompatDelegate.DefaultNightMode;
        };
    }

    private T FindPreference<T>(int keyId) where T : Preference => FindPreference(GetString(keyId)) as T;

    private void UpdateDataDirectoryUriPreferenceSummary()
    {
        string summary;
        if (SeekerState.RootDocumentFile == null) // even in API<21 we do set this RootDocumentFile
        {
            if (SeekerState.UseLegacyStorage())
            {
                // if not set and legacy storage, then the directory is simple the default music
                var path = Environment.GetExternalStoragePublicDirectory(Environment.DirectoryMusic)?.AbsolutePath;
                summary = Uri.Parse(new Java.IO.File(path!).ToURI().ToString())?.LastPathSegment;
            }
            else
            {
                // if not set and not legacy storage, then that is bad. user must set it.
                summary = SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.NotSet);
            }
        }
        else
        {
            summary = SeekerState.RootDocumentFile.Uri.LastPathSegment;
        }

        var prefix = GetString(ResourceConstant.String.CurrentDownloadFolder_);
        summary = CommonHelpers.AvoidLineBreaks(summary);
        dataDirectoryUriPreference.Summary = $"{prefix}\n{summary}";
    }

    private void UpdateIncompleteDirectoryUriPreferenceSummary()
    {
        string summary;
        if (!SeekerState.FileBackedDownloads.Value)
        {
            summary = SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.NotInUse);
        }
        // if doc file is null that means we could not write to it.
        else if (SeekerState.OverrideDefaultIncompleteLocations.Value && SeekerState.RootIncompleteDocumentFile != null)
        {
            summary = SeekerState.RootIncompleteDocumentFile.Uri.LastPathSegment;
        }
        else
        {
            if (!SeekerState.CreateCompleteAndIncompleteFolders.Value)
            {
                summary = SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.AppLocalStorage);
            }
            // if not override then it's whatever the download directory is...
            else if (SeekerState.RootDocumentFile == null) //even in API<21 we do set this RootDocumentFile
            {
                if (SeekerState.UseLegacyStorage())
                {
                    //if not set and legacy storage, then the directory is simple the default music
                    var path = Environment.GetExternalStoragePublicDirectory(Environment.DirectoryMusic)?.AbsolutePath;
                    // this is to prevent line breaks.
                    summary = Android.Net.Uri.Parse(new Java.IO.File(path!).ToURI().ToString())?.LastPathSegment;
                }
                else
                {
                    // if not set and not legacy storage, then that is bad.  user must set it.
                    summary = SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.NotSet);
                }
            }
            else
            {
                summary = SeekerState.RootDocumentFile.Uri.LastPathSegment;
            }
        }
        
        summary = CommonHelpers.AvoidLineBreaks(summary);
        var prefix = GetString(ResourceConstant.String.CurrentIncompleteFolder);
        incompleteDirectoryUriPreference.Summary = $"{prefix}\n{summary}";
    }
    
    private void ClearIncompleteFolder()
    {
        var doNotDelete = TransfersFragment.TransferItemManagerDL.GetInUseIncompleteFolderNames();

        var useDownloadDir = 
            SeekerState.CreateCompleteAndIncompleteFolders.Value && !SettingsActivity.UseIncompleteManualFolder();
        
        var useTempDir = SettingsActivity.UseTempDirectory();
        var useCustomDir = SettingsActivity.UseIncompleteManualFolder();

        bool folderExists;
        var folderCount = 0;
        if (SeekerState.UseLegacyStorage() && (SeekerState.RootDocumentFile == null && useDownloadDir))
        {
            var rootPath = Environment.GetExternalStoragePublicDirectory(Environment.DirectoryMusic)!.AbsolutePath;
            var rootFolder = new Java.IO.File(rootPath);
            if (!rootFolder.Exists())
            {
               rootFolder.Mkdirs();
            }

            var incompleteDirString = rootPath + "/Soulseek Incomplete/";
            var incompleteDir = new Java.IO.File(incompleteDirString);
            folderExists = incompleteDir.Exists();
            if (folderExists)
            {
                foreach (var file in incompleteDir.ListFiles()!)
                {
                    if (!file.IsDirectory || doNotDelete.Contains(file.Name))
                    {
                        continue;
                    }
                    
                    // TODO: Does this account for deleting folders containing other folders?
                    foreach (var document in file.ListFiles()!)
                    {
                        document.Delete();
                    }
                    file.Delete();
                        
                    folderCount++;
                }
            }
        }
        else
        {
            DocumentFile rootDir = null;
            if (useDownloadDir)
            {
                if (SeekerState.RootDocumentFile == null)
                {
                    Context.ShowLongToast(ResourceConstant.String.ErrorDownloadDirNotProperlySet);
                    return;
                }
                rootDir = SeekerState.RootDocumentFile;
                Logger.Debug("using download dir" + rootDir.Uri.LastPathSegment);
            }
            else if (useTempDir)
            {
                var appPrivateExternal = SeekerState.ActiveActivityRef.GetExternalFilesDir(null)!;
                rootDir = DocumentFile.FromFile(appPrivateExternal);
                Logger.Debug("using temp incomplete dir");
            }
            else if (useCustomDir)
            {
                rootDir = SeekerState.RootIncompleteDocumentFile;
                Logger.Debug("using custom incomplete dir" + rootDir.Uri.LastPathSegment);
            }

            var incompleteDirectory = rootDir!.FindFile("Soulseek Incomplete");
            folderExists = incompleteDirectory != null && incompleteDirectory.Exists();
            if (folderExists)
            {
                foreach (var file in incompleteDirectory!.ListFiles())
                {
                    if (!file.IsDirectory || doNotDelete.Contains(file.Name))
                    {
                        continue;
                    }
                    
                    // TODO: Does this account for deleting folders that contain other folders?
                    foreach (var document in file.ListFiles())
                    {
                        document.Delete();
                    }
                    file.Delete();
                        
                    folderCount++;
                }
            }
        }

        var messageString = !folderExists
            ? GetString(ResourceConstant.String.IncompleteFolderEmpty)
            : folderCount == 0
                ? GetString(ResourceConstant.String.NoEligibleToClear)
                : folderCount == 1
                    ? GetString(ResourceConstant.String.message_cleared_single_folder)
                    : string.Format(GetString(ResourceConstant.String.message_cleared_several_folders),
                        folderCount);
        
        Context.ShowLongToast(messageString);
    }

    private void ShowAboutFileBackedDownloadsDialog()
    {
        new AlertDialog.Builder(RequireActivity(), ResourceConstant.Style.MyAlertDialogTheme)!
            .SetTitle(ResourceConstant.String.preference_about_file_backed_downloads_dialog_title)!
            .SetMessage(ResourceConstant.String.memory_file_backed)!
            .SetPositiveButton(ResourceConstant.String.okay, (sender, _) => (sender as Dialog)!.Dismiss())
            .Show();
    }
    
    private void ShowSmartFiltersConfigurationDialog()
    {
        Logger.FirebaseInfo("ConfigSmartFilters_Click");
        
        var builder = new AlertDialog.Builder(RequireActivity(), ResourceConstant.Style.MyAlertDialogTheme)
            .SetTitle(ResourceConstant.String.ConfigureSmartFilters)!;
        
        var viewInflated = LayoutInflater.From(RequireActivity())!
            .Inflate(ResourceConstant.Layout.smart_filter_config_layout, null, false)!;
        
        var recyclerViewFiltersConfig = 
            viewInflated.FindViewById<RecyclerView>(ResourceConstant.Id.recyclerViewFiltersConfig)!;
        
        builder.SetView(viewInflated);
        
        var adapterItems = SeekerState.SmartFilterOptions.GetAdapterItems();
        var adapter = new RecyclerListAdapter(RequireActivity(), null, adapterItems);

        recyclerViewFiltersConfig.HasFixedSize = true;
        recyclerViewFiltersConfig.SetAdapter(adapter);
        recyclerViewFiltersConfig.SetLayoutManager(new LinearLayoutManager(RequireActivity()));

        var callback = new DragDropItemTouchHelper(adapter);
        var mItemTouchHelper = new ItemTouchHelper(callback);
        mItemTouchHelper.AttachToRecyclerView(recyclerViewFiltersConfig);
        adapter.ItemTouchHelper = mItemTouchHelper;

        builder.SetPositiveButton(ResourceConstant.String.okay, (_, _) => SaveSmartFilters(adapter));
        builder.SetNegativeButton(ResourceConstant.String.cancel, (_, _) => { });

        builder.Show();
    }
    
    private static void SaveSmartFilters(RecyclerListAdapter adapter)
    {
        SeekerState.SmartFilterOptions.FromAdapterItems(adapter.GetAdapterItems());
        SeekerState.SaveSmartFilterState();
    }

    private void UpdateStartStopServicePreference()
    {
        var isRunning = SeekerState.IsStartUpServiceCurrentlyRunning;
        startStopService.Title = isRunning
            ? GetString(ResourceConstant.String.preference_stop_seeker_service)
            : GetString(ResourceConstant.String.preference_start_seeker_service);
        startStopService.Summary = isRunning
            ? GetString(ResourceConstant.String.preference_stop_seeker_service_summary)
            : GetString(ResourceConstant.String.preference_start_seeker_service_summary);
        startStopService.SecondaryIcon = isRunning
            ? Resources.GetDrawable(ResourceConstant.Drawable.baseline_stop_circle)
            : Resources.GetDrawable(ResourceConstant.Drawable.baseline_play_circle);
    }

    private void OnStartStopPreferenceClicked()
    {
        var activity = RequireActivity();
        var intent = new Intent(activity, typeof(SeekerKeepAliveService));
        if (SeekerState.IsStartUpServiceCurrentlyRunning)
        {
            activity.StopService(intent);
        }
        else
        {
            activity.StartService(intent);
        }
        
        SeekerState.IsStartUpServiceCurrentlyRunning = !SeekerState.IsStartUpServiceCurrentlyRunning;
        UpdateStartStopServicePreference();
    }

    private void OnAboutSeekerServicePreferenceClicked()
    {
        new AlertDialog.Builder(RequireActivity()!, ResourceConstant.Style.MyAlertDialogTheme)
            .SetTitle(ResourceConstant.String.preference_about_seeker_service_dialog_title)!
            .SetMessage(ResourceConstant.String.keep_alive_service)!
            .SetPositiveButton(ResourceConstant.String.okay, (sender, _) => (sender as Dialog)!.Dismiss())
            .Show();
    }

    private void OnAllowPrivateRoomInvitationsClicked()
    {
        var isChecked = allowPrivateRoomInvites.Checked;
        if (isChecked == SeekerState.AllowPrivateRoomInvitations)
        {
            Logger.Debug("allow private: nothing to do");
            return;
        }

        var newState = isChecked 
            ? GetString(ResourceConstant.String.allowed) 
            : GetString(ResourceConstant.String.denied);

        var message = string.Format(GetString(ResourceConstant.String.setting_priv_invites), newState);
        RequireActivity().ShowShortToast(message);
        
        settingsActivity.ReconfigureOptionsApi(isChecked, null, null);
    }

    public void SetAllowPrivateRoomInvitations(bool value)
    {
        allowPrivateRoomInvites.Checked = value;
    }

    private static void ClearRecentUsers()
    {
        // set to just the added users....
        var count = UserListManager.UserList?.Count ?? 0;
        if (count > 0)
        {
            lock (UserListManager.UserList!)
            {
                var list = UserListManager.UserList.Select(uli => uli.Username).ToList();
                SeekerState.RecentUsersManager.SetRecentUserList(list);
            }
        }
        else
        {
            SeekerState.RecentUsersManager.SetRecentUserList([]);
        }
        
        SeekerState.RecentUsersManager.SaveRecentUsers();
    }
}
