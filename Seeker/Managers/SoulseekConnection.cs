using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using _Microsoft.Android.Resource.Designer;
using Android.Content;
using Seeker.Main;
using Seeker.Transfers;
using Seeker.Utils;
using Soulseek;

namespace Seeker.Managers;

public static class SoulseekConnection 
{
    public static Task OurCurrentLoginTask = null;
    public static object OurCurrentLoginTaskSyncObject = new object();

    public static bool ShowMessageAndCreateReconnectTask(Context c, bool silent, out Task connectTask)
    {
        c ??= SeekerState.MainActivityRef;

        if (!silent)
        {
            c.ShowShortToast(ResourceConstant.String.temporary_disconnected);
        }

        // if we are still not connected then creating the task will throw. 
        // also if the async part of the task fails we will get task.faulted.
        try
        {
            connectTask = ConnectAndPerformPostConnectTasks(SeekerState.Username, SeekerState.Password);
            return true;
        }
        catch
        {
            if (!silent)
            {
                c.ShowShortToast(ResourceConstant.String.failed_to_connect);
            }
        }

        connectTask = null;
        return false;
    }
    
    public static Task ConnectAndPerformPostConnectTasks(string username, string password)
    {
        Task t = SeekerState.SoulseekClient.ConnectAsync(username, password);
        OurCurrentLoginTask = t;
        t.ContinueWith(PerformPostConnectTasks);
        return t;
    }
    
    public static void PerformPostConnectTasks(Task t)
    {
        if (t.IsCompletedSuccessfully)
        {
            try
            {
                lock (UserListManager.UserList)
                {
                    foreach (UserListItem item in UserListManager.UserList)
                    {
                        Logger.Debug("adding user: " + item.Username);
                        SeekerState.SoulseekClient.AddUserAsync(item.Username).ContinueWith(UpdateUserInfo);
                    }
                }

                lock (TransfersFragment.UsersWhereDownloadFailedDueToOffline)
                {
                    foreach (string userDownloadOffline in TransfersFragment.UsersWhereDownloadFailedDueToOffline.Keys)
                    {
                        Logger.Debug("adding user (due to a download we wanted from them when they were offline): " + userDownloadOffline);
                        SeekerState.SoulseekClient.AddUserAsync(userDownloadOffline).ContinueWith(UpdateUserOfflineDownload);
                    }
                }

                //this is if we wanted to change the status earlier but could not. note that when we first login, our status is Online by default.
                //so no need to change it to online.
                if (SeekerState.PendingStatusChangeToAwayOnline == SeekerState.PendingStatusChange.OnlinePending)
                {
                    //we just did this by logging in...
                    Logger.Debug("online was pending");
                    SeekerState.PendingStatusChangeToAwayOnline = SeekerState.PendingStatusChange.NothingPending;
                }
                else if (((SeekerState.PendingStatusChangeToAwayOnline == SeekerState.PendingStatusChange.AwayPending || SeekerState.OurCurrentStatusIsAway)))
                {
                    Logger.Debug("a change to away was pending / our status is away. lets set it now");

                    if (SeekerState.PendingStatusChangeToAwayOnline == SeekerState.PendingStatusChange.AwayPending)
                    {
                        Logger.Debug("pending that is....");
                    }
                    else
                    {
                        Logger.Debug("current that is...");
                    }

                    if (ForegroundLifecycleTracker.NumberOfActiveActivities != 0)
                    {
                        Logger.Debug("There is a hole in our logic!!! the pendingstatus and/or current status should not be away!!!");
                    }
                    else
                    {
                        MainActivity.SetStatusApi(true);
                    }
                }

                //if the number of directories is stale (meaning it changing when we werent logged in and so we could not update the server)
                //and we have not yet attempted to set up sharing (since after we attempt to set up sharing we will notify the server)
                //then tell the server here.
                //this makes it so that we tell the server once when Seeker first launches, and when things change, but not every time
                //we log in.
                if (SeekerState.NumberOfSharedDirectoriesIsStale && SeekerState.AttemptedToSetUpSharing)
                {
                    Logger.Debug("stale and we already attempted to set up sharing, so lets do it here in post log in.");
                    SharingManager.InformServerOfSharedFiles();
                }

                TransfersController.InitializeService();
            }
            catch (Exception e)
            {
                Logger.FirebaseDebug("PerformPostConnectTasks" + e.Message + e.StackTrace);
            }
        }
    }
    
    /// <summary>
    /// UserStatusChanged will not get called until an actual change. hence this call..
    /// </summary>
    /// <param name="t"></param>
    private static void UpdateUserInfo(Task<UserData> t)
    {
        try
        {
            Logger.Debug("Update User Info Received");
            if (t.IsCompletedSuccessfully)
            {
                string username = t.Result.Username;
                Logger.Debug("Update User Info: " + username + " status: " + t.Result.Status.ToString());
                if (UserListManager.UserListContainsUser(username))
                {
                    UserListManager.UserListAddUser(t.Result, t.Result.Status);
                }


            }
            else if (t.Exception?.InnerException is UserNotFoundException)
            {
                if (t.Exception.InnerException.Message.Contains("User ") && t.Exception.InnerException.Message.Contains("does not exist"))
                {
                    string username = t.Exception.InnerException.Message.Split(null)[1];
                    if (UserListManager.UserListContainsUser(username))
                    {
                        UserListManager.UserListSetDoesNotExist(username);
                    }
                }
                else
                {
                    Logger.FirebaseDebug("unexcepted error message - " + t.Exception.InnerException.Message);
                }
            }
            else
            {
                //timeout
                Logger.FirebaseDebug("UpdateUserInfo case 3 " + t.Exception.Message);
            }
        }
        catch (Exception e)
        {
            Logger.FirebaseDebug("UpdateUserInfo" + e.Message + e.StackTrace);
        }
    }
    
    /// <summary>UserStatusChanged will not get called until an actual change. hence this call</summary>
    private static void UpdateUserOfflineDownload(Task<UserData> t)
    {
        if (t.IsCompletedSuccessfully)
        {
            ProcessPotentialUserOfflineChangedEvent(t.Result.Username, t.Result.Status);
        }
    }
    
    public static void ProcessPotentialUserOfflineChangedEvent(string username, UserPresence status)
    {
        if (status != UserPresence.Offline)
        {
            if (SeekerState.AutoRetryBackOnline)
            {
                if (TransfersFragment.UsersWhereDownloadFailedDueToOffline.ContainsKey(username))
                {
                    Logger.Debug("the user came back who we previously dl from " + username);
                    //retry all failed downloads from them..
                    List<TransferItem> items = TransfersFragment.TransferItemManagerDL.GetTransferItemsFromUser(username, true, true);
                    if (items.Count == 0)
                    {
                        //no offline, then remove this user.
                        lock (TransfersFragment.UsersWhereDownloadFailedDueToOffline)
                        {
                            TransfersFragment.UsersWhereDownloadFailedDueToOffline.Remove(username);
                        }
                    }
                    else
                    {
                        try
                        {
                            TransfersFragment.DownloadRetryAllConditionLogic(false, false, null, true, items);
                        }
                        catch (Exception e)
                        {
                            Logger.Debug("ProcessPotentialUserOfflineChangedEvent" + e.Message);
                        }
                    }

                }
            }
        }
    }
}