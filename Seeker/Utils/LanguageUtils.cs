using _Microsoft.Android.Resource.Designer;
using Android.App;
using Android.Content;
using Android.OS;
using Locale = Java.Util.Locale;

namespace Seeker.Utils;

public static class LanguageUtils
{
    public static void ApplyLanguageSettings(Context context, string language = null)
    {
        if (AndroidPlatform.HasProperPerAppLanguageSupport())
        {
            return;
        }
    
#pragma warning disable CA1416 // TODO: Fix requirement of Android 21  
        if (language == null)
        {
            var languageAuto = context.GetString(ResourceConstant.String.language_auto);
            language = SeekerState.SharedPreferences
                .GetString(ResourceConstant.String.key_language, languageAuto);
            
            if (language == languageAuto)
            {
                return;
            }
        }

        Logger.Debug("Received new language " + language);
        
        var localeManager = context.GetSystemService(Context.LocaleService) as LocaleManager;
        localeManager!.ApplicationLocales = new LocaleList(Locale.ForLanguageTag(language));
#pragma warning restore CA1416
    }
}
