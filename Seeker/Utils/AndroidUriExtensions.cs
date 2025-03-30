using Android.Net;

namespace Seeker.Utils;

public static class AndroidUriExtensions
{
    public static string GetPresentableName(this Uri uri, string folderToStripForPresentableNames, string volName)
    {
        if (uri.LastPathSegment == null)
        {
            Logger.FirebaseDebug($"{uri} has null last path segment");
            // next line throws
        }

        string presentableName = uri.LastPathSegment.Replace('/', '\\');

        // this means that the primary: is in the path so at least convert it from primary: to primary:\
        if (folderToStripForPresentableNames == null)
        {
            // i.e. if it has something after it...
            // primary: should be primary: not primary:\ but primary:Alarms should be primary:\Alarms
            if (volName != null && volName.Length != presentableName.Length)
            {
                var substring = presentableName.Substring(0, volName.Length);
                presentableName = substring + '\\' + presentableName.Substring(volName.Length);
            }
        }
        else
        {
            presentableName = presentableName.Substring(folderToStripForPresentableNames.Length);
        }

        return presentableName;
    }
}
