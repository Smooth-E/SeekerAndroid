﻿/*
 * Copyright 2021 Seeker
 *
 * This file is part of Seeker
 *
 * Seeker is free software: you can redistribute it and/or modify
 * it under the terms of the GNU General Public License as published by
 * the Free Software Foundation, either version 3 of the License, or
 * (at your option) any later version.
 *
 * Seeker is distributed in the hope that it will be useful,
 * but WITHOUT ANY WARRANTY; without even the implied warranty of
 * MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
 * GNU General Public License for more details.
 *
 * You should have received a copy of the GNU General Public License
 * along with Seeker. If not, see <http://www.gnu.org/licenses/>.
 */

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Serialization;
using _Microsoft.Android.Resource.Designer;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Core.Content;
using AndroidX.DocumentFile.Provider;
using AndroidX.Preference;
using AndroidX.RecyclerView.Widget;
using Common;
using Seeker.Components;
using Seeker.Exceptions;
using Seeker.Helpers;
using Seeker.Main;
using Seeker.Managers;
using Seeker.UPnP;
using Seeker.Utils;
using AlertDialog = AndroidX.AppCompat.App.AlertDialog;
using Toolbar = AndroidX.AppCompat.Widget.Toolbar;

namespace Seeker.Settings;

// AppCompatActivity is needed to support changing light / dark mode programmatically...
[Activity(Label = "SettingsActivity", Theme = "@style/AppTheme.NoActionBar", Exported = false)]
public class SettingsActivity : ThemeableActivity
{
    private const int CHANGE_WRITE_EXTERNAL = 0x909;
    private const int CHANGE_WRITE_EXTERNAL_LEGACY = 0x910;

    private const int UPLOAD_DIR_ADD_WRITE_EXTERNAL = 0x911;
    private const int UPLOAD_DIR_ADD_WRITE_EXTERNAL_RESELECT_CASE = 0x834;
    private const int UPLOAD_DIR_ADD_WRITE_EXTERNAL_LEGACY = 0x912;
    private const int UPLOAD_DIR_ADD_WRITE_EXTERNAL_LEGACY_RESELECT_CASE = 0x835;

    private const int SAVE_SEEKER_SETTINGS = 0x856;

    private const int READ_EXTERNAL_FOR_MEDIA_STORE = 1182021;

    private const int CHANGE_INCOMPLETE_EXTERNAL = 0x913;
    private const int CHANGE_INCOMPLETE_EXTERNAL_LEGACY = 0x914;

    private const int FORCE_REQUEST_STORAGE_MANAGER = 0x434;

    public const int SCROLL_TO_SHARING_SECTION = 10;
    public const string SCROLL_TO_SHARING_SECTION_STRING = "SCROLL_TO_SHARING_SECTION";
    
    private Toolbar toolbar;
    private SettingsFragment settingsFragment;
    
    private readonly List<Tuple<int, int>> positionNumberPairs = [];

    private ViewGroup sharingSubLayout1;
    private ViewGroup sharingSubLayout2;

    private ViewGroup listeningSubLayout2;
    private ViewGroup listeningSubLayout3;

    private ViewGroup limitDlSpeedSubLayout;
    private Button changeDlSpeed;
    private TextView dlSpeedTextView;
    private Spinner dlLimitPerTransfer;


    private ViewGroup limitUlSpeedSubLayout;
    private Button changeUlSpeed;
    private TextView ulSpeedTextView;
    private Spinner ulLimitPerTransfer;

    private ViewGroup concurrentDlSublayout;
    private TextView concurrentDlLabel;
    private Button concurrentDlButton;
    private CheckBox concurrentDlCheckbox;
    
    private Button addFolderButton;
    private Button clearAllFoldersButton;

    private TextView noSharedFoldersView;
    private RecyclerView recyclerViewFolders;
    private LinearLayoutManager recyclerViewFoldersLayoutManager;
    private UploadsRecyclerViewAdapter uploadsRecyclerViewFoldersAdapter;

    private Button browseSelfButton;
    private Button rescanSharesButton;

    private Button checkStatus;
    private Button changePort;
    
    private CheckBox useUPnPCheckBox;
    
    public static UploadDirectoryInfo ContextMenuItem;
    
    private ScrollView mainScrollView;
    private View sharingLayoutParent;

    private Spinner languageSpinner;
    
    public override bool OnOptionsItemSelected(IMenuItem item)
    {
        if (item.ItemId != Android.Resource.Id.Home)
        {
            return base.OnOptionsItemSelected(item);
        }
        
        OnBackPressedDispatcher.OnBackPressed();
        return true;
    }

    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        SetContentView(ResourceConstant.Layout.settings_layout);
        SeekerState.ActiveActivityRef = this;

        toolbar = FindViewById<Toolbar>(ResourceConstant.Id.setting_toolbar)!;
        SetSupportActionBar(toolbar);
        SupportActionBar!.SetDisplayHomeAsUpEnabled(true);

        settingsFragment = new SettingsFragment();
        SupportFragmentManager.BeginTransaction()
            .Replace(ResourceConstant.Id.preference_fragment_holder, settingsFragment)
            .Commit();
        
        var progBar = FindViewById<ProgressBar>(ResourceConstant.Id.progressBarSharedStatus)!;
        progBar.IndeterminateDrawable.SetColorFilter(SearchItemViewExpandable.GetColorFromAttribute(SeekerState.ActiveActivityRef, Resource.Attribute.mainTextColor), Android.Graphics.PorterDuff.Mode.SrcIn);
        progBar.Click += ImageView_Click;

        ImageView moreInfoDiagnostics = FindViewById<ImageView>(Resource.Id.moreInfoDiagnostics);
        moreInfoDiagnostics.Click += MoreInfoDiagnostics_Click;

        Button restoreDefaultsButton = FindViewById<Button>(Resource.Id.restoreDefaults);
        restoreDefaultsButton.Click += RestoreDefaults_Click;

        Button exportClientData = FindViewById<Button>(Resource.Id.exportDataButton);
        exportClientData.Click += ExportClientData_Click;

        ImageView moreInfoExport = FindViewById<ImageView>(Resource.Id.moreInfoExport);
        moreInfoExport.Click += MoreInfoExport_Click;
        
        CheckBox enableDiagnostics = FindViewById<CheckBox>(Resource.Id.enableDiagnostics);
        enableDiagnostics.Checked = DiagnosticFile.Enabled;
        enableDiagnostics.CheckedChange += EnableDiagnostics_CheckedChange;
        
        ImageView imageView = this.FindViewById<ImageView>(Resource.Id.sharedStatus);
        imageView.Click += ImageView_Click;
        UpdateShareImageView();

        addFolderButton = FindViewById<Button>(Resource.Id.addUploadDirectory);
        addFolderButton.Click += AddUploadDirectory;
        clearAllFoldersButton = FindViewById<Button>(Resource.Id.clearAllDirectories);
        clearAllFoldersButton.Click += ClearAllFoldersButton_Click;

        noSharedFoldersView = FindViewById<TextView>(Resource.Id.noSharedFolders);
        recyclerViewFolders = FindViewById<RecyclerView>(Resource.Id.uploadFoldersRecyclerView);

        uploadsRecyclerViewFoldersAdapter = new UploadsRecyclerViewAdapter(this, UploadDirectoryManager.UploadDirectories);

        var llm = new LinearLayoutManager(this);
        var dividerItemDecoration = new DividerItemDecoration(recyclerViewFolders.Context, llm.Orientation);
        recyclerViewFolders.AddItemDecoration(dividerItemDecoration);

        recyclerViewFolders.SetLayoutManager(llm);
        recyclerViewFolders.SetAdapter(uploadsRecyclerViewFoldersAdapter);
        
        CheckBox shareCheckBox = FindViewById<CheckBox>(Resource.Id.enableSharing);
        shareCheckBox.Checked = SeekerState.SharingOn;
        shareCheckBox.CheckedChange += ShareCheckBox_CheckedChange;

        CheckBox unmeteredConnectionsOnlyCheckBox = FindViewById<CheckBox>(Resource.Id.shareOnlyOnUnmetered);
        unmeteredConnectionsOnlyCheckBox.Checked = !SeekerState.AllowUploadsOnMetered;
        unmeteredConnectionsOnlyCheckBox.CheckedChange += UnmeteredConnectionsOnlyCheckBox_CheckedChange;

        ImageView moreInfoButton = FindViewById<ImageView>(Resource.Id.moreInfoButton);
        moreInfoButton.Click += MoreInfoButton_Click;
        
        ImageView moreInfoConcurrent = FindViewById<ImageView>(Resource.Id.moreInfoConcurrent);
        moreInfoConcurrent.Click += MoreInfoConcurrent_Click;

        browseSelfButton = FindViewById<Button>(Resource.Id.browseSelfButton);
        browseSelfButton.Click += BrowseSelfButton_Click;
        browseSelfButton.LongClick += BrowseSelfButton_LongClick;

        rescanSharesButton = FindViewById<Button>(Resource.Id.rescanShares);
        rescanSharesButton.Click += RescanSharesButton_Click;

        CheckBox enableListening = FindViewById<CheckBox>(Resource.Id.enableListening);
        enableListening.Checked = SeekerState.ListenerEnabled;
        enableListening.CheckedChange += EnableListening_CheckedChange;

        CheckBox enableDlSpeedLimits = FindViewById<CheckBox>(Resource.Id.enable_dl_speed_limits);
        enableDlSpeedLimits.Checked = SeekerState.SpeedLimitDownloadOn;
        enableDlSpeedLimits.CheckedChange += EnableDlSpeedLimits_CheckedChange;

        CheckBox enableUlSpeedLimits = FindViewById<CheckBox>(Resource.Id.enable_ul_speed_limits);
        enableUlSpeedLimits.Checked = SeekerState.SpeedLimitUploadOn;
        enableUlSpeedLimits.CheckedChange += EnableUlSpeedLimits_CheckedChange;
        
        limitDlSpeedSubLayout = FindViewById<ViewGroup>(Resource.Id.dlSpeedSubLayout);
        dlSpeedTextView = FindViewById<TextView>(Resource.Id.downloadSpeed);
        changeDlSpeed = FindViewById<Button>(Resource.Id.changeDlSpeed);
        changeDlSpeed.Click += ChangeDlSpeed_Click;
        dlLimitPerTransfer = FindViewById<Spinner>(Resource.Id.dlPerTransfer);
        SetSpeedTextView(dlSpeedTextView, false);

        limitUlSpeedSubLayout = FindViewById<ViewGroup>(Resource.Id.ulSpeedSubLayout);
        ulSpeedTextView = FindViewById<TextView>(Resource.Id.uploadSpeed);
        changeUlSpeed = FindViewById<Button>(Resource.Id.changeUlSpeed);
        changeUlSpeed.Click += ChangeUlSpeed_Click;
        ulLimitPerTransfer = FindViewById<Spinner>(Resource.Id.ulPerTransfer);
        SetSpeedTextView(ulSpeedTextView, true);

        UpdateSpeedLimitsState();

        concurrentDlSublayout = FindViewById<ViewGroup>(Resource.Id.limitConcurrentDownloadsSublayout2);
        concurrentDlLabel = FindViewById<TextView>(Resource.Id.concurrentDownloadsLabel);
        concurrentDlCheckbox = FindViewById<CheckBox>(Resource.Id.limitConcurrentDownloadsCheckBox);
        concurrentDlCheckbox.Checked = Soulseek.SimultaneousDownloadsGatekeeper.RestrictConcurrentUsers;
        concurrentDlCheckbox.CheckedChange += ConcurrentDlCheckbox_CheckedChange;

        concurrentDlButton = FindViewById<Button>(Resource.Id.changeConcurrentDownloads);
        concurrentDlButton.Click += ConcurrentDlBottom_Click;
        concurrentDlLabel.Text = SeekerApplication.ApplicationContext.GetString(Resource.String.MaxConcurrentIs) + " " + Soulseek.SimultaneousDownloadsGatekeeper.MaxUsersConcurrent;
        
        UpdateConcurrentDownloadLimitsState();

        String[] dlOptions = new String[] { SeekerApplication.ApplicationContext.GetString(Resource.String.PerTransfer), SeekerApplication.ApplicationContext.GetString(Resource.String.Global) };
        ArrayAdapter<String> dlOptionsStrings = new ArrayAdapter<string>(this, Resource.Layout.support_simple_spinner_dropdown_item, dlOptions);
        dlLimitPerTransfer.Adapter = dlOptionsStrings;
        SetSpinnerPositionSpeed(dlLimitPerTransfer, false);
        dlLimitPerTransfer.ItemSelected += DlLimitPerTransfer_ItemSelected;

        ulLimitPerTransfer.Adapter = dlOptionsStrings;
        SetSpinnerPositionSpeed(ulLimitPerTransfer, true);
        ulLimitPerTransfer.ItemSelected += UlLimitPerTransfer_ItemSelected;

