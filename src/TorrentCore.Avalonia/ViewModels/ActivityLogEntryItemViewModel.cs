using TorrentCore.Contracts.Diagnostics;

namespace TorrentCore.Avalonia.ViewModels;

public sealed class ActivityLogEntryItemViewModel(ActivityLogEntryDto entry)
{
    public long LogEntryId => entry.LogEntryId;
    public string Level => entry.Level;
    public string Category => entry.Category;
    public string EventType => entry.EventType;
    public string Message => entry.Message;
    public string? DetailsJson => entry.DetailsJson;
    public bool HasDetails => !string.IsNullOrWhiteSpace(DetailsJson);
    public bool HasTorrentId => entry.TorrentId is not null;
    public bool HasServiceInstanceId => entry.ServiceInstanceId is not null;
    public bool HasTraceId => !string.IsNullOrWhiteSpace(entry.TraceId);
    public string OccurredAtLocalText => entry.OccurredAtUtc.ToLocalTime().ToString("g");
    public string TorrentIdText => entry.TorrentId?.ToString("D") ?? string.Empty;
    public string ServiceInstanceIdText => entry.ServiceInstanceId?.ToString("D") ?? string.Empty;
    public string TraceIdText => entry.TraceId ?? string.Empty;
}
