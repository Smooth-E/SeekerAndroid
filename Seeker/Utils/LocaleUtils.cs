namespace Seeker.Utils;

public static class LocaleUtils
{
    /// <summary>converts say "pt-rBR" to "pt-BR"</summary>
    public static string FormatLocaleFromResourcesToStandard(string locale)
    {
        if (locale.Length == 6 && locale.Contains("-r"))
        {
            return locale.Replace("-r", "-");
        }

        return locale;
    }
    
    public static Java.Util.Locale LocaleFromString(string localeString)
    {
        Java.Util.Locale locale = null;
        if (localeString.Contains("-r"))
        {
            var parts = localeString.Replace("-r", "-").Split('-');
            locale = new Java.Util.Locale(parts[0], parts[1]);
        }
        else
        {
            locale = new Java.Util.Locale(localeString);
        }
        return locale;
    }
    
    public static string ToVariantAwareString(this Java.Util.Locale locale)
    {
        //"en" ""
        //"pt" "br"
        if (string.IsNullOrEmpty(locale.Variant))
        {
            return locale.Language;
        }

        return locale.Language + "-r" + locale.Variant.ToUpper();
    }
}
