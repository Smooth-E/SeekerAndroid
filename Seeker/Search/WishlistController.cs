﻿using Seeker.Utils;
using Seeker.Helpers;
using Android.App;
using Android.Content;
using AndroidX.Core.App;
using Soulseek;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Seeker.Main;

namespace Seeker.Search
{
    // TODOORG controllers
    public class WishlistController
    {
        private static int searchIntervalMilliseconds = -1;
        public static int SearchIntervalMilliseconds
        {
            get
            {
                return searchIntervalMilliseconds;
            }
            set
            {
                searchIntervalMilliseconds = value; //inverval of 0 means NOT ALLOWED.  In general we should also do a Min value of like 1 minute in case the server sends something not good.
                if (!IsInitialized)
                {
                    Initialize();
                }
            }
        }
        public static bool IsInitialized = false;

        private static System.Timers.Timer WishlistTimer = null;
        public static System.Collections.Concurrent.ConcurrentDictionary<int, HashSet<SearchResponse>> OldResultsToCompare = new System.Collections.Concurrent.ConcurrentDictionary<int, HashSet<SearchResponse>>();
        private static System.Collections.Concurrent.ConcurrentDictionary<int, int> OldNumResults = new System.Collections.Concurrent.ConcurrentDictionary<int, int>();

        public static void Initialize() //we need the wishlist interval before we can init
        {
            if (IsInitialized)
            {
                return;
            }
            if (searchIntervalMilliseconds == 0)
            {
                IsInitialized = true;
                Logger.FirebaseDebug("Wishlist not allowed");
                return;
            }
            if (searchIntervalMilliseconds == -1)
            {
                IsInitialized = true;
                Logger.FirebaseDebug("Wishlist interval is -1");
                return;
            }
            if (searchIntervalMilliseconds < 1000 * 60 * 2)
            {
                Logger.FirebaseDebug("Wishlist interval is: " + searchIntervalMilliseconds);
                searchIntervalMilliseconds = 2 * 60 * 1000; //min of 2 mins...
            }

#if DEBUG
//            searchIntervalMilliseconds = 1000 * 30; //turn off for now...
#endif

            WishlistTimer = new System.Timers.Timer(searchIntervalMilliseconds);
            WishlistTimer.AutoReset = true;
            WishlistTimer.Elapsed += WishlistTimer_Elapsed;
            WishlistTimer.Start();
            IsInitialized = true;
        }
        public const string CHANNEL_ID = "Wishlist Controller ID";
        public const string CHANNEL_NAME = "Wishlists";
        public const string FromWishlistString = "FromWishlistTabID";
        public const string FromWishlistStringID = "FromWishlistTabIDToGoTo";
        public static void SearchCompleted(int id)
        {
            //a search that we initiated completed...
            //var newResponses = SearchTabHelper.SearchTabCollection[id].SearchResponses.ToList();
            //var differenceNewResults = newResponses.Except(OldResultsToCompare[id],new SearchResponseComparer(SeekerState.HideLockedResultsInSearch)).ToList();
            OldResultsToCompare.TryRemove(id, out _); //save memory. wont always exist if the tab got deleted during the search.  exceptions thrown here dont crash anything tho.
            int newUniqueResults = SearchTabHelper.SearchTabCollection[id].SearchResponses.Count - OldNumResults[id];

            if (newUniqueResults >= 1)
            {
                SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                {
                    try
                    {
                        string description = string.Empty;
                        if (newUniqueResults > 1)
                        {
                            description = newUniqueResults + " " + SeekerState.ActiveActivityRef.GetString(Resource.String.new_results);
                        }
                        else
                        {
                            description = newUniqueResults + " " + SeekerState.ActiveActivityRef.GetString(Resource.String.new_result);
                        }
                        string lastTerm = SearchTabHelper.SearchTabCollection[id].LastSearchTerm;

                        CommonHelpers.CreateNotificationChannel(SeekerState.ActiveActivityRef, CHANNEL_ID, CHANNEL_NAME, NotificationImportance.High); //only high will "peek"
                        Intent notifIntent = new Intent(SeekerState.ActiveActivityRef, typeof(MainActivity));
                        notifIntent.AddFlags(ActivityFlags.SingleTop | ActivityFlags.ReorderToFront); //otherwise if another activity is in front then this intent will do nothing...
                        notifIntent.PutExtra(FromWishlistString, 1); //the tab to go to
                        notifIntent.PutExtra(FromWishlistStringID, id); //the tab to go to
                        PendingIntent pendingIntent =
                            PendingIntent.GetActivity(SeekerState.ActiveActivityRef, lastTerm.GetHashCode(), notifIntent, CommonHelpers.AppendMutabilityIfApplicable(PendingIntentFlags.UpdateCurrent, true));
                        Notification n = CommonHelpers.CreateNotification(SeekerState.ActiveActivityRef, pendingIntent, CHANNEL_ID, SeekerState.ActiveActivityRef.GetString(Resource.String.wishlist) + ": " + lastTerm, description, false);
                        NotificationManagerCompat notificationManager = NotificationManagerCompat.From(SeekerState.ActiveActivityRef);
                        // notificationId is a unique int for each notification that you must define
                        notificationManager.Notify(lastTerm.GetHashCode(), n);
                    }
                    catch (System.Exception e)
                    {
                        Logger.FirebaseDebug("ShowNotification For Wishlist failed: " + e.Message + e.StackTrace);
                    }
                });
            }
            SearchTabHelper.SaveHeadersToSharedPrefs();
            SearchTabHelper.SaveSearchResultsToDisk(id, SeekerState.ActiveActivityRef);
        }

