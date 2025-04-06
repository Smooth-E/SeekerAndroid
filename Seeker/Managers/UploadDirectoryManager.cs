using System;
using System.Collections.Generic;
using System.Linq;
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
    
    public static string GetCompositeErrorString()
    {
        if (UploadDirectories.Any(d => d.ErrorState == UploadDirectoryError.CannotWrite))
        {
            return GetErrorString(UploadDirectoryError.CannotWrite);
        }

        if (UploadDirectories.Any(d => d.ErrorState == UploadDirectoryError.DoesNotExist))
        {
            return GetErrorString(UploadDirectoryError.DoesNotExist);
        }

        return UploadDirectories.Any(d => d.ErrorState == UploadDirectoryError.Unknown)
            ? GetErrorString(UploadDirectoryError.Unknown)
            : null;
    }

    public static string GetErrorString(UploadDirectoryError errorCode)
    {
        switch (errorCode)
        {
            case UploadDirectoryError.CannotWrite:
                return SeekerApplication.ApplicationContext.GetString(Resource.String.PermissionErrorShared);
            case UploadDirectoryError.DoesNotExist:
                // this is a permission error the overwhelming majority of the time.
                // hence "not accessible" rather than "does not exist"
                return SeekerApplication.ApplicationContext.GetString(Resource.String.FolderNotAccessible);
            case UploadDirectoryError.Unknown:
                return SeekerApplication.ApplicationContext.GetString(Resource.String.UnknownErrorShared);
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
                UploadDirectoryInfo uploadDir =
                    new UploadDirectoryInfo(legacyUploadDataDirectory, fromTree, false, false, null);
                UploadDirectories = new List<UploadDirectoryInfo>();
                UploadDirectories.Add(uploadDir);

                //save new
                SaveToSharedPreferences(sharedPreferences);
                //clear old
                var editor = sharedPreferences.Edit();
                editor.PutString(KeyConsts.M_UploadDirectoryUri, string.Empty);
                editor.Commit();
            }
            else
            {
                UploadDirectories = new List<UploadDirectoryInfo>();
            }
        }
        else
        {
            UploadDirectories = SerializationHelper.DeserializeFromString<List<UploadDirectoryInfo>>(sharedDirInfo);
        }
    }

    public static void SaveToSharedPreferences(ISharedPreferences sharedPreferences)
    {
        using (System.IO.MemoryStream mem = new System.IO.MemoryStream())
        {
            string userDirsString = SerializationHelper.SerializeToString(UploadDirectories);
            lock (sharedPreferences)
            {
                var editor = sharedPreferences.Edit();
                editor.PutString(KeyConsts.M_SharedDirectoryInfo, userDirsString);
                editor.Commit();
            }
        }
    }

    public static bool IsFromTree(string presentablePath)
    {
        if (UploadDirectories.All(dir => dir.UploadDataDirectoryUriIsFromTree))
        {
            return true;
        }

        if (UploadDirectories.All(dir => !dir.UploadDataDirectoryUriIsFromTree))
        {
            return false;
        }

        return true; //todo
    }

    public static bool AreAnyFromLegacy()
    {
        return UploadDirectories.Where(dir => !dir.UploadDataDirectoryUriIsFromTree).Any();
    }

    /// <summary>
    /// If so then we turn off sharing. If only 1+ failed we let the user know, but keep sharing on.
    /// </summary>
    /// <returns></returns>
    public static bool AreAllFailed()
    {
        return UploadDirectories.All(dir => dir.HasError());
    }


    public static bool DoesNewDirectoryHaveUniqueRootName(UploadDirectoryInfo newDirInfo, bool updateItToHaveUniqueName)
    {
        bool isUnique = true;
        List<string> currentRootNames = new List<string>();
        foreach (UploadDirectoryInfo dirInfo in UploadDirectories)
        {
            if (dirInfo.IsSubdir || (dirInfo == newDirInfo))
            {
                continue;
            }

            StorageUtils.GetAllFolderInfo(dirInfo, out _, out _, out _, 
                out _, out var presentableName);
                
            currentRootNames.Add(presentableName);
        }

        StorageUtils.GetAllFolderInfo(newDirInfo, out _, out _, out _, 
            out _, out var presentableNameNew);
        
        if (currentRootNames.Contains(presentableNameNew))
        {
            isUnique = false;
            if (updateItToHaveUniqueName)
            {
                while (currentRootNames.Contains(presentableNameNew))
                {
                    presentableNameNew = presentableNameNew + " (1)";
                }

                newDirInfo.DisplayNameOverride = presentableNameNew;
            }
        }

        return isUnique;
    }

    /// <summary>
    /// If only 1+ failed we let the user know, but keep sharing on.
    /// </summary>
    /// <returns></returns>
    public static bool AreAnyFailed()
    {
        return UploadDirectories.Any(dir => dir.HasError());
    }

    /// <summary>
    /// I think this should just return "external" (TODO - implement and test)
    /// https://developer.android.google.cn/reference/android/provider/MediaStore#VOLUME_EXTERNAL
    /// </summary>
    /// <returns></returns>
    public static HashSet<string> GetInterestedVolNames()
    {
        HashSet<string> interestedVolnames = new HashSet<string>();
        foreach (var uploadDir in UploadDirectories)
        {
            if (!uploadDir.IsSubdir && uploadDir.UploadDirectory != null)
            {
                string lastPathSegment =
                    CommonHelpers.GetLastPathSegmentWithSpecialCaseProtection(uploadDir.UploadDirectory,
                        out bool msdCase);
                if (msdCase)
                {
                    interestedVolnames.Add(string.Empty); // primary
                }
                else
                {
                    string volName = StorageUtils.GetVolumeName(lastPathSegment, true, out _);

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
                        string volToCompare = volName.Replace(":", "");
                        foreach (string mediaStoreVolume in volumeNames)
                        {
                            if (mediaStoreVolume.ToLower() == volToCompare.ToLower())
                            {
                                chosenVolume = mediaStoreVolume;
                            }
                        }
                    }

                    if (chosenVolume == null)
                    {
                        interestedVolnames.Add(string.Empty); // primary
                    }
                    else
                    {
                        interestedVolnames.Add(chosenVolume);
                    }
                }
            }
        }

        return interestedVolnames;
    }

    public static void UpdateWithDocumentFileAndErrorStates()
    {
        foreach (var uploadDirectoryInfo in UploadDirectories)
        {
            Android.Net.Uri uploadDirUri = Android.Net.Uri.Parse(uploadDirectoryInfo.UploadDataDirectoryUri);
            try
            {
                uploadDirectoryInfo.ErrorState = UploadDirectoryError.NoError;
                if (SeekerState.PreOpenDocumentTree() || !uploadDirectoryInfo.UploadDataDirectoryUriIsFromTree)
                {
                    if (uploadDirUri?.Path != null)
                    {
                        uploadDirectoryInfo.UploadDirectory =
                            DocumentFile.FromFile(new Java.IO.File(uploadDirUri.Path));
                    }
                }
                else
                {
                    uploadDirectoryInfo.UploadDirectory =
                        DocumentFile.FromTreeUri(SeekerState.ActiveActivityRef, uploadDirUri);
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
            catch (Exception e)
            {
                uploadDirectoryInfo.ErrorState = UploadDirectoryError.Unknown;
            }
        }

        for (int i = 0; i < UploadDirectories.Count; i++)
        {
            UploadDirectoryInfo uploadDirectoryInfo = UploadDirectories[i];
            var ourUri = Android.Net.Uri.Parse(uploadDirectoryInfo.UploadDataDirectoryUri);

            for (int j = 0; j < UploadDirectories.Count; j++)
            {
                if (i != j)
                {
                    if (ourUri.LastPathSegment.Contains(Android.Net.Uri
                            .Parse(UploadDirectories[j].UploadDataDirectoryUri).LastPathSegment))
                    {
                        uploadDirectoryInfo.IsSubdir = true;
                    }
                }
            }
        }

        PresentableNameLockedDirectories.Clear();
        PresentableNameHiddenDirectories.Clear();
        for (int i = 0; i < UploadDirectories.Count; i++)
        {
            UploadDirectoryInfo uploadDirectoryInfo = UploadDirectories[i];
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

                UploadDirectoryInfo ourTopLevelParent = null;

                for (int j = 0; j < UploadDirectories.Count; j++)
                {
                    if (i != j)
                    {
                        if (!UploadDirectories[j].IsSubdir && ourUri.LastPathSegment.Contains(Android.Net.Uri
                                .Parse(UploadDirectories[j].UploadDataDirectoryUri).LastPathSegment))
                        {
                            ourTopLevelParent = UploadDirectories[j];
                            break;
                        }
                    }
                }

                if (!uploadDirectoryInfo.HasError() &&
                    !ourTopLevelParent.HasError()) //otherwise pointless + causes nullref
                {
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
}
