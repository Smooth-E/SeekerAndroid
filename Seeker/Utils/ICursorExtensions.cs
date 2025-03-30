using Android.Database;

namespace Seeker.Utils;

public static class ICursorExtensions
{
    /// <summary>
    /// Util method to close a closeable. Ignores all exceptions.
    /// </summary>
    public static void closeQuietly(this ICursor closeable)
    {
        if (closeable != null)
        {
            try
            {
                closeable.Close();
            }
            catch
            {
                // ignore exception
            }
        }
    }
}
