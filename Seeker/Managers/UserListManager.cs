using System;
using System.Collections.Generic;
using System.Linq;
using MessagePack;
using MessagePack.Formatters;
using MessagePack.Resolvers;
using Seeker.Serialization;
using SlskHelp;
using Soulseek;

namespace Seeker.Managers;

public static class UserListManager
{
    // TODO: Make the instance private
    // TODO: Make this field read-only and load / save the user list from preferences inside this class
    public static List<UserListItem> UserList = [];
    
    private static MessagePackSerializerOptions UserListOptions
    {
        get
        {
            var searchResponseResolver = CompositeResolver.Create(
                new IMessagePackFormatter[]
                {
                    new UserListItemFormatter(),
                    new UserStatusFormatter(),
                    new UserInfoFormatter(),
                    MessagePack.Formatters.TypelessFormatter.Instance
                },
                new IFormatterResolver[]
                {
                    ContractlessStandardResolver.Instance
                });
            return MessagePackSerializerOptions.Standard.WithResolver(searchResponseResolver);
        }
    }

    public class UserListChecker : IUserListChecker
    {
        public bool IsInUserList(string user)
        {
            return UserListContainsUser(user);
        }
    }

    public static int Count()
    {
        lock (UserList)
        {
            return UserList.Count;
        }
    }
    
    public static bool UserListContainsUser(string username)
    {
        lock (UserList)
        {
            if (UserList == null)
            {
                return false;
            }

            return UserList.FirstOrDefault(userListInfo =>
            {
                return userListInfo.Username == username;
            }) != null;
        }
    }
    
    public static bool UserListSetDoesNotExist(string username)
    {
        bool found = false;
        lock (UserList)
        {
            foreach (UserListItem item in UserList)
            {
                if (item.Username == username)
                {
                    found = true;
                    item.DoesNotExist = true;
                    break;
                }
            }
        }

        return found;
    }
    
    /// <summary>
    /// This is for adding new users...
    /// </summary>
    /// <returns>true if user was already added</returns>
    public static bool UserListAddUser(UserData userData, UserPresence? status = null)
    {
        lock (UserList)
        {
            bool found = false;
            foreach (UserListItem item in UserList)
            {
                if (item.Username == userData.Username)
                {
                    found = true;
                    if (userData != null)
                    {
                        if (status != null)
                        {
                            var oldStatus = item.UserStatus;
                            item.UserStatus = new UserStatus(status.Value, oldStatus?.IsPrivileged ?? false);
                        }

                        item.UserData = userData;
                        item.DoesNotExist = false;


                    }

                    break;
                }
            }

            if (!found)
            {
                // this is the normal case..
                var item = new UserListItem(userData.Username);
                item.UserData = userData;

                // if added an ignored user, then unignore the user.  the two are mutually exclusive.....
                if (SeekerApplication.IsUserInIgnoreList(userData.Username))
                {
                    SeekerApplication.RemoveFromIgnoreList(userData.Username);
                }

                UserList.Add(item);
                return false;
            }

            return true;
        }
    }
    
    /// <summary>
    /// Remove user from user list.
    /// </summary>
    /// <returns>true if user was found (if false then bad..)</returns>
    public static bool UserListRemoveUser(string username)
    {
        lock (UserList)
        {
            UserListItem itemToRemove = null;
            foreach (UserListItem item in UserList)
            {
                if (item.Username == username)
                {
                    itemToRemove = item;
                    break;
                }
            }

            if (itemToRemove == null)
            {
                return false;
            }

            UserList.Remove(itemToRemove);
            return true;
        }
    }

    public static string AsString()
    {
        if (UserList == null || UserList.Count == 0)
        {
            return string.Empty;
        }
        else
        {
            var bytes = MessagePackSerializer.Serialize(UserList, options: UserListOptions);
            return Convert.ToBase64String(bytes);
        }
    }
    
    public static List<UserListItem> FromString(string base64userList)
    {
        if (base64userList == string.Empty)
        {
            return new List<UserListItem>();
        }
        
        return MessagePackSerializer.Deserialize<List<UserListItem>>(
            Convert.FromBase64String(base64userList), 
            options: UserListOptions);
    }
}
