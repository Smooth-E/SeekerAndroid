using AndroidX.DocumentFile.Provider;
using System;
using System.Text.Json.Serialization;
using Seeker.Utils;

namespace Seeker
{
    /// <summary>
    /// Small info about which directories the user shared.
    /// </summary>
    [Serializable]
    public class UploadDirectoryInfo
    {
        public string UploadDataDirectoryUri;
        public bool UploadDataDirectoryUriIsFromTree;
        public bool IsLocked;
        public bool IsHidden;
        public string DisplayNameOverride;

        public bool HasError()
        {
            return ErrorState != UploadDirectoryError.NoError;
        }

        public string GetLastPathSegment()
        {
            return Android.Net.Uri.Parse(UploadDataDirectoryUri)?.LastPathSegment;
        }

        [JsonIgnore]
        [NonSerialized]
        public UploadDirectoryError ErrorState;

        [JsonIgnore]
        [NonSerialized]
        public DocumentFile UploadDirectory;
        
        [JsonIgnore]
        [NonSerialized]
        public bool IsSubdir;

        public void Reset()
        {
            UploadDataDirectoryUri = null;
            UploadDataDirectoryUriIsFromTree = true;
            IsLocked = false;
            IsHidden = false;
            DisplayNameOverride = null;
        }

        public UploadDirectoryInfo(string UploadDataDirectoryUri, bool UploadDataDirectoryUriIsFromTree, 
            bool IsLocked, bool IsHidden, string DisplayNameOverride)
        {
            this.UploadDataDirectoryUri = UploadDataDirectoryUri;
            this.UploadDataDirectoryUriIsFromTree = UploadDataDirectoryUriIsFromTree;
            this.IsLocked = IsLocked;
            this.IsHidden = IsHidden;
            this.DisplayNameOverride = DisplayNameOverride;
            ErrorState = UploadDirectoryError.NoError;
            UploadDirectory = null;
            IsSubdir = false;
        }

        public string GetPresentableName()
        {
            if (string.IsNullOrEmpty(DisplayNameOverride))
            {
                StorageUtils.GetAllFolderInfo(this, out _, out _, out _, 
                    out _, out string presentableName);
                
                return presentableName;
            }

            return DisplayNameOverride;
        }

        public string GetPresentableName(UploadDirectoryInfo ourTopMostParent)
        {
            string parentLastPathSegment = CommonHelpers
                .GetLastPathSegmentWithSpecialCaseProtection(ourTopMostParent.UploadDirectory, out bool msdCase);
            
            string ourLastPathSegment = CommonHelpers
                .GetLastPathSegmentWithSpecialCaseProtection(UploadDirectory, out bool ourMsdCase);
            
            if (ourMsdCase || msdCase)
            {
                return ourLastPathSegment; // not great but no good solution for msd. TODO test
            }

            StorageUtils.GetAllFolderInfo(ourTopMostParent, out bool overrideCase, 
                out _, out _, out string rootOverrideName, out string parentPresentableName);
                
            // remove parent part and replace it with the parent presentable name.
            return parentPresentableName + ourLastPathSegment.Substring(parentLastPathSegment.Length);
        }
    }

    public enum UploadDirectoryError
    {
        NoError = 0,
        DoesNotExist = 1,
        CannotWrite = 2,
        Unknown = 3,
    }

}