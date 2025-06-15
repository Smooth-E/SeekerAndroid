using System;
using Android.Content;
using Android.Runtime;
using Android.Views;
using AndroidX.ViewPager.Widget;

namespace Seeker.Components;

/// Fixes this:
/// java.lang.IllegalArgumentException: pointerIndex out of range
/// at android.view.MotionEvent.nativeGetAxisValue(Native Method)
/// at android.view.MotionEvent.getX(MotionEvent.java:1981)
/// at AndroidX.Core.View.MotionEventCompatEclair.getX(MotionEventCompatEclair.java:32)
/// at AndroidX.Core.View.MotionEventCompat$EclairMotionEventVersionImpl.getX(MotionEventCompat.java:86)
/// at AndroidX.Core.View.MotionEventCompat.getX(MotionEventCompat.java:184)
/// at AndroidX.ViewPager.Widget.ViewPager.onInterceptTouchEvent(ViewPager.java:1339)

// ReSharper disable once UnusedType.Global - used in layouts
public class ViewPagerFixed : ViewPager
{
    public ViewPagerFixed(Context context) : base(context)
    { 
        // Intentional no-op
    }

    public ViewPagerFixed(Context context, Android.Util.IAttributeSet attrs) : base(context, attrs)
    { 
        // Intentional no-op
    }

    public ViewPagerFixed(IntPtr intPtr, JniHandleOwnership handle) : base(intPtr, handle)
    { 
        // Intentional no-op
    }

    public override bool OnTouchEvent(MotionEvent ev)
    {
        try
        {
            return base.OnTouchEvent(ev);
        }
        catch (Exception)
        { 
            // Intentional no-op
        }

        return false;
    }

    public override bool OnInterceptTouchEvent(MotionEvent ev)
    {
        try
        {
            return base.OnInterceptTouchEvent(ev); // this can throw a random 
        }
        catch (Exception)
        {
            // Intentional no-op
        }

        return false;
    }
}
