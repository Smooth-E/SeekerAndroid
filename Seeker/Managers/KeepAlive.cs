using System;
using System.Timers;
using Android.Content;
using Android.Net;
using Android.Net.Wifi;
using Android.OS;
using Seeker.Utils;

namespace Seeker.Managers;

public static class KeepAlive
{
    public static PowerManager.WakeLock CpuKeepAlive_Transfer;
    public static WifiManager.WifiLock WifiKeepAlive_Transfer;
    public static Timer KeepAliveInactivityKillTimer;
    
    // TODO: Possible to replace with an event?
    public static void KeepAliveInactivityKillTimerElapsed(object sender, ElapsedEventArgs e)
    {
        if (CpuKeepAlive_Transfer != null)
        {
            CpuKeepAlive_Transfer.Release();
        }

        if (WifiKeepAlive_Transfer != null)
        {
            WifiKeepAlive_Transfer.Release();
        }

        KeepAliveInactivityKillTimer.Stop();
    }

    public static void Initialize(Context context, bool ignoreExceptions = true)
    {
        try
        {
            if (CpuKeepAlive_Transfer == null)
            {
                var powerManager = context.GetSystemService(Context.PowerService) as PowerManager;
                CpuKeepAlive_Transfer =
                    powerManager?.NewWakeLock(WakeLockFlags.Partial, "Seeker Download CPU_Keep_Alive");

                CpuKeepAlive_Transfer?.SetReferenceCounted(false);
            }

            if (WifiKeepAlive_Transfer == null)
            {
                var wifiManager = context.GetSystemService(Context.WifiService) as WifiManager;
                WifiKeepAlive_Transfer =
                    wifiManager?.CreateWifiLock(WifiMode.FullHighPerf, "Seeker Download Wifi_Keep_Alive");

                WifiKeepAlive_Transfer?.SetReferenceCounted(false);
            }
        }
        catch (Exception exception)
        {
            if (!ignoreExceptions)
            {
                throw;
            }
            
            Logger.FirebaseDebug("error init keepalives: " + exception.Message + exception.StackTrace);
        }

        if (KeepAliveInactivityKillTimer == null)
        {
            // kill after 10 minutes of no activity
            // remember that this is a fallback. for when foreground service is still running
            // but nothing is happening otherwise.
            KeepAliveInactivityKillTimer = new Timer(60 * 1000 * 10);

            KeepAliveInactivityKillTimer.Elapsed += KeepAliveInactivityKillTimerElapsed;
            KeepAliveInactivityKillTimer.AutoReset = false;
        }
    }
    
    public static void AcquireTransferLocksAndResetTimer()
    {
        if (CpuKeepAlive_Transfer is { IsHeld: false })
        {
            CpuKeepAlive_Transfer.Acquire();
        }
        
        if (WifiKeepAlive_Transfer is { IsHeld: false })
        {
            WifiKeepAlive_Transfer.Acquire();
        }

        if (KeepAliveInactivityKillTimer != null)
        {
            KeepAliveInactivityKillTimer.Stop(); // can be null
            KeepAliveInactivityKillTimer.Start(); // reset the timer
        }
        else
        {
            KeepAliveInactivityKillTimer = new Timer(60 * 1000 * 10); // kill after 10 minutes of no activity
            
            // remember that this is a fallback. for when foreground service
            // is still running but nothing is happening otherwise.
            KeepAliveInactivityKillTimer.Elapsed += KeepAliveInactivityKillTimerElapsed;
            KeepAliveInactivityKillTimer.AutoReset = false;
        }
    }
    
    public static void ReleaseTransferLocksIfServicesComplete()
    {
        //if all transfers are done..
        if (!SeekerState.UploadKeepAliveServiceRunning && !SeekerState.DownloadKeepAliveServiceRunning)
        {
            if (CpuKeepAlive_Transfer != null)
            {
                CpuKeepAlive_Transfer.Release();
            }
            if (WifiKeepAlive_Transfer != null)
            {
                WifiKeepAlive_Transfer.Release();
            }
            if (KeepAliveInactivityKillTimer != null)
            {
                KeepAliveInactivityKillTimer.Stop();
            }
        }
    }

    public static void RestartInactivityKillTimer(bool catchExceptions = true)
    {
        try
        {
            KeepAliveInactivityKillTimer.Stop(); // lot of null ref here...
            KeepAliveInactivityKillTimer.Start();
        }
        catch (Exception exception)
        {
            if (!catchExceptions)
            {
                throw;
            }
            
            // remember at worst the locks will get released early which is fine.
            Logger.FirebaseDebug("timer issue2: " + exception.Message + exception.StackTrace);
        }
    }
}
