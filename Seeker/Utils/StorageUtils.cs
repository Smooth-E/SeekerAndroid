using System;
using System.Collections.Generic;
using System.Linq;
using Android.Content;
using Android.Provider;
using AndroidX.DocumentFile.Provider;
using Seeker.Managers;
using SlskHelp;
using Soulseek;
using Uri = Android.Net.Uri;
using Document = Android.Provider.DocumentsContract.Document;

namespace Seeker.Utils;

/// <summary>
/// File system utilities
/// </summary>
public static class StorageUtils
{
    // TODO: The entireStringParameter is not set inside the method, consider removing
    public static string GetVolumeName(string lastPathSegment, bool alwaysReturn, out bool entireString)
    {
        entireString = false;

        // if the first part of the path has a colon in it, then strip it.
        int endOfFirstPart = lastPathSegment.IndexOf('\\');

        if (endOfFirstPart == -1)
        {
            endOfFirstPart = lastPathSegment.Length;
        }

        int volumeIndex = lastPathSegment.Substring(0, endOfFirstPart).IndexOf(':');

        if (volumeIndex == -1)
        {
            return null;
        }
        else
        {
            string volumeName = lastPathSegment.Substring(0, volumeIndex + 1);

            if (volumeName.Length != lastPathSegment.Length)
            {
                return volumeName;
            }

            // special case where root is primary:.
            // in this case we return null which gets treated as "dont strip out anything"
            entireString = true;

            return alwaysReturn ? volumeName : null;
        }
    }
    
    public static void GetAllFolderInfo(
        UploadDirectoryInfo uploadDirectoryInfo,
        out bool overrideCase,
        out string volName,
        out string toStrip,
        out string rootFolderDisplayName,
        out string presentableNameToUse)
    {
        DocumentFile dir = uploadDirectoryInfo.UploadDirectory;
        Uri uri = dir.Uri;
        Logger.FirebaseInfo("case " + uri.ToString() + " - - - - " + uri.LastPathSegment);

        string lastPathSegment = CommonHelpers.GetLastPathSegmentWithSpecialCaseProtection(dir, out bool msdCase);

        toStrip = string.Empty;

        // can be reproduced with pixel emulator API 28 (android 9).
        // the last path segment for the downloads dir is "downloads"
        // but the last path segment for its child is "raw:/storage/emulated/0/Download/Soulseek Complete"
        // (note it is still a content scheme, raw: is the volume)
        volName = null;

        // Otherwise we assume the volume is primary
        if (!msdCase)
        {
            volName = GetVolumeName(lastPathSegment, true, out _);
            if (lastPathSegment.Contains('\\'))
            {
                int stripIndex = lastPathSegment.LastIndexOf('\\');
                toStrip = lastPathSegment.Substring(0, stripIndex + 1);
            }
            else if (volName != null && lastPathSegment.Contains(volName))
            {
                toStrip = lastPathSegment == volName ? null : volName;
            }
            else
            {
                Logger.FirebaseDebug("contains neither: " + lastPathSegment); // Download (on Android 9 emu)
            }
        }


        rootFolderDisplayName = uploadDirectoryInfo.DisplayNameOverride;
        overrideCase = false;

        if (msdCase)
        {
            overrideCase = true;
            if (string.IsNullOrEmpty(rootFolderDisplayName))
            {
                rootFolderDisplayName = "downloads";
            }

            volName = null; // i.e. nothing to strip out!
            toStrip = string.Empty;
        }

        if (!string.IsNullOrEmpty(rootFolderDisplayName))
        {
            overrideCase = true;
            volName = null; // i.e. nothing to strip out!
            toStrip = string.Empty;
        }

        // Forcing Override Case
        // Basically there are two ways we construct the tree. One by appending each new name to the base as we go
        // (the 'Override' Case) the other by taking current.Uri minus root.Uri to get the difference.  
        // The latter does not work because sometimes current.Uri will be, say "home:"
        // and root will be say "primary:".
        overrideCase = true;

        if (!string.IsNullOrEmpty(rootFolderDisplayName))
        {
            presentableNameToUse = rootFolderDisplayName;
        }
        else
        {
            presentableNameToUse = uri.GetPresentableName(toStrip, volName);
            rootFolderDisplayName = presentableNameToUse;
        }
    }
    
