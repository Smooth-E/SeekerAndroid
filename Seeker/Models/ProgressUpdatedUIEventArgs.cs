using System;

namespace Seeker.Models;

public class ProgressUpdatedUIEventArgs(
    TransferItem transferItem,
    bool wasFailed,
    bool fullRefresh,
    double percentComplete,
    double averageSpeedBytes)
    : EventArgs
{
    public TransferItem TransferItem = transferItem;
    public bool WasFailed = wasFailed;
    public bool FullRefresh = fullRefresh;
    public double PercentComplete = percentComplete;
    public double AverageSpeedBytes = averageSpeedBytes;
}
