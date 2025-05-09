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

using Android.Content;
using Android.OS;
using Android.Text;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.Fragment.App;
using AndroidX.RecyclerView.Widget;
using Common;
using Google.Android.Material.BottomNavigation;
using Google.Android.Material.BottomSheet;
using Google.Android.Material.FloatingActionButton;
using Seeker.Transfers;
using Soulseek;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _Microsoft.Android.Resource.Designer;
using Seeker.Helpers;
using Seeker.Main;
using Seeker.Managers;
using Seeker.Models;
using Seeker.Utils;

namespace Seeker
{
    public class BrowseFragment : Fragment
    {
        //for filtering - we always just get the filtered copy from the main copy on the fly.
        //the main copy will move up, down, etc.  so no need for the filtered copy to keep track of any of that
        //just do what we normally do and then generate the filtered copy as the very last step

        //private static IParcelable listViewState = null; restoring this did not restore scroll pos
        public View rootView;

        private ListView listViewDirectories;
        private RecyclerView treePathRecyclerView;
        private LinearLayoutManager treePathLayoutManager;
        private TreePathRecyclerAdapter treePathRecyclerAdapter;

        private static List<DataItem> dataItemsForListView = new List<DataItem>();
        private static List<PathItem> pathItems = new List<PathItem>();
        public static string CurrentUsername;
        private static Tuple<string, List<DataItem>> cachedFilteredDataItemsForListView = null;//to help with superSetQueries new Tuple<string, List<DataItem>>; 
        private static int diagnostics_count;
        private static List<DataItem> filteredDataItemsForListView = new List<DataItem>();

        private static List<DataItem> dataItemsForDownload = null;
        private static List<DataItem> filteredDataItemsForDownload = null;

        private static bool refreshOnCreate = false;
        private bool tempHackItemClick = false;
        private static string username = "";
        private bool isPaused = true;
        private View noBrowseView = null;
        private View separator = null;
        public static Stack<Tuple<int, int>> ScrollPositionRestore = new Stack<Tuple<int, int>>(); //indexOfItem, topmargin. for going up/down dirs.
        public static Tuple<int, int> ScrollPositionRestoreRotate = null; //for rotating..

        public static bool FilteredResults = false;
        public static string FilterString = string.Empty;
        public static List<string> WordsToAvoid = new List<string>();
        public static List<string> WordsToInclude = new List<string>();
        public static List<int> SelectedPositionsState = new List<int>(); //this is used for restoring our state.  if its an empty list then thats fine, its just like if we didnt have one..
        public static System.Timers.Timer DebounceTimer = null;
        public static System.Diagnostics.Stopwatch DiagStopWatch = new System.Diagnostics.Stopwatch();
        public static long lastTime = -1;

        public BrowseFragment() : base()
        {
            if (DebounceTimer == null)
            {
                DebounceTimer = new System.Timers.Timer(250);

                DebounceTimer.AutoReset = false;
            }
            DiagStopWatch.Start();
        }

        /// <summary>
        /// For use going down directories
        /// </summary>
        private void SaveScrollPosition()
        {
            try
            {
                int index = listViewDirectories.FirstVisiblePosition;
                View v = listViewDirectories.GetChildAt(0);
                int top = (v == null) ? 0 : (v.Top - listViewDirectories.PaddingTop);
                ScrollPositionRestore.Push(new Tuple<int, int>(index, top));
            }
            catch (Exception e)
            {
                Logger.FirebaseDebug(e.Message + e.StackTrace);
            }
        }

        /// <summary>
        /// For use rotating screen, leaving and coming back, etc...
        /// </summary>
        private void SaveScrollPositionRotate()
        {
            try
            {
                int index = listViewDirectories.FirstVisiblePosition;
                View v = listViewDirectories.GetChildAt(0);
                int top = (v == null) ? 0 : (v.Top - listViewDirectories.PaddingTop);
                ScrollPositionRestoreRotate = new Tuple<int, int>(index, top);
            }
            catch (Exception e)
            {
                Logger.FirebaseDebug(e.Message + e.StackTrace);
            }
        }

        /// <summary>
        /// For use going up directories
        /// </summary>
        private void RestoreScrollPosition()
        {
            try
            {
                Tuple<int, int> pos = ScrollPositionRestore.Pop();
                listViewDirectories.SetSelectionFromTop(pos.Item1, pos.Item2);
            }
            catch (Exception e)
            {
                Logger.FirebaseDebug(e.Message + e.StackTrace);
            }

        }

        private void DebounceTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            UpdateFilteredResponses(); // this is the expensive function...
            SeekerState.MainActivityRef.RunOnUiThread(() =>
            {
                lock (filteredDataItemsForListView)
                {
                    BrowseAdapter customAdapter = new BrowseAdapter(SeekerState.MainActivityRef, filteredDataItemsForListView, this); //enumeration exception (that is, before I added the lock)
                    ListView lv = rootView?.FindViewById<ListView>(Resource.Id.listViewDirectories);
                    if (lv != null)
                    {
                        lv.Adapter = (customAdapter);
                    }
                }
            });
        }

        public override void OnCreateOptionsMenu(IMenu menu, MenuInflater inflater)
        {
            if (IsResponseLoaded())
            {
                inflater.Inflate(Resource.Menu.browse_menu_full, menu);
            }
            else
            {
                inflater.Inflate(Resource.Menu.browse_menu_empty, menu);
            }
            base.OnCreateOptionsMenu(menu, inflater);
        }

        public override void OnPrepareOptionsMenu(IMenu menu)
        {
            int numSelected = (listViewDirectories?.Adapter as BrowseAdapter)?.SelectedPositions?.Count ?? 0;

            CommonHelpers.SetMenuTitles(menu, username);

            if (menu.FindItem(Resource.Id.action_up_directory) != null) //lets just make sure we are using the full menu.  o.w. the menu is empty so these guys dont exist.
            {
                if (numSelected == 0)
                {
                    menu.FindItem(Resource.Id.action_download_selected_files).SetVisible(false);
                    menu.FindItem(Resource.Id.action_queue_selected_paused).SetVisible(false);
                    menu.FindItem(Resource.Id.action_copy_selected_url).SetVisible(false);
                }
                else if (numSelected > 0)
                {
                    menu.FindItem(Resource.Id.action_download_selected_files).SetVisible(true);
                    menu.FindItem(Resource.Id.action_queue_selected_paused).SetVisible(true);
                    menu.FindItem(Resource.Id.action_copy_selected_url).SetVisible(true);
                    if (numSelected > 1)
                    {
                        menu.FindItem(Resource.Id.action_copy_selected_url).SetTitle(Resource.String.CopySelectedURLs);
                    }
                    else
                    {
                        menu.FindItem(Resource.Id.action_copy_selected_url).SetTitle(Resource.String.CopySelectedURL);
                    }
                }
            }


            base.OnPrepareOptionsMenu(menu);
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            if (item.ItemId != Resource.Id.action_browse_user) //special handling (this browse user means browse user dialog).
            {
                if (CommonHelpers.HandleCommonContextMenuActions(item.TitleFormatted.ToString(), username, SeekerState.ActiveActivityRef, null))
                {
                    return true;
                }
            }
            switch (item.ItemId)
            {
                case Resource.Id.action_queue_folder_paused:
                    DownloadUserFilesEntry(true, true);
                    return true;
                case Resource.Id.action_browse_user:
                    ShowEditTextBrowseUserDialog();
                    return true;
                case Resource.Id.action_up_directory:
                    GoUpDirectory();
                    return true;
                case Resource.Id.action_download_files:
                    DownloadUserFilesEntry(false, true);
                    return true;
                case Resource.Id.action_download_selected_files:
                    DownloadSelectedFiles(false);
                    (listViewDirectories.Adapter as BrowseAdapter).SelectedPositions.Clear();
                    ClearAllSelectedPositions();
                    return true;
                case Resource.Id.action_show_folder_info:
                    var folderSummary = GetFolderSummary(dataItemsForListView);
                    ShowFolderSummaryDialog(folderSummary);
                    return true;
                case Resource.Id.action_queue_selected_paused:
                    DownloadSelectedFiles(true);
                    (listViewDirectories.Adapter as BrowseAdapter).SelectedPositions.Clear();
                    ClearAllSelectedPositions();
                    return true;
                case Resource.Id.action_copy_folder_url:
                    string fullDirName = dataItemsForListView[0].Node.Data.Name;
                    string slskLink = CommonHelpers.CreateSlskLink(true, fullDirName, this.currentUsernameUI);
                    CommonHelpers.CopyTextToClipboard(SeekerState.ActiveActivityRef, slskLink);
                    Toast.MakeText(SeekerState.ActiveActivityRef, Resource.String.LinkCopied, ToastLength.Short).Show();
                    return true;
                case Resource.Id.action_copy_selected_url:
                    CopySelectedURLs();
                    (listViewDirectories.Adapter as BrowseAdapter).SelectedPositions.Clear();
                    ClearAllSelectedPositions();
                    return true;
                case Resource.Id.action_add_user:
                    UserListActivity.AddUserAPI(SeekerState.MainActivityRef, username, null);
                    return true;
                case Resource.Id.action_get_user_info:
                    RequestedUserInfoHelper.RequestUserInfoApi(username);
                    return true;
            }
            return base.OnOptionsItemSelected(item);
        }

        public override void SetMenuVisibility(bool menuVisible)
        {
            //this is necessary if programmatically moving to a tab from another activity..
            if (menuVisible)
            {
                var navigator = SeekerState.MainActivityRef?.FindViewById<BottomNavigationView>(Resource.Id.navigation);
                if (navigator != null)
                {
                    navigator.Menu.GetItem(3).SetCheckable(true);
                    navigator.Menu.GetItem(3).SetChecked(true);
                }
            }
            base.SetMenuVisibility(menuVisible);
        }

        public override void OnDestroyView()
        {
            DebounceTimer.Elapsed -= DebounceTimer_Elapsed; //the timer is static...
            base.OnDestroyView();
        }

        //public override void OnDestroy() //this never gets called.
        //{
        //    DebounceTimer.Elapsed -= DebounceTimer_Elapsed; 
        //    base.OnDestroy();
        //}

