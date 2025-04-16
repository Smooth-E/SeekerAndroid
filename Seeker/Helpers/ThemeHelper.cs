using System;
using _Microsoft.Android.Resource.Designer;
using Android.Content;

namespace Seeker.Helpers;

public static class ThemeHelper
{
    public const string CLASSIC_PURPLE = "Classic Purple";
    public const string GREY = "Grey";
    public const string BLUE = "Blue";
    public const string RED = "Red";
    public const string AMOLED_CLASSIC_PURPLE = "Amoled - Classic Purple";
    public const string AMOLED_GREY = "Amoled - Grey";

    public enum DayThemeType : ushort
    {
        ClassicPurple = 0,
        Red = 1,
        Blue = 2,
        Grey = 3,
    }

    public static int ToDayThemeProper(DayThemeType dayTheme)
    {
        return dayTheme switch
        {
            DayThemeType.ClassicPurple => ResourceConstant.Style.DefaultLight,
            DayThemeType.Grey => ResourceConstant.Style.DefaultDark_Grey, // TODO: Create a theme
            DayThemeType.Blue => ResourceConstant.Style.DefaultLight_Blue,
            DayThemeType.Red => ResourceConstant.Style.DefaultLight_Red,
            _ => throw new Exception("unknown")
        };
    }

    public enum NightThemeType : ushort
    {
        ClassicPurple = 0,
        Grey = 1,
        Blue = 2,
        Red = 3,
        AmoledClassicPurple = 4,
        AmoledGrey = 5
    }

    public static int ToNightThemeProper(NightThemeType nightTheme)
    {
        return nightTheme switch
        {
            NightThemeType.ClassicPurple => ResourceConstant.Style.DefaultDark,
            NightThemeType.Grey => ResourceConstant.Style.DefaultDark_Grey,
            NightThemeType.Blue => ResourceConstant.Style.DefaultDark_Blue,
            NightThemeType.Red => ResourceConstant.Style.DefaultDark_Blue // doesn't exist
            ,
            NightThemeType.AmoledClassicPurple => ResourceConstant.Style.Amoled,
            NightThemeType.AmoledGrey => ResourceConstant.Style.Amoled_Grey,
            _ => throw new Exception("unknown")
        };
    }
    
    public static int GetThemeInChosenDayNightMode(bool isNightMode, Context c)
    {
        Context contextToUse = c ?? SeekerState.ActiveActivityRef;
        if (contextToUse.Resources!.Configuration!.UiMode.HasFlag(Android.Content.Res.UiMode.NightYes))
        {
            if (isNightMode)
            {
                return ToNightThemeProper(SeekerState.NightModeVarient);
            }

            return SeekerState.NightModeVarient switch
            {
                NightThemeType.ClassicPurple => ToDayThemeProper(DayThemeType.ClassicPurple),
                NightThemeType.Blue => ToDayThemeProper(DayThemeType.Blue),
                _ => ToDayThemeProper(DayThemeType.ClassicPurple)
            };
        }

        if (!isNightMode)
        {
            return ToDayThemeProper(SeekerState.DayModeVarient);
        }

        switch (SeekerState.DayModeVarient)
        {
            case DayThemeType.ClassicPurple:
                return ToNightThemeProper(NightThemeType.ClassicPurple);
            case DayThemeType.Blue:
                return ToNightThemeProper(NightThemeType.Blue);
            default:
                return ToNightThemeProper(NightThemeType.ClassicPurple);
        }
    }
}
