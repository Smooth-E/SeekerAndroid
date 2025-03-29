using System;
using Android.Content;
using Android.Net;
using Android.Widget;

namespace Seeker;

public class ConnectionReceiver : BroadcastReceiver
{
    public override void OnReceive(Context context, Intent intent)
    {
        // this will say Wifi Disconnected, and then Mobile Connected. so just wait for the "Connected" one.            
        NetworkInfo netInfo = intent?.GetParcelableExtra("networkInfo") as NetworkInfo; 
        bool isConnected = NetworkHandoffDetector.ProcessEvent(netInfo);

        MainActivity.LogDebug("ConnectionReceiver.OnReceive");
        // these are just toasts letting us know the status of the network...

        string action = intent?.Action;
        if (action != null && action == ConnectivityManager.ConnectivityAction)
        {
            bool changed = SeekerApplication.SetNetworkState(context);
            if (changed)
            {
                MainActivity.LogDebug("metered state changed.. lets set up our handlers and inform server..");
                MainActivity.SetUnsetSharingBasedOnConditions(true);
                SeekerState.SharingStatusChangedEvent?.Invoke(null, new EventArgs());
            }
#if DEBUG
            ConnectivityManager cm = (ConnectivityManager)context.GetSystemService(Context.ConnectivityService);

            if (cm.ActiveNetworkInfo != null && cm.ActiveNetworkInfo.IsConnected)
            {
                MainActivity.LogDebug("info: " + cm.ActiveNetworkInfo.GetDetailedState());
                SeekerApplication.ShowToast("Is Connected", ToastLength.Long);
                NetworkInfo info = cm.GetNetworkInfo(ConnectivityType.Wifi);
                if (info.IsConnected)
                {
                    SeekerApplication.ShowToast("Is Connected Wifi", ToastLength.Long);
                }
                info = cm.GetNetworkInfo(ConnectivityType.Mobile);
                if (info.IsConnected)
                {
                    SeekerApplication.ShowToast("Is Connected Mobile", ToastLength.Long);
                }
            }
            else
            {
                if (cm.ActiveNetworkInfo != null)
                {
                    MainActivity.LogDebug("info: " + cm.ActiveNetworkInfo.GetDetailedState());
                    SeekerApplication.ShowToast("Is Disconnected", ToastLength.Long);
                }
                else
                {
                    MainActivity.LogDebug("info: Is Disconnected(null)");
                    SeekerApplication.ShowToast("Is Disconnected (null)", ToastLength.Long);
                }
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
