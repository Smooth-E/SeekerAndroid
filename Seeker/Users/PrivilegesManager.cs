using Android.Content;
using Android.Widget;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using _Microsoft.Android.Resource.Designer;
using Seeker.Main;
using Seeker.Utils;

namespace Seeker.Managers
{
    public class PrivilegesManager
    {
        public static object PrivilegedUsersLock = new object();
        private static PrivilegesManager _instance;
        
        public IReadOnlyCollection<string> PrivilegedUsers = null;
        public bool IsPrivileged = false; //are we privileged

        private Context _context;
        
        public static PrivilegesManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    throw new NullReferenceException("PrivilegesManager was not initialized.");
                }

                return _instance;
            }
        }

        public static void Initialize(Context context)
        {
            _instance = new PrivilegesManager
            {
                _context = context
            };
        }
        
        /// <summary>
        /// Set Privileged Users List, this will also check if we have privileges and if so, get our remaining time..
        /// </summary>
        /// <param name="privUsers"></param>
        public void SetPrivilegedList(IReadOnlyCollection<string> privUsers)
        {
            lock (PrivilegedUsersLock)
            {
                PrivilegedUsers = privUsers;
                if (SeekerState.Username != null && SeekerState.Username != string.Empty)
                {
                    IsPrivileged = CheckIfPrivileged(SeekerState.Username);
                    if (IsPrivileged)
                    {
                        GetPrivilegesAPI(false);
                    }
                }
            }
        }

        public void SubtractDays(int days)
        {
            SecondsRemainingAtLastCheck -= (days * 24 * 3600);
        }

        private volatile int SecondsRemainingAtLastCheck = int.MinValue;
        private DateTime LastCheckTime = DateTime.MinValue;
        public int GetRemainingSeconds()
        {
            if (SecondsRemainingAtLastCheck == 0 || SecondsRemainingAtLastCheck == int.MinValue)
            {
                return 0;
            }
            else if (LastCheckTime == DateTime.MinValue)
            {
                //shouldnt go here
                return 0;
            }
            else
            {
                int secondsSinceLastCheck = (int)Math.Floor(LastCheckTime.Subtract(DateTime.UtcNow).TotalSeconds);
                int remainingSeconds = SecondsRemainingAtLastCheck - secondsSinceLastCheck;
                return Math.Max(remainingSeconds, 0);
            }
        }

        /// <summary>
        /// Get Remaining Days (rounded down)
        /// </summary>
        /// <returns></returns>
        public int GetRemainingDays()
        {
            return GetRemainingSeconds() / (24 * 3600);
        }

        public string GetPrivilegeStatus()
        {
            if (SecondsRemainingAtLastCheck == 0 || SecondsRemainingAtLastCheck == int.MinValue || GetRemainingSeconds() <= 0)
            {
                // this is if we are in the privileged list but our actual amount has not yet been returned.
                var stringId = IsPrivileged ? ResourceConstant.String.yes : ResourceConstant.String.no_image_chosen;
                return _context.GetString(stringId);
            }

            var seconds = GetRemainingSeconds();
            switch (seconds)
            {
                case > 3600 * 24:
                {
                    var days = seconds / (3600 * 24);
                    var stringId =  days == 1 
                        ? ResourceConstant.String.day_left
                        : ResourceConstant.String.days_left;
                    var rawString = _context.GetString(stringId);
                    return string.Format(rawString, days);
                }
                case > 3600:
                {
                    var hours = seconds / 3600;
                    return string.Format(hours == 1 
                        ? _context.GetString(ResourceConstant.String.hour_left) 
                        : _context.GetString(ResourceConstant.String.hours_left), hours);
                }
                case > 60:
                {
                    var mins = seconds / 60;
                    return string.Format(mins == 1 
                        ? _context.GetString(ResourceConstant.String.minute_left) 
                        : _context.GetString(ResourceConstant.String.minutes_left), mins);
                }
                case 1:
                    return string.Format(_context.GetString(ResourceConstant.String.second_left), seconds);
                default:
                    return string.Format(_context.GetString(ResourceConstant.String.seconds_left), seconds);
            }
        }

        public EventHandler PrivilegesChecked;

        private void GetPrivilegesLogic(bool feedback)
        {
            SeekerState.SoulseekClient.GetPrivilegesAsync().ContinueWith(new Action<Task<int>>
                ((Task<int> t) =>
                {
                    if (t.IsFaulted)
                    {
                        if (feedback)
                        {
                            if (t.Exception.InnerException is TimeoutException)
                            {
                                _context.ShowLongToast(
                                    _context.GetString(ResourceConstant.String.priv_failed) + ": " 
                                    + _context.GetString(ResourceConstant.String.timeout));
                            }
                            else
                            {
                                Logger.FirebaseDebug("Failed to get privileges" + t.Exception.InnerException?.Message);
                                _context.ShowLongToast(_context.GetString(ResourceConstant.String.priv_failed));
                            }
                        }
                        
                        return;
                    }

                    SecondsRemainingAtLastCheck = t.Result;
                    IsPrivileged = t.Result switch
                    {
                        > 0 => true,
                        0 => false,
                        _ => IsPrivileged
                    };
                    
                    LastCheckTime = DateTime.UtcNow;
                    if (feedback)
                    {
                        _context.ShowLongToast(_context.GetString(ResourceConstant.String.priv_success)
                                              + ". " + _context.GetString(ResourceConstant.String.status) + ": "
                                              + GetPrivilegeStatus());
                    }
                    PrivilegesChecked?.Invoke(null, EventArgs.Empty);
                }));
        }

        public void GetPrivilegesAPI(bool feedback)
        {
            if (!SeekerState.currentlyLoggedIn)
            {
                Toast.MakeText(SeekerState.ActiveActivityRef, Resource.String.must_be_logged_in_to_check_privileges, ToastLength.Short).Show();
                return;
            }
            if (SeekerState.CurrentlyLoggedInButDisconnectedState())
            {
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

                            Toast.MakeText(SeekerState.ActiveActivityRef, Resource.String.failed_to_connect, ToastLength.Short).Show();

                        });
                        return;
                    }
                    SeekerState.ActiveActivityRef.RunOnUiThread(() => { GetPrivilegesLogic(feedback); });

                }));
            }
            else
            {
                GetPrivilegesLogic(feedback);
            }
        }

        public bool CheckIfPrivileged(string username)
        {
            lock (PrivilegedUsersLock)
            {
                if (PrivilegedUsers != null)
                {
                    return PrivilegedUsers.Contains(username);
                }
                return false;
            }
        }
    }

}