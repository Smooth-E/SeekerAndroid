using _Microsoft.Android.Resource.Designer;
using Android.Content;
using Android.OS;
using AndroidX.AppCompat.App;
using AndroidX.Preference;
using Seeker.Utils;
using Object = Java.Lang.Object;

namespace Seeker.Main;

public class ThemeableActivity : AppCompatActivity
{
    protected override void OnCreate(Bundle savedInstanceState)
    {
        SetTheme(ThemeUtils.GetThemeResource(this));
        
        base.OnCreate(savedInstanceState);

        var listener = new ThemeChangeListener(this);
        PreferenceManager.GetDefaultSharedPreferences(this)!.RegisterOnSharedPreferenceChangeListener(listener);
    }
    
    private class ThemeChangeListener(ThemeableActivity activity) 
        : Object, ISharedPreferencesOnSharedPreferenceChangeListener
    {
        public void OnSharedPreferenceChanged(ISharedPreferences sharedPreferences, string key)
        {
            var lightMode = AndroidPlatform.IsInLightMode(activity);
            var updateTheme =
                lightMode && key == activity.GetString(ResourceConstant.String.key_light_theme_variant)
                || !lightMode && key == activity.GetString(ResourceConstant.String.key_dark_theme_variant);
            
            if (!updateTheme)
            {
                return;
            }
            
            activity.Recreate();
        }
    }
}
