using System;
using Android.Net;
using Seeker.Utils;

namespace Seeker;

/// <summary>
/// When we switch from wifi to data or vice versa, we want to try to continue our downloads and uploads seamlessly.
/// We try to detect this event (as a netinfo disconnect (from old network)
/// and then netinfo connect (with new network)).
/// Then in the transfers failure we check if a recent* network handoff occured causing the remote connection to close
/// And if so we retry the transfer.  *recent is tough to determine since you can still read from the pipe for a
/// bit of time even if wifi is turned off.
/// </summary>
public static class NetworkHandoffDetector
{
    public static bool NetworkSuccessfullyHandedOff = false;
    private static DateTime DisconnectedTime = DateTime.MinValue;
    private static DateTime NetworkHandOffTime = DateTime.MinValue;
    
    /// <summary>
    /// 
    /// </summary>
    /// <param name="netInfo"></param>
    /// <returns>true if connected</returns>
    public static bool ProcessEvent(NetworkInfo netInfo)
    {
        if (netInfo == null)
        {

        }
        else
        {
            if (netInfo.IsConnected)
            {
                if ((DateTime.UtcNow - DisconnectedTime).TotalSeconds < 2.0) // in practice .2s or less...
                {
                    Logger.Debug("total seconds..." + (DateTime.UtcNow - DisconnectedTime).TotalSeconds);
                    NetworkHandOffTime = DateTime.UtcNow;
                    NetworkSuccessfullyHandedOff = true;
                }
                
                return true;
            }
            
            NetworkSuccessfullyHandedOff = false;
            DisconnectedTime = DateTime.UtcNow;
        }
        
        return false;
    }

    public static bool HasHandoffOccuredRecently()
    {
        if (!NetworkSuccessfullyHandedOff)
        {
            return false;
        }
    
        Logger.Debug("total seconds..." + (DateTime.UtcNow - NetworkHandOffTime).TotalSeconds);
        
        // in practice, we can keep reading from the stream for a while so 30s is reasonable.
        return (DateTime.UtcNow - NetworkHandOffTime).TotalSeconds < 30.0;
    }
}
