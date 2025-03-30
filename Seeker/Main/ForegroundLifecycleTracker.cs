using System;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.OS;
using AndroidX.AppCompat.App;
using AndroidX.Fragment.App;
using Seeker.Helpers;
using Seeker.Utils;

namespace Seeker;

public class ForegroundLifecycleTracker : Java.Lang.Object, Application.IActivityLifecycleCallbacks
{
    public volatile static string DiagLastStarted = string.Empty;
    public volatile static string DiagLastStopped = string.Empty;
    
    public static int NumberOfActiveActivities = 0;
    public static System.Timers.Timer AutoAwayTimer = null;
    
    public static bool HasAppEverStarted = false;
    
    //basically this is for the very first time the app gets started so that we 
    //can launch a foreground service while we are in the foreground...
    void Application.IActivityLifecycleCallbacks.OnActivityCreated(Activity activity, Bundle savedInstanceState)
    {

    }

    void Application.IActivityLifecycleCallbacks.OnActivityDestroyed(Activity activity)
    {

    }

    void Application.IActivityLifecycleCallbacks.OnActivityPaused(Activity activity)
    {

    }

    void Application.IActivityLifecycleCallbacks.OnActivityResumed(Activity activity)
    {
        if (!HasAppEverStarted)
        {
            HasAppEverStarted = true;
            try
            {
                if (SeekerState.StartServiceOnStartup)
                {
                    Intent seekerKeepAliveService = new Intent(activity, typeof(SeekerKeepAliveService));
                    // so so so many people are in background when this starts....
                    activity.StartService(seekerKeepAliveService);
                }
            }
            catch (Exception e)
            {                  
                // this still happened if started from visual studio...
                // I don't know how since resume is literally the indicator of foreground...
                // that being said, the OnResume logic typically does work
                // even if started from visual studio on locked phone.
                // it's just that sometimes this almost gets like forced.... but not sure how to reproduce...
                
                try
                {
                    String message = e.Message + e.StackTrace;
                    if (activity is AppCompatActivity appCompatActivity)
                    {
                        bool? foreground = appCompatActivity.IsResumed();
                        if (foreground == null)
                        {
                            Logger.FirebaseDebug("Unknown seeker keep alive cannot be started: " + message);
                        }
                        else if (foreground.Value)
                        {
                            Logger.FirebaseDebug("FOREGROUND seeker keep alive cannot be started: " + message);
                        }
                        else
                        {
                            Logger.FirebaseDebug("BACKGROUND seeker keep alive cannot be started: " + message);
                        }
                    }
                    else
                    {
                        Logger.FirebaseDebug("seeker keep alive cannot be started: " + message);
                    }
                }
                catch
                {
                    // Intentional no-op
                }
            }
        }
    }

    void Application.IActivityLifecycleCallbacks.OnActivitySaveInstanceState(Activity activity, Bundle outState)
    {

    }

    void Application.IActivityLifecycleCallbacks.OnActivityStarted(Activity activity)
    {
        SeekerState.ActiveActivityRef = activity as FragmentActivity;
        if (SeekerState.ActiveActivityRef == null)
        {
            Logger.FirebaseDebug("OnActivityStarted activity is null!");
        }

        DiagLastStarted = activity.GetType().Name;
        Logger.Debug("OnActivityStarted " + DiagLastStarted);

        NumberOfActiveActivities++;
        
        // we are just coming back alive.
        if (NumberOfActiveActivities == 1)
        {
            Logger.Debug("We are back!");
            if (AutoAwayTimer != null)
            {
                AutoAwayTimer.Stop();
            }
        }

        if (SeekerState.PendingStatusChangeToAwayOnline == SeekerState.PendingStatusChange.AwayPending)
        {
            SeekerState.PendingStatusChangeToAwayOnline = SeekerState.PendingStatusChange.NothingPending;
        }

        if (SeekerState.OurCurrentStatusIsAway)
        {
            Logger.Debug("Our current status is away, lets set it back to online!");
            
            // set back to online
            MainActivity.SetStatusApi(false);
        }
    }

    private void TryToReconnect()
    {
        try
        {
            Logger.Debug("! TryToReconnect (on app resume) !");

            if (SeekerApplication.ReconnectSteppedBackOffThreadIsRunning)
            {
                // set and let it run.
                Logger.Debug("In progress, so .Set to let the next one run.");
                SeekerApplication.ReconnectAutoResetEvent.Set();
            }
            else
            {
                System.Threading.ThreadPool.QueueUserWorkItem(_ =>
                {

                    Task t = SeekerApplication.ConnectAndPerformPostConnectTasks(
                        SeekerState.Username, 
                        SeekerState.Password
                    );
#if DEBUG
                    t.ContinueWith(task =>
                    {

                        if (task.IsFaulted)
                        {
                            Logger.Debug("TryToReconnect FAILED");
                        }
                        else
                        {
                            Logger.Debug("TryToReconnect SUCCESSFUL");
                        }

                    });
#endif
                });
            }
        }
        catch (Exception e)
        {
            Logger.FirebaseDebug("TryToReconnect Failed " + e.Message + e.StackTrace);
        }
    }

    void Application.IActivityLifecycleCallbacks.OnActivityStopped(Activity activity)
    {
        DiagLastStopped = activity.GetType().Name.ToString();
        Logger.Debug("OnActivityStopped " + DiagLastStopped);

        NumberOfActiveActivities--;
        
        // if this is 0 then app is in background,
        // or screen is locked, user at home screen, other app in front, etc.
        if (NumberOfActiveActivities == 0 && SeekerState.AutoAwayOnInactivity)
        {
            Logger.Debug("We are away!");
            if (AutoAwayTimer == null)
            {
                AutoAwayTimer = new System.Timers.Timer(1000 * 60 * 5); // 5 minutes
                AutoAwayTimer.AutoReset = false; // raise event just once.
                AutoAwayTimer.Elapsed += AutoAwayTimer_Elapsed;
            }
            AutoAwayTimer.Start();
        }
    }

    private void AutoAwayTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
    {
        Logger.Debug("We were away for the interval specified.  time to set status to away.");
        MainActivity.SetStatusApi(true);
    }

    public static bool IsBackground()
    {
        return NumberOfActiveActivities == 0;
    }
}
