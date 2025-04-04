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
using System.Threading;
using System.Threading.Tasks;
using _Microsoft.Android.Resource.Designer;
using Android;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Views;
using Android.Views.InputMethods;
using Android.Widget;
using AndroidX.Activity;
using AndroidX.AppCompat.View.Menu;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AndroidX.Core.View;
using AndroidX.DocumentFile.Provider;
using AndroidX.Lifecycle;
using AndroidX.ViewPager.Widget;
using Google.Android.Material.BottomNavigation;
using Google.Android.Material.Navigation;
using Google.Android.Material.Tabs;
using Java.IO;
using JetBrains.Annotations;
using Seeker.Exceptions;
using Seeker.Helpers;
using Seeker.Managers;
using Seeker.Models;
using Seeker.Search;
using Seeker.Transfers;
using Seeker.Utils;
using Soulseek;
using Toolbar = AndroidX.AppCompat.Widget.Toolbar;

namespace Seeker.Main;

[Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true, Exported = true)]
public class MainActivity : ThemeableActivity
{
    public const string logCatTag = "seeker";
    
    public const int DEFAULT_SEARCH_RESULTS = 250;
    private const int WRITE_EXTERNAL = 9999;
    private const int NEW_WRITE_EXTERNAL = 0x428;
    private const int MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL = 0x429;
    private const int NEW_WRITE_EXTERNAL_VIA_LEGACY = 0x42A;
    private const int MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY = 0x42B;
    private const int NEW_WRITE_EXTERNAL_VIA_LEGACY_SETTINGS_SCREEN = 0x42C;
    private const int MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY_SETTINGS_SCREEN = 0x42D;
    private const int POST_NOTIFICATION_PERMISSION = 0x42E;
    
    public const string UPLOADS_CHANNEL_ID = "upload channel ID";
    public const string UPLOADS_CHANNEL_NAME = "Upload Notifications";
    private const string UPLOADS_NOTIFICATION_EXTRA = "From Upload";
    
    public static int GoToSearchTab = int.MaxValue;
    
    private const string DEFAULT_MUSIC_URI = "content://com.android.externalstorage.documents/tree/primary%3AMusic";
    
    private ISharedPreferences sharedPreferences;
    [NotNull] private ViewPager pager;
    [NotNull] private BottomNavigationView navigation;
    [NotNull] private Toolbar toolbar;
    [NotNull] private TabLayout tabs;

    public static event EventHandler<DownloadAddedEventArgs> DownloadAddedUiNotify;

    public static void InvokeDownloadAddedUiNotify(DownloadAddedEventArgs e)
    {
        DownloadAddedUiNotify?.Invoke(null, e);
    }

    public static void ClearDownloadAddedEventsFromTarget(object target)
    {
        if (DownloadAddedUiNotify == null)
        {
            return;
        }

        foreach (Delegate d in DownloadAddedUiNotify.GetInvocationList())
        {
            if (d.Target == null) // i.e. static
            {
                continue;
            }

            if (d.Target.GetType() == target.GetType())
            {
                DownloadAddedUiNotify -= (EventHandler<DownloadAddedEventArgs>)d;
            }
        }
    }

    /// <summary>
    /// Handle the intent this activity is created with
    /// </summary>
    /// <param name="recreated">whether this activity was rebuilt, for example on configuration changes</param>
    private void HandleOnCreateIntent(bool recreated)
    {
        // this is a relatively safe way that prevents rotates from redoing the intent.
        var alreadyHandled = Intent?.GetBooleanExtra("ALREADY_HANDLED", false) ?? false;
        Intent = Intent?.PutExtra("ALREADY_HANDLED", true);

        if (Intent == null)
        {
            return;
        }
        
        if (Intent.GetIntExtra(DownloadForegroundService.FromTransferString, -1) == 2)
        {
            pager.SetCurrentItem(2, false);
            return;
        }
        
        if (Intent.GetIntExtra(SeekerApplication.FromFolderAlert, -1) == 2)
        {
            pager.SetCurrentItem(2, false);
            return;
        }
        
        if (Intent.GetIntExtra(UserListActivity.IntentUserGoToBrowse, -1) == 3)
        {
            pager.SetCurrentItem(3, false);
            return;
        }
        
        if (Intent.GetIntExtra(UserListActivity.IntentUserGoToSearch, -1) == 1)
        {
            pager.SetCurrentItem(1, false);
            return;
        }
        
        if (Intent.GetIntExtra(UserListActivity.IntentSearchRoom, -1) == 1)
        {
            pager.SetCurrentItem(1, false);
            return;
        }
        
        // if it's not reborn then the OnNewIntent will handle it...
        if (Intent.GetIntExtra(WishlistController.FromWishlistString, -1) == 1 && !recreated)
        {
            SeekerState.MainActivityRef = this; // set these early. they are needed
            SeekerState.ActiveActivityRef = this;

            Logger.FirebaseInfo("is resumed: " + (SearchFragment.Instance?.IsResumed ?? false));
            Logger.FirebaseInfo("from wishlist clicked");

            var currentPage = pager.CurrentItem;
            var tabId = Intent.GetIntExtra(WishlistController.FromWishlistStringID, int.MaxValue);

            // this is the case even if process previously got am state killed.
            if (currentPage == 1)
            {
                Logger.FirebaseInfo("from wishlist clicked - current page");
                if (tabId == int.MaxValue)
                {
                    Logger.FirebaseDebug("tabID == int.MaxValue");
                }
                else if (!SearchTabHelper.SearchTabCollection.ContainsKey(tabId))
                {
                    Logger.FirebaseDebug("doesnt contain key");

                    Toast.MakeText(this, GetString(ResourceConstant.String.wishlist_tab_error), ToastLength.Long)
                        ?.Show();
                }
                else
                {
                    if (SearchFragment.Instance?.IsResumed ?? false) // !??! this logic is backwards...
                    {
                        Logger.Debug("we are on the search page " +
                                     "but we need to wait for OnResume search frag");

                        GoToSearchTab = tabId; // we read this we resume
                    }
                    else
                    {
                        SearchFragment.Instance?.GoToTab(tabId, false, true);
                    }
                }
            }
            else
            {
                Logger.FirebaseInfo("from wishlist clicked - different page");

                // when we move to the page, lets move to our tab, if its not the current one..
                GoToSearchTab = tabId; // we read this when we move tab...
                pager.SetCurrentItem(1, false);
            }

            return;
        }

        var fromTransferUploadStringExtra =
            Intent.GetIntExtra(UploadForegroundService.FromTransferUploadString, -1);
        var uploadNotificationExtra = Intent.GetIntExtra(UPLOADS_NOTIFICATION_EXTRA, -1);
        
        // else every rotation will change Downloads to Uploads.
        if ((fromTransferUploadStringExtra == 2 ||  uploadNotificationExtra == 2) && !alreadyHandled)
        {
            HandleFromNotificationUploadIntent();
            return;
        }
        
        if (Intent.GetIntExtra(SettingsActivity.FromBrowseSelf, -1) == 3)
        {
            Logger.FirebaseInfo("from browse self");
            pager.SetCurrentItem(3, false);
            return;
        }
        
        // this will always create a new instance,
        // so if its reborn then it's an old intent that we already followed.
        if (!SearchSendIntentHelper.IsFromActionSend(Intent) || recreated)
        {
            return;
        }
        
        SeekerState.MainActivityRef = this;
        SeekerState.ActiveActivityRef = this;
        Logger.Debug("MainActivity action send intent");

        // give us a new fresh tab if the current one has a search in it...
        if (!string.IsNullOrEmpty(SearchTabHelper.LastSearchTerm))
        {
            Logger.Debug("lets go to a new fresh tab");
            var newTabToGoTo = SearchTabHelper.AddSearchTab();

            Logger.Debug("search fragment null? " + (SearchFragment.Instance == null));

            if (SearchFragment.Instance?.IsResumed ?? false)
            {
                // if resumed is true
                SearchFragment.Instance.GoToTab(newTabToGoTo, false, true);
            }
            else
            {
                Logger.Debug("we are on the search page but we need to wait " +
                             "for OnResume search frag");

                GoToSearchTab = newTabToGoTo; // we read this we resume
            }
        }

        // TODO: Do not use static properties to create a dialog.
        //       Instead, create it right here, add needed properties to the constructor 
        
        // go to search tab
        Logger.Debug("prev search term: " + SearchDialog.SearchTerm);
        SearchDialog.SearchTerm = Intent.GetStringExtra(Intent.ExtraText);
        SearchDialog.IsFollowingLink = false;
        pager.SetCurrentItem(1, false);

        if (SearchSendIntentHelper.TryParseIntent(Intent, out var searchTermFound))
        {
            // we are done parsing the intent
            SearchDialog.SearchTerm = searchTermFound;
        }
        else if (SearchSendIntentHelper.FollowLinkTaskIfApplicable(Intent))
        {
            SearchDialog.IsFollowingLink = true;
        }

        // close previous instance
        if (SearchDialog.Instance != null)
        {
            Logger.Debug("previous instance exists");
        }

        var searchDialog = new SearchDialog(SearchDialog.SearchTerm, SearchDialog.IsFollowingLink);
        searchDialog.Show(SupportFragmentManager, "Search Dialog");
    }
    
    protected override void OnCreate(Bundle savedInstanceState)
    {
        // basically if the Intent created the MainActivity, then we want to handle it (i.e. if from "Search Here")
        // however, if we say rotate the device or leave
        // and come back to it (and the activity got destroyed in the meantime) then
        // it will re-handle the activity each time.
        // We can check if it is truly "new" by looking at the savedInstanceState.
        var recreated = false;

        if (savedInstanceState == null)
        {
            Logger.Debug("Main Activity On Create NEW");
        }
        else
        {
            recreated = true;
            Logger.Debug("Main Activity On Create REBORN");
        }

        KeepAlive.Initialize(this);

        base.OnCreate(savedInstanceState);

        Xamarin.Essentials.Platform.Init(this, savedInstanceState); // this is what you are supposed to do.
        SetContentView(ResourceConstant.Layout.activity_main);

        navigation = FindViewById<BottomNavigationView>(ResourceConstant.Id.navigation)!;
        navigation.SetOnItemSelectedListener(new NavigationBarItemSelectedListener(pager));


        toolbar = FindViewById<Toolbar>(ResourceConstant.Id.toolbar)!;
        toolbar.Title = GetString(ResourceConstant.String.home_tab);
        // TODO: Is it possible to inflate menus through XML?
        toolbar.InflateMenu(ResourceConstant.Menu.account_menu);
        SetSupportActionBar(toolbar);
        toolbar.InflateMenu(ResourceConstant.Menu.account_menu); // twice??

        var backPressedCallback = new GenericOnBackPressedCallback(true, OnBackPressedAction);
        OnBackPressedDispatcher.AddCallback(backPressedCallback);
        
        sharedPreferences = GetSharedPreferences("SoulSeekPrefs", FileCreationMode.Private);


        pager = FindViewById<ViewPager>(ResourceConstant.Id.pager)!;
        pager.PageSelected += Pager_PageSelected;
        pager.AddOnPageChangeListener(new MenuChangerOnPageSwitch(navigation));
        pager.Adapter = new TabsPagerAdapter(SupportFragmentManager);

        tabs = FindViewById<TabLayout>(ResourceConstant.Id.tabs)!;
        tabs.TabSelected += Tabs_TabSelected;
        
        HandleOnCreateIntent(recreated);

        // TODO: Do not use static references for Android Context entities
        SeekerState.MainActivityRef = this;
        SeekerState.ActiveActivityRef = this;

        // if we have all the conditions to share, then set sharing up.
        if (SharingManager.MeetsSharingConditions() && !SeekerState.IsParsing && !SharingManager.IsSharingSetUpSuccessfully())
        {
            SharingManager.SetUpSharing();
        }
        else if (SeekerState.NumberOfSharedDirectoriesIsStale)
        {
            SharingManager.InformServerOfSharedFiles();
            SeekerState.AttemptedToSetUpSharing = true;
        }

        SeekerState.SharedPreferences = sharedPreferences;

        UpdateForScreenSize();
        SetupStorage();
    }

