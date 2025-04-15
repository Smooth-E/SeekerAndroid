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
        (holder as SettingsActivity.RecyclerViewFolderHolder)!.folderView.setItem(localDataSet[position]);
    }

    // so view Type is a real thing that the recycler adapter knows about.
    public override RecyclerView.ViewHolder OnCreateViewHolder(ViewGroup parent, int viewType)
    {
        SettingsActivity.RecyclerViewFolderView view = SettingsActivity.RecyclerViewFolderView.inflate(parent);
        view.setupChildren();
        view.SettingsActivity = settingsActivity;
        view.Click += view.FolderClick;
        view.LongClick += view.FolderLongClick;
        return new SettingsActivity.RecyclerViewFolderHolder(view);
    }
}
