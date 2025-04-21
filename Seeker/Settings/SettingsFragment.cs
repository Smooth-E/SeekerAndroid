using System;
using _Microsoft.Android.Resource.Designer;
using Android.OS;
using AndroidX.Preference;
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
            Logger.Debug($"useManualIncompleteDirectory: {Convert.ToBoolean(args.NewValue)}");
            
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
                // if not set and not legacy storage, then that is bad.  user must set it.
                summary = SeekerApplication.ApplicationContext.GetString(Resource.String.NotSet);
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
            //if not override then its whatever the download directory is...
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
}