        ImageView listeningMoreInfo = FindViewById<ImageView>(Resource.Id.listeningHelp);
        listeningMoreInfo.Click += ListeningMoreInfo_Click;

        TextView portView = FindViewById<TextView>(Resource.Id.portView);
        SetPortViewText(portView);

        changePort = FindViewById<Button>(Resource.Id.changePort);
        changePort.Click += ChangePort_Click;

        checkStatus = FindViewById<Button>(Resource.Id.checkStatus);
        checkStatus.Click += CheckStatus_Click;

        Button getPriv = FindViewById<Button>(Resource.Id.getPriv);
        getPriv.Click += GetPriv_Click;

        Button checkPriv = FindViewById<Button>(Resource.Id.checkPriv);
        checkPriv.Click += CheckPriv_Click;

        TextView privStatus = FindViewById<TextView>(Resource.Id.privStatusView);
        SetPrivStatusView(privStatus);

        ImageView privHelp = FindViewById<ImageView>(Resource.Id.privHelp);
        privHelp.Click += PrivHelp_Click;

        Button forceFilesystemPermission = FindViewById<Button>(Resource.Id.forceFilesystemPermission);
        forceFilesystemPermission.Click += ForceFilesystemPermission_Click;

#if !IzzySoft
        forceFilesystemPermission.Enabled = false;
        forceFilesystemPermission.Alpha = 0.5f;
        forceFilesystemPermission.Clickable = false;
#endif

        ImageView moreInfoForceFilesystem = FindViewById<ImageView>(Resource.Id.moreInfoButtonForceFilesystemPermission);
        moreInfoForceFilesystem.Click += MoreInfoForceFilesystem_Click;

        Button editUserInfo = FindViewById<Button>(Resource.Id.editUserInfoButton);
        editUserInfo.Click += EditUserInfo_Click;

        Button changePassword = FindViewById<Button>(Resource.Id.changePassword);
        changePassword.Click += ChangePassword_Click;

        useUPnPCheckBox = FindViewById<CheckBox>(Resource.Id.useUPnPCheckBox);
        useUPnPCheckBox.Checked = SeekerState.ListenerUPnpEnabled;
        useUPnPCheckBox.CheckedChange += UseUPnPCheckBox_CheckedChange;

        ImageView UpnpStatusView = FindViewById<ImageView>(Resource.Id.UPnPStatus);
        SetUpnpStatusView(UpnpStatusView);
        UpnpStatusView.Click += ImageView_Click;

        sharingSubLayout1 = FindViewById<ViewGroup>(Resource.Id.dlChangeSharedDirectoryLayout);
        sharingSubLayout2 = FindViewById<ViewGroup>(Resource.Id.sharingSubLayout2);
        UpdateSharingViewState();

        listeningSubLayout2 = FindViewById<ViewGroup>(Resource.Id.listeningRow2);
        listeningSubLayout3 = FindViewById<ViewGroup>(Resource.Id.listeningRow3);
        UpdateListeningViewState();

        Button importData = FindViewById<Button>(Resource.Id.importDataButton);
        importData.Click += ImportData_Click;

        /*
         **NOTE**
         *
         * Regarding Directory Options (Incomplete Folder Options, Complete Folder Options):
         * Incomplete Folder internal structure is always the same (folder is username concat file foldername),
         *   it is just the placement of it that differs
         * The automatic placement is "Soulseek Incomplete" in the same directory chosen for downloads
         * (if "Create Folders for Downloads and Incomplete" is on.
         * Otherwise the placement is in AppData Local - what Android calls "Internal Storage"
         *
         * The incomplete folder choices are used when the stream is created and saved in IncompleteUri.
         * Therefore, changing this on the fly is okay, it just wont
         *   take effect until one starts a new download.
         * The complete folder choices are used when the file is actually saved / moved.
         * So changing this on the fly is okay, the transfer, once finished, will just go into its new place.
         *
         * When one turns "Manual Selection for Incomplete" off, it reverts back to Automatic.
         * The user will have to reselect their folder if they choose to turn it back on.
         *
         * To clear Incomplete, there cannot be any pending transfers.
         * Paused transfers are okay, they will just start from the top...
         *
         * If "Use Manual Incomplete Folder" is checked, but no Manual Incomplete Folder is chosen,
         * then it is as if it is not checked.
         * Also its fine if its null, or no longer writable, etc.  It will just get set back to default.
         * User will have to re-set it on their own.
         *
         **NOTE**
         */

        SetIncompleteDirectoryState();
        SetSharedFolderView();

        mainScrollView = FindViewById<ScrollView>(ResourceConstant.Id.mainScrollView);
        sharingLayoutParent = FindViewById<ViewGroup>(ResourceConstant.Id.sharingLayoutParent);
        if (Intent != null && Intent.GetIntExtra(SCROLL_TO_SHARING_SECTION_STRING, -1) != -1)
        {
            mainScrollView?.Post(() => mainScrollView.SmoothScrollTo(0, sharingLayoutParent.Top - 14));
        }

