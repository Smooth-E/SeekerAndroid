using System;
using _Microsoft.Android.Resource.Designer;
using Android.Content;
using Android.Util;
using Android.Views;
using Android.Widget;
using Seeker.Settings;

namespace Seeker.Components;

// ReSharper disable once ClassNeverInstantiated.Global
public class UploadsFolderItem : RelativeLayout
{
    public UploadDirectoryInfo BoundItem;

    public UploadsFolderHolder ViewHolder;
    public SettingsActivity  SettingsActivity;
    private TextView viewFolderName;
    private ImageView viewFolderStatus;

    public UploadsFolderItem(Context context, IAttributeSet attrs, int defStyle) : base(context, attrs, defStyle)
    {
        const int resource = ResourceConstant.Layout.upload_folder_row;
        LayoutInflater.From(context)!.Inflate(resource, this, true);
        SetupChildren();
    }
    
    public UploadsFolderItem(Context context, IAttributeSet attrs) : base(context, attrs)
    {
        const int resource = ResourceConstant.Layout.upload_folder_row;
        LayoutInflater.From(context)!.Inflate(resource, this, true);
        SetupChildren();
    }

    public static void FolderLongClick(object sender, LongClickEventArgs e)
    {
        (sender as View)!.ShowContextMenu();
    }

    public void FolderClick(object sender, EventArgs e)
    {
        var adapter = (ViewHolder.BindingAdapter as UploadsRecyclerViewAdapter)!;
        var boundItem = (sender as UploadsFolderItem)!.ViewHolder.FolderView.BoundItem;
        adapter.settingsActivity.ShowDialogForUploadDir(boundItem);
    }

    public static UploadsFolderItem Inflate(ViewGroup parent)
    {
        var folderView = LayoutInflater.From(parent.Context)!;
        const int resource = ResourceConstant.Layout.upload_folder_row_dummy;
        return (UploadsFolderItem)folderView.Inflate(resource, parent, false);
    }

    public void SetupChildren()
    {
        viewFolderName = FindViewById<TextView>(ResourceConstant.Id.uploadFolderName);
        viewFolderStatus = FindViewById<ImageView>(ResourceConstant.Id.uploadFolderStatus);
    }

    public void SetItem(UploadDirectoryInfo item)
    {
        Clickable = SeekerState.SharingOn;
        LongClickable = SeekerState.SharingOn;

        BoundItem = item;
        viewFolderName.Text = string.IsNullOrEmpty(item.DisplayNameOverride)
            ? item.GetLastPathSegment()
            : $"{item.GetLastPathSegment()} ({item.DisplayNameOverride})";

        if (item.HasError())
        {
            viewFolderStatus.Visibility = ViewStates.Visible;
            viewFolderStatus.SetImageResource(ResourceConstant.Drawable.alert_circle_outline);
        }
        else if (item.IsHidden)
        {
            viewFolderStatus.Visibility = ViewStates.Visible;
            viewFolderStatus.SetImageResource(ResourceConstant.Drawable.hidden_lock_question);
        }
        else if (item.IsLocked)
        {
            viewFolderStatus.Visibility = ViewStates.Visible;
            viewFolderStatus.SetImageResource(ResourceConstant.Drawable.lock_icon);
        }
        else
        {
            viewFolderStatus.Visibility = ViewStates.Gone;
        }
    }
}
