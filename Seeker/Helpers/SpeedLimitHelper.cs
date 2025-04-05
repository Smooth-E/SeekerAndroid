﻿using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Seeker.Utils;

namespace Seeker.Helpers;

public static class SpeedLimitHelper
{

    public static void RemoveDownloadUser(string username)
    {
        DownloadUserDelays.TryRemove(username, out _);
        DownloadLastAvgSpeed.TryRemove(username, out _);
    }

    public static void RemoveUploadUser(string username)
    {
        UploadUserDelays.TryRemove(username, out _);
        UploadLastAvgSpeed.TryRemove(username, out _);
    }

    public static System.Collections.Concurrent.ConcurrentDictionary<string, double> DownloadUserDelays = new(); //we need the double precision bc sometimes 1.1 cast to int will be the same number i.e. (int)(4*1.1)==4
    public static System.Collections.Concurrent.ConcurrentDictionary<string, double> DownloadLastAvgSpeed = new();

    public static System.Collections.Concurrent.ConcurrentDictionary<string, double> UploadUserDelays = new();
    public static System.Collections.Concurrent.ConcurrentDictionary<string, double> UploadLastAvgSpeed = new();
    public static Task OurDownloadGoverner(double currentSpeed, string username, CancellationToken cts)
    {
        try
        {
            if (SeekerState.SpeedLimitDownloadOn)
            {

                if (DownloadUserDelays.TryGetValue(username, out double msDelay))
                {
                    bool exists = DownloadLastAvgSpeed.TryGetValue(username, out double lastAvgSpeed); //this is here in the case of a race condition (due to RemoveUser)
                    if (exists && currentSpeed == lastAvgSpeed)
                    {
                        // do not adjust as we have not yet recalculated the average speed
                        return Task.Delay((int)msDelay, cts);
                    }

                    DownloadLastAvgSpeed[username] = currentSpeed;

                    double avgSpeed = currentSpeed;
                    if (!SeekerState.SpeedLimitDownloadIsPerTransfer && DownloadLastAvgSpeed.Count > 1)
                    {
                        // its threadsafe when using linq on concurrent dict itself.
                        avgSpeed = DownloadLastAvgSpeed.Sum((p) => p.Value);
                    }

                    if (avgSpeed > SeekerState.SpeedLimitDownloadBytesSec)
                    {

                        DownloadUserDelays[username] = msDelay = msDelay * 1.04;
                    }
                    else
                    {
                        DownloadUserDelays[username] = msDelay = msDelay * 0.96;
                    }

                    return Task.Delay((int)msDelay, cts);
                }
                // first time we need to guess a decent value
                // wait time if the loop took 0s with buffer size of 16kB
                // i.e. speed = 16kB / (delaytime). (delaytime in ms) = 1000 * 16,384 / (speed in bytes per second).
                var msDelaySeed = 1000 * 16384.0 / SeekerState.SpeedLimitDownloadBytesSec;
                DownloadUserDelays[username] = msDelaySeed;
                DownloadLastAvgSpeed[username] = currentSpeed;
                return Task.Delay((int)msDelaySeed, cts);

            }
            else
            {
                return Task.CompletedTask;
            }
        }
        catch (Exception ex)
        {
            Logger.FirebaseDebug("DL SPEED LIMIT EXCEPTION: " + ex.Message + ex.StackTrace);
            return Task.CompletedTask;
        }
    }

    //this is duplicated for speed.
    public static Task OurUploadGoverner(double currentSpeed, string username, CancellationToken cts)
    {
        try
        {
            if (SeekerState.SpeedLimitUploadOn)
            {

                if (UploadUserDelays.TryGetValue(username, out double msDelay))
                {
                    bool exists = UploadLastAvgSpeed.TryGetValue(username, out double lastAvgSpeed); //this is here in the case of a race condition (due to RemoveUser)
                    if (exists && currentSpeed == lastAvgSpeed)
                    {
#if DEBUG
                        //System.Console.WriteLine("UL dont update");
#endif
                        //do not adjust as we have not yet recalculated the average speed
                        return Task.Delay((int)msDelay, cts);
                    }

                    UploadLastAvgSpeed[username] = currentSpeed;

                    double avgSpeed = currentSpeed;
                    if (!SeekerState.SpeedLimitUploadIsPerTransfer && UploadLastAvgSpeed.Count > 1)
                    {

                        //its threadsafe when using linq on concurrent dict itself.
                        avgSpeed = UploadLastAvgSpeed.Sum((p) => p.Value);//Values.ToArray().Sum();
#if DEBUG
                        //System.Console.WriteLine("UL multiple total speed " + avgSpeed);
#endif
                    }

                    if (avgSpeed > SeekerState.SpeedLimitUploadBytesSec)
                    {
#if DEBUG
                        //System.Console.WriteLine("UL speed too high " + currentSpeed + "   " + msDelay);
#endif
                        UploadUserDelays[username] = msDelay = msDelay * 1.04;

                    }
                    else
                    {
#if DEBUG
                        //System.Console.WriteLine("UL speed too low " + currentSpeed + "   " + msDelay);
#endif
                        UploadUserDelays[username] = msDelay = msDelay * 0.96;
                    }

                    return Task.Delay((int)msDelay, cts);
                }
                else
                {
#if DEBUG
                    //System.Console.WriteLine("UL first time guess");
#endif
                    //first time we need to guess a decent value
                    //wait time if the loop took 0s with buffer size of 16kB i.e. speed = 16kB / (delaytime). (delaytime in ms) = 1000 * 16,384 / (speed in bytes per second).
                    double msDelaySeed = 1000 * 16384.0 / SeekerState.SpeedLimitUploadBytesSec;
                    UploadUserDelays[username] = msDelaySeed;
                    UploadLastAvgSpeed[username] = currentSpeed;
                    return Task.Delay((int)msDelaySeed, cts);
                }

            }
            else
            {
                return Task.CompletedTask;
            }
        }
        catch (Exception ex)
        {
            Logger.FirebaseDebug("UL SPEED LIMIT EXCEPTION: " + ex.Message + ex.StackTrace);
            return Task.CompletedTask;
        }
    }

}