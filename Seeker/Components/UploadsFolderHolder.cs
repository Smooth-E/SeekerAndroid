using _Microsoft.Android.Resource.Designer;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using Seeker.Settings;

namespace Seeker.Components;

public class UploadsFolderHolder : RecyclerView.ViewHolder, View.IOnCreateContextMenuListener
{
    public UploadsFolderItem FolderView;
        
    public UploadsFolderHolder(View view) : base(view)
    {
        FolderView = (UploadsFolderItem)view;
        FolderView.ViewHolder = this;
        FolderView.SetOnCreateContextMenuListener(this);
    }

    public void OnCreateContextMenu(IContextMenu menu, View v, IContextMenuContextMenuInfo menuInfo)
    {
        var folderRowView = v as UploadsFolderItem;
        SettingsActivity.ContextMenuItem = folderRowView!.BoundItem;
        
        menu!.Add(0, 1, 0,
            SettingsActivity.ContextMenuItem.HasError()
                ? ResourceConstant.String.ViewErrorOptions
                : ResourceConstant.String.ViewFolderOptions);

        menu.Add(0, 2, 1, ResourceConstant.String.Remove);
    }
}
