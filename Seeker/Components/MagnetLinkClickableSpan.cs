using _Microsoft.Android.Resource.Designer;
using Android.Content;
using Android.Views;
using Seeker.Utils;

namespace Seeker.Components;

public class MagnetLinkClickableSpan(Context context, string textClicked) : Android.Text.Style.ClickableSpan
{
    public override void OnClick(View widget)
    {
        Logger.Debug("magnet link click");
        try
        {
            var followLink = new Intent(Intent.ActionView);
            followLink.SetData(Android.Net.Uri.Parse(textClicked));
            SeekerState.ActiveActivityRef.StartActivity(followLink);
        }
        catch (ActivityNotFoundException e)
        {
            Logger.Debug("No magnet link handlers found." + " " + e.Message + " " + e.StackTrace);
            context.ShowLongToast(ResourceConstant.String.message_no_magnet_link_handlers);
        }
    }
}