        private static void WishlistTimer_Elapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (SearchTabHelper.SearchTabCollection != null)
            {
                var wishlistPairs = SearchTabHelper.SearchTabCollection.Where(pair => pair.Value.SearchTarget == SearchTarget.Wishlist);
                if (wishlistPairs.Count() == 0)
                {
                    return;
                }
                else
                {
                    Logger.FirebaseInfo("wishlist search ran " + searchIntervalMilliseconds);
                    DateTime oldest = DateTime.MaxValue;
                    int oldestId = int.MaxValue;
                    foreach (var pair in wishlistPairs)
                    {
                        if (pair.Value.LastRanTime < oldest)
                        {
                            oldest = pair.Value.LastRanTime;
                            oldestId = pair.Key;
                        }
                    }
                    //oldestId is the one we want to autosearch

                    //search response count: 4220 hashSet count: 1000 time 102 ms
                    //search response count: 13880 hashSet count: 1000 time 71 ms
                    //search response count: 14655 hashSet count: 1000 time 174 ms
                    //search response count: 2615 hashSet count: 999 time 21 ms
                    //search response count: 15758 hashSet count: 1000 time 124 ms
                    //search response count: 937 hashSet count: 937 time 4 ms

                    //this is incase someone is privileged (searching every 2 mins) and perhaps they only have 1 wishlist search.  we dont want the second to begin when the first hasnt even ended.
                    //there arent really any downsides if this happens actually....

                    if (!SearchTabHelper.SearchTabCollection[oldestId].IsLoaded())
                    {
                        SearchTabHelper.RestoreSearchResultsFromDisk(oldestId, SeekerState.ActiveActivityRef);
                    }


                    if (!OldResultsToCompare.ContainsKey(oldestId)) //this is better than setting currentlySearching bc currentlySearching changes UI components like the transition drawable, which I think is just too much happening for the user.
                    {
#if DEBUG
                        var sw = System.Diagnostics.Stopwatch.StartNew();
                        sw.Start();
#endif
                        OldNumResults[oldestId] = SearchTabHelper.SearchTabCollection[oldestId].SearchResponses.Count;
                        OldResultsToCompare[oldestId] = SearchTabHelper.SearchTabCollection[oldestId].SearchResponses.ToHashSet(new SearchResponseComparer(SeekerState.HideLockedResultsInSearch.Value));
#if DEBUG
                        sw.Stop();
                        Logger.Debug($"search response count: " +
                                     $"{SearchTabHelper.SearchTabCollection[oldestId].SearchResponses.Count}" +
                                     $" hashSet count: {OldResultsToCompare[oldestId].Count} " +
                                     $"time {sw.ElapsedMilliseconds} ms");
                        
                        Logger.Debug("now searching " + oldestId);
#endif
                        SearchFragment.SearchAPI(new CancellationTokenSource().Token, null, SearchTabHelper.SearchTabCollection[oldestId].LastSearchTerm, oldestId, true);
                    }
                    else
                    {
                        Logger.Debug("was already searching " + oldestId);
                    }

                }
            }
        }
    }


}