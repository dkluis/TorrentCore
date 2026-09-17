namespace TorrentCore.WebUI2.Shared;

public sealed record ConnectionProfile(Guid Id, string Name, string Address, string Environment);
public sealed record ConnectionState(List<ConnectionProfile> Profiles, Guid? ActiveProfileId, long Revision);
public sealed record SaveConnection(Guid? Id, string Name, string Address, string Environment, long ExpectedRevision);
public sealed record SelectConnection(Guid Id, long ExpectedRevision);
public sealed record ProbeConnection(string Address);
public sealed record ConnectionProbeResult(bool Connected, string Message, string? ServiceVersion, string? ServiceBuild);
