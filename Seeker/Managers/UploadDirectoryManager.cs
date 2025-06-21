using System;
using System.Collections.Generic;
using System.Linq;
using _Microsoft.Android.Resource.Designer;
using Android.Content;
using Android.Provider;
using AndroidX.DocumentFile.Provider;
using Seeker.Utils;

namespace Seeker.Managers;

public static class UploadDirectoryManager
{
    public static String UploadDataDirectoryUri = null;
    public static bool UploadDataDirectoryUriIsFromTree = true;
    
    public static List<UploadDirectoryInfo> UploadDirectories;
    
    public static List<string> PresentableNameLockedDirectories = [];
    public static List<string> PresentableNameHiddenDirectories = [];
    
    public static string GetCompositeErrorString(Context context)
    {
        if (UploadDirectories.Any(d => d.ErrorState == UploadDirectoryError.CannotWrite))
        {
            return GetErrorString(context, UploadDirectoryError.CannotWrite);
        }

        if (UploadDirectories.Any(d => d.ErrorState == UploadDirectoryError.DoesNotExist))
        {
            return GetErrorString(context, UploadDirectoryError.DoesNotExist);
        }

        return UploadDirectories.Any(d => d.ErrorState == UploadDirectoryError.Unknown)
            ? GetErrorString(context, UploadDirectoryError.Unknown)
            : null;
    }

    public static string GetErrorString(Context context, UploadDirectoryError errorCode)
    {
        switch (errorCode)
        {
            case UploadDirectoryError.CannotWrite:
                return context.GetString(ResourceConstant.String.PermissionErrorShared);
            case UploadDirectoryError.DoesNotExist:
                // this is a permission error the overwhelming majority of the time.
                // hence "not accessible" rather than "does not exist"
                return context.GetString(ResourceConstant.String.FolderNotAccessible);
            case UploadDirectoryError.Unknown:
                return context.GetString(ResourceConstant.String.UnknownErrorShared);
            case UploadDirectoryError.NoError:
            default:
                return "No Error.";
        }
    }

    public static void RestoreFromSavedState(ISharedPreferences sharedPreferences)
    {
        string sharedDirInfo = sharedPreferences.GetString(KeyConsts.M_SharedDirectoryInfo, string.Empty);
        if (string.IsNullOrEmpty(sharedDirInfo))
        {
            string legacyUploadDataDirectory =
                sharedPreferences.GetString(KeyConsts.M_UploadDirectoryUri, string.Empty);
            bool fromTree = sharedPreferences.GetBoolean(KeyConsts.M_UploadDirectoryUriIsFromTree, true);

            if (!string.IsNullOrEmpty(legacyUploadDataDirectory))
            {
                // legacy case. lets upgrade it.
                var uploadDir = new UploadDirectoryInfo(
                    legacyUploadDataDirectory,
                    fromTree,
                    false,
                    false,
                    null
                );
                
                UploadDirectories = [ uploadDir ];

                //save new
                SaveToSharedPreferences(sharedPreferences);
                //clear old
                sharedPreferences.Edit()!
                    .PutString(KeyConsts.M_UploadDirectoryUri, string.Empty)!
                    .Commit();
            }
            else
            {
                UploadDirectories = [];
            }
        }
        else
        {
            UploadDirectories = SerializationHelper.DeserializeFromString<List<UploadDirectoryInfo>>(sharedDirInfo);
        }
    }

    public static void SaveToSharedPreferences(ISharedPreferences sharedPreferences)
    {
        using (new System.IO.MemoryStream())
        {
            var userDirsString = SerializationHelper.SerializeToString(UploadDirectories);
            lock (sharedPreferences)
            {
                sharedPreferences.Edit()!
                    .PutString(KeyConsts.M_SharedDirectoryInfo, userDirsString)!
                    .Commit();
            }
        }
    }

    public static bool IsFromTree()
    {
        if (UploadDirectories.All(dir => dir.UploadDataDirectoryUriIsFromTree))
        {
            return true;
        }

        return UploadDirectories.Any(dir => dir.UploadDataDirectoryUriIsFromTree);
    }

