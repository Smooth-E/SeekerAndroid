using Android.Widget;

namespace Seeker.Utils;

public static class ToastUi
{
    public static void Long(int messageResource)
    {
        ShowToast(messageResource, ToastLength.Long);
    }

    public static void Short(int messageResource)
    {
        ShowToast(messageResource, ToastLength.Short);
    }
    
    public static void Long(string message)
    {
        ShowToast(message, ToastLength.Long);
    }

    public static void Short(string message)
    {
        ShowToast(message, ToastLength.Short);
    }
    
    private static void ShowToast(int messageResource, ToastLength duration)
    {
        var activity = SeekerState.ActiveActivityRef;
        var message = SeekerState.ActiveActivityRef.GetString(messageResource);
        Toast.MakeText(activity, message, duration)?.Show();
    }

    private static void ShowToast(string message, ToastLength duration)
    {
        if (ThreadingUtils.OnUiThread())
        {
            Toast.MakeText(SeekerState.ActiveActivityRef, message, duration)?.Show();
        }
        else
        {
            SeekerState.ActiveActivityRef.RunOnUiThread(() =>
            {
                Toast.MakeText(SeekerState.ActiveActivityRef, message, duration)?.Show();
            });
        }
    }
}
