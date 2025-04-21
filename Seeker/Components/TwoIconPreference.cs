using _Microsoft.Android.Resource.Designer;
using Android.Content;
using Android.Graphics.Drawables;
using Android.Util;
using Android.Widget;
using AndroidX.Preference;
using JetBrains.Annotations;

// ReSharper disable IntroduceOptionalParameters.Global
//     - optional constructor args are not supported when transitioning to Java
// ReSharper disable UnusedType.Global
//     - class is instantiated through XML only

namespace Seeker.Components;

public class TwoIconPreference : Preference
{
    private ImageView secondaryIconView;
    private readonly Drawable secondaryIcon;

    public TwoIconPreference([NotNull] Context context, IAttributeSet attrs, int defStyleAttr, int defStyleRes) 
        : base(context, attrs, defStyleAttr, defStyleRes)
    {
        WidgetLayoutResource = ResourceConstant.Layout.preference_two_icon_widget;
        
        var styleable = ResourceConstant.Styleable.TwoIconPreference;
        var obtainedAttributes = context.ObtainStyledAttributes(attrs, styleable, defStyleAttr, defStyleRes);

        const int index = ResourceConstant.Styleable.TwoIconPreference_secondaryIcon;
        secondaryIcon = obtainedAttributes.GetDrawable(index);
        
        obtainedAttributes.Recycle();
    }

    public TwoIconPreference([NotNull] Context context, IAttributeSet attrs, int defStyleAttr)
        : this(context, attrs, defStyleAttr, 0)
    {
        // Intentional no-op
    }

    public TwoIconPreference([NotNull] Context context, IAttributeSet attrs) 
        : this(context, attrs, ResourceConstant.Attribute.twoIconPreferenceStyle)
    {
        // Intentional no-op
    }
    
    public TwoIconPreference([NotNull] Context context) : this(context, null)
    {
        // Intentional no-op
    }

    public override void OnBindViewHolder(PreferenceViewHolder holder)
    {
        base.OnBindViewHolder(holder);
        
        secondaryIconView = holder.FindViewById(ResourceConstant.Id.secondary_icon) as ImageView;
        secondaryIconView?.SetImageDrawable(secondaryIcon);
    }
}
