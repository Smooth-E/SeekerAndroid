using System;
using System.Collections.Generic;
using Android.Content;

namespace Seeker.Utils;

public static class AudioUtils
{
     /// <summary>
    /// Any exceptions here get caught.  worst case, you just get no metadata...
    /// </summary>
    /// <param name="contentResolver"></param>
    /// <param name="displayName"></param>
    /// <param name="size"></param>
    /// <param name="presentableName"></param>
    /// <param name="childUri"></param>
    /// <param name="allMediaInfoDict"></param>
    /// <param name="prevInfoToUse"></param>
    /// <returns></returns>
    public static Tuple<int, int, int, int> GetAudioAttributes(
        ContentResolver contentResolver,
        string displayName,
        long size,
        string presentableName,
        Android.Net.Uri childUri,
        Dictionary<string, List<Tuple<string, int, int>>> allMediaInfoDict,
        Dictionary<string, Tuple<long, string, Tuple<int, int, int, int>, bool, bool>> prevInfoToUse)
    {
        try
        {
            if (prevInfoToUse != null)
            {
                if (prevInfoToUse.ContainsKey(presentableName))
                {
                    var tuple = prevInfoToUse[presentableName];
                    if (tuple.Item1 == size) // this is the file...
                    {
                        return tuple.Item3;
                    }
                }
            }

            // get media attributes...
            bool supported = IsSupportedAudio(presentableName);
            if (!supported)
            {
                return null;
            }

            bool lossless = IsLossless(presentableName);
            bool uncompressed = IsUncompressed(presentableName);
            int duration = -1;
            int bitrate = -1;
            int sampleRate = -1;
            int bitDepth = -1;

            // else it has no more additional data for us...
            bool useContentResolverQuery = AndroidPlatform.HasMediaStoreDurationColumn();

            if (useContentResolverQuery)
            {
                bool hasBitRate = AndroidPlatform.HasMediaStoreBitRateColumn();

                // querying it every time was slow...
                // so now we query it all ahead of time (with 1 query request) and put it in a dict.
                string key = size + displayName;

                if (allMediaInfoDict.ContainsKey(key))
                {
                    string nameToSearchFor = presentableName.Replace('\\', '/');
                    bool found = true;
                    var listInfo = allMediaInfoDict[key];
                    Tuple<string, int, int> infoItem = null;
                    if (listInfo.Count > 1)
                    {
                        found = false;
                        foreach (var item in listInfo)
                        {
                            if (item.Item1.Contains(nameToSearchFor))
                            {
                                infoItem = item;
                                found = true;
                                break;
                            }
                        }
                    }
                    else
                    {
                        infoItem = listInfo[0];
                    }

                    if (found)
                    {
                        duration = infoItem.Item2 / 1000; // in ms
                        if (hasBitRate)
                        {
                            bitrate = infoItem.Item3;
                        }
                    }
                }
            }

            if ((SeekerState.PerformDeepMetadataSearch && (bitrate == -1 || duration == -1) && size != 0))
            {
                try
                {
                    Android.Media.MediaMetadataRetriever mediaMetadataRetriever =
                        new Android.Media.MediaMetadataRetriever();

                    // TODO: error file descriptor must not be null.
                    mediaMetadataRetriever.SetDataSource(SeekerState.ActiveActivityRef, childUri);

                    string? bitRateStr = mediaMetadataRetriever.ExtractMetadata(Android.Media.MetadataKey.Bitrate);

                    string? durationStr =
                        mediaMetadataRetriever.ExtractMetadata(Android.Media.MetadataKey.Duration);

                    if (AndroidPlatform.HasMediaStoreDurationColumn())
                    {
                        mediaMetadataRetriever.Close(); // added in api 29
                    }
                    else
                    {
                        mediaMetadataRetriever.Release();
                    }

                    if (bitRateStr != null)
                    {
                        bitrate = int.Parse(bitRateStr);
                    }

                    if (durationStr != null)
                    {
                        duration = int.Parse(durationStr) / 1000;
                    }
                }
                catch (Exception e)
                {
                    // ape and aiff always fail with built in metadata retreiver.
                    if (System.IO.Path.GetExtension(presentableName).ToLower() == ".ape")
                    {
                        MicroTagReader
                            .GetApeMetadata(contentResolver, childUri, out sampleRate, out bitDepth, out duration);
                    }
                    else if (System.IO.Path.GetExtension(presentableName).ToLower() == ".aiff")
                    {
                        MicroTagReader
                            .GetAiffMetadata(contentResolver, childUri, out sampleRate, out bitDepth, out duration);
                    }

                    // if still not fixed
                    if (sampleRate == -1 || duration == -1 || bitDepth == -1)
                    {
                        Logger.FirebaseDebug("MediaMetadataRetriever: " + e.Message + e.StackTrace + " isnull"
                                                 + (SeekerState.ActiveActivityRef == null) + childUri?.ToString());
                    }
                }
            }

            // this is the mp3 vbr case, android meta data retriever and therefore also the mediastore cache fail
            // quite badly in this case.  they often return the min vbr bitrate of 32000.
            // if its under 128kbps then lets just double check it..
            // I did test .m4a vbr.  android meta data retriever handled it quite well.
            // on api 19 the vbr being reported at 32000 is reported as 128000.... both obviously quite incorrect...
            if (System.IO.Path.GetExtension(presentableName) == ".mp3"
                && (bitrate >= 0 && bitrate <= 128000)
                && size != 0)
            {
                if (SeekerState.PerformDeepMetadataSearch)
                {
                    MicroTagReader.GetMp3Metadata(contentResolver, childUri, duration, size, out bitrate);
                }
                else
                {
                    bitrate = -1; // better to have nothing than for it to be so blatantly wrong..
                }
            }

            if (SeekerState.PerformDeepMetadataSearch
                && System.IO.Path.GetExtension(presentableName) == ".flac" && size != 0)
            {
                MicroTagReader.GetFlacMetadata(contentResolver, childUri, out sampleRate, out bitDepth);
            }

            // if uncompressed we can use this simple formula
            if (uncompressed)
            {
                if (bitrate != -1)
                {
                    // bitrate = 2 * sampleRate * depth
                    // so test pairs in order of precedence..
                    if ((bitrate) / (2 * 44100) == 16)
                    {
                        sampleRate = 44100;
                        bitDepth = 16;
                    }
                    else if ((bitrate) / (2 * 44100) == 24)
                    {
                        sampleRate = 44100;
                        bitDepth = 24;
                    }
                    else if ((bitrate) / (2 * 48000) == 16)
                    {
                        sampleRate = 48000;
                        bitDepth = 16;
                    }
                    else if ((bitrate) / (2 * 48000) == 24)
                    {
                        sampleRate = 48000;
                        bitDepth = 24;
                    }
                }
            }

            if (duration == -1 && bitrate == -1 && bitDepth == -1 && sampleRate == -1)
            {
                return null;
            }

            return new Tuple<int, int, int, int>(
                duration,
                // for lossless do not send bitrate!! no other client does that!!
                (lossless || bitrate == -1) ? -1 : (bitrate / 1000),
                bitDepth,
                sampleRate);
        }
        catch (Exception e)
        {
            Logger.FirebaseDebug("get audio attr failed: " + e.Message + e.StackTrace);
            return null;
        }
    }
     
    private static bool IsSupportedAudio(string name)
    {
        string ext = System.IO.Path.GetExtension(name);
        switch (ext)
        {
            case ".ape":
            case ".flac":
            case ".wav":
            case ".alac":
            case ".aiff":
            case ".mp3":
            case ".m4a":
            case ".wma":
            case ".aac":
            case ".opus":
            case ".ogg":
            case ".oga":
                return true;
            default:
                return false;
        }
    }
    
    private static bool IsUncompressed(string name)
    {
        string ext = System.IO.Path.GetExtension(name);
        switch (ext)
        {
            case ".wav":
                return true;
            default:
                return false;
        }
    }

    private static bool IsLossless(string name)
    {
        string ext = System.IO.Path.GetExtension(name);
        switch (ext)
        {
            case ".ape":
            case ".flac":
            case ".wav":
            case ".alac":
            case ".aiff":
                return true;
            default:
                return false;
        }
    }
}