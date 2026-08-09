namespace TorrentCore.Service.Configuration;

internal static class VpnEgressSettingsValidation
{
    public static bool TryNormalizeEndpoint(string? value, out string normalizedValue, out string error)
    {
        normalizedValue = string.Empty;
        error = string.Empty;

        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var endpoint) ||
            string.IsNullOrWhiteSpace(endpoint.Host))
        {
            error = "VPN egress validation endpoint must be an absolute HTTPS URL.";
            return false;
        }

        if (!string.Equals(endpoint.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            error = "VPN egress validation endpoint must use HTTPS.";
            return false;
        }

        if (!string.IsNullOrEmpty(endpoint.UserInfo))
        {
            error = "VPN egress validation endpoint must not contain credentials.";
            return false;
        }

        if (!string.IsNullOrEmpty(endpoint.Fragment))
        {
            error = "VPN egress validation endpoint must not contain a fragment.";
            return false;
        }

        normalizedValue = endpoint.AbsoluteUri;
        return true;
    }

    public static bool TryNormalizeCidrs(
        IEnumerable<string>? values,
        out IReadOnlyList<string> normalizedValues,
        out string error)
    {
        var normalized = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var value in values ?? [])
        {
            if (!Ipv4CidrBlock.TryParse(value, out var block))
            {
                normalizedValues = [];
                error = $"VPN direct-ISP CIDR '{value}' must be a valid IPv4 CIDR.";
                return false;
            }

            var canonicalValue = block.ToString();
            if (seen.Add(canonicalValue))
            {
                normalized.Add(canonicalValue);
            }
        }

        normalizedValues = normalized;
        error = string.Empty;
        return true;
    }

    public static bool TryValidateIntervals(
        int degradedCheckIntervalSeconds,
        int readyCheckIntervalSeconds,
        int requestTimeoutSeconds,
        out string error,
        out string? target)
    {
        if (degradedCheckIntervalSeconds < 1)
        {
            error = "VpnEgressDegradedCheckIntervalSeconds must be 1 or greater.";
            target = nameof(TorrentCoreServiceOptions.VpnEgressDegradedCheckIntervalSeconds);
            return false;
        }

        if (readyCheckIntervalSeconds < 1)
        {
            error = "VpnEgressReadyCheckIntervalSeconds must be 1 or greater.";
            target = nameof(TorrentCoreServiceOptions.VpnEgressReadyCheckIntervalSeconds);
            return false;
        }

        if (requestTimeoutSeconds < 1)
        {
            error = "VpnEgressRequestTimeoutSeconds must be 1 or greater.";
            target = nameof(TorrentCoreServiceOptions.VpnEgressRequestTimeoutSeconds);
            return false;
        }

        if (requestTimeoutSeconds >= degradedCheckIntervalSeconds ||
            requestTimeoutSeconds >= readyCheckIntervalSeconds)
        {
            error = "VpnEgressRequestTimeoutSeconds must be less than both VPN check intervals.";
            target = nameof(TorrentCoreServiceOptions.VpnEgressRequestTimeoutSeconds);
            return false;
        }

        error = string.Empty;
        target = null;
        return true;
    }
}