    public static bool AreAnyFromLegacy()
    {
        return UploadDirectories.Any(dir => !dir.UploadDataDirectoryUriIsFromTree);
    }

    /// <summary>
    /// If so then we turn off sharing. If only 1+ failed we let the user know, but keep sharing on.
    /// </summary>
    public static bool AreAllFailed()
    {
        return UploadDirectories.All(dir => dir.HasError());
    }
    
    public static bool DoesNewDirectoryHaveUniqueRootName(UploadDirectoryInfo newDirInfo, bool updateItToHaveUniqueName)
    {
        var currentRootNames = new List<string>();
        foreach (var dirInfo in UploadDirectories.Where(dirInfo => !dirInfo.IsSubdir && (dirInfo != newDirInfo)))
        {
            StorageUtils.GetAllFolderInfo(dirInfo, 
                out _,
                out _,
                out _,
                out _,
                out var presentableName
            );
                
            currentRootNames.Add(presentableName);
        }

        StorageUtils.GetAllFolderInfo(newDirInfo, out _, out _, out _, 
            out _, out var presentableNameNew);

        if (!currentRootNames.Contains(presentableNameNew))
        {
            return true;
        }

        if (!updateItToHaveUniqueName)
        {
            return false;
        }
        
        while (currentRootNames.Contains(presentableNameNew))
        {
            presentableNameNew += " (1)";
        }

        newDirInfo.DisplayNameOverride = presentableNameNew;

        return false;
    }

    /// <summary>If only 1+ failed we let the user know, but keep sharing on.</summary>
    public static bool AreAnyFailed()
    {
        return UploadDirectories.Any(dir => dir.HasError());
    }

    /// <summary>
    /// I think this should just return "external" (TODO - implement and test)
    /// https://developer.android.google.cn/reference/android/provider/MediaStore#VOLUME_EXTERNAL
    /// </summary>
    public static HashSet<string> GetInterestedVolNames()
    {
        var interestedVolnames = new HashSet<string>();
        var dirs = UploadDirectories
            .Where(uploadDir => !uploadDir.IsSubdir && uploadDir.UploadDirectory != null);
        
        foreach (var uploadDir in dirs)
        {
            var lastPathSegment = CommonHelpers
                .GetLastPathSegmentWithSpecialCaseProtection(uploadDir.UploadDirectory, out var msdCase);
            
            if (msdCase)
            {
                interestedVolnames.Add(string.Empty); // primary
            }
            else
            {
                var volName = StorageUtils.GetVolumeName(lastPathSegment, true, out _);

                //this is for if the chosen volume is not primary external
                if ((int)Android.OS.Build.VERSION.SdkInt < 29)
                {
                    interestedVolnames.Add("external");
                    return interestedVolnames;
                }

                // TODO: Check for Android API level here
                var volumeNames = 
                    MediaStore.GetExternalVolumeNames(SeekerState.ActiveActivityRef); // added in 29
                    
                string chosenVolume = null;
                if (volName != null)
                {
                    var volToCompare = volName.Replace(":", "");
                    foreach (var mediaStoreVolume in volumeNames)
                    {
                        if (mediaStoreVolume.Equals(volToCompare, StringComparison.CurrentCultureIgnoreCase))
                        {
                            chosenVolume = mediaStoreVolume;
                        }
                    }
                }

                interestedVolnames.Add(chosenVolume ?? string.Empty); // primary
            }
        }

        return interestedVolnames;
    }