    private void SetupStorage()
    {
        if (SeekerState.UseLegacyStorage())
        {
            const string permissionName = Manifest.Permission.WriteExternalStorage;
            if (ContextCompat.CheckSelfPermission(this, permissionName) == Permission.Denied)
            {
                ActivityCompat
                    .RequestPermissions(this, [permissionName], WRITE_EXTERNAL);
            }

            // file picker with legacy case
            if (!string.IsNullOrEmpty(SeekerState.SaveDataDirectoryUri))
            {
                // an example of a random bad url that passes parsing but fails FromTreeUri:
                // "file:/media/storage/sdcard1/data/example.externalstorage/files/"
                var chosenUri = Android.Net.Uri.Parse(SeekerState.SaveDataDirectoryUri);
                var canWrite = false;

                try
                {
                    // a phone failed 4 times with //POCO X3 Pro
                    // Android 11(SDK 30)
                    // Caused by: java.lang.IllegalArgumentException: 
                    // at android.provider.DocumentsContract.getTreeDocumentId(DocumentsContract.java:1278)
                    // at androidx.documentfile.provider.DocumentFile.fromTreeUri(DocumentFile.java:136)
                    if (SeekerState.PreOpenDocumentTree() || !SeekerState.SaveDataDirectoryUriIsFromTree)
                    {
                        canWrite = DocumentFile.FromFile(new Java.IO.File(chosenUri!.Path!)).CanWrite();
                    }
                    else
                    {
                        // on changing the code and restarting for api 22 
                        // persistenduripermissions is empty
                        // and exists is false, cannot list files
                        canWrite = DocumentFile.FromTreeUri(this, chosenUri!)!.CanWrite();
                    }
                }
                catch (Exception e)
                {
                    if (chosenUri != null)
                    {
                        //legacy DocumentFile.FromTreeUri failed with URI: /tree/2A6B-256B:Seeker/Soulseek Complete Invalid URI: /tree/2A6B-256B:Seeker/Soulseek Complete
                        //legacy DocumentFile.FromTreeUri failed with URI: /tree/raw:/storage/emulated/0/Download/Soulseek Downloads Invalid URI: /tree/raw:/storage/emulated/0/Download/Soulseek Downloads
                        Logger.FirebaseDebug("legacy DocumentFile.FromTreeUri failed with URI: " + chosenUri.ToString() +
                                    " " + e.Message + " scheme " + chosenUri.Scheme);
                    }
                    else
                    {
                        Logger.FirebaseDebug("legacy DocumentFile.FromTreeUri failed with null URI");
                    }
                }

                if (canWrite)
                {
                    SeekerState.RootDocumentFile = SeekerState.PreOpenDocumentTree() 
                        ? DocumentFile.FromFile(new Java.IO.File(chosenUri.Path!)) 
                        : DocumentFile.FromTreeUri(this, chosenUri);
                }
                else
                {
                    Logger.FirebaseDebug("cannot write" + chosenUri?.ToString());
                }
            }

            // TODO: This is practically duplicate code from what is above
            // now for incomplete
            if (!string.IsNullOrEmpty(SeekerState.ManualIncompleteDataDirectoryUri))
            {
                // an example of a random bad url that passes parsing but fails FromTreeUri:
                // "file:/media/storage/sdcard1/data/example.externalstorage/files/"
                var chosenIncompleteUri = Android.Net.Uri.Parse(SeekerState.ManualIncompleteDataDirectoryUri);
                var canWrite = false;
                try
                {
                    //a phone failed 4 times with //POCO X3 Pro
                    //Android 11(SDK 30)
                    //Caused by: java.lang.IllegalArgumentException: 
                    //at android.provider.DocumentsContract.getTreeDocumentId(DocumentsContract.java:1278)
                    //at androidx.documentfile.provider.DocumentFile.fromTreeUri(DocumentFile.java:136)
                    if (SeekerState.PreOpenDocumentTree()
                        || !SeekerState.ManualIncompleteDataDirectoryUriIsFromTree)
                    {
                        canWrite = DocumentFile.FromFile(new Java.IO.File(chosenIncompleteUri!.Path!)).CanWrite();
                    }
                    else
                    {
                        // on changing the code and restarting for api 22 
                        // persistenduripermissions is empty
                        // and exists is false, cannot list file
                        canWrite = DocumentFile.FromTreeUri(this, chosenIncompleteUri)!.CanWrite();
                    }
                }
                catch (Exception exception)
                {
                    if (chosenIncompleteUri != null)
                    {
                        Logger.FirebaseDebug("legacy Incomplete DocumentFile.FromTreeUri failed with URI: "
                                    + chosenIncompleteUri.ToString() + " " + exception.Message);
                    }
                    else
                    {
                        Logger.FirebaseDebug("legacy Incomplete DocumentFile.FromTreeUri failed with null URI");
                    }
                }

                if (canWrite)
                {
                    if (SeekerState.PreOpenDocumentTree())
                    {
                        SeekerState.RootIncompleteDocumentFile =
                            DocumentFile.FromFile(new Java.IO.File(chosenIncompleteUri.Path!));
                    }
                    else
                    {
                        SeekerState.RootIncompleteDocumentFile =
                            DocumentFile.FromTreeUri(this, chosenIncompleteUri);
                    }
                }
                else
                {
                    Logger.FirebaseDebug("cannot write incomplete" + chosenIncompleteUri?.ToString());
                }
            }


        }
        else
        {
            // an example of a random bad url that passes parsing but fails FromTreeUri:
            // "file:/media/storage/sdcard1/data/example.externalstorage/files/"
            var res = Android.Net.Uri.Parse(string.IsNullOrEmpty(SeekerState.SaveDataDirectoryUri)
                ? DEFAULT_MUSIC_URI 
                : SeekerState.SaveDataDirectoryUri);

            // TODO: Below code seems to be duplicate from what is above, as even the comment is the same
            var canWrite = false;
            try
            {
                // a phone failed 4 times with //POCO X3 Pro
                // Android 11(SDK 30)
                // Caused by: java.lang.IllegalArgumentException: 
                // at android.provider.DocumentsContract.getTreeDocumentId(DocumentsContract.java:1278)
                // at androidx.documentfile.provider.DocumentFile.fromTreeUri(DocumentFile.java:136)

                // this will never get hit..
                // TODO: If this is never hit, why check for it?
                if (SeekerState.PreOpenDocumentTree() || !SeekerState.SaveDataDirectoryUriIsFromTree)
                {
                    canWrite = DocumentFile.FromFile(new Java.IO.File(res.Path)).CanWrite();
                }
                else
                {
                    canWrite = DocumentFile.FromTreeUri(this, res)!.CanWrite();
                }
            }
            catch (Exception e)
            {
                if (res != null)
                {
                    Logger.FirebaseDebug("DocumentFile.FromTreeUri failed with URI: " + res.ToString() + " " + e.Message);
                }
                else
                {
                    Logger.FirebaseDebug("DocumentFile.FromTreeUri failed with null URI");
                }
            }

            if (!canWrite)
            {

                var dialogBuilder = new AndroidX.AppCompat.App.AlertDialog
                        .Builder(this, ResourceConstant.Style.MyAlertDialogTheme)!
                    .SetTitle(GetString(ResourceConstant.String.seeker_needs_dl_dir))!
                    .SetMessage(GetString(ResourceConstant.String.seeker_needs_dl_dir_content))!;

                EventHandler<DialogClickEventArgs> eventHandler = (_, _) =>
                {
                    var storageManager = Android.OS.Storage.StorageManager.FromContext(this);
                    var intent = storageManager.PrimaryStorageVolume.CreateOpenDocumentTreeIntent();
                    intent.PutExtra(DocumentsContract.ExtraInitialUri, res);

                    intent.AddFlags(ActivityFlags.GrantPersistableUriPermission
                                    | ActivityFlags.GrantReadUriPermission
                                    | ActivityFlags.GrantWriteUriPermission
                                    | ActivityFlags.GrantPrefixUriPermission);

                    try
                    {
                        StartActivityForResult(intent, NEW_WRITE_EXTERNAL);
                    }
                    catch (Exception exception)
                    {
                        if (exception.Message.Contains(CommonHelpers.NoDocumentOpenTreeToHandle))
                        {
                            FallbackFileSelectionEntry(false);
                        }
                        else
                        {
                            throw;
                        }
                    }
                };

                dialogBuilder.SetPositiveButton(ResourceConstant.String.okay, eventHandler)!
                    .SetCancelable(false)!
                    .Show();
            }
            else
            {
                if (SeekerState.SaveDataDirectoryUriIsFromTree)
                {
                    SeekerState.RootDocumentFile = DocumentFile.FromTreeUri(this, res);

                }
                else
                {
                    SeekerState.RootDocumentFile = DocumentFile.FromFile(new Java.IO.File(res.Path));
                }
            }

            bool manualSet;

            // TODO: Some duplcate code below again, consider revisiting

            // for incomplete case
            Android.Net.Uri incompleteRes = null;

            if (!string.IsNullOrEmpty(SeekerState.ManualIncompleteDataDirectoryUri))
            {
                manualSet = true;
                // an example of a random bad url that passes parsing but fails FromTreeUri:
                // "file:/media/storage/sdcard1/data/example.externalstorage/files/"
                incompleteRes = Android.Net.Uri.Parse(SeekerState.ManualIncompleteDataDirectoryUri);
            }
            else
            {
                manualSet = false;
            }

            if (manualSet)
            {
                bool canWriteIncomplete = false;
                try
                {
                    // a phone failed 4 times with //POCO X3 Pro
                    // Android 11(SDK 30)
                    // Caused by: java.lang.IllegalArgumentException: 
                    // at android.provider.DocumentsContract.getTreeDocumentId(DocumentsContract.java:1278)
                    // at androidx.documentfile.provider.DocumentFile.fromTreeUri(DocumentFile.java:136)
                    if (SeekerState.PreOpenDocumentTree()
                        || !SeekerState.ManualIncompleteDataDirectoryUriIsFromTree)
                    {
                        canWriteIncomplete = DocumentFile.FromFile(new Java.IO.File(incompleteRes.Path)).CanWrite();
                    }
                    else
                    {
                        canWriteIncomplete = DocumentFile.FromTreeUri(this, incompleteRes)!.CanWrite();
                    }
                }
                catch (Exception e)
                {
                    if (incompleteRes != null)
                    {
                        Logger.FirebaseDebug("DocumentFile.FromTreeUri failed with incomplete URI: "
                                    + incompleteRes.ToString() + " " + e.Message);
                    }
                    else
                    {
                        Logger.FirebaseDebug("DocumentFile.FromTreeUri failed with incomplete null URI");
                    }
                }

                if (!canWriteIncomplete)
                {
                    return;
                }
                
                if (SeekerState.PreOpenDocumentTree()
                    || !SeekerState.ManualIncompleteDataDirectoryUriIsFromTree)
                {
                    SeekerState.RootIncompleteDocumentFile =
                        DocumentFile.FromFile(new Java.IO.File(incompleteRes.Path!));
                }
                else
                {
                    SeekerState.RootIncompleteDocumentFile = DocumentFile.FromTreeUri(this, incompleteRes);
                }
            }
        }
    }

    private void FallbackFileSelection(int requestCode)
    {
        // Create FolderOpenDialog
        SimpleFileDialog fileDialog =
            new(SeekerState.ActiveActivityRef, SimpleFileDialog.FileSelectionMode.FolderChoose);

        fileDialog.GetFileOrDirectoryAsync(Android.OS.Environment.ExternalStorageDirectory?.AbsolutePath)
            .ContinueWith(t =>
            {
                if (string.IsNullOrEmpty(t.Result))
                {
                    OnActivityResult(requestCode, Result.Canceled, new Intent());
                    return;
                }

                var intent = new Intent();
                var documentFile = DocumentFile.FromFile(new Java.IO.File(t.Result));
                intent.SetData(documentFile.Uri);
                OnActivityResult(requestCode, Result.Ok, intent);
            });
    }

    public static Action<Task> GetPostNotificationsPermissionTask()
    {
        return task =>
        {
            if (task.IsCompletedSuccessfully)
            {
                RequestPostNotificationPermissionsIfApplicable();
            }
        };

    }

    private static bool _postNotificationsAlreadyRequestedInSession;

    /// <summary>
    /// As far as where to place this, doing it on launch is no good (as they will already
    ///   see yet another though more important permission in the background behind them).
    /// Doing this on login (i.e. first session login) seems decent.
    /// </summary>
    private static void RequestPostNotificationPermissionsIfApplicable()
    {
        if (_postNotificationsAlreadyRequestedInSession)
        {
            return;
        }

        _postNotificationsAlreadyRequestedInSession = true;

        if (Build.VERSION.SdkInt < BuildVersionCodes.Tiramisu)
        {
            return;
        }

        try
        {
            var permissionState = ContextCompat
                .CheckSelfPermission(SeekerState.ActiveActivityRef, Manifest.Permission.PostNotifications);

            if (permissionState == Permission.Denied)
            {
                bool alreadyShown = SeekerState.SharedPreferences
                    .GetBoolean(KeyConsts.M_PostNotificationRequestAlreadyShown, false);

                if (alreadyShown)
                {
                    return;
                }

                if (ThreadingUtils.OnUiThread())
                {
                    RequestNotifPermissionsLogic();
                }
                else
                {
                    SeekerState.ActiveActivityRef.RunOnUiThread(RequestNotifPermissionsLogic);
                }
            }
        }
        catch (Exception e)
        {
            Logger.FirebaseDebug("RequestPostNotificationPermissionsIfApplicable error: "
                                     + e.Message + e.StackTrace);
        }
    }

    // recommended way, if user only denies once then next time
    // (ShouldShowRequestPermissionRationale lets us know this)
    //   then show a blurb on what the permissions are used for and ask a second (and last) time.
    private static void RequestNotifPermissionsLogic()
    {
        try
        {
            void setAlreadyShown()
            {
                lock (SeekerApplication.SHARED_PREF_LOCK)
                {
                    var editor = SeekerState.SharedPreferences.Edit();
                    editor.PutBoolean(KeyConsts.M_PostNotificationRequestAlreadyShown, true);
                    editor.Commit();
                }
            }

            ActivityCompat.RequestPermissions(
                SeekerState.ActiveActivityRef,
                [Manifest.Permission.PostNotifications],
                POST_NOTIFICATION_PERMISSION
            );

            setAlreadyShown();
        }
        catch (Exception e)
        {
            Logger.FirebaseDebug("RequestPostNotificationPermissionsIfApplicable error: "
                                     + e.Message + e.StackTrace);
        }
    }

    protected override void OnStart()
    {
        // TODO: onStart is called before onCreate, yet we set main activity ref in onCreate again
        
        // this fixes a bug as follows:
        // previously we only set MainActivityRef on Create.
        // therefore if one launches MainActivity via a new intent (i.e. go to user list, then search users files)
        // it will be set with the new search user activity.
        // then if you press back twice you will see the original activity but the MainActivityRef will still be set
        // to the now destroyed activity since it was last to call onCreate.
        // so then the FragmentManager will be null among other things...
        // TODO: Do not use static references for Android Context entities
        SeekerState.MainActivityRef = this;

        base.OnStart();
    }

    public static bool fromNotificationMoveToUploads;

    protected override void OnNewIntent(Intent intent)
    {
        Logger.Debug("OnNewIntent");
        base.OnNewIntent(intent);
        Intent = intent.PutExtra("ALREADY_HANDLED", true);
        if (Intent.GetIntExtra(WishlistController.FromWishlistString, -1) == 1)
        {
            Logger.FirebaseInfo("is null: "
                                         + (SearchFragment.Instance?.Activity == null
                                            || (SearchFragment.Instance?.IsResumed ?? false)).ToString());

            Logger.FirebaseInfo("from wishlist clicked");

            int currentPage = pager.CurrentItem;
            int tabID = Intent.GetIntExtra(WishlistController.FromWishlistStringID, int.MaxValue);

            if (currentPage == 1)
            {
                if (tabID == int.MaxValue)
                {
                    Logger.FirebaseDebug("tabID == int.MaxValue");
                }
                else if (!SearchTabHelper.SearchTabCollection.ContainsKey(tabID))
                {
                    Toast.MakeText(this, this.GetString(Resource.String.wishlist_tab_error), ToastLength.Long)
                        ?.Show();
                }
                else
                {
                    if (SearchFragment.Instance?.Activity == null || (SearchFragment.Instance?.IsResumed ?? false))
                    {
                        Logger.Debug("we are on the search page but we need to wait " + 
                                     "for OnResume search frag");

                        GoToSearchTab = tabID; // we read this we resume
                    }
                    else
                    {
                        SearchFragment.Instance?.GoToTab(tabID, false, true);
                    }
                }
            }
            else
            {
                // when we move to the page, lets move to our tab, if its not the current one..
                GoToSearchTab = tabID; // we read this when we move tab...
                pager.SetCurrentItem(1, false);
            }
        }
        // else every rotation will change Downloads to Uploads.
        else if (((Intent.GetIntExtra(UploadForegroundService.FromTransferUploadString, -1) == 2)
                  || (Intent.GetIntExtra(UPLOADS_NOTIFICATION_EXTRA, -1) == 2)))
        {
            HandleFromNotificationUploadIntent();
        }
        else if (Intent.GetIntExtra(SettingsActivity.FromBrowseSelf, -1) == 3)
        {
            Logger.FirebaseInfo("from browse self");
            pager.SetCurrentItem(3, false);
        }
        else if (Intent.GetIntExtra(UserListActivity.IntentUserGoToBrowse, -1) == 3)
        {
            pager.SetCurrentItem(3, false);
        }
        else if (Intent.GetIntExtra(UserListActivity.IntentUserGoToSearch, -1) == 1)
        {
            pager.SetCurrentItem(1, false);
        }
        else if (Intent.GetIntExtra(DownloadForegroundService.FromTransferString, -1) == 2)
        {
            pager.SetCurrentItem(2, false);
        }
        else if (Intent.GetIntExtra(SeekerApplication.FromFolderAlert, -1) == 2)
        {
            pager.SetCurrentItem(2, false);
        }
    }

    private void HandleFromNotificationUploadIntent()
    {
        // either we change to uploads mode now (if resumed), or we wait for on resume to do it.
        Logger.FirebaseInfo("from uploads clicked");
        var currentPage = pager.CurrentItem;

        if (currentPage == 2)
        {
            if (StaticHacks.TransfersFrag?.Activity == null || (StaticHacks.TransfersFrag?.IsResumed ?? false))
            {
                Logger.FirebaseInfo("we need to wait for on resume");
                fromNotificationMoveToUploads = true; // we read this in onresume
            }
            else
            {
                // we can change to uploads mode now
                Logger.Debug("go to upload now");
                StaticHacks.TransfersFrag?.MoveToUploadForNotif();
            }
        }
        else
        {
            fromNotificationMoveToUploads = true; // we read this in onresume
            pager.SetCurrentItem(2, false);
        }
    }

