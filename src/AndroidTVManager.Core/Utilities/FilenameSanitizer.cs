using System.IO;
using System.Text;

namespace AndroidTVManager.Core.Utilities;

public static class FilenameSanitizer
{
    public static string Sanitize(string? value, string fallback = "device")
    {
        var input = string.IsNullOrWhiteSpace(value) ? fallback : value.Trim();
        var invalid = Path.GetInvalidFileNameChars();
        var builder = new StringBuilder(input.Length);
        foreach (var character in input)
            builder.Append(invalid.Contains(character) ? '_' : character);

        var result = builder.ToString().Trim().TrimEnd('.');
        return string.IsNullOrWhiteSpace(result) ? fallback : result;
    }
}
