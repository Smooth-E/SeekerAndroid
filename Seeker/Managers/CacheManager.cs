using System;
using System.Collections.Generic;
using System.Linq;
using Android.Content;
using AndroidX.DocumentFile.Provider;
using Seeker.Exceptions;
using Seeker.Models;
using Seeker.Utils;
using SlskHelp;
using Soulseek;
using Directory = Soulseek.Directory;
using File = Java.IO.File;

namespace Seeker.Managers;

// TODO: Understand the purpose of this class and come up with a better name
public static class CacheManager
{
    private static CachedParseResults GetCachedParseResults(Context c)
    {
        File fileshare_dir = new File(c.FilesDir, KeyConsts.M_fileshare_cache_dir);
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
        var fileshareDir = new File(c.FilesDir, KeyConsts.M_fileshare_cache_dir);
        if (!fileshareDir.Exists())
        {
            return;
        }

        var files = fileshareDir.ListFiles();
        if (files is null)
        {
            return;
        }
        
        foreach (var file in files)
        {
            file.Delete();
        }
    }
    
    static T deserializeFromDisk<T>(
        Context c,
        File dir,
        string filename,
        MessagePack.MessagePackSerializerOptions options = null) where T : class
    {
        var fileForOurInternalStorage = new File(dir, filename);

        if (!fileForOurInternalStorage.Exists())
        {
            return null;
        }

        using var inputStream = c.ContentResolver
            ?.OpenInputStream(DocumentFile.FromFile(fileForOurInternalStorage).Uri);
        return MessagePack.MessagePackSerializer.Deserialize<T>(inputStream!, options);
    }
    
    private static CachedParseResults GetLegacyCachedParseResult()
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
    
    public static void ClearLegacyParsedCacheResults()
    {
        try
        {
            lock (SeekerApplication.SHARED_PREF_LOCK)
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
    
    private static void StoreCachedParseResults(Context c, CachedParseResults cachedParseResults)
    {
        var sw = System.Diagnostics.Stopwatch.StartNew();

        Java.IO.File fileShareCachedDir = new File(c.FilesDir, KeyConsts.M_fileshare_cache_dir);
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

        lock (SeekerApplication.SHARED_PREF_LOCK)
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
}