    public static void GetDownloadPlaceInQueueBatch(List<TransferItem> transferItems, bool addIfNotAdded)
    {
        if (CurrentlyLoggedInButDisconnectedState())
        {
            if (!ShowMessageAndCreateReconnectTask(SeekerState.ActiveActivityRef, false, out var reconnectTask))
            {
                reconnectTask.ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        return;
                    }

                    SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                    {
                        GetDownloadPlaceInQueueBatchLogic(transferItems, addIfNotAdded);
                    });
                });
            }
        }
        else
        {
            GetDownloadPlaceInQueueBatchLogic(transferItems, addIfNotAdded);
        }
    }


    private static void GetDownloadPlaceInQueueBatchLogic(List<TransferItem> transferItems, bool addIfNotAdded)
    {
        foreach (var transferItem in transferItems)
        {
            GetDownloadPlaceInQueueLogic(
                transferItem.Username,
                transferItem.FullFilename,
                addIfNotAdded,
                true,
                transferItem
            );
        }
    }

    public static void GetDownloadPlaceInQueue(
        string username,
        string fullFileName,
        bool addIfNotAdded,
        bool silent,
        TransferItem transferItemInQuestion = null,
        Func<TransferItem, object> actionOnComplete = null)
    {

        if (CurrentlyLoggedInButDisconnectedState())
        {
            if (!ShowMessageAndCreateReconnectTask(SeekerState.ActiveActivityRef, false, out var reconnectTask))
            {
                reconnectTask.ContinueWith(task =>
                {
                    if (task.IsFaulted)
                    {
                        SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                        {
                            if (SeekerState.ActiveActivityRef != null)
                            {
                                Toast.MakeText(
                                    SeekerState.ActiveActivityRef,
                                    SeekerState.ActiveActivityRef.GetString(ResourceConstant.String.failed_to_connect),
                                    ToastLength.Short
                                )?.Show();
                            }
                        });

                        return;
                    }

                    SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                    {
                        GetDownloadPlaceInQueueLogic(
                            username,
                            fullFileName,
                            addIfNotAdded,
                            silent,
                            transferItemInQuestion,
                            actionOnComplete
                        );
                    });
                });
            }
        }
        else
        {
            GetDownloadPlaceInQueueLogic(
                username,
                fullFileName,
                addIfNotAdded,
                silent,
                transferItemInQuestion,
                actionOnComplete
            );
        }
    }

    private static void GetDownloadPlaceInQueueLogic(
        string username,
        string fullFileName,
        bool addIfNotAdded,
        bool silent,
        TransferItem transferItemInQuestion = null,
        Func<TransferItem, object> actionOnComplete = null)
    {

        var updateTask = new Action<Task<int>>(
            taask =>
            {
                if (taask.IsFaulted)
                {
                    bool transitionToNextState = false;
                    TransferStates state = TransferStates.Errored;
                    if (taask.Exception?.InnerException is UserOfflineException)
                    {
                        // Nicotine always immediately transitions from queued to user offline
                        // the second the user goes offline. We dont do it immediately but on next check.
                        // for QT you always are in "Queued" no matter what.
                        transitionToNextState = true;

                        state = TransferStates.Errored
                                | TransferStates.UserOffline
                                | TransferStates.FallenFromQueue;

                        if (!silent)
                        {
                            var userIsOfflineString = SeekerApplication.GetString(Resource.String.UserXIsOffline);
                            var formattedString = string.Format(userIsOfflineString, username);
                            ToastUiWithDebouncer(formattedString, "_6_", username);
                        }
                    }
                    else if (taask.Exception?.InnerException?.Message != null
                             && taask.Exception.InnerException.Message.Contains(
                                 SoulseekClient.FailedToEstablishDirectOrIndirectStringLower,
                                 StringComparison.CurrentCultureIgnoreCase))
                    {
                        // Nicotine transitions from Queued to Cannot Connect
                        // IF you pause and resume. Otherwise you stay in Queued.
                        // Here if someone explicitly retries (i.e. silent = false) then we will transition states.
                        // otherwise, its okay, lets just stay in Queued.
                        // for QT you always are in "Queued" no matter what.

                        transitionToNextState = !silent;

                        state = TransferStates.Errored
                                | TransferStates.CannotConnect
                                | TransferStates.FallenFromQueue;

                        if (!silent)
                        {
                            var cannotConnectString =
                                SeekerApplication.GetString(Resource.String.CannotConnectUserX);

                            ToastUiWithDebouncer(string.Format(cannotConnectString, username), "_7_", username);
                        }
                    }
                    else if (taask.Exception?.InnerException?.Message != null &&
                             taask.Exception.InnerException is System.TimeoutException)
                    {
                        // they may just not be sending queue position messages.
                        // that is okay, we can still connect to them just fine for download time.
                        if (!silent)
                        {
                            var messageString = SeekerApplication.GetString(ResourceConstant.String.TimeoutQueueUserX);
                            ToastUiWithDebouncer(string.Format(messageString, username), "_8_", username, 6);
                        }
                    }
                    else if (taask.Exception?.InnerException?.Message != null
                             && taask.Exception.InnerException.Message.Contains("underlying Tcp connection is closed"))
                    {
                        // can be server connection (get user endpoint) or peer connection.
                        if (!silent)
                        {
                            var formattedString =
                                $"Failed to get queue position for {username}: Connection was unexpectedly closed.";
                            ToastUiWithDebouncer(formattedString, "_9_", username, 6);
                        }
                    }
                    else
                    {
                        if (!silent)
                        {
                            ToastUiWithDebouncer($"Error getting queue position from {username}", "_9_", username);
                        }

                        Logger.FirebaseDebug("GetDownloadPlaceInQueue" + taask.Exception);
                    }

                    if (!transitionToNextState)
                    {
                        return;
                    }
                    
                    // update the transferItem array
                    transferItemInQuestion ??= TransfersFragment.TransferItemManagerDL
                        .GetTransferItemWithIndexFromAll(fullFileName, username, out _);

                    if (transferItemInQuestion == null)
                    {
                        return;
                    }

                    try
                    {
                        transferItemInQuestion.CancellationTokenSource.Cancel();
                    }
                    catch (Exception err)
                    {
                        Logger.FirebaseDebug("cancellation token src issue: " + err.Message);
                    }

                    transferItemInQuestion.State = state;
                }
                else
                {
                    bool queuePositionChanged;

                    // update the transferItem array
                    transferItemInQuestion ??= TransfersFragment.TransferItemManagerDL
                        .GetTransferItemWithIndexFromAll(fullFileName, username, out _);

                    if (transferItemInQuestion == null)
                    {
                        return;
                    }

                    queuePositionChanged = transferItemInQuestion.QueueLength != taask.Result;
                    transferItemInQuestion.QueueLength = taask.Result >= 0 ? taask.Result : int.MaxValue;

                    Logger.Debug(queuePositionChanged
                        ? $"Queue Position of {fullFileName} has changed to {taask.Result}"
                        : $"Queue Position of {fullFileName} is still {taask.Result}");

                    if (actionOnComplete != null)
                    {
                        SeekerState.ActiveActivityRef?.RunOnUiThread(() =>
                        {
                            actionOnComplete(transferItemInQuestion);
                        });
                    }
                    else
                    {
                        if (queuePositionChanged)
                        {
                            // if the transfer item fragment is bound then we update it..
                            TransferItemQueueUpdated?.Invoke(null, transferItemInQuestion);
                        }
                    }

                }
            }
        );

        Task<int> getDownloadPlace;
        try
        {
            getDownloadPlace = SeekerState.SoulseekClient.GetDownloadPlaceInQueueAsync(
                username,
                fullFileName,
                null,
                transferItemInQuestion!.ShouldEncodeFileLatin1(),
                transferItemInQuestion.ShouldEncodeFolderLatin1()
            );
        }
        catch (TransferNotFoundException)
        {
            if (addIfNotAdded)
            {
                // it is not downloading... therefore retry the download...
                transferItemInQuestion ??= TransfersFragment.TransferItemManagerDL
                    .GetTransferItemWithIndexFromAll(fullFileName, username, out _);

                var cancellationTokenSource = new CancellationTokenSource();
                try
                {
                    transferItemInQuestion.QueueLength = int.MaxValue;

                    // else when you go to cancel you are cancelling an already cancelled useless token!!
                    TransfersFragment
                        .SetupCancellationToken(transferItemInQuestion, cancellationTokenSource, out _);

                    var task = TransfersUtil.DownloadFileAsync(
                        transferItemInQuestion.Username,
                        transferItemInQuestion.FullFilename,
                        transferItemInQuestion.GetSizeForDL(),
                        cancellationTokenSource,
                        out _,
                        isFileDecodedLegacy: transferItemInQuestion.ShouldEncodeFileLatin1(),
                        isFolderDecodedLegacy: transferItemInQuestion.ShouldEncodeFolderLatin1()
                    );

                    task.ContinueWith(DownloadContinuationActionUi(
                        new DownloadAddedEventArgs(
                            new DownloadInfo(
                                transferItemInQuestion.Username,
                                transferItemInQuestion.FullFilename,
                                transferItemInQuestion.Size, task,
                                cancellationTokenSource,
                                transferItemInQuestion.QueueLength,
                                0,
                                transferItemInQuestion.GetDirectoryLevel()
                            ) { TransferItemReference = transferItemInQuestion }
                        )
                    ), cancellationTokenSource.Token);
                }
                catch (DuplicateTransferException)
                {
                    // happens due to button mashing...
                    return;
                }
                catch (Exception error)
                {
                    var a = () =>
                    {
                        // TODO: Logging errors through toasts isn't a good practice
                        Toast.MakeText(
                            SeekerState.ActiveActivityRef,
                            SeekerState.ActiveActivityRef.GetString(Resource.String.error_) + error.Message,
                            ToastLength.Long
                        );
                    };

                    if (error.Message == null || !error.Message.ToString().Contains("must be connected and logged"))
                    {
                        Logger.FirebaseDebug(error.Message + " OnContextItemSelected");
                    }

                    if (!silent)
                    {
                        SeekerState.ActiveActivityRef.RunOnUiThread(a);
                    }

                    return; // otherwise null ref with task!
                }

                //TODO: THIS OCCURS TO SOON, ITS NOT gaurentted for the transfer to be in downloads yet...
                try
                {
                    getDownloadPlace = SeekerState.SoulseekClient.GetDownloadPlaceInQueueAsync(
                        username,
                        fullFileName,
                        null,
                        transferItemInQuestion.ShouldEncodeFileLatin1(),
                        transferItemInQuestion.ShouldEncodeFolderLatin1()
                    );

                    getDownloadPlace.ContinueWith(updateTask);
                }
                catch (Exception e)
                {
                    Logger.FirebaseDebug("you likely called getdownloadplaceinqueueasync too soon..." + e.Message);
                }

                return;
            }

            Logger.Debug("Transfer Item we are trying to get queue position " +
                         "of is not currently being downloaded.");
            return;
        }
        catch (Exception)
        {
            return;
        }

        getDownloadPlace.ContinueWith(updateTask);
    }

    // for transferItemPage to update its recyclerView
    public static EventHandler<TransferItem> TransferItemQueueUpdated;

    private void OnCloseClick(object sender, DialogClickEventArgs e)
    {
        (sender as AndroidX.AppCompat.App.AlertDialog)?.Dismiss();
    }

    public override bool OnOptionsItemSelected(IMenuItem item)
    {
        switch (item.ItemId)
        {
            case ResourceConstant.Id.user_list_action:
                var intent = new Intent(SeekerState.MainActivityRef, typeof(UserListActivity));
                SeekerState.MainActivityRef.StartActivityForResult(intent, 141);
                return true;
            case ResourceConstant.Id.messages_action:
                var intentMessages = new Intent(SeekerState.MainActivityRef, typeof(MessagesActivity));
                SeekerState.MainActivityRef.StartActivityForResult(intentMessages, 142);
                return true;
            case ResourceConstant.Id.chatroom_action:
                var intentChatroom = new Intent(SeekerState.MainActivityRef, typeof(ChatroomActivity));
                SeekerState.MainActivityRef.StartActivityForResult(intentChatroom, 143);
                return true;
            case ResourceConstant.Id.settings_action:
                var intent2 = new Intent(SeekerState.MainActivityRef, typeof(SettingsActivity));
                SeekerState.MainActivityRef.StartActivityForResult(intent2, 140);
                return true;
            case ResourceConstant.Id.shutdown_action:
                var intent3 = new Intent(this, typeof(CloseActivity));
                // Clear all activities and start new task
                // ClearTask - causes any existing task that would be associated with the activity 
                //  to be cleared before the activity is started. can only be used in conjunction with NewTask.
                //  basically it clears all activities in the current task.
                intent3.SetFlags(ActivityFlags.ClearTask | ActivityFlags.NewTask);
                StartActivity(intent3);

                if ((int)Build.VERSION.SdkInt < 21)
                {
                    FinishAffinity();
                }
                else
                {
                    FinishAndRemoveTask();
                }

                return true;
            case ResourceConstant.Id.about_action:
                var builder =
                    new AndroidX.AppCompat.App.AlertDialog.Builder(this, ResourceConstant.Style.MyAlertDialogTheme);

                var diag = builder.SetMessage(ResourceConstant.String.about_body)
                    ?.SetPositiveButton(ResourceConstant.String.close, OnCloseClick).Create();
                diag?.Show();

                // this is a literal CDATA string.
                var origString = string.Format(
                    SeekerState.ActiveActivityRef.GetString(ResourceConstant.String.about_body),
                    SeekerApplication.GetVersionString()
                );

                // this can be slow so do NOT do it in loops...
                ((TextView)diag?.FindViewById(Android.Resource.Id.Message))!.TextFormatted = 
                    (int)Build.VERSION.SdkInt >= 24 
                        ? Android.Text.Html.FromHtml(origString, Android.Text.FromHtmlOptions.ModeLegacy) 
                        : Android.Text.Html.FromHtml(origString);

                ((TextView)diag?.FindViewById(Android.Resource.Id.Message))!.MovementMethod =
                    Android.Text.Method.LinkMovementMethod.Instance;

                return true;
        }

        return base.OnOptionsItemSelected(item);
    }

    public static Notification CreateUploadNotification(
        Context context,
        string username,
        List<string> directories,
        int numFiles)
    {
        var fileS = numFiles == 1
            ? SeekerState.ActiveActivityRef.GetString(ResourceConstant.String.file)
            : SeekerState.ActiveActivityRef.GetString(ResourceConstant.String.files);

        var titleText = string.Format(
            SeekerState.ActiveActivityRef.GetString(ResourceConstant.String.upload_f_string),
            numFiles,
            fileS,
            username
        );

        string directoryString;

        if (directories.Count == 1)
        {
            directoryString = SeekerState.ActiveActivityRef.GetString(ResourceConstant.String.from_directory)
                              + ": " + directories[0];
        }
        else
        {
            directoryString = SeekerState.ActiveActivityRef.GetString(ResourceConstant.String.from_directories)
                              + ": " + directories[0];

            for (var i = 0; i < directories.Count; i++)
            {
                if (i == 0)
                {
                    continue;
                }

                directoryString += ", " + directories[i];
            }
        }

        var contextText = directoryString;
        var notificationIntent = new Intent(context, typeof(MainActivity));
        notificationIntent.AddFlags(ActivityFlags.SingleTop);
        notificationIntent.PutExtra(UPLOADS_NOTIFICATION_EXTRA, 2);

        PendingIntent pendingIntent = PendingIntent.GetActivity(
            context,
            username.GetHashCode(),
            notificationIntent,
            CommonHelpers.AppendMutabilityIfApplicable(PendingIntentFlags.UpdateCurrent, true)
        );

        // no such method takes args CHANNEL_ID in API 25. API 26 = 8.0 which requires channel ID.
        // a "channel" is a category in the UI to the end user.
        Notification notification;

        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            notification = new Notification.Builder(context, UPLOADS_CHANNEL_ID)
                .SetContentTitle(titleText)
                .SetContentText(contextText)
                .SetSmallIcon(Resource.Drawable.ic_stat_soulseekicontransparent)
                .SetContentIntent(pendingIntent)
                .SetOnlyAlertOnce(true) // maybe
                .SetTicker(titleText).Build();
        }
        else
        {
            notification =
                new Notification.Builder(context)
                    .SetContentTitle(titleText)
                    .SetContentText(contextText)
                    .SetSmallIcon(ResourceConstant.Drawable.ic_stat_soulseekicontransparent)
                    .SetContentIntent(pendingIntent)
                    .SetOnlyAlertOnce(true) //maybe
                    .SetTicker(titleText).Build();
        }

        return notification;
    }

    public void SetUpLoginContinueWith(Task continuationTask)
    {
        if (continuationTask == null)
        {
            return;
        }

        if (!SharingManager.MeetsSharingConditions())
        {
            return;
        }
        
        Action<Task> getAndSetLoggedInInfoAction = task =>
        {
            // we want to 
            // UpdateStatus ??
            // inform server if we are sharing..
            // get our upload speed..
            if (task.Status == TaskStatus.Faulted || task.IsFaulted || task.IsCanceled)
            {
                return;
            }

            // don't need to get the result of this one.
            SharingManager.InformServerOfSharedFiles();

            // the result of this one if from an event handler
            SeekerState.SoulseekClient.GetUserDataAsync(SeekerState.Username);
        };

        continuationTask.ContinueWith(getAndSetLoggedInInfoAction);
    }

    public bool OnBrowseTab()
    {
        return pager.CurrentItem == 3;
    }

    private void OnBackPressedAction(OnBackPressedCallback callback)
    {
        bool relevant = false;
        try
        {
            switch (pager.CurrentItem)
            {
                //browse tab
                case 3:
                    relevant = BrowseFragment.Instance.BackButton();
                    break;
                // transfer tab
                case 2:
                {
                    if (TransfersFragment.GetCurrentlySelectedFolder() != null)
                    {
                        if (TransfersFragment.InUploadsMode)
                        {
                            TransfersFragment.CurrentlySelectedUploadFolder = null;
                        }
                        else
                        {
                            TransfersFragment.CurrentlySelectedDLFolder = null;
                        }

                        SetTransferSupportActionBarState();
                        InvalidateOptionsMenu();
                        StaticHacks.TransfersFrag.SetRecyclerAdapter();
                        StaticHacks.TransfersFrag.RestoreScrollPosition();
                        relevant = true;
                    }

                    break;
                }
            }
        }
        catch (Exception e)
        {
            // During Back Button:
            // Attempt to invoke virtual method 'java.lang.Object android.content.Context.getSystemService(java.lang.String)' on a null object reference
            Logger.FirebaseDebug("During Back Button: " + e.Message);
        }

        if (relevant)
        {
            return;
        }
        
        callback.Enabled = false;
        OnBackPressedDispatcher.OnBackPressed();
        callback.Enabled = true;
    }

    public static void DebugLogHandler(object sender, SoulseekClient.ErrorLogEventArgs e)
    {
        Logger.Debug(e.Message);
    }

    public static void SoulseekClient_ErrorLogHandler(object sender, SoulseekClient.ErrorLogEventArgs e)
    {
        if (e?.Message != null)
        {
            if (e.Message.Contains("Operation timed out"))
            {
                // this happens to me all the time and it is literally fine
                return;
            }
        }

        Logger.FirebaseDebug(e.Message);
    }

    public static bool IfLoggingInTaskCurrentlyBeingPerformedContinueWithAction(
        Action<Task> action, string msg = null, Context contextToUseForMessage = null)
    {
        lock (SeekerApplication.OurCurrentLoginTaskSyncObject)
        {
            if (!SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.Connected)
                || !SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.LoggedIn))
            {
                SeekerApplication.OurCurrentLoginTask = SeekerApplication.OurCurrentLoginTask.ContinueWith(
                    action,
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default
                );

                if (msg != null)
                {
                    if (contextToUseForMessage == null)
                    {
                        Toast.MakeText(SeekerState.ActiveActivityRef, msg, ToastLength.Short).Show();
                    }
                    else
                    {
                        Toast.MakeText(contextToUseForMessage, msg, ToastLength.Short).Show();
                    }
                }

                return true;
            }
            else
            {
                return false;
            }
        }
    }

    public static bool ShowMessageAndCreateReconnectTask(Context c, bool silent, out Task connectTask)
    {
        c ??= SeekerState.MainActivityRef;

        if (Looper.MainLooper?.Thread == Java.Lang.Thread.CurrentThread()) // tested..
        {
            if (!silent)
            {
                Toast.MakeText(c, c.GetString(ResourceConstant.String.temporary_disconnected), ToastLength.Short)?.Show();
            }
        }
        else
        {
            if (!silent)
            {
                SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                {
                    Toast.MakeText(c, c.GetString(ResourceConstant.String.temporary_disconnected), ToastLength.Short)
                        ?.Show();
                });
            }
        }

        // if we are still not connected then creating the task will throw. 
        // also if the async part of the task fails we will get task.faulted.
        try
        {
            connectTask =
                SeekerApplication.ConnectAndPerformPostConnectTasks(SeekerState.Username, SeekerState.Password);
            return true;
        }
        catch
        {
            if (!silent)
            {
                Toast.MakeText(c, c.GetString(Resource.String.failed_to_connect), ToastLength.Short).Show();
            }
        }

        connectTask = null;
        return false;
    }

    public static bool CurrentlyLoggedInButDisconnectedState()
    {
        return SeekerState.currentlyLoggedIn &&
               (SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.Disconnected)
                || SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.Disconnecting));
    }

    public static void SetStatusApi(bool away)
    {
        if (IsNotLoggedIn())
        {
            return;
        }

        if (!SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.Connected)
            || !SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.LoggedIn))
        {
            // dont log in just for this.
            // but if we later connect while still in the background, it may be best to set a flag.
            // do it when we log in... since we could not set it now...
            SeekerState.PendingStatusChangeToAwayOnline = away
                ? SeekerState.PendingStatusChange.AwayPending
                : SeekerState.PendingStatusChange.OnlinePending;

            return;
        }

        try
        {
            SeekerState.SoulseekClient.SetStatusAsync(away
                ? UserPresence.Away
                : UserPresence.Online).ContinueWith((Task t) =>
            {
                if (t.IsCompletedSuccessfully)
                {
                    SeekerState.PendingStatusChangeToAwayOnline = SeekerState.PendingStatusChange.NothingPending;
                    SeekerState.OurCurrentStatusIsAway = away;
                    var statusString = away ? "away" : "online"; // not user facing
                    Logger.Debug($"We successfully changed our status to {statusString}");
                }
                else
                {
                    Logger.Debug("SetStatusApi FAILED " + t.Exception?.Message);
                }
            });
        }
        catch (Exception exception)
        {
            Logger.Debug("SetStatusApi FAILED " + exception.Message + exception.StackTrace);
        }
    }

    private void UpdateForScreenSize()
    {
        if (!SeekerState.IsLowDpi())
        {
            return;
        }

        try
        {
            var vg = (LinearLayout)tabs.GetChildAt(0);
            var tabsCount = vg!.ChildCount;

            for (var j = 0; j < tabsCount; j++)
            {
                var vgTab = vg.GetChildAt(j) as ViewGroup;
                var tabChildrenCount = vgTab?.ChildCount;
                for (var i = 0; i < tabChildrenCount; i++)
                {
                    var tabViewChild = vgTab.GetChildAt(i);
                    if (tabViewChild is TextView textView)
                    {
                        textView.SetAllCaps(false);
                    }
                }
            }
        }
        catch
        {
            // not worth throwing over..
        }
    }

    protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
    {
        base.OnActivityResult(requestCode, resultCode, data);

        if (NEW_WRITE_EXTERNAL == requestCode
            || NEW_WRITE_EXTERNAL_VIA_LEGACY == requestCode
            || NEW_WRITE_EXTERNAL_VIA_LEGACY_SETTINGS_SCREEN == requestCode)
        {
            var showDirectoryButton = new Action(() =>
            {
                ToastUi.Long(ResourceConstant.String.seeker_needs_dl_dir_error);
                AddLoggedInLayout(StaticHacks.LoginFragment.View); // TODO: null ref
                if (!SeekerState.currentlyLoggedIn)
                {
                    BackToLogInLayout(
                        StaticHacks.LoginFragment.View,
                        (StaticHacks.LoginFragment as LoginFragment)!.LogInClick
                    );
                }

                if (StaticHacks.LoginFragment.View == null) // this can happen...
                {
                    // .View is a method so it can return null.
                    // I tested it on MainActivity.OnPause and it was in fact null.
                    ToastUi.Long(ResourceConstant.String.seeker_needs_dl_dir_choose_settings);
                    Logger.FirebaseDebug("StaticHacks.LoginFragment.View is null");
                    return;
                }

                var button = StaticHacks.LoginFragment.View
                    .FindViewById<Button>(ResourceConstant.Id.mustSelectDirectory);

                if (button == null)
                {
                    return;
                }
                
                button.Visibility = ViewStates.Visible;
                button.Click += MustSelectDirectoryClick;
            });

            if (NEW_WRITE_EXTERNAL_VIA_LEGACY_SETTINGS_SCREEN == requestCode)
            {
                // the resultCode will always be Cancelled for this since you have to back out of it.
                // so instead we check Android.OS.Environment.IsExternalStorageManager
                if (SettingsActivity.DoWeHaveProperPermissionsForInternalFilePicker())
                {
                    // phase 2 - actually pick a file.
                    FallbackFileSelection(NEW_WRITE_EXTERNAL_VIA_LEGACY);
                    return;
                }

                if (ThreadingUtils.OnUiThread())
                {
                    showDirectoryButton();
                }
                else
                {
                    RunOnUiThread(showDirectoryButton);
                }

                return;
            }

            if (resultCode == Result.Ok)
            {
                switch (requestCode)
                {
                    case NEW_WRITE_EXTERNAL:
                        SeekerState.RootDocumentFile = DocumentFile.FromTreeUri(this, data!.Data!);
                        SeekerState.SaveDataDirectoryUri = data.Data.ToString();
                        SeekerState.SaveDataDirectoryUriIsFromTree = true;

                        ContentResolver?.TakePersistableUriPermission(
                            data.Data,
                            ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission
                        );
                        break;
                    case NEW_WRITE_EXTERNAL_VIA_LEGACY:
                        SeekerState.RootDocumentFile = DocumentFile.FromFile(new Java.IO.File(data!.Data!.Path!));
                        SeekerState.SaveDataDirectoryUri = data.Data.ToString();
                        SeekerState.SaveDataDirectoryUriIsFromTree = false;
                        break;
                }
            }
            else
            {
                if (ThreadingUtils.OnUiThread())
                {
                    showDirectoryButton();
                }
                else
                {
                    RunOnUiThread(showDirectoryButton);
                }
            }
        }
        else if (MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL == requestCode ||
                 MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY == requestCode ||
                 MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY_SETTINGS_SCREEN == requestCode)
        {

            var reiterate = new Action(() =>
            {
                ToastUi.Long(ResourceConstant.String.seeker_needs_dl_dir_error);
            });

            var hideButton = new Action(() =>
            {
                StaticHacks.LoginFragment.View!.FindViewById<Button>(ResourceConstant.Id.mustSelectDirectory)!
                    .Visibility = ViewStates.Gone;
            });

            if (MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY_SETTINGS_SCREEN == requestCode)
            {
                // the resultCode will always be Cancelled for this since you have to back out of it.
                // so instead we check Android.OS.Environment.IsExternalStorageManager
                if (SettingsActivity.DoWeHaveProperPermissionsForInternalFilePicker())
                {
                    // phase 2 - actually pick a file.
                    FallbackFileSelection(MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY);
                    return;
                }
                else
                {
                    if (ThreadingUtils.OnUiThread())
                    {
                        reiterate();
                    }
                    else
                    {
                        RunOnUiThread(reiterate);
                    }

                    return;
                }
            }

            if (resultCode == Result.Ok)
            {
                switch (requestCode)
                {
                    case MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY:
                        SeekerState.RootDocumentFile = DocumentFile.FromFile(new Java.IO.File(data!.Data!.Path!));
                        SeekerState.SaveDataDirectoryUri = data.Data.ToString();
                        SeekerState.SaveDataDirectoryUriIsFromTree = false;
                        break;
                    case MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL:
                        SeekerState.RootDocumentFile = DocumentFile.FromTreeUri(this, data.Data);
                        SeekerState.SaveDataDirectoryUri = data.Data.ToString();
                        SeekerState.SaveDataDirectoryUriIsFromTree = true;
                        ContentResolver?.TakePersistableUriPermission(
                            data.Data,
                            ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission
                        );
                        break;
                }

                // hide the button
                if (ThreadingUtils.OnUiThread())
                {
                    hideButton();
                }
                else
                {
                    RunOnUiThread(hideButton);
                }
            }
            else
            {
                if (ThreadingUtils.OnUiThread())
                {
                    reiterate();
                }
                else
                {
                    RunOnUiThread(reiterate);
                }
            }
        }
    }

    private void MustSelectDirectoryClick(object sender, EventArgs e)
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.Lollipop)
        {
            Logger.Debug($"Calling MainActivity.MustSelectDirectoryClick on API {Build.VERSION.SdkInt}, " +
                         $"while it requires API 21");
            return;
        }
        
        var storageManager = Android.OS.Storage.StorageManager.FromContext(this);

        var intent = storageManager.PrimaryStorageVolume!.CreateOpenDocumentTreeIntent();
        intent.AddFlags(ActivityFlags.GrantPersistableUriPermission
                        | ActivityFlags.GrantReadUriPermission
                        | ActivityFlags.GrantWriteUriPermission
                        | ActivityFlags.GrantPrefixUriPermission);

        var res = Android.Net.Uri.Parse(string.IsNullOrEmpty(SeekerState.SaveDataDirectoryUri) 
            ? DEFAULT_MUSIC_URI 
            : SeekerState.SaveDataDirectoryUri);

        intent.PutExtra(DocumentsContract.ExtraInitialUri, res);
        try
        {
            StartActivityForResult(intent, MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL);
        }
        catch (Exception exception)
        {
            if (exception.Message.Contains(CommonHelpers.NoDocumentOpenTreeToHandle))
            {
                FallbackFileSelectionEntry(true);
            }
            else
            {
                throw;
            }
        }
    }

    private void FallbackFileSelectionEntry(bool mustSelectDirectoryButton)
    {
        bool hasManageAllFilesManisfestPermission = false;

#if IzzySoft
        hasManageAllFilesManisfestPermission = true;
#endif

        if (SeekerState.RequiresEitherOpenDocumentTreeOrManageAllFiles()
            && hasManageAllFilesManisfestPermission
            && !Android.OS.Environment.IsExternalStorageManager) // this is "step 1"
        {
            Intent allFilesPermission =
                new Intent(Android.Provider.Settings.ActionManageAppAllFilesAccessPermission);

            Android.Net.Uri packageUri = Android.Net.Uri.FromParts("package", this.PackageName, null);
            allFilesPermission.SetData(packageUri);

            this.StartActivityForResult(
                allFilesPermission,
                mustSelectDirectoryButton
                    ? MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY_SETTINGS_SCREEN
                    : NEW_WRITE_EXTERNAL_VIA_LEGACY_SETTINGS_SCREEN
            );
        }
        else if (SettingsActivity.DoWeHaveProperPermissionsForInternalFilePicker())
        {
            FallbackFileSelection(
                mustSelectDirectoryButton
                    ? MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY
                    : NEW_WRITE_EXTERNAL_VIA_LEGACY
            );
        }
        else
        {
            if (SeekerState.RequiresEitherOpenDocumentTreeOrManageAllFiles())
            {
                this.ShowSimpleAlertDialog(
                    Resource.String.error_no_file_manager_dir_manage_storage,
                    Resource.String.okay
                );
            }
            else
            {
                Toast.MakeText(
                    this,
                    SeekerState.ActiveActivityRef.GetString(Resource.String.error_no_file_manager_dir),
                    ToastLength.Long
                ).Show();
            }

            // Note:
            // If your app targets Android 12 (API level 31) or higher, its toast is limited to two lines of text
            // and shows the application icon next to the text.
            // Be aware that the line length of this text varies by screen size, so it's good to make the
            // text as short as possible.
            // on Pixel 5 emulator this limit is around 78 characters.
            // ^It must BOTH target Android 12 AND be running on Android 12^
        }
    }

    /// <summary>
    /// This RETURNS the task for ContinueWith
    /// </summary>
    /// <param name="e"></param>
    /// <returns></returns>
    public static Action<Task> DownloadContinuationActionUi(DownloadAddedEventArgs e)
    {
        Action<Task> continuationActionSaveFile = new Action<Task>(task =>
        {
            try
            {
                Action action = null;
                if (task.IsCanceled)
                {
                    Logger.Debug((DateTimeOffset.Now.ToUnixTimeMilliseconds()
                                  - SeekerState.TaskWasCancelledToastDebouncer).ToString());

                    if ((DateTimeOffset.Now.ToUnixTimeMilliseconds()
                         - SeekerState.TaskWasCancelledToastDebouncer) > 1000)
                    {
                        SeekerState.TaskWasCancelledToastDebouncer = DateTimeOffset.Now.ToUnixTimeMilliseconds();
                    }

                    // if we pressed "Retry Download" and it was in progress so we first had to cancel...
                    if (e.dlInfo.TransferItemReference.CancelAndRetryFlag)
                    {
                        e.dlInfo.TransferItemReference.CancelAndRetryFlag = false;
                        try
                        {
                            // retry download.
                            CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

                            // else when you go to cancel you are cancelling an already cancelled useless token!!
                            TransfersFragment.SetupCancellationToken(e.dlInfo.TransferItemReference,
                                cancellationTokenSource, out _);

                            var retryTask = TransfersUtil.DownloadFileAsync(
                                e.dlInfo.username,
                                e.dlInfo.fullFilename,
                                e.dlInfo.TransferItemReference.Size,
                                cancellationTokenSource,
                                out _,
                                1,
                                e.dlInfo.TransferItemReference.ShouldEncodeFileLatin1(),
                                e.dlInfo.TransferItemReference.ShouldEncodeFolderLatin1()
                            );

                            retryTask.ContinueWith(DownloadContinuationActionUi(
                                new DownloadAddedEventArgs(
                                    new DownloadInfo(
                                        e.dlInfo.username,
                                        e.dlInfo.fullFilename,
                                        e.dlInfo.TransferItemReference.Size,
                                        retryTask,
                                        cancellationTokenSource,
                                        e.dlInfo.QueueLength,
                                        0,
                                        task.Exception,
                                        e.dlInfo.Depth
                                    )
                                )
                            ), cancellationTokenSource.Token);
                        }
                        catch (Exception exception)
                        {
                            // disconnected error
                            if (exception is System.InvalidOperationException
                                && exception.Message.ToLower()
                                    .Contains("server connection must be connected and logged in"))
                            {
                                action = () =>
                                {
                                    ToastUiWithDebouncer(
                                        SeekerApplication.GetString(Resource.String.MustBeLoggedInToRetryDL),
                                        "_16_"
                                    );
                                };
                            }
                            else
                            {
                                Logger.FirebaseDebug("cancel and retry creation failed: "
                                                         + exception.Message + exception.StackTrace);
                            }

                            if (action != null)
                            {
                                SeekerState.ActiveActivityRef.RunOnUiThread(action);
                            }
                        }
                    }

                    if (e.dlInfo.TransferItemReference.CancelAndClearFlag)
                    {
                        Logger.Debug("continue with cleanup activity: " + e.dlInfo.fullFilename);
                        e.dlInfo.TransferItemReference.CancelAndRetryFlag = false;
                        e.dlInfo.TransferItemReference.InProcessing = false;

                        // this way we are sure that the stream is closed.
                        TransferItemManagerWrapper.PerformCleanupItem(e.dlInfo.TransferItemReference);
                    }

                    return;
                }
                else if (task.Status == TaskStatus.Faulted)
                {
                    bool retriable = false;
                    bool forceRetry = false;

                    // in the cases where there is mojibake, and you undo it, you still cannot download from Nicotine older client.
                    // reason being: the shared cache and disk do not match.
                    // so if you send them the filename on disk they will say it is not in the cache.
                    // and if you send them the filename from cache they will say they could not find it on disk.

                    bool resetRetryCount = false;
                    var transferItem = e.dlInfo.TransferItemReference;

                    if (task.Exception.InnerException is System.TimeoutException)
                    {
                        action = () =>
                        {
                            ToastUi.Long(SeekerState.ActiveActivityRef.GetString(Resource.String.timeout_peer));
                        };
                    }
                    else if (task.Exception.InnerException is TransferSizeMismatchException sizeException)
                    {
                        // THIS SHOULD NEVER HAPPEN. WE FIX THE TRANSFER SIZE MISMATCH INLINE.

                        // update the size and rerequest.
                        // if we have partially downloaded the file already
                        // we need to delete it to prevent corruption.
                        Logger.Debug($"OLD SIZE {transferItem.Size} NEW SIZE {sizeException.RemoteSize}");
                        transferItem.Size = sizeException.RemoteSize;
                        e.dlInfo.Size = sizeException.RemoteSize;
                        retriable = true;
                        forceRetry = true;
                        resetRetryCount = true;

                        if (!string.IsNullOrEmpty(transferItem.IncompleteParentUri))
                        {
                            try
                            {
                                TransferItemManagerWrapper.PerformCleanupItem(transferItem);
                            }
                            catch (Exception ex)
                            {
                                string exceptionString =
                                    "Failed to delete incomplete file on TransferSizeMismatchException: "
                                    + ex.ToString();

                                Logger.Debug(exceptionString);
                                Logger.FirebaseDebug(exceptionString);
                            }
                        }
                    }
                    else if (task.Exception.InnerException is DownloadDirectoryNotSetException
                             || task.Exception?.InnerException?.InnerException is DownloadDirectoryNotSetException)
                    {
                        action = () =>
                        {
                            var messageString =
                                SeekerState.ActiveActivityRef.GetString(Resource.String
                                    .FailedDownloadDirectoryNotSet);

                            ToastUiWithDebouncer(messageString, "_17_");
                        };
                    }
                    else if
                        (task.Exception
                             .InnerException is Soulseek.TransferRejectedException
                         tre) //derived class of TransferException...
                    {
                        // we go here when trying to download a locked file...
                        // (the exception only gets thrown on rejected with "not shared")
                        bool isFileNotShared = tre.Message.ToLower().Contains("file not shared");

                        // if we request a file from a soulseek NS client such as eÌe.jpg which when encoded
                        // in UTF fails to be decoded by Latin1
                        // soulseek NS will send TransferRejectedException "File Not Shared."
                        // with our filename (the filename will be identical).
                        // when we retry lets try a Latin1 encoding.  If no special characters this will not make
                        // any difference and it will be just a normal retry.
                        // we only want to try this once. and if it fails reset
                        // it to normal and do not try it again.
                        // if we encode the same way we decode, then such a thing will not occur.

                        // in the nicotine 3.1.1 and earlier, if we request a file such as
                        // "fÃ¶r", nicotine will encode it in Latin1.  We will
                        // decode it as UTF8, encode it back as UTF8 and then they will decode it as UTF-8
                        // resulting in för".  So even though we encoded and decoded
                        // in the same way there can still be an issue.  If we force legacy it will be fixed.


                        // always set this since it only shows if we DO NOT retry
                        if (isFileNotShared)
                        {
                            action = () =>
                            {
                                var messageString =
                                    SeekerState.ActiveActivityRef.GetString(Resource.String
                                        .transfer_rejected_file_not_shared);

                                ToastUiWithDebouncer(messageString, "_2_");
                            }; // needed
                        }
                        else
                        {
                            action = () =>
                            {
                                var messageString =
                                    SeekerState.ActiveActivityRef.GetString(Resource.String.transfer_rejected);
                                ToastUiWithDebouncer(messageString, "_2_");
                            }; // needed
                        }

                        Logger.Debug("rejected. is not shared: " + isFileNotShared);
                    }
                    else if (task.Exception.InnerException is Soulseek.TransferException)
                    {
                        action = () =>
                        {
                            ToastUiWithDebouncer(
                                string.Format(SeekerState.ActiveActivityRef
                                        .GetString(Resource.String.failed_to_establish_connection_to_peer),
                                    e.dlInfo.username),
                                "_1_",
                                e?.dlInfo?.username ?? string.Empty
                            );
                        };
                    }
                    else if (task.Exception.InnerException is Soulseek.UserOfflineException)
                    {
                        action = () =>
                        {
                            ToastUiWithDebouncer(
                                task.Exception.InnerException.Message,
                                "_3_",
                                e?.dlInfo?.username ?? string.Empty
                            );
                        }; // needed. "User x appears to be offline"
                    }
                    else if (task.Exception.InnerException is Soulseek.SoulseekClientException
                             && task.Exception.InnerException.Message != null
                             && task.Exception.InnerException.Message.ToLower()
                                 .Contains(Soulseek.SoulseekClient.FailedToEstablishDirectOrIndirectStringLower))
                    {
                        Logger.Debug("Task Exception: " + task.Exception.InnerException.Message);
                        action = () =>
                        {
                            ToastUiWithDebouncer(
                                SeekerState.ActiveActivityRef
                                    .GetString(Resource.String.failed_to_establish_direct_or_indirect),
                                "_4_"
                            );
                        };
                    }
                    else if (task.Exception.InnerException.Message != null
                             && task.Exception.InnerException.Message.ToLower()
                                 .Contains("read error: remote connection closed"))
                    {
                        retriable = true;
                        Logger.Debug("Unhandled task exception: " + task.Exception.InnerException.Message);
                        action = () =>
                        {
                            ToastUi.Long(
                                SeekerState.ActiveActivityRef.GetString(Resource.String.remote_conn_closed));
                        };

                        if (NetworkHandoffDetector.HasHandoffOccuredRecently())
                        {
                            resetRetryCount = true;
                        }
                    }
                    else if (task.Exception.InnerException.Message != null
                             && task.Exception.InnerException.Message.ToLower()
                                 .Contains("network subsystem is down"))
                    {
                        // if we have internet again by the time we get here then its retriable.
                        // this is often due to handoff. handoff either causes this or "remote connection closed"
                        if (ConnectionReceiver.DoWeHaveInternet())
                        {
                            Logger.Debug("we do have internet");
                            action = () =>
                            {
                                ToastUi.Long(SeekerState.ActiveActivityRef
                                    .GetString(Resource.String.remote_conn_closed));
                            };

                            retriable = true;
                            if (NetworkHandoffDetector.HasHandoffOccuredRecently())
                            {
                                resetRetryCount = true;
                            }
                        }
                        else
                        {
                            action = () =>
                            {
                                ToastUi.Long(
                                    SeekerState.ActiveActivityRef.GetString(Resource.String.network_down));
                            };
                        }

                        Logger.Debug("Unhandled task exception: " + task.Exception.InnerException.Message);

                    }
                    else if (task.Exception.InnerException.Message != null && task.Exception.InnerException.Message
                                 .ToLower().Contains("reported as failed by"))
                    {
                        // if we request a file from a soulseek NS client such as eÌÌÌe.jpg which when
                        // encoded in UTF fails to be decoded by Latin1
                        // soulseek NS will send UploadFailed with our filename (the filename will be identical).
                        // when we retry lets try a Latin1 encoding.  If no special characters this will not make
                        // any difference and it will be just a normal retry.
                        // we only want to try this once. and if it fails reset it to normal and
                        // do not try it again.

                        retriable = true;
                        Logger.Debug("Unhandled task exception: " + task.Exception.InnerException.Message);
                        action = () =>
                        {
                            ToastUi.Long(
                                SeekerState.ActiveActivityRef.GetString(Resource.String.reported_as_failed));
                        };
                    }
                    else if (task.Exception.InnerException.Message != null
                             && task.Exception.InnerException.Message.ToLower()
                                 .Contains(Soulseek.SoulseekClient.FailedToEstablishDirectOrIndirectStringLower))
                    {
                        Logger.Debug("Unhandled task exception: " + task.Exception.InnerException.Message);
                        action = () =>
                        {
                            ToastUiWithDebouncer(
                                SeekerState.ActiveActivityRef
                                    .GetString(Resource.String.failed_to_establish_direct_or_indirect),
                                "_5_"
                            );
                        };
                    }
                    else
                    {
                        retriable = true;

                        // the server connection task.Exception.InnerException.Message.Contains("The server connection was closed unexpectedly")
                        // this seems to be retry able
                        // or task.Exception.InnerException.InnerException.Message.Contains("The server connection was closed unexpectedly""
                        // or task.Exception.InnerException.Message.Contains("Transfer failed: Read error: Object reference not set to an instance of an object

                        bool unknownException = true;
                        if (task.Exception != null && task.Exception.InnerException != null)
                        {
                            // I get a lot of null refs from task.Exception.InnerException.Message
                            Logger.Debug("Unhandled task exception: " + task.Exception.InnerException.Message);

                            // is thrown by Stream.Close()
                            if (task.Exception.InnerException.Message.StartsWith("Disk full."))
                            {
                                action = () =>
                                {
                                    ToastUi.Long(SeekerState.ActiveActivityRef
                                        .GetString(Resource.String.error_no_space));
                                };
                                unknownException = false;
                            }

                            if (task.Exception.InnerException.InnerException != null && unknownException)
                            {
                                if (task.Exception.InnerException.InnerException.Message
                                        .Contains("ENOSPC (No space left on device)")
                                    || task.Exception.InnerException.InnerException.Message
                                        .Contains("Read error: Disk full."))
                                {
                                    action = () =>
                                    {
                                        ToastUi.Long(SeekerState.ActiveActivityRef
                                            .GetString(Resource.String.error_no_space));
                                    };
                                    unknownException = false;
                                }

                                // 1.983 - Non-fatal Exception: java.lang.Throwable: InnerInnerException: Transfer failed: Read error: Object reference not set to an instance of an object  at Soulseek.SoulseekClient.DownloadToStreamAsync (System.String username, System.String filename, System.IO.Stream outputStream, System.Nullable`1[T] size, System.Int64 startOffset, System.Int32 token, Soulseek.TransferOptions options, System.Threading.CancellationToken cancellationToken) [0x00cc2] in <bda1848b50e64cd7b441e1edf9da2d38>:0 
                                if (task.Exception.InnerException.InnerException.Message.ToLower()
                                    .Contains(Soulseek.SoulseekClient.FailedToEstablishDirectOrIndirectStringLower))
                                {
                                    unknownException = false;
                                }

                                if (unknownException)
                                {
                                    Logger.FirebaseDebug("InnerInnerException: "
                                                             + task.Exception.InnerException.InnerException.Message
                                                             + task.Exception.InnerException
                                                                 .InnerException.StackTrace);
                                }

                                // this is to help with the collection was modified
                                if (task.Exception.InnerException.InnerException.InnerException != null
                                    && unknownException)
                                {
                                    Logger.FirebaseInfo("InnerInnerException: "
                                                                 + task.Exception.InnerException
                                                                     .InnerException.Message
                                                                 + task.Exception.InnerException
                                                                     .InnerException.StackTrace);

                                    var innerInner = task.Exception.InnerException.InnerException.InnerException;

                                    //1.983 - Non-fatal Exception: java.lang.Throwable: InnerInnerException: Transfer failed: Read error: Object reference not set to an instance of an object  at Soulseek.SoulseekClient.DownloadToStreamAsync (System.String username, System.String filename, System.IO.Stream outputStream, System.Nullable`1[T] size, System.Int64 startOffset, System.Int32 token, Soulseek.TransferOptions options, System.Threading.CancellationToken cancellationToken) [0x00cc2] in <bda1848b50e64cd7b441e1edf9da2d38>:0 
                                    Logger.FirebaseDebug("Innerx3_Exception: " + innerInner.Message
                                        + innerInner.StackTrace);
                                }
                            }

                            if (unknownException)
                            {
                                if (task.Exception.InnerException.StackTrace
                                    .Contains("System.Xml.Serialization.XmlSerializationWriterInterpreter"))
                                {
                                    if (task.Exception.InnerException.StackTrace.Length > 1201)
                                    {
                                        Logger.FirebaseDebug("xml Unhandled task exception 2nd part: "
                                                                 + task.Exception.InnerException.StackTrace
                                                                     .Skip(1000).ToString());
                                    }

                                    Logger.FirebaseDebug("xml Unhandled task exception: "
                                                             + task.Exception.InnerException.Message
                                                             + task.Exception.InnerException.StackTrace);
                                }
                                else
                                {
                                    Logger.FirebaseDebug("dlcontaction Unhandled task exception: "
                                                             + task.Exception.InnerException.Message
                                                             + task.Exception.InnerException.StackTrace);
                                }
                            }
                        }
                        else if (task.Exception != null && unknownException)
                        {
                            Logger.FirebaseDebug("Unhandled task exception (little info): "
                                                     + task.Exception.Message);

                            Logger.Debug("Unhandled task exception (little info):" + task.Exception.Message);
                        }
                    }


                    if (forceRetry
                        || ((resetRetryCount || e.dlInfo.RetryCount == 0)
                            && (SeekerState.AutoRetryDownload)
                            && retriable))
                    {
                        Logger.Debug("!! retry the download " + e.dlInfo.fullFilename);
                        try
                        {
                            // retry download.
                            var cancellationTokenSource = new CancellationTokenSource();

                            // else when you go to cancel you are cancelling an already cancelled useless token!!
                            TransfersFragment.SetupCancellationToken(
                                e.dlInfo.TransferItemReference,
                                cancellationTokenSource,
                                out _);

                            var retryTask = TransfersUtil.DownloadFileAsync(
                                e.dlInfo.username,
                                e.dlInfo.fullFilename,
                                e.dlInfo.Size,
                                cancellationTokenSource,
                                out _,
                                1,
                                e.dlInfo.TransferItemReference.ShouldEncodeFileLatin1(),
                                e.dlInfo.TransferItemReference.ShouldEncodeFolderLatin1());

                            retryTask.ContinueWith(DownloadContinuationActionUi(
                                new DownloadAddedEventArgs(
                                    new DownloadInfo(
                                        e.dlInfo.username,
                                        e.dlInfo.fullFilename,
                                        e.dlInfo.Size,
                                        retryTask,
                                        cancellationTokenSource,
                                        e.dlInfo.QueueLength,
                                        resetRetryCount ? 0 : 1, task.Exception,
                                        e.dlInfo.Depth
                                    )
                                )
                            ), cancellationTokenSource.Token);

                            return; // i.e. don't toast anything just retry.
                        }
                        catch (System.Exception e)
                        {
                            // if this happens at least log the normal message....
                            Logger.FirebaseDebug("retry creation failed: " + e.Message + e.StackTrace);
                        }

                    }

                    if (e.dlInfo.RetryCount == 1 && e.dlInfo.PreviousFailureException != null)
                    {
                        Logger.FirebaseDebug("auto retry failed: prev exception: "
                                    + e.dlInfo.PreviousFailureException.InnerException?.Message?.ToString()
                                    + "new exception: "
                                    + task.Exception?.InnerException?.Message?.ToString());
                    }

                    action ??= () =>
                    {
                        ToastUi.Long(SeekerState.ActiveActivityRef
                            .GetString(Resource.String.error_unspecified));
                    };

                    SeekerState.ActiveActivityRef.RunOnUiThread(action);
                    return;
                }

                // failed downloads return before getting here...

                if (e.dlInfo.RetryCount == 1 && e.dlInfo.PreviousFailureException != null)
                {
                    Logger.FirebaseDebug("auto retry succeeded: prev exception: "
                                + e.dlInfo.PreviousFailureException.InnerException?.Message);
                }

                if (!SeekerState.DisableDownloadToastNotification)
                {
                    action = () =>
                    {
                        ToastUi.Long(CommonHelpers.GetFileNameFromFile(e.dlInfo.fullFilename) +
                                     " " + SeekerApplication.GetString(Resource.String.FinishedDownloading));
                    };

                    SeekerState.ActiveActivityRef.RunOnUiThread(action);
                }

                var finalUri = string.Empty;
                if (task is Task<byte[]> tbyte)
                {
                    var noSubfolder = e.dlInfo.TransferItemReference.TransferItemExtra.HasFlag(TransferItemExtras.NoSubfolder);

                    var path = StorageUtils.SaveToFile(
                        e.dlInfo.fullFilename,
                        e.dlInfo.username,
                        tbyte.Result,
                        null,
                        null,
                        true,
                        e.dlInfo.Depth,
                        noSubfolder,
                        out finalUri);

                    StorageUtils.SaveFileToMediaStore(path);
                }
                else if (task is Task<Tuple<string, string>> tString)
                {
                    // move file...
                    var noSubfolder = e.dlInfo.TransferItemReference.TransferItemExtra.HasFlag(TransferItemExtras.NoSubfolder);

                    var path = StorageUtils.SaveToFile(
                        e.dlInfo.fullFilename,
                        e.dlInfo.username,
                        null,
                        Android.Net.Uri.Parse(tString.Result.Item1),
                        Android.Net.Uri.Parse(tString.Result.Item2),
                        false,
                        e.dlInfo.Depth,
                        noSubfolder,
                        out finalUri);

                    StorageUtils.SaveFileToMediaStore(path);
                }
                else
                {
                    Logger.FirebaseDebug("Very bad. Task is not the right type.....");
                }

                e.dlInfo.TransferItemReference.FinalUri = finalUri;
            }
            finally
            {
                e.dlInfo.TransferItemReference.InProcessing = false;
            }
        });

        return continuationActionSaveFile;
    }
    
    /// <summary>
    /// This is to solve the problem of, are all the toasts part of the same session?  
    /// For example if you download a locked folder of 20 files, you will get immediately 20 toasts
    /// So our logic is, if you just did a message, wait a full second before showing anything more.
    /// </summary>
    /// <param name="msgToToast"></param>
    /// <param name="caseOrCode"></param>
    /// <param name="usernameIfApplicable"></param>
    /// <param name="seconds">might be useful to increase this if something has a lot of variance even
    /// if requested at the same time, like a timeout.</param>
    private static void ToastUiWithDebouncer(string msgToToast, string caseOrCode, string usernameIfApplicable = "",
        int seconds = 1)
    {
        var curTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
        
        // if it does not exist then updatedTime will be curTime.
        // If it does exist but is older than a second then updated time will also be curTime.
        // In those two cases, show the toast.
        Logger.Debug("curtime " + curTime);
        
        var stale = false;
        var updatedTime = _toastUiDebouncer.AddOrUpdate(caseOrCode + usernameIfApplicable, curTime,
            (_, oldValue) =>
            {
                Logger.Debug("key exists: " + (curTime - oldValue));
                stale = (curTime - oldValue) < (seconds * 1000);
                if (stale)
                {
                    Logger.Debug("stale");
                }

                return stale ? oldValue : curTime;
            });
        
        Logger.Debug("updatedTime " + updatedTime);
        if (!stale)
        {
            ToastUi.Long(msgToToast);
        }
    }

    private static System.Collections.Concurrent.ConcurrentDictionary<string, long> _toastUiDebouncer = new();
    
    /// <param name="force">the log in layout is full of hacks. that being said force 
    ///   makes it so that if we are currently logged in to still add the logged in fragment
    ///   if not there, which makes sense. </param>
    public static void AddLoggedInLayout(View rootView = null, bool force = false)
    {
        View button = StaticHacks.RootView?.FindViewById<Button>(ResourceConstant.Id.buttonLogout);
        View buttonTryTwo = rootView?.FindViewById<Button>(ResourceConstant.Id.buttonLogout);
        var buttonIsAttached = false;
        var buttonTwoIsAttached = false;
        if (button != null && button.IsAttachedToWindow)
        {
            buttonIsAttached = true;
        }

        if (buttonTryTwo != null && buttonTryTwo.IsAttachedToWindow)
        {
            buttonTwoIsAttached = true;
        }

        if (buttonIsAttached || buttonTwoIsAttached || (SeekerState.currentlyLoggedIn && !force))
        {
            return;
        }
        
        // THIS MEANS THAT WE STILL HAVE THE LOGINFRAGMENT NOT THE LOGGEDIN FRAGMENT
        var action1 = new Action(() =>
        {
            (rootView as ViewGroup)?.AddView(
                SeekerState.MainActivityRef.LayoutInflater.Inflate(ResourceConstant.Layout.loggedin,
                    (ViewGroup)rootView, false));
        });
        
        if (ThreadingUtils.OnUiThread())
        {
            action1();
        }
        else
        {
            SeekerState.MainActivityRef.RunOnUiThread(action1);
        }
    }

    public static void UpdateUiForLoggedIn(View rootView = null, EventHandler btnClick = null,
        View cWelcome = null, View cBtn = null, ViewGroup cLoading = null, EventHandler settingClick = null)
    {
        var action = new Action(() =>
        {
            // this is the case where it already has the loggedin fragment loaded.
            Button btn = null;
            TextView welcome = null;
            ViewGroup loggingInLayout = null;
            ViewGroup logInLayout = null;

            Button settings = null;
            try
            {
                var localRootView = StaticHacks.RootView != null && StaticHacks.RootView.IsAttachedToWindow
                    ? StaticHacks.RootView
                    : rootView!;

                btn = localRootView.FindViewById<Button>(ResourceConstant.Id.buttonLogout);
                welcome = localRootView.FindViewById<TextView>(ResourceConstant.Id.userNameView);
                loggingInLayout = localRootView.FindViewById<ViewGroup>(ResourceConstant.Id.loggingInLayout);
                logInLayout = localRootView.FindViewById<ViewGroup>(ResourceConstant.Id.logInLayout);
                settings = localRootView.FindViewById<Button>(ResourceConstant.Id.settingsButton);
            }
            catch
            {
                // Intentional no-op
            }

            if (welcome != null)
            {
                // meanwhile: rootView.FindViewById<TextView>(Resource.Id.userNameView).
                // so I don't think that the welcome here is the right one.. I dont think it exists.
                // try checking properties such as isAttachedToWindow, getWindowVisiblity etx...
                welcome.Visibility = ViewStates.Visible;
                
                btn.Visibility = ViewStates.Visible;
                settings.Visibility = ViewStates.Visible;


                settings.Click -= settingClick;
                settings.Click += settingClick;
                ViewCompat.SetTranslationZ(btn, 90);
                btn.Click -= btnClick;
                btn.Click += btnClick;
                loggingInLayout.Visibility = ViewStates.Gone;
                welcome.Text = string
                    .Format(SeekerApplication.GetString(ResourceConstant.String.welcome), SeekerState.Username);
            }
            else if (cWelcome != null)
            {
                cWelcome.Visibility = ViewStates.Visible;
                cBtn.Visibility = ViewStates.Visible;
                ViewCompat.SetTranslationZ(cBtn, 90);
                cLoading.Visibility = ViewStates.Gone;
            }
            else
            {
                StaticHacks.UpdateUI = true; // if we aren't ready rn then do it when we are...
            }

            if (logInLayout == null)
            {
                return;
            }
            
            var loginButton = logInLayout.FindViewById<Button>(ResourceConstant.Id.buttonLogin);
            ViewCompat.SetTranslationZ(loginButton, 0);
            logInLayout.Visibility = ViewStates.Gone;

        });
        
        if (ThreadingUtils.OnUiThread())
        {
            action();
        }
        else
        {
            SeekerState.MainActivityRef.RunOnUiThread(action);
        }
    }

    public static bool IsNotLoggedIn()
    {
        return !SeekerState.currentlyLoggedIn || SeekerState.Username == null || SeekerState.Password == null ||
               SeekerState.Username == string.Empty;
    }
    
    public static void BackToLogInLayout(View rootView, EventHandler logInClick, bool clearUserPass = true)
    {
        var action = new Action(() =>
        {
            // this is the case where it already has the loggedin fragment loaded.
            Button bttn = null;
            TextView welcome = null;
            TextView loading = null;
            ViewGroup loggingInLayout = null;
            ViewGroup logInLayout = null;
            Button buttonLogin = null;
            Button settings = null;
            
            Logger.Debug("BackToLogInLayout");
            try
            {
                if (StaticHacks.RootView != null && StaticHacks.RootView.IsAttachedToWindow)
                {
                    Logger.Debug("StaticHacks.RootView != null");
                    bttn = StaticHacks.RootView.FindViewById<Button>(ResourceConstant.Id.buttonLogout);
                    welcome = StaticHacks.RootView.FindViewById<TextView>(ResourceConstant.Id.userNameView);
                    loggingInLayout = StaticHacks.RootView.FindViewById<ViewGroup>(ResourceConstant.Id.loggingInLayout);

                    // this is the case we have a bad SAVED user pass....
                    try
                    {
                        logInLayout = StaticHacks.RootView.FindViewById<ViewGroup>(ResourceConstant.Id.logInLayout);
                        buttonLogin = StaticHacks.RootView.FindViewById<Button>(ResourceConstant.Id.buttonLogin);
                        if (logInLayout == null)
                        {
                            var relLayout = SeekerState.MainActivityRef
                                .LayoutInflater.Inflate(ResourceConstant.Layout.login,
                                    StaticHacks.RootView as ViewGroup, false) as ViewGroup;
                            
                            relLayout!.LayoutParameters =
                                new ViewGroup.LayoutParams(StaticHacks.RootView.LayoutParameters);
                            
                            (StaticHacks.RootView as ViewGroup)?.AddView(
                                SeekerState.MainActivityRef.LayoutInflater
                                    .Inflate(ResourceConstant.Layout.login,
                                        StaticHacks.RootView as ViewGroup, false));
                        }

                        settings = StaticHacks.RootView.FindViewById<Button>(ResourceConstant.Id.settingsButton);
                        buttonLogin = StaticHacks.RootView.FindViewById<Button>(ResourceConstant.Id.buttonLogin);
                        logInLayout = StaticHacks.RootView.FindViewById<ViewGroup>(ResourceConstant.Id.logInLayout);
                        buttonLogin!.Click -= logInClick;
                        (StaticHacks.LoginFragment as LoginFragment)!.rootView = StaticHacks.RootView;
                        (StaticHacks.LoginFragment as LoginFragment)!.SetUpLogInLayout();
                    }
                    catch (Exception ex)
                    {
                        Logger.Debug("BackToLogInLayout" + ex.Message);
                    }

                }
                else
                {
                    Logger.Debug("StaticHacks.RootView == null");
                    bttn = rootView.FindViewById<Button>(ResourceConstant.Id.buttonLogout);
                    welcome = rootView.FindViewById<TextView>(ResourceConstant.Id.userNameView);
                    loggingInLayout = rootView.FindViewById<ViewGroup>(ResourceConstant.Id.loggingInLayout);
                    logInLayout = rootView.FindViewById<ViewGroup>(ResourceConstant.Id.logInLayout);
                    buttonLogin = rootView.FindViewById<Button>(ResourceConstant.Id.buttonLogin);
                    settings = rootView.FindViewById<Button>(ResourceConstant.Id.settingsButton);
                }
            }
            catch
            {
                // Intentional no-op
            }

            Logger.Debug("logInLayout is here? " + (logInLayout != null));
            if (logInLayout == null)
            {
                return;
            }
            
            logInLayout.Visibility = ViewStates.Visible;
            if (!clearUserPass && !string.IsNullOrEmpty(SeekerState.Username))
            {
                logInLayout.FindViewById<EditText>(ResourceConstant.Id.etUsername).Text = SeekerState.Username;
                logInLayout.FindViewById<EditText>(ResourceConstant.Id.etPassword).Text = SeekerState.Password;
            }

            ViewCompat.SetTranslationZ(buttonLogin, 90);

            if (loading == null)
            {
                AddLoggedInLayout(rootView);
                if (rootView != null)
                {
                    bttn = rootView.FindViewById<Button>(ResourceConstant.Id.buttonLogout);
                    welcome = rootView.FindViewById<TextView>(ResourceConstant.Id.userNameView);
                    loggingInLayout = rootView.FindViewById<ViewGroup>(ResourceConstant.Id.loggingInLayout);
                    settings = rootView.FindViewById<Button>(ResourceConstant.Id.settingsButton);
                }

                if (rootView == null && loading == null && StaticHacks.RootView != null)
                {
                    bttn = StaticHacks.RootView.FindViewById<Button>(ResourceConstant.Id.buttonLogout);
                    welcome = StaticHacks.RootView.FindViewById<TextView>(ResourceConstant.Id.userNameView);
                    loggingInLayout = 
                        StaticHacks.RootView.FindViewById<ViewGroup>(ResourceConstant.Id.loggingInLayout);
                    settings = StaticHacks.RootView.FindViewById<Button>(ResourceConstant.Id.settingsButton);
                }
            }

            // can get null ref here!!! (at least before the .AddLoggedInLayout code..
            loggingInLayout.Visibility = ViewStates.Gone;
            welcome.Visibility = ViewStates.Gone;
            settings.Visibility = ViewStates.Gone;
            bttn.Visibility = ViewStates.Gone;
            ViewCompat.SetTranslationZ(bttn, 0);

        });
        if (ThreadingUtils.OnUiThread())
        {
            action();
        }
        else
        {
            SeekerState.MainActivityRef.RunOnUiThread(action);
        }
    }
    
    public static void UpdateUiForLoggingInLoading(View rootView = null)
    {
        Logger.Debug("UpdateUIForLoggingInLoading");
        var action = new Action(() =>
        {
            // this is the case where it already has the loggedin fragment loaded.
            Button logoutButton = null;
            TextView welcome = null;
            ViewGroup loggingInView = null;
            ViewGroup logInLayout = null;
            Button settingsButton = null;
            try
            {
                if (StaticHacks.RootView != null && rootView == null)
                {
                    logoutButton = StaticHacks.RootView.FindViewById<Button>(ResourceConstant.Id.buttonLogout);
                    settingsButton = StaticHacks.RootView.FindViewById<Button>(ResourceConstant.Id.settingsButton);
                    welcome = StaticHacks.RootView.FindViewById<TextView>(ResourceConstant.Id.userNameView);
                    loggingInView = StaticHacks.RootView.FindViewById<ViewGroup>(ResourceConstant.Id.loggingInLayout);
                    logInLayout = StaticHacks.RootView.FindViewById<ViewGroup>(ResourceConstant.Id.logInLayout);

                }
                else if (rootView != null)
                {
                    logoutButton = rootView.FindViewById<Button>(ResourceConstant.Id.buttonLogout);
                    settingsButton = rootView.FindViewById<Button>(ResourceConstant.Id.settingsButton);
                    welcome = rootView.FindViewById<TextView>(ResourceConstant.Id.userNameView);
                    loggingInView = rootView.FindViewById<ViewGroup>(ResourceConstant.Id.loggingInLayout);
                    logInLayout = rootView.FindViewById<ViewGroup>(ResourceConstant.Id.logInLayout);
                }
            }
            catch
            {
                // Intentional no-op
            }

            if (logInLayout == null)
            {
                return;
            }
            
            // TODO: change back..
            //       basically when we AddChild we add it UNDER the logInLayout..
            //       so making it gone makes everything gone... we need a root layout for it...
            logInLayout.Visibility = ViewStates.Gone;
            ViewCompat.SetTranslationZ(logInLayout.FindViewById<Button>(ResourceConstant.Id.buttonLogin)!, 0);
            loggingInView!.Visibility = ViewStates.Visible;
                
            // WE GET NULLREF HERE. FORCE connection already established exception
            // and maybe see what is going on here...
            welcome!.Visibility = ViewStates.Gone;
            logoutButton!.Visibility = ViewStates.Gone;
            settingsButton!.Visibility = ViewStates.Gone;
            ViewCompat.SetTranslationZ(logoutButton, 0);

        });
        if (ThreadingUtils.OnUiThread())
        {
            action();
        }
        else
        {
            SeekerState.MainActivityRef.RunOnUiThread(action);
        }
    }
    
    public static object lock_toplevel_ifexist_create = new();
    public static object lock_album_ifexist_create = new();

    public static System.IO.Stream GetIncompleteStream(string username, string fullFilename, int depth,
        out Android.Net.Uri incompleteUri, out Android.Net.Uri parentUri, out long partialLength)
    {
        var name = CommonHelpers.GetFileNameFromFile(fullFilename);
        var useDownloadDir = SeekerState.CreateCompleteAndIncompleteFolders && !SettingsActivity.UseIncompleteManualFolder();
        var useTempDir = SettingsActivity.UseTempDirectory();
        var useCustomDir = SettingsActivity.UseIncompleteManualFolder();

        var fileExists = false;
        if (SeekerState.UseLegacyStorage() && SeekerState.RootDocumentFile == null && useDownloadDir)
        {
            System.IO.FileStream fs;
            Java.IO.File incompleteDir = null;
            Java.IO.File musicDir = null;
            
            try
            {
                var rootDir = Android.OS.Environment
                    .GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryMusic)!.AbsolutePath;

                if (!(new Java.IO.File(rootDir)).Exists())
                {
                    (new Java.IO.File(rootDir)).Mkdirs();
                }

                var incompleteDirString = rootDir + @"/Soulseek Incomplete/";
                lock (lock_toplevel_ifexist_create)
                {
                    incompleteDir = new Java.IO.File(incompleteDirString);
                    if (!incompleteDir.Exists())
                    {
                        // make it and add nomedia...
                        incompleteDir.Mkdirs();
                        StorageUtils.CreateNoMediaFileLegacy(incompleteDirString);
                    }
                }

                var fullDir = rootDir + "/Soulseek Incomplete/" +
                              CommonHelpers.GenerateIncompleteFolderName(username, fullFilename,
                                  depth);
                musicDir = new Java.IO.File(fullDir);
                lock (lock_album_ifexist_create)
                {
                    if (!musicDir.Exists())
                    {
                        musicDir.Mkdirs();
                        StorageUtils.CreateNoMediaFileLegacy(fullDir);
                    }
                }

                parentUri = Android.Net.Uri.Parse(new Java.IO.File(fullDir).ToURI().ToString());
                var filePath = fullDir + "/" + name;
                Java.IO.File f = new Java.IO.File(filePath);
                if (f.Exists())
                {
                    fileExists = true;
                    fs = new System.IO.FileStream(filePath, System.IO.FileMode.Append, System.IO.FileAccess.Write,
                        System.IO.FileShare.None);
                    partialLength = f.Length();
                }
                else
                {
                    fs = System.IO.File.Create(filePath);
                    partialLength = 0;
                }

                // using incompleteUri.Path gives you filePath :)
                incompleteUri = Android.Net.Uri.Parse(new Java.IO.File(filePath).ToURI().ToString());
            }
            catch (Exception e)
            {
                Logger.FirebaseDebug("Legacy Filesystem Issue: " + e.Message + e.StackTrace 
                                     + System.Environment.NewLine + incompleteDir!.Exists() 
                                     + musicDir!.Exists() + fileExists);
                throw;
            }

            return fs;
        }
        else
        {
            DocumentFile folderDir1 = null; // this is the desired location.
            DocumentFile rootDir = null;

            var diagRootDirExistsAndCanWrite = false;
            var diagDidWeCreateSoulSeekDir = false;
            var diagSlskDirExistsAfterCreation = false;
            var rootDocumentFileIsNull = SeekerState.RootDocumentFile == null;

            if (rootDocumentFileIsNull)
            {
                throw new DownloadDirectoryNotSetException();
            }

            try
            {
                if (useDownloadDir)
                {
                    rootDir = SeekerState.RootDocumentFile;
                    Logger.Debug("using download dir" + rootDir.Uri.LastPathSegment);
                }
                else if (useTempDir)
                {
                    Java.IO.File appPrivateExternal = SeekerState.ActiveActivityRef.GetExternalFilesDir(null);
                    rootDir = DocumentFile.FromFile(appPrivateExternal);
                    Logger.Debug("using temp incomplete dir");
                }
                else if (useCustomDir)
                {
                    rootDir = SeekerState.RootIncompleteDocumentFile;
                    Logger.Debug("using custom incomplete dir" + rootDir.Uri.LastPathSegment);
                }
                else
                {
                    Logger.FirebaseDebug("!! should not get here, no dirs");
                }

                if (!rootDir.Exists())
                {
                    Logger.FirebaseDebug("rootdir (nonnull) does not exist: " + rootDir.Uri);
                    diagRootDirExistsAndCanWrite = false;
                }
                else if (!rootDir.CanWrite())
                {
                    diagRootDirExistsAndCanWrite = false;
                    Logger.FirebaseDebug("rootdir (nonnull) exists but cant write: " + rootDir.Uri);
                }
                else
                {
                    diagRootDirExistsAndCanWrite = true;
                }

                DocumentFile slskDir1 = null;
                lock (lock_toplevel_ifexist_create)
                {
                    slskDir1 = rootDir.FindFile("Soulseek Incomplete"); //does Soulseek Complete folder exist
                    if (slskDir1 == null || !slskDir1.Exists())
                    {
                        slskDir1 = rootDir.CreateDirectory("Soulseek Incomplete");
                        if (slskDir1 == null)
                        {
                            string diagMessage = CheckPermissions(rootDir.Uri);
                            Logger.FirebaseDebug("slskDir1 is null" + rootDir.Uri + "parent: " + diagMessage);
                            Logger.FirebaseInfo("slskDir1 is null" + rootDir.Uri + "parent: " + diagMessage);
                        }
                        else if (!slskDir1.Exists())
                        {
                            Logger.FirebaseDebug("slskDir1 does not exist" + rootDir.Uri);
                        }
                        else if (!slskDir1.CanWrite())
                        {
                            Logger.FirebaseDebug("slskDir1 cannot write" + rootDir.Uri);
                        }

                        StorageUtils.CreateNoMediaFile(slskDir1);
                    }
                }
                
                if (slskDir1 == null)
                {
                    diagSlskDirExistsAfterCreation = false;
                    Logger.FirebaseDebug("slskDir1 is null");
                    Logger.FirebaseInfo("slskDir1 is null");
                }
                else
                {
                    diagSlskDirExistsAfterCreation = true;
                }


                string album_folder_name =
                    CommonHelpers.GenerateIncompleteFolderName(username, fullFilename, depth);
                
                lock (lock_album_ifexist_create)
                {
                    folderDir1 = slskDir1.FindFile(album_folder_name); // does the folder we want to save to exist
                    if (folderDir1 == null || !folderDir1.Exists())
                    {
                        folderDir1 = slskDir1.CreateDirectory(album_folder_name);
                        var rootUri = string.Empty;
                        if (folderDir1 == null)
                        {
                            if (SeekerState.RootDocumentFile != null)
                            {
                                rootUri = SeekerState.RootDocumentFile.Uri.ToString();
                            }

                            bool slskDirExistsWriteable;
                            slskDirExistsWriteable = slskDir1.Exists() && slskDir1.CanWrite();

                            string diagMessage = CheckPermissions(slskDir1.Uri);
                            
                            Logger.FirebaseInfo("folderDir1 is null:" + album_folder_name + "root: " + rootUri +
                                                "slskDirExistsWriteable" + slskDirExistsWriteable + "slskDir: " +
                                                diagMessage);
                            
                            Logger.FirebaseDebug("folderDir1 is null:" + album_folder_name + "root: " + rootUri +
                                                 "slskDirExistsWriteable" + slskDirExistsWriteable + "slskDir: " +
                                                 diagMessage);
                        }
                        else if (!folderDir1.Exists())
                        {
                            Logger.FirebaseDebug("folderDir1 does not exist:" + album_folder_name);
                        }
                        else if (!folderDir1.CanWrite())
                        {
                            Logger.FirebaseDebug("folderDir1 cannot write:" + album_folder_name);
                        }

                        StorageUtils.CreateNoMediaFile(folderDir1);
                    }
                }
                
                // THE VALID GOOD flags for a directory is Supports Dir Create.
                // The write is off in all of my cases and not necessary..
            }
            catch (Exception e)
            {
                string rootDirUri = SeekerState.RootDocumentFile?.Uri == null
                    ? "null"
                    : SeekerState.RootDocumentFile.Uri.ToString();
                Logger.FirebaseDebug("Filesystem Issue: " + rootDirUri + " " + e.Message +
                                     diagSlskDirExistsAfterCreation + diagRootDirExistsAndCanWrite +
                                     diagDidWeCreateSoulSeekDir + rootDocumentFileIsNull + e.StackTrace);
            }

            if (rootDir == null && !SeekerState.UseLegacyStorage())
            {
                SeekerState.MainActivityRef.RunOnUiThread(() =>
                {
                    ToastUi.Long(ResourceConstant.String.seeker_cannot_access_files);
                });
            }

            // BACKUP IF FOLDER DIR IS NULL
            folderDir1 ??= rootDir;

            parentUri = folderDir1.Uri;
            System.IO.Stream stream;
            DocumentFile potentialFile = folderDir1.FindFile(name); // this will return null if does not exist!!
            
            // don't do a check for length 0 because then it will go to else and create another identical file (2)
            if (potentialFile != null && potentialFile.Exists())
            {
                partialLength = potentialFile.Length();
                incompleteUri = potentialFile.Uri;
                stream = SeekerState.MainActivityRef.ContentResolver.OpenOutputStream(incompleteUri, "wa");
            }
            else
            {
                partialLength = 0;
                
                // on samsung api 19 it renames song.mp3 to song.mp3.mp3.
                // TODO: fix this! (tho below api 29 doesnt use this path anymore)
                DocumentFile mFile = CommonHelpers.CreateMediaFile(folderDir1, name);
                
                // String: name of new document, without any file extension appended;
                // the underlying provider may choose to append the extension.. Whoops...
                
                // nullref TODO TODO: if null throw custom exception so you can better handle
                // it later on in DL continuation action
                incompleteUri = mFile.Uri;
                stream = SeekerState.MainActivityRef.ContentResolver.OpenOutputStream(incompleteUri);
            }

            return stream;
        }
    }
    
    /// <summary>
    /// Check Permissions if things dont go right for better diagnostic info
    /// </summary>
    /// <param name="folder"></param>
    /// <returns></returns>
    private static string CheckPermissions(Android.Net.Uri folder)
    {
        if (SeekerState.ActiveActivityRef == null)
        {
            return string.Empty;
        }
        
        var cursor = SeekerState.ActiveActivityRef.ContentResolver!.Query(folder,
            [DocumentsContract.Document.ColumnFlags], null, null, null)!;
        
        var flags = 0;
        if (cursor.MoveToFirst())
        {
            flags = cursor.GetInt(0);
        }

        cursor.Close();
        
        var canWrite = (flags & (int)DocumentContractFlags.SupportsWrite) != 0;
        var canDirCreate = (flags & (int)DocumentContractFlags.DirSupportsCreate) != 0;
        return canWrite switch
        {
            true when canDirCreate => "Can Write and DirSupportsCreate",
            true => "Can Write and not DirSupportsCreate",
            _ => canDirCreate ? "Can not Write and can DirSupportsCreate" : "No permissions"
        };

    }
    
    protected override void OnPause()
    {
        base.OnPause();

        TransfersFragment.SaveTransferItems(sharedPreferences);
        lock (SeekerApplication.SHARED_PREF_LOCK)
        {
            var editor = sharedPreferences.Edit()!
                .PutBoolean(KeyConsts.M_CurrentlyLoggedIn, SeekerState.currentlyLoggedIn)!
                .PutString(KeyConsts.M_Username, SeekerState.Username)!
                .PutString(KeyConsts.M_Password, SeekerState.Password)!
                .PutString(KeyConsts.M_SaveDataDirectoryUri, SeekerState.SaveDataDirectoryUri)!
                .PutBoolean(KeyConsts.M_SaveDataDirectoryUriIsFromTree, SeekerState.SaveDataDirectoryUriIsFromTree)!
                .PutInt(KeyConsts.M_NumberSearchResults, SeekerState.NumberSearchResults)!
                .PutInt(KeyConsts.M_DayNightMode, SeekerState.DayNightMode)!
                .PutBoolean(KeyConsts.M_AutoClearComplete, SeekerState.AutoClearCompleteDownloads)!
                .PutBoolean(KeyConsts.M_AutoClearCompleteUploads, SeekerState.AutoClearCompleteUploads)!
                .PutBoolean(KeyConsts.M_RememberSearchHistory, SeekerState.RememberSearchHistory)!
                .PutBoolean(KeyConsts.M_RememberUserHistory, SeekerState.ShowRecentUsers)!
                .PutBoolean(KeyConsts.M_TransfersShowSizes, SeekerState.TransferViewShowSizes)!
                .PutBoolean(KeyConsts.M_TransfersShowSpeed, SeekerState.TransferViewShowSpeed)!
                .PutBoolean(KeyConsts.M_OnlyFreeUploadSlots, SeekerState.FreeUploadSlotsOnly)!
                .PutBoolean(KeyConsts.M_HideLockedSearch, SeekerState.HideLockedResultsInSearch)!
                .PutBoolean(KeyConsts.M_HideLockedBrowse, SeekerState.HideLockedResultsInBrowse)!
                .PutBoolean(KeyConsts.M_FilterSticky, SearchFragment.FilterSticky)!
                .PutString(KeyConsts.M_FilterStickyString, SearchTabHelper.FilterString)!
                .PutBoolean(KeyConsts.M_MemoryBackedDownload, SeekerState.MemoryBackedDownload)!
                .PutInt(KeyConsts.M_SearchResultStyle, (int)(SearchFragment.SearchResultStyle))!
                .PutBoolean(KeyConsts.M_DisableToastNotifications, SeekerState.DisableDownloadToastNotification)!
                .PutInt(KeyConsts.M_UploadSpeed, SeekerState.UploadSpeed)!
                .PutBoolean(KeyConsts.M_SharingOn, SeekerState.SharingOn)!
                .PutBoolean(KeyConsts.M_AllowPrivateRooomInvitations, SeekerState.AllowPrivateRoomInvitations)!;

            if (UserListManager.UserList != null)
            {
                editor.PutString(KeyConsts.M_UserList, UserListManager.AsString());
            }
            
            editor.Commit();
        }
    }

    protected override void OnSaveInstanceState(Bundle outState)
    {
        base.OnSaveInstanceState(outState);
        outState.PutBoolean(KeyConsts.M_CurrentlyLoggedIn, SeekerState.currentlyLoggedIn);
        outState.PutString(KeyConsts.M_Username, SeekerState.Username);
        outState.PutString(KeyConsts.M_Password, SeekerState.Password);
        outState.PutBoolean(KeyConsts.M_SaveDataDirectoryUriIsFromTree, SeekerState.SaveDataDirectoryUriIsFromTree);
        outState.PutString(KeyConsts.M_SaveDataDirectoryUri, SeekerState.SaveDataDirectoryUri);
        outState.PutInt(KeyConsts.M_NumberSearchResults, SeekerState.NumberSearchResults);
        outState.PutInt(KeyConsts.M_DayNightMode, SeekerState.DayNightMode);
        outState.PutBoolean(KeyConsts.M_AutoClearComplete, SeekerState.AutoClearCompleteDownloads);
        outState.PutBoolean(KeyConsts.M_AutoClearCompleteUploads, SeekerState.AutoClearCompleteUploads);
        outState.PutBoolean(KeyConsts.M_RememberSearchHistory, SeekerState.RememberSearchHistory);
        outState.PutBoolean(KeyConsts.M_RememberUserHistory, SeekerState.ShowRecentUsers);
        outState.PutBoolean(KeyConsts.M_MemoryBackedDownload, SeekerState.MemoryBackedDownload);
        outState.PutBoolean(KeyConsts.M_FilterSticky, SearchFragment.FilterSticky);
        outState.PutBoolean(KeyConsts.M_OnlyFreeUploadSlots, SeekerState.FreeUploadSlotsOnly);
        outState.PutBoolean(KeyConsts.M_HideLockedSearch, SeekerState.HideLockedResultsInSearch);
        outState.PutBoolean(KeyConsts.M_HideLockedBrowse, SeekerState.HideLockedResultsInBrowse);
        outState.PutBoolean(KeyConsts.M_DisableToastNotifications, SeekerState.DisableDownloadToastNotification);
        outState.PutInt(KeyConsts.M_SearchResultStyle, (int)(SearchFragment.SearchResultStyle));
        outState.PutString(KeyConsts.M_FilterStickyString, SearchTabHelper.FilterString);
        outState.PutInt(KeyConsts.M_UploadSpeed, SeekerState.UploadSpeed);
        outState.PutBoolean(KeyConsts.M_AllowPrivateRooomInvitations, SeekerState.AllowPrivateRoomInvitations);
        outState.PutBoolean(KeyConsts.M_SharingOn, SeekerState.SharingOn);
        
        if (UserListManager.UserList != null)
        {
            outState.PutString(KeyConsts.M_UserList, UserListManager.AsString());
        }
    }

    private void Tabs_TabSelected(object sender, TabLayout.TabSelectedEventArgs args)
    {
        Logger.Debug(args.Tab!.Position.ToString());
        
        if (args.Tab.Position == 1)
        {
            // i.e. if we are not the search tab
            return;
        }
        
        var imm = GetSystemService(InputMethodService) as InputMethodManager;
        imm?.HideSoftInputFromWindow((sender as View)?.WindowToken, 0);
    }

    private void Pager_PageSelected(object sender, ViewPager.PageSelectedEventArgs args)
    {
        // if we are changing modes and the transfers action mode is not null (i.e. is active)
        // then we need to get out of it.
        if (TransfersFragment.TransfersActionMode != null)
        {
            TransfersFragment.TransfersActionMode.Finish();
        }

        // TODO: Why is the support action bar located somewhere else?
        if (SupportActionBar == null)
        {
            return;
        }

        // in addition, each fragment is responsible for expanding their menu...
        if (args.Position == 0)
        {
            SupportActionBar.SetDisplayHomeAsUpEnabled(false);
            SupportActionBar.SetHomeButtonEnabled(false);

            SupportActionBar.SetDisplayShowCustomEnabled(false);
            SupportActionBar.SetDisplayShowTitleEnabled(true);
            SupportActionBar.Title = GetString(ResourceConstant.String.home_tab);
            toolbar.InflateMenu(ResourceConstant.Menu.account_menu);
        } 
        else if (args.Position == 1) // search
        {
            SupportActionBar.SetDisplayHomeAsUpEnabled(false);
            SupportActionBar.SetHomeButtonEnabled(false);

            SupportActionBar.SetDisplayShowCustomEnabled(true);
            SupportActionBar.SetDisplayShowTitleEnabled(false);
            SupportActionBar.SetCustomView(ResourceConstant.Layout.custom_menu_layout);
            SearchFragment.ConfigureSupportCustomView(SupportActionBar.CustomView);
            toolbar.InflateMenu(ResourceConstant.Menu.account_menu);

            if (GoToSearchTab == int.MaxValue)
            {
                return;
            }
            
            // this happens if we come from settings activity.
            // Main Activity has NOT been started.
            // SearchFragment has the .Actvity ref of an OLD activity.
            // so we are not ready yet. 
            var searchFragmentActivityNotStarted =
                !SearchFragment.Instance.Activity!.Lifecycle.CurrentState.IsAtLeast(Lifecycle.State.Started!);
            
            if (SearchFragment.Instance?.Activity == null || searchFragmentActivityNotStarted)
            {
                // let onresume go to the search tab..
                Logger.Debug("Delay Go To Wishlist Search Fragment for OnResume");
            }
            else
            {
                // can we do this now??? or should we pass this down to the search fragment
                // for when it gets created...  maybe we should put this in a like "OnResume"
                Logger.Debug("Do Go To Wishlist in page selected");
                SearchFragment.Instance.GoToTab(GoToSearchTab, false, true);
                GoToSearchTab = int.MaxValue;
            }
        }
        else if (args.Position == 2)
        {
            SupportActionBar.SetDisplayShowCustomEnabled(false);
            SupportActionBar.SetDisplayShowTitleEnabled(true);
            
            SetTransferSupportActionBarState();

            toolbar.InflateMenu(ResourceConstant.Menu.browse_menu_empty); //todo remove?
        }
        else if (args.Position == 3)
        {
            SupportActionBar.SetDisplayHomeAsUpEnabled(false);
            SupportActionBar.SetHomeButtonEnabled(false);
            SupportActionBar.SetDisplayShowCustomEnabled(false);
            SupportActionBar.SetDisplayShowTitleEnabled(true);
            SupportActionBar.Title = string.IsNullOrEmpty(BrowseFragment.CurrentUsername)
                ? GetString(ResourceConstant.String.browse_tab)
                : $"{GetString(ResourceConstant.String.browse_tab)}: {BrowseFragment.CurrentUsername}";
            
            toolbar.InflateMenu(ResourceConstant.Menu.transfers_menu);
        }
    }

    public void SetTransferSupportActionBarState()
    {
        if (SupportActionBar == null)
        {
            return;
        }
        
        if (TransfersFragment.InUploadsMode)
        {
            var uploadFolderPresent = TransfersFragment.CurrentlySelectedUploadFolder != null;
            SupportActionBar.Title = uploadFolderPresent
                ? TransfersFragment.CurrentlySelectedUploadFolder.FolderName
                : GetString(ResourceConstant.String.Uploads);
            SupportActionBar.SetDisplayHomeAsUpEnabled(uploadFolderPresent);
            SupportActionBar.SetHomeButtonEnabled(uploadFolderPresent);
            return;
        }

        var downloadFolderPresent = TransfersFragment.CurrentlySelectedDLFolder != null;
        SupportActionBar.Title = downloadFolderPresent
            ? TransfersFragment.CurrentlySelectedDLFolder.FolderName
            : GetString(ResourceConstant.String.Downloads);
        SupportActionBar.SetDisplayHomeAsUpEnabled(downloadFolderPresent);
        SupportActionBar.SetHomeButtonEnabled(downloadFolderPresent);
    }

    public override void OnRequestPermissionsResult(int requestCode, string[] permissions,
        [GeneratedEnum] Permission[] grantResults)
    {
        Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode != POST_NOTIFICATION_PERMISSION)
        {
            if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
            {
                return;
            }

            // TODO: - why?? this was added in initial commit. kills process if permission not granted?
            FinishAndRemoveTask();
        }
        
        base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
    }

    private class MenuChangerOnPageSwitch([NotNull] BottomNavigationView bottomNavigationView) 
        : Java.Lang.Object, ViewPager.IOnPageChangeListener
    {
        public void OnPageScrolled(int position, float positionOffset, int positionOffsetPixels)
        {
            // Intentional no-op
        }

        public void OnPageScrollStateChanged(int state)
        {
            // Intentional no-op
        }

        public void OnPageSelected(int position)
        {
            if (position == -1)
            {
                return;
            }
            
            var menu = bottomNavigationView.Menu as MenuBuilder;
            menu?.GetItem(position)?.SetCheckable(true); // this is necessary if side scrolling...
            menu?.GetItem(position)?.SetChecked(true);
        }
    }

    private class NavigationBarItemSelectedListener(ViewPager pager) 
        : Java.Lang.Object, NavigationBarView.IOnItemSelectedListener
    {
        public bool OnNavigationItemSelected(IMenuItem item)
        {
            pager.CurrentItem = item.ItemId switch
            {
                ResourceConstant.Id.navigation_home => 0,
                ResourceConstant.Id.navigation_search => 1,
                ResourceConstant.Id.navigation_transfers => 2,
                ResourceConstant.Id.navigation_browse => 3,
                _ => pager.CurrentItem
            };

            return true;
        }   
    }
}
