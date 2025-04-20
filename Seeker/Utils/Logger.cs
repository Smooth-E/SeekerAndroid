using System;
using Android.Util;
using Seeker.Managers;

namespace Seeker.Utils;

public static class Logger
{
    // This is set after creating the Firebase application
    public static bool CrashlyticsEnabled = true;
    
    public static void Debug(string msg)
    {
        DiagnosticFile.AppendMessageToDiagFile(msg);
        Log.Debug("Seeker_Debug", msg);
    }
    
    public static void FirebaseError(string msg, Exception e)
    {
        FirebaseDebug($"{msg} msg: {e.Message} stack: {e.StackTrace}");
    }

    public static void FirebaseDebug(string msg)
    {
        DiagnosticFile.AppendMessageToDiagFile(msg);
#if !IzzySoft
        if (CrashlyticsEnabled)
        {
            Firebase.Crashlytics.FirebaseCrashlytics.Instance.RecordException(new Java.Lang.Throwable(msg));
        }
#endif
        Log.Debug("Seeker_Debug", msg);
    }

    public static void FirebaseInfo(string msg)
    {
        DiagnosticFile.AppendMessageToDiagFile(msg);
#if !IzzySoft
        if (CrashlyticsEnabled)
        {
            Firebase.Crashlytics.FirebaseCrashlytics.Instance.Log(msg);
        }
#endif
        Log.Debug("Seeker_Debug", msg);
    }
}