    public static void UpdateWithDocumentFileAndErrorStates(Context context)
    {
        foreach (var uploadDirectoryInfo in UploadDirectories)
        {
            var uploadDirUri = Android.Net.Uri.Parse(uploadDirectoryInfo.UploadDataDirectoryUri);
            try
            {
                uploadDirectoryInfo.ErrorState = UploadDirectoryError.NoError;
                if (SeekerState.PreOpenDocumentTree() || !uploadDirectoryInfo.UploadDataDirectoryUriIsFromTree)
                {
                    if (uploadDirUri?.Path != null)
                    {
                        var file = new Java.IO.File(uploadDirUri.Path);
                        uploadDirectoryInfo.UploadDirectory = DocumentFile.FromFile(file);
                    }
                }
                else
                {
                    uploadDirectoryInfo.UploadDirectory = DocumentFile.FromTreeUri(context, uploadDirUri!);
                    if (uploadDirectoryInfo.UploadDirectory?.Exists() != true)
                    {
                        uploadDirectoryInfo.UploadDirectory = null;
                        uploadDirectoryInfo.ErrorState = UploadDirectoryError.DoesNotExist;
                    }
                    else if (!uploadDirectoryInfo.UploadDirectory.CanWrite())
                    {
                        uploadDirectoryInfo.UploadDirectory = null;
                        uploadDirectoryInfo.ErrorState = UploadDirectoryError.CannotWrite;
                    }
                }
            }
            catch (Exception exception)
            {
                var errorIfo = $"{exception.Message}\n{exception.StackTrace}";
                Logger.Debug($"UploadDirectoryManager.UpdateWithDocumentFileAndErrorStates: {errorIfo}");
                uploadDirectoryInfo.ErrorState = UploadDirectoryError.Unknown;
            }
        }

        for (var i = 0; i < UploadDirectories.Count; i++)
        {
            var uploadDirectoryInfo = UploadDirectories[i];
            var ourUri = Android.Net.Uri.Parse(uploadDirectoryInfo.UploadDataDirectoryUri);

            for (var j = 0; j < UploadDirectories.Count; j++)
            {
                if (i == j)
                {
                    continue;
                }

                var uploadDirPath = Android.Net.Uri.Parse(UploadDirectories[j].UploadDataDirectoryUri);
                if (ourUri!.LastPathSegment!.Contains(uploadDirPath!.LastPathSegment!))
                {
                    uploadDirectoryInfo.IsSubdir = true;
                }
            }
        }

        PresentableNameLockedDirectories.Clear();
        PresentableNameHiddenDirectories.Clear();
        
        for (var i = 0; i < UploadDirectories.Count; i++)
        {
            var uploadDirectoryInfo = UploadDirectories[i];
            if (!uploadDirectoryInfo.IsLocked && !uploadDirectoryInfo.IsHidden)
            {
                continue;
            }

            if (!uploadDirectoryInfo.IsSubdir)
            {
                if (uploadDirectoryInfo.IsLocked)
                {
                    PresentableNameLockedDirectories.Add(uploadDirectoryInfo.GetPresentableName());
                }

                if (uploadDirectoryInfo.IsHidden)
                {
                    PresentableNameHiddenDirectories.Add(uploadDirectoryInfo.GetPresentableName());
                }
            }
            else
            {
                // find our topmost parent so we can get the effective presentable name
                var ourUri = Android.Net.Uri.Parse(uploadDirectoryInfo.UploadDataDirectoryUri);

                var ourTopLevelParent = UploadDirectories
                    .Where((_, j) => i != j)
                    .FirstOrDefault(t =>
                    {
                        if (t.IsSubdir)
                        {
                            return false;
                        }

                        var segment = Android.Net.Uri.Parse(t.UploadDataDirectoryUri)!.LastPathSegment;
                        return ourUri!.LastPathSegment!.Contains(segment!);
                    });

                // Check for error, otherwise pointless + causes null ref
                if (uploadDirectoryInfo.HasError() || ourTopLevelParent!.HasError())
                {
                    continue;
                }
                
                if (uploadDirectoryInfo.IsLocked)
                {
                    PresentableNameLockedDirectories.Add(uploadDirectoryInfo.GetPresentableName(ourTopLevelParent));
                }

                if (uploadDirectoryInfo.IsHidden)
                {
                    PresentableNameHiddenDirectories.Add(uploadDirectoryInfo.GetPresentableName(ourTopLevelParent));
                }
            }
        }
    }
}