        /// <summary>
        /// This is used to determine whether we should show the "No browse, to get started" message and whether we should use the browse full or empty.  
        /// I changed it from dataItems!=0 because its too confusing if you browse someone who is sharing an empty directory.
        /// </summary>
        /// <returns></returns>
        public bool IsResponseLoaded()
        {
            return !string.IsNullOrEmpty(username); //(dataItemsForListView.Count != 0);
        }
        public static BrowseFragment Instance = null;
        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            Instance = this;
            this.HasOptionsMenu = true;
            SeekerState.InDarkModeCache = DownloadDialog.InNightMode(this.Context);
            Logger.Debug("BrowseFragmentOnCreateView");
            this.rootView = inflater.Inflate(Resource.Layout.browse, container, false);
            UpdateForScreenSize();
            //this.rootView.FindViewById<Button>(Resource.Id.button2).Click += UpDirectory;
            //this.rootView.FindViewById<Button>(Resource.Id.dlFiles).Click += BrowseFragment_Click;
            //Java.Lang.IllegalStateException: 'The specified child already has a parent. You must call removeView() on the child's parent first.' if third param is not false above...
            //if(!refreshOnCreate)
            //{
            listViewDirectories = this.rootView.FindViewById<ListView>(Resource.Id.listViewDirectories);
            listViewDirectories.ItemClick -= ListViewDirectories_ItemClick; //there may be a change of this not getting attached which would be bad
            listViewDirectories.ItemClick += ListViewDirectories_ItemClick; //there may be a change of this not getting attached which would be bad
            listViewDirectories.ItemLongClick -= ListViewDirectories_ItemLongClick;
            listViewDirectories.ItemLongClick += ListViewDirectories_ItemLongClick;
            this.RegisterForContextMenu(listViewDirectories);
            DebounceTimer.Elapsed += DebounceTimer_Elapsed;

            treePathRecyclerView = this.rootView.FindViewById<RecyclerView>(Resource.Id.recyclerViewHorizontalPath);
            treePathLayoutManager = new LinearLayoutManager(this.Context, LinearLayoutManager.Horizontal, false);
            treePathRecyclerView.SetLayoutManager(treePathLayoutManager);


            //savedInstanceState can be null if first time.
            int[]? selectedPos = savedInstanceState?.GetIntArray("selectedPositions");

            if (FilteredResults)
            {
                //tempHackItemClick = true;
                lock (filteredDataItemsForListView)
                { //on ui thread.
                    currentUsernameUI = CurrentUsername;
                    listViewDirectories.Adapter = new BrowseAdapter(this.Context, filteredDataItemsForListView, this, selectedPos);
                }
            }
            else
            {
                //tempHackItemClick = true;
                lock (dataItemsForListView)
                { //on ui thread.
                    currentUsernameUI = CurrentUsername;
                    listViewDirectories.Adapter = new BrowseAdapter(this.Context, dataItemsForListView, this, selectedPos);
                }
            }

            if (dataItemsForListView.Count != 0)
            {
                pathItems = GetPathItems(dataItemsForListView);
            }

            treePathRecyclerAdapter = new TreePathRecyclerAdapter(pathItems, this);
            treePathRecyclerView.SetAdapter(treePathRecyclerAdapter);

            //}
            this.noBrowseView = this.rootView.FindViewById<TextView>(Resource.Id.noBrowseView);
            this.separator = this.rootView.FindViewById<View>(Resource.Id.recyclerViewHorizontalPathSep);
            this.separator.Visibility = ViewStates.Gone;
            if (FilteredResults || IsResponseLoaded()) // if we are filtering then we already know how it works..
            {
                noBrowseView.Visibility = ViewStates.Gone;
                separator.Visibility = ViewStates.Visible;
            }


            View v = rootView.FindViewById<View>(Resource.Id.relativeLayout1);
            v.Focusable = true;
            //SetFocusable(int) was added in API26. bool was there since API1
            if ((int)Android.OS.Build.VERSION.SdkInt >= 26)
            {
                v.SetFocusable(ViewFocusability.Focusable);
            }
            else
            {
                //v.SetFocusable(true); no bool method in xamarin...
            }

            v.FocusableInTouchMode = true;


            EditText filterText = rootView.FindViewById<EditText>(Resource.Id.filterText);
            filterText.TextChanged += FilterText_TextChanged;
            filterText.FocusChange += FilterText_FocusChange;
            filterText.EditorAction += FilterText_EditorAction;
            filterText.Touch += FilterText_Touch;
            SearchFragment.UpdateDrawableState(filterText, true);

            RelativeLayout rel = rootView.FindViewById<RelativeLayout>(Resource.Id.bottomSheet);
            BottomSheetBehavior bsb = BottomSheetBehavior.From(rel);
            bsb.Hideable = true;
            bsb.PeekHeight = 320;
            bsb.State = BottomSheetBehavior.StateHidden;
            View b = rootView.FindViewById<View>(Resource.Id.bsbutton);
            (b as FloatingActionButton).SetImageResource(Resource.Drawable.ic_filter_list_white_24dp);
            b.Click += B_Click;


