using _Microsoft.Android.Resource.Designer;
using Android.OS;
using AndroidX.Preference;

namespace Seeker.Settings;

public class SettingsFragment : PreferenceFragmentCompat
{
    private SettingsActivity settingsActivity;
    private Preference dataDirectoryUriPreference;
    private SwitchPreferenceCompat createCompleteIncompleteFolders;
    private SwitchPreferenceCompat createUsernameSubfolders;
    private SwitchPreferenceCompat createSubfoldersForSingleDownloads;

    public override void OnCreatePreferences(Bundle savedInstanceState, string rootKey)
    {
        settingsActivity = RequireActivity() as SettingsActivity;

        SetPreferencesFromResource(ResourceConstant.Xml.seeker_preferences, rootKey);

        dataDirectoryUriPreference = FindPreference<Preference>(ResourceConstant.String.key_data_directory_uri);
        dataDirectoryUriPreference.PreferenceChange += (_, _) => UpdateDataDirectoryUriPreferenceSummary();
        dataDirectoryUriPreference.PreferenceClick += (_, _) => settingsActivity.ShowDirSettings(SeekerState.SaveDataDirectoryUri, DirectoryType.Download);
        UpdateDataDirectoryUriPreferenceSummary();

        createCompleteIncompleteFolders =
            FindPreference<SwitchPreferenceCompat>(ResourceConstant.String.key_create_complete_and_incomplete_folders);
        createCompleteIncompleteFolders.PreferenceChange += (_, _) =>
        {
            SeekerState.CreateCompleteAndIncompleteFolders = createCompleteIncompleteFolders.Checked;
            settingsActivity.SetIncompleteFolderView();
        };

        createUsernameSubfolders =
            FindPreference<SwitchPreferenceCompat>(ResourceConstant.String.key_create_username_subfolders);
        createUsernameSubfolders.PreferenceChange +=
            (_, _) => SeekerState.CreateUsernameSubfolders = createUsernameSubfolders.Checked;

        createSubfoldersForSingleDownloads = FindPreference<SwitchPreferenceCompat>(ResourceConstant.String.key_create_subfolders_for_single_downloads);
        createSubfoldersForSingleDownloads.PreferenceChange += (_, _) =>
            SeekerState.NoSubfolderForSingle = createSubfoldersForSingleDownloads.Checked;
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
}
