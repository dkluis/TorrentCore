namespace TorrentCore.Core.Torrents;

// Service-owned settings. No submitter or downstream configuration is consulted.
public sealed record MediaPayloadPolicy(string[] RequiredVideoExtensions, string[] ProhibitedPayloadExtensions)
{
    public static string[] Normalize(IEnumerable<string> extensions)
    {
        ArgumentNullException.ThrowIfNull(extensions);
        var values = extensions.Select(value => value?.Trim().TrimStart('.').ToLowerInvariant()).ToArray();
        if (values.Length is 0 or > 64 || values.Any(value => string.IsNullOrEmpty(value) ||
                value.Length > 20 || !value.All(char.IsAsciiLetterOrDigit)))
        { throw new ArgumentException("Provide 1–64 filename extensions, using letters or digits and an optional leading dot."); }
        return values.Select(value => "." + value).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    public MediaPayloadPolicy Validated()
    {
        var required = Normalize(RequiredVideoExtensions);
        var prohibited = Normalize(ProhibitedPayloadExtensions);
        if (required.Intersect(prohibited, StringComparer.OrdinalIgnoreCase).Any())
        { throw new ArgumentException("Required video extensions and prohibited payload extensions must not overlap."); }
        return new(required, prohibited);
    }

    public MediaPayloadInspection Inspect(IReadOnlyList<string> files)
    {
        var prohibited = files.Where(path => ProhibitedPayloadExtensions.Contains(Path.GetExtension(path),
            StringComparer.OrdinalIgnoreCase)).Distinct(StringComparer.Ordinal).ToArray();
        if (prohibited.Length > 0)
        { return new(prohibited.Any(path => Path.GetExtension(path).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            ? "Executable" : "ProhibitedExtension", prohibited, files.Take(1000).ToArray()); }
        return files.Any(path => RequiredVideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            ? new(null, [], []) : new("MissingVideo", [], files.Take(1000).ToArray());
    }
}

public sealed record MediaPayloadInspection(string? RejectionReason, string[] OffendingFiles, string[] ObservedFiles)
{
    public bool Rejected => RejectionReason is not null;
}