            return this.rootView;
        }



        private void FilterText_Touch(object sender, View.TouchEventArgs e)
        {
            EditText editText = sender as EditText;
            e.Handled = false;
            if (e.Event.GetX() >= (editText.Width - editText.TotalPaddingRight))
            {
                if (e.Event.Action == MotionEventActions.Up)
                {
                    //e.Handled = true;
                    editText.Text = string.Empty;
                    SearchFragment.UpdateDrawableState(editText, true);


                    //editText.RequestFocus();
                }
            }
        }

        private void FilterText_FocusChange(object sender, View.FocusChangeEventArgs e)
        {
            try
            {
                SeekerState.MainActivityRef.Window.SetSoftInputMode(SoftInput.AdjustResize);
            }
            catch (System.Exception err)
            {
                Logger.FirebaseDebug("MainActivity_FocusChange" + err.Message);
            }
        }

        public override void OnViewCreated(View view, Bundle savedInstanceState)
        {
            base.OnViewCreated(view, savedInstanceState);

        }



        private void B_Click(object sender, EventArgs e)
        {
            RelativeLayout rel = rootView.FindViewById<RelativeLayout>(Resource.Id.bottomSheet);
            BottomSheetBehavior bsb = BottomSheetBehavior.From(rel);
            if (bsb.State != BottomSheetBehavior.StateExpanded && bsb.State != BottomSheetBehavior.StateCollapsed)
            {
                bsb.State = BottomSheetBehavior.StateExpanded;
            }
            else
            {
                // if the keyboard is up and the edittext is in focus then maybe just put the keyboard down
                // else put the bottom sheet down.  
                // so make it two tiered.
                // or maybe just unset the focus...
                EditText test = rootView.FindViewById<EditText>(Resource.Id.filterText);

                Logger.Debug(this.Resources.Configuration.HardKeyboardHidden.ToString()); //on pixel2 it is YES. on emulator with HW Keyboard = true it is NO

                if (test.IsFocused && (this.Resources.Configuration.HardKeyboardHidden == Android.Content.Res.HardKeyboardHidden.Yes)) //it can still be focused without the keyboard up...
                {
                    try
                    {

                        //SeekerState.MainActivityRef.DispatchKeyEvent(new KeyEvent(new KeyEventActions(),Keycode.Enter));
                        Android.Views.InputMethods.InputMethodManager imm = (Android.Views.InputMethods.InputMethodManager)Context.GetSystemService(Context.InputMethodService);
                        imm.HideSoftInputFromWindow(rootView.WindowToken, 0);
                        test.ClearFocus();
                        rootView.FindViewById<View>(Resource.Id.relativeLayout1).RequestFocus();
                    }
                    catch
                    {
                        //not worth throwing over
                    }
                    return;
                }
                else if (test.IsFocused && (this.Resources.Configuration.HardKeyboardHidden == Android.Content.Res.HardKeyboardHidden.No))
                {

                    //we still want to change focus as otherwise one can still type into it...
                    test.ClearFocus();
                    rootView.FindViewById<View>(Resource.Id.relativeLayout1).RequestFocus();
                    bsb.State = BottomSheetBehavior.StateHidden;

                }
                //test.ClearFocus(); //doesnt do anything. //maybe focus the search text.

                bsb.State = BottomSheetBehavior.StateHidden;
            }
        }


        private void FilterText_EditorAction(object sender, TextView.EditorActionEventArgs e)
        {
            if (e.ActionId == Android.Views.InputMethods.ImeAction.Done || //in this case it is Done (blue checkmark)
                e.ActionId == Android.Views.InputMethods.ImeAction.Go ||
                e.ActionId == Android.Views.InputMethods.ImeAction.Next ||
                e.ActionId == Android.Views.InputMethods.ImeAction.Search)
            {
                Logger.Debug("IME ACTION: " + e.ActionId.ToString());
                rootView.FindViewById<EditText>(Resource.Id.filterText).ClearFocus();
                rootView.FindViewById<View>(Resource.Id.relativeLayout1).RequestFocus();
                //overriding this, the keyboard fails to go down by default for some reason.....
                try
                {
                    Android.Views.InputMethods.InputMethodManager imm = (Android.Views.InputMethods.InputMethodManager)SeekerState.MainActivityRef.GetSystemService(Context.InputMethodService);
                    imm.HideSoftInputFromWindow(rootView.WindowToken, 0);
                }
                catch (System.Exception ex)
                {
                    Logger.FirebaseDebug(ex.Message + " error closing keyboard");
                }
            }
        }


        private void ClearFilter_Click(object sender, EventArgs e)
        {
            //CheckBox filterSticky = rootView.FindViewById<CheckBox>(Resource.Id.stickyFilterCheckbox);
            //filterSticky.Checked = false;
            ClearFilterStringAndCached(true);
        }

        public override void OnSaveInstanceState(Bundle outState)
        {
            outState.PutIntArray("selectedPositions", (this.listViewDirectories?.Adapter as BrowseAdapter)?.SelectedPositions?.ToArray());
            base.OnSaveInstanceState(outState);
        }



        public override void OnViewStateRestored(Bundle savedInstanceState)
        {
            base.OnViewStateRestored(savedInstanceState);
            //the system by default will set the value of filterText to whatever it was last... but we keep track of it using the static so do that instead....
            //cant do this OnViewCreated since filterText.Text will always be empty...
            DebounceTimer.Stop();
            EditText filterText = rootView.FindViewById<EditText>(Resource.Id.filterText);
            filterText.Text = FilterString;  //this will often be empty (which is good) if we got a new response... otherwise (on screen rotate, it will be the same as it otherwise was).
            SearchFragment.UpdateDrawableState(filterText, true);
        }

        private string currentUsernameUI;
        public static EventHandler<EventArgs> BrowseResponseReceivedUI;

        public void BrowseResponseReceivedUI_Handler(object sender, EventArgs args)
        {
            //if the fragment was never created then this.Context will be null
            SeekerState.MainActivityRef.RunOnUiThread(() =>
            {

                lock (dataItemsForListView)
                {
                    if (BrowseFragment.Instance == null || BrowseFragment.Instance.Context == null || BrowseFragment.Instance.rootView == null)
                    {
                        refreshOnCreate = true;
                    }
                    else
                    {
                        BrowseFragment.Instance.RefreshOnRecieved();
                    }
                }
                var pager = (AndroidX.ViewPager.Widget.ViewPager)SeekerState.MainActivityRef?.FindViewById(Resource.Id.pager);
                if (pager != null && pager.CurrentItem == 3)
                {
                    SeekerState.MainActivityRef.SupportActionBar.Title = this.GetString(Resource.String.browse_tab) + ": " + BrowseFragment.CurrentUsername;
                    SeekerState.MainActivityRef.InvalidateOptionsMenu();
                }

            });
        }

        public override void OnResume()
        {
            base.OnResume();
            if (SeekerState.MainActivityRef?.SupportActionBar?.Title != null && !string.IsNullOrEmpty(CurrentUsername)
                && !SeekerState.MainActivityRef.SupportActionBar.Title.EndsWith(": " + CurrentUsername)
                && SeekerState.MainActivityRef.OnBrowseTab())
            {
                SeekerState.MainActivityRef.SupportActionBar.Title = this.GetString(Resource.String.browse_tab) + ": " + BrowseFragment.CurrentUsername;
            }
            BrowseResponseReceivedUI += BrowseResponseReceivedUI_Handler;
            if (currentUsernameUI != CurrentUsername)
            {
                currentUsernameUI = CurrentUsername;
                BrowseResponseReceivedUI_Handler(null, new EventArgs());
            }
            Instance = this;
            if (listViewDirectories != null && ScrollPositionRestoreRotate != null)
            {
                //restore scroll
                listViewDirectories.SetSelectionFromTop(ScrollPositionRestoreRotate.Item1, ScrollPositionRestoreRotate.Item2);
            }
            isPaused = false;
        }

        public override void OnPause()
        {
            BrowseResponseReceivedUI -= BrowseResponseReceivedUI_Handler;
            SaveScrollPositionRotate();
            base.OnPause();
            isPaused = true;
        }


        private void ParseFilterString()
        {
            List<string> filterStringSplit = FilterString.Split(' ').ToList();
            WordsToAvoid.Clear();
            WordsToInclude.Clear();
            //FilterSpecialFlags.Clear();
            foreach (string word in filterStringSplit)
            {
                //if (word.Contains("mbr:") || word.Contains("minbitrate:"))
                //{
                //    FilterSpecialFlags.ContainsSpecialFlags = true;
                //    try
                //    {
                //        FilterSpecialFlags.MinBitRateKBS = Integer.ParseInt(word.Split(':')[1]);
                //    }
                //    catch (System.Exception)
                //    {

                //    }
                //}
                //else if (word.Contains("mfs:") || word.Contains("minfilesize:"))
                //{
                //    FilterSpecialFlags.ContainsSpecialFlags = true;
                //    try
                //    {
                //        FilterSpecialFlags.MinFileSizeMB = (Integer.ParseInt(word.Split(':')[1]));
                //    }
                //    catch (System.Exception)
                //    {

                //    }
                //}
                //else if (word.Contains("mfif:") || word.Contains("minfilesinfolder:"))
                //{
                //    FilterSpecialFlags.ContainsSpecialFlags = true;
                //    try
                //    {
                //        FilterSpecialFlags.MinFoldersInFile = Integer.ParseInt(word.Split(':')[1]);
                //    }
                //    catch (System.Exception)
                //    {

                //    }
                //}
                //else if (word == "isvbr")
                //{
                //    FilterSpecialFlags.ContainsSpecialFlags = true;
                //    FilterSpecialFlags.IsVBR = true;
                //}
                //else if (word == "iscbr")
                //{
                //    FilterSpecialFlags.ContainsSpecialFlags = true;
                //    FilterSpecialFlags.IsCBR = true;
                //}
                if (word.StartsWith('-'))
                {
                    if (word.Length > 1)//if just '-' dont remove everything. just skip it.
                    {
                        WordsToAvoid.Add(word.Substring(1)); //skip the '-'
                    }
                }
                else
                {
                    WordsToInclude.Add(word);
                }
            }
        }


        private void FilterText_TextChanged(object sender, TextChangedEventArgs e)
        {
            string oldFilterString = FilteredResults ? FilterString : string.Empty;
            Logger.Debug("time between typing: " + (DiagStopWatch.ElapsedMilliseconds - lastTime).ToString());
            lastTime = DiagStopWatch.ElapsedMilliseconds;
            if (e.Text != null && e.Text.ToString() != string.Empty && isPaused)
            {
                return;//this is the case where going from search fragment to browse fragment this event gets fired
                //with an old e.text value and so its impossible to autoclear the value.
            }
            
            Logger.Debug("Text Changed: " + e.Text);
            if (e.Text != null && e.Text.ToString() != string.Empty)
            {
                FilteredResults = true;
                FilterString = e.Text.ToString();
                ParseFilterString();

                DebounceTimer.Stop(); //average time bewteen typing is around 150-250 ms (if you know what you are going to type etc).  backspacing (i.e. holding it down) is about 50 ms.
                DebounceTimer.Start();
                //UpdateFilteredResponses(); // this is the expensive function...
                //BrowseAdapter customAdapter = new BrowseAdapter(SeekerState.MainActivityRef, filteredDataItemsForListView);
                //ListView lv = rootView.FindViewById<ListView>(Resource.Id.listViewDirectories);
                //lv.Adapter = (customAdapter);
            }
            else
            {
                DebounceTimer.Stop();
                FilteredResults = false;
                lock (dataItemsForListView) //collection was modified exception here...
                {
                    BrowseAdapter customAdapter = new BrowseAdapter(SeekerState.MainActivityRef, dataItemsForListView, this);
                    ListView lv = rootView.FindViewById<ListView>(Resource.Id.listViewDirectories);
                    lv.Adapter = (customAdapter);
                }
            }

            if (oldFilterString == string.Empty && e.Text.ToString() != string.Empty)
            {
                SearchFragment.UpdateDrawableState(sender as EditText, true);
            }
            else if (oldFilterString != string.Empty && e.Text.ToString() == string.Empty)
            {
                SearchFragment.UpdateDrawableState(sender as EditText, true);
            }
        }

        private bool MatchesCriteriaShallow(DataItem di)
        {
            string fullyQualifiedName = string.Empty;
            if (di.File != null)
            {
                //we are looking at files here...
                fullyQualifiedName = di.Node.Data.Name + di.File.Filename;
            }
            else
            {
                fullyQualifiedName = di.Node.Data.Name;
                //maybe here we should also do children...
            }


            if (WordsToAvoid != null)
            {
                foreach (string avoid in WordsToAvoid)
                {
                    if (fullyQualifiedName.Contains(avoid, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                        //badTerm = true;
                    }
                }
            }
            if (WordsToInclude != null)
            {
                foreach (string include in WordsToInclude)
                {
                    if (!fullyQualifiedName.Contains(include, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                    }
                }
            }
            return true;
        }



        private bool MatchesCriteriaFull(DataItem di)
        {
            string fullyQualifiedName = string.Empty;
            if (di.File != null)
            {
                //we are looking at files here...
                fullyQualifiedName = di.Node.Data.Name + di.File.Filename;
            }
            else
            {
                fullyQualifiedName = di.Node.Data.Name;
                //maybe here we should also do children...
            }


            if (WordsToAvoid != null)
            {
                foreach (string avoid in WordsToAvoid)
                {
                    if (fullyQualifiedName.Contains(avoid, StringComparison.OrdinalIgnoreCase))
                    {
                        return false;
                        //badTerm = true;
                    }
                }
            }
            bool includesAll = true;
            if (WordsToInclude != null)
            {
                foreach (string include in WordsToInclude)
                {
                    if (!fullyQualifiedName.Contains(include, StringComparison.OrdinalIgnoreCase))
                    {
                        includesAll = false;
                    }
                }
            }
            if (includesAll)
            {
                return true;
            }
            else
            {
                //search children for a match.. if there are children.. else we are done..
                if (di.Node.Children.Count == 0 && (di.Directory == null || di.Directory.Files.Count == 0))
                {
                    return false;
                }
                else if (di.File != null)
                {
                    //then we are at the end
                    return false;
                }
                else
                {
                    if (di.Node.Children.Count != 0)
                    {
                        foreach (TreeNode<Directory> child in di.Node.Children)
                        {
                            if (MatchesCriteriaFull(new DataItem(child.Data, child)))
                            {
                                return true;
                            }
                        }
                    }
                    if (di.File == null && di.Directory != null && di.Directory.Files.Count != 0)
                    {
                        foreach (File f in di.Directory.Files)
                        {
                            if (MatchesCriteriaFull(new DataItem(f, di.Node)))
                            {
                                return true;
                            }
                        }
                    }
                    return false;
                }
            }
        }

        /// <summary>
        /// Flattens the tree
        /// </summary>
        /// <param name="di"></param>
        /// <returns></returns>
        public static List<FullFileInfo> GetRecursiveFullFileInfo(DataItem di)
        {
            List<FullFileInfo> listOfFiles = new List<FullFileInfo>();
            AddFiles(listOfFiles, di);
            return listOfFiles;
        }

        public static List<FullFileInfo> GetRecursiveFullFileInfo(List<DataItem> di)
        {
            List<FullFileInfo> listOfFiles = new List<FullFileInfo>();
            foreach (var childNode in di)
            {
                AddFiles(listOfFiles, childNode);
            }
            return listOfFiles;
        }

        private static void AddFiles(List<FullFileInfo> fullFileList, DataItem d)
        {
            if (d.File != null)
            {
                FullFileInfo f = new FullFileInfo();
                f.FullFileName = d.Node.Data.Name + @"\" + d.File.Filename;
                f.Size = d.File.Size;
                f.wasFilenameLatin1Decoded = d.File.IsLatin1Decoded;
                f.wasFolderLatin1Decoded = d.Node.Data.DecodedViaLatin1;
                fullFileList.Add(f);
                return;
            }
            else
            {
                foreach (Soulseek.File slskFile in d.Directory.Files) //files in dir
                {
                    FullFileInfo f = new FullFileInfo();
                    f.FullFileName = d.Node.Data.Name + @"\" + slskFile.Filename;
                    f.Size = slskFile.Size;
                    f.wasFilenameLatin1Decoded = slskFile.IsLatin1Decoded;
                    f.wasFolderLatin1Decoded = d.Node.Data.DecodedViaLatin1;
                    fullFileList.Add(f);
                }
                foreach (var childNode in d.Node.Children) //dirs in dir
                {
                    AddFiles(fullFileList, new DataItem(childNode.Data, childNode));
                }
                return;
            }
        }


        public class FolderSummary
        {
            public int LengthSeconds = 0;
            public long SizeBytes = 0;
            public int NumFiles = 0;
            public int NumSubFolders = 0;
            public void AddFile(Soulseek.File file)
            {
                if (file.Length.HasValue)
                {
                    LengthSeconds += file.Length.Value;
                }
                SizeBytes += file.Size;
                NumFiles++;
            }
        }

        public static FolderSummary GetFolderSummary(DataItem di)
        {
            FolderSummary folderSummary = new FolderSummary();
            SumFiles(folderSummary, di);
            return folderSummary;
        }

        public static FolderSummary GetFolderSummary(List<DataItem> di)
        {
            FolderSummary folderSummary = new FolderSummary();
            foreach (var childNode in di)
            {
                SumFiles(folderSummary, childNode);
            }
            return folderSummary;
        }

        private static void SumFiles(FolderSummary fileSummary, DataItem d)
        {
            if (d.File != null)
            {
                fileSummary.AddFile(d.File);
                return;
            }
            else
            {
                foreach (Soulseek.File slskFile in d.Directory.Files) //files in dir
                {
                    fileSummary.AddFile(slskFile);
                }
                foreach (var childNode in d.Node.Children) //dirs in dir
                {
                    fileSummary.NumSubFolders++;
                    SumFiles(fileSummary, new DataItem(childNode.Data, childNode));
                }
                return;
            }
        }




        private List<DataItem> FilterBrowseList(List<DataItem> unfiltered)
        {
            System.Diagnostics.Stopwatch s = new System.Diagnostics.Stopwatch();
            s.Start();

            List<DataItem> filtered = new List<DataItem>();
            foreach (DataItem di in unfiltered)
            {
                if (MatchesCriteriaFull(di)) //change back to shallow...
                {
                    filtered.Add(di);
                }
            }
            s.Stop();
            Logger.Debug("total dir in tree: " + diagnostics_count + " total time: " + s.ElapsedMilliseconds);
            return filtered;
        }

        /// <summary>
        /// basically if the current filter is more restrictive, then we dont have to filter everything again, we can just filter the existing filtered data more.
        /// </summary>
        /// <param name="currentSearch"></param>
        /// <param name="previousSearch"></param>
        /// <returns></returns>
        /// <remarks>
        /// examples: 
        /// bool yes = IsCurrentSearchMoreRestrictive("hello how are you -ex","hello how are yo -excludeTerms");
        /// bool yes = IsCurrentSearchMoreRestrictive("hello how are you -excludeTerms", "hello how are yo -excludeTerms");
        /// bool no = IsCurrentSearchMoreRestrictive("hello how are you -excludeTerms", "hello how are yo -excludeTerms -a");
        /// bool no = IsCurrentSearchMoreRestrictive("hello how are you -excludeTerms -b -c", "hello how are yo -excludeTerms -a");
        /// bool yes = IsCurrentSearchMoreRestrictive("hello how are you -excludeTerms -a -b -c", "hello how are yo -excludeTerms -a");
        /// bool yes = IsCurrentSearchMoreRestrictive("hello how are you -excludeTerms -", "hello how are yo -excludeTerms");
        /// bool yes = IsCurrentSearchMoreRestrictive("hello how are you -excludeTerms -a", "hello how are yo -excludeTerms -");
        /// bool no = IsCurrentSearchMoreRestrictive("hello how are you -excludeTerms -aa", "hello how are yo -excludeTerms -a");
        /// bool no = IsCurrentSearchMoreRestrictive("hell", "hello");
        /// bool yes = IsCurrentSearchMoreRestrictive("hello", "hell");
        /// </remarks>
        private static bool IsCurrentSearchMoreRestrictive(string currentSearch, string previousSearch)
        {
            var currentWords = currentSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var previousWords = previousSearch.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var currentExcludeWords = currentWords.Where(s => s.StartsWith("-") && s.Length != 1);
            var previousExcludeWords = previousWords.Where(s => s.StartsWith("-") && s.Length != 1);
            var currentIncludeWords = currentWords.Where(s => !s.StartsWith("-"));
            var previousIncludeWords = previousWords.Where(s => !s.StartsWith("-"));
            string currentIncludeString = string.Join(' ', currentIncludeWords).Trim();
            string previousIncludeString = string.Join(' ', previousIncludeWords).Trim();

            if (currentExcludeWords.Count() < previousExcludeWords.Count())
            {
                //current is not necessarily more restrictive as it excludes less.
                return false;
            }

            //and for each previous exlusion, the current exlusions are just as bad.
            for (int i = 0; i < previousExcludeWords.Count(); i++)
            {
                if (!previousExcludeWords.ElementAt(i).Contains(currentExcludeWords.ElementAt(i)))
                {
                    return false;
                }
            }

            return currentIncludeString.Contains(previousIncludeString);
        }

        private void UpdateFilteredResponses()
        {
            lock (filteredDataItemsForListView)
            {
                filteredDataItemsForListView.Clear();
                if (cachedFilteredDataItemsForListView != null && IsCurrentSearchMoreRestrictive(FilterString, cachedFilteredDataItemsForListView.Item1))//is less restrictive than the current search)
                {
                    var test = FilterBrowseList(cachedFilteredDataItemsForListView.Item2);
                    filteredDataItemsForListView.AddRange(test);
                }
                else
                {
                    var test = FilterBrowseList(dataItemsForListView);
                    filteredDataItemsForListView.AddRange(test);
                }
                cachedFilteredDataItemsForListView = new Tuple<string, List<DataItem>>(FilterString, filteredDataItemsForListView.ToList());
            }
        }

        private void UpdateForScreenSize()
        {
            if (!SeekerState.IsLowDpi()) return;
            try
            {
                //this.rootView.FindViewById<TextView>(Resource.Id.browseQueue).SetTextSize(ComplexUnitType.Dip, 8);
                //this.rootView.FindViewById<TextView>(Resource.Id.browseKbs).SetTextSize(ComplexUnitType.Dip, 8);
            }
            catch
            {
                //not worth throwing over
            }
        }

        private void CopySelectedURLs()
        {
            if ((!FilteredResults && dataItemsForListView.Count == 0) || (FilteredResults && filteredDataItemsForListView.Count == 0))
            {
                Toast.MakeText(this.Context, Resource.String.NothingToCopy, ToastLength.Long).Show();
            }
            else if ((listViewDirectories.Adapter as BrowseAdapter).SelectedPositions.Count == 0)
            {
                Toast.MakeText(this.Context, Resource.String.nothing_selected, ToastLength.Long).Show();
            }
            else
            {
                List<FullFileInfo> slskFile = new List<FullFileInfo>();
                if (FilteredResults)
                {
                    lock (filteredDataItemsForListView)
                    {
                        for (int i = 0; i < filteredDataItemsForListView.Count; i++)
                        {
                            if ((listViewDirectories.Adapter as BrowseAdapter).SelectedPositions.Contains(i))
                            {
                                DataItem d = filteredDataItemsForListView[i];
                                FullFileInfo f = new FullFileInfo();
                                f.wasFilenameLatin1Decoded = d.File.IsLatin1Decoded;
                                f.wasFolderLatin1Decoded = d.Node.Data.DecodedViaLatin1;
                                f.FullFileName = d.Node.Data.Name + @"\" + d.File.Filename;
                                f.Size = d.File.Size;
                                slskFile.Add(f);
                            }
                        }
                    }
                }
                else
                {
                    //List<Soulseek.File> slskFile = new List<File>();
                    //List<UserFilename> = new List<UserFilename>();

                    lock (dataItemsForListView)
                    {
                        for (int i = 0; i < dataItemsForListView.Count; i++)
                        {
                            if ((listViewDirectories.Adapter as BrowseAdapter).SelectedPositions.Contains(i))
                            {
                                DataItem d = dataItemsForListView[i];
                                FullFileInfo f = new FullFileInfo();
                                f.FullFileName = d.Node.Data.Name + @"\" + d.File.Filename;
                                f.Size = d.File.Size;
                                f.wasFilenameLatin1Decoded = d.File.IsLatin1Decoded;
                                f.wasFolderLatin1Decoded = d.Node.Data.DecodedViaLatin1;
                                slskFile.Add(f);
                            }
                        }
                    }
                }


                string linkToCopy = string.Empty;
                foreach (FullFileInfo ffi in slskFile)
                {
                    //there is something before us
                    if (linkToCopy != string.Empty)
                    {
                        linkToCopy = linkToCopy + " \n";
                    }
                    linkToCopy = linkToCopy + CommonHelpers.CreateSlskLink(false, ffi.FullFileName, this.currentUsernameUI);
                }
                CommonHelpers.CopyTextToClipboard(SeekerState.ActiveActivityRef, linkToCopy);
                if ((listViewDirectories.Adapter as BrowseAdapter).SelectedPositions.Count > 1)
                {
                    Toast.MakeText(this.Context, Resource.String.LinksCopied, ToastLength.Short).Show();
                }
                else
                {
                    Toast.MakeText(this.Context, Resource.String.LinkCopied, ToastLength.Short).Show();
                }
            }


        }

        private void DownloadSelectedFiles(bool queuePaused)
        {
            if ((!FilteredResults && dataItemsForListView.Count == 0) || (FilteredResults && filteredDataItemsForListView.Count == 0))
            {
                Toast.MakeText(this.Context, this.Resources.GetString(Resource.String.nothing_to_download), ToastLength.Long).Show();
            }
            else if ((listViewDirectories.Adapter as BrowseAdapter).SelectedPositions.Count == 0)
            {
                Toast.MakeText(this.Context, this.Resources.GetString(Resource.String.nothing_selected), ToastLength.Long).Show();
            }
            else
            {
                List<FullFileInfo> slskFile = new List<FullFileInfo>();
                if (FilteredResults)
                {
                    lock (filteredDataItemsForListView)
                    {
                        for (int i = 0; i < filteredDataItemsForListView.Count; i++)
                        {
                            if ((listViewDirectories.Adapter as BrowseAdapter).SelectedPositions.Contains(i))
                            {
                                DataItem d = filteredDataItemsForListView[i];
                                FullFileInfo f = new FullFileInfo();
                                f.FullFileName = d.Node.Data.Name + @"\" + d.File.Filename;
                                f.Size = d.File.Size;
                                f.wasFilenameLatin1Decoded = d.File.IsLatin1Decoded;
                                f.wasFolderLatin1Decoded = d.Node.Data.DecodedViaLatin1;
                                slskFile.Add(f);
                            }
                        }
                    }
                }
                else
                {
                    //List<Soulseek.File> slskFile = new List<File>();
                    //List<UserFilename> = new List<UserFilename>();

                    lock (dataItemsForListView)
                    {
                        for (int i = 0; i < dataItemsForListView.Count; i++)
                        {
                            if ((listViewDirectories.Adapter as BrowseAdapter).SelectedPositions.Contains(i))
                            {
                                DataItem d = dataItemsForListView[i];
                                FullFileInfo f = new FullFileInfo();
                                f.FullFileName = d.Node.Data.Name + @"\" + d.File.Filename;
                                f.Size = d.File.Size;
                                f.wasFilenameLatin1Decoded = d.File.IsLatin1Decoded;
                                f.wasFolderLatin1Decoded = d.Node.Data.DecodedViaLatin1;
                                slskFile.Add(f);
                            }
                        }
                    }
                }
                if (SeekerState.CurrentlyLoggedInButDisconnectedState())
                {
                    //we disconnected. login then do the rest.
                    //this is due to temp lost connection
                    Task t;
                    if (!SoulseekConnection.ShowMessageAndCreateReconnectTask(this.Context, false, out t))
                    {
                        return;
                    }

                    t.ContinueWith(t =>
                    {
                        if (t.IsFaulted)
                        {
                            var stringId = ResourceConstant.String.failed_to_connect;
                            SeekerApplication.ApplicationContext.ShowShortToast(stringId);
                            return;
                        }
                        
                        TransfersUtil.CreateDownloadAllTask(slskFile.ToArray(), queuePaused, username).Start();
                    });
                }
                else
                {
                    TransfersUtil.CreateDownloadAllTask(slskFile.ToArray(), queuePaused, username).Start();
                }
            }
        }

        private void DownloadUserFilesEntryStage3(bool downloadSubfolders, List<FullFileInfo> recusiveFullFileInfo, List<FullFileInfo> topLevelFullFileInfoOnly, bool queuePaused)
        {
            if (downloadSubfolders)
            {
                if (recusiveFullFileInfo.Count == 0) //this is possible if they have a tree of folders with no files in them at all.  which would be rare but possible.
                {
                    Toast.MakeText(SeekerState.ActiveActivityRef, this.Resources.GetString(Resource.String.nothing_to_download), ToastLength.Long).Show();
                    return;
                }
                DownloadListOfFiles(recusiveFullFileInfo, queuePaused, username);
            }
            else
            {
                if (topLevelFullFileInfoOnly.Count == 0)
                {
                    Toast.MakeText(SeekerState.ActiveActivityRef, this.Resources.GetString(Resource.String.nothing_to_download), ToastLength.Long).Show();
                    return;
                }
                DownloadListOfFiles(topLevelFullFileInfoOnly, queuePaused, username);
            }
        }

        private void DownloadUserFilesEntryStage2(bool justFilteredItems, bool queuePaused)
        {
            if (justFilteredItems && filteredDataItemsForDownload.Count == 0)
            {
                Toast.MakeText(SeekerState.ActiveActivityRef, this.Resources.GetString(Resource.String.nothing_to_download), ToastLength.Long).Show();
                return;
            }

            bool containsSubDirs = false;
            int totalItems = -1; //this one we set.
            int toplevelItems = 0; //this one we increment.
            List<FullFileInfo> recusiveFullFileInfo = new List<FullFileInfo>();
            List<FullFileInfo> topLevelFullFileInfoOnly = new List<FullFileInfo>();
            if (!justFilteredItems)
            {
                lock (dataItemsForDownload)
                {
                    foreach (DataItem d in dataItemsForDownload)
                    {
                        if (d.IsDirectory())
                        {
                            containsSubDirs = true;
                        }
                        else
                        {
                            FullFileInfo f = new FullFileInfo();
                            f.FullFileName = d.Node.Data.Name + @"\" + d.File.Filename;
                            f.Size = d.File.Size;
                            f.wasFilenameLatin1Decoded = d.File.IsLatin1Decoded;
                            f.wasFolderLatin1Decoded = d.Node.Data.DecodedViaLatin1;
                            topLevelFullFileInfoOnly.Add(f);
                            toplevelItems++;
                        }
                    }
                }
                if (containsSubDirs)
                {
                    recusiveFullFileInfo = GetRecursiveFullFileInfo(dataItemsForDownload);
                    totalItems = recusiveFullFileInfo.Count;
                }
            }
            else
            {
                lock (filteredDataItemsForDownload)
                {
                    foreach (DataItem d in filteredDataItemsForDownload)
                    {
                        if (d.IsDirectory())
                        {
                            containsSubDirs = true;
                        }
                        else
                        {
                            FullFileInfo f = new FullFileInfo();
                            f.FullFileName = d.Node.Data.Name + @"\" + d.File.Filename;
                            f.Size = d.File.Size;
                            f.wasFilenameLatin1Decoded = d.File.IsLatin1Decoded;
                            f.wasFolderLatin1Decoded = d.Node.Data.DecodedViaLatin1;
                            topLevelFullFileInfoOnly.Add(f);
                            toplevelItems++;
                        }
                    }
                }
                if (containsSubDirs)
                {
                    recusiveFullFileInfo = GetRecursiveFullFileInfo(filteredDataItemsForDownload);
                    totalItems = recusiveFullFileInfo.Count;
                }
            }

            //show message with total num of files...
            if (containsSubDirs)
            {
                //this is Android.  There are no WinForm style blocking modal dialogs.  Show() is not synchronous.  It will not block or wait for a response.
                var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(SeekerState.ActiveActivityRef, Resource.Style.MyAlertDialogTheme);
                builder.SetTitle(Resource.String.ThisFolderContainsSubfolders);

                string topLevelStr = string.Empty;
                if (toplevelItems == 1)
                {
                    topLevelStr = string.Format(SeekerApplication.ApplicationContext.GetString(Resource.String.item_total_singular), toplevelItems);
                }
                else
                {
                    topLevelStr = string.Format(SeekerApplication.ApplicationContext.GetString(Resource.String.item_total_plural), toplevelItems);
                }
                string recursiveStr = string.Empty;
                if (totalItems == 1)
                {
                    recursiveStr = string.Format(SeekerApplication.ApplicationContext.GetString(Resource.String.item_total_singular), totalItems);
                }
                else
                {
                    recursiveStr = string.Format(SeekerApplication.ApplicationContext.GetString(Resource.String.item_total_plural), totalItems);
                }

                if (queuePaused)
                {
                    builder.SetMessage(string.Format(SeekerApplication.ApplicationContext.GetString(Resource.String.subfolders_warning_queue_paused), recursiveStr, topLevelStr));
                }
                else
                {
                    builder.SetMessage(string.Format(SeekerApplication.ApplicationContext.GetString(Resource.String.subfolders_warning), recursiveStr, topLevelStr));
                }
                EventHandler<DialogClickEventArgs> eventHandlerCurrentFolder = new EventHandler<DialogClickEventArgs>((object sender, DialogClickEventArgs okayArgs) =>
                {
                    DownloadUserFilesEntryStage3(false, recusiveFullFileInfo, topLevelFullFileInfoOnly, queuePaused);
                });
                EventHandler<DialogClickEventArgs> eventHandlerRecursiveFolders = new EventHandler<DialogClickEventArgs>((object sender, DialogClickEventArgs okayArgs) =>
                {
                    if (containsSubDirs)
                    {
                        if (!justFilteredItems)
                        {
                            TagDepths(dataItemsForDownload.First(), recusiveFullFileInfo);
                        }
                        else
                        {
                            TagDepths(filteredDataItemsForDownload.First(), recusiveFullFileInfo);
                        }
                    }
                    DownloadUserFilesEntryStage3(true, recusiveFullFileInfo, topLevelFullFileInfoOnly, queuePaused);
                });
                builder.SetPositiveButton(Resource.String.all, eventHandlerRecursiveFolders);
                builder.SetNegativeButton(Resource.String.current_folder_only, eventHandlerCurrentFolder);
                builder.Show();
            }
            else
            {
                DownloadUserFilesEntryStage3(false, recusiveFullFileInfo, topLevelFullFileInfoOnly, queuePaused);
            }
        }

        private static void TagDepths(DataItem d, List<FullFileInfo> recursiveFileInfo)
        {
            int lowestLevel = GetLevel(d.Node.Data.Name);
            foreach (FullFileInfo fullFileInfo in recursiveFileInfo)
            {
                int level = GetLevel(fullFileInfo.FullFileName);
                int depth = level - lowestLevel + 1;
#if DEBUG
                if (depth == 0)
                {
                    throw new Exception("depth is 0");
                }
#endif
                fullFileInfo.Depth = depth;
            }

        }

        //private static int GetMaxDepth(DataItem d, List<FullFileInfo> recursiveFileInfo)
        //{
        //    int lowestLevel = GetLevel(d.Node.Data.Name);
        //    int highestLevel = int.MinValue;
        //    foreach(FullFileInfo fullFileInfo in recursiveFileInfo)
        //    {
        //        int level = GetLevel(fullFileInfo.FullFileName);
        //        if(level > highestLevel)
        //        {
        //            highestLevel = level;
        //        }
        //    }
        //    return highestLevel - lowestLevel + 1;
        //}

        private static int GetLevel(string fileName)
        {
            int count = 0;
            foreach (char c in fileName)
            {
                if (c == '\\')
                {
                    count++;
                }
            }
            return count;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="queuePaused"></param>
        /// <param name="downloadShownInListView">True if to select everything currently shown in the listview.  False if the user is selecting a single folder.</param>
        /// <param name="positionOfFolderToDownload"></param>
        private void DownloadUserFilesEntry(bool queuePaused, bool downloadShownInListView, int positionOfFolderToDownload = -1)
        {
            if (downloadShownInListView)
            {
                dataItemsForDownload = dataItemsForListView.ToList();
                filteredDataItemsForDownload = filteredDataItemsForListView.ToList();
            }
            else
            {
                //put the contents of the selected folder into the dataItemsToDownload and then do the functions as normal.
                dataItemsForDownload = new List<DataItem>();
                DataItem itemSelected = GetItemSelected(positionOfFolderToDownload, FilteredResults);
                if (itemSelected == null)
                {
                    CommonHelpers.ShowReportErrorDialog(SeekerState.ActiveActivityRef, "Browse User File Selection Issue");
                    return; //else nullref
                }
                PopulateDataItemsToItemSelected(dataItemsForDownload, itemSelected);
                filteredDataItemsForDownload = FilterBrowseList(dataItemsForDownload.ToList());
            }


            if (dataItemsForDownload.Count == 0)
            {
                Toast.MakeText(SeekerState.ActiveActivityRef, this.Resources.GetString(Resource.String.nothing_to_download), ToastLength.Long).Show();
                return;
            }
            if (FilteredResults && (dataItemsForDownload.Count != filteredDataItemsForDownload.Count))
            {
                //this is Android.  There are no WinForm style blocking modal dialogs.  Show() is not synchronous.  It will not block or wait for a response.
                var b = new AndroidX.AppCompat.App.AlertDialog.Builder(SeekerState.ActiveActivityRef, Resource.Style.MyAlertDialogTheme);
                b.SetTitle(Resource.String.filter_is_on);
                b.SetMessage(Resource.String.filter_is_on_body);
                EventHandler<DialogClickEventArgs> eventHandlerAll = new EventHandler<DialogClickEventArgs>((object sender, DialogClickEventArgs okayArgs) =>
                {
                    DownloadUserFilesEntryStage2(false, queuePaused);
                });
                EventHandler<DialogClickEventArgs> eventHandlerFiltered = new EventHandler<DialogClickEventArgs>((object sender, DialogClickEventArgs okayArgs) =>
                {
                    DownloadUserFilesEntryStage2(true, queuePaused);
                });
                b.SetPositiveButton(Resource.String.just_filtered, eventHandlerFiltered);
                b.SetNegativeButton(Resource.String.all, eventHandlerAll);
                b.Show();
            }
            else
            {
                DownloadUserFilesEntryStage2(false, queuePaused);
            }


        }

        /// <summary>
        /// Safe entrypoint for anyone to call.
        /// </summary>
        /// <param name="slskFiles"></param>
        /// <param name="queuePaused"></param>
        /// <param name="_username"></param>
        public static void DownloadListOfFiles(List<FullFileInfo> slskFiles, bool queuePaused, string _username)
        {
            if (SeekerState.CurrentlyLoggedInButDisconnectedState())
            {
                //we disconnected. login then do the rest.
                //this is due to temp lost connection
                Task t;
                if (!SoulseekConnection.ShowMessageAndCreateReconnectTask(SeekerState.ActiveActivityRef, false, out t))
                {
                    return;
                }

                t.ContinueWith(new Action<Task>((Task t) =>
                {
                    if (t.IsFaulted)
                    {
                        SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                        {
                            //fragment.Context returns null if the fragment has not been attached, or if it got detached. (detach and attach happens on screen rotate).
                            //so best to use SeekerState.MainActivityRef which is static and so not null after MainActivity.OnCreate

                            Toast.MakeText(SeekerState.ActiveActivityRef, SeekerState.ActiveActivityRef.Resources.GetString(Resource.String.failed_to_connect), ToastLength.Short).Show();

                        });
                        return;
                    }
                    TransfersUtil.CreateDownloadAllTask(slskFiles.ToArray(), queuePaused, _username).Start();
                }));
            }
            else
            {
                TransfersUtil.CreateDownloadAllTask(slskFiles.ToArray(), queuePaused, _username).Start();
            }
        }

        private void UpDirectory(object sender, System.EventArgs e)
        {
            GoUpDirectory();
        }

        private DataItem GetItemSelected(int position, bool filteredResults)
        {
            DataItem itemSelected = null;
            if (filteredResults)
            {
                try
                {
                    itemSelected = filteredDataItemsForListView[position];
                }
                catch (IndexOutOfRangeException) //this did happen to me.... when filtering...
                {
                    string logMsg = $"ListViewDirectories_ItemClick position: {position} filteredDataItemsForListView.Count: {filteredDataItemsForListView.Count}";
                    Logger.FirebaseDebug(logMsg);
                    return null;
                }
            }
            else
            {
                //!! firebase -- OnContextItemSelected --- DownloadUserFilesEntry
                //!! firebase -- ListViewDirectories_ItemClick
                try
                {
                    itemSelected = dataItemsForListView[position]; //out of bounds here...
                }
                catch (IndexOutOfRangeException)
                {
                    string logMsg = $"ListViewDirectories_ItemClick position: {position} filteredDataItemsForListView.Count: {filteredDataItemsForListView.Count} isFilter {filteredResults}";
                    Logger.FirebaseDebug(logMsg);
                    return null;
                }
            }
            return itemSelected;
        }
        public static int ItemPositionLongClicked = -1;
        private void ListViewDirectories_ItemLongClick(object sender, AdapterView.ItemLongClickEventArgs e)
        {
            bool filteredResults = FilteredResults;
            DataItem itemSelected = GetItemSelected(e.Position, filteredResults);
            if (itemSelected == null)
            {
                return;
            }
            if (itemSelected.IsDirectory())
            {
                e.Handled = true;
                //show options
                //SeekerState.ActiveActivityRef.Open
                //this.RegisterForContextMenu(e.View);
                ItemPositionLongClicked = e.Position;
                Logger.FirebaseInfo($"{nameof(ItemPositionLongClicked)} {ItemPositionLongClicked}");
                this.listViewDirectories.ShowContextMenu();
                //BrowseFragment.Instance.Context

                //e.Handled = true;
                //e.View.ShowContextMenu(); //causes stack overflow...
                //this.UnregisterForContextMenu(e.View);

            }
            else
            {
                //no special long click event if file.
                this.ListViewDirectories_ItemClick(sender, new AdapterView.ItemClickEventArgs(e.Parent, e.View, e.Position, e.Id));
            }
        }

        private void PopulateDataItemsToItemSelected(List<DataItem> dirs, DataItem itemSelected)
        {
            dirs.Clear();
            if (itemSelected.Node.Children.Count != 0) //then more directories
            {
                foreach (TreeNode<Directory> d in itemSelected.Node.Children)
                {
                    dirs.Add(new DataItem(d.Data, d));
                }
                //here we do files as well......
                if (itemSelected.Directory != null && itemSelected.Directory.FileCount != 0)
                {
                    foreach (File f in itemSelected.Directory.OrderedFiles)
                    {
                        dirs.Add(new DataItem(f, itemSelected.Node));
                    }
                }
            }
            else
            {
                foreach (File f in itemSelected.Directory.OrderedFiles)
                {
                    dirs.Add(new DataItem(f, itemSelected.Node));
                }
            }
        }

        private void ListViewDirectories_ItemClick(object sender, AdapterView.ItemClickEventArgs e)
        {
            cachedFilteredDataItemsForListView = null;
            bool filteredResults = FilteredResults;
            DataItem itemSelected = GetItemSelected(e.Position, filteredResults);
            if (itemSelected == null)
            {
                return;
            }

            bool isFile = false;
            lock (dataItemsForListView)
            {
                //DataItem itemSelected = dataItemsForListView[e.Position];
                if (itemSelected.IsDirectory())
                {
                    if (itemSelected.Node.Children.Count == 0 && (itemSelected.Directory == null || itemSelected.Directory.FileCount == 0))
                    {
                        //dont let them do this... if this happens then there is no way to get back up...
                        Toast.MakeText(SeekerState.MainActivityRef, this.Resources.GetString(Resource.String.directory_is_empty), ToastLength.Short).Show();
                        return;
                    }
                    SaveScrollPosition();

                    PopulateDataItemsToItemSelected(dataItemsForListView, itemSelected);

                    if (!filteredResults)
                    {
                        SetBrowseAdapters(filteredResults, dataItemsForListView, false, false);
                        //listViewDirectories.Adapter = new BrowseAdapter(this.Context, dataItemsForListView, this);
                    }
                }
                else
                {
                    isFile = true;


                    bool alreadySelected = (this.listViewDirectories.Adapter as BrowseAdapter).SelectedPositions.Contains<int>(e.Position);
                    if (!alreadySelected)
                    {
                        (e.View as BrowseResponseItemView).SetSelectedBackground(true);
                        (this.listViewDirectories.Adapter as BrowseAdapter).SelectedPositions.Add(e.Position);
                    }
                    else
                    {
                        (e.View as BrowseResponseItemView).SetSelectedBackground(false);
                        (this.listViewDirectories.Adapter as BrowseAdapter).SelectedPositions.Remove(e.Position);
                    }

                }

            }
            lock (dataItemsForListView)
            {
                lock (filteredDataItemsForListView)
                {
                    if (!isFile && filteredResults)
                    {
                        SetBrowseAdapters(filteredResults, dataItemsForListView, false, false);
                    }
                }
            }
        }

        private void ClearAllSelectedPositions()
        {
            //nullref crash was here.. not worth crashing over...
            if (listViewDirectories == null)
            {
                return;
            }
            for (int i = 0; i < listViewDirectories.Count; i++)
            {
                View v = listViewDirectories.GetChildAt(i);
                if (v != null)
                {
                    listViewDirectories.GetChildAt(i).Background = null;
                }
            }
        }

        public bool BackButton()
        {
            return GoUpDirectory();
        }

        /// <summary>
        /// 
        /// </summary>
        /// <returns>whether we can successfully go up.</returns>
        public bool GoUpDirectory(int additionalLevels = 0)
        {
            cachedFilteredDataItemsForListView = null;
            bool filteredResults = FilteredResults;
            lock (dataItemsForListView)
            {
                TreeNode<Directory> item = null;
                try
                {
                    //var testItem = dataItemsForListView[0]?.Node;
                    if (dataItemsForListView[0].File != null)
                    {
                        item = dataItemsForListView[0]?.Node?.Parent;  //?.Parent; //This used to do grandparent.  Which is a bug I think, so I changed it.
                    }
                    else if (dataItemsForListView[0].Directory != null)
                    {
                        item = dataItemsForListView[0]?.Node?.Parent?.Parent;
                    }
                    else
                    {
                        return false;
                    }
                }
                catch
                {
                    return false; //bad... 
                }
                if (item == null)
                {
                    return false; //we must be at or near the highest
                }
                for (int i = 0; i < additionalLevels; i++)
                {
                    item = item.Parent;
                }
                dataItemsForListView.Clear();

                foreach (TreeNode<Directory> d in item.Children) //nullref TODO TODO
                {
                    dataItemsForListView.Add(new DataItem(d.Data, d));
                }

                //here we do files as well......
                if (item.Data != null && item.Data.FileCount != 0)
                {
                    foreach (File f in item.Data.OrderedFiles)
                    {
                        dataItemsForListView.Add(new DataItem(f, item));
                    }
                }
                if (!filteredResults)
                {
                    SetBrowseAdapters(filteredResults, dataItemsForListView, false, true);
                    //listViewDirectories.Adapter = new BrowseAdapter(this.Context, dataItemsForListView, this);
                }

            }
            lock (dataItemsForListView)
            {
                lock (filteredDataItemsForListView)
                {
                    SetBrowseAdapters(filteredResults, dataItemsForListView, false, true);
                    //if (filteredResults)
                    //{
                    //    filteredDataItemsForListView = FilterBrowseList(dataItemsForListView);
                    //    listViewDirectories.Adapter = new BrowseAdapter(this.Context, filteredDataItemsForListView, this);
                    //}
                }
            }
            RestoreScrollPosition();

            return true;
        }

        /// <summary>
        /// Sets both the main and the Path Items adapters.  necessary when first loading or when going up or down directories (i.e. if path changes).  not necessary if just changing the filter.
        /// </summary>
        /// <param name="toFilter"></param>
        /// <param name="nonFilteredItems"></param>
        public void SetBrowseAdapters(bool toFilter, List<DataItem> nonFilteredItems, bool fullRefreshOfPathItems, bool goingUp = false)
        {
            if (toFilter)
            {
                filteredDataItemsForListView = FilterBrowseList(dataItemsForListView);
                listViewDirectories.Adapter = new BrowseAdapter(this.Context, filteredDataItemsForListView, this);
            }
            else
            {
                listViewDirectories.Adapter = new BrowseAdapter(this.Context, dataItemsForListView, this);
            }

            var items = GetPathItems(dataItemsForListView);
            pathItems.Clear();
            pathItems.AddRange(items);
            if (fullRefreshOfPathItems)
            {
                treePathRecyclerAdapter.NotifyDataSetChanged();
            }
            else if (goingUp)
            {
                treePathRecyclerAdapter.NotifyDataSetChanged(); //removed individual updates due to weird graphical glitches...
                //treePathRecyclerAdapter.NotifyItemChanged(pathItems.Count-1); //since now the last node (remove separator)
                //treePathRecyclerAdapter.NotifyItemRemoved(pathItems.Count);
            }
            else
            {
                treePathRecyclerAdapter.NotifyDataSetChanged();
                //treePathRecyclerAdapter.NotifyItemChanged(pathItems.Count - 2); //since now no longer the last node (add separator)
                //treePathRecyclerAdapter.NotifyItemInserted(pathItems.Count - 1);
                treePathRecyclerView.ScrollToPosition(pathItems.Count - 1);
            }
            //List<PathItem> pathItems = GetPathItems(dataItemsForListView);

        }



        public static List<PathItem> GetPathItems(List<DataItem> nonFilteredDataItemsForListView)
        {
            if (nonFilteredDataItemsForListView.Count == 0)
            {
                return new List<PathItem>();
            }
            List<PathItem> pathItemsList = new List<PathItem>();
            if (nonFilteredDataItemsForListView[0].IsDirectory())
            {
                GetPathItemsInternal(pathItemsList, nonFilteredDataItemsForListView[0].Node.Parent, true);
            }
            else
            {
                GetPathItemsInternal(pathItemsList, nonFilteredDataItemsForListView[0].Node, true);
            }
            pathItemsList.Reverse();
            FixNullRootDisplayName(pathItemsList);
            return pathItemsList;
        }

        private static void FixNullRootDisplayName(List<PathItem> pathItemsList)
        {
            if (pathItemsList.Count > 0 && pathItemsList[0].DisplayName == string.Empty)
            {
                pathItemsList[0].DisplayName = "root";
            }
        }

        private static void GetPathItemsInternal(List<PathItem> pathItems, TreeNode<Directory> treeNode, bool lastChild)
        {
            string displayName = CommonHelpers.GetFileNameFromFile(treeNode.Data.Name);
            pathItems.Add(new PathItem(displayName, lastChild));
            if (treeNode.Parent == null)
            {
                return;
            }
            else
            {
                GetPathItemsInternal(pathItems, treeNode.Parent, false);
            }
        }
        //https://stackoverflow.com/questions/5297842/how-to-handle-oncontextitemselected-in-a-multi-fragment-activity
        //onContextItemSelected() is called for all currently existing fragments starting with the first added one.
        public const int UNIQUE_BROWSE_GROUP_ID = 304;
        public override void OnCreateContextMenu(IContextMenu menu, View v, IContextMenuContextMenuInfo menuInfo)
        {
            menu.Add(UNIQUE_BROWSE_GROUP_ID, 0, 0, Resource.String.download_folder);
            menu.Add(UNIQUE_BROWSE_GROUP_ID, 1, 1, Resource.String.QueueFolderAsPaused);
            menu.Add(UNIQUE_BROWSE_GROUP_ID, 2, 2, Resource.String.ShowFolderInfo);
            menu.Add(UNIQUE_BROWSE_GROUP_ID, 3, 3, Resource.String.CopyURL);
            base.OnCreateContextMenu(menu, v, menuInfo);
        }

        public override bool OnContextItemSelected(IMenuItem item)
        {
            if (item.GroupId == UNIQUE_BROWSE_GROUP_ID)
            {
                switch (item.ItemId)
                {
                    case 0:
                        DownloadUserFilesEntry(false, false, ItemPositionLongClicked);
                        return true;
                    case 1:
                        DownloadUserFilesEntry(true, false, ItemPositionLongClicked);
                        return true;
                    case 2:
                        DataItem itemSelected = GetItemSelected(ItemPositionLongClicked, FilteredResults);
                        var folderSummary = GetFolderSummary(itemSelected);
                        ShowFolderSummaryDialog(folderSummary);
                        return true;
                    case 3:
                        DataItem _itemSelected = GetItemSelected(ItemPositionLongClicked, FilteredResults);
                        //bool isDir = itemSelected.IsDirectory();
                        string slskLink = CommonHelpers.CreateSlskLink(true, _itemSelected.Directory.Name, currentUsernameUI);
                        CommonHelpers.CopyTextToClipboard(SeekerState.ActiveActivityRef, slskLink);
                        Toast.MakeText(SeekerState.ActiveActivityRef, Resource.String.LinkCopied, ToastLength.Short).Show();
                        return true;
                }
            }
            return base.OnContextItemSelected(item);
        }

        public void ShowFolderSummaryDialog(FolderSummary folderSummary)
        {
            string lengthTimePt2 = (folderSummary.LengthSeconds == 0) ? ": -" : string.Format(": {0}", CommonHelpers.GetHumanReadableTime(folderSummary.LengthSeconds));
            string lengthTime = SeekerApplication.ApplicationContext.GetString(Resource.String.Length) + lengthTimePt2;

            string sizeString = SeekerApplication.ApplicationContext.GetString(Resource.String.size_column) + string.Format(" {0}", CommonHelpers.GetHumanReadableSize(folderSummary.SizeBytes));

            string numFilesString = SeekerApplication.ApplicationContext.GetString(Resource.String.NumFiles) + string.Format(": {0}", folderSummary.NumFiles);
            string numSubFoldersString = SeekerApplication.ApplicationContext.GetString(Resource.String.NumSubfolders) + string.Format(": {0}", folderSummary.NumSubFolders);

            var builder = new AndroidX.AppCompat.App.AlertDialog.Builder(this.Context, Resource.Style.MyAlertDialogTheme);

            void OnCloseClick(object sender, DialogClickEventArgs e)
            {
                (sender as AndroidX.AppCompat.App.AlertDialog).Dismiss();
            }

            var diag = builder.SetMessage(numFilesString +
                System.Environment.NewLine +
                System.Environment.NewLine +
                numSubFoldersString +
                System.Environment.NewLine +
                System.Environment.NewLine +
                sizeString +
                System.Environment.NewLine +
                System.Environment.NewLine +
                lengthTime).SetPositiveButton(Resource.String.close, OnCloseClick).Create();
            diag.Show();
            diag.GetButton((int)Android.Content.DialogButtonType.Positive).SetTextColor(SearchItemViewExpandable.GetColorFromAttribute(SeekerState.ActiveActivityRef, Resource.Attribute.mainTextColor));
        }

        public static TreeNode<Directory> GetNodeByName(TreeNode<Directory> rootTree, string nameToFindDirName)
        {
            if (rootTree.Data.Name == nameToFindDirName)
            {
                return rootTree;
            }
            else
            {
                foreach (TreeNode<Directory> d in rootTree.Children)
                {
                    var node = GetNodeByName(d, nameToFindDirName);
                    if (node != null)
                    {
                        return node;
                    }
                }
            }
            return null;
        }

        private static void ClearFilterStringAndCached(bool force = false)
        {
            FilterString = string.Empty;
            FilteredResults = false;
            WordsToAvoid.Clear();
            WordsToInclude.Clear();
            //FilterSpecialFlags.Clear();
            if (BrowseFragment.Instance != null && BrowseFragment.Instance.rootView != null) //if you havent been there it will be null.
            {
                SeekerState.MainActivityRef.RunOnUiThread(() =>
                {
                    EditText filterText = BrowseFragment.Instance.rootView.FindViewById<EditText>(Resource.Id.filterText);
                    filterText.Text = string.Empty;
                    SearchFragment.UpdateDrawableState(filterText, true);
                });
            }
        }

        public static void SeekerState_BrowseResponseReceived(object sender, BrowseResponseEvent e)
        {
            ClearFilterStringAndCached();
            ScrollPositionRestore?.Clear();
            ScrollPositionRestoreRotate = null;
            filteredDataItemsForListView = new List<DataItem>();
            cachedFilteredDataItemsForListView = null;
            CurrentUsername = e.Username;
            diagnostics_count = e.OriginalBrowseResponse.DirectoryCount;
            
            lock (dataItemsForListView) // on non UI thread.
            {
                dataItemsForListView.Clear(); // clear old
                username = e.Username;
                if (e.StartingLocation != null && e.StartingLocation != string.Empty)
                {
                    var staringPoint = BrowseFragment.GetNodeByName(e.BrowseResponseTree, e.StartingLocation);

                    if (staringPoint == null)
                    {
                        Logger.FirebaseDebug("SeekerState_BrowseResponseReceived: startingPoint is null");
                        SeekerState.ActiveActivityRef.RunOnUiThread(() => { Toast.MakeText(SeekerState.ActiveActivityRef, SeekerState.ActiveActivityRef.Resources.GetString(Resource.String.error_browse_at_location), ToastLength.Long).Show(); });
                        return; //we might be in a bad state just returning like this... idk...
                    }

                    //**added bc if someone wants to browse at folder and there are other folders then they will not see them....
                    foreach (TreeNode<Directory> d in staringPoint.Children)
                    {
                        dataItemsForListView.Add(new DataItem(d.Data, d));
                    }
                    //**added bc if someone wants to browse at folder and there are other folders then they will not see them....


                    foreach (File f in staringPoint.Data.OrderedFiles)
                    {
                        dataItemsForListView.Add(new DataItem(f, staringPoint));
                    }
                }
                else
                {
                    foreach (TreeNode<Directory> d in e.BrowseResponseTree.Children)
                    {
                        dataItemsForListView.Add(new DataItem(d.Data, d));
                    }

                    //here we do files as well......  **I added this bc on your first browse you will not get any root dir files....
                    if (e.BrowseResponseTree.Data != null && e.BrowseResponseTree.Data.FileCount != 0)
                    {
                        foreach (File f in e.BrowseResponseTree.Data.OrderedFiles)
                        {
                            dataItemsForListView.Add(new DataItem(f, e.BrowseResponseTree));
                        }
                    }
                    //**I added this bc on your first browse you will not get any root dir files....
                }
            }

            BrowseResponseReceivedUI?.Invoke(null, new EventArgs());
        }

        public void RefreshOnRecieved()
        {
            if (noBrowseView != null)
            {
                noBrowseView.Visibility = ViewStates.Gone;
                separator.Visibility = ViewStates.Visible;
            }
            listViewDirectories = rootView.FindViewById<ListView>(Resource.Id.listViewDirectories);
            //if(!tempHackItemClick)
            //{
            listViewDirectories.ItemClick -= ListViewDirectories_ItemClick;
            listViewDirectories.ItemClick += ListViewDirectories_ItemClick;
            listViewDirectories.ItemLongClick -= ListViewDirectories_ItemLongClick;
            listViewDirectories.ItemLongClick += ListViewDirectories_ItemLongClick;



            //tempHackItemClick =true; 
            //}

            Logger.FirebaseInfo("RefreshOnRecieved " + CurrentUsername);
            //!!!collection was modified exception!!!
            //guessing from modifying dataItemsForListView which can happen in this method and in others...
            currentUsernameUI = CurrentUsername;
            SetBrowseAdapters(false, dataItemsForListView, true);
            //listViewDirectories.Adapter = new BrowseAdapter(this.Context, dataItemsForListView, this); //on UI thread. in a lock.
        }


 

        private static AndroidX.AppCompat.App.AlertDialog browseUserDialog = null;

        public void ShowEditTextBrowseUserDialog()
        {
            //AndroidX.AppCompat.App.AlertDialog.Builder builder = new AndroidX.AppCompat.App.AlertDialog.Builder(); //failed to bind....
            FragmentActivity c = this.Activity != null ? this.Activity : SeekerState.MainActivityRef;
            Logger.FirebaseInfo("ShowEditTextBrowseUserDialog" + c.IsDestroyed + c.IsFinishing);
            AndroidX.AppCompat.App.AlertDialog.Builder builder = new AndroidX.AppCompat.App.AlertDialog.Builder(c, Resource.Style.MyAlertDialogTheme); //failed to bind....
            builder.SetTitle(c.Resources.GetString(Resource.String.browse_user_files));

            View viewInflated = LayoutInflater.From(c).Inflate(Resource.Layout.browse_chosen_user, (ViewGroup)this.View, false);

            AutoCompleteTextView input = (AutoCompleteTextView)viewInflated.FindViewById<AutoCompleteTextView>(Resource.Id.chosenUserEditText);
            SeekerApplication.SetupRecentUserAutoCompleteTextView(input);

            builder.SetView(viewInflated);

            Action<View> goSnackBarAction = new Action<View>((View v) =>
            {
                ((AndroidX.ViewPager.Widget.ViewPager)(SeekerState.MainActivityRef.FindViewById(Resource.Id.pager))).SetCurrentItem(3, true);
            });

            EventHandler<DialogClickEventArgs> eventHandler = new EventHandler<DialogClickEventArgs>((object sender, DialogClickEventArgs okayArgs) =>
            {
                //Do the Browse Logic...
                string usernameToBrowse = input.Text;
                if (string.IsNullOrWhiteSpace(usernameToBrowse))
                {
                    Toast.MakeText(this.Activity != null ? this.Activity : SeekerState.MainActivityRef, SeekerState.MainActivityRef.Resources.GetString(Resource.String.must_type_a_username_to_browse), ToastLength.Short).Show();
                    if (sender is AndroidX.AppCompat.App.AlertDialog aDiag1) //actv
                    {
                        aDiag1.Dismiss();
                    }
                    else
                    {
                        BrowseFragment.browseUserDialog.Dismiss();
                    }
                    return;
                }
                SeekerState.RecentUsersManager.AddUserToTop(usernameToBrowse, true);
                DownloadDialog.RequestFilesApi(usernameToBrowse, this.View, goSnackBarAction, null);
                if (sender is AndroidX.AppCompat.App.AlertDialog aDiag)
                {
                    aDiag.Dismiss();
                }
                else
                {
                    BrowseFragment.browseUserDialog.Dismiss();
                }
            });
            EventHandler<DialogClickEventArgs> eventHandlerCancel = new EventHandler<DialogClickEventArgs>((object sender, DialogClickEventArgs cancelArgs) =>
            {
                if (sender is AndroidX.AppCompat.App.AlertDialog aDiag)
                {
                    aDiag.Dismiss();
                }
                else
                {
                    BrowseFragment.browseUserDialog.Dismiss();
                }
            });

            System.EventHandler<TextView.EditorActionEventArgs> editorAction = (object sender, TextView.EditorActionEventArgs e) =>
            {
                if (e.ActionId == Android.Views.InputMethods.ImeAction.Done || //in this case it is Done (blue checkmark)
                    e.ActionId == Android.Views.InputMethods.ImeAction.Go ||
                    e.ActionId == Android.Views.InputMethods.ImeAction.Next ||
                    e.ActionId == Android.Views.InputMethods.ImeAction.Search) //ImeNull if being called due to the enter key being pressed. (MSDN) but ImeNull gets called all the time....
                {
                    Logger.Debug("IME ACTION: " + e.ActionId);
                    
                    // overriding this, the keyboard fails to go down by default for some reason.....
                    try
                    {
                        Android.Views.InputMethods.InputMethodManager imm = (Android.Views.InputMethods.InputMethodManager)SeekerState.MainActivityRef.GetSystemService(Context.InputMethodService);
                        imm.HideSoftInputFromWindow(rootView.WindowToken, 0);
                    }
                    catch (System.Exception ex)
                    {
                        Logger.FirebaseDebug(ex.Message + " error closing keyboard");
                    }
                    //Do the Browse Logic...
                    eventHandler(sender, null);
                }
            };

            System.EventHandler<TextView.KeyEventArgs> keypressAction = (object sender, TextView.KeyEventArgs e) =>
            {
                if (e.Event != null && e.Event.Action == KeyEventActions.Up && e.Event.KeyCode == Keycode.Enter)
                {
                    Logger.Debug("keypress: " + e.Event.KeyCode);
                    
                    // overriding this, the keyboard fails to go down by default for some reason.....
                    try
                    {
                        Android.Views.InputMethods.InputMethodManager imm = (Android.Views.InputMethods.InputMethodManager)SeekerState.MainActivityRef.GetSystemService(Context.InputMethodService);
                        imm.HideSoftInputFromWindow(rootView.WindowToken, 0);
                    }
                    catch (System.Exception ex)
                    {
                        Logger.FirebaseDebug(ex.Message + " error closing keyboard");
                    }
                    //Do the Browse Logic...
                    eventHandler(sender, null);
                }
                else
                {
                    e.Handled = false;
                }
            };

            input.KeyPress += keypressAction;
            input.EditorAction += editorAction;
            input.FocusChange += Input_FocusChange;

            builder.SetPositiveButton(this.Resources.GetString(Resource.String.okay), eventHandler);
            builder.SetNegativeButton(this.Resources.GetString(Resource.String.cancel), eventHandlerCancel);
            // Set up the buttons

            BrowseFragment.browseUserDialog = builder.Create();
            try
            {
                BrowseFragment.browseUserDialog.Show();
                CommonHelpers.DoNotEnablePositiveUntilText(BrowseFragment.browseUserDialog, input);

            }
            catch (WindowManagerBadTokenException e)
            {
                if (SeekerState.MainActivityRef == null || this.Activity == null)
                {
                    Logger.FirebaseDebug("WindowManagerBadTokenException null activities");
                }
                else
                {
                    bool isCachedMainActivityFinishing = SeekerState.MainActivityRef.IsFinishing;
                    bool isOurActivityFinishing = this.Activity.IsFinishing;
                    Logger.FirebaseDebug("WindowManagerBadTokenException are we finishing:" + isCachedMainActivityFinishing + isOurActivityFinishing);
                }
            }
            catch (Exception err)
            {
                if (SeekerState.MainActivityRef == null || this.Activity == null)
                {
                    Logger.FirebaseDebug("Exception null activities");
                }
                else
                {
                    bool isCachedMainActivityFinishing = SeekerState.MainActivityRef.IsFinishing;
                    bool isOurActivityFinishing = this.Activity.IsFinishing;
                    Logger.FirebaseDebug("Exception are we finishing:" + isCachedMainActivityFinishing + isOurActivityFinishing);
                }
            }

        }



        private void Input_FocusChange(object sender, View.FocusChangeEventArgs e)
        {
            try
            {
                SeekerState.MainActivityRef.Window.SetSoftInputMode(SoftInput.AdjustNothing);
            }
            catch (System.Exception err)
            {
                Logger.FirebaseDebug("MainActivity_FocusChange" + err.Message);
            }
        }

    }

    public class PathItem
    {
        public string DisplayName;
        public bool IsLastNode;
        public PathItem(string displayName, bool isLastNode)
        {
            DisplayName = displayName;
            IsLastNode = isLastNode;
        }
    }


    public class FullFileInfo
    {
        public long Size = 0;
        public string FullFileName = string.Empty;
        public int Depth = 1;
        public bool wasFilenameLatin1Decoded = false;
        public bool wasFolderLatin1Decoded = false;
    }

    public class DataItem
    {
        private string Name = "";
        public Directory Directory;
        public Soulseek.File File;
        public TreeNode<Directory> Node;
        public DataItem(Directory d, TreeNode<Directory> n)
        {
            Name = d.Name; //this is the full name (other than file).. i.e. primary:Music\\Soulseek Complete
            Directory = d;
            Node = n;
        }
        public DataItem(Soulseek.File f, TreeNode<Directory> n)
        {
            Name = f.Filename;
            File = f;
            Node = n;
        }
        public bool IsDirectory()
        {
            return Directory != null;
        }
        public string GetDisplayName()
        {
            if (IsDirectory())
            {
                if (this.Node.IsLocked)
                {
                    return new System.String(Java.Lang.Character.ToChars(0x1F512)) + CommonHelpers.GetFileNameFromFile(Name);
                }
                else
                {
                    return CommonHelpers.GetFileNameFromFile(Name);
                }
            }
            else
            {
                if (this.Node.IsLocked)
                {
                    return new System.String(Java.Lang.Character.ToChars(0x1F512)) + Name;
                }
                else
                {
                    return Name;
                }
            }
        }
    }
}