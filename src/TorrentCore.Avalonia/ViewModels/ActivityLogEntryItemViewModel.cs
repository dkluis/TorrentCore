#region

using CommunityToolkit.Mvvm.Input;
using TorrentCore.Contracts.Diagnostics;

#endregion

namespace TorrentCore.Avalonia.ViewModels;

public sealed class ActivityLogEntryItemViewModel
{
    private readonly ActivityLogEntryDto _entry;
    private readonly Action<Guid>?       _showTorrentDetail;

    public ActivityLogEntryItemViewModel(ActivityLogEntryDto entry, Action<Guid>? showTorrentDetail = null)
    {
        _entry                   = entry;
        _showTorrentDetail       = showTorrentDetail;
        OpenTorrentDetailCommand = new RelayCommand(OpenTorrentDetail, () => CanOpenTorrentDetail);
    }

    public long          LogEntryId               => _entry.LogEntryId;
    public string        Level                    => _entry.Level;
    public string        Category                 => _entry.Category;
    public string        EventType                => _entry.EventType;
    public string        Message                  => _entry.Message;
    public string?       DetailsJson              => _entry.DetailsJson;
    public bool          HasDetails               => !string.IsNullOrWhiteSpace(DetailsJson);
    public bool          HasTorrentId             => _entry.TorrentId is not null;
    public bool          HasServiceInstanceId     => _entry.ServiceInstanceId is not null;
    public bool          HasTraceId               => !string.IsNullOrWhiteSpace(_entry.TraceId);
    public bool          CanOpenTorrentDetail     => _entry.TorrentId is not null && _showTorrentDetail is not null;
    public string        OccurredAtLocalText      => _entry.OccurredAtUtc.ToLocalTime().ToString("g");
    public string        TorrentIdText            => _entry.TorrentId?.ToString("D")         ?? string.Empty;
    public string        ServiceInstanceIdText    => _entry.ServiceInstanceId?.ToString("D") ?? string.Empty;
    public string        TraceIdText              => _entry.TraceId                          ?? string.Empty;
    public IRelayCommand OpenTorrentDetailCommand { get; }

    private void OpenTorrentDetail()
    {
        if (_entry.TorrentId is Guid torrentId)
        {
            _showTorrentDetail?.Invoke(torrentId);
        }
    }
}
