using _Microsoft.Android.Resource.Designer;
using Android.Content;
using AndroidX.AppCompat.App;

namespace Seeker.Utils;

public static class ThemeUtils
{
    public static void UpdateNightModePreference(Context context, string option = null)
    {
        var appThemeAuto = context.GetString(ResourceConstant.String.key_app_theme_system);
        option ??= SeekerState.SharedPreferences.GetString(ResourceConstant.String.key_app_theme, appThemeAuto);
        AppCompatDelegate.DefaultNightMode = NightModeOptionToInt(context, option);
    }

    public static int NightModeOptionToInt(Context context, string option)
    {
        if (option == context.GetString(ResourceConstant.String.key_app_theme_system))
        {
            return AppCompatDelegate.ModeNightFollowSystem;
        }
        
        if (option == context.GetString(ResourceConstant.String.key_app_theme_light))
        {
            return AppCompatDelegate.ModeNightNo;
        }
        
        if (option == context.GetString(ResourceConstant.String.key_app_theme_dark))
        { 
            return AppCompatDelegate.ModeNightYes;
        }
        
        Logger.Debug($"Incorrect night mode preference: {option}");
        return AppCompatDelegate.ModeNightFollowSystem;   
    }

    // TODO: SeekerState.NightModeOption is not really needed, remove it later
    public static string IntToNightModeOption(Context context, int option)
    {
        switch (option)
        {
            case AppCompatDelegate.ModeNightFollowSystem:
                return context.GetString(ResourceConstant.String.key_app_theme_system);
            case AppCompatDelegate.ModeNightNo:
                return context.GetString(ResourceConstant.String.key_app_theme_light);
            case AppCompatDelegate.ModeNightYes:
                return context.GetString(ResourceConstant.String.key_app_theme_dark);
            default:
                Logger.Debug($"Incorrect night mode preference int: {option}");
                return context.GetString(ResourceConstant.String.key_app_theme_system);
        }
    }
}
