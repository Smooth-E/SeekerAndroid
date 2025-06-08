using System;
using _Microsoft.Android.Resource.Designer;
using Android.Content;
using Android.Net;
using Android.Widget;
using Seeker.Managers;
using Seeker.Utils;

namespace Seeker;

public class ConnectionReceiver : BroadcastReceiver
{
    public override void OnReceive(Context context, Intent intent)
    {
        // this will say Wifi Disconnected, and then Mobile Connected. so just wait for the "Connected" one.            
        NetworkInfo netInfo = intent?.GetParcelableExtra("networkInfo") as NetworkInfo; 
        bool isConnected = NetworkHandoffDetector.ProcessEvent(netInfo);

        Logger.Debug("ConnectionReceiver.OnReceive");
        // these are just toasts letting us know the status of the network...

        string action = intent?.Action;
        if (action != null && action == ConnectivityManager.ConnectivityAction)
        {
            bool changed = SeekerApplication.SetNetworkState(context);
            if (changed)
            {
                Logger.Debug("metered state changed.. lets set up our handlers and inform server..");
                SharingManager.SetUnsetSharingBasedOnConditions();
                SeekerState.SharingStatusChangedEvent?.Invoke(null, new EventArgs());
            }
#if DEBUG
            ConnectivityManager cm = (ConnectivityManager)context.GetSystemService(Context.ConnectivityService);

            if (cm.ActiveNetworkInfo != null && cm.ActiveNetworkInfo.IsConnected)
            {
                Logger.Debug("info: " + cm.ActiveNetworkInfo.GetDetailedState());
                
                // TODO: Use a resource string
                context.ShowShortToast("Is Connected");
                
                var info = cm.GetNetworkInfo(ConnectivityType.Wifi);
                if (info.IsConnected)
                {
                    // TODO: Use a resource string
                    context.ShowShortToast("Is Connected Wifi");
                }
                info = cm.GetNetworkInfo(ConnectivityType.Mobile);
                if (info.IsConnected)
                {
                    // TODO: Use a resource string
                    context.ShowShortToast("Is Connected Mobile");
                }
            }
            else
            {
                Logger.Debug("info: " + cm.ActiveNetworkInfo?.GetDetailedState());
                    
                // TODO: Use a resource string
                var state = cm.ActiveNetworkInfo != null ? string.Empty : ", no network state";
                context.ShowShortToast($"Is Disconnected" + state);
            }
#endif
        }

    }

    public static bool DoWeHaveInternet()
    {
        ConnectivityManager cm = (ConnectivityManager) SeekerState.ActiveActivityRef
            .GetSystemService(Context.ConnectivityService);
        
        return cm.ActiveNetworkInfo != null && cm.ActiveNetworkInfo.IsConnected;
    }
}
