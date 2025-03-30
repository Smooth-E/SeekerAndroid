using System;
using System.Collections.Generic;
using AndroidX.DocumentFile.Provider;

namespace Seeker.Utils;

public static class DocumentFileExtensions
{
    /// <summary>
    /// We only use this in Contents Response Resolver.
    /// </summary>
    /// <param name="dirFile"></param>
    /// <param name="dirToStrip"></param>
    /// <param name="diagFromDirectoryResolver"></param>
    /// <param name="volumePath"></param>
    /// <returns></returns>
    public static Soulseek.Directory ToSoulseekDirectory(this DocumentFile dirFile, string volumePath)
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

        var soulseekDirectory = new Soulseek.Directory(directoryPath, files);
        return soulseekDirectory;
    }
}