        UpdateLayoutParametersForScreenSize();
    }
    
    protected override void OnResume()
    {
        base.OnResume();

        UPnpManager.Instance.SearchFinished += OnUpnpSearchFinished;
        UPnpManager.Instance.SearchStarted += UpnpSearchStarted;
        UPnpManager.Instance.DeviceSuccessfullyMapped += OnUpnpDeviceMapped;
        PrivilegesManager.Instance.PrivilegesChecked += OnPrivilegesChecked;
        UploadDirectoryChanged += OnDirectoryViewsChanged;

        // when you open up the directory selection with OpenDocumentTree the SettingsActivity is paused
        UpdateDirectoryViews();

        // however with the api<21 it is not paused and so an event is needed.
        SeekerState.DirectoryUpdatedEvent += DirectoryUpdated;
        SeekerState.SharingStatusChangedEvent += SharingStatusUpdated;
    }

    protected override void OnPause()
    {
        UPnpManager.Instance.SearchFinished -= OnUpnpSearchFinished;
        UPnpManager.Instance.SearchStarted -= UpnpSearchStarted;
        UPnpManager.Instance.DeviceSuccessfullyMapped -= OnUpnpDeviceMapped;
        PrivilegesManager.Instance.PrivilegesChecked -= OnPrivilegesChecked;
        SeekerState.DirectoryUpdatedEvent -= DirectoryUpdated;
        UploadDirectoryChanged -= OnDirectoryViewsChanged;
        SeekerState.SharingStatusChangedEvent -= SharingStatusUpdated;
        
        SaveAdditionalDirectorySettingsToSharedPreferences();
        
        base.OnPause();
    }
    
    private void OnDirectoryViewsChanged(object sender, EventArgs e) =>
        RunOnUiThread(() => uploadsRecyclerViewFoldersAdapter?.NotifyDataSetChanged());

    private void SharingStatusUpdated(object sender, EventArgs e) =>
        RunOnUiThread(() => UpdateShareImageView());

    private void OnPrivilegesChecked(object sender, EventArgs e) =>
        RunOnUiThread(() => SetPrivStatusView(FindViewById<TextView>(ResourceConstant.Id.privStatusView)));

    private void OnUpnpDeviceMapped(object sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            this.ShowShortToast(ResourceConstant.String.upnp_success);
            SetUpnpStatusView(FindViewById<ImageView>(ResourceConstant.Id.UPnPStatus));
        });
    }

    private void OnUpnpSearchFinished(object sender, EventArgs e)
    {
        RunOnUiThread(() =>
        {
            if (SeekerState.ListenerEnabled 
                && SeekerState.ListenerUPnpEnabled 
                && UPnpManager.Instance.RunningStatus == UPnPRunningStatus.Finished
                && UPnpManager.Instance.DiagStatus != UPnPDiagStatus.Success)
            {
                this.ShowShortToast(ResourceConstant.String.upnp_search_finished);
            }
            
            SetUpnpStatusView(FindViewById<ImageView>(ResourceConstant.Id.UPnPStatus));
        });
    }

    private void DirectoryUpdated(object sender, EventArgs e) => 
        UpdateDirectoryViews();

    private void UpdateDirectoryViews()
    {
        // TODO: Track down this use case of removed method
        
        // SetIncompleteFolderView();
        
        SetSharedFolderView();
    }

    private void UpnpSearchStarted(object sender, EventArgs e) =>
        RunOnUiThread(() => SetUpnpStatusView(FindViewById<ImageView>(ResourceConstant.Id.UPnPStatus)));
    
    private void MoreInfoExport_Click(object sender, EventArgs e)
    {
        var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(this, Resource.Style.MyAlertDialogTheme);
        var diag = builder.SetMessage(Resource.String.export_more_info).SetPositiveButton(Resource.String.close, OnCloseClick).Create();
        diag.Show();
    }

    private const string DefaultDocumentsUri = "content://com.android.externalstorage.documents/tree/primary%3ADocuments";

    private void ExportClientData_Click(object sender, EventArgs e)
    {
        var intent = new Android.Content.Intent(Android.Content.Intent.ActionCreateDocument);
        intent.SetType("application/xml");
        intent.PutExtra(Android.Content.Intent.ExtraTitle, "seeker_data.xml");
        intent.AddCategory(Android.Content.Intent.CategoryOpenable);
        if ((int)Android.OS.Build.VERSION.SdkInt >= 26)
        {
            intent.PutExtra(Android.Provider.DocumentsContract.ExtraInitialUri, Android.Net.Uri.Parse(DefaultDocumentsUri));
        }
        this.StartActivityForResult(intent, SAVE_SEEKER_SETTINGS);
    }

    private void UpdateLayoutParametersForScreenSize()
    {
        try
        {
            if (this.Resources.DisplayMetrics.WidthPixels < 400) //320 is MDPI
            {
                (concurrentDlSublayout as LinearLayout).Orientation = Orientation.Vertical;
                (concurrentDlSublayout as LinearLayout).SetGravity(GravityFlags.Center);
                ((LinearLayout.LayoutParams)concurrentDlButton.LayoutParameters).Gravity = GravityFlags.Center;
            }
            else
            {
                (concurrentDlSublayout as LinearLayout).Orientation = Orientation.Horizontal;
                (concurrentDlSublayout as LinearLayout).SetGravity(GravityFlags.CenterVertical);
                ((LinearLayout.LayoutParams)concurrentDlButton.LayoutParameters).Gravity = GravityFlags.CenterVertical;
            }
        }
        catch (Exception ex)
        {
            Logger.FirebaseDebug("Unable to tweak layout " + ex);
        }
    }

    private void UnmeteredConnectionsOnlyCheckBox_CheckedChange(object sender, CompoundButton.CheckedChangeEventArgs e)
    {
        bool oldState = SharingManager.MeetsCurrentSharingConditions();
        SeekerState.AllowUploadsOnMetered = !e.IsChecked;
        bool newState = SharingManager.MeetsCurrentSharingConditions();
        if (oldState != newState)
        {
            SharingManager.SetUnsetSharingBasedOnConditions(true);
            UpdateShareImageView();
        }
        lock (SeekerApplication.SharedPrefLock)
        {
            var editor = SeekerState.ActiveActivityRef.GetSharedPreferences("SoulSeekPrefs", 0).Edit();
            editor.PutBoolean(KeyConsts.M_AllowUploadsOnMetered, SeekerState.AllowUploadsOnMetered);
            editor.Commit();
        }
    }

    private void ClearAllFoldersButton_Click(object sender, EventArgs e)
    {
        if (UploadDirectoryManager.UploadDirectories.Count > 1) //ask before doing.
        {
            var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(this, Resource.Style.MyAlertDialogTheme);
            var diag = builder.SetMessage(String.Format(SeekerApplication.ApplicationContext.GetString(Resource.String.AreYouSureClearAllDirectories), UploadDirectoryManager.UploadDirectories.Count))
                .SetPositiveButton(Resource.String.yes, (object sender, DialogClickEventArgs e) =>
                {
                    this.ClearAllFolders();
                    this.OnCloseClick(sender, e);
                })
                .SetNegativeButton(Resource.String.No, OnCloseClick)
                .Create();
            diag.Show();
        }
        else
        {
            this.ClearAllFolders();
        }
    }

    private void ClearAllFolders()
    {
        UploadDirectoryManager.UploadDirectories.Clear();
        UploadDirectoryManager.SaveToSharedPreferences(SeekerState.SharedPreferences);
        uploadsRecyclerViewFoldersAdapter.NotifyDataSetChanged();
        SetSharedFolderView();
        SeekerState.SharedFileCache = SlskHelp.SharedFileCache.GetEmptySharedFileCache();
        SharedCacheManager.SharedFileCache_Refreshed(null, (0, 0));
        this.UpdateShareImageView();
    }

    private void MoreInfoDiagnostics_Click(object sender, EventArgs e)
    {
        var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(this, Resource.Style.MyAlertDialogTheme);
        var diag = builder.SetMessage(Resource.String.diagnostics_more_info).SetPositiveButton(Resource.String.close, OnCloseClick).Create();
        diag.Show();
    }

    private void MoreInfoConcurrent_Click(object sender, EventArgs e)
    {
        var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(this, Resource.Style.MyAlertDialogTheme);
        var diag = builder.SetMessage(Resource.String.concurrent_dialog).SetPositiveButton(Resource.String.close, OnCloseClick).Create();
        diag.Show();
    }

    private void ConcurrentDlCheckbox_CheckedChange(object sender, CompoundButton.CheckedChangeEventArgs e)
    {
        if (e.IsChecked == Soulseek.SimultaneousDownloadsGatekeeper.RestrictConcurrentUsers)
        {
            return;
        }
        Soulseek.SimultaneousDownloadsGatekeeper.RestrictConcurrentUsers = e.IsChecked;
        this.UpdateConcurrentDownloadLimitsState();
        SaveMaxConcurrentDownloadsSettings();
    }

    private void ConcurrentDlBottom_Click(object sender, EventArgs e)
    {
        ShowChangeDialog(ChangeDialogType.ConcurrentDL);
    }

    private void UlLimitPerTransfer_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
    {
        if (e.Position == 0)
        {
            SeekerState.SpeedLimitUploadIsPerTransfer = true;
        }
        else
        {
            SeekerState.SpeedLimitUploadIsPerTransfer = false;
        }
    }

    private void ChangeUlSpeed_Click(object sender, EventArgs e)
    {
        ShowChangeDialog(ChangeDialogType.ChangeUL);
    }

    private void DlLimitPerTransfer_ItemSelected(object sender, AdapterView.ItemSelectedEventArgs e)
    {
        if (e.Position == 0)
        {
            SeekerState.SpeedLimitDownloadIsPerTransfer = true;
        }
        else
        {
            SeekerState.SpeedLimitDownloadIsPerTransfer = false;
        }
    }

    private void ChangeDlSpeed_Click(object sender, EventArgs e)
    {
        ShowChangeDialog(ChangeDialogType.ChangeDL);
    }

    private void EnableDlSpeedLimits_CheckedChange(object sender, CompoundButton.CheckedChangeEventArgs e)
    {
        if (e.IsChecked == SeekerState.SpeedLimitDownloadOn)
        {
            return;
        }
        SeekerState.SpeedLimitDownloadOn = e.IsChecked;
        UpdateSpeedLimitsState();
        SharedPreferencesUtils.SaveSpeedLimitState();
    }

    private void EnableUlSpeedLimits_CheckedChange(object sender, CompoundButton.CheckedChangeEventArgs e)
    {
        if (e.IsChecked == SeekerState.SpeedLimitUploadOn)
        {
            return;
        }
        SeekerState.SpeedLimitUploadOn = e.IsChecked;
        UpdateSpeedLimitsState();
        SharedPreferencesUtils.SaveSpeedLimitState();
    }

    private void ImportData_Click(object sender, EventArgs e)
    {
        if (!SeekerState.currentlyLoggedIn || !SeekerState.SoulseekClient.State.HasFlag(Soulseek.SoulseekClientStates.LoggedIn))
        {
            Toast.MakeText(this, Resource.String.MustBeLoggedInToImport, ToastLength.Long).Show();
            return;
        }
        Intent intent = new Intent(this, typeof(ImportWizardActivity));
        StartActivity(intent);
    }

    private void EnableDiagnostics_CheckedChange(object sender, CompoundButton.CheckedChangeEventArgs e)
    {
        if (DiagnosticFile.Enabled != e.IsChecked)
        {
            DiagnosticFile.Enabled = e.IsChecked;
            //if you do this without restarting, you have everything other than the diagnostics of slskclient set to Info+ rather than Debug+ 
            DiagnosticFile.UpdateDiagnosticState();
            lock (SeekerApplication.SharedPrefLock)
            {
                var editor = SeekerState.ActiveActivityRef.GetSharedPreferences("SoulSeekPrefs", 0).Edit();
                editor.PutBoolean(KeyConsts.M_LOG_DIAGNOSTICS, DiagnosticFile.Enabled);
                bool success = editor.Commit();
            }
        }
    }

    private void RescanSharesButton_Click(object sender, EventArgs e)
    {
        // for rescan=true, we use the previous parse to get metadata if there is a match...
        // so that we do not have to read the file again to get things like bitrate, samples, etc.
        // if the presentable name is in the last parse, and the size matches,
        // then use those attributes we previously had to read the file to get..
        Rescan(null, -1, SeekerState.PreOpenDocumentTree(), true);
    }

    public static bool UseIncompleteManualFolder()
    {
        return SeekerState.OverrideDefaultIncompleteLocations.Value && SeekerState.RootIncompleteDocumentFile != null;
    }
    
    public void SetIncompleteDirectoryState()
    {
        // TODO: These will be handled with preference dependency
        var overrideDefault = SeekerState.OverrideDefaultIncompleteLocations.Value;
        recyclerViewFolders.Clickable = overrideDefault;
    }

    private void SetSharedFolderView()
    {
        if (UploadDirectoryManager.UploadDirectories.Count == 0)
        {
            this.noSharedFoldersView.Visibility = ViewStates.Visible;
            this.recyclerViewFolders.Visibility = ViewStates.Gone;
            this.clearAllFoldersButton.Enabled = false;
            this.clearAllFoldersButton.Alpha = 0.5f;
        }
        else
        {
            this.noSharedFoldersView.Visibility = ViewStates.Gone;
            this.recyclerViewFolders.Visibility = ViewStates.Visible;
            this.clearAllFoldersButton.Enabled = true;
            this.clearAllFoldersButton.Alpha = 1.0f;
        }
    }

    private void PrivHelp_Click(object sender, EventArgs e)
    {
        var builder = new AlertDialog.Builder(this, ResourceConstant.Style.MyAlertDialogTheme);
        var diag = builder.SetMessage(ResourceConstant.String.privileges_more_info)?
            .SetPositiveButton(ResourceConstant.String.close, OnCloseClick)
            .Create();
        diag?.Show();
    }

    private void CheckPriv_Click(object sender, EventArgs e)
    {
        SeekerApplication.ApplicationContext.ShowShortToast(ResourceConstant.String.checking_priv_);
        PrivilegesManager.Instance.GetPrivilegesAPI(true);
    }

    private void GetPriv_Click(object sender, EventArgs e)
    {
        if (MainActivity.IsNotLoggedIn())
        {
            SeekerApplication.ApplicationContext
                .ShowLongToast(ResourceConstant.String.must_be_logged_in_to_get_privileges);
            return;
        }
        //note: it seems that the Uri.Encode is not strictly necessary.  that is both "dog gone it" and "dog%20gone%20it" work just fine...
        Android.Net.Uri uri = Android.Net.Uri.Parse("https://www.slsknet.org/userlogin.php?username=" + Android.Net.Uri.Encode(SeekerState.Username)); // missing 'http://' will cause crash.
        CommonHelpers.ViewUri(uri, this);
    }

    private void EditUserInfo_Click(object sender, EventArgs e)
    {
        Intent intent = new Intent(SeekerState.ActiveActivityRef, typeof(EditUserInfoActivity));
        this.StartActivity(intent);
    }

    private void ChangePassword_Click(object sender, EventArgs e)
    {
        if (!SeekerState.currentlyLoggedIn)
        {
            Toast.MakeText(SeekerState.ActiveActivityRef, SeekerApplication.ApplicationContext.GetString(Resource.String.must_be_logged_in_to_change_password), ToastLength.Short).Show();
            return;
        }

        // show dialog
        Logger.FirebaseInfo("ChangePasswordDialog" + this.IsFinishing + this.IsDestroyed);

        void OkayAction(object sender, string textInput)
        {
            CommonHelpers.PerformConnectionRequiredAction(() => CommonHelpers.ChangePasswordLogic(textInput), SeekerApplication.ApplicationContext.GetString(Resource.String.must_be_logged_in_to_change_password));
            if (sender is AndroidX.AppCompat.App.AlertDialog aDiag)
            {
                aDiag.Dismiss();
            }
            else
            {
                CommonHelpers._dialogInstance?.Dismiss(); // todo: why?
            }
        }

        CommonHelpers.ShowSimpleDialog(
            this,
            Resource.Layout.edit_text_password_dialog_content,
            this.Resources.GetString(Resource.String.change_password),
            OkayAction,
            this.Resources.GetString(Resource.String.okay),
            null,
            this.Resources.GetString(Resource.String.new_password),
            this.Resources.GetString(Resource.String.cancel),
            this.Resources.GetString(Resource.String.cannot_be_empty),
            true);
    }
    
    private void UseUPnPCheckBox_CheckedChange(object sender, CompoundButton.CheckedChangeEventArgs e)
    {
        if (e.IsChecked == SeekerState.ListenerUPnpEnabled)
        {
            return;
        }
        SeekerState.ListenerUPnpEnabled = e.IsChecked;
        SharedPreferencesUtils.SaveListeningState();

        if (e.IsChecked)
        {
            //open port...
            UPnpManager.Instance.Feedback = true;
            UPnpManager.Instance.SearchAndSetMappingIfRequired();

        }
        else
        {
            SetUpnpStatusView(this.FindViewById<ImageView>(Resource.Id.UPnPStatus)); //so that it shows not enabled...
        }
    }

    private void EnableListening_CheckedChange(object sender, CompoundButton.CheckedChangeEventArgs e)
    {
        if (e.IsChecked == SeekerState.ListenerEnabled)
        {
            return;
        }
        if (e.IsChecked)
        {
            Toast.MakeText(SeekerState.ActiveActivityRef, Resource.String.enabling_listener, ToastLength.Short).Show();
        }
        else
        {
            Toast.MakeText(SeekerState.ActiveActivityRef, Resource.String.disabling_listener, ToastLength.Short).Show();
        }
        SeekerState.ListenerEnabled = e.IsChecked;
        UpdateListeningViewState();
        SharedPreferencesUtils.SaveListeningState();
        ReconfigureOptionsApi(null, e.IsChecked, null);
        if (e.IsChecked)
        {
            UPnpManager.Instance.Feedback = true;
            UPnpManager.Instance.SearchAndSetMappingIfRequired(); //bc it may not have been set before...
        }
    }
    
    private void CheckStatus_Click(object sender, EventArgs e)
    {
        Android.Net.Uri uri = Android.Net.Uri.Parse("http://www.slsknet.org/porttest.php?port=" + SeekerState.ListenerPort); // missing 'http://' will cause crashed. //an https for this link does not exist
        CommonHelpers.ViewUri(uri, this);
    }
    private static AndroidX.AppCompat.App.AlertDialog changeDialog = null;
    private void ChangePort_Click(object sender, EventArgs e)
    {
        ShowChangeDialog(ChangeDialogType.ChangePort);
    }

    public enum ChangeDialogType
    {
        ChangePort = 0,
        ChangeDL = 1,
        ChangeUL = 2,
        ConcurrentDL = 3,
    }

    private void ShowChangeDialog(ChangeDialogType changeDialogType)
    {
        Logger.FirebaseInfo("ShowChangePortDialog");
        AndroidX.AppCompat.App.AlertDialog.Builder builder = new AndroidX.AppCompat.App.AlertDialog.Builder(this, Resource.Style.MyAlertDialogTheme); //failed to bind....
        if (changeDialogType == ChangeDialogType.ChangePort)
        {
            builder.SetTitle(this.GetString(Resource.String.change_port) + ":");
        }
        else if (changeDialogType == ChangeDialogType.ChangeDL)
        {
            builder.SetTitle(Resource.String.ChangeDownloadSpeed);
        }
        else if (changeDialogType == ChangeDialogType.ChangeUL)
        {
            builder.SetTitle(Resource.String.ChangeUploadSpeed);
        }
        else if (changeDialogType == ChangeDialogType.ConcurrentDL)
        {
            builder.SetTitle(Resource.String.MaxConcurrentIs);
        }
        View viewInflated = LayoutInflater.From(this).Inflate(Resource.Layout.choose_port, (ViewGroup)this.FindViewById(Android.Resource.Id.Content), false);
        // Set up the input
        EditText input = (EditText)viewInflated.FindViewById<EditText>(Resource.Id.chosePortEditText);
        if (changeDialogType == ChangeDialogType.ChangeDL)
        {
            input.Hint = SeekerApplication.ApplicationContext.GetString(Resource.String.EnterSpeed);
        }
        else if (changeDialogType == ChangeDialogType.ChangeUL)
        {
            input.Hint = SeekerApplication.ApplicationContext.GetString(Resource.String.EnterSpeed);
        }
        else if (changeDialogType == ChangeDialogType.ConcurrentDL)
        {
            input.Hint = SeekerApplication.ApplicationContext.GetString(Resource.String.EnterMaxDownloadSimultaneously);
        }
        builder.SetView(viewInflated);

        EventHandler<DialogClickEventArgs> eventHandler = new EventHandler<DialogClickEventArgs>((object sender, DialogClickEventArgs okayArgs) =>
        {
            if (changeDialogType == ChangeDialogType.ChangePort)
            {
                int portNum = -1;
                if (!int.TryParse(input.Text, out portNum))
                {
                    Toast.MakeText(this, Resource.String.port_failed_parse, ToastLength.Long).Show();
                    return;
                }
                if (portNum < 1024 || portNum > 65535)
                {
                    Toast.MakeText(this, Resource.String.port_out_of_range, ToastLength.Long).Show();
                    return;
                }
                ReconfigureOptionsApi(null, null, portNum);
                SeekerState.ListenerPort = portNum;
                UPnpManager.Instance.Feedback = true;
                UPnpManager.Instance.SearchAndSetMappingIfRequired();
                SharedPreferencesUtils.SaveListeningState();
                SetPortViewText(FindViewById<TextView>(Resource.Id.portView));
                changeDialog.Dismiss();
            }
            else if (changeDialogType == ChangeDialogType.ChangeUL || changeDialogType == ChangeDialogType.ChangeDL)
            {
                int dlSpeedKbs = -1;
                if (!int.TryParse(input.Text, out dlSpeedKbs))
                {
                    Toast.MakeText(this, "Speed failed to parse", ToastLength.Long).Show();
                    return;
                }
                if (dlSpeedKbs < 64)
                {
                    Toast.MakeText(this, "Minimum Speed is 64 kb/s", ToastLength.Long).Show();
                    return;
                }
                if (changeDialogType == ChangeDialogType.ChangeDL)
                {
                    SeekerState.SpeedLimitDownloadBytesSec = 1024 * dlSpeedKbs;
                    SetSpeedTextView(FindViewById<TextView>(Resource.Id.downloadSpeed), false);
                }
                else
                {
                    SeekerState.SpeedLimitUploadBytesSec = 1024 * dlSpeedKbs;
                    SetSpeedTextView(FindViewById<TextView>(Resource.Id.uploadSpeed), true);
                }

                SharedPreferencesUtils.SaveSpeedLimitState();
                changeDialog.Dismiss();
            }
            else if (changeDialogType == ChangeDialogType.ConcurrentDL)
            {
                int concurrentDL = -1;
                if (!int.TryParse(input.Text, out concurrentDL))
                {
                    Toast.MakeText(this, "Failed to Parse Number", ToastLength.Long).Show();
                    return;
                }
                if (concurrentDL < 1)
                {
                    Toast.MakeText(this, "Must be greater than 0", ToastLength.Long).Show();
                    return;
                }

                Soulseek.SimultaneousDownloadsGatekeeper.MaxUsersConcurrent = concurrentDL;
                // always add space as the resource string will always trim trailing spaces.
                FindViewById<TextView>(Resource.Id.concurrentDownloadsLabel).Text = SeekerApplication.ApplicationContext.GetString(Resource.String.MaxConcurrentIs) + " " + Soulseek.SimultaneousDownloadsGatekeeper.MaxUsersConcurrent;

                SaveMaxConcurrentDownloadsSettings();
                changeDialog.Dismiss();
            }

        });

        EventHandler<DialogClickEventArgs> cancelHandler = new EventHandler<DialogClickEventArgs>((object sender, DialogClickEventArgs okayArgs) =>
        {
            if (sender is AndroidX.AppCompat.App.AlertDialog aDiag)
            {
                aDiag.Dismiss();
            }
            else
            {
                changeDialog.Dismiss();
            }
        });
        
        System.EventHandler<TextView.EditorActionEventArgs> editorAction = (object sender, TextView.EditorActionEventArgs e) =>
        {
            if (e.ActionId == Android.Views.InputMethods.ImeAction.Done || //in this case it is Done (blue checkmark)
                e.ActionId == Android.Views.InputMethods.ImeAction.Go ||
                e.ActionId == Android.Views.InputMethods.ImeAction.Next ||
                e.ActionId == Android.Views.InputMethods.ImeAction.Search) //ImeNull if being called due to the enter key being pressed. (MSDN) but ImeNull gets called all the time....
            {
                Logger.Debug("IME ACTION: " + e.ActionId.ToString());
                
                //overriding this, the keyboard fails to go down by default for some reason.....
                try
                {
                    Android.Views.InputMethods.InputMethodManager imm = (Android.Views.InputMethods.InputMethodManager)SeekerState.ActiveActivityRef.GetSystemService(Context.InputMethodService);
                    imm.HideSoftInputFromWindow(this.FindViewById<ViewGroup>(Android.Resource.Id.Content).WindowToken, 0);
                }
                catch (System.Exception ex)
                {
                    Logger.FirebaseDebug(ex.Message + " error closing keyboard");
                }
                
                // Do the Browse Logic...
                eventHandler(sender, null);
            }
        };

        input.EditorAction += editorAction;
        input.FocusChange += Input_FocusChange;

        builder.SetPositiveButton(Resource.String.okay, eventHandler);
        builder.SetNegativeButton(Resource.String.cancel, cancelHandler);

        changeDialog = builder.Create();
        changeDialog.Show();
    }

    private void Input_FocusChange(object sender, View.FocusChangeEventArgs e)
    {
        try
        {
            SeekerState.ActiveActivityRef.Window.SetSoftInputMode(SoftInput.AdjustNothing);
        }
        catch (Exception err)
        {
            Logger.FirebaseDebug("MainActivity_FocusChange" + err.Message);
        }
    }

    private static void SetPortViewText(TextView tv)
    {
        tv.Text = SeekerState.ActiveActivityRef.GetString(Resource.String.port) + ": " + SeekerState.ListenerPort.ToString();
    }

    private static void SetSpeedTextView(TextView tv, bool isUpload)
    {
        int speedKbs = isUpload ? (SeekerState.SpeedLimitUploadBytesSec / 1024) : (SeekerState.SpeedLimitDownloadBytesSec / 1024);
        tv.Text = speedKbs.ToString() + " kb/s";
    }

    private static void SetSpinnerPositionSpeed(Spinner spinner, bool isUpload)
    {
        if (isUpload)
        {
            if (SeekerState.SpeedLimitUploadIsPerTransfer)
            {
                spinner.SetSelection(0);
            }
            else
            {
                spinner.SetSelection(1);
            }
        }
        else
        {
            if (SeekerState.SpeedLimitDownloadIsPerTransfer)
            {
                spinner.SetSelection(0);
            }
            else
            {
                spinner.SetSelection(1);
            }
        }
    }

    private static void SetPrivStatusView(TextView tv)
    {
        string privileges = SeekerApplication.ApplicationContext.GetString(Resource.String.privileges) + ": ";
        tv.Text = privileges + PrivilegesManager.Instance.GetPrivilegeStatus();
    }

    private static void SetUpnpStatusView(ImageView iv)
    {
        // TODO: ???
        Tuple<UPnpManager.ListeningIcon, string> info = UPnpManager.Instance.GetIconAndMessage();
        if (iv == null) return;
        if ((int)Android.OS.Build.VERSION.SdkInt >= 26)
        {
            iv.TooltipText = info.Item2; //api26+ otherwise crash...
        }
        else
        {
            AndroidX.AppCompat.Widget.TooltipCompat.SetTooltipText(iv, info.Item2);
        }
        switch (info.Item1)
        {
            case UPnpManager.ListeningIcon.ErrorIcon:
                iv.SetImageResource(Resource.Drawable.lan_disconnect);
                break;
            case UPnpManager.ListeningIcon.OffIcon:
                iv.SetImageResource(Resource.Drawable.network_off_outline);
                break;
            case UPnpManager.ListeningIcon.PendingIcon:
                iv.SetImageResource(Resource.Drawable.lan_pending);
                break;
            case UPnpManager.ListeningIcon.SuccessIcon:
                iv.SetImageResource(Resource.Drawable.lan_connect);
                break;
        }
    }

    private void ListeningMoreInfo_Click(object sender, EventArgs e)
    {
        var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(this, Resource.Style.MyAlertDialogTheme);
        var diag = builder.SetMessage(Resource.String.listening).SetPositiveButton(Resource.String.close, OnCloseClick).Create();
        diag.Show();
    }

    public void MoreInfoForceFilesystem_Click(object sender, EventArgs e)
    {
        var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(this, Resource.Style.MyAlertDialogTheme);
        var diag = builder.SetMessage(Resource.String.force_filesystem_message).SetPositiveButton(Resource.String.close, OnCloseClick).Create();
        diag.Show();
        var origString = SeekerState.ActiveActivityRef.GetString(Resource.String.force_filesystem_message); //this is a literal CDATA string.
        if ((int)Android.OS.Build.VERSION.SdkInt >= 24)
        {
            ((TextView)diag.FindViewById(Android.Resource.Id.Message)).TextFormatted = Android.Text.Html.FromHtml(origString, Android.Text.FromHtmlOptions.ModeLegacy); //this can be slow so do NOT do it in loops...
        }
        else
        {
            ((TextView)diag.FindViewById(Android.Resource.Id.Message)).TextFormatted = Android.Text.Html.FromHtml(origString); //this can be slow so do NOT do it in loops...
        }
        ((TextView)diag.FindViewById(Android.Resource.Id.Message)).MovementMethod = (Android.Text.Method.LinkMovementMethod.Instance);
    }

    private static bool HasManageStoragePermission(Context context)
    {
        bool hasExternalStoragePermissions = false;
        if ((int)Android.OS.Build.VERSION.SdkInt >= 30)
        {
            hasExternalStoragePermissions = Android.OS.Environment.IsExternalStorageManager;
        }
        else
        {
            hasExternalStoragePermissions = ContextCompat.CheckSelfPermission(context, Android.Manifest.Permission.ManageExternalStorage) != Android.Content.PM.Permission.Denied;
        }
        return hasExternalStoragePermissions;
    }

    private void ForceFilesystemPermission_Click(object sender, EventArgs e)
    {
        bool hasExternalStoragePermissions = HasManageStoragePermission(this);

        if (hasExternalStoragePermissions)
        {
            Toast.MakeText(this, SeekerState.ActiveActivityRef.GetString(Resource.String.permission_already_successfully_granted), ToastLength.Long).Show();
        }
        else
        {
            Intent allFilesPermission = new Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);
            Android.Net.Uri packageUri = Android.Net.Uri.FromParts("package", this.PackageName, null);
            allFilesPermission.SetData(packageUri);
            this.StartActivityForResult(allFilesPermission, FORCE_REQUEST_STORAGE_MANAGER);
        }
    }

    public const string FromBrowseSelf = "FromBrowseSelf";
    private void BrowseSelfButton_Click(object sender, EventArgs e)
    {
        BrowseSelf(false, false);
    }

    private void BrowseSelfButton_LongClick(object sender, View.LongClickEventArgs e)
    {
        var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(this, Resource.Style.MyAlertDialogTheme);
        var diag = builder.SetMessage(Resource.String.BrowseWhich)
            .SetPositiveButton(Resource.String.public_room, (object sender, DialogClickEventArgs e) => { BrowseSelf(true, false); OnCloseClick(sender, e); })
            .SetNegativeButton(Resource.String.target_user_list, (object sender, DialogClickEventArgs e) => { BrowseSelf(false, true); OnCloseClick(sender, e); })
            .Create();
        diag.Show();
    }

    private void BrowseSelf(bool forcePublic, bool forceFriend)
    {
        if (!SeekerState.SharingOn || SeekerState.SharedFileCache == null || UploadDirectoryManager.UploadDirectories.Count == 0)
        {
            Toast.MakeText(this, Resource.String.not_sharing, ToastLength.Short).Show();
            return;
        }
        if (SeekerState.IsParsing)
        {
            Toast.MakeText(this, Resource.String.WaitForParsing, ToastLength.Short).Show();
            return;
        }
        if (!SeekerState.SharedFileCache.SuccessfullyInitialized || SeekerState.SharedFileCache.GetBrowseResponseForUser(SeekerState.Username) == null)
        {
            Toast.MakeText(this, Resource.String.failed_to_parse_shares_post, ToastLength.Short).Show();
            return;
        }
        string errorMsgToToast = string.Empty;

        Soulseek.BrowseResponse browseResponseToShow = null;
        if (forcePublic)
        {
            browseResponseToShow = SeekerState.SharedFileCache.GetBrowseResponseForUser(null);
        }
        else if (forceFriend)
        {
            browseResponseToShow = SeekerState.SharedFileCache.GetBrowseResponseForUser(null, true);
        }
        else
        {
            browseResponseToShow = SeekerState.SharedFileCache.GetBrowseResponseForUser(SeekerState.Username);
        }

        TreeNode<Soulseek.Directory> tree = DownloadDialog.CreateTree(browseResponseToShow, false, null, null, SeekerState.Username, out errorMsgToToast);
        if (errorMsgToToast != null && errorMsgToToast != string.Empty)
        {
            Toast.MakeText(this, errorMsgToToast, ToastLength.Short).Show();
            return;
        }
        if (tree != null)
        {
            SeekerState.OnBrowseResponseReceived(SeekerState.SharedFileCache.GetBrowseResponseForUser(SeekerState.Username), tree, SeekerState.Username, null);
        }

        Intent intent = new Intent(SeekerState.ActiveActivityRef, typeof(MainActivity));
        intent.AddFlags(ActivityFlags.SingleTop);
        intent.PutExtra(FromBrowseSelf, 3); //the tab to go to
        this.StartActivity(intent);
    }

    public void ReconfigureOptionsApi(bool? allowPrivateInvites, bool? enableListener, int? newPort)
    {
        var requiresConnection = allowPrivateInvites.HasValue;
        
        // note: you CAN in fact change listening and port without being logged in...
        if (!SeekerState.currentlyLoggedIn && requiresConnection)
        {
            this.ShowShortToast(ResourceConstant.String.must_be_logged_to_toggle_priv_invites);
            return;
        }
        
        if (SeekerState.CurrentlyLoggedInButDisconnectedState() && requiresConnection)
        {
            // We disconnected. login then do the rest. This is due to temp lost connection
            var shouldContinue = SoulseekConnection
                .ShowMessageAndCreateReconnectTask(SeekerState.ActiveActivityRef, false, out var reconnectTask);
            if (!shouldContinue)
            {
                return;
            }
            
            reconnectTask.ContinueWith(previousTask =>
            {
                if (previousTask.IsFaulted)
                {
                    this.ShowShortToast(ResourceConstant.String.failed_to_connect);
                    return;
                }
                
                RunOnUiThread(() => ReconfigureOptionsLogic(allowPrivateInvites, enableListener, newPort));
            });

            return;
        }

        ReconfigureOptionsLogic(allowPrivateInvites, enableListener, newPort);
    }

    private void ReconfigureOptionsLogic(bool? allowPrivateInvites, bool? enableTheListener, int? listenerPort)
    {
        Task<bool> reconfigurationTask;
        try
        {
            var patch = new Soulseek.SoulseekClientOptionsPatch(
                acceptPrivateRoomInvitations: allowPrivateInvites,
                enableListener: enableTheListener,
                listenPort: listenerPort);

            reconfigurationTask = SeekerState.SoulseekClient.ReconfigureOptionsAsync(patch);
        }
        catch (Exception e)
        {   
            // this can still happen on ReqFiles_Click...
            // maybe for the first check we were logged in but for the second we somehow were not...
            
            Logger.FirebaseDebug("reconfigure options: " + e.Message + e.StackTrace);
            Logger.Debug("reconfigure options FAILED" + e.Message + e.StackTrace);
            return;
        }
        
        Action<Task<bool>> continueWithAction = reconfigureTask =>
        {
            SeekerState.ActiveActivityRef.RunOnUiThread(() =>
            {
                if (reconfigureTask.IsFaulted)
                {
                    Logger.Debug("reconfigure options FAILED");
                    if (allowPrivateInvites.HasValue)
                    {
                        var rawString = GetString(ResourceConstant.String.failed_setting_priv_invites);
                        var enabledDisabled = allowPrivateInvites.Value 
                            ? SeekerState.ActiveActivityRef.GetString(ResourceConstant.String.allowed)
                            : SeekerState.ActiveActivityRef.GetString(ResourceConstant.String.denied);
                        this.ShowLongToast(string.Format(rawString, enabledDisabled));
                        
                        if (SeekerState.ActiveActivityRef is SettingsActivity settingsActivity)
                        {
                            // set the check to false
                            // TODO: Move this to the settings fragment itself
                            settingsFragment.SetAllowPrivateRoomInvitations(SeekerState.AllowPrivateRoomInvitations);
                        }
                    }

                    if (enableTheListener.HasValue)
                    {
                        var rawString = GetString(ResourceConstant.String.network_error_setting_listener);
                        var enabledDisabled = enableTheListener.Value 
                            ? GetString(ResourceConstant.String.allowed) 
                            : GetString(ResourceConstant.String.denied);
                        this.ShowLongToast(string.Format(rawString, enabledDisabled));
                    }

                    if (!listenerPort.HasValue)
                    {
                        return;
                    }

                    var baseMessage = GetString(ResourceConstant.String.network_error_setting_listener_port);
                    this.ShowLongToast(string.Format(baseMessage, listenerPort.Value));
                }
                else if (allowPrivateInvites.HasValue)
                {
                    Logger.Debug("reconfigure options SUCCESS, restart required? " + reconfigureTask.Result);
                    SeekerState.AllowPrivateRoomInvitations = allowPrivateInvites.Value;
                    
                    // set shared prefs...
                    lock (SeekerApplication.SharedPrefLock)
                    {
                        GetSharedPreferences("SoulSeekPrefs", 0)!.Edit()!
                            .PutBoolean(ResourceConstant.String.key_allow_private_room_invites, 
                                allowPrivateInvites.Value)!
                            .Commit();
                    }
                }
            });
        };
        
        reconfigurationTask.ContinueWith(continueWithAction);
    }

    private void MoreInfoButton_Click(object sender, EventArgs e)
    {
        var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(this, Resource.Style.MyAlertDialogTheme);
        var diag = builder.SetMessage(Resource.String.sharing_dialog).SetPositiveButton(Resource.String.close, OnCloseClick).Create();
        diag.Show();
    }

    private void OnCloseClick(object sender, DialogClickEventArgs e)
    {
        (sender as AndroidX.AppCompat.App.AlertDialog).Dismiss();
    }

    private void UpdateShareImageView()
    {
        Tuple<SharingManager.SharingIcons, string> info = SharingManager.GetSharingMessageAndIcon(out bool isParsing);
        ImageView imageView = this.FindViewById<ImageView>(Resource.Id.sharedStatus);
        ProgressBar progressBar = this.FindViewById<ProgressBar>(Resource.Id.progressBarSharedStatus);
        if (imageView == null || progressBar == null) return;
        string toolTip = info.Item2;
        int numParsed = SeekerState.NumberParsed;
        if (isParsing && numParsed != 0)
        {
            if (numParsed == int.MaxValue) //our signal we are finishing up (i.e. creating token index)
            {
                toolTip = toolTip + $" ({SeekerApplication.ApplicationContext.GetString(Resource.String.finishingUp)})";
            }
            else
            {
                toolTip = toolTip + String.Format($" ({SeekerApplication.ApplicationContext.GetString(Resource.String.XFilesParsed)})", numParsed);
            }
        }
        if ((int)Android.OS.Build.VERSION.SdkInt >= 26)
        {
            imageView.TooltipText = toolTip; //api26+ otherwise crash...
            progressBar.TooltipText = toolTip;
        }
        else
        {
            AndroidX.AppCompat.Widget.TooltipCompat.SetTooltipText(imageView, toolTip);
            AndroidX.AppCompat.Widget.TooltipCompat.SetTooltipText(progressBar, toolTip);
        }
        
        switch (info.Item1)
        {
            case SharingManager.SharingIcons.On:
                imageView.SetImageResource(Resource.Drawable.ic_file_upload_black_24dp);
                break;
            case SharingManager.SharingIcons.Error:
                imageView.SetImageResource(Resource.Drawable.ic_error_outline_white_24dp);
                break;
            case SharingManager.SharingIcons.CurrentlyParsing:
                imageView.SetImageResource(Resource.Drawable.exclamation_thick);
                break;
            case SharingManager.SharingIcons.Off:
                imageView.SetImageResource(Resource.Drawable.ic_sharing_off_black_24dp);
                break;
            case SharingManager.SharingIcons.OffDueToNetwork:
                imageView.SetImageResource(Resource.Drawable.network_strength_off_outline);
                break;
        }

        switch (info.Item1)
        {
            case SharingManager.SharingIcons.CurrentlyParsing:
                progressBar.Visibility = ViewStates.Visible;
                break;
            default:
                progressBar.Visibility = ViewStates.Invisible;
                break;
        }

        // in case new errors to update.
        this.uploadsRecyclerViewFoldersAdapter?.NotifyDataSetChanged();
    }

    public override bool OnContextItemSelected(IMenuItem item)
    {
        if (item.ItemId == 1) //options
        {
            ShowDialogForUploadDir(ContextMenuItem);
        }
        else if (item.ItemId == 2) //remove
        {
            RemoveUploadDirFolder(ContextMenuItem);
        }
        return true;
    }

    private void RemoveUploadDirFolder(UploadDirectoryInfo uploadDirInfo)
    {
        if (UploadDirectoryManager.UploadDirectories.Count == 1)
        {
            this.ClearAllFolders(); //since now we have 0 this will just properly clear everything.
        }
        else
        {
            UploadDirectoryManager.UploadDirectories.Remove(uploadDirInfo);
            this.uploadsRecyclerViewFoldersAdapter.NotifyDataSetChanged();
            SetSharedFolderView();
            Rescan(null, -1, UploadDirectoryManager.AreAnyFromLegacy(), false);
        }
    }


    private void ShareCheckBox_CheckedChange(object sender, CompoundButton.CheckedChangeEventArgs e)
    {
        SeekerState.SharingOn = e.IsChecked;
        SharingManager.SetUnsetSharingBasedOnConditions(true);
        if (SharingManager.MeetsSharingConditions() && !SeekerState.IsParsing && !SharingManager.IsSharingSetUpSuccessfully())
        {
            //try to set up sharing...
            SharingManager.SetUpSharing(this, UpdateShareImageView);
        }
        UpdateShareImageView();
        UpdateSharingViewState();
        SetSharedFolderView();
        this.uploadsRecyclerViewFoldersAdapter?.NotifyDataSetChanged(); //so that the views rebind as unclickable.
    }

    private void UpdateSharingViewState()
    {
        //this isnt winforms where disabling parent, disables all children..

        if (SeekerState.SharingOn)
        {
            sharingSubLayout1.Enabled = true;
            sharingSubLayout1.Alpha = 1.0f;
            sharingSubLayout2.Enabled = true;
            sharingSubLayout2.Alpha = 1.0f;
            addFolderButton.Clickable = true;
            clearAllFoldersButton.Clickable = true;
            browseSelfButton.Clickable = true;
            browseSelfButton.LongClickable = true;
            rescanSharesButton.Clickable = true;
        }
        else
        {
            sharingSubLayout1.Enabled = false;
            sharingSubLayout1.Alpha = 0.5f;
            sharingSubLayout2.Enabled = false;
            sharingSubLayout2.Alpha = 0.5f;
            addFolderButton.Clickable = false;
            clearAllFoldersButton.Clickable = false;
            browseSelfButton.Clickable = false;
            browseSelfButton.LongClickable = false;
            rescanSharesButton.Clickable = false;
        }
    }

    private void UpdateListeningViewState()
    {
        if (SeekerState.ListenerEnabled)
        {
            listeningSubLayout2.Enabled = true;
            listeningSubLayout3.Enabled = true;
            listeningSubLayout2.Alpha = 1.0f;
            listeningSubLayout3.Alpha = 1.0f;
            useUPnPCheckBox.Clickable = true;
            changePort.Clickable = true;
            checkStatus.Clickable = true;
        }
        else
        {
            listeningSubLayout2.Enabled = false;
            listeningSubLayout3.Enabled = false;
            listeningSubLayout2.Alpha = 0.5f;
            listeningSubLayout3.Alpha = 0.5f;
            useUPnPCheckBox.Clickable = false;
            changePort.Clickable = false;
            checkStatus.Clickable = false;
        }
    }

    private void UpdateConcurrentDownloadLimitsState()
    {
        if (Soulseek.SimultaneousDownloadsGatekeeper.RestrictConcurrentUsers)
        {
            concurrentDlSublayout.Enabled = true;
            concurrentDlSublayout.Alpha = 1.0f;
            concurrentDlButton.Clickable = true;
            concurrentDlButton.Alpha = 1.0f;
        }
        else
        {
            concurrentDlSublayout.Enabled = false;
            concurrentDlSublayout.Alpha = 0.5f;
            concurrentDlButton.Clickable = false;
            concurrentDlButton.Alpha = 0.5f;
        }
    }

    private void UpdateSpeedLimitsState()
    {
        if (SeekerState.SpeedLimitDownloadOn)
        {
            limitDlSpeedSubLayout.Enabled = true;
            limitDlSpeedSubLayout.Alpha = 1.0f;
            dlSpeedTextView.Alpha = 1.0f;
            changeDlSpeed.Alpha = 1.0f;
            changeDlSpeed.Clickable = true;
            dlLimitPerTransfer.Alpha = 1.0f;
            dlLimitPerTransfer.Clickable = true;
            dlLimitPerTransfer.Enabled = true;
        }
        else
        {
            limitDlSpeedSubLayout.Enabled = false;
            limitDlSpeedSubLayout.Alpha = 0.5f;
            dlSpeedTextView.Alpha = 0.5f;
            changeDlSpeed.Alpha = 0.5f;
            changeDlSpeed.Clickable = false;
            dlLimitPerTransfer.Alpha = 0.5f;
            dlLimitPerTransfer.Clickable = false;
            dlLimitPerTransfer.Enabled = false;
        }

        if (SeekerState.SpeedLimitUploadOn)
        {
            limitUlSpeedSubLayout.Enabled = true;
            limitUlSpeedSubLayout.Alpha = 1.0f;
            ulSpeedTextView.Alpha = 1.0f;
            changeUlSpeed.Alpha = 1.0f;
            changeUlSpeed.Clickable = true;
            ulLimitPerTransfer.Alpha = 1.0f;
            ulLimitPerTransfer.Clickable = true;
            ulLimitPerTransfer.Enabled = true;
        }
        else
        {
            limitUlSpeedSubLayout.Enabled = false;
            limitUlSpeedSubLayout.Alpha = 0.5f;
            ulSpeedTextView.Alpha = 0.5f;
            changeUlSpeed.Alpha = 0.5f;
            changeUlSpeed.Clickable = false;
            ulLimitPerTransfer.Alpha = 0.5f;
            ulLimitPerTransfer.Clickable = false;
            ulLimitPerTransfer.Enabled = false;
        }
    }

    private void ImageView_Click(object sender, EventArgs e)
    {
        UpdateShareImageView();
        (sender as View).PerformLongClick();
    }

    private void RestoreDefaults_Click(object sender, EventArgs e)
    {
        SeekerState.NumberSearchResults.Reset();
        SeekerState.AutoClearCompleteDownloads.Reset();
        SeekerState.AutoClearCompleteUploads = false;
        SeekerState.RememberSearchHistory.Reset();
        SeekerState.ShowRecentUsers = true;
        SeekerState.SharingOn = false;
        SeekerState.FreeUploadSlotsOnly.Reset();
        SeekerState.DisableDownloadToastNotification = true;
        SeekerState.FileBackedDownloads.Reset();
        SeekerState.DayNightMode = AppCompatDelegate.ModeNightFollowSystem;
        SeekerState.HideLockedResultsInBrowse.Reset();
        SeekerState.HideLockedResultsInSearch.Reset();
        // TODO: (FindViewById<CheckBox>(Resource.Id.autoClearComplete) as CheckBox).Checked = SeekerState.AutoClearCompleteDownloads;
        // TODO: (FindViewById<CheckBox>(Resource.Id.autoClearCompleteUploads) as CheckBox).Checked = SeekerState.AutoClearCompleteUploads;
        // TODO: (FindViewById<CheckBox>(Resource.Id.searchHistoryRemember) as CheckBox).Checked = SeekerState.RememberSearchHistory;
        // TODO: (FindViewById<CheckBox>(Resource.Id.rememberRecentUsers) as CheckBox).Checked = SeekerState.ShowRecentUsers;
        (FindViewById<CheckBox>(Resource.Id.enableSharing) as CheckBox).Checked = SeekerState.SharingOn;
        // TODO: (FindViewById<CheckBox>(Resource.Id.freeUploadSlots) as CheckBox).Checked = SeekerState.FreeUploadSlotsOnly;
        // TODO: (FindViewById<CheckBox>(Resource.Id.showLockedInBrowseResponse) as CheckBox).Checked = !SeekerState.HideLockedResultsInBrowse;
        // TODO: (FindViewById<CheckBox>(Resource.Id.showLockedInSearch) as CheckBox).Checked = !SeekerState.HideLockedResultsInSearch;
        // TODO: (FindViewById<CheckBox>(Resource.Id.showToastNotificationOnDownload) as CheckBox).Checked = SeekerState.DisableDownloadToastNotification;
        // TODO: (FindViewById<CheckBox>(Resource.Id.memoryFileDownloadSwitchCheckBox) as CheckBox).Checked = !SeekerState.MemoryBackedDownload;
        // TODO: SetSpinnerPosition(searchNumSpinner);
        // TODO: Spinner daynightSpinner = FindViewById<Spinner>(Resource.Id.nightModeSpinner);
        // TODO: SetSpinnerPositionDayNight(daynightSpinner);
    }

    private bool needsMediaStorePermission()
    {
        if ((int)Android.OS.Build.VERSION.SdkInt >= 33)
        {
            return AndroidX.Core.Content.ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.ReadMediaAudio) == Android.Content.PM.Permission.Denied;
        }
        else
        {
            return AndroidX.Core.Content.ContextCompat.CheckSelfPermission(this, Android.Manifest.Permission.ReadExternalStorage) == Android.Content.PM.Permission.Denied;
        }
    }

    private void requestMediaStorePermission()
    {
        if ((int)Android.OS.Build.VERSION.SdkInt >= 33)
        {
            AndroidX.Core.App.ActivityCompat.RequestPermissions(this, new string[] { Android.Manifest.Permission.ReadMediaAudio }, READ_EXTERNAL_FOR_MEDIA_STORE);
        }
        else
        {
            AndroidX.Core.App.ActivityCompat.RequestPermissions(this, new string[] { Android.Manifest.Permission.ReadExternalStorage }, READ_EXTERNAL_FOR_MEDIA_STORE);
        }
    }

    private void AddUploadDirectory(object sender, EventArgs e)
    {
        // We request ReadExternalStorage so that we can query the media store to get music attributes (duration, bitrate)
        //   quickly (i.e. without having to load the file from disk and read attributes).
        // API 33 (Android 13) target - this permission has no effect.  Instead use the granular ReadMediaAudio since we only 
        //   use the media store for audio anyway.  If we were previously granted ReadExternalStorage then we get ReadMedia* 
        //   automatically when upgrading.

        //you dont have this on api >= 29 because you never requested it, but it is NECESSARY to read media store
        if (needsMediaStorePermission())
        {
            //if they deny the permission twice and are on api >= 30, then it will auto deny (behavior is the same as if they manually clicked deny).
            requestMediaStorePermission();
        }
        else
        {
            ShowDirSettings(null, DirectoryType.Upload);
        }
    }

    private void UseInternalFilePicker(int requestCode)
    {
        //Create FolderOpenDialog
        SimpleFileDialog fileDialog = new SimpleFileDialog(this, SimpleFileDialog.FileSelectionMode.FolderChoose);
        fileDialog.GetFileOrDirectoryAsync(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath).ContinueWith(
            (Task<string> t) =>
            {
                if (t.Result == null || t.Result == string.Empty)
                {
                    return;
                }
                else
                {
                    Android.Net.Uri uri = Android.Net.Uri.FromFile(new Java.IO.File(t.Result));
                    DocumentFile f = DocumentFile.FromFile(new Java.IO.File(t.Result)); //from tree uri not added til 21 also.  from single uri returns a f.Exists=false file.
                    if (f == null)
                    {
                        Logger.FirebaseDebug("api<21 f is null");
                        SeekerState.ActiveActivityRef.RunOnUiThread(() => { Toast.MakeText(this, Resource.String.error_reading_dir, ToastLength.Long).Show(); });
                        return;
                    }
                    else if (!f.Exists())
                    {
                        Logger.FirebaseDebug("api<21 f does not exist");
                        SeekerState.ActiveActivityRef.RunOnUiThread(() => { Toast.MakeText(this, Resource.String.error_reading_dir, ToastLength.Long).Show(); });
                        return;
                    }
                    else if (!f.IsDirectory)
                    {
                        Logger.FirebaseDebug("api<21 NOT A DIRECTORY");
                        SeekerState.ActiveActivityRef.RunOnUiThread(() => { Toast.MakeText(this, Resource.String.error_not_a_dir, ToastLength.Long).Show(); });
                        return;
                    }

                    if (requestCode == CHANGE_WRITE_EXTERNAL_LEGACY)
                    {
                        this.SuccessfulWriteExternalLegacyCallback(uri, true);
                    }
                    else if (requestCode == UPLOAD_DIR_ADD_WRITE_EXTERNAL_LEGACY)
                    {
                        this.Rescan(uri, requestCode, true);
                    }
                    else if (requestCode == CHANGE_INCOMPLETE_EXTERNAL_LEGACY)
                    {
                        this.SuccessfulIncompleteExternalLegacyCallback(uri, true);
                    }
                }


            });
    }
    
    public void ShowDirSettings(string startingDirectory, DirectoryType directoryType, bool errorReselectCase = false)
    {
        int requestCode = -1;
        if (SeekerState.UseLegacyStorage())
        {
            var legacyIntent = new Intent(Intent.ActionOpenDocumentTree);
            if (!string.IsNullOrEmpty(startingDirectory))
            {
                Android.Net.Uri res = Android.Net.Uri.Parse(startingDirectory);
                legacyIntent.PutExtra(DocumentsContract.ExtraInitialUri, res);
            }
            legacyIntent.AddFlags(ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPrefixUriPermission);
            if (directoryType == DirectoryType.Download)
            {
                requestCode = CHANGE_WRITE_EXTERNAL_LEGACY;
            }
            else if (directoryType == DirectoryType.Upload)
            {
                if (errorReselectCase)
                {
                    requestCode = UPLOAD_DIR_ADD_WRITE_EXTERNAL_LEGACY_RESELECT_CASE;
                }
                else
                {
                    requestCode = UPLOAD_DIR_ADD_WRITE_EXTERNAL_LEGACY;
                }
            }
            else if (directoryType == DirectoryType.Incomplete)
            {
                requestCode = CHANGE_INCOMPLETE_EXTERNAL_LEGACY;
            }
            try
            {
                this.StartActivityForResult(legacyIntent, requestCode);
            }
            catch (Exception e)
            {
                if (e.Message.Contains(CommonHelpers.NoDocumentOpenTreeToHandle))
                {
                    FallbackFileSelectionEntry(requestCode);
                }
                else
                {
                    Logger.FirebaseDebug("showDirSettings: " + e.Message + e.StackTrace);
                    throw e;
                }
            }
        }
        else
        {
            var storageManager = Android.OS.Storage.StorageManager.FromContext(this);
            var intent = storageManager.PrimaryStorageVolume.CreateOpenDocumentTreeIntent();
            intent.AddFlags(ActivityFlags.GrantPersistableUriPermission | ActivityFlags.GrantReadUriPermission | ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantPrefixUriPermission);
            if (!string.IsNullOrEmpty(startingDirectory))
            {
                Android.Net.Uri res = Android.Net.Uri.Parse(startingDirectory);
                intent.PutExtra(DocumentsContract.ExtraInitialUri, res);
            }
            if (directoryType == DirectoryType.Download)
            {
                requestCode = CHANGE_WRITE_EXTERNAL;
            }
            else if (directoryType == DirectoryType.Upload)
            {
                if (errorReselectCase)
                {
                    requestCode = UPLOAD_DIR_ADD_WRITE_EXTERNAL_RESELECT_CASE;
                }
                else
                {
                    requestCode = UPLOAD_DIR_ADD_WRITE_EXTERNAL;
                }
            }
            else if (directoryType == DirectoryType.Incomplete)
            {
                requestCode = CHANGE_INCOMPLETE_EXTERNAL;
            }
            try
            {
                this.StartActivityForResult(intent, requestCode);
            }
            catch (Exception e)
            {
                if (e.Message.Contains(CommonHelpers.NoDocumentOpenTreeToHandle))
                {
                    FallbackFileSelectionEntry(requestCode);
                }
                else
                {
                    Logger.FirebaseDebug("showDirSettings: " + e.Message + e.StackTrace);
                    throw e;
                }
            }
        }
    }

    private int ConvertRequestCodeIntoLegacyVersion(int requestCodeNotLegacy)
    {
        switch (requestCodeNotLegacy)
        {
            case UPLOAD_DIR_ADD_WRITE_EXTERNAL:
                return UPLOAD_DIR_ADD_WRITE_EXTERNAL_LEGACY;
            case CHANGE_INCOMPLETE_EXTERNAL:
                return CHANGE_INCOMPLETE_EXTERNAL_LEGACY;
            case CHANGE_WRITE_EXTERNAL:
                return CHANGE_WRITE_EXTERNAL_LEGACY;
            case UPLOAD_DIR_ADD_WRITE_EXTERNAL_RESELECT_CASE:
                return UPLOAD_DIR_ADD_WRITE_EXTERNAL_LEGACY_RESELECT_CASE;
            default:
                return requestCodeNotLegacy;
        }
    }

    public static bool DoWeHaveProperPermissionsForInternalFilePicker()
    {
        if (SeekerState.RequiresEitherOpenDocumentTreeOrManageAllFiles())
        {
            return Android.OS.Environment.IsExternalStorageManager;
        }
        else
        {
            return true; //since in this case its ContextCompat.CheckSelfPermission(this, Manifest.Permission.WriteExternalStorage) == Android.Content.PM.Permission.Denied. which we already request if user does not have it since its needed to download.
        }
    }

    private void FallbackFileSelectionEntry(int requestCode)
    {
        requestCode = ConvertRequestCodeIntoLegacyVersion(requestCode);

        bool hasManageAllFilesManisfestPermission = false;

#if IzzySoft
            hasManageAllFilesManisfestPermission = true;
#endif

        if (SeekerState.RequiresEitherOpenDocumentTreeOrManageAllFiles() && hasManageAllFilesManisfestPermission && !Android.OS.Environment.IsExternalStorageManager) //this is "step 1"
        {
            Intent allFilesPermission = new Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);
            Android.Net.Uri packageUri = Android.Net.Uri.FromParts("package", this.PackageName, null);
            allFilesPermission.SetData(packageUri);
            this.StartActivityForResult(allFilesPermission, requestCode + 32);
        }
        else if (DoWeHaveProperPermissionsForInternalFilePicker())  //isExternalStorageManager added in API30, but RequiresEitherOpenDocumentTreeOrManageAllFiles protects against that being called on pre 30 devices.
        {
            UseInternalFilePicker(requestCode);
        }
        else
        {
            // show error message...
            if (SeekerState.RequiresEitherOpenDocumentTreeOrManageAllFiles() 
                && !hasManageAllFilesManisfestPermission)
            {
                this.ShowSimpleAlertDialog(
                    ResourceConstant.String.error_no_file_manager_dir_manage_storage, 
                    ResourceConstant.String.okay);
            }
            else
            {
                Toast.MakeText(this, SeekerState.ActiveActivityRef.GetString(Resource.String.error_no_file_manager_dir), ToastLength.Long).Show();
            }
        }
    }
    
    private void SuccessfulWriteExternalLegacyCallback(Android.Net.Uri uri, bool fromLegacyPicker = false)
    {
        SeekerState.SaveDataDirectoryUri = uri.ToString();
        SeekerState.SaveDataDirectoryUriIsFromTree = !fromLegacyPicker;
        var docFile = fromLegacyPicker ? DocumentFile.FromFile(new Java.IO.File(uri.Path)) : DocumentFile.FromTreeUri(this, uri);
        
        SeekerState.RootDocumentFile = docFile;
        RunOnUiThread(() =>
        {
            SeekerState.DirectoryUpdatedEvent?.Invoke(null, new EventArgs());
            Toast.MakeText(this, string.Format(this.GetString(Resource.String.successfully_changed_dl_dir), uri.Path), ToastLength.Long).Show();
        });
    }

    public static bool UseTempDirectory()
    {
        return !UseIncompleteManualFolder() && !SeekerState.CreateCompleteAndIncompleteFolders.Value;
    }

    private void SuccessfulIncompleteExternalLegacyCallback(Android.Net.Uri uri, bool fromLegacyPicker = false)
    {
        SeekerState.ManualIncompleteDataDirectoryUri = uri.ToString();
        SeekerState.ManualIncompleteDataDirectoryUriIsFromTree = !fromLegacyPicker;
        var docFile = fromLegacyPicker ? DocumentFile.FromFile(new Java.IO.File(uri.Path)) : DocumentFile.FromTreeUri(this, uri);
        
        SeekerState.RootIncompleteDocumentFile = docFile;
        
        RunOnUiThread(new Action(() =>
        {
            SeekerState.DirectoryUpdatedEvent?.Invoke(null, new EventArgs());
            Toast.MakeText(this, string.Format(this.GetString(Resource.String.successfully_changed_incomplete_dir), uri.Path), ToastLength.Long).Show();
        }));
    }

    public void ShowDialogForUploadDir(UploadDirectoryInfo uploadInfo)
    {
        if (uploadInfo.HasError())
        {
            ShowUploadDirectoryErrorDialog(uploadInfo);
        }
        else
        {
            ShowUploadDirectoryOptionsDialog(uploadInfo);
        }
    }
    
    private static UploadDirectoryInfo UploadDirToReplaceOnReselect = null;
    
    public void ShowUploadDirectoryErrorDialog(UploadDirectoryInfo uploadInfo)
    {
        var builder = new AlertDialog.Builder(this, Resource.Style.MyAlertDialogTheme);
        builder.SetTitle(ResourceConstant.String.FolderError);
        
        var diagMessage = GetString(ResourceConstant.String.ErrorForFolder) 
                          + uploadInfo.GetLastPathSegment()
                          + System.Environment.NewLine
                          + UploadDirectoryManager.GetErrorString(this, uploadInfo.ErrorState)
                          + System.Environment.NewLine;
        
        var diag = builder!
            .SetMessage(diagMessage)!
            .SetNegativeButton(Resource.String.RemoveFolder, (sender, e) =>
            { 
                // puts it slightly right
                RemoveUploadDirFolder(uploadInfo);
                OnCloseClick(sender, e);
            })
            .SetPositiveButton(Resource.String.Reselect, (sender, e) =>
            { 
                // puts it rightmost
                UploadDirToReplaceOnReselect = uploadInfo;
                ShowDirSettings(uploadInfo.UploadDataDirectoryUri, DirectoryType.Upload, true);
                OnCloseClick(sender, e);
            })
            .SetNeutralButton(Resource.String.cancel, OnCloseClick) //puts it leftmost
            .Create();
        
        diag.Show();
    }

    public void ShowUploadDirectoryOptionsDialog(UploadDirectoryInfo uploadDirInfo)
    {
        AlertDialog.Builder builder = new AlertDialog.Builder(this, ResourceConstant.Style.MyAlertDialogTheme); //used to be our cached main activity ref...
        builder.SetTitle(ResourceConstant.String.UploadFolderOptions);
        View viewInflated = LayoutInflater.From(this).Inflate(ResourceConstant.Layout.upload_folder_options, FindViewById<ViewGroup>(Android.Resource.Id.Content), false);
        EditText custromFolderNameEditText = viewInflated.FindViewById<EditText>(ResourceConstant.Id.customFolderNameEditText);
        CheckBox overrideFolderName = viewInflated.FindViewById<CheckBox>(ResourceConstant.Id.overrideFolderName);
        CheckBox hiddenCheck = viewInflated.FindViewById<CheckBox>(ResourceConstant.Id.hiddenUserlistOnly);
        CheckBox lockedCheck = viewInflated.FindViewById<CheckBox>(ResourceConstant.Id.lockedUserlistOnly);
        overrideFolderName.CheckedChange += (sender, e) =>
        {
            if (e.IsChecked)
            {
                custromFolderNameEditText.Enabled = true;
                custromFolderNameEditText.Alpha = 1.0f;
            }
            else
            {
                custromFolderNameEditText.Enabled = false;
                custromFolderNameEditText.Alpha = 0.5f;
            }


        };
        if (!string.IsNullOrEmpty(uploadDirInfo.DisplayNameOverride))
        {
            custromFolderNameEditText.Text = uploadDirInfo.DisplayNameOverride;
            overrideFolderName.Checked = true;
        }
        else
        {
            overrideFolderName.Checked = false;
        }
        hiddenCheck.Checked = uploadDirInfo.IsHidden;
        lockedCheck.Checked = uploadDirInfo.IsLocked;

        builder.SetView(viewInflated);

        EventHandler<DialogClickEventArgs> eventHandlerOkay = (sender, _) =>
        {
            bool hiddenChanged = uploadDirInfo.IsHidden != hiddenCheck.Checked;
            bool lockedChanged = uploadDirInfo.IsLocked != lockedCheck.Checked;
            bool overrideNameChanged =
                (string.IsNullOrEmpty(uploadDirInfo.DisplayNameOverride) && overrideFolderName.Checked && !string.IsNullOrEmpty(custromFolderNameEditText.Text)) ||
                ((!overrideFolderName.Checked || string.IsNullOrEmpty(custromFolderNameEditText.Text)) && !string.IsNullOrEmpty(uploadDirInfo.DisplayNameOverride)) ||
                (overrideFolderName.Checked && uploadDirInfo.DisplayNameOverride != custromFolderNameEditText.Text);

            uploadDirInfo.IsHidden = hiddenCheck.Checked;
            uploadDirInfo.IsLocked = lockedCheck.Checked;
            string displayNameOld = uploadDirInfo.DisplayNameOverride;

            if (overrideFolderName.Checked && !string.IsNullOrEmpty(custromFolderNameEditText.Text))
            {
                if (uploadDirInfo.DisplayNameOverride != custromFolderNameEditText.Text)
                {
                    //make sure that we CAN change it.
                    uploadDirInfo.DisplayNameOverride = custromFolderNameEditText.Text;
                    if (!UploadDirectoryManager.DoesNewDirectoryHaveUniqueRootName(uploadDirInfo, false))
                    {
                        uploadDirInfo.DisplayNameOverride = displayNameOld;
                        this.ShowLongToast(ResourceConstant.String.CannotChangeNameNotUnique);
                        overrideNameChanged = false; // we prevented it
                    }
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(uploadDirInfo.DisplayNameOverride))
                {
                    //make sure that we CAN change it.
                    uploadDirInfo.DisplayNameOverride = null;
                    if (!UploadDirectoryManager.DoesNewDirectoryHaveUniqueRootName(uploadDirInfo, false))
                    {
                        uploadDirInfo.DisplayNameOverride = displayNameOld;
                        this.ShowLongToast(ResourceConstant.String.CannotChangeNameNotUnique);
                        overrideNameChanged = false; // we prevented it
                    }
                }
            }

            uploadsRecyclerViewFoldersAdapter.NotifyDataSetChanged();
            if (hiddenChanged || lockedChanged || overrideNameChanged)
            {
                Logger.Debug("things changed re: folder options..");
                Rescan(null, -1, UploadDirectoryManager.AreAnyFromLegacy());
            }

            if (sender is AlertDialog aDiag)
            {
                aDiag.Dismiss();
            }
        };

        builder.SetPositiveButton(ResourceConstant.String.okay, eventHandlerOkay);
        var diag = builder.Create();
        diag.Show();
    }

    public static EventHandler<EventArgs> UploadDirectoryChanged;
    public static volatile bool MoreChangesHaveBeenMadeSoRescanWhenDone;

    public void ParseDatabaseAndUpdateUI(Android.Net.Uri newlyAddedUriIfApplicable, int requestCode, bool fromLegacyPicker = false, bool rescanClicked = false, bool reselectCase = false)
    {

        if (rescanClicked)
        {
            if (SeekerState.IsParsing)
            {
                this.RunOnUiThread(new Action(() =>
                {
                    Toast.MakeText(this, Resource.String.AlreadyParsing, ToastLength.Long).Show();
                }));
                return;
            }
            if (UploadDirectoryManager.UploadDirectories.Count == 0)
            {
                this.RunOnUiThread(new Action(() =>
                {
                    Toast.MakeText(this, Resource.String.DirectoryNotSet, ToastLength.Long).Show();
                }));
                return;
            }
        }

        if (rescanClicked || newlyAddedUriIfApplicable != null)
        {
            this.RunOnUiThread(new Action(() =>
            {
                Toast.MakeText(this, Resource.String.parsing_files_wait, ToastLength.Long).Show();
            }));
        }



        UploadDirectoryInfo newlyAddedDirectory = null;
        if (newlyAddedUriIfApplicable != null)
        {
            //RESELECT CASE
            if (reselectCase)
            {
                newlyAddedDirectory = new UploadDirectoryInfo(newlyAddedUriIfApplicable.ToString(), !fromLegacyPicker, UploadDirToReplaceOnReselect.IsLocked, UploadDirToReplaceOnReselect.IsHidden, UploadDirToReplaceOnReselect.DisplayNameOverride);
                newlyAddedDirectory.UploadDirectory = fromLegacyPicker ? DocumentFile.FromFile(new Java.IO.File(newlyAddedUriIfApplicable.Path)) : DocumentFile.FromTreeUri(this, newlyAddedUriIfApplicable);
                UploadDirectoryManager.UploadDirectories.Remove(UploadDirToReplaceOnReselect);
            }
            else
            {
                newlyAddedDirectory = new UploadDirectoryInfo(newlyAddedUriIfApplicable.ToString(), !fromLegacyPicker, false, false, null);
                newlyAddedDirectory.UploadDirectory = fromLegacyPicker ? DocumentFile.FromFile(new Java.IO.File(newlyAddedUriIfApplicable.Path)) : DocumentFile.FromTreeUri(this, newlyAddedUriIfApplicable);
            }



            var anyNewlyAdded = UploadDirectoryManager.UploadDirectories
                .Any(up => up.UploadDataDirectoryUri == newlyAddedUriIfApplicable.ToString());
            
            if (anyNewlyAdded)
            {
                // error!!
                this.ShowLongToast(ResourceConstant.String.ErrorAlreadyAdded);
                return;
            }

            UploadDirectoryManager.UploadDirectories.Add(newlyAddedDirectory);
        }

        UploadDirectoryManager.UpdateWithDocumentFileAndErrorStates(this);
        if (UploadDirectoryManager.AreAllFailed())
        {
            throw new DirectoryAccessFailure("All Failed");
        }

        if (newlyAddedDirectory != null)
        {
            bool isUnqiue = UploadDirectoryManager.DoesNewDirectoryHaveUniqueRootName(newlyAddedDirectory, true);
            if (!isUnqiue)
            {
                Logger.Debug("Root name was not unique. Updated it to be unique.");
            }
            UploadDirectoryChanged?.Invoke(null, new EventArgs());
        }


        if (SeekerState.IsParsing)
        {
            Logger.Debug("We are already parsing!!! so after this parse, lets parse again with our cached results to pick up our new changes");
            MoreChangesHaveBeenMadeSoRescanWhenDone = true;
            return;
        }

        try
        {
            Logger.Debug("Parsing now......");

            SeekerState.IsParsing = true;
            int prevFiles = -1;
            bool success = false;
            if (rescanClicked && SeekerState.SharedFileCache != null)
            {
                prevFiles = SeekerState.SharedFileCache.FileCount;
            }
            this.RunOnUiThread(new Action(() =>
            {
                UpdateShareImageView(); //for is parsing..
                SetSharedFolderView();
            }));
            try
            {

                success = SharedCacheManager.InitializeDatabase(this, false, out var errorMessage);
                if (!success)
                {
                    throw new Exception("Failed to parse shared files: " + errorMessage);
                }
                SeekerState.IsParsing = false;
            }
            catch (Exception e)
            {
                SeekerState.IsParsing = false;
                    
                SharedCacheManager.ClearLegacyParsedCacheResults();
                SharedCacheManager.ClearParsedCacheResults(SeekerState.ActiveActivityRef);
                SharingManager.SetUnsetSharingBasedOnConditions(true);
                if (!(e is DirectoryAccessFailure))
                {
                    Logger.FirebaseDebug("error parsing: " + e.Message + "  " + e.StackTrace);
                }
                this.RunOnUiThread(new Action(() =>
                {
                    UpdateShareImageView();
                    SetSharedFolderView();
                    if (!(e is DirectoryAccessFailure))
                    {
                        Toast.MakeText(this, e.Message, ToastLength.Long).Show();
                    }
                    else
                    {
                        Toast.MakeText(this, Resource.String.FailedGettingAccess, ToastLength.Long).Show(); //TODO get error from UploadManager..
                    }

                }));
                UploadDirectoryChanged?.Invoke(null, new EventArgs());
                return;
            }
            
            if ((UPLOAD_DIR_ADD_WRITE_EXTERNAL == requestCode || UPLOAD_DIR_ADD_WRITE_EXTERNAL_RESELECT_CASE == requestCode) && newlyAddedUriIfApplicable != null)
            {
                ContentResolver!.TakePersistableUriPermission(newlyAddedUriIfApplicable, ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);
            }
            //setup soulseek client with handlers if all conditions met
            SharingManager.SetUnsetSharingBasedOnConditions(true, true);
            this.RunOnUiThread(new Action(() =>
            {
                UpdateShareImageView();
                SetSharedFolderView();
                int dirs = SeekerState.SharedFileCache.DirectoryCount; //TODO: nullref here... U318AA, LG G7 ThinQ, both android 10
                int files = SeekerState.SharedFileCache.FileCount;
                string msg = string.Format(this.GetString(Resource.String.success_setting_shared_dir_fnum_dnum), dirs, files);
                if (rescanClicked) //tack on additional message if applicable..
                {
                    int diff = files - prevFiles;
                    if (diff > 0)
                    {
                        if (diff > 1)
                        {
                            msg = msg + String.Format(" " + SeekerApplication.ApplicationContext.GetString(Resource.String.AdditionalFiles), diff);
                        }
                        else
                        {
                            msg = msg + " " + SeekerApplication.ApplicationContext.GetString(Resource.String.OneAdditionalFile);
                        }
                    }
                }
                Toast.MakeText(this, msg, ToastLength.Long).Show();
            }));
        }
        finally
        {
            SeekerState.IsParsing = false;
            if (MoreChangesHaveBeenMadeSoRescanWhenDone)
            {
                Logger.Debug("okay now lets pick up our new changes");
                MoreChangesHaveBeenMadeSoRescanWhenDone = false;
                ParseDatabaseAndUpdateUI(null, requestCode, fromLegacyPicker, false);
            }
        }
    }


    /// <summary>
    /// We always use the previous metadata info if its there. so we always kind of "rescan"
    /// </summary>
    /// <param name="newlyAddedUriIfApplicable"></param>
    /// <param name="requestCode"></param>
    /// <param name="fromLegacyPicker"></param>
    /// <param name="rescanClicked"></param>
    private void Rescan(Android.Net.Uri newlyAddedUriIfApplicable, int requestCode, bool fromLegacyPicker = false, bool rescanClicked = false, bool reselectCase = false)
    {
        Action parseDatabaseAndUpdateUiAction = new Action(() =>
        {
            try
            {
                ParseDatabaseAndUpdateUI(newlyAddedUriIfApplicable, requestCode, fromLegacyPicker, rescanClicked, reselectCase);
            }
            catch (DirectoryAccessFailure)
            {
                if (rescanClicked)
                {
                    SeekerState.ActiveActivityRef.RunOnUiThread(() => { Toast.MakeText(this, Resource.String.SharedFolderIssuesAllFailed, ToastLength.Long).Show(); });
                }
                else
                {
                    throw;
                }
            }
        });

        System.Threading.ThreadPool.QueueUserWorkItem((object o) => { parseDatabaseAndUpdateUiAction(); });
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions, [GeneratedEnum] Permission[] grantResults)
    {
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (READ_EXTERNAL_FOR_MEDIA_STORE == requestCode)
        {
            if (grantResults.Length > 0 && grantResults[0] == Permission.Granted) //still let them do it. important for auto-deny case.
            {
                ShowDirSettings(null, DirectoryType.Upload);
            }
            else
            {
                Toast.MakeText(SeekerState.ActiveActivityRef, Resource.String.NoMediaStore, ToastLength.Short).Show();
                ShowDirSettings(null, DirectoryType.Upload);
            }
        }
    }

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        //if from manage external settings
        if (CHANGE_WRITE_EXTERNAL_LEGACY == requestCode - 32 || CHANGE_INCOMPLETE_EXTERNAL_LEGACY == requestCode - 32 || UPLOAD_DIR_ADD_WRITE_EXTERNAL_LEGACY == requestCode - 32)
        {
            if (SettingsActivity.DoWeHaveProperPermissionsForInternalFilePicker())
            {
                //phase 2 - actually pick a file.
                UseInternalFilePicker(requestCode - 32);
            }
            else
            {
                Toast.MakeText(this, Resource.String.NoPermissionsForDir, ToastLength.Long).Show();
            }
        }


        if (CHANGE_WRITE_EXTERNAL == requestCode)
        {
            if (resultCode == Result.Ok)
            {
                var x = data.Data;
                SeekerState.RootDocumentFile = DocumentFile.FromTreeUri(this, data.Data);
                SeekerState.SaveDataDirectoryUri = data.Data.ToString();
                SeekerState.SaveDataDirectoryUriIsFromTree = true;
                this.ContentResolver.TakePersistableUriPermission(data.Data, ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);
                this.RunOnUiThread(new Action(() =>
                {
                    SeekerState.DirectoryUpdatedEvent?.Invoke(null, new EventArgs());
                    Toast.MakeText(this, string.Format(this.GetString(Resource.String.successfully_changed_dl_dir), data.Data), ToastLength.Long).Show();
                }));
            }
        }
        if (CHANGE_WRITE_EXTERNAL_LEGACY == requestCode)
        {
            if (resultCode == Result.Ok)
            {
                this.ContentResolver.TakePersistableUriPermission(data.Data, ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);
                SuccessfulWriteExternalLegacyCallback(data.Data);
            }
        }


        if (CHANGE_INCOMPLETE_EXTERNAL == requestCode)
        {
            if (resultCode == Result.Ok)
            {
                var x = data.Data;
                SeekerState.RootIncompleteDocumentFile = DocumentFile.FromTreeUri(this, data.Data);
                SeekerState.ManualIncompleteDataDirectoryUri = data.Data.ToString();
                SeekerState.ManualIncompleteDataDirectoryUriIsFromTree = true;
                this.ContentResolver.TakePersistableUriPermission(data.Data, ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);
                this.RunOnUiThread(new Action(() =>
                {
                    SeekerState.DirectoryUpdatedEvent?.Invoke(null, new EventArgs());
                    Toast.MakeText(this, string.Format(this.GetString(Resource.String.successfully_changed_incomplete_dir), data.Data), ToastLength.Long).Show();
                }));
            }
        }
        if (CHANGE_INCOMPLETE_EXTERNAL_LEGACY == requestCode)
        {
            if (resultCode == Result.Ok)
            {
                this.ContentResolver.TakePersistableUriPermission(data.Data, ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission);
                SuccessfulIncompleteExternalLegacyCallback(data.Data);
            }
        }


        if (UPLOAD_DIR_ADD_WRITE_EXTERNAL == requestCode ||
            UPLOAD_DIR_ADD_WRITE_EXTERNAL_LEGACY == requestCode ||
            UPLOAD_DIR_ADD_WRITE_EXTERNAL_LEGACY_RESELECT_CASE == requestCode ||
            UPLOAD_DIR_ADD_WRITE_EXTERNAL_RESELECT_CASE == requestCode)
        {
            if (resultCode != Result.Ok)
            {
                return;
            }

            bool reselectCase = false;
            if (UPLOAD_DIR_ADD_WRITE_EXTERNAL_RESELECT_CASE == requestCode || UPLOAD_DIR_ADD_WRITE_EXTERNAL_LEGACY_RESELECT_CASE == requestCode)
            {
                reselectCase = true;
            }
            //make sure you can parse the files before setting the directory..

            //this takes 5+ seconds in Debug mode (with 20-30 albums) which means that this MUST be done on a separate thread..
            Rescan(data.Data, requestCode, false, false, reselectCase);

        }

        if (SAVE_SEEKER_SETTINGS == requestCode)
        {
            if (resultCode == Result.Ok)
            {
                var seekerImportExportData = GetCurrentExportData();

                var stream = this.ContentResolver.OpenOutputStream(data.Data);
                var xmlWriterSettings = new XmlWriterSettings() { Indent = true };
                using (var writer = XmlWriter.Create(stream, xmlWriterSettings))
                {
                    new XmlSerializer(typeof(SeekerImportExportData)).Serialize(writer, seekerImportExportData);
                }

                Toast.MakeText(this, Resource.String.successfully_exported, ToastLength.Short).Show();
            }
        }

        if (FORCE_REQUEST_STORAGE_MANAGER == requestCode)
        {
            bool hasPermision = HasManageStoragePermission(this);
            if (hasPermision)
            {
                Toast.MakeText(this, Resource.String.permission_successfully_granted, ToastLength.Short).Show();
            }
            else
            {
                Toast.MakeText(this, Resource.String.permission_failed, ToastLength.Short).Show();
            }
        }
    }

    private SeekerImportExportData GetCurrentExportData()
    {
        var seekerImportExportData = new SeekerImportExportData();
        seekerImportExportData.Userlist = UserListManager.UserList.Select(uli => uli.Username).ToList();
        seekerImportExportData.BanIgnoreList = SeekerState.IgnoreUserList.Select(uli => uli.Username).ToList();
        seekerImportExportData.Wishlist = SearchTabHelper.SearchTabCollection.Where((pair1) => pair1.Value.SearchTarget == SearchTarget.Wishlist).Select((pair1) => pair1.Value.LastSearchTerm).ToList();
        List<KeyValueEl> userNotes = new List<KeyValueEl>();
        foreach (KeyValuePair<string, string> pair in SeekerState.UserNotes)
        {
            userNotes.Add(new KeyValueEl() { Key = pair.Key, Value = pair.Value });
        }
        seekerImportExportData.UserNotes = userNotes;
        return seekerImportExportData;
    }

    public static void RestoreAdditionalDirectorySettingsFromSharedPreferences()
    {
        lock (SeekerApplication.SharedPrefLock)
        {
            SeekerState.ManualIncompleteDataDirectoryUri = SeekerState.SharedPreferences.GetString(ResourceConstant.String.key_manual_incomplete_directory_uri, string.Empty);
        }
    }

    public static void SaveAdditionalDirectorySettingsToSharedPreferences()
    {
        lock (SeekerApplication.SharedPrefLock)
        {
            SeekerState.SharedPreferences.Edit()!
                .PutString(ResourceConstant.String.key_manual_incomplete_directory_uri, SeekerState.ManualIncompleteDataDirectoryUri)!
                .Commit();
        }
    }

    public static void SaveMaxConcurrentDownloadsSettings()
    {
        lock (SeekerApplication.SharedPrefLock)
        {
            var editor = SeekerState.SharedPreferences.Edit();
            editor.PutBoolean(KeyConsts.M_LimitSimultaneousDownloads, Soulseek.SimultaneousDownloadsGatekeeper.RestrictConcurrentUsers);
            editor.PutInt(KeyConsts.M_MaxSimultaneousLimit, Soulseek.SimultaneousDownloadsGatekeeper.MaxUsersConcurrent);
            bool success = editor.Commit();
        }
    }
}

public enum DirectoryType : ushort
{
    Download = 0,
    Upload = 1,
    Incomplete = 2
}
