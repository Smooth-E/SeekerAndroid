﻿using Android.App;
using Android.Content;
using Android.OS;
using Android.Runtime;
using Android.Views;
using Android.Widget;
using AndroidX.RecyclerView.Widget;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Seeker.Utils;

namespace Seeker.Chatroom
{
    public class ChatroomOverviewFragment : AndroidX.Fragment.App.Fragment
    {
        private RecyclerView recyclerViewOverview;
        private LinearLayoutManager recycleLayoutManager;
        private ChatroomOverviewRecyclerAdapter recyclerAdapter;
        private SearchView filterChatroomView;
        private Soulseek.RoomList internalList = null;
        private List<Soulseek.RoomInfo> internalListParsed = null;
        private List<Soulseek.RoomInfo> internalListParsedFiltered = null;
        private static string FilterString = string.Empty;
        private TextView chatroomsListLoadingView = null;
        private bool created = false;
        public override View OnCreateView(LayoutInflater inflater, ViewGroup container, Bundle savedInstanceState)
        {
            Logger.Debug("create chatroom overview view");
            ChatroomController.RoomListReceived += OnChatListReceived;
            View rootView = inflater.Inflate(Resource.Layout.chatroom_overview, container, false);
            chatroomsListLoadingView = rootView.FindViewById<TextView>(Resource.Id.chatroomListLoading);
            filterChatroomView = rootView.FindViewById<SearchView>(Resource.Id.filterChatroom);
            filterChatroomView.QueryTextChange += FilterChatroomView_QueryTextChange;
            if (ChatroomController.RoomList == null)
            {
                chatroomsListLoadingView.Visibility = ViewStates.Visible;
            }
            else
            {
                chatroomsListLoadingView.Visibility = ViewStates.Gone;
            }
            recyclerViewOverview = rootView.FindViewById<RecyclerView>(Resource.Id.recyclerViewOverview);
            recyclerViewOverview.AddItemDecoration(new DividerItemDecoration(this.Context, DividerItemDecoration.Vertical));
            recycleLayoutManager = new LinearLayoutManager(Activity);
            if (ChatroomController.RoomList == null)
            {
                internalList = null;
                internalListParsed = new List<Soulseek.RoomInfo>();
                ChatroomController.GetRoomListApi();
            }
            else
            {
                internalList = ChatroomController.RoomList;
                internalListParsed = ChatroomController.GetParsedList(ChatroomController.RoomList);
            }
            recyclerAdapter = new ChatroomOverviewRecyclerAdapter(FilterRoomList(internalListParsed)); //this depends tightly on MessageController... since these are just strings..
            recyclerViewOverview.SetAdapter(recyclerAdapter);
            recyclerViewOverview.SetLayoutManager(recycleLayoutManager);
            recyclerAdapter.NotifyDataSetChanged();

            HookUpOverviewEventHandlers(true);

            created = true;
            return rootView;
        }

        private void HookUpOverviewEventHandlers(bool binding)
        {
            ChatroomController.RoomNowHasUnreadMessages -= OnRoomNowHasUnreadMessages;
            ChatroomController.CurrentlyJoinedRoomHasUpdated -= OnCurrentConnectedChanged;
            ChatroomController.CurrentlyJoinedRoomsCleared -= OnCurrentConnectedCleared;
            ChatroomController.JoinedRoomsHaveUpdated -= OnJoinedRoomsHaveUpdated;
            if (binding)
            {
                ChatroomController.RoomNowHasUnreadMessages += OnRoomNowHasUnreadMessages;
                ChatroomController.CurrentlyJoinedRoomHasUpdated += OnCurrentConnectedChanged;
                ChatroomController.CurrentlyJoinedRoomsCleared += OnCurrentConnectedCleared;
                ChatroomController.JoinedRoomsHaveUpdated += OnJoinedRoomsHaveUpdated;
            }
        }

