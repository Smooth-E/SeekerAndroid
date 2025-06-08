using Seeker.Helpers;
using Android.App;
using Android.OS;
using Android.Views;
using Android.Widget;
using System;
using _Microsoft.Android.Resource.Designer;
using Seeker.Utils;

namespace Seeker
{
    public class SearchDialog : AndroidX.Fragment.App.DialogFragment
    {

        public static EventHandler<bool> SearchTermFetched;

        public static volatile string SearchTerm = string.Empty;
        public static volatile bool IsFollowingLink = false;

        private Guid guid = Guid.NewGuid();

        public SearchDialog(string searchTerm, bool isFollowingLink)
        {
            SearchTerm = searchTerm;
            IsFollowingLink = isFollowingLink;
            SearchDialog.Instance = this;
        }
        public SearchDialog()
        {

        }

        private void SetControlState()
        {
            var editText = this.View.FindViewById<EditText>(Resource.Id.editText);
            ViewGroup followingLinkLayout = this.View.FindViewById<ViewGroup>(Resource.Id.followingLinkLayout);
            //ProgressBar followingLinkBar = this.View.FindViewById<ProgressBar>(Resource.Id.progressBarFollowingLink);
            editText.Text = SearchTerm;
            if (IsFollowingLink)
            {
                editText.Enabled = false;
                editText.Clickable = false;
                editText.Focusable = false;
                editText.FocusableInTouchMode = false;
                editText.SetCursorVisible(false);
                editText.Alpha = 0.8f;
                followingLinkLayout.Visibility = ViewStates.Visible;
            }
            else
            {
                editText.Enabled = true;
                editText.Clickable = true;
                editText.Focusable = true;
                editText.FocusableInTouchMode = true;
                editText.SetCursorVisible(true);
                editText.Alpha = 1.0f;
                followingLinkLayout.Visibility = ViewStates.Gone;
            }
        }

        public override void OnPause()
        {
            base.OnPause();
            SearchTermFetched -= SearchTermFetchedEventHandler;
        }

        public static SearchDialog Instance = null;

        public override void OnResume()
        {
            if (Instance != null && Instance != this)
            {
                // we only support 1 dialog, the most recent one..
                Logger.Debug("cancelling old search dialog");
                Dismiss();
            }
            Logger.Debug("resuming instance: " + guid);

            SetControlState();
            base.OnResume();
            SearchTermFetched += SearchTermFetchedEventHandler;

            Dialog?.SetSizeProportional(.9, -1);
        }

        public override void OnDestroy()
        {
            Logger.Debug("OnDestroy SearchDialog");
            Instance = null;
            
            base.OnDestroy();
        }

        private void SearchTermFetchedEventHandler(object o, bool failed)
        {
            this.Activity.RunOnUiThread(() =>
            {
                this.SetControlState();
                if (failed)
                {
                    Toast.MakeText(SeekerState.ActiveActivityRef, "Failed to parse search term from link. Contact Developer.", ToastLength.Long).Show();
                }
            }
            );
        }

        private void SetupEventHandlers()
        {
            View Cancel = this.View.FindViewById<View>(Resource.Id.textViewCancel);
            Cancel.Click += Cancel_Click;

            //todo search and cancel / close.
            Button closeButton = this.View.FindViewById<Button>(Resource.Id.searchCloseButton);
            closeButton.Click += CloseButton_Click;

            Button searchButton = this.View.FindViewById<Button>(Resource.Id.searchButton);
            searchButton.Click += SearchButton_Click; ;
        }

        private void SearchButton_Click(object sender, EventArgs e)
        {
            var editText = this.View.FindViewById<EditText>(Resource.Id.editText);
            SearchFragment.PerformSearchLogicFromSearchDialog(editText.Text);
            IsFollowingLink = false;
            SearchTerm = null;
            this.Dismiss();
        }

        private void CloseButton_Click(object sender, EventArgs e)
        {
            IsFollowingLink = false;
            SearchTerm = null;
            this.Dismiss();
        }

        private void Cancel_Click(object sender, EventArgs e)
        {
            IsFollowingLink = false;
            SetControlState();
        }

        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            // container is parent
            return inflater.Inflate(ResourceConstant.Layout.search_intent_dialog, container);
        }

        /// <summary>Called after on create view</summary>
        public override void OnViewCreated(View view, Bundle savedInstanceState)
        {
            // after opening up my soulseek app on my phone,
            // 6 hours after I last used it, I got a nullref somewhere in here....
            base.OnViewCreated(view, savedInstanceState);

            const int attr = ResourceConstant.Attribute.the_rounded_corner_dialog_background_drawable;
            var drawable = SeekerApplication.GetDrawableFromAttribute(SeekerState.ActiveActivityRef, attr);
            Dialog!.Window!.SetBackgroundDrawable(drawable);
            SetStyle((int)DialogFragmentStyle.Normal, 0);
            SetupEventHandlers();


        }
    }
}