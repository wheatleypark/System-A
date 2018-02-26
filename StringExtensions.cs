using System.Globalization;

namespace WpsBehaviour
{
  public static class StringExtensions
  {
    public static string ToTitleCase(this string s)
    {
      return CultureInfo.InvariantCulture.TextInfo.ToTitleCase(s.ToLower());
    }
  }
}