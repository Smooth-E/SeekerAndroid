using System.Collections.Generic;
using System.Linq;

namespace Seeker.Managers;

/// <summary>Manages recent users</summary>
public class RecentUserManager
{
    private readonly object recentUserLock = new();
    private List<string> recentUsers;
    
    /// <summary>Called at startup</summary>
    public void SetRecentUserList(List<string> recentUsers)
    {
        lock(recentUserLock)
        {
            this.recentUsers = recentUsers;
        }
    }

    public List<string> GetRecentUserList()
    {
        lock (recentUserLock)
        {
            return recentUsers.ToList(); // a copy to avoid threading issues.
        }
    }

    public void AddUserToTop(string user, bool andSave)
    {
        lock (recentUserLock)
        {
            if (recentUsers.Contains(user))
            {
                recentUsers.Remove(user);
            }
            recentUsers.Insert(0, user);
        }
        
        if (andSave)
        {
            SaveRecentUsers();
        }
    }
    
    public void SaveRecentUsers()
    {
        string recentUsersStr;
        var userList = GetRecentUserList();
        
        using (var writer = new System.IO.StringWriter())
        {
            var serializer = new System.Xml.Serialization.XmlSerializer(userList.GetType());
            serializer.Serialize(writer, userList);
            recentUsersStr = writer.ToString();
        }
        
        lock (SeekerApplication.SharedPrefLock)
        {
            SeekerState.SharedPreferences.Edit()!
                .PutString(KeyConsts.M_RecentUsersList, recentUsersStr)!
                .Commit();
        }
    }
    
    public static RecentUserManager FromXmlString(string xmlRecentUsersList)
    {
        // if empty then this is the first time creating it.  initialize it with our list of added users.
        var instance = new RecentUserManager();
        if (xmlRecentUsersList == string.Empty)
        {
            var count = UserListManager.UserList?.Count ?? 0;
            instance.SetRecentUserList(count > 0
                ? UserListManager.UserList!.Select(uli => uli.Username).ToList()
                : []);
        }
        else
        {
            using var stream = new System.IO.StringReader(xmlRecentUsersList);
            // this happens too often not allowing new things to be properly stored..
            var serializer = new System.Xml.Serialization.XmlSerializer(typeof(List<string>));
            instance.SetRecentUserList(serializer.Deserialize(stream) as List<string>);
        }
        
        return instance;
    }
}
