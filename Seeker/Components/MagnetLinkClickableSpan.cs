using Android.Content;
using Android.Views;
using Android.Widget;
using Seeker.Utils;

namespace Seeker.Components;

public class MagnetLinkClickableSpan(string textClicked) : Android.Text.Style.ClickableSpan
{
    public override void OnClick(View widget)
    {
        Logger.Debug("magnet link click");
        try
        {
            Intent followLink = new Intent(Intent.ActionView);
            followLink.SetData(Android.Net.Uri.Parse(textClicked));
            SeekerState.ActiveActivityRef.StartActivity(followLink);
        }
        catch (ActivityNotFoundException e)
        {
            const string message = "No Activity Found to handle Magnet Links.  Please Install a BitTorrent Client.";
            Logger.Debug(message + " " + e.Message + " " + e.StackTrace);
            Toast.MakeText(SeekerState.ActiveActivityRef, message, ToastLength.Long)?.Show();
        }
    }
}
