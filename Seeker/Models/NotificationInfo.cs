using System.Collections.Generic;

namespace Seeker.Models;

public class NotificationInfo(string firstDir)
{
    public int FilesUploadedToUser = 1;
    public List<string> DirNames = [firstDir];
}
