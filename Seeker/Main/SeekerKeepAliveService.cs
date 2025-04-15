﻿using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Widget;
using Seeker.Helpers;
using Seeker.Utils;
using System;
using Seeker.Main;

namespace Seeker
{
    //Services are natural singletons. There will be 0 or 1 instance of your service at any given time.
    [Service(Name = "com.companyname.andriodapp1.SeekerKeepAliveService", ForegroundServiceType = Android.Content.PM.ForegroundService.TypeDataSync)]
    public class SeekerKeepAliveService : Service
    {
        public const int NOTIF_ID = 121;
        public const string CHANNEL_ID = "seeker keep alive id";
        public const string CHANNEL_NAME = "Seeker Keep Alive Service";

        public static Android.Net.Wifi.WifiManager.WifiLock WifiKeepAlive_FullService = null;
        public static PowerManager.WakeLock CpuKeepAlive_FullService = null;


        public override IBinder OnBind(Intent intent)
        {
            return null; //does not allow binding. 
        }



        public static Notification CreateNotification(Context context)
        {
            Intent notifIntent = new Intent(context, typeof(MainActivity));
            notifIntent.AddFlags(ActivityFlags.SingleTop);
            PendingIntent pendingIntent =
                PendingIntent.GetActivity(context, 0, notifIntent, CommonHelpers.AppendMutabilityIfApplicable((PendingIntentFlags)0, true));
            //no such method takes args CHANNEL_ID in API 25. API 26 = 8.0 which requires channel ID.
            //a "channel" is a category in the UI to the end user.
            return CommonHelpers.CreateNotification(context, pendingIntent, CHANNEL_ID, context.GetString(Resource.String.seeker_running), context.GetString(Resource.String.seeker_running_content), true, true, true);
        }


        [return: GeneratedEnum]
        public override StartCommandResult OnStartCommand(Intent intent, [GeneratedEnum] StartCommandFlags flags, int startId)
        {
            if (intent.IsShuttingDown())
            {
                this.StopSelf();
                return StartCommandResult.NotSticky;
            }
            Logger.FirebaseInfo("keep alive service started...");
            SeekerState.IsStartUpServiceCurrentlyRunning = true;

            CommonHelpers.CreateNotificationChannel(this, CHANNEL_ID, CHANNEL_NAME);//in android 8.1 and later must create a notif channel else get Bad Notification for startForeground error.
            Notification notification = CreateNotification(this);


            //System.Threading.Thread.Sleep(4000); // bug exposer - does help reproduce on samsung galaxy

            //if (foreground)
            //{
            try
            {
                StartForeground(NOTIF_ID, notification); // this can crash if started in background... (and firebase does say they started in background)
            }
            catch (Exception e)
            {
                // this exception is in fact catchable.. though "startForegroundService() did not then call Service.startForeground()" is supposed to cause issues
                //   in my case it did not.
                SeekerState.IsStartUpServiceCurrentlyRunning = false;
                bool? foreground = SeekerState.ActiveActivityRef?.IsResumed();
                Logger.FirebaseDebug($"StartForeground issue: is foreground: {foreground} {e.Message} {e.StackTrace}");
#if DEBUG
                ApplicationContext?.ShowLongToast($"StartForeground failed - is foreground: {foreground}");
#endif
            }

            try
            {
                if (CpuKeepAlive_FullService != null && !CpuKeepAlive_FullService.IsHeld)
                {
                    CpuKeepAlive_FullService.Acquire();
                    Logger.FirebaseInfo("CpuKeepAlive acquire");
                }
                if (WifiKeepAlive_FullService != null && !WifiKeepAlive_FullService.IsHeld)
                {
                    WifiKeepAlive_FullService.Acquire();
                    Logger.FirebaseInfo("WifiKeepAlive acquire");
                }
            }
            catch (System.Exception e)
            {
                Logger.FirebaseInfo("keepalive issue: " + e.Message + e.StackTrace);
                Logger.FirebaseDebug("keepalive issue: " + e.Message + e.StackTrace);
            }
            //}
            //runs indefinitely until stop.

            return StartCommandResult.Sticky;
        }

        public override void OnDestroy()
        {
            SeekerState.IsStartUpServiceCurrentlyRunning = false;
            if (CpuKeepAlive_FullService != null && CpuKeepAlive_FullService.IsHeld)
            {
                CpuKeepAlive_FullService.Release();
                Logger.FirebaseInfo("CpuKeepAlive release");
            }
            else if (CpuKeepAlive_FullService == null)
            {
                Logger.FirebaseDebug("CpuKeepAlive is null");
            }
            else if (!CpuKeepAlive_FullService.IsHeld)
            {
                Logger.FirebaseDebug("CpuKeepAlive not held");
            }
            if (WifiKeepAlive_FullService != null && WifiKeepAlive_FullService.IsHeld)
            {
                WifiKeepAlive_FullService.Release();
                Logger.FirebaseInfo("WifiKeepAlive release");
            }
            else if (WifiKeepAlive_FullService == null)
            {
                Logger.FirebaseDebug("WifiKeepAlive is null");
            }
            else if (!WifiKeepAlive_FullService.IsHeld)
            {
                Logger.FirebaseDebug("WifiKeepAlive not held");
            }

            base.OnDestroy();
        }

        public override void OnCreate()
        {
            base.OnCreate();
        }
    }



}