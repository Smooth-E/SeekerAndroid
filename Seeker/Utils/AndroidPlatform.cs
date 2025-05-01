using Android.OS;

namespace Seeker.Utils;

/// <summary>
/// Check capabilities of the Android platform the app is currently running on
/// </summary>
public static class AndroidPlatform
{
    // Pretty much all clients send "attributes" or limited metadata.
    // if lossless - they send duration, bit rate, bit depth, and sample rate
    // if lossy - they send duration and bit rate.

    //notes:
    // for lossless - bit rate = sample rate * bit depth * num channels
    //                1411.2 kpbs = 44.1kHz * 16 * 2
    //  --if the formula is required note that typically sample rate is in (44.1, 48, 88.2, 96)
    //      with the last too being very rare (never seen it).
    //      and bit depth in (16, 24, 32) with 16 most common, sometimes 24, never seen 32.  
    // for both lossy and lossless - determining bit rate from file size and duration is a bit too imprecise.  
    //      for mp3 320kps cbr one will get 320.3, 314, 315, etc.

    // for the pre-indexed media store (note: it's possible for one to revoke the photos&media permission and for
    // seeker to work right in all places by querying mediastore)
    //  api 29+ we have duration
    //  api 30+ we have bit rate
    //  api 31+ (Android 12) we have sample rate and bit depth - proposed change?  I don't think this made it in...

    // for the built in media retreiver (which requires actually reading the file) we have duration, bit rate,
    // with sample rate and bit depth for api31+

    // the library tag lib sharp can get us everything, tho it is 1 MB extra.
    
    public static bool HasMediaStoreDurationColumn() => Build.VERSION.SdkInt >= BuildVersionCodes.Q;

    public static bool HasMediaStoreBitRateColumn() => Build.VERSION.SdkInt >= BuildVersionCodes.R;
    
    public static bool HasProperPerAppLanguageSupport() => Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu;
}