    /// <summary>
    /// Presentable Filename, Uri.ToString(), length
    /// </summary>
    /// <param name="dir"></param>
    /// <param name="directoryCount"></param>
    /// <returns></returns>
    public static Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>>
        ParseSharedDirectoryFastDocContract(
            UploadDirectoryInfo newlyAddedDirectoryIfApplicable,
            Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>> previousFileInfoToUse,
            ref int directoryCount, out BrowseResponse br,
            out List<Tuple<string, string>> dirMappingFriendlyNameToUri,
            out Dictionary<int, string> index,
            out List<Soulseek.Directory> allHiddenDirs
        )
    {
        // searchable name (just folder/song), uri.ToString (to actually get it),
        // size (for ID purposes and to send),
        // presentablename (to send - this is the name that is supposed to show up as the folder
        //     that the QT and nicotine clients send)

        // so the presentablename should be FolderSelected/path to rest
        // there due to the way android separates the sdcard root (or primary:) and other OS.
        // whereas other OS use path separators, Android uses primary:FolderName vs say C:\Foldername.
        // If primary: is part of the presentable name then I will change 
        // it to primary:\Foldername similar to C:\Foldername.
        // I think this makes most sense of the things I have tried.

        Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>> pairs = new();
        List<Soulseek.Directory> allDirs = new List<Soulseek.Directory>();
        List<Soulseek.Directory> allLockedDirs = new List<Soulseek.Directory>();
        allHiddenDirs = new List<Soulseek.Directory>();
        dirMappingFriendlyNameToUri = new List<Tuple<string, string>>();

        HashSet<string> volNames = UploadDirectoryManager.GetInterestedVolNames();

        Dictionary<string, List<Tuple<string, int, int>>> allMediaStoreInfo = new();
        PopulateAllMediaStoreInfo(allMediaStoreInfo, volNames);


        index = new Dictionary<int, string>();
        int indexNum = 0;

        // avoid race conditions and enumeration modified exceptions.
        var tmpUploadDirs = UploadDirectoryManager.UploadDirectories.ToList();

        foreach (var uploadDirectoryInfo in tmpUploadDirs)
        {
            if (uploadDirectoryInfo.IsSubdir || uploadDirectoryInfo.HasError())
            {
                continue;
            }

            DocumentFile dir = uploadDirectoryInfo.UploadDirectory;

            GetAllFolderInfo(uploadDirectoryInfo, out bool overrideCase, out string volName, out string toStrip,
                out string rootFolderDisplayName, out _);

            traverseDirectoryEntriesInternal(
                SeekerState.ActiveActivityRef.ContentResolver,
                dir.Uri,
                DocumentsContract.GetTreeDocumentId(dir.Uri),
                dir.Uri,
                pairs,
                true,
                volName,
                allDirs,
                allLockedDirs,
                allHiddenDirs,
                dirMappingFriendlyNameToUri,
                toStrip,
                index,
                dir,
                allMediaStoreInfo,
                previousFileInfoToUse,
                overrideCase,
                overrideCase ? rootFolderDisplayName : null,
                ref directoryCount,
                ref indexNum
            );
        }

        br = new BrowseResponse(allDirs, allLockedDirs);
        return pairs;
    }
    
