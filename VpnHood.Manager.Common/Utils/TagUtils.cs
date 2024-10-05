using System.Text.RegularExpressions;

namespace VpnHood.Manager.Common.Utils;

public static class TagUtils
{
    public static string? TagsToString(string[] tags)
    {
        // add # to tags if it is not
        tags = tags
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Select(x => x.StartsWith("#") ? x.Trim() : $"#{x}".Trim()).ToArray();

        // tag should only have letters, numbers, underscore and colon
        var regex = new Regex("^#[a-zA-Z0-9_:]+$");
        var invalidTag = tags.FirstOrDefault(x => !regex.IsMatch(x));
        if (invalidTag != null)
            throw new ArgumentException("Invalid tag format. Tag should only have letters, numbers, and underscore.", invalidTag);

        return tags.Length == 0 ? null : string.Join(" ", tags);
    }

    public static string[] TagsFromString(string? tags)
    {
        return string.IsNullOrWhiteSpace(tags)
            ? []
            : tags.Split(" ");
    }

    public static string BuildLocation(string? location)
    {
        return $"#loc:{location}";
    }
}