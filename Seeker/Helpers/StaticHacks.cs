﻿using Android.Views;

namespace Seeker.Helpers;

public static class StaticHacks
{
    public static bool LoggingIn = false;
    public static bool UpdateUI = false;
    public static View RootView = null;
    public static AndroidX.Fragment.App.Fragment LoginFragment = null;
    public static TransfersFragment TransfersFrag = null;
}
