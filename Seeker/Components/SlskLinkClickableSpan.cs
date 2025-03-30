using Android.Views;
using Seeker.Utils;

namespace Seeker.Components;

public class SlskLinkClickableSpan(string textClicked) : Android.Text.Style.ClickableSpan
{
    public override void OnClick(View widget)
    {
        Logger.Debug("slsk link click");
        CommonHelpers.SlskLinkClickedData = textClicked;
        CommonHelpers.ShowSlskLinkContextMenu = true;
        SeekerState.ActiveActivityRef.RegisterForContextMenu(widget);
        SeekerState.ActiveActivityRef.OpenContextMenu(widget);
        SeekerState.ActiveActivityRef.UnregisterForContextMenu(widget);
    }
}
