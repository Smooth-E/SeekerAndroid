using _Microsoft.Android.Resource.Designer;
using Android.Animation;
using Android.Views;
using Google.Android.Material.BottomNavigation;

namespace Seeker;

public class BottomNavigationViewAnimationListener : Java.Lang.Object, Animator.IAnimatorListener
{
    public void OnAnimationCancel(Animator animation)
    {
        // Intentional no-op
    }

    public void OnAnimationEnd(Animator animation)
    {
        // TODO: PAss main activity reference into the constructor instead
        var mainActivity = SeekerState.MainActivityRef;
        mainActivity.FindViewById<BottomNavigationView>(ResourceConstant.Id.navigation)!.Visibility = ViewStates.Gone;
    }

    public void OnAnimationRepeat(Animator animation)
    {
        // Intentional no-op
    }

    public void OnAnimationStart(Animator animation)
    {
        // Intentional no-op
    }
}
