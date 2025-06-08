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

    public static int GetThemeResource(Context context, bool? darkMode = null, string option = null)
    {
        darkMode ??= !AndroidPlatform.IsInLightMode(context);
        if (option == null)
        {
            var keyId = darkMode == true
                ? ResourceConstant.String.key_dark_theme_variant
                : ResourceConstant.String.key_light_theme_variant;
            var defaultOption = context.GetString(ResourceConstant.String.theme_variant_purple);
            option = SeekerState.SharedPreferences.GetString(keyId, defaultOption);
        }
        
        Logger.Debug($"Requesting theme resource for option {option} with dark mode {darkMode}");
        
        var defaultThemeVariant = context.GetString(ResourceConstant.String.key_theme_variant_purple);
        var blueThemeVariant = context.GetString(ResourceConstant.String.key_theme_variant_blue);
        
        if (darkMode == false)
        {
            if (option == defaultThemeVariant)
            {
                return ResourceConstant.Style.DefaultLight;
            }
            
            if (option == blueThemeVariant)
            {
                return ResourceConstant.Style.DefaultLight_Blue;
            }
            
            if (option == context.GetString(ResourceConstant.String.key_theme_variant_red))
            {
                return ResourceConstant.Style.DefaultLight_Red;
            }
            
            Logger.Debug($"Incorrect option set for light theme variant: {option}");
            return ResourceConstant.Style.DefaultLight;
        }

        if (option == defaultThemeVariant)
        {
            return ResourceConstant.Style.DefaultDark;
        }
            
        if (option == blueThemeVariant)
        {
            return ResourceConstant.Style.DefaultDark_Blue;
        }
            
        if (option == context.GetString(ResourceConstant.String.key_theme_variant_gray))
        {
            return ResourceConstant.Style.DefaultDark_Grey;
        }
            
        if (option == context.GetString(ResourceConstant.String.key_theme_variant_amoled_purple))
        {
            return ResourceConstant.Style.Amoled;
        }
            
        if (option == context.GetString(ResourceConstant.String.key_theme_variant_amoled_gray))
        {
            return ResourceConstant.Style.Amoled_Grey;
        }

        Logger.Debug($"Incorrect option set for dark theme variant: {option}");
        return ResourceConstant.Style.DefaultDark;
    }
}
