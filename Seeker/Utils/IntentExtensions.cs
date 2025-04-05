using Android.Content;

namespace Seeker.Utils;

public static class IntentExtensions
{
    /// <returns>Whether this intent requests Seeker services shutdown</returns>
    public static bool IsShuttingDown(this Intent intent)
    {
        return intent?.Action switch
        {
            null => false,
            SeekerApplication.ACTION_SHUTDOWN => true,
            _ => false
        };
    }
}
