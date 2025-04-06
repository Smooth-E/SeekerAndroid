using Android.App;
using Android.Content;
using Android.OS;

namespace Seeker.Utils;

public static class LanguageUtils
{
    public static void SetLanguageBasedOnPlatformSupport(Application app)
    {
        if (AndroidPlatform.HasProperPerAppLanguageSupport())
        {
            if (!SeekerState.LegacyLanguageMigrated)
            {
                SeekerState.LegacyLanguageMigrated = true;
                lock (SeekerApplication.SharedPrefLock)
                {
                    SeekerState.SharedPreferences!.Edit()!
                        .PutBoolean(KeyConsts.M_LegacyLanguageMigrated, SeekerState.LegacyLanguageMigrated)!
                        .Commit();
                }
                    
                SetLanguage(app, SeekerState.Language);
            }
        }
        else
        {
            SetLanguageLegacy(app, SeekerState.Language, false);
        }
    }
    
    private static void SetLanguageLegacy(Application app, string language, bool changed)
    {
        var res = app.Resources;
        var config = res!.Configuration;
        var displayMetrics = res.DisplayMetrics;

        var currentLocale = config!.Locale;

        if (currentLocale.ToVariantAwareString() == language)
        {
            return;
        }

        if (language == SeekerState.FieldLangAuto && 
            SeekerState.SystemLanguage == currentLocale.ToVariantAwareString())
        {
            return;
        }


        var locale = LocaleUtils
            .LocaleFromString(language != SeekerState.FieldLangAuto ? language : SeekerState.SystemLanguage);
        Java.Util.Locale.Default = locale;
        config.SetLocale(locale);

        // TODO: This call is reachable on Android 25, though the method is called 'legacy'
        app.BaseContext?.Resources?.UpdateConfiguration(config, displayMetrics);

        if (changed)
        {
            SeekerApplication.RecreateActivies();
        }
    }

    // TODO: This is probably not needed with SetLanguageBasedOnPlatformSupport being present
    public static void SetLanguage(Application app, string language)
    {
        if (AndroidPlatform.HasProperPerAppLanguageSupport())
        {
#pragma warning disable CA1416
            var lm = (LocaleManager)app.GetSystemService(Context.LocaleService)!;
            lm.ApplicationLocales = language == SeekerState.FieldLangAuto 
                ? LocaleList.EmptyLocaleList
                : LocaleList.ForLanguageTags(LocaleUtils.FormatLocaleFromResourcesToStandard(language));
#pragma warning restore CA1416
        }
        else
        {
            SetLanguageLegacy(app, SeekerState.Language, true);
        }
    }
    
    public static string GetLegacyLanguageString(Context context)
    {
        if (!AndroidPlatform.HasProperPerAppLanguageSupport())
        {
            return SeekerState.Language;
        }
            
#pragma warning disable CA1416
        var lm = (LocaleManager)context.GetSystemService(Context.LocaleService)!;
        var appLocales = lm.ApplicationLocales!;
        if (appLocales.IsEmpty)
        {
            return SeekerState.FieldLangAuto;
        }

        var locale = appLocales.Get(0);
        var lang = locale?.Language; // ex. fr, uk
        return lang == "pt" ? SeekerState.FieldLangPtBr : lang;
#pragma warning restore CA1416
    }
}
