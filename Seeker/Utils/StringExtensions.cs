using System.Text;

namespace Seeker.Utils;

public static class StringExtensions
{
    // TODO: This was moved from MainActivity but seems unused. Consider removing if so
    public static bool HasNonASCIIChars(this string str)
    {
        return Encoding.UTF8.GetByteCount(str) != str.Length;
    }
}
