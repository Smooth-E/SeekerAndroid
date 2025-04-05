using System;
using System.Collections.Generic;
using System.Linq;
using _Microsoft.Android.Resource.Designer;
using Android.Content;
using Android.Views;
using Android.Widget;
using Google.Android.Material.Snackbar;
using Seeker.Main;
using Seeker.Managers;
using Seeker.Utils;
using Soulseek;

namespace Seeker.Helpers;

/// <summary>
/// When getting a users info for their User Info Activity, we need their peer UserInfo and their server UserData.
/// We add them to a list so that when the UserData comes in from the server, we know to save it.
/// </summary>
public static class RequestedUserInfoHelper
{
    private static object picturesStoredUsersLock = new();
    private static List<string> picturesStoredUsers = new();

    // these are people we have specifically requested. normally there are 0-4 people in here I would expect.
    public static volatile List<UserListItem> RequestedUserList = new();

    public static UserListItem GetInfoForUser(string uname)
    {
        lock (RequestedUserList)
        {
            return RequestedUserList.FirstOrDefault(userListItem => userListItem.Username == uname);
        }
    }

    private static bool ContainsUserInfo(string uname)
    {
        var uinfo = GetInfoForUser(uname);
        if (uinfo == null)
        {
            return false;
        }

        if (uinfo.UserInfo == null || uinfo.UserData == null)
        {
            return false;
        }

        return true;
    }

