using Common;
using Soulseek;

namespace Seeker.Models;

public class BrowseResponseEvent(
    BrowseResponse originalBrowseResponse,
    TreeNode<Directory> t,
    string u,
    string startingLocation)
{
    public TreeNode<Directory> BrowseResponseTree = t;
    public BrowseResponse OriginalBrowseResponse = originalBrowseResponse;
    public string Username = u;
    public string StartingLocation = startingLocation;
}