    public static Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>>
        ParseSharedDirectoryLegacy(
            UploadDirectoryInfo newlyAddedDirectoryIfApplicable,
            Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>> previousFileInfoToUse,
            ref int directoryCount,
            out BrowseResponse br,
            out List<Tuple<string, string>> dirMappingFriendlyNameToUri,
            out Dictionary<int, string> index,
            out List<Soulseek.Directory> allHiddenDirs
        )
    {
        // searchable name (just folder/song),
        // uri.ToString (to actually get it),
        // size (for ID purposes and to send),
        // presentablename (to send - this is the name that is supposed to show
        // up as the folder that the QT and nicotine clients send)

        // so the presentablename should be FolderSelected/path to rest
        // there due to the way android separates the sdcard root (or primary:) and other OS.
        // wherewas other OS use path separators, Android uses primary:FolderName vs say C:\Foldername.
        // If primary: is part of the presentable name then I will change 
        // it to primary:\Foldername similar to C:\Foldername.
        // I think this makes most sense of the things I have tried.
        Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>> pairs = new();

        List<Soulseek.Directory> allDirs = new List<Soulseek.Directory>();
        List<Soulseek.Directory> allLockedDirs = new List<Soulseek.Directory>();
        allHiddenDirs = new List<Soulseek.Directory>();

        dirMappingFriendlyNameToUri = new List<Tuple<string, string>>();
        index = new Dictionary<int, string>();
        int indexNum = 0;

        // avoid race conditions and enumeration modified exceptions.
        var tmpUploadDirs = UploadDirectoryManager.UploadDirectories.ToList();
        foreach (var uploadDirectoryInfo in tmpUploadDirs)
        {
            if (uploadDirectoryInfo.IsSubdir || uploadDirectoryInfo.HasError())
            {
                continue;
            }

            DocumentFile dir = uploadDirectoryInfo.UploadDirectory;

            GetAllFolderInfo(uploadDirectoryInfo, out bool overrideCase, out string volName,
                out string toStrip, out string rootFolderDisplayName, out _);

            traverseDirectoryEntriesLegacy(
                dir,
                pairs,
                true,
                allDirs,
                allLockedDirs,
                allHiddenDirs,
                dirMappingFriendlyNameToUri,
                toStrip,
                index,
                previousFileInfoToUse,
                overrideCase,
                overrideCase ? rootFolderDisplayName : null,
                ref directoryCount,
                ref indexNum
            );
        }

        br = new BrowseResponse(allDirs, allLockedDirs);
        return pairs;
    }
    
    public static void PopulateAllMediaStoreInfo(
        Dictionary<string, List<Tuple<string, int, int>>> allMediaStoreInfo,
        HashSet<string> volumeNamesOfInterest)
    {

        bool hasAnyInfo = AndroidPlatform.HasMediaStoreDurationColumn();
        if (hasAnyInfo)
        {
            bool hasBitRate = AndroidPlatform.HasMediaStoreBitRateColumn();
            string[] selectionColumns = null;

            if (hasBitRate)
            {
                selectionColumns = new string[]
                {
                    Android.Provider.MediaStore.IMediaColumns.Size,
                    Android.Provider.MediaStore.IMediaColumns.DisplayName,

                    Android.Provider.MediaStore.IMediaColumns.Data, // disambiguator if applicable
                    Android.Provider.MediaStore.IMediaColumns.Duration,
                    Android.Provider.MediaStore.IMediaColumns.Bitrate
                };
            }
            else //only has duration
            {
                selectionColumns = new string[]
                {
                    Android.Provider.MediaStore.IMediaColumns.Size,
                    Android.Provider.MediaStore.IMediaColumns.DisplayName,

                    Android.Provider.MediaStore.IMediaColumns.Data, //disambiguator if applicable
                    Android.Provider.MediaStore.IMediaColumns.Duration
                };
            }


            foreach (var chosenVolume in volumeNamesOfInterest)
            {
                Android.Net.Uri mediaStoreUri = null;
                if (!string.IsNullOrEmpty(chosenVolume))
                {
                    mediaStoreUri = MediaStore.Audio.Media.GetContentUri(chosenVolume);
                }
                else
                {
                    mediaStoreUri = MediaStore.Audio.Media.ExternalContentUri;
                }

                // metadata content resolver info
                Android.Database.ICursor mediaStoreInfo = null;
                try
                {
                    mediaStoreInfo = SeekerState.ActiveActivityRef.ContentResolver.Query(mediaStoreUri,
                        selectionColumns, null, null, null);

                    while (mediaStoreInfo.MoveToNext())
                    {
                        string key = mediaStoreInfo.GetInt(0) + mediaStoreInfo.GetString(1);

                        var tuple = new Tuple<string, int, int>(
                            mediaStoreInfo.GetString(2),
                            mediaStoreInfo.GetInt(3),
                            hasBitRate ? mediaStoreInfo.GetInt(4) : -1
                        );

                        if (!allMediaStoreInfo.ContainsKey(key))
                        {
                            var list = new List<Tuple<string, int, int>>();
                            list.Add(tuple);
                            allMediaStoreInfo.Add(key, list);
                        }
                        else
                        {
                            allMediaStoreInfo[key].Add(tuple);
                        }
                    }
                }
                catch (Exception e)
                {
                    Logger.FirebaseDebug("pre get all mediaStoreInfo: " + e.Message + e.StackTrace);
                }
                finally
                {
                    if (mediaStoreInfo != null)
                    {
                        mediaStoreInfo.Close();
                    }
                }
            }
        }
    }
    
