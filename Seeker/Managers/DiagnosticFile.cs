using System;
using AndroidX.DocumentFile.Provider;
using Seeker.Utils;

namespace Seeker.Managers;

public static class DiagnosticFile
{
    public static bool Enabled = false;
    private static bool _diagnosticFilesystemErrorShown; // so that we only show it once.

    private static string CreateMessage(Soulseek.Diagnostics.DiagnosticEventArgs e)
    {
        var timestamp = e.Timestamp.ToString("[MM_dd-hh:mm:ss] ");
        string body;
        if (e.IncludesException)
        {
            body = e.Message + Environment.NewLine + e.Exception.Message
                   + Environment.NewLine + e.Exception.StackTrace;
        }
        else
        {
            body = e.Message;
        }

        return timestamp + body;
    }

    public static void AppendMessageToDiagFile(string msg)
    {
        if (!Enabled)
        {
            return;
        }
        
        var timestamp = DateTime.UtcNow.ToString("[MM_dd-hh:mm:ss] ");
        AppendLineToDiagFile(timestamp + msg);
    }

    private static void AppendLineToDiagFile(string line)
    {
        try
        {
            if (SeekerState.DiagnosticTextFile == null)
            {
                if (SeekerState.RootDocumentFile != null) //i.e. if api > 21 and they set it.
                {
                    SeekerState.DiagnosticTextFile =
                        SeekerState.RootDocumentFile.FindFile("seeker_diagnostics.txt");

                    if (SeekerState.DiagnosticTextFile == null)
                    {
                        SeekerState.DiagnosticTextFile = SeekerState.RootDocumentFile
                            .CreateFile("text/plain", "seeker_diagnostics");
                        if (SeekerState.DiagnosticTextFile == null)
                        {
                            return;
                        }
                    }
                }
                // if api < 30 and they did not set it. OR api <= 21, and they did set it.
                else if (SeekerState.UseLegacyStorage() || !SeekerState.SaveDataDirectoryUriIsFromTree)
                {
                    var musicFolderPath = Android.OS.Environment.DirectoryMusic;
                    var fullPath = string.IsNullOrEmpty(SeekerState.SaveDataDirectoryUri)
                        ? Android.OS.Environment.GetExternalStoragePublicDirectory(musicFolderPath)!.AbsolutePath
                        : Android.Net.Uri.Parse(SeekerState.SaveDataDirectoryUri)!.Path!;

                    var containingDir = new Java.IO.File(fullPath);

                    var javaDiagFile = new Java.IO.File(fullPath + "/seeker_diagnostics.txt");
                    var rootDir =
                        DocumentFile.FromFile(new Java.IO.File(fullPath + "/seeker_diagnostics.txt"));

                    if (javaDiagFile.Exists() || (containingDir.CanWrite() && javaDiagFile.CreateNewFile()))
                    {
                        SeekerState.DiagnosticTextFile = rootDir;
                    }
                }
                else // if api >29 and they did not set it. nothing we can do.
                {
                    return;
                }
            }

            if (SeekerState.DiagnosticStreamWriter == null)
            {
                var outputStream = SeekerApplication.ApplicationContext.ContentResolver!
                    .OpenOutputStream(SeekerState.DiagnosticTextFile!.Uri, "wa");
                if (outputStream == null)
                {
                    return;
                }

                SeekerState.DiagnosticStreamWriter = new System.IO.StreamWriter(outputStream);
                if (SeekerState.DiagnosticStreamWriter == null)
                {
                    return;
                }
            }

            SeekerState.DiagnosticStreamWriter.WriteLine(line);
            SeekerState.DiagnosticStreamWriter.Flush();
        }
        catch (Exception ex)
        {
            if (!_diagnosticFilesystemErrorShown)
            {
                var message = $"failed to write to diagnostic file {ex.Message} {line} {ex.StackTrace}";
                Logger.FirebaseDebug(message);
                SeekerApplication.ApplicationContext.ShowLongToast("Failed to write to diagnostic file.");
                _diagnosticFilesystemErrorShown = true;
            }
        }
    }
    
    public static void OnDiagnosticFileGenerated(object sender, 
        Soulseek.Diagnostics.DiagnosticEventArgs e)
    {
        AppendLineToDiagFile(CreateMessage(e));
    }
}
