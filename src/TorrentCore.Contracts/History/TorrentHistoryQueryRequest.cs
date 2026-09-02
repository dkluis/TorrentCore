namespace TorrentCore.Contracts.History;

public sealed class TorrentHistoryQueryRequest
{
    public Guid?                  TorrentId   { get; init; }
    public string?                TorrentName { get; init; }
    public string?                CategoryKey { get; init; }
    public string?                State       { get; init; }
    public TorrentHistoryOutcome? Outcome     { get; init; }
    public bool?                  Removed     { get; init; }
    public DateOnly?              FromDate    { get; init; }
    public DateOnly?              ToDate      { get; init; }
    public int?                   Take        { get; init; }
}
