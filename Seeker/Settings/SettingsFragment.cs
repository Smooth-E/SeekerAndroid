using _Microsoft.Android.Resource.Designer;
using Android.OS;
using AndroidX.Preference;

namespace Seeker.Settings;

public class SettingsFragment : PreferenceFragmentCompat
{
    private SettingsActivity settingsActivity;
    private SwitchPreferenceCompat createCompleteIncompleteFolders;
    private SwitchPreferenceCompat createUsernameSubfolders;
    private SwitchPreferenceCompat createSubfoldersForSingleDownloads;
    
    public override void OnCreatePreferences(Bundle savedInstanceState, string rootKey)
    {
        settingsActivity = RequireActivity() as SettingsActivity;
        
        SetPreferencesFromResource(ResourceConstant.Xml.seeker_preferences, rootKey);
        
        createCompleteIncompleteFolders = FindPreference<SwitchPreferenceCompat>(ResourceConstant.String.key_create_complete_and_incomplete_folders);
        createCompleteIncompleteFolders.PreferenceChange += (_, _) =>
        {
            SeekerState.CreateCompleteAndIncompleteFolders = createCompleteIncompleteFolders.Checked;
            settingsActivity.SetIncompleteFolderView();
        };

        createUsernameSubfolders = FindPreference<SwitchPreferenceCompat>(ResourceConstant.String.key_create_username_subfolders);
        createUsernameSubfolders.PreferenceChange += (_, _) => SeekerState.CreateUsernameSubfolders = createUsernameSubfolders.Checked;
        
        createSubfoldersForSingleDownloads = FindPreference<SwitchPreferenceCompat>(ResourceConstant.String.key_create_subfolders_for_single_downloads);
        createSubfoldersForSingleDownloads.PreferenceChange += (_, _) => SeekerState.NoSubfolderForSingle = createSubfoldersForSingleDownloads.Checked;
    }

    private T FindPreference<T>(int keyId) where T : Preference => FindPreference(GetString(keyId)) as T;
}
