using SlskHelp;

namespace Seeker.Utils;

/// <summary>
/// for the lower assembly
/// </summary>
public class UserListChecker : IUserListChecker
{
    public bool IsInUserList(string user)
    {
        return MainActivity.UserListContainsUser(user);
    }
}
