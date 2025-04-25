using System;
using _Microsoft.Android.Resource.Designer;
using Android.OS;
using Android.Views;
using AndroidX.AppCompat.App;
using AndroidX.DocumentFile.Provider;
using AndroidX.Preference;
using AndroidX.RecyclerView.Widget;
using Seeker.Utils;
using Environment = Android.OS.Environment;

namespace Seeker.Settings;

public class SettingsFragment : PreferenceFragmentCompat
{
    private SettingsActivity settingsActivity;
    
    private Preference dataDirectoryUriPreference;
    private SwitchPreferenceCompat createCompleteIncompleteFolders;
    private SwitchPreferenceCompat createUsernameSubfolders;
    private SwitchPreferenceCompat createSubfoldersForSingleDownloads;
    private SwitchPreferenceCompat useManualIncompleteDirectory;
    private Preference incompleteDirectoryUriPreference;
    private Preference clearIncompleteFolder;
    
    private SeekBarPreference maxSearchResults;
    private SwitchPreferenceCompat showSmartFilters;
    private Preference configureSmartFilters;
    private SwitchPreferenceCompat freeUploadSlotsOnly;
    private SwitchPreferenceCompat hideLockedWhenSearching;
    private SwitchPreferenceCompat hideLockedWhenBrowsing;

    public override void OnCreatePreferences(Bundle savedInstanceState, string rootKey)
    {
        settingsActivity = RequireActivity() as SettingsActivity;

        SetPreferencesFromResource(ResourceConstant.Xml.seeker_preferences, rootKey);

        dataDirectoryUriPreference = FindPreference<Preference>(ResourceConstant.String.key_data_directory_uri);
        dataDirectoryUriPreference.PreferenceChange += (_, _) => UpdateDataDirectoryUriPreferenceSummary();
        dataDirectoryUriPreference.PreferenceClick += (_, _) => 
            settingsActivity.ShowDirSettings(SeekerState.SaveDataDirectoryUri, DirectoryType.Download);
        UpdateDataDirectoryUriPreferenceSummary();

        createCompleteIncompleteFolders = FindPreference<SwitchPreferenceCompat>(
            ResourceConstant.String.key_create_complete_and_incomplete_folders);
        createCompleteIncompleteFolders.PreferenceChange += (_, args) =>
        {
            SeekerState.CreateCompleteAndIncompleteFolders = Convert.ToBoolean(args.NewValue);
            UpdateIncompleteDirectoryUriPreferenceSummary();
        };

        createUsernameSubfolders = FindPreference<SwitchPreferenceCompat>(
            ResourceConstant.String.key_create_username_subfolders);
        createUsernameSubfolders.PreferenceChange += (_, args) =>
            SeekerState.CreateUsernameSubfolders = Convert.ToBoolean(args.NewValue);

        createSubfoldersForSingleDownloads = FindPreference<SwitchPreferenceCompat>(
            ResourceConstant.String.key_create_subfolders_for_single_downloads);
        createSubfoldersForSingleDownloads.PreferenceChange += (_, args) =>
            SeekerState.NoSubfolderForSingle = Convert.ToBoolean(args.NewValue);

        useManualIncompleteDirectory = FindPreference<SwitchPreferenceCompat>(
            ResourceConstant.String.key_use_manual_incomplete_directory_uri);
        useManualIncompleteDirectory.PreferenceChange += (_, args) =>
        {
            SeekerState.OverrideDefaultIncompleteLocations = Convert.ToBoolean(args.NewValue);
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
        
        clearIncompleteFolder = FindPreference<Preference>(ResourceConstant.String.key_clear_incomplete_folder);
        clearIncompleteFolder.PreferenceClick += (_, _) => ClearIncompleteFolder();

        maxSearchResults = FindPreference<SeekBarPreference>(ResourceConstant.String.key_max_search_results);
        maxSearchResults.PreferenceChange += (_, args) =>
            SeekerState.NumberSearchResults = Convert.ToInt32(args.NewValue);
        
        showSmartFilters = FindPreference<SwitchPreferenceCompat>(ResourceConstant.String.key_show_smart_filters);
        showSmartFilters.PreferenceChange += (_, args) =>
            SeekerState.ShowSmartFilters = Convert.ToBoolean(args.NewValue);
        
        configureSmartFilters = FindPreference<Preference>(ResourceConstant.String.key_configure_smart_filters);
        configureSmartFilters.PreferenceClick += (_, _) => ShowSmartFiltersConfigurationDialog();

        freeUploadSlotsOnly = FindPreference<SwitchPreferenceCompat>(
            ResourceConstant.String.key_free_upload_slots_only);
        freeUploadSlotsOnly.PreferenceChange += (_, args) => 
            SeekerState.FreeUploadSlotsOnly = Convert.ToBoolean(args.NewValue);

        hideLockedWhenSearching = FindPreference<SwitchPreferenceCompat>(
            ResourceConstant.String.key_hide_locked_in_search);
        hideLockedWhenSearching.PreferenceChange += (_, args) =>
            SeekerState.HideLockedResultsInSearch = Convert.ToBoolean(args.NewValue);

        hideLockedWhenBrowsing = FindPreference<SwitchPreferenceCompat>(
            ResourceConstant.String.key_hide_locked_in_browse);
        hideLockedWhenBrowsing.PreferenceChange += (_, args) =>
            SeekerState.HideLockedResultsInBrowse = Convert.ToBoolean(args.NewValue);
    }

    private T FindPreference<T>(int keyId) where T : Preference => FindPreference(GetString(keyId)) as T;

    private void UpdateDataDirectoryUriPreferenceSummary()
    {
        string summary;
        if (SeekerState.RootDocumentFile == null) //even in API<21 we do set this RootDocumentFile
        {
            if (SeekerState.UseLegacyStorage())
            {
                //if not set and legacy storage, then the directory is simple the default music
                var path = Environment.GetExternalStoragePublicDirectory(Environment.DirectoryMusic)?.AbsolutePath;
                summary = Android.Net.Uri.Parse(new Java.IO.File(path!).ToURI().ToString())?.LastPathSegment;
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
        if (SeekerState.MemoryBackedDownload)
        {
            summary = SeekerApplication.ApplicationContext.GetString(ResourceConstant.String.NotInUse);
        }
        // if doc file is null that means we could not write to it.
        else if (SeekerState.OverrideDefaultIncompleteLocations && SeekerState.RootIncompleteDocumentFile != null)
        {
            summary = SeekerState.RootIncompleteDocumentFile.Uri.LastPathSegment;
        }
        else
        {
            if (!SeekerState.CreateCompleteAndIncompleteFolders)
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
            SeekerState.CreateCompleteAndIncompleteFolders && !SettingsActivity.UseIncompleteManualFolder();
        
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
}