    public static void traverseDirectoryEntriesLegacy(
        DocumentFile parentDocFile,
        Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>> pairs,
        bool isRootCase,
        List<Directory> listOfDirs, List<Directory> listOfLockedDirs,
        List<Directory> listOfHiddenDirs,
        List<Tuple<string, string>> dirMappingFriendlyNameToUri,
        string folderToStripForPresentableNames,
        Dictionary<int, string> index,
        Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>> previousFileInfoToUse,
        bool overrideCase,
        string msdMsfOrOverrideBuildParentName,
        ref int totalDirectoryCount,
        ref int indexNum)
    {
        // this should be the folder before the selected to strip away..
        List<Soulseek.File> files = new List<Soulseek.File>();
        foreach (var childDocFile in parentDocFile.ListFiles())
        {
            if (childDocFile.IsDirectory)
            {
                totalDirectoryCount++;
                traverseDirectoryEntriesLegacy(
                    childDocFile, pairs,
                    false,
                    listOfDirs,
                    listOfLockedDirs,
                    listOfHiddenDirs,
                    dirMappingFriendlyNameToUri,
                    folderToStripForPresentableNames,
                    index,
                    previousFileInfoToUse,
                    overrideCase,
                    overrideCase ? msdMsfOrOverrideBuildParentName + '\\' + childDocFile.Name : null,
                    ref totalDirectoryCount,
                    ref indexNum
                );
            }
            else
            {
                // for subAPI21 last path segment is:
                // ".android_secure" so just the filename whereas Path is more similar to last part segment:
                // "/storage/sdcard/.android_secure"
                string presentableName = childDocFile.Uri.Path.Replace('/', '\\');

                if (overrideCase)
                {
                    presentableName = msdMsfOrOverrideBuildParentName + '\\' + childDocFile.Name;
                }
                // this means that the primary: is in the path so at least convert it from primary: to primary:\
                else if (folderToStripForPresentableNames != null)
                {
                    presentableName = presentableName.Substring(folderToStripForPresentableNames.Length);
                }

                Tuple<int, int, int, int> attributes = AudioUtils.GetAudioAttributes(
                    SeekerState.ActiveActivityRef.ContentResolver,
                    childDocFile.Name,
                    childDocFile.Length(),
                    presentableName,
                    childDocFile.Uri,
                    null,
                    previousFileInfoToUse
                );

                // todo attributes was null here???? before
                var tuple = new Tuple<long, string, Tuple<int, int, int, int>, bool, bool>(
                    childDocFile.Length(),
                    childDocFile.Uri.ToString(),
                    attributes, IsLockedFile(presentableName),
                    IsHiddenFile(presentableName)
                );
                pairs.Add(presentableName, tuple);

                index.Add(indexNum, presentableName);
                indexNum++;

                if (indexNum % 50 == 0)
                {
                    // update public status variable every so often
                    SeekerState.NumberParsed = indexNum;
                }

                // TODO: I've seen this comment a couple of times already.
                //       Seems like there is some code repetition or code similarity, at least

                // use presentable name so that the filename will not be primary:file.mp3
                // for the brose response should only be the filename!!!
                // when a user tries to download something from a browse resonse, the soulseek client
                // on their end must create a fully qualified path for us
                // bc we get a path that is:
                // "Soulseek Complete\\document\\primary:Pictures\\Soulseek Complete\\album\\09 Between Songs 4.mp3"
                // not quite a full URI but it does add quite a bit..
                string fname = CommonHelpers.GetFileNameFromFile(presentableName.Replace("/", @"\"));

                var extension = System.IO.Path.GetExtension(childDocFile.Uri.Path);
                var slskFile = new Soulseek.File(1, fname, childDocFile.Length(), extension);
                files.Add(slskFile);
            }
        }

        CommonHelpers.SortSlskDirFiles(files);
        string directoryPath = parentDocFile.Uri.Path.Replace("/", @"\");

        if (overrideCase)
        {
            directoryPath = msdMsfOrOverrideBuildParentName;
        }
        else if (folderToStripForPresentableNames != null)
        {
            directoryPath = directoryPath.Substring(folderToStripForPresentableNames.Length);
        }

        var slskDir = new Soulseek.Directory(directoryPath, files);
        if (IsHiddenFolder(directoryPath))
        {
            listOfHiddenDirs.Add(slskDir);
        }
        else if (IsLockedFolder(directoryPath))
        {
            listOfLockedDirs.Add(slskDir);
        }
        else
        {
            listOfDirs.Add(slskDir);
        }

        dirMappingFriendlyNameToUri.Add(new Tuple<string, string>(directoryPath, parentDocFile.Uri.ToString()));
    }
    
    private static void traverseToGetDirectories(DocumentFile dir, List<Android.Net.Uri> dirUris)
    {
        if (dir.IsDirectory)
        {
            DocumentFile[] files = dir.ListFiles(); // doesn't need to be sorted
            for (int i = 0; i < files.Length; ++i)
            {
                DocumentFile file = files[i];
                if (file.IsDirectory)
                {
                    dirUris.Add(file.Uri);
                    traverseToGetDirectories(file, dirUris);
                }
            }
        }
    }
    
    public static void traverseDirectoryEntriesInternal(
        ContentResolver contentResolver,
        Android.Net.Uri rootUri,
        string parentDoc,
        Android.Net.Uri parentUri,
        Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>> pairs, bool isRootCase,
        string volName,
        List<Directory> listOfDirs,
        List<Directory> listOfLockedDirs,
        List<Directory> listOfHiddenDirs,
        List<Tuple<string, string>> dirMappingFriendlyNameToUri,
        string folderToStripForPresentableNames,
        Dictionary<int, string> index,
        DocumentFile rootDirCase,
        Dictionary<string, List<Tuple<string, int, int>>> allMediaInfoDict,
        Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>> previousFileInfoToUse,
        bool msdMsfOrOverrideCase,
        string msdMsfOrOverrideBuildParentName,
        ref int totalDirectoryCount,
        ref int indexNum)
    {
        // this should be the folder before the selected to strip away..

        Android.Net.Uri listChildrenUri = DocumentsContract.BuildChildDocumentsUriUsingTree(rootUri, parentDoc);

        var queryArgs = new String[]
        {
            Document.ColumnDocumentId,
            Document.ColumnDisplayName,
            Document.ColumnMimeType,
            Document.ColumnSize
        };
        Android.Database.ICursor c = contentResolver.Query(listChildrenUri, queryArgs, null, null, null);

        // c can be null... reasons are fairly opaque
        // - if remote exception return null. if underlying content provider is null.
        if (c == null)
        {
            // diagnostic code.

            // would a non /children uri work?
            bool nonChildrenWorks =
                contentResolver.Query(rootUri, new string[] { Document.ColumnSize }, null, null, null) != null;

            // would app context work?
            var args = new String[]
            {
                Document.ColumnDocumentId,
                Document.ColumnDisplayName,
                Document.ColumnMimeType,
                Document.ColumnSize
            };
            var cr = SeekerState.ActiveActivityRef.ApplicationContext.ContentResolver;
            bool wouldActiveWork = cr.Query(listChildrenUri, args, null, null, null) != null;

            //would list files work?
            bool docFileLegacyWork = DocumentFile.FromTreeUri(SeekerState.ActiveActivityRef, parentUri).Exists();

            Logger.FirebaseDebug("cursor is null: parentDoc" + parentDoc + " list children uri: "
                                     + listChildrenUri?.ToString() + "nonchildren: " + nonChildrenWorks
                                     + " activeContext: " + wouldActiveWork + " legacyWork: " + docFileLegacyWork);
        }

        List<Soulseek.File> files = new List<Soulseek.File>();
        try
        {
            while (c.MoveToNext())
            {
                string docId = c.GetString(0);
                string name = c.GetString(1);
                string mime = c.GetString(2);
                long size = c.GetLong(3);
                var childUri = DocumentsContract.BuildDocumentUriUsingTree(rootUri, docId);

                if (isDirectory(mime))
                {
                    totalDirectoryCount++;
                    traverseDirectoryEntriesInternal(
                        contentResolver,
                        rootUri,
                        docId,
                        childUri,
                        pairs,
                        false,
                        volName,
                        listOfDirs,
                        listOfLockedDirs,
                        listOfHiddenDirs,
                        dirMappingFriendlyNameToUri,
                        folderToStripForPresentableNames,
                        index,
                        null,
                        allMediaInfoDict,
                        previousFileInfoToUse,
                        msdMsfOrOverrideCase,
                        msdMsfOrOverrideCase ? msdMsfOrOverrideBuildParentName + '\\' + name : null,
                        ref totalDirectoryCount,
                        ref indexNum
                    );
                }
                else
                {
                    string presentableName = null;
                    if (msdMsfOrOverrideCase)
                    {
                        presentableName = msdMsfOrOverrideBuildParentName + '\\' + name;
                    }
                    else
                    {
                        presentableName = childUri.GetPresentableName(folderToStripForPresentableNames, volName);
                    }


                    string searchableName = Common.Helpers.GetFolderNameFromFile(presentableName) + @"\"
                        + CommonHelpers.GetFileNameFromFile(presentableName);

                    Tuple<int, int, int, int> attributes = AudioUtils.GetAudioAttributes(
                        contentResolver,
                        name,
                        size,
                        presentableName,
                        childUri,
                        allMediaInfoDict,
                        previousFileInfoToUse
                    );

                    var tuple = new Tuple<long, string, Tuple<int, int, int, int>, bool, bool>(
                        size,
                        childUri.ToString(),
                        attributes,
                        IsLockedFile(presentableName),
                        IsHiddenFile(presentableName)
                    );

                    pairs.Add(presentableName, tuple);

                    // throws on same key (the file in question ends with unicode EOT char (\u04)).
                    index.Add(indexNum, presentableName);
                    indexNum++;

                    if (indexNum % 50 == 0)
                    {
                        // update public status variable every so often
                        SeekerState.NumberParsed = indexNum;
                    }

                    // use presentable name so that the filename will not be primary:file.mp3
                    // for the brose response should only be the filename!!!
                    // when a user tries to download something from a browse resonse,
                    // the soulseek client on their end must create a fully qualified path for us
                    // bc we get a path that is:
                    // "Soulseek Complete\\document\\primary:Pictures\\Soulseek Complete\\album\\09 Between Songs 4.mp3"
                    // not quite a full URI but it does add quite a bit...
                    string fname = CommonHelpers.GetFileNameFromFile(presentableName.Replace("/", @"\"));

                    // SoulseekQT does not show attributes in browse tab, but nicotine does.
                    var fileExtension = System.IO.Path.GetExtension(childUri.Path);
                    var fileAttributes = SharedFileCache.GetFileAttributesFromTuple(attributes);
                    var slskFile = new Soulseek.File(1, fname, size, fileExtension, fileAttributes);
                    files.Add(slskFile);
                }
            }

            CommonHelpers.SortSlskDirFiles(files);
            string lastPathSegment = null;
            if (msdMsfOrOverrideCase)
            {
                lastPathSegment = msdMsfOrOverrideBuildParentName;
            }
            else if (isRootCase)
            {
                lastPathSegment = CommonHelpers.GetLastPathSegmentWithSpecialCaseProtection(rootDirCase, out _);
            }
            else
            {
                lastPathSegment = parentUri.LastPathSegment;
            }

            string directoryPath = lastPathSegment.Replace("/", @"\");

            if (!msdMsfOrOverrideCase)
            {
                // this means that the primary: is in the path so at least convert it from primary: to primary:\
                if (folderToStripForPresentableNames == null)
                {
                    // i.e. if it has something after it.. primary: should be primary: not primary:\
                    // but primary:Alarms should be primary:\Alarms
                    if (volName != null && volName.Length != directoryPath.Length)
                    {
                        if (volName.Length > directoryPath.Length)
                        {
                            Logger.FirebaseDebug("volName > directoryPath" + volName + " -- "
                                                     + directoryPath + " -- " + isRootCase);
                        }

                        directoryPath = directoryPath.Substring(0, volName.Length) + '\\'
                            + directoryPath.Substring(volName.Length);
                    }
                }
                else
                {
                    directoryPath = directoryPath.Substring(folderToStripForPresentableNames.Length);
                }
            }

            var slskDir = new Soulseek.Directory(directoryPath, files);
            if (IsHiddenFolder(directoryPath))
            {
                listOfHiddenDirs.Add(slskDir);
            }
            else if (IsLockedFolder(directoryPath))
            {
                listOfLockedDirs.Add(slskDir);
            }
            else
            {
                listOfDirs.Add(slskDir);
            }

            dirMappingFriendlyNameToUri.Add(new Tuple<string, string>(directoryPath, parentUri.ToString()));
        }
        finally
        {
            c.closeQuietly();
        }
    }
    
    public static bool IsHiddenFile(string presentableName)
    {
        foreach (string hiddenDir in UploadDirectoryManager.PresentableNameHiddenDirectories)
        {
            if (presentableName.StartsWith($"{hiddenDir}\\")) //no need for == bc files
            {
                return true;
            }
        }

        return false;
    }
    
    public static bool IsLockedFile(string presentableName)
    {
        foreach (string lockedDir in UploadDirectoryManager.PresentableNameLockedDirectories)
        {
            if (presentableName.StartsWith($"{lockedDir}\\")) // no need for == bc files
            {
                return true;
            }
        }

        return false;
    }
    
    /// <summary>
    /// Util method to check if the mime type is a directory
    /// </summary>
    public static bool isDirectory(String mimeType)
    {
        return DocumentsContract.Document.MimeTypeDir.Equals(mimeType);
    }
    
    public static bool IsLockedFolder(string presentableName)
    {
        if (IsLockedFile(presentableName))
        {
            return true;
        }

        foreach (string lockedDir in UploadDirectoryManager.PresentableNameLockedDirectories)
        {
            if (presentableName == lockedDir)
            {
                return true;
            }
        }

        return false;
    }
    
    public static bool IsHiddenFolder(string presentableName)
    {
        if (IsHiddenFile(presentableName))
        {
            return true;
        }

        foreach (string hiddenDir in UploadDirectoryManager.PresentableNameHiddenDirectories)
        {
            if (presentableName == hiddenDir)
            {
                return true;
            }
        }

        return false;
    }
}