        /// <summary>
        /// This is due to log out.
        /// In which case grey out the joined rooms.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="rooms"></param>
        public void OnCurrentConnectedCleared(object sender, List<string> rooms)
        {
            Logger.Debug("OnCurrentConnectedCleared");
            SeekerState.ActiveActivityRef?.RunOnUiThread(() =>
            {
                recyclerAdapter?.notifyRoomStatusesChanged(rooms);
            });
        }

        public void OnRoomNowHasUnreadMessages(object sender, string room)
        {
            SeekerState.ActiveActivityRef?.RunOnUiThread(() =>
            {
                recyclerAdapter?.notifyRoomStatusChanged(room);
            });
        }

        /// <summary>
        /// This is when we re-connect and successfully send server join message.
        /// We ungrey if we were previously greyed.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="room"></param>
        public void OnCurrentConnectedChanged(object sender, string room)
        {
            Logger.Debug("OnCurrentConnectedChanged");
            SeekerState.ActiveActivityRef?.RunOnUiThread(() => { this.recyclerAdapter?.notifyRoomStatusChanged(room); });
        }

        public void OnJoinedRoomsHaveUpdated(object sender, EventArgs e)
        {
            Logger.Debug("OnJoinedRoomsHaveUpdated");
            ChatroomController.RoomListParsed = ChatroomController.GetParsedList(ChatroomController.RoomList); //reparse this for our newly joined rooms.
            internalList = ChatroomController.RoomList;
            internalListParsed = ChatroomController.RoomListParsed;
            UpdateChatroomList();
        }

        public override void OnResume()
        {
            Logger.Debug("overview on resume");
            Logger.Debug("hook up chat overview event handlers ");
            HookUpOverviewEventHandlers(true);
            recyclerAdapter?.NotifyDataSetChanged();
            base.OnResume();
        }

        public override void OnPause()
        {
            Logger.Debug("overview on pause");
            HookUpOverviewEventHandlers(false);
            base.OnPause();
        }

        private void FilterChatroomView_QueryTextChange(object sender, SearchView.QueryTextChangeEventArgs e)
        {
            FilterString = e.NewText;
            this.UpdateChatroomList();
        }

        private static List<Soulseek.RoomInfo> FilterRoomList(List<Soulseek.RoomInfo> original)
        {
            if (FilterString != string.Empty)
            {
                return original.Where((roomInfo) => { return roomInfo.Name.Contains(FilterString, StringComparison.InvariantCultureIgnoreCase); }).ToList();
            }
            else
            {
                return original;
            }
        }

        public void OnChatListReceived(object sender, EventArgs eventArgs)
        {
            internalList = ChatroomController.RoomList;
            internalListParsed = ChatroomController.RoomListParsed; //here it is already parsed.

            this.UpdateChatroomList();
        }

        private void UpdateChatroomList()
        {
            Logger.Debug("update chatroom list");
            var filteredRoomList = FilterRoomList(internalListParsed);
            var activity = Activity ?? ChatroomActivity.ChatroomActivityRef;
            
            activity?.RunOnUiThread(() =>
            {
                // this depends tightly on MessageController... since these are just strings..
                recyclerAdapter = new ChatroomOverviewRecyclerAdapter(filteredRoomList);
                chatroomsListLoadingView.Visibility = ViewStates.Gone;
                recyclerViewOverview.SetAdapter(recyclerAdapter);
                recyclerAdapter.NotifyDataSetChanged();
            });
        }

        public override void OnAttach(Context activity)
        {
            // attach can happen before we created our view...
            if (created)
            {
                internalList = ChatroomController.RoomList;
                internalListParsed = ChatroomController.GetParsedList(ChatroomController.RoomList);
                recyclerAdapter = new ChatroomOverviewRecyclerAdapter(FilterRoomList(internalListParsed)); //this depends tightly on MessageController... since these are just strings..
                recyclerViewOverview.SetAdapter(recyclerAdapter);
                recyclerAdapter.NotifyDataSetChanged();
                Logger.Debug("on chatroom overview attach");
                ChatroomController.RoomListReceived -= OnChatListReceived;
                ChatroomController.RoomListReceived += OnChatListReceived;
            }
            base.OnAttach(activity);
        }
    }
}
