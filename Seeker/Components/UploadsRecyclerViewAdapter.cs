using System.Collections.Generic;
using Android.Views;
using AndroidX.RecyclerView.Widget;
using Seeker.Settings;

namespace Seeker.Components;

public class UploadsRecyclerViewAdapter : RecyclerView.Adapter
{
    public override int ItemCount => localDataSet.Count;
    public SettingsActivity settingsActivity;
    
    private readonly List<UploadDirectoryInfo> localDataSet;
    
    public UploadsRecyclerViewAdapter(SettingsActivity activity, List<UploadDirectoryInfo> ti)
    {
        settingsActivity = activity;
        localDataSet = ti;
    }

    public override void OnBindViewHolder(RecyclerView.ViewHolder holder, int position)
    {
        (holder as UploadsFolderHolder)!.FolderView.SetItem(localDataSet[position]);
    }

    // so view Type is a real thing that the recycler adapter knows about.
    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        var view = UploadsFolderItem.Inflate(parent);
        view.SetupChildren();
        view.SettingsActivity = settingsActivity;
        view.Click += view.FolderClick;
        view.LongClick += UploadsFolderItem.FolderLongClick;
        return new UploadsFolderHolder(view);
    }
}
