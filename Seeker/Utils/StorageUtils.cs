using System;
using System.Collections.Generic;
using System.Linq;
using _Microsoft.Android.Resource.Designer;
using Android.Content;
using Android.Provider;
using Android.Widget;
using AndroidX.DocumentFile.Provider;
using Java.IO;
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
    
        private static void MoveFile(System.IO.Stream from, System.IO.Stream to, Android.Net.Uri toDelete,
        Android.Net.Uri parentToDelete)
    {
        var buffer = new byte[4096];
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
            // this returns a file that doesn't exist with file ://
            var df = DocumentFile.FromSingleUri(SeekerState.ActiveActivityRef, toDelete);

            if (df?.Delete() != true) // on API 19 this seems to always fail
            {
                Logger.FirebaseDebug("df.Delete() failed to delete");
            }
        }

        DocumentFile parent;
        if (SeekerState.PreOpenDocumentTree() || SettingsActivity.UseTempDirectory() || parentToDelete.Scheme == "file")
        {
            parent = DocumentFile.FromFile(new Java.IO.File(parentToDelete.Path));
        }
        else
        {
            // if from single uri then listing files will give unsupported operation exception...
            // if temp (file: //)this will throw (which makes sense as it did not come from open tree uri)
            parent = DocumentFile.FromTreeUri(SeekerState.ActiveActivityRef, parentToDelete);
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
            if (parent.ListFiles().Length != 1 || parent.ListFiles()[0].Name != ".nomedia")
            {
                return;
            }
            
            if (!parent.ListFiles()[0].Delete())
            {
                Logger.FirebaseDebug("parent.Delete() failed to delete .nomedia child...");
            }

            if (!parent.Delete())
            {
                Logger.FirebaseDebug("parent.Delete() failed to delete parent");
            }
        }
        catch (Exception ex)
        {
            // race condition between checking length of ListFiles() and indexing [0] (twice)
            if (!ex.Message.Contains("Index was outside"))
            {
                throw; // this might be important
            }
        }
    }


    public static void DeleteParentIfEmpty(Java.IO.File parent)
    {
        var files = parent.ListFiles();
        
        if (files is not { Length: 1 } || files[0].Name != ".nomedia")
        {
            return;
        }
        
        if (!files[0].Delete())
        {
            Logger.FirebaseDebug("LEGACY parent.Delete() failed to delete .nomedia child...");
        }

        // this returns false... maybe delete .nomedia child??? YUP.  cannot delete non empty dir...
        if (!parent.Delete())
        {
            Logger.FirebaseDebug("LEGACY parent.Delete() failed to delete parent");
        }
    }


    private static void MoveFile(FileInputStream from, FileOutputStream to, Java.IO.File toDelete, Java.IO.File parent)
    {
        var buffer = new byte[4096];
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

// The call is only reachable on API 21
#pragma warning disable CA1422
    public static void SaveFileToMediaStore(string path)
    {
        var mediaScanIntent = new Intent(Intent.ActionMediaScannerScanFile);
        var f = new Java.IO.File(path);
        var contentUri = Android.Net.Uri.FromFile(f);
        mediaScanIntent.SetData(contentUri);
        SeekerState.ActiveActivityRef.ApplicationContext?.SendBroadcast(mediaScanIntent);
    }
#pragma warning restore CA1422
    
     public static string SaveToFile(
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

                DocumentFile slskDir1;
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
                
                if (SeekerState.CreateUsernameSubfolders)
                {
                    DocumentFile tempUsernameDir1;
                    lock (string.Intern("IfNotExistCreateAtomic_1"))
                    {
                        tempUsernameDir1 = slskDir1?.FindFile(username); // does username folder exist
                        if (tempUsernameDir1 == null || !tempUsernameDir1.Exists())
                        {
                            tempUsernameDir1 = slskDir1.CreateDirectory(username);
                            Logger.Debug(string.Format("Creating {0} dir", username));
                        }
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
                            folderDir1 = slskDir1?.FindFile(dir); // does the folder we want to save to exist
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
                    var localDepth = depth;
                    while (localDepth > 0)
                    {
                        var parts = dir.Split('\\');
                        var singleDir = parts[parts.Length - localDepth];
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
                                Logger.FirebaseDebug("folderDir is null or does not exists, depth" + localDepth);
                            }
                        }

                        folderDir1 = folderDirNext;
                        localDepth--;
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
                    ToastUi.Long(ResourceConstant.String.seeker_cannot_access_files);
                });
            }

            // BACKUP IF FOLDER DIR IS NULL
            folderDir1 ??= rootdir;

            filePath = folderDir1.Uri + "/" + name;
            
            if (memoryMode)
            {
                var mFile = CommonHelpers.CreateMediaFile(folderDir1, name);
                finalUri = mFile.Uri.ToString();
                var stream = SeekerState.ActiveActivityRef.ContentResolver.OpenOutputStream(mFile.Uri);
                stream.Write(bytes);
                stream.Close();
            }
            else
            {
                //106ms for 32mb
                Android.Net.Uri uri = null;
                if (SeekerState.PreMoveDocument() ||
                    // i.e. if use temp dir which is file: // rather than content: //
                    SettingsActivity.UseTempDirectory() ||
                    // i.e. if use complete dir is file: // rather than content: // but Incomplete is content: //
                    (SeekerState.UseLegacyStorage() && SettingsActivity.UseIncompleteManualFolder() 
                                                    && SeekerState.RootDocumentFile == null) || 
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
     
    public static void CreateNoMediaFile(DocumentFile atDirectory)
    {
        atDirectory.CreateFile("nomedia/customnomedia", ".nomedia");
    }
}
