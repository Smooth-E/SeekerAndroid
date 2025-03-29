using System.Collections.Generic;
using System.Linq;
using Seeker.Extensions.SearchResponseExtensions;
using Soulseek;

namespace Seeker.Utils;

public class SearchResponseComparer(bool hideLocked) : IEqualityComparer<SearchResponse>
{
    public bool Equals(SearchResponse s1, SearchResponse s2)
    {
        if (s1 is null && s2 is null)
        {
            return true;
        }

        if (s1!.Username != s2!.Username)
        {
            return false;
        }

        if (s1.Files.Count != s2.Files.Count)
        {
            return false;
        }
        
        if (s1.Files.Count == 0)
        {
            return s1.LockedFiles.First().Filename == s2.LockedFiles.First().Filename;
        }

        if (s1.Files.First().Filename == s2.Files.First().Filename)
        {
            return true;
        }

        return false;

    }

    public int GetHashCode(SearchResponse s1)
    {
        var filenameHash = s1.GetElementAtAdapterPosition(hideLocked, 0).Filename.GetHashCode();
        return s1.Username.GetHashCode() + filenameHash;
    }
}
