namespace TorrentCore.WebUI2.Shared;

public static class ConnectionRules
{
    public static string NormalizeAddress(string address)
    {
        var value = address?.Trim() ?? string.Empty;
        if (!value.Contains("://", StringComparison.Ordinal)) value = "http://" + value;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https")
            || string.IsNullOrWhiteSpace(uri.Host) || uri.AbsolutePath != "/" || !string.IsNullOrEmpty(uri.Query)
            || !string.IsNullOrEmpty(uri.Fragment) || !string.IsNullOrEmpty(uri.UserInfo))
            throw new ArgumentException("Enter an HTTP or HTTPS TorrentCore address containing only a host and optional port.");
        return uri.GetLeftPart(UriPartial.Authority);
    }

    public static ConnectionState Save(ConnectionState state, SaveConnection request)
    {
        CheckRevision(state, request.ExpectedRevision);
        if (string.IsNullOrWhiteSpace(request.Name)) throw new ArgumentException("Enter a name for this TorrentCore installation.");
        if (request.Environment is not ("Production" or "Test")) throw new ArgumentException("Choose Production or Test before saving this connection.");
        var address = NormalizeAddress(request.Address);
        if (request.Id.HasValue && state.Profiles.All(x => x.Id != request.Id)) throw new ArgumentException("The selected connection no longer exists.");
        if (state.Profiles.Any(x => x.Id != request.Id && string.Equals(x.Address, address, StringComparison.OrdinalIgnoreCase)))
            throw new ArgumentException("A connection profile already uses this TorrentCore address.");
        var profile = new ConnectionProfile(request.Id ?? Guid.NewGuid(), request.Name.Trim(), address, request.Environment);
        var profiles = state.Profiles.ToList();
        var index = profiles.FindIndex(x => x.Id == profile.Id);
        if (index >= 0) profiles[index] = profile; else profiles.Add(profile);
        return new(profiles, profile.Id, state.Revision + 1);
    }

    public static ConnectionState Select(ConnectionState state, SelectConnection request)
    {
        CheckRevision(state, request.ExpectedRevision);
        if (state.Profiles.All(x => x.Id != request.Id)) throw new ArgumentException("The selected connection no longer exists.");
        return state with { ActiveProfileId = request.Id, Revision = state.Revision + 1 };
    }

    public static ConnectionState Delete(ConnectionState state, SelectConnection request)
    {
        CheckRevision(state, request.ExpectedRevision);
        if (state.Profiles.All(x => x.Id != request.Id)) throw new ArgumentException("The selected connection no longer exists.");
        return new(state.Profiles.Where(x => x.Id != request.Id).ToList(), state.ActiveProfileId == request.Id ? null : state.ActiveProfileId, state.Revision + 1);
    }

    public static void CheckRevision(ConnectionState state, long expected)
    {
        if (state.Revision != expected) throw new InvalidOperationException("The connection changed in another tab. Refresh and try again.");
    }
}
