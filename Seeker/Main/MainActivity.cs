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

using Seeker.Extensions.SearchResponseExtensions;
using Seeker.Helpers;
using Seeker.Search;
using Android;
using Android.Animation;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Provider;
using Android.Runtime;
using Android.Util;
using Android.Views;
using Android.Widget;
using AndroidX.AppCompat.App;
using AndroidX.Core.App;
using AndroidX.Core.Content;
using AndroidX.DocumentFile.Provider;
using AndroidX.Fragment.App;
using AndroidX.Lifecycle;
using AndroidX.ViewPager.Widget;
using Common;
using Google.Android.Material.BottomNavigation;
using Google.Android.Material.Snackbar;
using Google.Android.Material.Tabs;
using Java.IO;
using SlskHelp;
using Soulseek;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Runtime.Serialization.Formatters.Binary;
using System.Threading;
using System.Threading.Tasks;
using static Android.Provider.DocumentsContract;
using log = Android.Util.Log;
using Seeker.Serialization;
using AndroidX.Activity;
using Seeker.Transfers;
using Seeker.Exceptions;
using Seeker.Managers;
using Seeker.Models;
using Seeker.Utils;
using Seeker.Components;

// using System.IO;
// readme:
// dotnet add package Soulseek --version 1.0.0-rc3.1
// xamarin
// Had to rewrite this one from .csproj
// <Import Project="C:\Program Files (x86)\Microsoft Visual Studio\2019\Community\MSBuild
// \Xamarin\Android\Xamarin.Android.CSharp.targets" />
namespace Seeker
{
    // WindowSoftInputMode = SoftInput.StateAlwaysHidden) didn't change anything...
    [Activity(Label = "@string/app_name", Theme = "@style/AppTheme.NoActionBar", MainLauncher = true, Exported = true)]
    public class MainActivity :
        ThemeableActivity,
        ActivityCompat.IOnRequestPermissionsResultCallback,
        BottomNavigationView.IOnNavigationItemSelectedListener
    {
        public static object SHARED_PREF_LOCK = new object();
        public const string logCatTag = "seeker";
        
        public static void createNotificationChannel(Context c, string id, string name)
        {
            if (Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
            {
                NotificationChannel serviceChannel = new NotificationChannel(
                    id,
                    name,
                    Android.App.NotificationImportance.Low
                );
                NotificationManager manager = c.GetSystemService(Context.NotificationService) as NotificationManager;
                manager.CreateNotificationChannel(serviceChannel);
            }
        }

        public void KeyboardChanged(object sender, bool isShown)
        {
            var bottomNavigationView =
                SeekerState.MainActivityRef.FindViewById<BottomNavigationView>(Resource.Id.navigation);

            if (isShown)
            {
                bottomNavigationView
                    .Animate()
                    .Alpha(0f)
                    .SetDuration(250)
                    .SetListener(new BottomNavigationViewAnimationListener());
            }
            else
            {
                bottomNavigationView.Visibility = ViewStates.Visible;
                bottomNavigationView
                    .Animate()
                    .Alpha(1f)
                    .SetDuration(300)
                    .SetListener(null);
            }
        }
        
        public static event EventHandler<TransferItem> TransferAddedUINotify;

        public static event EventHandler<DownloadAddedEventArgs> DownloadAddedUINotify;

        public static void InvokeDownloadAddedUINotify(DownloadAddedEventArgs e)
        {
            DownloadAddedUINotify?.Invoke(null, e);
        }

        public static void ClearDownloadAddedEventsFromTarget(object target)
        {
            if (DownloadAddedUINotify == null)
            {
                return;
            }
            else
            {
                foreach (Delegate d in DownloadAddedUINotify.GetInvocationList())
                {
                    if (d.Target == null) //i.e. static
                    {
                        continue;
                    }

                    if (d.Target.GetType() == target.GetType())
                    {
                        DownloadAddedUINotify -= (EventHandler<DownloadAddedEventArgs>)d;
                    }
                }
            }
        }

        private List<string> GetRootDirs(DocumentFile dir)
        {
            List<string> dirUris = new List<string>();
            DocumentFile[] files = dir.ListFiles(); // doesn't need to be sorted

            for (int i = 0; i < files.Length; ++i)
            {
                DocumentFile file = files[i];
                if (file.IsDirectory)
                {
                    dirUris.Add(file.Uri.ToString());
                }
            }

            return dirUris;
        }

        private static Soulseek.Directory SlskDirFromUri(
            ContentResolver contentResolver,
            Android.Net.Uri rootUri,
            Android.Net.Uri dirUri,
            string dirToStrip,
            bool diagFromDirectoryResolver,
            string volumePath)
        {
            // on the emulator this is /tree/downloads/document/docwonlowds but the dirToStrip is uppercase Downloads
            string directoryPath = dirUri.LastPathSegment;
            directoryPath = directoryPath.Replace("/", @"\");

            var documentId = DocumentsContract.GetDocumentId(dirUri);
            Android.Net.Uri listChildrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(rootUri, documentId);

            var cursorData = new String[]
            {
                Document.ColumnDocumentId,
                Document.ColumnDisplayName,
                Document.ColumnMimeType,
                Document.ColumnSize
            };
            Android.Database.ICursor c = contentResolver.Query(listChildrenUri, cursorData, null, null, null);

            List<Soulseek.File> files = new List<Soulseek.File>();

            try
            {
                while (c.MoveToNext())
                {
                    string docId = c.GetString(0);
                    string name = c.GetString(1);
                    string mime = c.GetString(2);
                    long size = c.GetLong(3);
                    var childUri = DocumentsContract.BuildDocumentUri(rootUri.Authority, docId);

                    if (!StorageUtils.isDirectory((mime)))
                    {

                        string fname = CommonHelpers.GetFileNameFromFile(childUri.Path.Replace("/", @"\"));
                        string folderName = Common.Helpers.GetFolderNameFromFile(childUri.Path.Replace("/", @"\"));

                        // for the brose response should only be the filename!!! 
                        // when a user tries to download something from a browse resonse,
                        // the soulseek client on their end must create a fully qualified path for us
                        // bc we get a path that is:
                        // "Soulseek Complete\\document\\primary:Pictures\\Soulseek Complete\\(2009.09.23) Sufjan Stevens - Live from Castaways\\09 Between Songs 4.mp3"
                        // not quite a full URI but it does add quite a bit..
                        string searchableName = fname;

                        var pathExtension = System.IO.Path.GetExtension(childUri.Path);
                        var slskFile = new Soulseek.File(1, searchableName.Replace("/", @"\"), size, pathExtension);
                        files.Add(slskFile);
                    }
                }
            }
            catch (Exception e)
            {
                Logger.Debug("Parse error with " + dirUri.Path + e.Message + e.StackTrace);
                Logger.FirebaseDebug("Parse error with " + dirUri.Path + e.Message + e.StackTrace);
            }
            finally
            {
                c.closeQuietly();
            }

            CommonHelpers.SortSlskDirFiles(files); // otherwise our browse response files will be way out of order

            if (volumePath != null)
            {
                if (directoryPath.Substring(0, volumePath.Length) == volumePath)
                {
                    directoryPath = directoryPath.Substring(volumePath.Length);
                }
            }

            var slskDir = new Soulseek.Directory(directoryPath, files);
            return slskDir;
        }

        /// <summary>
        /// We only use this in Contents Response Resolver.
        /// </summary>
        /// <param name="dirFile"></param>
        /// <param name="dirToStrip"></param>
        /// <param name="diagFromDirectoryResolver"></param>
        /// <param name="volumePath"></param>
        /// <returns></returns>
        private static Soulseek.Directory SlskDirFromDocumentFile(DocumentFile dirFile, bool diagFromDirectoryResolver,
            string volumePath)
        {
            // on the emulator this is /tree/downloads/document/docwonlowds but the dirToStrip is uppercase Downloads
            string directoryPath = dirFile.Uri.LastPathSegment;
            directoryPath = directoryPath.Replace("/", @"\");

            List<Soulseek.File> files = new List<Soulseek.File>();
            foreach (DocumentFile f in dirFile.ListFiles())
            {
                if (f.IsDirectory)
                {
                    continue;
                }

                try
                {
                    string fname = null;
                    string searchableName = null;

                    var isDocumentsAuthority = dirFile.Uri.Authority == "com.android.providers.downloads.documents";
                    if (isDocumentsAuthority && !f.Uri.Path.Contains(dirFile.Uri.Path))
                    {
                        //msd, msf case
                        fname = f.Name;
                        searchableName = fname; // for the brose response should only be the filename!!! 
                    }
                    else
                    {
                        fname = CommonHelpers.GetFileNameFromFile(f.Uri.Path.Replace("/", @"\"));
                        searchableName = fname; // for the brose response should only be the filename!!! 
                    }
                    // when a user tries to download something from a browse resonse,
                    // the soulseek client on their end must create a fully qualified path for us
                    // bc we get a path that is:
                    // "Soulseek Complete\\document\\primary:Pictures\\Soulseek Complete\\(2009.09.23) Sufjan Stevens - Live from Castaways\\09 Between Songs 4.mp3"
                    // not quite a full URI but it does add quite a bit...

                    var pathExtension = System.IO.Path.GetExtension(f.Uri.Path);
                    var slskFile = new Soulseek.File(1, searchableName.Replace("/", @"\"), f.Length(), pathExtension);
                    files.Add(slskFile);
                }
                catch (Exception e)
                {
                    Logger.Debug("Parse error with " + f.Uri.Path + e.Message + e.StackTrace);
                    Logger.FirebaseDebug("Parse error with " + f.Uri.Path + e.Message + e.StackTrace);
                }

            }

            CommonHelpers.SortSlskDirFiles(files); // otherwise our browse response files will be way out of order

            if (volumePath != null && directoryPath.Substring(0, volumePath.Length) == volumePath)
            {
                directoryPath = directoryPath.Substring(volumePath.Length);
            }

            var slskDir = new Soulseek.Directory(directoryPath, files);
            return slskDir;
        }

        // TODO: Move these methods into the CacheParseResults class

        public static void ClearLegacyParsedCacheResults()
        {
            try
            {
                lock (SHARED_PREF_LOCK)
                {
                    var editor = SeekerState.SharedPreferences.Edit();
                    editor.Remove(KeyConsts.M_CACHE_stringUriPairs);
                    editor.Remove(KeyConsts.M_CACHE_browseResponse);
                    editor.Remove(KeyConsts.M_CACHE_friendlyDirNameToUriMapping);
                    editor.Remove(KeyConsts.M_CACHE_auxDupList);
                    editor.Remove(KeyConsts.M_CACHE_stringUriPairs_v2);
                    editor.Remove(KeyConsts.M_CACHE_stringUriPairs_v3);
                    editor.Remove(KeyConsts.M_CACHE_browseResponse_v2);
                    editor.Remove(KeyConsts.M_CACHE_friendlyDirNameToUriMapping_v2);
                    editor.Remove(KeyConsts.M_CACHE_tokenIndex_v2);
                    editor.Remove(KeyConsts.M_CACHE_intHelperIndex_v2);
                    editor.Commit();
                }
            }
            catch (Exception e)
            {
                Logger.Debug("ClearParsedCacheResults " + e.Message + e.StackTrace);
                Logger.FirebaseDebug("ClearParsedCacheResults " + e.Message + e.StackTrace);
            }
        }


        public static CachedParseResults GetLegacyCachedParseResult()
        {
#if !BinaryFormatterAvailable
            return null;
#else
            bool convertFrom2to3 = false;

            string s_stringUriPairs = 
                SeekerState.SharedPreferences.GetString(KeyConsts.M_CACHE_stringUriPairs_v3, string.Empty);

            if (s_stringUriPairs == string.Empty)
            {
                s_stringUriPairs = 
                    SeekerState.SharedPreferences.GetString(KeyConsts.M_CACHE_stringUriPairs_v2, string.Empty);

                convertFrom2to3 = true;
            }

            string s_BrowseResponse = 
                SeekerState.SharedPreferences.GetString(KeyConsts.M_CACHE_browseResponse_v2, string.Empty);

            string s_FriendlyDirNameMapping = 
                SeekerState.SharedPreferences.GetString(KeyConsts.M_CACHE_friendlyDirNameToUriMapping_v2, string.Empty);

            string s_intHelperIndex = 
                SeekerState.SharedPreferences.GetString(KeyConsts.M_CACHE_intHelperIndex_v2, string.Empty);

            int nonHiddenFileCount = SeekerState.SharedPreferences.GetInt(KeyConsts.M_CACHE_nonHiddenFileCount_v3, -1);

            string s_tokenIndex =
 SeekerState.SharedPreferences.GetString(KeyConsts.M_CACHE_tokenIndex_v2, string.Empty);

            // this one can be empty.
            string s_BrowseResponse_hiddenPortion = 
                SeekerState.SharedPreferences.GetString(KeyConsts.M_CACHE_browseResponse_hidden_portion, string.Empty);

            if (s_intHelperIndex == string.Empty 
                || s_tokenIndex == string.Empty
                || s_stringUriPairs == string.Empty
                || s_BrowseResponse == string.Empty 
                || s_FriendlyDirNameMapping == string.Empty)
            {
                return null;
            }
            
            // deserialize...
            try
            {
                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();

                byte[] b_stringUriPairs = Convert.FromBase64String(s_stringUriPairs);
                byte[] b_BrowseResponse = Convert.FromBase64String(s_BrowseResponse);
                byte[] b_FriendlyDirNameMapping = Convert.FromBase64String(s_FriendlyDirNameMapping);
                byte[] b_intHelperIndex = Convert.FromBase64String(s_intHelperIndex);
                byte[] b_tokenIndex = Convert.FromBase64String(s_tokenIndex);

                using (System.IO.MemoryStream m_stringUriPairs = new(b_stringUriPairs))
                using (System.IO.MemoryStream m_BrowseResponse = new(b_BrowseResponse))
                using (System.IO.MemoryStream m_FriendlyDirNameMapping = new(b_FriendlyDirNameMapping))
                using (System.IO.MemoryStream m_intHelperIndex = new(b_intHelperIndex))
                using (System.IO.MemoryStream m_tokenIndex = new(b_tokenIndex))
                {
                    BinaryFormatter binaryFormatter = SerializationHelper.GetLegacyBinaryFormatter();
                    CachedParseResults cachedParseResults = new CachedParseResults();
                    if (convertFrom2to3)
                    {
                        Logger.Debug("convert from v2 to v3");

                        var oldKeys = binaryFormatter.Deserialize(m_stringUriPairs) 
                                as Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>>>;

                        var newKeys = 
                                new Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>>();

                        if (oldKeys != null)
                        {
                            foreach (var oldkeyvaluepair in oldKeys)
                            {
                                newKeys.Add(oldkeyvaluepair.Key, 
                                    new Tuple<long, string, Tuple<int, int, int, int>, bool, bool>(
                                        oldkeyvaluepair.Value.Item1, 
                                        oldkeyvaluepair.Value.Item2, 
                                        oldkeyvaluepair.Value.Item3, 
                                        false, 
                                        false
                                    )
                                );
                            }
                        }

                        lock (SHARED_PREF_LOCK)
                        {
                            var editor = SeekerState.SharedPreferences.Edit();
                            editor.PutString(KeyConsts.M_CACHE_stringUriPairs_v2, string.Empty);

                            using (System.IO.MemoryStream bstringUrimemoryStreamv3 = new System.IO.MemoryStream())
                            {
                                BinaryFormatter formatter = SerializationHelper.GetLegacyBinaryFormatter();
                                formatter.Serialize(bstringUrimemoryStreamv3, newKeys);
                                var streamArray = bstringUrimemoryStreamv3.ToArray();
                                string stringUrimemoryStreamv3 = Convert.ToBase64String(streamArray);

                                editor.PutString(KeyConsts.M_CACHE_stringUriPairs_v3, stringUrimemoryStreamv3);
                                editor.Commit();
                            }
                        }
                        cachedParseResults.keys = newKeys;
                    }
                    else
                    {
                        Logger.Debug("v3");
                        cachedParseResults.keys = binaryFormatter.Deserialize(m_stringUriPairs) 
                            as Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>>;
                    }


                    cachedParseResults.browseResponse = binaryFormatter.Deserialize(m_BrowseResponse) as BrowseResponse;


                    if (!string.IsNullOrEmpty(s_BrowseResponse_hiddenPortion))
                    {
                        byte[] b_BrowseResponse_hiddenPortion =
 Convert.FromBase64String(s_BrowseResponse_hiddenPortion);

                        // TODO: Consider adding a using directive for System.IO.MemoryStream to make this prettier
                        using (System.IO.MemoryStream m_BrowseResponse_hiddenPortion =
 new(b_BrowseResponse_hiddenPortion))
                        {
                            cachedParseResults.browseResponseHiddenPortion = 
                                binaryFormatter.Deserialize(m_BrowseResponse_hiddenPortion) as List<Soulseek.Directory>;
                        }
                    }
                    else
                    {
                        cachedParseResults.browseResponseHiddenPortion = null;
                    }


                    cachedParseResults.friendlyDirNameToUriMapping = 
                        binaryFormatter.Deserialize(m_FriendlyDirNameMapping) as List<Tuple<string, string>>;

                    cachedParseResults.directoryCount = cachedParseResults.browseResponse.DirectoryCount;

                    cachedParseResults.helperIndex = 
                        binaryFormatter.Deserialize(m_intHelperIndex) as Dictionary<int, string>;

                    cachedParseResults.tokenIndex = 
                        binaryFormatter.Deserialize(m_tokenIndex) as Dictionary<string, List<int>>;

                    cachedParseResults.nonHiddenFileCount = nonHiddenFileCount;

                    if (cachedParseResults.keys == null 
                        || cachedParseResults.browseResponse == null 
                        || cachedParseResults.friendlyDirNameToUriMapping == null 
                        || cachedParseResults.helperIndex == null 
                        || cachedParseResults.tokenIndex == null)
                    {
                        return null;
                    }

                    sw.Stop();
                    Logger.Debug("time to deserialize all sharing helpers: " + sw.ElapsedMilliseconds);

                    return cachedParseResults;
                }

            }
            catch (Exception e)
            {
                Logger.Debug("error deserializing" + e.Message + e.StackTrace);
                Logger.FirebaseDebug("error deserializing" + e.Message + e.StackTrace);
                return null;
            }
#endif
        }

        /// <summary>
        /// Check Cache should be false if setting a new dir.. true if on startup.
        /// </summary>
        /// <param name="dir"></param>
        /// <param name="checkCache"></param>
        public static bool InitializeDatabase(UploadDirectoryInfo newlyAddedDirectoryIfApplicable,
            bool checkCache, out string errorMsg)
        {
            errorMsg = string.Empty;
            bool success = false;
            try
            {
                CachedParseResults cachedParseResults = null;
                if (checkCache)
                {
                    // migrate if applicable
                    cachedParseResults = GetLegacyCachedParseResult();
                    if (cachedParseResults != null)
                    {
                        StoreCachedParseResults(SeekerState.ActiveActivityRef, cachedParseResults);
                        ClearLegacyParsedCacheResults();
                    }

                    cachedParseResults = GetCachedParseResults(SeekerState.ActiveActivityRef);
                }

                if (cachedParseResults == null)
                {
                    System.Diagnostics.Stopwatch s = new System.Diagnostics.Stopwatch();
                    s.Start();
                    Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>> keys = null;
                    BrowseResponse browseResponse = null;
                    List<Tuple<string, string>> dirMappingFriendlyNameToUri = null;
                    List<Soulseek.Directory> hiddenDirectories = null;
                    Dictionary<int, string> helperIndex = null;
                    int directoryCount = 0;


                    // optimization
                    // - if new directory is a subdir we can skip this part.
                    // !!!! but we still have things to do like make all files
                    // that start with said presentableDir to be locked / hidden. etc.

                    UploadDirectoryManager.UpdateWithDocumentFileAndErrorStates();
                    if (UploadDirectoryManager.AreAllFailed())
                    {
                        throw new DirectoryAccessFailure("All Failed");
                    }

                    if (SeekerState.PreOpenDocumentTree() || UploadDirectoryManager.AreAnyFromLegacy())
                    {
                        keys = StorageUtils.ParseSharedDirectoryLegacy(
                            null,
                            SeekerState.SharedFileCache?.FullInfo,
                            ref directoryCount,
                            out browseResponse,
                            out dirMappingFriendlyNameToUri,
                            out helperIndex,
                            out hiddenDirectories
                        );
                    }
                    else
                    {
                        keys = StorageUtils.ParseSharedDirectoryFastDocContract(
                            null,
                            SeekerState.SharedFileCache?.FullInfo,
                            ref directoryCount,
                            out browseResponse,
                            out dirMappingFriendlyNameToUri,
                            out helperIndex,
                            out hiddenDirectories
                        );
                    }

                    int nonHiddenCountForServer = keys.Count(pair1 => !pair1.Value.Item5);
                    Logger.Debug($"Non Hidden Count for Server: {nonHiddenCountForServer}");

                    SeekerState.NumberParsed = int.MaxValue; // our signal that we are finishing up...
                    s.Stop();

                    Logger.Debug(string.Format("{0} Files parsed in {1} milliseconds",
                        keys.Keys.Count, s.ElapsedMilliseconds));

                    s.Reset();

                    s.Start();

                    Dictionary<string, List<int>> tokenIndex = new Dictionary<string, List<int>>();
                    var reversed = helperIndex.ToDictionary(x => x.Value, x => x.Key);

                    foreach (string presentableName in keys.Keys)
                    {
                        var folderName = Common.Helpers.GetFolderNameFromFile(presentableName);
                        var extension = System.IO.Path
                            .GetFileNameWithoutExtension(CommonHelpers.GetFileNameFromFile(presentableName));

                        string searchableName = folderName + " " + extension;

                        searchableName = SharedFileCache.MatchSpecialCharAgnostic(searchableName);
                        int code = reversed[presentableName];

                        foreach (string token in searchableName.ToLower().Split(null)) // null means whitespace
                        {
                            if (token == string.Empty)
                            {
                                continue;
                            }

                            if (tokenIndex.ContainsKey(token))
                            {
                                tokenIndex[token].Add(code);
                            }
                            else
                            {
                                tokenIndex[token] = new List<int>();
                                tokenIndex[token].Add(code);
                            }
                        }
                    }

                    s.Stop();

                    Logger.Debug(string.Format("Token index created in {0} milliseconds", s.ElapsedMilliseconds));

                    var newCachedResults = new CachedParseResults(
                        keys,
                        browseResponse.DirectoryCount, // todo?
                        browseResponse,
                        hiddenDirectories,
                        dirMappingFriendlyNameToUri,
                        tokenIndex,
                        helperIndex,
                        nonHiddenCountForServer);
                    StoreCachedParseResults(SeekerState.ActiveActivityRef, newCachedResults);

                    UploadDirectoryManager.SaveToSharedPreferences(SeekerState.SharedPreferences);

                    // TODO: we do not save the directoryCount ?? and so subsequent times its just browseResponse.Count?
                    // would it ever be different?

                    SlskHelp.SharedFileCache sharedFileCache = new SlskHelp.SharedFileCache(
                        keys,
                        directoryCount,
                        browseResponse,
                        dirMappingFriendlyNameToUri,
                        tokenIndex,
                        helperIndex,
                        hiddenDirectories,
                        nonHiddenCountForServer
                    );

                    var argsTuple =
                    (
                        sharedFileCache.DirectoryCount,
                        nonHiddenCountForServer != -1 ? nonHiddenCountForServer : sharedFileCache.FileCount
                    );
                    SharedFileCache_Refreshed(null, argsTuple);
                    SeekerState.SharedFileCache = sharedFileCache;

                    // *********Profiling********* for 2252 files - 13s initial parsing, 1.9 MB total
                    // 2552 Files parsed in 13,161 milliseconds  - if the phone is locked it takes twice as long
                    // Token index created in 370 milliseconds   - if the phone is locked it takes twice as long
                    // Browse Response is 379,963 bytes
                    // File Dictionary is 769,386 bytes
                    // Directory Dictionary is 137,518 bytes
                    // int(helper) index is 258,237 bytes
                    // token index is 393,354 bytes
                    // cache:
                    // time to deserialize all sharing helpers is 664 ms for 2k files...

                    // searching an hour (18,000) worth of terms
                    // linear - 22,765 ms
                    // dictionary based - 27ms

                    // *********Profiling********* for 807 files - 3s initial parsing, .66 MB total
                    // 807 Files parsed in 2,935 milliseconds
                    // Token index created in 182 milliseconds
                    // Browse Response is 114,432 bytes
                    // File Dictionary is 281,610 bytes
                    // Directory Dictionary is 38,250 bytes
                    // int(helper) index is 78,589 bytes
                    // token index is 156,274 bytes

                    // searching an hour (18,000) worth of terms
                    // linear - 6,570 ms
                    // dictionary based - 22ms

                    // *********Profiling********* for 807 files -- deep metadata retreival off.
                    //                                              (i.e. only whats indexed in MediaStore) - 
                    // *********Profiling********* for 807 files -- metadata for flac
                    //                                              and those not in MediaStore - 12,234
                    // *********Profiling********* for 807 files -- mediaretreiver for everything.
                    //                                              metadata for flac
                    //                                              and those not in MediaStore - 38,063
                }
                else
                {
                    Logger.Debug("Using cached results");
                    UploadDirectoryManager.UpdateWithDocumentFileAndErrorStates();

                    if (UploadDirectoryManager.AreAllFailed())
                    {
                        throw new DirectoryAccessFailure("All Failed");
                    }
                    else
                    {
                        SlskHelp.SharedFileCache sharedFileCache = new SlskHelp.SharedFileCache(
                            cachedParseResults.keys, // TODO: new constructor
                            cachedParseResults.directoryCount,
                            cachedParseResults.browseResponse,
                            cachedParseResults.friendlyDirNameToUriMapping,
                            cachedParseResults.tokenIndex,
                            cachedParseResults.helperIndex,
                            cachedParseResults.browseResponseHiddenPortion,
                            cachedParseResults.nonHiddenFileCount
                        );

                        var argsTuple =
                        (
                            sharedFileCache.DirectoryCount,
                            sharedFileCache.GetNonHiddenFileCountForServer() != -1
                                ? sharedFileCache.GetNonHiddenFileCountForServer()
                                : sharedFileCache.FileCount
                        );
                        SharedFileCache_Refreshed(null, argsTuple);
                        SeekerState.SharedFileCache = sharedFileCache;
                    }
                }

                success = true;
                SeekerState.FailedShareParse = false;
                SeekerState.SharedFileCache.SuccessfullyInitialized = true;
            }
            catch (Exception e)
            {
                string defaultUnspecified = "Shared Folder Error - Unspecified Error";
                errorMsg = defaultUnspecified;

                if (e.GetType().FullName == "Java.Lang.SecurityException" || e is Java.Lang.SecurityException)
                {
                    errorMsg = SeekerApplication.GetString(Resource.String.PermissionsIssueShared);
                }

                success = false;
                Logger.Debug("Error parsing files: " + e.Message + e.StackTrace);

                if (e is DirectoryAccessFailure)
                {
                    errorMsg = "Shared Folder Error - " + UploadDirectoryManager.GetCompositeErrorString();
                }
                else
                {
                    Logger.FirebaseDebug("Error parsing files: " + e.Message + e.StackTrace);
                }

                if (e.Message.Contains("An item with the same key"))
                {
                    try
                    {
                        var codePoints = ShowCodePoints(e.Message.Substring(e.Message.Length - 7));
                        Logger.FirebaseDebug("Possible encoding issue: " + codePoints);
                        errorMsg = "Path Conflict. Same Name?";
                    }
                    catch
                    {
                        // just in case
                    }
                }

                if (errorMsg == defaultUnspecified)
                {
                    Logger.FirebaseDebug("Error Parsing Files Unspecified Error" + e.Message + e.StackTrace);
                }
            }
            finally
            {
                if (!success)
                {
                    SeekerState.FailedShareParse = true;

                    // if success if false then SeekerState.SharedFileCache might be null still causing a crash!
                    if (SeekerState.SharedFileCache != null)
                    {
                        SeekerState.SharedFileCache.SuccessfullyInitialized = false;
                    }
                }
            }

            return success;
        }

        static T deserializeFromDisk<T>(
            Context c,
            Java.IO.File dir,
            string filename,
            MessagePack.MessagePackSerializerOptions options = null) where T : class
        {
            Java.IO.File fileForOurInternalStorage = new Java.IO.File(dir, filename);

            if (!fileForOurInternalStorage.Exists())
            {
                return null;
            }

            using (System.IO.Stream inputStream = c.ContentResolver.OpenInputStream(
                       AndroidX.DocumentFile.Provider.DocumentFile.FromFile(fileForOurInternalStorage).Uri))
            {
                return MessagePack.MessagePackSerializer.Deserialize<T>(inputStream, options);
            }
        }

        private static CachedParseResults GetCachedParseResults(Context c)
        {
            Java.IO.File fileshare_dir = new Java.IO.File(c.FilesDir, KeyConsts.M_fileshare_cache_dir);
            if (!fileshare_dir.Exists())
            {
                return null;
            }

            try
            {
                var helperIndex =
                    deserializeFromDisk<Dictionary<int, string>>(c, fileshare_dir, KeyConsts.M_HelperIndex_Filename);

                var tokenIndex = deserializeFromDisk<Dictionary<string, List<int>>>(
                    c,
                    fileshare_dir,
                    KeyConsts.M_TokenIndex_Filename
                );

                var keys =
                    deserializeFromDisk<Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>>>(
                        c,
                        fileshare_dir,
                        KeyConsts.M_Keys_Filename
                    );

                var browseResponse = deserializeFromDisk<BrowseResponse>(
                    c,
                    fileshare_dir,
                    KeyConsts.M_BrowseResponse_Filename,
                    SerializationHelper.BrowseResponseOptions
                );

                var browseResponseHidden = deserializeFromDisk<List<Directory>>(
                    c,
                    fileshare_dir,
                    KeyConsts.M_BrowseResponse_Hidden_Filename,
                    SerializationHelper.BrowseResponseOptions
                );

                var friendlyDirToUri = deserializeFromDisk<List<Tuple<string, string>>>(c, fileshare_dir,
                    KeyConsts.M_FriendlyDirNameToUri_Filename);

                int nonHiddenFileCount =
                    SeekerState.SharedPreferences.GetInt(KeyConsts.M_CACHE_nonHiddenFileCount_v3, -1);

                var cachedParseResults = new CachedParseResults(
                    keys,
                    browseResponse.DirectoryCount, // TODO: This line needs some attention
                    browseResponse,
                    browseResponseHidden,
                    friendlyDirToUri,
                    tokenIndex,
                    helperIndex,
                    nonHiddenFileCount);

                return cachedParseResults;
            }
            catch (Exception e)
            {
                Logger.FirebaseDebug("FAILED to restore sharing parse results: " + e.Message + e.StackTrace);
                return null;
            }
        }

        public static void ClearParsedCacheResults(Context c)
        {
            Java.IO.File fileshare_dir = new Java.IO.File(c.FilesDir, KeyConsts.M_fileshare_cache_dir);
            if (!fileshare_dir.Exists())
            {
                return;
            }

            foreach (var file in fileshare_dir.ListFiles())
            {
                file.Delete();
            }
        }

        private static void StoreCachedParseResults(Context c, CachedParseResults cachedParseResults)
        {
            var sw = System.Diagnostics.Stopwatch.StartNew();

            Java.IO.File fileShareCachedDir = new Java.IO.File(c.FilesDir, KeyConsts.M_fileshare_cache_dir);
            if (!fileShareCachedDir.Exists())
            {
                fileShareCachedDir.Mkdir();
            }

            byte[] data = MessagePack.MessagePackSerializer.Serialize(cachedParseResults.helperIndex);
            CommonHelpers.SaveToDisk(c, data, fileShareCachedDir, KeyConsts.M_HelperIndex_Filename);

            data = MessagePack.MessagePackSerializer.Serialize(cachedParseResults.tokenIndex);
            CommonHelpers.SaveToDisk(c, data, fileShareCachedDir, KeyConsts.M_TokenIndex_Filename);

            data = MessagePack.MessagePackSerializer.Serialize(cachedParseResults.keys); // TODO: directoryCount
            CommonHelpers.SaveToDisk(c, data, fileShareCachedDir, KeyConsts.M_Keys_Filename);

            data = MessagePack.MessagePackSerializer
                .Serialize(cachedParseResults.browseResponse, options: SerializationHelper.BrowseResponseOptions);

            CommonHelpers.SaveToDisk(c, data, fileShareCachedDir, KeyConsts.M_BrowseResponse_Filename);

            data = MessagePack.MessagePackSerializer.Serialize(cachedParseResults.browseResponseHiddenPortion,
                options: SerializationHelper.BrowseResponseOptions);

            CommonHelpers.SaveToDisk(c, data, fileShareCachedDir, KeyConsts.M_BrowseResponse_Hidden_Filename);

            data = MessagePack.MessagePackSerializer.Serialize(cachedParseResults.friendlyDirNameToUriMapping);
            CommonHelpers.SaveToDisk(c, data, fileShareCachedDir, KeyConsts.M_FriendlyDirNameToUri_Filename);

            lock (SHARED_PREF_LOCK)
            {
                var editor = SeekerState.SharedPreferences.Edit();
                editor.PutInt(KeyConsts.M_CACHE_nonHiddenFileCount_v3, cachedParseResults.nonHiddenFileCount);

                // TODO: save upload dirs ---- do this now might as well....

                // before this line ^ ,its possible for the saved UploadDirectoryUri
                // and the actual browse response to be different.
                // this is because upload data uri saves on MainActivity OnPause. and so one could set shared folder
                // and then press home and then swipe up. never having saved uploadirectoryUri.
                editor.Commit();
            }
        }

        private static string ShowCodePoints(string str)
        {
            string codePointString = string.Empty;
            foreach (char c in str)
            {
                codePointString = codePointString + ($"_{(int)c:x4}");
            }

            return codePointString;
        }

        private void SoulseekClient_SearchResponseDeliveryFailed(object sender, SearchRequestResponseEventArgs e)
        {
            // Intentional no-op
        }

        private void SoulseekClient_SearchResponseDelivered(object sender, SearchRequestResponseEventArgs e)
        {
            // Intentional no-op
        }

        public static void SharedFileCache_Refreshed(object sender, (int Directories, int Files) e)
        {
            if (!SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.LoggedIn))
            {
                SeekerState.NumberOfSharedDirectoriesIsStale = true;
                return;
            }

            SeekerState.SoulseekClient.SetSharedCountsAsync(e.Directories, e.Files);
            SeekerState.NumberOfSharedDirectoriesIsStale = false;
        }

        /// <summary>
        /// Inform server the number of files we are sharing or 0,0 if not sharing...
        /// it looks like people typically report all including locked files. lets not report hidden files though.
        /// </summary>
        public static void InformServerOfSharedFiles()
        {
            try
            {
                if (SeekerState.SoulseekClient != null
                    && SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.LoggedIn))
                {
                    if (MeetsCurrentSharingConditions())
                    {
                        if (SeekerState.SharedFileCache != null)
                        {
                            Logger.Debug("Tell server we are sharing "
                                         + SeekerState.SharedFileCache.DirectoryCount
                                         + " dirs and "
                                         + SeekerState.SharedFileCache.GetNonHiddenFileCountForServer()
                                         + " files");

                            SeekerState.SoulseekClient.SetSharedCountsAsync(
                                SeekerState.SharedFileCache.DirectoryCount,
                                SeekerState.SharedFileCache.GetNonHiddenFileCountForServer() != -1
                                    ? SeekerState.SharedFileCache.GetNonHiddenFileCountForServer()
                                    : SeekerState.SharedFileCache.FileCount
                            );
                        }
                        else
                        {
                            Logger.Debug("We would tell server but we are not successfully set up yet.");
                        }
                    }
                    else
                    {
                        Logger.Debug("Tell server we are sharing 0 dirs and 0 files");
                        SeekerState.SoulseekClient.SetSharedCountsAsync(0, 0);
                    }

                    SeekerState.NumberOfSharedDirectoriesIsStale = false;
                }
                else
                {
                    if (MeetsCurrentSharingConditions())
                    {
                        if (SeekerState.SharedFileCache != null)
                        {
                            Logger.Debug("We need to Tell server we are sharing "
                                         + SeekerState.SharedFileCache.DirectoryCount
                                         + " dirs and "
                                         + SeekerState.SharedFileCache.GetNonHiddenFileCountForServer()
                                         + " files on next log in");
                        }
                        else
                        {
                            Logger.Debug("we meet sharing conditions " + 
                                         "but our shared file cache is not successfully set up");
                        }
                    }
                    else
                    {
                        Logger.Debug("We need to Tell server we are sharing 0 dirs" +
                                     " and 0 files on next log in");
                    }

                    SeekerState.NumberOfSharedDirectoriesIsStale = true;
                }
            }
            catch (Exception e)
            {
                Logger.Debug("Failed to InformServerOfSharedFiles " + e.Message + e.StackTrace);
                Logger.FirebaseDebug("Failed to InformServerOfSharedFiles " + e.Message + e.StackTrace);
            }
        }

        /// <summary>
        /// returns a list of searchable names (final dir + filename) for other's searches,
        /// and then the URI so android can actually get that file.
        /// </summary>
        /// <param name="dir"></param>
        /// <returns></returns>
        public void traverseDocumentFile(
            DocumentFile dir,
            List<Tuple<string, string, long, string>> pairs,
            Dictionary<string, List<Tuple<string, string, long>>> auxilaryDuplicatesList,
            bool isRootCase,
            string volName,
            ref int directoryCount)
        {
            if (dir.Exists())
            {
                DocumentFile[] files = dir.ListFiles();
                for (int i = 0; i < files.Length; ++i)
                {
                    DocumentFile file = files[i];
                    if (file.IsDirectory)
                    {
                        directoryCount++;
                        traverseDocumentFile(file, pairs, auxilaryDuplicatesList, false, volName, ref directoryCount);
                    }
                    else
                    {
                        string fullPath = file.Uri.Path.ToString().Replace('/', '\\');
                        string presentableName = file.Uri.LastPathSegment.Replace('/', '\\');

                        string searchableName = Common.Helpers.GetFolderNameFromFile(fullPath) + @"\"
                            + CommonHelpers.GetFileNameFromFile(fullPath);

                        if (isRootCase && (volName != null))
                        {
                            if (searchableName.Substring(0, volName.Length) == volName)
                            {
                                if (searchableName.Length != volName.Length) // i.e. if its just "primary:"
                                {
                                    searchableName = searchableName.Substring(volName.Length);
                                }
                            }
                        }

                        pairs.Add(new Tuple<string, string, long, string>(
                            searchableName,
                            file.Uri.ToString(),
                            file.Length(),
                            presentableName
                        ));
                    }
                }
            }
        }

        public void traverseFile(Java.IO.File dir)
        {
            if (dir.Exists())
            {
                Java.IO.File[] files = dir.ListFiles();
                for (int i = 0; i < files.Length; ++i)
                {
                    Java.IO.File file = files[i];
                    if (file.IsDirectory)
                    {
                        traverseFile(file);
                    }
                    else
                    {
                        // do something here with the file
                        Logger.Debug(file.Path);
                        Logger.Debug(file.AbsolutePath);
                        Logger.Debug(file.CanonicalPath);
                    }
                }
            }
        }

        // TODO: Move these constants to the top of the file
        //       or move them to a separate file along with some of the utility methods below and above

        public const string SETTINGS_INTENT = "com.example.seeker.SETTINGS";
        public const int SETTINGS_EXTERNAL = 0x430;
        public const int DEFAULT_SEARCH_RESULTS = 250;
        private const int WRITE_EXTERNAL = 9999;
        private const int NEW_WRITE_EXTERNAL = 0x428;
        private const int MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL = 0x429;
        private const int NEW_WRITE_EXTERNAL_VIA_LEGACY = 0x42A;
        private const int MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY = 0x42B;
        private const int NEW_WRITE_EXTERNAL_VIA_LEGACY_Settings_Screen = 0x42C;
        private const int MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY_Settings_Screen = 0x42D;
        private const int POST_NOTIFICATION_PERMISSION = 0x42E;
        private AndroidX.ViewPager.Widget.ViewPager pager = null;

        public static PowerManager.WakeLock CpuKeepAlive_Transfer = null;
        public static Android.Net.Wifi.WifiManager.WifiLock WifiKeepAlive_Transfer = null;
        public static System.Timers.Timer KeepAliveInactivityKillTimer = null;

        public static void KeepAliveInactivityKillTimerEllapsed(object sender, System.Timers.ElapsedEventArgs e)
        {
            if (CpuKeepAlive_Transfer != null)
            {
                CpuKeepAlive_Transfer.Release();
            }

            if (WifiKeepAlive_Transfer != null)
            {
                WifiKeepAlive_Transfer.Release();
            }

            KeepAliveInactivityKillTimer.Stop();
        }

        public static void SetUpSharing(Action uiUpdateAction = null)
        {
            Action setUpSharedFileCache = new Action(() =>
            {
                string errorMessage = string.Empty;
                bool success = false;
                Logger.Debug("We meet sharing conditions, lets set up the sharedFileCache for 1st time.");
                
                try
                {
                    // we check the cache which has ALL of the parsed results in it. much different from rescanning.
                    success = InitializeDatabase(null, true, out errorMessage);
                }
                catch (Exception e)
                {
                    Logger.Debug("Error setting up sharedFileCache for 1st time." + e.Message + e.StackTrace);
                    SetUnsetSharingBasedOnConditions(false, true);

                    if (!(e is DirectoryAccessFailure))
                    {
                        Logger.FirebaseDebug("MainActivity error parsing: " + e.Message + "  " + e.StackTrace);
                    }

                    SeekerState.ActiveActivityRef.RunOnUiThread(new Action(() =>
                    {
                        Toast.MakeText(
                            SeekerState.ActiveActivityRef,
                            SeekerState.ActiveActivityRef.GetString(Resource.String.error_sharing),
                            ToastLength.Long
                        ).Show();
                    }));
                }

                if (success
                    && SeekerState.SharedFileCache != null
                    && SeekerState.SharedFileCache.SuccessfullyInitialized)
                {
                    Logger.Debug("database full initialized.");
                    SeekerState.ActiveActivityRef.RunOnUiThread(new Action(() =>
                    {
                        Toast.MakeText(
                            SeekerState.ActiveActivityRef,
                            SeekerState.ActiveActivityRef.GetString(Resource.String.success_sharing),
                            ToastLength.Short
                        ).Show();
                    }));

                    try
                    {
                        // setup soulseek client with handlers if all conditions met
                        SetUnsetSharingBasedOnConditions(false);
                    }
                    catch (Exception e)
                    {
                        Logger.FirebaseDebug("MainActivity error setting handlers: "
                                                 + e.Message + "  " + e.StackTrace);
                    }
                }
                else if (!success)
                {
                    SeekerState.ActiveActivityRef.RunOnUiThread(new Action(() =>
                    {
                        if (string.IsNullOrEmpty(errorMessage))
                        {
                            Toast.MakeText(
                                SeekerState.ActiveActivityRef,
                                SeekerState.ActiveActivityRef.GetString(Resource.String.error_sharing),
                                ToastLength.Short
                            ).Show();
                        }
                        else
                        {
                            Toast.MakeText(SeekerState.ActiveActivityRef, errorMessage, ToastLength.Short).Show();
                        }
                    }));
                }

                if (uiUpdateAction != null)
                {
                    SeekerState.ActiveActivityRef.RunOnUiThread(uiUpdateAction);
                }

                SeekerState.AttemptedToSetUpSharing = true;
            });
            System.Threading.ThreadPool.QueueUserWorkItem((object o) => { setUpSharedFileCache(); });
        }

        private ISharedPreferences sharedPreferences;
        private const string defaultMusicUri = "content://com.android.externalstorage.documents/tree/primary%3AMusic";

        protected override void OnCreate(Bundle savedInstanceState)
        {

            // basically if the Intent created the MainActivity, then we want to handle it (i.e. if from "Search Here")
            // however, if we say rotate the device or leave
            // and come back to it (and the activity got destroyed in the mean time) then
            // it will re-handle the activity each time.
            // We can check if it is truly "new" by looking at the savedInstanceState.
            bool reborn = false;

            if (savedInstanceState == null)
            {
                Logger.Debug("Main Activity On Create NEW");
            }
            else
            {
                reborn = true;
                Logger.Debug("Main Activity On Create REBORN");
            }

            try
            {
                if (CpuKeepAlive_Transfer == null)
                {
                    CpuKeepAlive_Transfer = ((PowerManager)this.GetSystemService(Context.PowerService))
                        .NewWakeLock(WakeLockFlags.Partial, "Seeker Download CPU_Keep_Alive");

                    CpuKeepAlive_Transfer.SetReferenceCounted(false);
                }

                if (WifiKeepAlive_Transfer == null)
                {
                    WifiKeepAlive_Transfer = ((Android.Net.Wifi.WifiManager)this.GetSystemService(Context.WifiService))
                        .CreateWifiLock(Android.Net.WifiMode.FullHighPerf, "Seeker Download Wifi_Keep_Alive");

                    WifiKeepAlive_Transfer.SetReferenceCounted(false);
                }
            }
            catch (Exception e)
            {
                Logger.FirebaseDebug("error init keepalives: " + e.Message + e.StackTrace);
            }

            if (KeepAliveInactivityKillTimer == null)
            {
                // kill after 10 mins of no activity..
                // remember that this is a fallback. for when foreground service is still running
                // but nothing is happening otherwise.
                KeepAliveInactivityKillTimer = new System.Timers.Timer(60 * 1000 * 10);

                KeepAliveInactivityKillTimer.Elapsed += KeepAliveInactivityKillTimerEllapsed;
                KeepAliveInactivityKillTimer.AutoReset = false;
            }

            base.OnCreate(savedInstanceState);

            Xamarin.Essentials.Platform.Init(this, savedInstanceState); // this is what you are supposed to do.
            SetContentView(Resource.Layout.activity_main);

            BottomNavigationView navigation = FindViewById<BottomNavigationView>(Resource.Id.navigation);
            navigation.SetOnNavigationItemSelectedListener(this);


            AndroidX.AppCompat.Widget.Toolbar myToolbar =
                (AndroidX.AppCompat.Widget.Toolbar)FindViewById(Resource.Id.toolbar);

            myToolbar.Title = this.GetString(Resource.String.home_tab);
            myToolbar.InflateMenu(Resource.Menu.account_menu);
            SetSupportActionBar(myToolbar);
            myToolbar.InflateMenu(Resource.Menu.account_menu); // twice??

            var backPressedCallback = new GenericOnBackPressedCallback(true, onBackPressedAction);
            OnBackPressedDispatcher.AddCallback(backPressedCallback);

            System.Console.WriteLine("Testing.....");

            sharedPreferences = this.GetSharedPreferences("SoulSeekPrefs", 0);

            TabLayout tabs = (TabLayout)FindViewById(Resource.Id.tabs);

            pager = (AndroidX.ViewPager.Widget.ViewPager)FindViewById(Resource.Id.pager);
            pager.PageSelected += Pager_PageSelected;
            TabsPagerAdapter adapter = new TabsPagerAdapter(SupportFragmentManager);

            tabs.TabSelected += Tabs_TabSelected;
            pager.Adapter = adapter;
            pager.AddOnPageChangeListener(new OnPageChangeLister1());

            // this is a relatively safe way that prevents rotates from redoing the intent.
            bool alreadyHandled = Intent.GetBooleanExtra("ALREADY_HANDLED", false);
            Intent = Intent.PutExtra("ALREADY_HANDLED", true);

            if (Intent != null)
            {
                if (Intent.GetIntExtra(DownloadForegroundService.FromTransferString, -1) == 2)
                {
                    pager.SetCurrentItem(2, false);
                }
                else if (Intent.GetIntExtra(SeekerApplication.FromFolderAlert, -1) == 2)
                {
                    pager.SetCurrentItem(2, false);
                }
                else if (Intent.GetIntExtra(UserListActivity.IntentUserGoToBrowse, -1) == 3)
                {
                    pager.SetCurrentItem(3, false);
                }
                else if (Intent.GetIntExtra(UserListActivity.IntentUserGoToSearch, -1) == 1)
                {
                    pager.SetCurrentItem(1, false);
                }
                else if (Intent.GetIntExtra(UserListActivity.IntentSearchRoom, -1) == 1)
                {
                    pager.SetCurrentItem(1, false);
                }
                // if its not reborn then the OnNewIntent will handle it...
                else if (Intent.GetIntExtra(WishlistController.FromWishlistString, -1) == 1 && !reborn)
                {
                    SeekerState.MainActivityRef = this; // set these early. they are needed
                    SeekerState.ActiveActivityRef = this;

                    Logger.FirebaseInfo("is resumed: "
                                                 + (SearchFragment.Instance?.IsResumed ?? false).ToString());

                    Logger.FirebaseInfo("from wishlist clicked");

                    int currentPage = pager.CurrentItem;
                    int tabID = Intent.GetIntExtra(WishlistController.FromWishlistStringID, int.MaxValue);

                    // this is the case even if process previously got am state killed.
                    if (currentPage == 1)
                    {
                        Logger.FirebaseInfo("from wishlist clicked - current page");
                        if (tabID == int.MaxValue)
                        {
                            Logger.FirebaseDebug("tabID == int.MaxValue");
                        }
                        else if (!SearchTabHelper.SearchTabCollection.ContainsKey(tabID))
                        {
                            Logger.FirebaseDebug("doesnt contain key");

                            Toast.MakeText(this, this.GetString(Resource.String.wishlist_tab_error), ToastLength.Long)
                                .Show();
                        }
                        else
                        {
                            if (SearchFragment.Instance?.IsResumed ?? false) // !??! this logic is backwards...
                            {
                                Logger.Debug("we are on the search page " +
                                             "but we need to wait for OnResume search frag");

                                goToSearchTab = tabID; // we read this we resume
                            }
                            else
                            {
                                SearchFragment.Instance.GoToTab(tabID, false, true);
                            }
                        }
                    }
                    else
                    {
                        Logger.FirebaseInfo("from wishlist clicked - different page");

                        // when we move to the page, lets move to our tab, if its not the current one..
                        goToSearchTab = tabID; //we read this when we move tab...
                        pager.SetCurrentItem(1, false);
                    }
                }
                else if (((Intent.GetIntExtra(UploadForegroundService.FromTransferUploadString, -1) == 2)
                          || (Intent.GetIntExtra(UPLOADS_NOTIF_EXTRA, -1) == 2))
                         && !alreadyHandled) // else every rotation will change Downloads to Uploads.
                {
                    HandleFromNotificationUploadIntent();
                }
                else if (Intent.GetIntExtra(SettingsActivity.FromBrowseSelf, -1) == 3)
                {
                    Logger.FirebaseInfo("from browse self");
                    pager.SetCurrentItem(3, false);
                }
                // this will always create a new instance,
                // so if its reborn then its an old intent that we already followed.
                else if (SearchSendIntentHelper.IsFromActionSend(Intent) && !reborn)
                {
                    SeekerState.MainActivityRef = this;
                    SeekerState.ActiveActivityRef = this;
                    Logger.Debug("MainActivity action send intent");

                    // give us a new fresh tab if the current one has a search in it...
                    if (!string.IsNullOrEmpty(SearchTabHelper.LastSearchTerm))
                    {
                        Logger.Debug("lets go to a new fresh tab");
                        int newTabToGoTo = SearchTabHelper.AddSearchTab();

                        Logger.Debug("search fragment null? " + (SearchFragment.Instance == null).ToString());

                        if (SearchFragment.Instance?.IsResumed ?? false)
                        {
                            //if resumed is true
                            SearchFragment.Instance.GoToTab(newTabToGoTo, false, true);
                        }
                        else
                        {
                            Logger.Debug("we are on the search page but we need to wait " +
                                         "for OnResume search frag");

                            goToSearchTab = newTabToGoTo; // we read this we resume
                        }
                    }

                    // go to search tab
                    Logger.Debug("prev search term: " + SearchDialog.SearchTerm);
                    SearchDialog.SearchTerm = Intent.GetStringExtra(Intent.ExtraText);
                    SearchDialog.IsFollowingLink = false;
                    pager.SetCurrentItem(1, false);

                    if (SearchSendIntentHelper.TryParseIntent(Intent, out string searchTermFound))
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
            }


            SeekerState.MainActivityRef = this;
            SeekerState.ActiveActivityRef = this;

            // if we have all the conditions to share, then set sharing up.
            if (MeetsSharingConditions() && !SeekerState.IsParsing && !MainActivity.IsSharingSetUpSuccessfully())
            {
                SetUpSharing(); // TODO: why is this tied to the MainActivity !!
            }
            else if (SeekerState.NumberOfSharedDirectoriesIsStale)
            {
                InformServerOfSharedFiles();
                SeekerState.AttemptedToSetUpSharing = true;
            }

            SeekerState.SharedPreferences = sharedPreferences;
            SeekerState.MainActivityRef = this;
            SeekerState.ActiveActivityRef = this;

            UpdateForScreenSize();

            if (SeekerState.UseLegacyStorage())
            {
                var permissionName = Manifest.Permission.WriteExternalStorage;
                if (ContextCompat.CheckSelfPermission(this, permissionName) == Android.Content.PM.Permission.Denied)
                {
                    ActivityCompat.RequestPermissions(this, new string[] { permissionName }, WRITE_EXTERNAL);
                }

                // file picker with legacy case
                if (!string.IsNullOrEmpty(SeekerState.SaveDataDirectoryUri))
                {
                    // an example of a random bad url that passes parsing but fails FromTreeUri:
                    // "file:/media/storage/sdcard1/data/example.externalstorage/files/"
                    Android.Net.Uri chosenUri = Android.Net.Uri.Parse(SeekerState.SaveDataDirectoryUri);
                    bool canWrite = false;

                    try
                    {
                        // a phone failed 4 times with //POCO X3 Pro
                        // Android 11(SDK 30)
                        // Caused by: java.lang.IllegalArgumentException: 
                        // at android.provider.DocumentsContract.getTreeDocumentId(DocumentsContract.java:1278)
                        // at androidx.documentfile.provider.DocumentFile.fromTreeUri(DocumentFile.java:136)
                        if (SeekerState.PreOpenDocumentTree() || !SeekerState.SaveDataDirectoryUriIsFromTree)
                        {
                            canWrite = DocumentFile.FromFile(new Java.IO.File(chosenUri.Path)).CanWrite();
                        }
                        else
                        {
                            // on changing the code and restarting for api 22 
                            // persistenduripermissions is empty
                            // and exists is false, cannot list files
                            canWrite = DocumentFile.FromTreeUri(this, chosenUri).CanWrite();
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
                        if (SeekerState.PreOpenDocumentTree())
                        {
                            SeekerState.RootDocumentFile = DocumentFile.FromFile(new Java.IO.File(chosenUri.Path));
                        }
                        else
                        {
                            SeekerState.RootDocumentFile = DocumentFile.FromTreeUri(this, chosenUri);
                        }
                    }
                    else
                    {
                        Logger.FirebaseDebug("cannot write" + chosenUri?.ToString() ?? "null");
                    }
                }

                // TODO: This is practically duplicate code from what is above
                // now for incomplete
                if (!string.IsNullOrEmpty(SeekerState.ManualIncompleteDataDirectoryUri))
                {
                    // an example of a random bad url that passes parsing but fails FromTreeUri:
                    // "file:/media/storage/sdcard1/data/example.externalstorage/files/"
                    Android.Net.Uri chosenIncompleteUri =
                        Android.Net.Uri.Parse(SeekerState.ManualIncompleteDataDirectoryUri);

                    bool canWrite = false;
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
                            canWrite = DocumentFile.FromFile(new Java.IO.File(chosenIncompleteUri.Path)).CanWrite();
                        }
                        else
                        {
                            // on changing the code and restarting for api 22 
                            // persistenduripermissions is empty
                            // and exists is false, cannot list file
                            canWrite = DocumentFile.FromTreeUri(this, chosenIncompleteUri).CanWrite();
                        }
                    }
                    catch (Exception e)
                    {
                        if (chosenIncompleteUri != null)
                        {
                            Logger.FirebaseDebug("legacy Incomplete DocumentFile.FromTreeUri failed with URI: "
                                        + chosenIncompleteUri.ToString() + " " + e.Message);
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
                                DocumentFile.FromFile(new Java.IO.File(chosenIncompleteUri.Path));
                        }
                        else
                        {
                            SeekerState.RootIncompleteDocumentFile =
                                DocumentFile.FromTreeUri(this, chosenIncompleteUri);
                        }
                    }
                    else
                    {
                        Logger.FirebaseDebug("cannot write incomplete" + chosenIncompleteUri?.ToString() ?? "null");
                    }
                }


            }
            else
            {

                Android.Net.Uri res = null;
                if (string.IsNullOrEmpty(SeekerState.SaveDataDirectoryUri))
                {
                    res = Android.Net.Uri.Parse(defaultMusicUri);
                }
                else
                {
                    // an example of a random bad url that passes parsing but fails FromTreeUri:
                    // "file:/media/storage/sdcard1/data/example.externalstorage/files/"
                    res = Android.Net.Uri.Parse(SeekerState.SaveDataDirectoryUri);
                }

                // TODO: Below code seems to be duplicate from what is above, as even the comment is the same
                bool canWrite = false;
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
                        canWrite = DocumentFile.FromTreeUri(this, res).CanWrite();
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

                    var b = new AndroidX.AppCompat.App.AlertDialog.Builder(this, Resource.Style.MyAlertDialogTheme);
                    b.SetTitle(this.GetString(Resource.String.seeker_needs_dl_dir));
                    b.SetMessage(this.GetString(Resource.String.seeker_needs_dl_dir_content));

                    ManualResetEvent mre = new ManualResetEvent(false);
                    EventHandler<DialogClickEventArgs> eventHandler = new(
                        (object sender, DialogClickEventArgs okayArgs) =>
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
                                this.StartActivityForResult(intent, NEW_WRITE_EXTERNAL);
                            }
                            catch (Exception ex)
                            {
                                if (ex.Message.Contains(CommonHelpers.NoDocumentOpenTreeToHandle))
                                {
                                    FallbackFileSelectionEntry(false);
                                }
                                else
                                {
                                    throw ex;
                                }
                            }
                        });

                    b.SetPositiveButton(Resource.String.okay, eventHandler);
                    b.SetCancelable(false);
                    b.Show();
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

                bool manualSet = false;

                // TODO: Some dfuplcate code below again, consider revisiting

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
                            canWriteIncomplete = DocumentFile.FromTreeUri(this, incompleteRes).CanWrite();
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

                    if (canWriteIncomplete)
                    {
                        if (SeekerState.PreOpenDocumentTree()
                            || !SeekerState.ManualIncompleteDataDirectoryUriIsFromTree)
                        {
                            SeekerState.RootIncompleteDocumentFile =
                                DocumentFile.FromFile(new Java.IO.File(incompleteRes.Path));
                        }
                        else
                        {
                            SeekerState.RootIncompleteDocumentFile = DocumentFile.FromTreeUri(this, incompleteRes);
                        }
                    }
                }




            }
        }

        public void FallbackFileSelection(int requestCode)
        {
            // Create FolderOpenDialog
            SimpleFileDialog fileDialog =
                new(SeekerState.ActiveActivityRef, SimpleFileDialog.FileSelectionMode.FolderChoose);

            fileDialog.GetFileOrDirectoryAsync(Android.OS.Environment.ExternalStorageDirectory.AbsolutePath)
                .ContinueWith((Task<string> t) =>
                {
                    if (t.Result == null || t.Result == string.Empty)
                    {
                        this.OnActivityResult(requestCode, Result.Canceled, new Intent());
                        return;
                    }
                    else
                    {
                        var intent = new Intent();
                        DocumentFile f = DocumentFile.FromFile(new Java.IO.File(t.Result));
                        intent.SetData(f.Uri);
                        this.OnActivityResult(requestCode, Result.Ok, intent);
                    }
                });
        }

        public static Action<Task> GetPostNotifPermissionTask()
        {
            return new Action<Task>((task) =>
            {
                if (task.IsCompletedSuccessfully)
                {
                    RequestPostNotificationPermissionsIfApplicable();
                }
            });

        }

        private static bool postNotficationAlreadyRequestedInSession = false;

        /// <summary>
        /// As far as where to place this, doing it on launch is no good (as they will already
        ///   see yet another though more important permission in the background behind them).
        /// Doing this on login (i.e. first session login) seems decent.
        /// </summary>
        private static void RequestPostNotificationPermissionsIfApplicable()
        {
            if (postNotficationAlreadyRequestedInSession)
            {
                return;
            }

            postNotficationAlreadyRequestedInSession = true;

            if ((int)Android.OS.Build.VERSION.SdkInt < 33)
            {
                return;
            }

            try
            {
                var permissionState = ContextCompat
                    .CheckSelfPermission(SeekerState.ActiveActivityRef, Manifest.Permission.PostNotifications);

                if (permissionState == Android.Content.PM.Permission.Denied)
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
                    lock (MainActivity.SHARED_PREF_LOCK)
                    {
                        var editor = SeekerState.SharedPreferences.Edit();
                        editor.PutBoolean(KeyConsts.M_PostNotificationRequestAlreadyShown, true);
                        editor.Commit();
                    }
                }

                ActivityCompat.RequestPermissions(
                    SeekerState.ActiveActivityRef,
                    new string[] { Manifest.Permission.PostNotifications },
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
            // this fixes a bug as follows:
            // previously we only set MainActivityRef on Create.
            // therefore if one launches MainActivity via a new intent (i.e. go to user list, then search users files)
            // it will be set with the new search user activity.
            // then if you press back twice you will see the original activity but the MainActivityRef will still be set
            // to the now destroyed activity since it was last to call onCreate.
            // so then the FragmentManager will be null among other things...
            SeekerState.MainActivityRef = this;

            base.OnStart();
        }

        public static bool fromNotificationMoveToUploads = false;

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
                            .Show();
                    }
                    else
                    {
                        if (SearchFragment.Instance?.Activity == null || (SearchFragment.Instance?.IsResumed ?? false))
                        {
                            Logger.Debug("we are on the search page but we need to wait " + 
                                         "for OnResume search frag");

                            goToSearchTab = tabID; // we read this we resume
                        }
                        else
                        {
                            SearchFragment.Instance.GoToTab(tabID, false, true);
                        }
                    }
                }
                else
                {
                    // when we move to the page, lets move to our tab, if its not the current one..
                    goToSearchTab = tabID; // we read this when we move tab...
                    pager.SetCurrentItem(1, false);
                }
            }
            // else every rotation will change Downloads to Uploads.
            else if (((Intent.GetIntExtra(UploadForegroundService.FromTransferUploadString, -1) == 2)
                      || (Intent.GetIntExtra(UPLOADS_NOTIF_EXTRA, -1) == 2)))
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
            int currentPage = pager.CurrentItem;

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
                    StaticHacks.TransfersFrag.MoveToUploadForNotif();
                }
            }
            else
            {
                fromNotificationMoveToUploads = true; // we read this in onresume
                pager.SetCurrentItem(2, false);
            }
        }

        /// <summary>
        /// This is responsible for filing the PMs into the data structure...
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SoulseekClient_PrivateMessageReceived(object sender, PrivateMessageReceivedEventArgs e)
        {
            AddMessage(e);
        }

        private void AddMessage(PrivateMessageReceivedEventArgs messageEvent)
        {
            // Intentional no-op
        }

        private void Navigator_ViewAttachedToWindow(object sender, View.ViewAttachedToWindowEventArgs e)
        {
            // Intentional no-op
        }

        public static void GetDownloadPlaceInQueueBatch(List<TransferItem> transferItems, bool addIfNotAdded)
        {
            if (MainActivity.CurrentlyLoggedInButDisconnectedState())
            {
                Task t;
                if (!MainActivity.ShowMessageAndCreateReconnectTask(SeekerState.ActiveActivityRef, false, out t))
                {
                    t.ContinueWith(new Action<Task>((Task t) =>
                    {
                        if (t.IsFaulted)
                        {
                            return;
                        }

                        SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                        {
                            GetDownloadPlaceInQueueBatchLogic(transferItems, addIfNotAdded);
                        });
                    }));
                }
            }
            else
            {
                GetDownloadPlaceInQueueBatchLogic(transferItems, addIfNotAdded);
            }
        }


        public static void GetDownloadPlaceInQueueBatchLogic(
            List<TransferItem> transferItems,
            bool addIfNotAdded,
            Func<TransferItem, object> actionOnComplete = null)
        {
            foreach (TransferItem transferItem in transferItems)
            {
                GetDownloadPlaceInQueueLogic(
                    transferItem.Username,
                    transferItem.FullFilename,
                    addIfNotAdded,
                    true,
                    transferItem,
                    null
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

            if (MainActivity.CurrentlyLoggedInButDisconnectedState())
            {
                Task t;
                if (!MainActivity.ShowMessageAndCreateReconnectTask(SeekerState.ActiveActivityRef, false, out t))
                {
                    t.ContinueWith(new Action<Task>((Task t) =>
                    {
                        if (t.IsFaulted)
                        {
                            SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                            {
                                if (SeekerState.ActiveActivityRef != null)
                                {
                                    Toast.MakeText(
                                        SeekerState.ActiveActivityRef,
                                        SeekerState.ActiveActivityRef.GetString(Resource.String.failed_to_connect),
                                        ToastLength.Short
                                    ).Show();
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
                    }));
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

            Action<Task<int>> updateTask = new Action<Task<int>>(
                (Task<int> t) =>
                {
                    if (t.IsFaulted)
                    {
                        bool transitionToNextState = false;
                        Soulseek.TransferStates state = TransferStates.Errored;
                        if (t.Exception?.InnerException is Soulseek.UserOfflineException uoe)
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
                                ToastUIWithDebouncer(formattedString, "_6_", username);
                            }
                        }
                        else if (t.Exception?.InnerException?.Message != null
                                 && t.Exception.InnerException.Message.ToLower()
                                     .Contains(Soulseek.SoulseekClient.FailedToEstablishDirectOrIndirectStringLower))
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

                                ToastUIWithDebouncer(string.Format(cannotConnectString, username), "_7_", username);
                            }
                        }
                        else if (t.Exception?.InnerException?.Message != null &&
                                 t.Exception.InnerException is System.TimeoutException)
                        {
                            // they may just not be sending queue position messages.
                            // that is okay, we can still connect to them just fine for download time.
                            transitionToNextState = false;

                            if (!silent)
                            {
                                var messageString = SeekerApplication.GetString(Resource.String.TimeoutQueueUserX);
                                ToastUIWithDebouncer(string.Format(messageString, username), "_8_", username, 6);
                            }
                        }
                        else if (t.Exception?.InnerException?.Message != null
                                 && t.Exception.InnerException.Message.Contains("underlying Tcp connection is closed"))
                        {
                            // can be server connection (get user endpoint) or peer connection.
                            transitionToNextState = false;

                            if (!silent)
                            {
                                var formattedString = string.Format(
                                    "Failed to get queue position for {0}: Connection was unexpectedly closed.",
                                    username
                                );

                                ToastUIWithDebouncer(formattedString, "_9_", username, 6);
                            }
                        }
                        else
                        {
                            if (!silent)
                            {
                                ToastUIWithDebouncer($"Error getting queue position from {username}", "_9_", username);
                            }

                            Logger.FirebaseDebug("GetDownloadPlaceInQueue" + t.Exception.ToString());
                        }

                        if (transitionToNextState)
                        {
                            // update the transferItem array
                            if (transferItemInQuestion == null)
                            {
                                transferItemInQuestion = TransfersFragment.TransferItemManagerDL
                                    .GetTransferItemWithIndexFromAll(fullFileName, username, out int _);
                            }

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
                    }
                    else
                    {
                        bool queuePositionChanged = false;

                        // update the transferItem array
                        if (transferItemInQuestion == null)
                        {
                            transferItemInQuestion = TransfersFragment.TransferItemManagerDL
                                .GetTransferItemWithIndexFromAll(fullFileName, username, out int _);
                        }

                        if (transferItemInQuestion == null)
                        {
                            return;
                        }
                        else
                        {
                            queuePositionChanged = transferItemInQuestion.QueueLength != t.Result;

                            if (t.Result >= 0)
                            {
                                transferItemInQuestion.QueueLength = t.Result;
                            }
                            else
                            {
                                transferItemInQuestion.QueueLength = int.MaxValue;
                            }

                            if (queuePositionChanged)
                            {
                                Logger.Debug($"Queue Position of {fullFileName} has changed to {t.Result}");
                            }
                            else
                            {
                                Logger.Debug($"Queue Position of {fullFileName} is still {t.Result}");
                            }
                        }

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

            Task<int> getDownloadPlace = null;
            try
            {
                getDownloadPlace = SeekerState.SoulseekClient.GetDownloadPlaceInQueueAsync(
                    username,
                    fullFileName,
                    null,
                    transferItemInQuestion.ShouldEncodeFileLatin1(),
                    transferItemInQuestion.ShouldEncodeFolderLatin1()
                );
            }
            catch (TransferNotFoundException)
            {
                if (addIfNotAdded)
                {
                    // it is not downloading... therefore retry the download...
                    if (transferItemInQuestion == null)
                    {
                        transferItemInQuestion = TransfersFragment.TransferItemManagerDL
                            .GetTransferItemWithIndexFromAll(fullFileName, username, out int _);
                    }

                    CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
                    try
                    {
                        transferItemInQuestion.QueueLength = int.MaxValue;
                        Android.Net.Uri incompleteUri = null;

                        // else when you go to cancel you are cancelling an already cancelled useless token!!
                        TransfersFragment
                            .SetupCancellationToken(transferItemInQuestion, cancellationTokenSource, out _);

                        Task task = TransfersUtil.DownloadFileAsync(
                            transferItemInQuestion.Username,
                            transferItemInQuestion.FullFilename,
                            transferItemInQuestion.GetSizeForDL(),
                            cancellationTokenSource,
                            out _,
                            isFileDecodedLegacy: transferItemInQuestion.ShouldEncodeFileLatin1(),
                            isFolderDecodedLegacy: transferItemInQuestion.ShouldEncodeFolderLatin1()
                        );

                        task.ContinueWith(DownloadContinuationActionUI(
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
                        ));
                    }
                    catch (DuplicateTransferException)
                    {
                        // happens due to button mashing...
                        return;
                    }
                    catch (System.Exception error)
                    {
                        Action a = new Action(() =>
                        {
                            // TODO: Logging errors through toasts isn't a good practice
                            Toast.MakeText(
                                SeekerState.ActiveActivityRef,
                                SeekerState.ActiveActivityRef.GetString(Resource.String.error_) + error.Message,
                                ToastLength.Long
                            );
                        });

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
                else
                {
                    Logger.Debug("Transfer Item we are trying to get queue position " +
                                 "of is not currently being downloaded.");
                    return;
                }


            }
            catch (System.Exception e)
            {
                return;
            }

            getDownloadPlace.ContinueWith(updateTask);
        }

        // for transferItemPage to update its recyclerView
        public static EventHandler<TransferItem> TransferItemQueueUpdated;

        private void OnCloseClick(object sender, DialogClickEventArgs e)
        {
            (sender as AndroidX.AppCompat.App.AlertDialog).Dismiss();
        }

        public override bool OnOptionsItemSelected(IMenuItem item)
        {
            switch (item.ItemId)
            {
                case Resource.Id.user_list_action:
                    Intent intent = new Intent(SeekerState.MainActivityRef, typeof(UserListActivity));
                    SeekerState.MainActivityRef.StartActivityForResult(intent, 141);
                    return true;
                case Resource.Id.messages_action:
                    Intent intentMessages = new Intent(SeekerState.MainActivityRef, typeof(MessagesActivity));
                    SeekerState.MainActivityRef.StartActivityForResult(intentMessages, 142);
                    return true;
                case Resource.Id.chatroom_action:
                    Intent intentChatroom = new Intent(SeekerState.MainActivityRef, typeof(ChatroomActivity));
                    SeekerState.MainActivityRef.StartActivityForResult(intentChatroom, 143);
                    return true;
                case Resource.Id.settings_action:
                    Intent intent2 = new Intent(SeekerState.MainActivityRef, typeof(SettingsActivity));
                    SeekerState.MainActivityRef.StartActivityForResult(intent2, 140);
                    return true;
                case Resource.Id.shutdown_action:
                    Intent intent3 = new Intent(this, typeof(CloseActivity));
                    // Clear all activities and start new task
                    // ClearTask - causes any existing task that would be associated with the activity 
                    //  to be cleared before the activity is started. can only be used in conjunction with NewTask.
                    //  basically it clears all activities in the current task.
                    intent3.SetFlags(ActivityFlags.ClearTask | ActivityFlags.NewTask);
                    this.StartActivity(intent3);

                    if ((int)Android.OS.Build.VERSION.SdkInt < 21)
                    {
                        this.FinishAffinity();
                    }
                    else
                    {
                        this.FinishAndRemoveTask();
                    }

                    return true;
                case Resource.Id.about_action:
                    var builder =
                        new AndroidX.AppCompat.App.AlertDialog.Builder(this, Resource.Style.MyAlertDialogTheme);

                    var diag = builder.SetMessage(Resource.String.about_body)
                        .SetPositiveButton(Resource.String.close, OnCloseClick).Create();
                    diag.Show();

                    // this is a literal CDATA string.
                    var origString = string.Format(
                        SeekerState.ActiveActivityRef.GetString(Resource.String.about_body),
                        SeekerApplication.GetVersionString()
                    );

                    if ((int)Android.OS.Build.VERSION.SdkInt >= 24)
                    {
                        // this can be slow so do NOT do it in loops...
                        ((TextView)diag.FindViewById(Android.Resource.Id.Message)).TextFormatted =
                            Android.Text.Html.FromHtml(origString, Android.Text.FromHtmlOptions.ModeLegacy);
                    }
                    else
                    {
                        // this can be slow so do NOT do it in loops...
                        ((TextView)diag.FindViewById(Android.Resource.Id.Message)).TextFormatted =
                            Android.Text.Html.FromHtml(origString);
                    }

                    ((TextView)diag.FindViewById(Android.Resource.Id.Message)).MovementMethod =
                        Android.Text.Method.LinkMovementMethod.Instance;

                    return true;
            }

            return base.OnOptionsItemSelected(item);
        }


        public const string UPLOADS_CHANNEL_ID = "upload channel ID";
        public const string UPLOADS_CHANNEL_NAME = "Upload Notifications";
        public const string UPLOADS_NOTIF_EXTRA = "From Upload";

        public static Notification CreateUploadNotification(
            Context context,
            String username,
            List<String> directories,
            int numFiles)
        {
            string fileS = numFiles == 1
                ? SeekerState.ActiveActivityRef.GetString(Resource.String.file)
                : SeekerState.ActiveActivityRef.GetString(Resource.String.files);

            string titleText = string.Format(
                SeekerState.ActiveActivityRef.GetString(Resource.String.upload_f_string),
                numFiles,
                fileS,
                username
            );

            string directoryString = string.Empty;

            if (directories.Count == 1)
            {
                directoryString = SeekerState.ActiveActivityRef.GetString(Resource.String.from_directory)
                                  + ": " + directories[0];
            }
            else
            {
                directoryString = SeekerState.ActiveActivityRef.GetString(Resource.String.from_directories)
                                  + ": " + directories[0];

                for (int i = 0; i < directories.Count; i++)
                {
                    if (i == 0)
                    {
                        continue;
                    }

                    directoryString += ", " + directories[i];
                }
            }

            string contextText = directoryString;
            Intent notifIntent = new Intent(context, typeof(MainActivity));
            notifIntent.AddFlags(ActivityFlags.SingleTop);
            notifIntent.PutExtra(UPLOADS_NOTIF_EXTRA, 2);

            PendingIntent pendingIntent = PendingIntent.GetActivity(
                context,
                username.GetHashCode(),
                notifIntent,
                CommonHelpers.AppendMutabilityIfApplicable(PendingIntentFlags.UpdateCurrent, true)
            );

            // no such method takes args CHANNEL_ID in API 25. API 26 = 8.0 which requires channel ID.
            // a "channel" is a category in the UI to the end user.
            Notification notification = null;

            if (Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.O)
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
#pragma warning disable CS0618 // Type or member is obsolete
                    new Notification.Builder(context)
#pragma warning restore CS0618 // Type or member is obsolete
                        .SetContentTitle(titleText)
                        .SetContentText(contextText)
                        .SetSmallIcon(Resource.Drawable.ic_stat_soulseekicontransparent)
                        .SetContentIntent(pendingIntent)
                        .SetOnlyAlertOnce(true) //maybe
                        .SetTicker(titleText).Build();
            }

            return notification;
        }

        public static bool UserListContainsUser(string username)
        {
            lock (SeekerState.UserList)
            {
                if (SeekerState.UserList == null)
                {
                    return false;
                }

                return SeekerState.UserList.FirstOrDefault((userlistinfo) =>
                {
                    return userlistinfo.Username == username;
                }) != null;
            }
        }

        public static bool UserListSetDoesNotExist(string username)
        {
            bool found = false;
            lock (SeekerState.UserList)
            {
                foreach (UserListItem item in SeekerState.UserList)
                {
                    if (item.Username == username)
                    {
                        found = true;
                        item.DoesNotExist = true;
                        break;
                    }
                }
            }

            return found;
        }

        /// <summary>
        /// This is for adding new users...
        /// </summary>
        /// <returns>true if user was already added</returns>
        public static bool UserListAddUser(UserData userData, UserPresence? status = null)
        {
            lock (SeekerState.UserList)
            {
                bool found = false;
                foreach (UserListItem item in SeekerState.UserList)
                {
                    if (item.Username == userData.Username)
                    {
                        found = true;
                        if (userData != null)
                        {
                            if (status != null)
                            {
                                var oldStatus = item.UserStatus;
                                item.UserStatus = new UserStatus(status.Value, oldStatus?.IsPrivileged ?? false);
                            }

                            item.UserData = userData;
                            item.DoesNotExist = false;


                        }

                        break;
                    }
                }

                if (!found)
                {
                    // this is the normal case..
                    var item = new UserListItem(userData.Username);
                    item.UserData = userData;

                    // if added an ignored user, then unignore the user.  the two are mutually exclusive.....
                    if (SeekerApplication.IsUserInIgnoreList(userData.Username))
                    {
                        SeekerApplication.RemoveFromIgnoreList(userData.Username);
                    }

                    SeekerState.UserList.Add(item);
                    return false;
                }
                else
                {
                    return true;
                }

            }
        }

        /// <summary>
        /// Remove user from user list.
        /// </summary>
        /// <returns>true if user was found (if false then bad..)</returns>
        public static bool UserListRemoveUser(string username)
        {
            lock (SeekerState.UserList)
            {
                UserListItem itemToRemove = null;
                foreach (UserListItem item in SeekerState.UserList)
                {
                    if (item.Username == username)
                    {
                        itemToRemove = item;
                        break;
                    }
                }

                if (itemToRemove == null)
                {
                    return false;
                }

                SeekerState.UserList.Remove(itemToRemove);
                return true;
            }

        }




        /// <summary>
        /// Creates and returns an <see cref="IEnumerable{T}"/> of <see cref="Soulseek.Directory"/>
        /// in response to a remote request.
        /// </summary>
        /// <param name="username">The username of the requesting user.</param>
        /// <param name="endpoint">The IP endpoint of the requesting user.</param>
        /// <returns>A Task resolving an IEnumerable of Soulseek.Directory.</returns>
        private static Task<BrowseResponse> BrowseResponseResolver(string username, IPEndPoint endpoint)
        {
            if (SeekerApplication.IsUserInIgnoreList(username))
            {
                return Task.FromResult(new BrowseResponse(Enumerable.Empty<Directory>()));
            }

            return Task.FromResult(SeekerState.SharedFileCache.GetBrowseResponseForUser(username));
        }

        /// <summary>
        ///     Creates and returns a <see cref="Soulseek.Directory"/> in response to a remote request.
        /// </summary>
        /// <param name="username">The username of the requesting user.</param>
        /// <param name="endpoint">The IP endpoint of the requesting user.</param>
        /// <param name="token">The unique token for the request, supplied by the requesting user.</param>
        /// <param name="directory">The requested directory.</param>
        /// <returns>A Task resolving an instance of Soulseek.Directory containing the contents of the requested directory.</returns>
        private static Task<Soulseek.Directory> DirectoryContentsResponseResolver(
            string username,
            IPEndPoint endpoint,
            int token,
            string directory)
        {
            // the directory is the presentable name.
            // the old EndsWith(dir) fails if the directory is not unique
            // i.e. document structure of Soulseek Complete > some dirs and files,
            // Soulseek Complete > more dirs and files..
            Tuple<string, string> fullDirUri = SeekerState.SharedFileCache.FriendlyDirNameToUriMapping
                .Where((Tuple<string, string> t) => { return t.Item1 == directory; })
                .FirstOrDefault(); // TODO: DICTIONARY>>>>>

            if (fullDirUri == null)
            {
                // as fallback safety.  I dont think this will ever happen.....
                fullDirUri = SeekerState.SharedFileCache.FriendlyDirNameToUriMapping
                    .Where((Tuple<string, string> t) => { return t.Item1.EndsWith(directory); })
                    .FirstOrDefault();
            }

            DocumentFile fullDir = null;
            if (SeekerState.PreOpenDocumentTree() || !UploadDirectoryManager.IsFromTree(fullDirUri.Item2)) //todo
            {
                fullDir = DocumentFile.FromFile(new Java.IO.File(Android.Net.Uri.Parse(fullDirUri.Item2).Path));
            }
            else
            {
                fullDir =
                    DocumentFile.FromTreeUri(SeekerState.ActiveActivityRef, Android.Net.Uri.Parse(fullDirUri.Item2));
            }

            var slskDir =
                SlskDirFromDocumentFile(fullDir, true, 
                    StorageUtils.GetVolumeName(fullDir.Uri.LastPathSegment, false, out _));

            slskDir = new Directory(directory, slskDir.Files);
            return Task.FromResult(slskDir);
        }

        public static void TurnOnSharing()
        {
            SeekerState.SoulseekClient.Options.SetSharedHandlers(
                BrowseResponseResolver,
                SearchResponseResolver,
                DirectoryContentsResponseResolver,
                EnqueueDownloadAction
            );
        }

        public static void TurnOffSharing()
        {
            SeekerState.SoulseekClient.Options.NullSharedHandlers();
        }

        /// <summary>
        /// Do this on any changes (like in Settings) but also on Login.
        /// </summary>
        /// <param name="informServerOfChangeIfThereIsAChange"></param>
        /// <param name="force">force if we are chaning the upload directory...</param>
        public static void SetUnsetSharingBasedOnConditions(
            bool informServerOfChangeIfThereIsAChange,
            bool force = false)
        {
            // when settings gets recreated can get nullref here.
            bool wasShared = SeekerState.SoulseekClient.Options.SearchResponseResolver != null;
            if (MeetsCurrentSharingConditions())
            {
                TurnOnSharing();
                if (!wasShared || force)
                {
                    Logger.Debug("sharing state changed to ON");
                    InformServerOfSharedFiles();
                }
            }
            else
            {
                TurnOffSharing();
                if (wasShared)
                {
                    Logger.Debug("sharing state changed to OFF");
                    InformServerOfSharedFiles();
                }
            }
        }

        /// <summary>
        /// Has set things up properly and has sharing on.
        /// </summary>
        /// <returns></returns>
        public static bool MeetsSharingConditions()
        {
            return SeekerState.SharingOn
                   && UploadDirectoryManager.UploadDirectories.Count != 0
                   && !SeekerState.IsParsing
                   && !UploadDirectoryManager.AreAllFailed();
        }

        /// <summary>
        /// Has set things up properly and has sharing on + their network settings currently allow it.
        /// </summary>
        /// <returns></returns>
        public static bool MeetsCurrentSharingConditions()
        {
            return MeetsSharingConditions() && SeekerState.IsNetworkPermitting();
        }

        public static bool IsSharingSetUpSuccessfully()
        {
            return SeekerState.SharedFileCache != null && SeekerState.SharedFileCache.SuccessfullyInitialized;
        }

        public static Tuple<SharingIcons, string> GetSharingMessageAndIcon(out bool isParsing)
        {
            isParsing = false;
            if (MeetsSharingConditions() && IsSharingSetUpSuccessfully())
            {
                // try to parse this into a path: SeekerState.ShareDataDirectoryUri
                if (MeetsCurrentSharingConditions())
                {
                    return new Tuple<SharingIcons, string>(
                        SharingIcons.On,
                        SeekerState.ActiveActivityRef.GetString(Resource.String.success_sharing)
                    );
                }
                else
                {
                    // TODO: Use a string resource here
                    return new Tuple<SharingIcons, string>(
                        SharingIcons.OffDueToNetwork,
                        "Sharing disabled on metered connection"
                    );
                }
            }
            else if (MeetsSharingConditions() && !IsSharingSetUpSuccessfully())
            {
                if (SeekerState.SharedFileCache == null)
                {
                    return new Tuple<SharingIcons, string>(SharingIcons.Off, "Not yet initialized.");
                }
                else
                {
                    return new Tuple<SharingIcons, string>(
                        SharingIcons.Error,
                        SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_disabled_share_not_set)
                    );
                }
            }
            else if (!SeekerState.SharingOn)
            {
                return new Tuple<SharingIcons, string>(
                    SharingIcons.Off,
                    SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_off)
                );
            }
            else if (SeekerState.IsParsing)
            {
                isParsing = true;
                return new Tuple<SharingIcons, string>(
                    SharingIcons.CurrentlyParsing,
                    SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_currently_parsing)
                );
            }
            else if (SeekerState.FailedShareParse)
            {
                return new Tuple<SharingIcons, string>(
                    SharingIcons.Error,
                    SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_disabled_failure_parsing)
                );
            }
            else if (UploadDirectoryManager.UploadDirectories.Count == 0)
            {
                return new Tuple<SharingIcons, string>(
                    SharingIcons.Error,
                    SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_disabled_share_not_set)
                );
            }
            else if (UploadDirectoryManager.AreAllFailed())
            {
                // TODO: get error
                return new Tuple<SharingIcons, string>(
                    SharingIcons.Error,
                    SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_disabled_error)
                );
            }
            else
            {
                return new Tuple<SharingIcons, string>(
                    SharingIcons.Error,
                    SeekerState.ActiveActivityRef.GetString(Resource.String.sharing_disabled_error)
                );
            }
        }

        public void SetUpLoginContinueWith(Task t)
        {
            if (t == null)
            {
                return;
            }

            if (MeetsSharingConditions())
            {

                Action<Task> getAndSetLoggedInInfoAction = new Action<Task>((Task t) =>
                {
                    // we want to 
                    // UpdateStatus ??
                    // inform server if we are sharing..
                    // get our upload speed..
                    if (t.Status == TaskStatus.Faulted || t.IsFaulted || t.IsCanceled)
                    {
                        return;
                    }

                    // don't need to get the result of this one.
                    InformServerOfSharedFiles();

                    // the result of this one if from an event handler..
                    SeekerState.SoulseekClient.GetUserDataAsync(SeekerState.Username);
                });

                t.ContinueWith(getAndSetLoggedInInfoAction);
            }
        }

        public bool OnBrowseTab()
        {
            try
            {
                var pager = (AndroidX.ViewPager.Widget.ViewPager)FindViewById(Resource.Id.pager);
                return pager.CurrentItem == 3;
            }
            catch
            {
                Logger.FirebaseDebug("OnBrowseTab failed");
            }

            return false;
        }

        private void onBackPressedAction(OnBackPressedCallback callback)
        {
            bool relevant = false;
            try
            {
                var pager = (AndroidX.ViewPager.Widget.ViewPager)FindViewById(Resource.Id.pager);
                if (pager.CurrentItem == 3) //browse tab
                {
                    relevant = BrowseFragment.Instance.BackButton();
                }
                else if (pager.CurrentItem == 2) // transfer tab
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
                        this.InvalidateOptionsMenu();
                        StaticHacks.TransfersFrag.SetRecyclerAdapter();
                        StaticHacks.TransfersFrag.RestoreScrollPosition();
                        relevant = true;
                    }
                }
            }
            catch (Exception e)
            {
                // During Back Button:
                // Attempt to invoke virtual method 'java.lang.Object android.content.Context.getSystemService(java.lang.String)' on a null object reference
                Logger.FirebaseDebug("During Back Button: " + e.Message);
            }

            if (!relevant)
            {
                callback.Enabled = false;
                OnBackPressedDispatcher.OnBackPressed();
                callback.Enabled = true;
            }
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
                        System.Threading.CancellationToken.None,
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
            if (c == null)
            {
                c = SeekerState.MainActivityRef;
            }

            if (Looper.MainLooper.Thread == Java.Lang.Thread.CurrentThread()) // tested..
            {
                if (!silent)
                {
                    Toast.MakeText(c, c.GetString(Resource.String.temporary_disconnected), ToastLength.Short).Show();
                }
            }
            else
            {
                if (!silent)
                {
                    SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                    {
                        Toast.MakeText(c, c.GetString(Resource.String.temporary_disconnected), ToastLength.Short)
                            .Show();
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
            return (SeekerState.currentlyLoggedIn &&
                    (SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.Disconnected)
                     || SeekerState.SoulseekClient.State.HasFlag(SoulseekClientStates.Disconnecting))
                );
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
                        string statusString = away ? "away" : "online"; // not user facing
                        Logger.Debug($"We successfully changed our status to {statusString}");
                    }
                    else
                    {
                        Logger.Debug("SetStatusApi FAILED " + t.Exception?.Message);
                    }
                });
            }
            catch (Exception e)
            {
                Logger.Debug("SetStatusApi FAILED " + e.Message + e.StackTrace);
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
                TabLayout tabs = (TabLayout)FindViewById(Resource.Id.tabs);
                LinearLayout vg = (LinearLayout)tabs.GetChildAt(0);
                int tabsCount = vg.ChildCount;

                for (int j = 0; j < tabsCount; j++)
                {
                    ViewGroup vgTab = (ViewGroup)vg.GetChildAt(j);
                    int tabChildsCount = vgTab.ChildCount;
                    for (int i = 0; i < tabChildsCount; i++)
                    {
                        View tabViewChild = vgTab.GetChildAt(i);
                        if (tabViewChild is TextView)
                        {
                            ((TextView)tabViewChild).SetAllCaps(false);
                        }
                    }
                }
            }
            catch
            {
                // not worth throwing over..
            }
        }

        public void RecreateFragment(AndroidX.Fragment.App.Fragment f)
        {
            if (Build.VERSION.SdkInt >= Android.OS.BuildVersionCodes.N)
            {
                SupportFragmentManager.BeginTransaction().Detach(f).CommitNowAllowingStateLoss();
                SupportFragmentManager.BeginTransaction().Attach(f).CommitNowAllowingStateLoss();
            }
            else
            {
                SupportFragmentManager.BeginTransaction().Detach(f).Attach(f).CommitNow();
            }
        }

        /// <summary>
        /// Creates and returns a <see cref="SearchResponse"/> in response to the given <paramref name="query"/>.
        /// </summary>
        /// <param name="username">The username of the requesting user.</param>
        /// <param name="token">The search token.</param>
        /// <param name="query">The search query.</param>
        /// <returns>A Task resolving a SearchResponse, or null.</returns>
        private static Task<SearchResponse> SearchResponseResolver(string username, int token, SearchQuery query)
        {
            var defaultResponse = Task.FromResult<SearchResponse>(null);

            // some bots continually query for very common strings.  blacklist known names here.
            var blacklist = new[] { "Lola45", "Lolo51", "rajah" };
            if (blacklist.Contains(username))
            {
                return defaultResponse;
            }

            if (SeekerApplication.IsUserInIgnoreList(username))
            {
                return defaultResponse;
            }

            // some bots and perhaps users search for very short terms.
            // only respond to queries >= 3 characters.  sorry, U2 fans.
            if (query.Query.Length < 5)
            {
                return defaultResponse;
            }

            if (SeekerState.Username == null
                || SeekerState.Username == string.Empty
                || SeekerState.SharedFileCache == null)
            {
                return defaultResponse;
            }

            var results =
                SeekerState.SharedFileCache.Search(query, username, out IEnumerable<Soulseek.File> lockedResults);

            if (results.Any() || lockedResults.Any())
            {
                int ourUploadSpeed = 1024 * 256;
                if (SeekerState.UploadSpeed > 0)
                {
                    ourUploadSpeed = SeekerState.UploadSpeed;
                }

                return Task.FromResult(new SearchResponse(
                    SeekerState.Username,
                    token,
                    freeUploadSlots: 1,
                    uploadSpeed: ourUploadSpeed,
                    queueLength: 0,
                    fileList: results,
                    lockedFileList: lockedResults));
            }

            // if no results, either return null or an instance of SearchResponse with a fileList of length 0
            // in either case, no response will be sent to the requestor.
            return Task.FromResult<SearchResponse>(null);
        }

        /// <summary>
        /// Invoked upon a remote request to download a file.
        /// THE ORIGINAL BUT WITHOUT ITRANSFERTRACKER!!!!
        /// </summary>
        /// <param name="username">The username of the requesting user.</param>
        /// <param name="endpoint">The IP endpoint of the requesting user.</param>
        /// <param name="filename">The filename of the requested file.</param>
        /// <param name="tracker">(for example purposes) the ITransferTracker used to track progress.</param>
        /// <returns>A Task representing the asynchronous operation.</returns>
        /// <exception cref="DownloadEnqueueException">
        /// Thrown when the download is rejected.
        /// The Exception message will be passed to the remote user.
        /// </exception>
        /// <exception cref="Exception">
        /// Thrown on any other Exception other than a rejection.
        /// A generic message will be passed to the remote user for security reasons.
        /// </exception>
        private static Task EnqueueDownloadAction(string username, IPEndPoint endpoint, string filename)
        {
            if (SeekerApplication.IsUserInIgnoreList(username))
            {
                return Task.CompletedTask;
            }

            // the filename is basically "the key"
            _ = endpoint;
            string errorMsg = null;
            Tuple<long, string, Tuple<int, int, int, int>, bool, bool> ourFileInfo =
                SeekerState.SharedFileCache.GetFullInfoFromSearchableName(filename, out errorMsg);

            if (ourFileInfo == null)
            {
                Logger.FirebaseDebug("ourFileInfo is null: " + ourFileInfo + " " + errorMsg);
                throw new DownloadEnqueueException($"File not found.");
            }

            DocumentFile ourFile = null;
            Android.Net.Uri ourUri = Android.Net.Uri.Parse(ourFileInfo.Item2);

            if (ourFileInfo.Item4 || ourFileInfo.Item5)
            {
                // locked or hidden (hidden shouldnt happen but just in case, it should still be userlist only)
                // CHECK USER LIST
                if (!SlskHelp.CommonHelpers.UserListChecker.IsInUserList(username))
                {
                    throw new DownloadEnqueueException($"File not shared");
                }
            }

            if (SeekerState.PreOpenDocumentTree() || !UploadDirectoryManager.IsFromTree(filename)) // IsFromTree method!
            {
                ourFile = DocumentFile.FromFile(new Java.IO.File(ourUri.Path));
            }
            else
            {
                ourFile = DocumentFile.FromTreeUri(SeekerState.ActiveActivityRef, ourUri);
            }

            if (!ourFile.Exists())
            {
                throw new DownloadEnqueueException($"File not found.");
            }

            // create a new cancellation token source so that we can cancel the upload from the UI.
            var cts = new CancellationTokenSource();

            TransferItem transferItem = new TransferItem();
            transferItem.Username = username;
            transferItem.FullFilename = filename;
            transferItem.Filename = CommonHelpers.GetFileNameFromFile(filename);
            transferItem.FolderName = Common.Helpers.GetFolderNameFromFile(filename);
            transferItem.CancellationTokenSource = cts;
            transferItem.Size = ourFile.Length();
            transferItem.isUpload = true;
            transferItem = TransfersFragment.TransferItemManagerUploads
                .AddIfNotExistAndReturnTransfer(transferItem, out bool exists);

            if (!exists) // else the state will simply be updated a bit later. 
            {
                TransferAddedUINotify?.Invoke(null, transferItem);
            }

            // accept all download requests, and begin the upload immediately.
            // normally there would be an internal queue, and uploads would be handled separately.
            Task.Run(async () =>
            {
                CancellationTokenSource oldCts = null;
                try
                {
                    // outputstream.CanRead is false...
                    using var stream = SeekerState.MainActivityRef.ContentResolver.OpenInputStream(ourFile.Uri);

                    TransfersFragment.SetupCancellationToken(transferItem, cts, out oldCts);

                    // THE FILENAME THAT YOU PASS INTO HERE MUST MATCH EXACTLY
                    // ELSE THE CLIENT WILL REJECT IT.  //MUST MATCH EXACTLY THE ONE THAT WAS REQUESTED THAT IS.
                    await SeekerState.SoulseekClient.UploadAsync(
                        username,
                        filename,
                        transferItem.Size,
                        stream,
                        options: new TransferOptions(governor: SeekerApplication.SpeedLimitHelper.OurUploadGoverner),
                        cancellationToken: cts.Token
                    );
                }
                catch (DuplicateTransferException dup) //not tested
                {
                    Logger.Debug("UPLOAD DUPL - " + dup.Message);

                    // if there is a duplicate you do not want to overwrite the good cancellation token
                    // with a meaningless one. so restore the old one.
                    TransfersFragment.SetupCancellationToken(transferItem, oldCts, out _);
                }
                catch (DuplicateTokenException dup)
                {
                    Logger.Debug("UPLOAD DUPL - " + dup.Message);

                    // if there is a duplicate you do not want to overwrite the good cancellation token
                    // with a meaningless one. so restore the old one.
                    TransfersFragment.SetupCancellationToken(transferItem, oldCts, out _);
                }
            }).ContinueWith(t => { }, TaskContinuationOptions.NotOnRanToCompletion); // fire and forget

            // return a completed task so that the invoking code can respond to the remote client.
            return Task.CompletedTask;
        }

        protected override void OnActivityResult(int requestCode, [GeneratedEnum] Result resultCode, Intent data)
        {
            base.OnActivityResult(requestCode, resultCode, data);

            if (NEW_WRITE_EXTERNAL == requestCode
                || NEW_WRITE_EXTERNAL_VIA_LEGACY == requestCode
                || NEW_WRITE_EXTERNAL_VIA_LEGACY_Settings_Screen == requestCode)
            {
                Action showDirectoryButton = new Action(() =>
                {
                    ToastUi.Long(SeekerState.MainActivityRef.GetString(Resource.String.seeker_needs_dl_dir_error));
                    AddLoggedInLayout(StaticHacks.LoginFragment.View); // TODO: nullref
                    if (!SeekerState.currentlyLoggedIn)
                    {
                        MainActivity.BackToLogInLayout(
                            StaticHacks.LoginFragment.View,
                            (StaticHacks.LoginFragment as LoginFragment).LogInClick
                        );
                    }

                    if (StaticHacks.LoginFragment.View == null) // this can happen...
                    {
                        // .View is a method so it can return null.
                        // I tested it on MainActivity.OnPause and it was in fact null.

                        var toastMessage =
                            SeekerState.MainActivityRef.GetString(Resource.String.seeker_needs_dl_dir_choose_settings);
                        ToastUi.Long(toastMessage);
                        Logger.FirebaseDebug("StaticHacks.LoginFragment.View is null");
                        return;
                    }

                    Button bttn = StaticHacks.LoginFragment.View.FindViewById<Button>(Resource.Id.mustSelectDirectory);
                    Button bttnLogout = StaticHacks.LoginFragment.View.FindViewById<Button>(Resource.Id.buttonLogout);

                    if (bttn != null)
                    {
                        bttn.Visibility = ViewStates.Visible;
                        bttn.Click += MustSelectDirectoryClick;
                    }
                });

                if (NEW_WRITE_EXTERNAL_VIA_LEGACY_Settings_Screen == requestCode)
                {
                    // the resultCode will always be Cancelled for this since you have to back out of it.
                    // so instead we check Android.OS.Environment.IsExternalStorageManager
                    if (SettingsActivity.DoWeHaveProperPermissionsForInternalFilePicker())
                    {
                        // phase 2 - actually pick a file.
                        FallbackFileSelection(NEW_WRITE_EXTERNAL_VIA_LEGACY);
                        return;
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

                        return;
                    }
                }

                if (resultCode == Result.Ok)
                {
                    if (NEW_WRITE_EXTERNAL == requestCode)
                    {
                        var x = data.Data;
                        SeekerState.RootDocumentFile = DocumentFile.FromTreeUri(this, data.Data);
                        SeekerState.SaveDataDirectoryUri = data.Data.ToString();
                        SeekerState.SaveDataDirectoryUriIsFromTree = true;

                        this.ContentResolver.TakePersistableUriPermission(
                            data.Data,
                            ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission
                        );
                    }
                    else if (NEW_WRITE_EXTERNAL_VIA_LEGACY == requestCode)
                    {
                        SeekerState.RootDocumentFile = DocumentFile.FromFile(new Java.IO.File(data.Data.Path));
                        SeekerState.SaveDataDirectoryUri = data.Data.ToString();
                        SeekerState.SaveDataDirectoryUriIsFromTree = false;
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
                     MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY_Settings_Screen == requestCode)
            {

                Action reiterate = new Action(() =>
                {
                    ToastUi.Long(SeekerState.MainActivityRef.GetString(Resource.String.seeker_needs_dl_dir_error));
                });

                Action hideButton = new Action(() =>
                {
                    Button bttn = StaticHacks.LoginFragment.View.FindViewById<Button>(Resource.Id.mustSelectDirectory);
                    bttn.Visibility = ViewStates.Gone;
                });

                if (MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY_Settings_Screen == requestCode)
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
                    if (MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY == requestCode)
                    {
                        SeekerState.RootDocumentFile = DocumentFile.FromFile(new Java.IO.File(data.Data.Path));
                        SeekerState.SaveDataDirectoryUri = data.Data.ToString();
                        SeekerState.SaveDataDirectoryUriIsFromTree = false;
                    }
                    else if (MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL == requestCode)
                    {
                        SeekerState.RootDocumentFile = DocumentFile.FromTreeUri(this, data.Data);
                        SeekerState.SaveDataDirectoryUri = data.Data.ToString();
                        SeekerState.SaveDataDirectoryUriIsFromTree = true;
                        this.ContentResolver.TakePersistableUriPermission(
                            data.Data,
                            ActivityFlags.GrantWriteUriPermission | ActivityFlags.GrantReadUriPermission
                        );
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
            var storageManager = Android.OS.Storage.StorageManager.FromContext(this);

            var intent = storageManager.PrimaryStorageVolume.CreateOpenDocumentTreeIntent();
            intent.AddFlags(ActivityFlags.GrantPersistableUriPermission
                            | ActivityFlags.GrantReadUriPermission
                            | ActivityFlags.GrantWriteUriPermission
                            | ActivityFlags.GrantPrefixUriPermission);

            Android.Net.Uri res = null;
            if (string.IsNullOrEmpty(SeekerState.SaveDataDirectoryUri))
            {
                res = Android.Net.Uri.Parse(defaultMusicUri);
            }
            else
            {
                res = Android.Net.Uri.Parse(SeekerState.SaveDataDirectoryUri);
            }

            intent.PutExtra(DocumentsContract.ExtraInitialUri, res);
            try
            {
                this.StartActivityForResult(intent, MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL);
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains(CommonHelpers.NoDocumentOpenTreeToHandle))
                {
                    FallbackFileSelectionEntry(true);
                }
                else
                {
                    throw ex;
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
                        ? MUST_SELECT_A_DIRECTORY_WRITE_EXTERNAL_VIA_LEGACY_Settings_Screen
                        : NEW_WRITE_EXTERNAL_VIA_LEGACY_Settings_Screen
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
                if (SeekerState.RequiresEitherOpenDocumentTreeOrManageAllFiles()
                    && !hasManageAllFilesManisfestPermission)
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

        private void AndroidEnvironment_UnhandledExceptionRaiser(object sender, RaiseThrowableEventArgs e)
        {
            e.Handled = false; // make sure we still crash.. we just want to clean up..
            try
            {
                // save transfers state !!!
                TransfersFragment.SaveTransferItems(sharedPreferences);
            }
            catch
            {
                // Intentional no-op
            }

            try
            {
                // stop dl service..
                Intent downloadServiceIntent = new Intent(this, typeof(DownloadForegroundService));
                Logger.Debug("Stop Service");
                this.StopService(downloadServiceIntent);
            }
            catch
            {
                // Intentional no-op
            }
        }

        /// <summary>
        /// This RETURNS the task for Continuewith
        /// </summary>
        /// <param name="e"></param>
        /// <returns></returns>
        public static Action<Task> DownloadContinuationActionUI(DownloadAddedEventArgs e)
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
                                //retry download.
                                CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
                                Android.Net.Uri incompleteUri = null;

                                // else when you go to cancel you are cancelling an already cancelled useless token!!
                                TransfersFragment.SetupCancellationToken(e.dlInfo.TransferItemReference,
                                    cancellationTokenSource, out _);

                                Task retryTask = TransfersUtil.DownloadFileAsync(
                                    e.dlInfo.username,
                                    e.dlInfo.fullFilename,
                                    e.dlInfo.TransferItemReference.Size,
                                    cancellationTokenSource,
                                    out _,
                                    1,
                                    e.dlInfo.TransferItemReference.ShouldEncodeFileLatin1(),
                                    e.dlInfo.TransferItemReference.ShouldEncodeFolderLatin1()
                                );

                                retryTask.ContinueWith(MainActivity.DownloadContinuationActionUI(
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
                                ));
                            }
                            catch (System.Exception e)
                            {
                                // disconnected error
                                if (e is System.InvalidOperationException
                                    && e.Message.ToLower()
                                        .Contains("server connection must be connected and logged in"))
                                {
                                    action = () =>
                                    {
                                        ToastUIWithDebouncer(
                                            SeekerApplication.GetString(Resource.String.MustBeLoggedInToRetryDL),
                                            "_16_"
                                        );
                                    };
                                }
                                else
                                {
                                    Logger.FirebaseDebug("cancel and retry creation failed: "
                                                             + e.Message + e.StackTrace);
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

                                ToastUIWithDebouncer(messageString, "_17_");
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

                                    ToastUIWithDebouncer(messageString, "_2_");
                                }; // needed
                            }
                            else
                            {
                                action = () =>
                                {
                                    var messageString =
                                        SeekerState.ActiveActivityRef.GetString(Resource.String.transfer_rejected);
                                    ToastUIWithDebouncer(messageString, "_2_");
                                }; // needed
                            }

                            Logger.Debug("rejected. is not shared: " + isFileNotShared);
                        }
                        else if (task.Exception.InnerException is Soulseek.TransferException)
                        {
                            action = () =>
                            {
                                ToastUIWithDebouncer(
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
                                ToastUIWithDebouncer(
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
                                ToastUIWithDebouncer(
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
                                ToastUIWithDebouncer(
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
                                CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();
                                Android.Net.Uri incompleteUri = null;

                                // else when you go to cancel you are cancelling an already cancelled useless token!!
                                TransfersFragment.SetupCancellationToken(
                                    e.dlInfo.TransferItemReference,
                                    cancellationTokenSource,
                                    out _);

                                Task retryTask = TransfersUtil.DownloadFileAsync(
                                    e.dlInfo.username,
                                    e.dlInfo.fullFilename,
                                    e.dlInfo.Size,
                                    cancellationTokenSource,
                                    out _,
                                    1,
                                    e.dlInfo.TransferItemReference.ShouldEncodeFileLatin1(),
                                    e.dlInfo.TransferItemReference.ShouldEncodeFolderLatin1());

                                retryTask.ContinueWith(MainActivity.DownloadContinuationActionUI(
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
                                ));

                                return; // i.e. dont toast anything just retry.
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

                        if (action == null)
                        {
                            action = () =>
                            {
                                ToastUi.Long(SeekerState.ActiveActivityRef
                                    .GetString(Resource.String.error_unspecified));
                            };
                        }

                        SeekerState.ActiveActivityRef.RunOnUiThread(action);
                        return;
                    }

                    // failed downloads return before getting here...

                    if (e.dlInfo.RetryCount == 1 && e.dlInfo.PreviousFailureException != null)
                    {
                        Logger.FirebaseDebug("auto retry succeeded: prev exception: "
                                    + e.dlInfo.PreviousFailureException.InnerException?.Message?.ToString());
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

                    string finalUri = string.Empty;
                    if (task is Task<byte[]> tbyte)
                    {
                        bool noSubfolder = e.dlInfo.TransferItemReference.TransferItemExtra
                            .HasFlag(Transfers.TransferItemExtras.NoSubfolder);

                        string path = SaveToFile(
                            e.dlInfo.fullFilename,
                            e.dlInfo.username,
                            tbyte.Result,
                            null,
                            null,
                            true,
                            e.dlInfo.Depth,
                            noSubfolder,
                            out finalUri);

                        SaveFileToMediaStore(path);
                    }
                    else if (task is Task<Tuple<string, string>> tString)
                    {
                        // move file...
                        bool noSubfolder = e.dlInfo.TransferItemReference.TransferItemExtra
                            .HasFlag(Transfers.TransferItemExtras.NoSubfolder);

                        string path = SaveToFile(
                            e.dlInfo.fullFilename,
                            e.dlInfo.username,
                            null,
                            Android.Net.Uri.Parse(tString.Result.Item1),
                            Android.Net.Uri.Parse(tString.Result.Item2),
                            false,
                            e.dlInfo.Depth,
                            noSubfolder,
                            out finalUri);

                        SaveFileToMediaStore(path);
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
        private static void ToastUIWithDebouncer(string msgToToast, string caseOrCode, string usernameIfApplicable = "",
            int seconds = 1)
        {
            long curTime = DateTimeOffset.Now.ToUnixTimeMilliseconds();
            
            // if it does not exist then updatedTime will be curTime.
            // If it does exist but is older than a second then updated time will also be curTime.
            // In those two cases, show the toast.
            Logger.Debug("curtime " + curTime);
            
            bool stale = false;
            long updatedTime = ToastUIDebouncer.AddOrUpdate(caseOrCode + usernameIfApplicable, curTime,
                (key, oldValue) =>
                {
                    Logger.Debug("key exists: " + (curTime - oldValue).ToString());
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

        private static System.Collections.Concurrent.ConcurrentDictionary<string, long> ToastUIDebouncer =
            new System.Collections.Concurrent.ConcurrentDictionary<string, long>();
        
        /// <param name="force">the log in layout is full of hacks. that being said force 
        ///   makes it so that if we are currently logged in to still add the logged in fragment
        ///   if not there, which makes sense. </param>
        public static void AddLoggedInLayout(View rootView = null, bool force = false)
        {
            View bttn = StaticHacks.RootView?.FindViewById<Button>(Resource.Id.buttonLogout);
            View bttnTryTwo = rootView?.FindViewById<Button>(Resource.Id.buttonLogout);
            bool bttnIsAttached = false;
            bool bttnTwoIsAttached = false;
            if (bttn != null && bttn.IsAttachedToWindow)
            {
                bttnIsAttached = true;
            }

            if (bttnTryTwo != null && bttnTryTwo.IsAttachedToWindow)
            {
                bttnTwoIsAttached = true;
            }

            if (!bttnIsAttached && !bttnTwoIsAttached && (!SeekerState.currentlyLoggedIn || force))
            {
                // THIS MEANS THAT WE STILL HAVE THE LOGINFRAGMENT NOT THE LOGGEDIN FRAGMENT
                var action1 = new Action(() =>
                {
                    (rootView as ViewGroup).AddView(
                        SeekerState.MainActivityRef.LayoutInflater.Inflate(Resource.Layout.loggedin,
                            rootView as ViewGroup, false));
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
        }

        public static void UpdateUIForLoggedIn(View rootView = null, EventHandler BttnClick = null,
            View cWelcome = null, View cbttn = null, ViewGroup cLoading = null, EventHandler SettingClick = null)
        {
            var action = new Action(() =>
            {
                // this is the case where it already has the loggedin fragment loaded.
                Button bttn = null;
                TextView welcome = null;
                ViewGroup loggingInLayout = null;
                ViewGroup logInLayout = null;

                Button settings = null;
                try
                {
                    if (StaticHacks.RootView != null && StaticHacks.RootView.IsAttachedToWindow)
                    {
                        bttn = StaticHacks.RootView.FindViewById<Button>(Resource.Id.buttonLogout);
                        welcome = StaticHacks.RootView.FindViewById<TextView>(Resource.Id.userNameView);
                        loggingInLayout = StaticHacks.RootView.FindViewById<ViewGroup>(Resource.Id.loggingInLayout);

                        logInLayout = StaticHacks.RootView.FindViewById<ViewGroup>(Resource.Id.logInLayout);

                        settings = StaticHacks.RootView.FindViewById<Button>(Resource.Id.settingsButton);
                    }
                    else
                    {
                        bttn = rootView.FindViewById<Button>(Resource.Id.buttonLogout);
                        welcome = rootView.FindViewById<TextView>(Resource.Id.userNameView);
                        loggingInLayout = rootView.FindViewById<ViewGroup>(Resource.Id.loggingInLayout);

                        logInLayout = rootView.FindViewById<ViewGroup>(Resource.Id.logInLayout);

                        settings = rootView.FindViewById<Button>(Resource.Id.settingsButton);
                    }
                }
                catch
                {

                }

                if (welcome != null)
                {
                    // meanwhile: rootView.FindViewById<TextView>(Resource.Id.userNameView).
                    // so I dont think that the welcome here is the right one.. I dont think it exists.
                    // try checking properties such as isAttachedToWindow, getWindowVisiblity etx...
                    welcome.Visibility = ViewStates.Visible;

                    bool isShown = welcome.IsShown;
                    bool isAttachedToWindow = welcome.IsAttachedToWindow;
                    bool isActivated = welcome.Activated;
                    ViewStates viewState = welcome.WindowVisibility;
                    
                    bttn.Visibility = ViewStates.Visible;
                    settings.Visibility = ViewStates.Visible;


                    settings.Click -= SettingClick;
                    settings.Click += SettingClick;
                    AndroidX.Core.View.ViewCompat.SetTranslationZ(bttn, 90);
                    bttn.Click -= BttnClick;
                    bttn.Click += BttnClick;
                    loggingInLayout.Visibility = ViewStates.Gone;
                    welcome.Text = String.Format(SeekerApplication.GetString(Resource.String.welcome),
                        SeekerState.Username);
                }
                else if (cWelcome != null)
                {
                    cWelcome.Visibility = ViewStates.Visible;
                    cbttn.Visibility = ViewStates.Visible;
                    AndroidX.Core.View.ViewCompat.SetTranslationZ(cbttn, 90);
                    cLoading.Visibility = ViewStates.Gone;
                }
                else
                {
                    StaticHacks.UpdateUI = true; // if we arent ready rn then do it when we are..
                }

                if (logInLayout != null)
                {
                    logInLayout.Visibility = ViewStates.Gone;
                    AndroidX.Core.View.ViewCompat.SetTranslationZ(
                        logInLayout.FindViewById<Button>(Resource.Id.buttonLogin), 0);
                }

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
            return (!SeekerState.currentlyLoggedIn) || SeekerState.Username == null || SeekerState.Password == null ||
                   SeekerState.Username == string.Empty;
        }
        
        public static void BackToLogInLayout(View rootView, EventHandler LogInClick, bool clearUserPass = true)
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
                        bttn = StaticHacks.RootView.FindViewById<Button>(Resource.Id.buttonLogout);
                        welcome = StaticHacks.RootView.FindViewById<TextView>(Resource.Id.userNameView);
                        loggingInLayout = StaticHacks.RootView.FindViewById<ViewGroup>(Resource.Id.loggingInLayout);

                        // this is the case we have a bad SAVED user pass....
                        try
                        {
                            logInLayout = StaticHacks.RootView.FindViewById<ViewGroup>(Resource.Id.logInLayout);
                            buttonLogin = StaticHacks.RootView.FindViewById<Button>(Resource.Id.buttonLogin);
                            if (logInLayout == null)
                            {
                                ViewGroup relLayout =
                                    SeekerState.MainActivityRef.LayoutInflater.Inflate(Resource.Layout.login,
                                        StaticHacks.RootView as ViewGroup, false) as ViewGroup;
                                relLayout.LayoutParameters =
                                    new ViewGroup.LayoutParams(StaticHacks.RootView.LayoutParameters);
                                (StaticHacks.RootView as ViewGroup).AddView(
                                    SeekerState.MainActivityRef.LayoutInflater.Inflate(Resource.Layout.login,
                                        StaticHacks.RootView as ViewGroup, false));
                            }

                            settings = StaticHacks.RootView.FindViewById<Button>(Resource.Id.settingsButton);
                            buttonLogin = StaticHacks.RootView.FindViewById<Button>(Resource.Id.buttonLogin);
                            logInLayout = StaticHacks.RootView.FindViewById<ViewGroup>(Resource.Id.logInLayout);
                            buttonLogin.Click -= LogInClick;
                            (StaticHacks.LoginFragment as Seeker.LoginFragment).rootView = StaticHacks.RootView;
                            (StaticHacks.LoginFragment as Seeker.LoginFragment).SetUpLogInLayout();
                        }
                        catch (Exception ex)
                        {
                            Logger.Debug("BackToLogInLayout" + ex.Message);
                        }

                    }
                    else
                    {
                        Logger.Debug("StaticHacks.RootView == null");
                        bttn = rootView.FindViewById<Button>(Resource.Id.buttonLogout);
                        welcome = rootView.FindViewById<TextView>(Resource.Id.userNameView);
                        loggingInLayout = rootView.FindViewById<ViewGroup>(Resource.Id.loggingInLayout);
                        logInLayout = rootView.FindViewById<ViewGroup>(Resource.Id.logInLayout);
                        buttonLogin = rootView.FindViewById<Button>(Resource.Id.buttonLogin);
                        settings = rootView.FindViewById<Button>(Resource.Id.settingsButton);
                    }
                }
                catch
                {
                    // Intentional no-op
                }

                Logger.Debug("logInLayout is here? " + (logInLayout != null).ToString());
                if (logInLayout != null)
                {
                    logInLayout.Visibility = ViewStates.Visible;
                    if (!clearUserPass && !string.IsNullOrEmpty(SeekerState.Username))
                    {
                        logInLayout.FindViewById<EditText>(Resource.Id.etUsername).Text = SeekerState.Username;
                        logInLayout.FindViewById<EditText>(Resource.Id.etPassword).Text = SeekerState.Password;
                    }

                    AndroidX.Core.View.ViewCompat.SetTranslationZ(buttonLogin, 90);

                    if (loading == null)
                    {
                        MainActivity.AddLoggedInLayout(rootView);
                        if (rootView != null)
                        {
                            bttn = rootView.FindViewById<Button>(Resource.Id.buttonLogout);
                            welcome = rootView.FindViewById<TextView>(Resource.Id.userNameView);
                            loggingInLayout = rootView.FindViewById<ViewGroup>(Resource.Id.loggingInLayout);
                            settings = rootView.FindViewById<Button>(Resource.Id.settingsButton);
                        }

                        if (rootView == null && loading == null && StaticHacks.RootView != null)
                        {
                            bttn = StaticHacks.RootView.FindViewById<Button>(Resource.Id.buttonLogout);
                            welcome = StaticHacks.RootView.FindViewById<TextView>(Resource.Id.userNameView);
                            loggingInLayout = StaticHacks.RootView.FindViewById<ViewGroup>(Resource.Id.loggingInLayout);
                            settings = StaticHacks.RootView.FindViewById<Button>(Resource.Id.settingsButton);
                        }
                    }

                    // can get nullref here!!! (at least before the .AddLoggedInLayout code..
                    loggingInLayout.Visibility = ViewStates.Gone;
                    welcome.Visibility = ViewStates.Gone;
                    settings.Visibility = ViewStates.Gone;
                    bttn.Visibility = ViewStates.Gone;
                    AndroidX.Core.View.ViewCompat.SetTranslationZ(bttn, 0);


                }

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
        
        public static void UpdateUIForLoggingInLoading(View rootView = null)
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
                        logoutButton = StaticHacks.RootView.FindViewById<Button>(Resource.Id.buttonLogout);
                        settingsButton = StaticHacks.RootView.FindViewById<Button>(Resource.Id.settingsButton);
                        welcome = StaticHacks.RootView.FindViewById<TextView>(Resource.Id.userNameView);
                        loggingInView = StaticHacks.RootView.FindViewById<ViewGroup>(Resource.Id.loggingInLayout);
                        logInLayout = StaticHacks.RootView.FindViewById<ViewGroup>(Resource.Id.logInLayout);

                    }
                    else
                    {
                        logoutButton = rootView.FindViewById<Button>(Resource.Id.buttonLogout);
                        settingsButton = rootView.FindViewById<Button>(Resource.Id.settingsButton);
                        welcome = rootView.FindViewById<TextView>(Resource.Id.userNameView);
                        loggingInView = rootView.FindViewById<ViewGroup>(Resource.Id.loggingInLayout);
                        logInLayout = rootView.FindViewById<ViewGroup>(Resource.Id.logInLayout);
                    }
                }
                catch
                {
                    // Intentional no-op
                }

                if (logInLayout != null)
                {
                    // TODO: change back..
                    //       basically when we AddChild we add it UNDER the logInLayout..
                    //       so making it gone makes everything gone... we need a root layout for it...
                    logInLayout.Visibility = ViewStates.Gone;
                    AndroidX.Core.View.ViewCompat.SetTranslationZ(
                        logInLayout.FindViewById<Button>(Resource.Id.buttonLogin), 0);
                    loggingInView.Visibility = ViewStates.Visible;
                    
                    // WE GET NULLREF HERE. FORCE connection already established exception
                    // and maybe see what is going on here...
                    welcome.Visibility = ViewStates.Gone;
                    logoutButton.Visibility = ViewStates.Gone;
                    settingsButton.Visibility = ViewStates.Gone;
                    AndroidX.Core.View.ViewCompat.SetTranslationZ(logoutButton, 0);
                }

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

        private static void CreateNoMediaFileLegacy(string atDirectory)
        {
            Java.IO.File noMediaRootFile = new Java.IO.File(atDirectory + @"/.nomedia");
            if (!noMediaRootFile.Exists())
            {
                noMediaRootFile.CreateNewFile();
            }
        }

        private static void CreateNoMediaFile(DocumentFile atDirectory)
        {
            atDirectory.CreateFile("nomedia/customnomedia", ".nomedia");
        }
        
        public static object lock_toplevel_ifexist_create = new object();
        public static object lock_album_ifexist_create = new object();

        public static System.IO.Stream GetIncompleteStream(string username, string fullfilename, int depth,
            out Android.Net.Uri incompleteUri, out Android.Net.Uri parentUri, out long partialLength)
        {
            string name = CommonHelpers.GetFileNameFromFile(fullfilename);
            string filePath = string.Empty;

            bool useDownloadDir = false;
            if (SeekerState.CreateCompleteAndIncompleteFolders && !SettingsActivity.UseIncompleteManualFolder())
            {
                useDownloadDir = true;
            }

            bool useTempDir = false;
            if (SettingsActivity.UseTempDirectory())
            {
                useTempDir = true;
            }

            bool useCustomDir = false;
            if (SettingsActivity.UseIncompleteManualFolder())
            {
                useCustomDir = true;
            }

            bool fileExists = false;
            if (SeekerState.UseLegacyStorage() && (SeekerState.RootDocumentFile == null && useDownloadDir))
            {
                System.IO.FileStream fs = null;
                Java.IO.File incompleteDir = null;
                Java.IO.File musicDir = null;
                try
                {
                    string rootdir = string.Empty;
                    rootdir = Android.OS.Environment
                        .GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryMusic).AbsolutePath;

                    if (!(new Java.IO.File(rootdir)).Exists())
                    {
                        (new Java.IO.File(rootdir)).Mkdirs();
                    }

                    string incompleteDirString = rootdir + @"/Soulseek Incomplete/";
                    lock (lock_toplevel_ifexist_create)
                    {
                        incompleteDir = new Java.IO.File(incompleteDirString);
                        if (!incompleteDir.Exists())
                        {
                            // make it and add nomedia...
                            incompleteDir.Mkdirs();
                            CreateNoMediaFileLegacy(incompleteDirString);
                        }
                    }

                    string fullDir = rootdir + @"/Soulseek Incomplete/" +
                                     CommonHelpers.GenerateIncompleteFolderName(username, fullfilename,
                                         depth); //+ @"/" + name;
                    musicDir = new Java.IO.File(fullDir);
                    lock (lock_album_ifexist_create)
                    {
                        if (!musicDir.Exists())
                        {
                            musicDir.Mkdirs();
                            CreateNoMediaFileLegacy(fullDir);
                        }
                    }

                    parentUri = Android.Net.Uri.Parse(new Java.IO.File(fullDir).ToURI().ToString());
                    filePath = fullDir + @"/" + name;
                    Java.IO.File f = new Java.IO.File(filePath);
                    fs = null;
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

                    incompleteUri =
                        Android.Net.Uri.Parse(new Java.IO.File(filePath).ToURI()
                            .ToString()); // using incompleteUri.Path gives you filePath :)
                }
                catch (Exception e)
                {
                    Logger.FirebaseDebug("Legacy Filesystem Issue: " + e.Message + e.StackTrace 
                                         + System.Environment.NewLine + incompleteDir.Exists() 
                                         + musicDir.Exists() + fileExists);
                    throw;
                }

                return fs;
            }
            else
            {
                DocumentFile folderDir1 = null; // this is the desired location.
                DocumentFile rootdir = null;

                bool diagRootDirExistsAndCanWrite = false;
                bool diagDidWeCreateSoulSeekDir = false;
                bool diagSlskDirExistsAfterCreation = false;
                bool rootDocumentFileIsNull = SeekerState.RootDocumentFile == null;

                if (rootDocumentFileIsNull)
                {
                    throw new DownloadDirectoryNotSetException();
                }

                try
                {
                    if (useDownloadDir)
                    {
                        rootdir = SeekerState.RootDocumentFile;
                        Logger.Debug("using download dir" + rootdir.Uri.LastPathSegment);
                    }
                    else if (useTempDir)
                    {
                        Java.IO.File appPrivateExternal = SeekerState.ActiveActivityRef.GetExternalFilesDir(null);
                        rootdir = DocumentFile.FromFile(appPrivateExternal);
                        Logger.Debug("using temp incomplete dir");
                    }
                    else if (useCustomDir)
                    {
                        rootdir = SeekerState.RootIncompleteDocumentFile;
                        Logger.Debug("using custom incomplete dir" + rootdir.Uri.LastPathSegment);
                    }
                    else
                    {
                        Logger.FirebaseDebug("!! should not get here, no dirs");
                    }

                    if (!rootdir.Exists())
                    {
                        Logger.FirebaseDebug("rootdir (nonnull) does not exist: " + rootdir.Uri);
                        diagRootDirExistsAndCanWrite = false;
                    }
                    else if (!rootdir.CanWrite())
                    {
                        diagRootDirExistsAndCanWrite = false;
                        Logger.FirebaseDebug("rootdir (nonnull) exists but cant write: " + rootdir.Uri);
                    }
                    else
                    {
                        diagRootDirExistsAndCanWrite = true;
                    }

                    DocumentFile slskDir1 = null;
                    lock (lock_toplevel_ifexist_create)
                    {
                        slskDir1 = rootdir.FindFile("Soulseek Incomplete"); //does Soulseek Complete folder exist
                        if (slskDir1 == null || !slskDir1.Exists())
                        {
                            slskDir1 = rootdir.CreateDirectory("Soulseek Incomplete");
                            if (slskDir1 == null)
                            {
                                string diagMessage = CheckPermissions(rootdir.Uri);
                                Logger.FirebaseDebug("slskDir1 is null" + rootdir.Uri + "parent: " + diagMessage);
                                Logger.FirebaseInfo("slskDir1 is null" + rootdir.Uri + "parent: " + diagMessage);
                            }
                            else if (!slskDir1.Exists())
                            {
                                Logger.FirebaseDebug("slskDir1 does not exist" + rootdir.Uri);
                            }
                            else if (!slskDir1.CanWrite())
                            {
                                Logger.FirebaseDebug("slskDir1 cannot write" + rootdir.Uri);
                            }

                            CreateNoMediaFile(slskDir1);
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
                        CommonHelpers.GenerateIncompleteFolderName(username, fullfilename, depth);
                    
                    lock (lock_album_ifexist_create)
                    {
                        folderDir1 = slskDir1.FindFile(album_folder_name); // does the folder we want to save to exist
                        if (folderDir1 == null || !folderDir1.Exists())
                        {
                            folderDir1 = slskDir1.CreateDirectory(album_folder_name);
                            if (folderDir1 == null)
                            {
                                string rootUri = string.Empty;
                                if (SeekerState.RootDocumentFile != null)
                                {
                                    rootUri = SeekerState.RootDocumentFile.Uri.ToString();
                                }

                                bool slskDirExistsWriteable = false;
                                if (slskDir1 != null)
                                {
                                    slskDirExistsWriteable = slskDir1.Exists() && slskDir1.CanWrite();
                                }

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

                            CreateNoMediaFile(folderDir1);
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

                if (rootdir == null && !SeekerState.UseLegacyStorage())
                {
                    SeekerState.MainActivityRef.RunOnUiThread(() =>
                    {
                        ToastUi.Long(
                            SeekerState.MainActivityRef.GetString(Resource.String.seeker_cannot_access_files));
                    });
                }

                // BACKUP IF FOLDER DIR IS NULL
                if (folderDir1 == null)
                {
                    folderDir1 = rootdir; // use the root instead..
                }

                parentUri = folderDir1.Uri;

                filePath = folderDir1.Uri + @"/" + name;

                System.IO.Stream stream = null;
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
            if (SeekerState.ActiveActivityRef != null)
            {
                var cursor = SeekerState.ActiveActivityRef.ContentResolver.Query(folder,
                    new string[] { DocumentsContract.Document.ColumnFlags }, null, null, null);
                int flags = 0;
                if (cursor.MoveToFirst())
                {
                    flags = cursor.GetInt(0);
                }

                cursor.Close();
                bool canWrite = (flags & (int)DocumentContractFlags.SupportsWrite) != 0;
                bool canDirCreate = (flags & (int)DocumentContractFlags.DirSupportsCreate) != 0;
                if (canWrite && canDirCreate)
                {
                    return "Can Write and DirSupportsCreate";
                }
                else if (canWrite)
                {
                    return "Can Write and not DirSupportsCreate";
                }
                else if (canDirCreate)
                {
                    return "Can not Write and can DirSupportsCreate";
                }
                else
                {
                    return "No permissions";
                }
            }

            return string.Empty;
        }

        private static string SaveToFile(
            string fullfilename,
            string username,
            byte[] bytes,
            Android.Net.Uri uriOfIncomplete,
            Android.Net.Uri parentUriOfIncomplete,
            bool memoryMode,
            int depth,
            bool noSubFolder,
            out string finalUri)
        {
            string name = CommonHelpers.GetFileNameFromFile(fullfilename);
            string dir = Common.Helpers.GetFolderNameFromFile(fullfilename, depth);
            string filePath = string.Empty;

            if (memoryMode && (bytes == null || bytes.Length == 0))
            {
                Logger.FirebaseDebug("EMPTY or NULL BYTE ARRAY in mem mode");
            }

            if (!memoryMode && uriOfIncomplete == null)
            {
                Logger.FirebaseDebug("no URI in file mode");
            }

            finalUri = string.Empty;
            if (SeekerState.UseLegacyStorage() &&
                (SeekerState.RootDocumentFile == null &&
                 // if the user didnt select a complete OR incomplete directory. i.e. pure java files.
                 !SettingsActivity.UseIncompleteManualFolder()))  
            {
                // this method works just fine if coming from a temp dir.  just not a open doc tree dir.
                string rootdir = string.Empty;
                
                rootdir = Android.OS.Environment
                    .GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryMusic).AbsolutePath;
                
                if (!(new Java.IO.File(rootdir)).Exists())
                {
                    (new Java.IO.File(rootdir)).Mkdirs();
                }

                string intermediateFolder = @"/";
                if (SeekerState.CreateCompleteAndIncompleteFolders)
                {
                    intermediateFolder = @"/Soulseek Complete/";
                }

                if (SeekerState.CreateUsernameSubfolders)
                {
                    // TODO: escape? slashes? etc... can easily test by just setting username to '/' in debugger
                    intermediateFolder = intermediateFolder + username + @"/";
                }

                string fullDir = rootdir + intermediateFolder + (noSubFolder ? "" : dir); // + @"/" + name;
                Java.IO.File musicDir = new Java.IO.File(fullDir);
                musicDir.Mkdirs();
                filePath = fullDir + @"/" + name;
                Java.IO.File musicFile = new Java.IO.File(filePath);
                FileOutputStream stream = new FileOutputStream(musicFile);
                finalUri = musicFile.ToURI().ToString();
                
                if (memoryMode)
                {
                    stream.Write(bytes);
                    stream.Close();
                }
                else
                {
                    Java.IO.File inFile = new Java.IO.File(uriOfIncomplete.Path);
                    Java.IO.File inDir = new Java.IO.File(parentUriOfIncomplete.Path);
                    MoveFile(new FileInputStream(inFile), stream, inFile, inDir);
                }
            }
            else
            {
                bool useLegacyDocFileToJavaFileOverride = false;
                DocumentFile legacyRootDir = null;
                if (SeekerState.UseLegacyStorage() && SeekerState.RootDocumentFile == null &&
                    SettingsActivity.UseIncompleteManualFolder())
                {
                    // this means that even though rootfile is null, manual folder is set and is a docfile.
                    // so we must wrap the default root doc file.
                    string legacyRootdir = string.Empty;

                    legacyRootdir = Android.OS.Environment
                        .GetExternalStoragePublicDirectory(Android.OS.Environment.DirectoryMusic).AbsolutePath;

                    Java.IO.File legacyRoot = (new Java.IO.File(legacyRootdir));
                    if (!legacyRoot.Exists())
                    {
                        legacyRoot.Mkdirs();
                    }

                    legacyRootDir = DocumentFile.FromFile(legacyRoot);

                    useLegacyDocFileToJavaFileOverride = true;

                }

                DocumentFile folderDir1 = null; // this is the desired location.
                DocumentFile rootdir = null;

                bool diagRootDirExists = true;
                bool diagDidWeCreateSoulSeekDir = false;
                bool diagSlskDirExistsAfterCreation = true;
                bool rootDocumentFileIsNull = SeekerState.RootDocumentFile == null;
                try
                {
                    rootdir = SeekerState.RootDocumentFile;

                    if (useLegacyDocFileToJavaFileOverride)
                    {
                        rootdir = legacyRootDir;
                    }

                    if (!rootdir.Exists())
                    {
                        diagRootDirExists = false;
                    }

                    DocumentFile slskDir1 = null;
                    if (SeekerState.CreateCompleteAndIncompleteFolders)
                    {
                        slskDir1 = rootdir.FindFile("Soulseek Complete"); // does Soulseek Complete folder exist
                        if (slskDir1 == null || !slskDir1.Exists())
                        {
                            slskDir1 = rootdir.CreateDirectory("Soulseek Complete");
                            Logger.Debug("Creating Soulseek Complete");
                            diagDidWeCreateSoulSeekDir = true;
                        }

                        if (slskDir1 == null)
                        {
                            diagSlskDirExistsAfterCreation = false;
                        }
                        else if (!slskDir1.Exists())
                        {
                            diagSlskDirExistsAfterCreation = false;
                        }
                    }
                    else
                    {
                        slskDir1 = rootdir;
                    }

                    bool diagUsernameDirExistsAfterCreation = false;
                    bool diagDidWeCreateUsernameDir = false;
                    if (SeekerState.CreateUsernameSubfolders)
                    {
                        DocumentFile tempUsernameDir1 = null;
                        lock (string.Intern("IfNotExistCreateAtomic_1"))
                        {
                            tempUsernameDir1 = slskDir1.FindFile(username); // does username folder exist
                            if (tempUsernameDir1 == null || !tempUsernameDir1.Exists())
                            {
                                tempUsernameDir1 = slskDir1.CreateDirectory(username);
                                Logger.Debug(string.Format("Creating {0} dir", username));
                                diagDidWeCreateUsernameDir = true;
                            }
                        }

                        if (tempUsernameDir1 == null)
                        {
                            diagUsernameDirExistsAfterCreation = false;
                        }
                        else if (!slskDir1.Exists())
                        {
                            diagUsernameDirExistsAfterCreation = false;
                        }
                        else
                        {
                            diagUsernameDirExistsAfterCreation = true;
                        }

                        slskDir1 = tempUsernameDir1;
                    }

                    if (depth == 1)
                    {
                        if (noSubFolder)
                        {
                            folderDir1 = slskDir1;
                        }
                        else
                        {
                            lock (string.Intern("IfNotExistCreateAtomic_2"))
                            {
                                folderDir1 = slskDir1.FindFile(dir); // does the folder we want to save to exist
                                if (folderDir1 == null || !folderDir1.Exists())
                                {
                                    Logger.Debug("Creating " + dir);
                                    folderDir1 = slskDir1.CreateDirectory(dir);
                                }

                                if (folderDir1 == null || !folderDir1.Exists())
                                {
                                    Logger.FirebaseDebug("folderDir is null or does not exists");
                                }
                            }
                        }
                    }
                    else
                    {
                        DocumentFile folderDirNext = null;
                        folderDir1 = slskDir1;
                        int _depth = depth;
                        while (_depth > 0)
                        {
                            var parts = dir.Split('\\');
                            string singleDir = parts[parts.Length - _depth];
                            lock (string.Intern("IfNotExistCreateAtomic_3"))
                            {
                                folderDirNext =
                                    folderDir1.FindFile(singleDir); // does the folder we want to save to exist
                                if (folderDirNext == null || !folderDirNext.Exists())
                                {
                                    Logger.Debug("Creating " + dir);
                                    folderDirNext = folderDir1.CreateDirectory(singleDir);
                                }

                                if (folderDirNext == null || !folderDirNext.Exists())
                                {
                                    Logger.FirebaseDebug("folderDir is null or does not exists, depth" + _depth);
                                }
                            }

                            folderDir1 = folderDirNext;
                            _depth--;
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.FirebaseDebug("Filesystem Issue: " + e.Message + diagSlskDirExistsAfterCreation +
                                             diagRootDirExists + diagDidWeCreateSoulSeekDir + rootDocumentFileIsNull +
                                             SeekerState.CreateUsernameSubfolders);
                }

                if (rootdir == null && !SeekerState.UseLegacyStorage())
                {
                    SeekerState.ActiveActivityRef.RunOnUiThread(() =>
                    {
                        ToastUi.Long(
                            SeekerState.MainActivityRef.GetString(Resource.String.seeker_cannot_access_files));
                    });
                }

                // BACKUP IF FOLDER DIR IS NULL
                if (folderDir1 == null)
                {
                    folderDir1 = rootdir; // use the root instead..
                }

                filePath = folderDir1.Uri + @"/" + name;
                
                if (memoryMode)
                {
                    DocumentFile mFile = CommonHelpers.CreateMediaFile(folderDir1, name);
                    finalUri = mFile.Uri.ToString();
                    System.IO.Stream stream = SeekerState.ActiveActivityRef.ContentResolver.OpenOutputStream(mFile.Uri);
                    stream.Write(bytes);
                    stream.Close();
                }
                else
                {

                    //106ms for 32mb
                    Android.Net.Uri uri = null;
                    if (SeekerState.PreMoveDocument() ||
                        SettingsActivity
                            .UseTempDirectory() || //i.e. if use temp dir which is file: // rather than content: //
                        (SeekerState.UseLegacyStorage() && SettingsActivity.UseIncompleteManualFolder() &&
                         SeekerState.RootDocumentFile ==
                         null) || //i.e. if use complete dir is file: // rather than content: // but Incomplete is content: //
                        CommonHelpers.CompleteIncompleteDifferentVolume() ||
                        !SeekerState.ManualIncompleteDataDirectoryUriIsFromTree ||
                        !SeekerState.SaveDataDirectoryUriIsFromTree)
                    {
                        try
                        {
                            DocumentFile mFile = CommonHelpers.CreateMediaFile(folderDir1, name);
                            uri = mFile.Uri;
                            finalUri = mFile.Uri.ToString();
                            System.IO.Stream stream =
                                SeekerState.ActiveActivityRef.ContentResolver.OpenOutputStream(mFile.Uri);
                            MoveFile(SeekerState.ActiveActivityRef.ContentResolver.OpenInputStream(uriOfIncomplete),
                                stream, uriOfIncomplete, parentUriOfIncomplete);
                        }
                        catch (Exception e)
                        {
                            Logger.FirebaseDebug("CRITICAL FILESYSTEM ERROR pre" + e.Message);
                            SeekerApplication.ShowToast("Error Saving File", ToastLength.Long);
                            Logger.Debug(e.Message + " " + uriOfIncomplete.Path);
                        }
                    }
                    else
                    {
                        try
                        {
                            string realName = string.Empty;
                            
                            // fix due to above^  otherwise "Play File" silently fails
                            if (SettingsActivity.UseIncompleteManualFolder())
                            {
                                // dont use name!!! in my case the name was .m4a but the actual file was .mp3!!
                                var df = DocumentFile.FromSingleUri(SeekerState.ActiveActivityRef, uriOfIncomplete);
                                realName = df.Name;
                            }

                            uri = DocumentsContract.MoveDocument(SeekerState.ActiveActivityRef.ContentResolver,
                                uriOfIncomplete, parentUriOfIncomplete, folderDir1.Uri); // ADDED IN API 24!!
                            DeleteParentIfEmpty(DocumentFile.FromTreeUri(SeekerState.ActiveActivityRef,
                                parentUriOfIncomplete));
                            
                            // "/tree/primary:musictemp/document/primary:music2/J when two different uri trees the
                            // uri returned from move document is a mismash of the two...
                            // even tho it actually moves it correctly.
                            // folderDir1.FindFile(name).Uri.Path is right uri and IsFile returns true...
                            
                            // fix due to above^  otherwise "Play File" silently fails
                            if (SettingsActivity.UseIncompleteManualFolder())
                            {
                                // dont use name!!! in my case the name was .m4a but the actual file was .mp3!!
                                uri = folderDir1.FindFile(realName).Uri;
                            }
                        }
                        catch (Exception e)
                        {
                            // move document fails if two different volumes:
                            // "Failed to move to /storage/1801-090D/Music/Soulseek Complete/folder/song.mp3"
                            // {content://com.android.externalstorage.documents/tree/primary%3A/document/primary%3ASoulseek%20Incomplete%2F/****.mp3}
                            // content://com.android.externalstorage.documents/tree/1801-090D%3AMusic/document/1801-090D%3AMusic%2FSoulseek%20Complete%2F/****}
                            if (e.Message.ToLower().Contains("already exists"))
                            {
                                try
                                {
                                    // set the uri to the existing file...
                                    var df = DocumentFile.FromSingleUri(SeekerState.ActiveActivityRef, uriOfIncomplete);
                                    string realName = df.Name;
                                    uri = folderDir1.FindFile(realName).Uri;

                                    if (folderDir1.Uri == parentUriOfIncomplete)
                                    {
                                        // case where SDCARD was full - all files were 0 bytes, folders could not
                                        // be created, documenttree.CreateDirectory() returns null.
                                        // no errors until you tried to move it. then you would ge "alreay exists"
                                        // since (if Create Complete and Incomplete folders is checked and 
                                        // the incomplete dir isnt changed) then the destination is the same as the
                                        // incomplete file (since the incomplete and complete folders
                                        // couldnt be created.
                                        // This error is misleading though so do a more generic error.
                                        SeekerApplication.ShowToast($"Filesystem Error for file {realName}.",
                                            ToastLength.Long);
                                        
                                        Logger.Debug("complete and incomplete locations are the same");
                                    }
                                    else
                                    {
                                        SeekerApplication.ShowToast(
                                            string.Format(
                                                "File {0} already exists at {1}.  Delete it and try again " +
                                                "if you want to overwrite it.",
                                                realName, uri.LastPathSegment.ToString()), ToastLength.Long);
                                    }
                                }
                                catch (Exception e2)
                                {
                                    Logger.FirebaseDebug("CRITICAL FILESYSTEM ERROR errorhandling " + e2.Message);
                                }

                            }
                            else
                            {
                                if (uri == null) // this means doc file failed (else it would be after)
                                {
                                    Logger.FirebaseInfo("uri==null");
                                    
                                    // lets try with the non MoveDocument way.
                                    // this case can happen (for a legitimate reason) if:
                                    //  the user is on api <29.  they start downloading an album.
                                    // then while its downloading they set the download directory.
                                    // the manual one will be file:\\ but the end location will be content:\\
                                    try
                                    {

                                        DocumentFile mFile = CommonHelpers.CreateMediaFile(folderDir1, name);
                                        uri = mFile.Uri;
                                        finalUri = mFile.Uri.ToString();
                                        Logger.FirebaseInfo("retrying: incomplete: " + uriOfIncomplete +
                                                                     " complete: " + finalUri + " parent: " +
                                                                     parentUriOfIncomplete);
                                        System.IO.Stream stream =
                                            SeekerState.ActiveActivityRef.ContentResolver.OpenOutputStream(mFile.Uri);
                                        MoveFile(
                                            SeekerState.ActiveActivityRef.ContentResolver.OpenInputStream(
                                                uriOfIncomplete), stream, uriOfIncomplete, parentUriOfIncomplete);
                                    }
                                    catch (Exception secondTryErr)
                                    {
                                        Logger.FirebaseDebug(
                                            "Legacy backup failed - CRITICAL FILESYSTEM ERROR pre" +
                                            secondTryErr.Message);
                                        SeekerApplication.ShowToast("Error Saving File", ToastLength.Long);
                                        Logger.Debug(secondTryErr.Message + " " + uriOfIncomplete.Path);
                                    }
                                }
                                else
                                {
                                    Logger.FirebaseInfo("uri!=null");
                                    Logger.FirebaseDebug("CRITICAL FILESYSTEM ERROR " + e.Message +
                                                             " path child: " +
                                                             Android.Net.Uri.Decode(uriOfIncomplete.ToString()) +
                                                             " path parent: " +
                                                             Android.Net.Uri.Decode(parentUriOfIncomplete.ToString()) +
                                                             " path dest: " +
                                                             Android.Net.Uri.Decode(folderDir1?.Uri?.ToString()));
                                    SeekerApplication.ShowToast("Error Saving File", ToastLength.Long);
                                    
                                    // Unknown Authority happens when source is
                                    // file :/// storage/emulated/0/Android/data/com.companyname.andriodapp1/files/Soulseek%20Incomplete/
                                    Logger.Debug(e.Message + " " + uriOfIncomplete.Path);
                                }
                            }
                        }
                        // throws "no static method with name='moveDocument' signature='(Landroid/content/ContentResolver;Landroid/net/Uri;Landroid/net/Uri;Landroid/net/Uri;)Landroid/net/Uri;' in class Landroid/provider/DocumentsContract;"
                    }

                    if (uri == null)
                    {
                        Logger.FirebaseDebug("DocumentsContract MoveDocument FAILED, override incomplete: " +
                                    SeekerState.OverrideDefaultIncompleteLocations);
                    }

                    finalUri = uri.ToString();
                }
            }

            return filePath;
        }

        private static void MoveFile(System.IO.Stream from, System.IO.Stream to, Android.Net.Uri toDelete,
            Android.Net.Uri parentToDelete)
        {
            byte[] buffer = new byte[4096];
            int read;
            while ((read = from.Read(buffer)) != 0) // C# does 0 for you've reached the end!
            {
                to.Write(buffer, 0, read);
            }

            from.Close();
            to.Flush();
            to.Close();

            if (SeekerState.PreOpenDocumentTree() || SettingsActivity.UseTempDirectory() || toDelete.Scheme == "file")
            {
                try
                {
                    if (!(new Java.IO.File(toDelete.Path)).Delete())
                    {
                        Logger.FirebaseDebug("Java.IO.File.Delete() failed to delete");
                    }
                }
                catch (Exception e)
                {
                    Logger.FirebaseDebug("Java.IO.File.Delete() threw" + e.Message + e.StackTrace);
                }
            }
            else
            {
                DocumentFile
                    df = DocumentFile.FromSingleUri(SeekerState.ActiveActivityRef,
                        toDelete); // this returns a file that doesnt exist with file ://

                if (!df.Delete()) // on API 19 this seems to always fail..
                {
                    Logger.FirebaseDebug("df.Delete() failed to delete");
                }
            }

            DocumentFile parent = null;
            if (SeekerState.PreOpenDocumentTree() || SettingsActivity.UseTempDirectory() ||
                parentToDelete.Scheme == "file")
            {
                parent = DocumentFile.FromFile(new Java.IO.File(parentToDelete.Path));
            }
            else
            {
                // if from single uri then listing files will give unsupported operation exception...
                // if temp (file: //)this will throw (which makes sense as it did not come from open tree uri)
                parent = DocumentFile.FromTreeUri(SeekerState.ActiveActivityRef,
                    parentToDelete);
            }

            DeleteParentIfEmpty(parent);
        }

        public static void DeleteParentIfEmpty(DocumentFile parent)
        {
            if (parent == null)
            {
                Logger.FirebaseDebug("null parent");
                return;
            }
            
            try
            {
                if (parent.ListFiles().Length == 1 && parent.ListFiles()[0].Name == ".nomedia")
                {
                    if (!parent.ListFiles()[0].Delete())
                    {
                        Logger.FirebaseDebug("parent.Delete() failed to delete .nomedia child...");
                    }

                    if (!parent.Delete())
                    {
                        Logger.FirebaseDebug("parent.Delete() failed to delete parent");
                    }
                }
            }
            catch (Exception ex)
            {
                // race condition between checking length of ListFiles() and indexing [0] (twice)
                if (!ex.Message.Contains("Index was outside"))
                {
                    throw ex; // this might be important..
                }
            }
        }


        public static void DeleteParentIfEmpty(Java.IO.File parent)
        {
            if (parent.ListFiles().Length == 1 && parent.ListFiles()[0].Name == ".nomedia")
            {
                if (!parent.ListFiles()[0].Delete())
                {
                    Logger.FirebaseDebug("LEGACY parent.Delete() failed to delete .nomedia child...");
                }

                // this returns false... maybe delete .nomedia child??? YUP.  cannot delete non empty dir...
                if (!parent.Delete())
                {
                    Logger.FirebaseDebug("LEGACY parent.Delete() failed to delete parent");
                }
            }
        }


        private static void MoveFile(Java.IO.FileInputStream from, Java.IO.FileOutputStream to, Java.IO.File toDelete,
            Java.IO.File parent)
        {
            byte[] buffer = new byte[4096];
            int read;
            while ((read = from.Read(buffer)) != -1) // unlike C# this method does -1 for no more bytes left..
            {
                to.Write(buffer, 0, read);
            }

            from.Close();
            to.Flush();
            to.Close();
            if (!toDelete.Delete())
            {
                Logger.FirebaseDebug("LEGACY df.Delete() failed to delete ()");
            }

            DeleteParentIfEmpty(parent);
        }

        private static void SaveFileToMediaStore(string path)
        {
            Intent mediaScanIntent = new Intent(Intent.ActionMediaScannerScanFile);
            Java.IO.File f = new Java.IO.File(path);
            Android.Net.Uri contentUri = Android.Net.Uri.FromFile(f);
            mediaScanIntent.SetData(contentUri);
            SeekerState.ActiveActivityRef.ApplicationContext.SendBroadcast(mediaScanIntent);
        }
        
        protected override void OnPause()
        {
            base.OnPause();

            TransfersFragment.SaveTransferItems(sharedPreferences);
            lock (SHARED_PREF_LOCK)
            {
                var editor = sharedPreferences.Edit();
                editor.PutBoolean(KeyConsts.M_CurrentlyLoggedIn, SeekerState.currentlyLoggedIn);
                editor.PutString(KeyConsts.M_Username, SeekerState.Username);
                editor.PutString(KeyConsts.M_Password, SeekerState.Password);
                editor.PutString(KeyConsts.M_SaveDataDirectoryUri, SeekerState.SaveDataDirectoryUri);
                editor.PutBoolean(KeyConsts.M_SaveDataDirectoryUriIsFromTree,
                    SeekerState.SaveDataDirectoryUriIsFromTree);
                editor.PutInt(KeyConsts.M_NumberSearchResults, SeekerState.NumberSearchResults);
                editor.PutInt(KeyConsts.M_DayNightMode, SeekerState.DayNightMode);
                editor.PutBoolean(KeyConsts.M_AutoClearComplete, SeekerState.AutoClearCompleteDownloads);
                editor.PutBoolean(KeyConsts.M_AutoClearCompleteUploads, SeekerState.AutoClearCompleteUploads);
                editor.PutBoolean(KeyConsts.M_RememberSearchHistory, SeekerState.RememberSearchHistory);
                editor.PutBoolean(KeyConsts.M_RememberUserHistory, SeekerState.ShowRecentUsers);
                editor.PutBoolean(KeyConsts.M_TransfersShowSizes, SeekerState.TransferViewShowSizes);
                editor.PutBoolean(KeyConsts.M_TransfersShowSpeed, SeekerState.TransferViewShowSpeed);
                editor.PutBoolean(KeyConsts.M_OnlyFreeUploadSlots, SeekerState.FreeUploadSlotsOnly);
                editor.PutBoolean(KeyConsts.M_HideLockedSearch, SeekerState.HideLockedResultsInSearch);
                editor.PutBoolean(KeyConsts.M_HideLockedBrowse, SeekerState.HideLockedResultsInBrowse);
                editor.PutBoolean(KeyConsts.M_FilterSticky, SearchFragment.FilterSticky);
                editor.PutString(KeyConsts.M_FilterStickyString, SearchTabHelper.FilterString);
                editor.PutBoolean(KeyConsts.M_MemoryBackedDownload, SeekerState.MemoryBackedDownload);
                editor.PutInt(KeyConsts.M_SearchResultStyle, (int)(SearchFragment.SearchResultStyle));
                editor.PutBoolean(KeyConsts.M_DisableToastNotifications, SeekerState.DisableDownloadToastNotification);
                editor.PutInt(KeyConsts.M_UploadSpeed, SeekerState.UploadSpeed);
                editor.PutBoolean(KeyConsts.M_SharingOn, SeekerState.SharingOn);
                editor.PutBoolean(KeyConsts.M_AllowPrivateRooomInvitations, SeekerState.AllowPrivateRoomInvitations);

                if (SeekerState.UserList != null)
                {
                    editor.PutString(KeyConsts.M_UserList,
                        SerializationHelper.SaveUserListToString(SeekerState.UserList));
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
            
            if (SeekerState.UserList != null)
            {
                outState.PutString(KeyConsts.M_UserList,
                    SerializationHelper.SaveUserListToString(SeekerState.UserList));
            }

        }

        private void Tabs_TabSelected(object sender, TabLayout.TabSelectedEventArgs e)
        {
            System.Console.WriteLine(e.Tab.Position);
            if (e.Tab.Position != 1) //i.e. if we are not the search tab
            {
                try
                {
                    Android.Views.InputMethods.InputMethodManager imm =
                        (Android.Views.InputMethods.InputMethodManager)this.GetSystemService(
                            Context.InputMethodService);
                    imm.HideSoftInputFromWindow((sender as View).WindowToken, 0);
                }
                catch
                {
                    // not worth throwing over
                }
            }
        }

        public static int goToSearchTab = int.MaxValue;

        private void Pager_PageSelected(object sender, ViewPager.PageSelectedEventArgs e)
        {
            // if we are changing modes and the transfers action mode is not null (i.e. is active)
            // then we need to get out of it.
            if (TransfersFragment.TransfersActionMode != null)
            {
                TransfersFragment.TransfersActionMode.Finish();
            }

            // in addition each fragment is responsible for expanding their menu...
            if (e.Position == 0)
            {
                this.SupportActionBar.SetDisplayHomeAsUpEnabled(false);
                this.SupportActionBar.SetHomeButtonEnabled(false);

                this.SupportActionBar.SetDisplayShowCustomEnabled(false);
                this.SupportActionBar.SetDisplayShowTitleEnabled(true);
                this.SupportActionBar.Title = this.GetString(Resource.String.home_tab);
                this.FindViewById<AndroidX.AppCompat.Widget.Toolbar>(Resource.Id.toolbar)
                    .InflateMenu(Resource.Menu.account_menu);
            }

            if (e.Position == 1) // search
            {
                this.SupportActionBar.SetDisplayHomeAsUpEnabled(false);
                this.SupportActionBar.SetHomeButtonEnabled(false);
                
                this.SupportActionBar.SetDisplayShowCustomEnabled(true);
                this.SupportActionBar.SetDisplayShowTitleEnabled(false);
                this.SupportActionBar.SetCustomView(Resource.Layout.custom_menu_layout);
                SearchFragment.ConfigureSupportCustomView(this.SupportActionBar.CustomView /*, this*/);
                this.FindViewById<AndroidX.AppCompat.Widget.Toolbar>(Resource.Id.toolbar)
                    .InflateMenu(Resource.Menu.account_menu);
                
                if (goToSearchTab != int.MaxValue)
                {
                    // this happens if we come from settings activity.
                    // Main Activity has NOT been started.
                    // SearchFragment has the .Actvity ref of an OLD activity.
                    // so we are not ready yet. 
                    if (SearchFragment.Instance?.Activity == null ||
                        !(SearchFragment.Instance.Activity.Lifecycle.CurrentState
                            .IsAtLeast(Lifecycle.State
                                .Started)))
                    {
                        //let onresume go to the search tab..
                        Logger.Debug("Delay Go To Wishlist Search Fragment for OnResume");
                    }
                    else
                    {
                        // can we do this now??? or should we pass this down to the search fragment
                        // for when it gets created...  maybe we should put this in a like "OnResume"
                        Logger.Debug("Do Go To Wishlist in page selected");
                        SearchFragment.Instance.GoToTab(goToSearchTab, false, true);
                        goToSearchTab = int.MaxValue;
                    }
                }
            }
            else if (e.Position == 2)
            {


                this.SupportActionBar.SetDisplayShowCustomEnabled(false);
                this.SupportActionBar.SetDisplayShowTitleEnabled(true);


                SetTransferSupportActionBarState();

                this.FindViewById<AndroidX.AppCompat.Widget.Toolbar>(Resource.Id.toolbar)
                    .InflateMenu(Resource.Menu.browse_menu_empty); //todo remove?
            }
            else if (e.Position == 3)
            {
                this.SupportActionBar.SetDisplayHomeAsUpEnabled(false);
                this.SupportActionBar.SetHomeButtonEnabled(false);

                this.SupportActionBar.SetDisplayShowCustomEnabled(false);
                this.SupportActionBar.SetDisplayShowTitleEnabled(true);
                if (string.IsNullOrEmpty(BrowseFragment.CurrentUsername))
                {
                    this.SupportActionBar.Title = this.GetString(Resource.String.browse_tab);
                }
                else
                {
                    this.SupportActionBar.Title = this.GetString(Resource.String.browse_tab) + ": " +
                                                  BrowseFragment.CurrentUsername;
                }

                this.FindViewById<AndroidX.AppCompat.Widget.Toolbar>(Resource.Id.toolbar)
                    .InflateMenu(Resource.Menu.transfers_menu);
            }
        }

        public void SetTransferSupportActionBarState()
        {
            if (TransfersFragment.InUploadsMode)
            {
                if (TransfersFragment.CurrentlySelectedUploadFolder == null)
                {
                    this.SupportActionBar.Title = this.GetString(Resource.String.Uploads);
                    this.SupportActionBar.SetDisplayHomeAsUpEnabled(false);
                    this.SupportActionBar.SetHomeButtonEnabled(false);
                }
                else
                {
                    this.SupportActionBar.Title = TransfersFragment.CurrentlySelectedUploadFolder.FolderName;
                    this.SupportActionBar.SetDisplayHomeAsUpEnabled(true);
                    this.SupportActionBar.SetHomeButtonEnabled(true);
                }
            }
            else
            {
                if (TransfersFragment.CurrentlySelectedDLFolder == null)
                {
                    this.SupportActionBar.Title = this.GetString(Resource.String.Downloads);
                    this.SupportActionBar.SetDisplayHomeAsUpEnabled(false);
                    this.SupportActionBar.SetHomeButtonEnabled(false);
                }
                else
                {
                    this.SupportActionBar.Title = TransfersFragment.CurrentlySelectedDLFolder.FolderName;
                    this.SupportActionBar.SetDisplayHomeAsUpEnabled(true);
                    this.SupportActionBar.SetHomeButtonEnabled(true);
                }
            }
        }

        private class OnPageChangeLister1 : Java.Lang.Object, ViewPager.IOnPageChangeListener
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
                BottomNavigationView navigator =
                    SeekerState.MainActivityRef?.FindViewById<BottomNavigationView>(Resource.Id.navigation);

                if (position != -1 && navigator != null)
                {
                    AndroidX.AppCompat.View.Menu.MenuBuilder menu =
                        navigator.Menu as AndroidX.AppCompat.View.Menu.MenuBuilder;

                    menu.GetItem(position).SetCheckable(true); // this is necessary if side scrolling...
                    menu.GetItem(position).SetChecked(true);
                }
            }
        }

        public override bool OnCreateOptionsMenu(IMenu menu)
        {
            return base.OnCreateOptionsMenu(menu);
        }

        public override void OnRequestPermissionsResult(
            int requestCode,
            string[] permissions,
            [GeneratedEnum] Android.Content.PM.Permission[] grantResults)
        {
            Xamarin.Essentials.Platform.OnRequestPermissionsResult(requestCode, permissions, grantResults);
            switch (requestCode)
            {
                case POST_NOTIFICATION_PERMISSION:
                    break;
                default:
                    if (grantResults.Length > 0 && grantResults[0] == Permission.Granted)
                    {
                        return;
                    }
                    else
                    {
                        // TODO: - why?? this was added in initial commit. kills process if permission not granted?
                        FinishAndRemoveTask();
                    }

                    break;
            }

            base.OnRequestPermissionsResult(requestCode, permissions, grantResults);
        }

        public bool OnNavigationItemSelected(IMenuItem item)
        {
            switch (item.ItemId)
            {
                case Resource.Id.navigation_home:
                    pager.CurrentItem = 0;
                    break;
                case Resource.Id.navigation_search:
                    pager.CurrentItem = 1;
                    break;
                case Resource.Id.navigation_transfers:
                    pager.CurrentItem = 2;
                    break;
                case Resource.Id.navigation_browse:
                    pager.CurrentItem = 3;
                    break;
            }

            return true;
        }
    }
}
