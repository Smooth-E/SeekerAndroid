using Android.OS;

namespace Seeker.Utils;

public static class ThreadingUtils
{
    // TODO: Rename to IsOnUiThread
    public static bool OnUiThread()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.M) // 23
        {
            return Looper.MainLooper != null && Looper.MainLooper.IsCurrentThread;
        }
        
        return Looper.MainLooper != null && Looper.MainLooper.Thread == Java.Lang.Thread.CurrentThread();
    }
}