    public static void RequestUserInfoApi(string uname)
    {
        if (uname == string.Empty)
        {
            Toast.MakeText(SeekerApplication.ApplicationContext, Resource.String.request_user_error_empty,
                ToastLength.Short).Show();
            return;
        }

        if (!SeekerState.currentlyLoggedIn)
        {
            Toast.MakeText(SeekerApplication.ApplicationContext, Resource.String.must_be_logged_to_request_user_info,
                ToastLength.Short).Show();
            return;
        }

        // if we already have the username, then just do it
        if (ContainsUserInfo(uname))
        {
            //just do it.....
            LaunchUserInfoView(uname);
            return;
        }

        if (SeekerState.CurrentlyLoggedInButDisconnectedState())
        {
            // we disconnected. login then do the rest.
            // this is due to temp lost connection
            if (!SoulseekConnection.ShowMessageAndCreateReconnectTask(SeekerState.ActiveActivityRef, false, out var t))
            {
                return;
            }

            t.ContinueWith(continuationAction =>
            {
                if (continuationAction.IsFaulted)
                {
                    SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                    {
                        Toast.MakeText(SeekerState.ActiveActivityRef, ResourceConstant.String.failed_to_connect,
                            ToastLength.Short)
                            ?.Show();
                    });
                    return;
                }

                SeekerState.ActiveActivityRef.RunOnUiThread(new Action(() => { RequestUserInfoLogic(uname); }));
            });
        }
        else
        {
            RequestUserInfoLogic(uname);
        }
    }

    public static void RequestUserInfoLogic(string uname)
    {
        Toast.MakeText(SeekerApplication.ApplicationContext, ResourceConstant.String.requesting_user_info, ToastLength.Short)
            ?.Show();
        lock (RequestedUserList)
        {
            RequestedUserList.Add(new UserListItem(uname));
        }

        SeekerState.SoulseekClient.GetUserDataAsync(uname);
        SeekerState.SoulseekClient.GetUserInfoAsync(uname).ContinueWith(userInfoTask =>
        {
            if (userInfoTask.IsCompletedSuccessfully)
            {
                if (!AddIfRequestedUser(uname, null, null, userInfoTask.Result))
                {
                    Logger.FirebaseDebug("requested user info logic yet could not find in list!!");
                    // TODO: HANDLE ERROR
                }
                else
                {
                    Action<View> action = new Action<View>((View v) => { LaunchUserInfoView(uname); });

                    SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                    {
                        //show snackbar (for active activity, active content view) so they can go to it... TODO
                        Snackbar sb = Snackbar
                            .Make(SeekerApplication.GetViewForSnackbar(),
                                string.Format(
                                    SeekerState.ActiveActivityRef.GetString(Resource.String.user_info_received),
                                    uname), Snackbar.LengthLong).SetAction(Resource.String.view, action)
                            .SetActionTextColor(Resource.Color.lightPurpleNotTransparent);
                        (sb.View.FindViewById<TextView>(Resource.Id.snackbar_action) as TextView).SetTextColor(
                            SearchItemViewExpandable.GetColorFromAttribute(SeekerState.ActiveActivityRef,
                                Resource.Attribute
                                    .mainTextColor)); //AndroidX.Core.Content.ContextCompat.GetColor(this.Context,Resource.Color.lightPurpleNotTransparent));
                        sb.Show();
                    });
                }
            }
            else
            {
                Exception e = userInfoTask.Exception;

                if (e.InnerException is SoulseekClientException && e.InnerException.Message.ToLower()
                        .Contains(Soulseek.SoulseekClient.FailedToEstablishDirectOrIndirectStringLower))
                {
                    SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                    {
                        Toast.MakeText(SeekerState.ActiveActivityRef,
                            string.Format(
                                SeekerState.ActiveActivityRef.GetString(Resource.String.user_info_failed_conn),
                                uname), ToastLength.Long).Show();
                    });
                }
                else if (e.InnerException is UserOfflineException)
                {
                    SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                    {
                        Toast.MakeText(SeekerState.ActiveActivityRef,
                            string.Format(
                                SeekerState.ActiveActivityRef.GetString(
                                    Resource.String.user_info_failed_offline), uname), ToastLength.Long).Show();
                    });
                }
                else if (e.InnerException is TimeoutException)
                {
                    SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                    {
                        Toast.MakeText(SeekerState.ActiveActivityRef,
                            string.Format(
                                SeekerState.ActiveActivityRef.GetString(
                                    Resource.String.user_info_failed_timeout), uname), ToastLength.Long).Show();
                    });
                }
                else
                {
                    string msg = e.Message;
                    string innerMsg = e.InnerException.Message;
                    Type t = e.InnerException.GetType();
                    Logger.FirebaseDebug("unexpected exception: " + msg + t.Name);
                }
                //toast timed out for user x, etc... user X is offline etc.
            }
        });
    }

    public static void LaunchUserInfoView(string uname)
    {
        Intent intent = new Intent(SeekerState.ActiveActivityRef, typeof(ViewUserInfoActivity));
        intent.PutExtra(ViewUserInfoActivity.USERNAME_TO_VIEW, uname);
        SeekerState.ActiveActivityRef.StartActivity(intent);
    }

    /// <summary>
    /// This event is used if the userinfo comes late and one is on the ViewUserInfoActivity to load it...
    /// </summary>
    public static EventHandler<UserData> UserDataReceivedUI;

    public static bool AddIfRequestedUser(string uname, UserData userData, UserStatus userStatus, UserInfo userInfo)
    {
        bool found = false;
        lock (RequestedUserList)
        {
            bool removeOldestPic = false;
            foreach (UserListItem item in RequestedUserList)
            {
                if (item.Username == uname)
                {
                    found = true;
                    if (userData != null)
                    {
                        Logger.Debug("Requested server UserData received");
                        item.UserData = userData;
                        UserDataReceivedUI?.Invoke(null, userData);
                    }

                    if (userStatus != null)
                    {
                        Logger.Debug("Requested server UserStatus received");
                        item.UserStatus = userStatus;
                    }

                    if (userInfo != null)
                    {
                        Logger.Debug("Requested peer UserInfo received");
                        item.UserInfo = userInfo;
                        
                        if (userInfo.HasPicture)
                        {
                            Logger.Debug("peer has pic");
                            lock (picturesStoredUsersLock)
                            {
                                picturesStoredUsers.Add(uname);
                                picturesStoredUsers = picturesStoredUsers.Distinct().ToList();
                                
                                if (picturesStoredUsers.Count > int.MaxValue) // disabled for now
                                {
                                    removeOldestPic = true;
                                }
                            }
                        }
                    }

                    break;
                }
            }

            if (!found)
            {
                //this is normal
            }

            if (removeOldestPic)
            {
                Logger.FirebaseInfo("Remove oldest picture");
                lock (picturesStoredUsersLock)
                {
                    string userToRemovePic = picturesStoredUsers[0];
                    picturesStoredUsers.RemoveAt(0);
                }

                //lock (RequestedUserList)
                //{
                foreach (UserListItem item in RequestedUserList)
                {
                    if (item.Username == uname)
                    {
                        item.UserInfo = null;
                    }
                }
                //}
            }
        }

        return found;
    }
}
