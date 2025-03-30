using System;
using Android.OS;
using Android.Views;
using Seeker.Utils;

namespace Seeker;

// TODO: This class seems to be unused, consider removing
public class ListenerKeyboard : Java.Lang.Object, ViewTreeObserver.IOnGlobalLayoutListener
{
    // oh, so it just needs the Java.Lang.Object and then you can make it like a Java Anon Class where you only
    // implement that one thing that you truly need
    // Since C# doesn't support anonymous classes

    private bool alreadyOpen;
    private const int defaultKeyboardHeightDP = 100;

    private int EstimatedKeyboardDP = defaultKeyboardHeightDP +
                                      (Build.VERSION.SdkInt >= BuildVersionCodes.Lollipop ? 48 : 0); //api 21

    private View parentView;
    private Android.Graphics.Rect rect = new Android.Graphics.Rect();

    public static EventHandler<bool> KeyBoardVisibilityChanged;
    
    public ListenerKeyboard(View _parentView)
    {
        parentView = _parentView;
    }

    // this is technically overridden and it will be called,
    // its just weird due to the java IJavaObject, IDisposable, IJavaPeerable stuff.
    public void OnGlobalLayout()
    {
        int estimatedKeyboardHeight = (int)Android.Util.TypedValue.ApplyDimension(
            Android.Util.ComplexUnitType.Dip,
            EstimatedKeyboardDP,
            parentView.Resources.DisplayMetrics
        );

        parentView.GetWindowVisibleDisplayFrame(rect); //getWindowVisibleDisplayFrame(rect);

        int heightDiff = parentView.RootView.Height - (rect.Bottom - rect.Top);
        bool isShown = heightDiff >= estimatedKeyboardHeight;

        if (isShown == alreadyOpen)
        {
            Logger.Debug("Keyboard state - Ignoring global layout change...");
            return;
        }

        alreadyOpen = isShown;
        KeyBoardVisibilityChanged?.Invoke(null, isShown);
    }
}
