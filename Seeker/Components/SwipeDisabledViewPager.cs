using Android.Content;
using Android.Util;
using Android.Views;
using AndroidX.ViewPager.Widget;

namespace Seeker.Components;

// TODO: Use ViewPager2 with disabled user input instead
// ReSharper disable once UnusedType.Global - used in layouts
public class SwipeDisabledViewPager(Context context, IAttributeSet attrs) : ViewPager(context, attrs)
{
    public bool SwipeEnabled = false;

    public override bool OnTouchEvent(MotionEvent motionEvent)
    {
        return SwipeEnabled && base.OnTouchEvent(motionEvent);
    }

    public override bool OnInterceptTouchEvent(MotionEvent motionEvent)
    {
        return SwipeEnabled && base.OnInterceptTouchEvent(motionEvent);
    }
}
