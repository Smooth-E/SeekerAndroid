using System;
using _Microsoft.Android.Resource.Designer;
using Android.Content;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using Seeker.Utils;

namespace Seeker.Components;

// ReSharper disable once UnusedType.Global - used in layouts
public class MaterialProgressBarPassThrough : LinearLayout
{
    private bool disposed;
    
    public MaterialProgressBarPassThrough(Context context, IAttributeSet attrs, int defStyle) 
        : base(context, attrs, defStyle)
    {
        // Intentional no-op
    }
    
    public MaterialProgressBarPassThrough(Context context, IAttributeSet attrs) : base(context, attrs)
    {
        Logger.Debug("MaterialProgressBarPassThrough disposed" + disposed);
        var wrapper = new ContextThemeWrapper(context, ResourceConstant.Style.MaterialThemeForChip);
        const int resource = ResourceConstant.Layout.material_progress_bar_pass_through;
        LayoutInflater.From(wrapper)?.Inflate(resource, this, true);
    }

    public MaterialProgressBarPassThrough(IntPtr handle, JniHandleOwnership transfer) : base(handle, transfer)
    {
        // Intentional no-op
    }
    
    // ReSharper disable once IntroduceOptionalParameters.Global
    public MaterialProgressBarPassThrough(Context context) : this(context, null)
    {
        // Intentional no-op
    }

    protected override void Dispose(bool disposing)
    {
        disposed = true;
        base.Dispose(disposing);
    }
}
