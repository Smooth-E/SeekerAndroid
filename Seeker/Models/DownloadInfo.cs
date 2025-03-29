﻿using System;
using System.Threading;
using System.Threading.Tasks;

namespace Seeker.Models;

public class DownloadInfo
{
    public string username;
    public string fullFilename;
    public long Size;
    public int QueueLength;
    public CancellationTokenSource CancellationTokenSource;
    public int RetryCount;
    public Exception PreviousFailureException; // used for diagnostic purposes.
    public Android.Net.Uri IncompleteLocation = null; // used in the file backed case...

    // reference to the associated transfer item that we create based on this dl info.
    // we use this to store the complete uri for later playback option.
    public TransferItem TransferItemReference = null; 

    public int Depth = 1;

    public DownloadInfo(string usr, string file, long size, Task task, CancellationTokenSource token, int queueLength,
        int retryCount, int depth)
    {
        username = usr;
        fullFilename = file;
        Size = size;
        CancellationTokenSource = token;
        QueueLength = queueLength;
        RetryCount = retryCount;
        Depth = depth;
    }

    public DownloadInfo(string usr, string file, long size, Task task, CancellationTokenSource token, int queueLength,
        int retryCount, Exception previousFailureException, int depth)
    {
        username = usr;
        fullFilename = file;
        Size = size;
        CancellationTokenSource = token;
        QueueLength = queueLength;
        RetryCount = retryCount;
        PreviousFailureException = previousFailureException;
        Depth = depth;
    }

    public DownloadInfo(string usr, string file, long size, Task task, CancellationTokenSource token, int queueLength,
        int retryCount, Android.Net.Uri incompleteLocation, int depth)
    {
        username = usr;
        fullFilename = file;
        Size = size;
        CancellationTokenSource = token;
        QueueLength = queueLength;
        RetryCount = retryCount;
        IncompleteLocation = incompleteLocation;
        Depth = depth;
    }
}
