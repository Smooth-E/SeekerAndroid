using System;

namespace Seeker.Models;

public class DownloadAddedEventArgs(DownloadInfo downloadInfo) : EventArgs
{
    // TODO: Remove this as downloadInfo is already present
    public DownloadInfo dlInfo = downloadInfo;
}
