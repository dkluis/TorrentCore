using System.Reflection;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.Primitives;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Torrents;

namespace TorrentCore.Service.Application;

public sealed class InMemoryTorrentApplicationService(IHostEnvironment hostEnvironment) : ITorrentApplicationService
{
    private readonly object _syncRoot = new();
    private readonly Dictionary<Guid, TorrentRecord> _torrents = CreateSeedData();
    private readonly string _downloadRootPath = Path.Combine(AppContext.BaseDirectory, "downloads");

    public Task<EngineHostStatusDto> GetHostStatusAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return Task.FromResult(new EngineHostStatusDto
            {
                ServiceName               = "TorrentCore.Service",
                ServiceVersion            = Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0.0.0",
                Status                    = EngineHostStatus.Ready,
                EnvironmentName           = hostEnvironment.EnvironmentName,
                DownloadRootPath          = _downloadRootPath,
                TorrentCount              = _torrents.Count,
                SupportsMagnetAdds        = true,
                SupportsPause             = true,
                SupportsResume            = true,
                SupportsRemove            = true,
                SupportsPersistentStorage = false,
                SupportsMultiHost         = false,
                CheckedAtUtc              = DateTimeOffset.UtcNow,
            });
        }
    }

    public Task<IReadOnlyList<TorrentSummaryDto>> GetTorrentsAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var results = _torrents.Values
                .OrderByDescending(record => record.AddedAtUtc)
                .Select(MapSummary)
                .ToArray();

            return Task.FromResult<IReadOnlyList<TorrentSummaryDto>>(results);
        }
    }

    public Task<TorrentDetailDto?> GetTorrentAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            return Task.FromResult(
                _torrents.TryGetValue(torrentId, out var record)
                    ? MapDetail(record)
                    : null);
        }
    }

    public Task<TorrentDetailDto> AddMagnetAsync(AddMagnetRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var magnet = ParseMagnet(request.MagnetUri);

        lock (_syncRoot)
        {
            if (_torrents.Values.Any(record =>
                    string.Equals(record.InfoHash, magnet.InfoHash, StringComparison.OrdinalIgnoreCase)))
            {
                throw new ServiceOperationException(
                    "duplicate_magnet",
                    "A torrent with the same info hash already exists on this host.",
                    StatusCodes.Status409Conflict,
                    nameof(request.MagnetUri));
            }

            var now = DateTimeOffset.UtcNow;
            var torrentId = Guid.NewGuid();
            var record = new TorrentRecord
            {
                TorrentId                   = torrentId,
                Name                        = magnet.DisplayName,
                MagnetUri                   = request.MagnetUri.Trim(),
                InfoHash                    = magnet.InfoHash,
                SavePath                    = Path.Combine(_downloadRootPath, SanitizePathSegment(magnet.DisplayName)),
                State                       = TorrentState.ResolvingMetadata,
                ProgressPercent             = 0,
                DownloadedBytes             = 0,
                TotalBytes                  = null,
                DownloadRateBytesPerSecond  = 0,
                UploadRateBytesPerSecond    = 0,
                AddedAtUtc                  = now,
                LastActivityAtUtc           = now,
            };

            _torrents.Add(torrentId, record);
            return Task.FromResult(MapDetail(record));
        }
    }

    public Task<TorrentActionResultDto> PauseAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var record = GetRequiredRecord(torrentId);

            if (!CanPause(record.State))
            {
                throw new ServiceOperationException(
                    "invalid_state",
                    $"Torrent '{record.Name}' cannot be paused while in state '{record.State}'.",
                    StatusCodes.Status409Conflict,
                    nameof(torrentId));
            }

            record.State = TorrentState.Paused;
            record.DownloadRateBytesPerSecond = 0;
            record.UploadRateBytesPerSecond = 0;
            record.LastActivityAtUtc = DateTimeOffset.UtcNow;

            return Task.FromResult(new TorrentActionResultDto
            {
                TorrentId     = record.TorrentId,
                Action        = "pause",
                State         = record.State,
                ProcessedAtUtc = record.LastActivityAtUtc.Value,
                DataDeleted   = false,
            });
        }
    }

    public Task<TorrentActionResultDto> ResumeAsync(Guid torrentId, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var record = GetRequiredRecord(torrentId);

            if (!CanResume(record.State))
            {
                throw new ServiceOperationException(
                    "invalid_state",
                    $"Torrent '{record.Name}' cannot be resumed while in state '{record.State}'.",
                    StatusCodes.Status409Conflict,
                    nameof(torrentId));
            }

            record.State = record.ProgressPercent >= 100 ? TorrentState.Seeding : TorrentState.Downloading;
            record.DownloadRateBytesPerSecond = record.State == TorrentState.Downloading ? 3_250_000 : 0;
            record.UploadRateBytesPerSecond = record.State == TorrentState.Seeding ? 760_000 : 120_000;
            record.LastActivityAtUtc = DateTimeOffset.UtcNow;

            return Task.FromResult(new TorrentActionResultDto
            {
                TorrentId      = record.TorrentId,
                Action         = "resume",
                State          = record.State,
                ProcessedAtUtc = record.LastActivityAtUtc.Value,
                DataDeleted    = false,
            });
        }
    }

    public Task<TorrentActionResultDto> RemoveAsync(Guid torrentId, RemoveTorrentRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        lock (_syncRoot)
        {
            var record = GetRequiredRecord(torrentId);
            _torrents.Remove(torrentId);

            return Task.FromResult(new TorrentActionResultDto
            {
                TorrentId      = record.TorrentId,
                Action         = "remove",
                State          = TorrentState.Removed,
                ProcessedAtUtc = DateTimeOffset.UtcNow,
                DataDeleted    = request.DeleteData,
            });
        }
    }

    private static Dictionary<Guid, TorrentRecord> CreateSeedData()
    {
        var now = DateTimeOffset.UtcNow;
        var firstId = Guid.Parse("6fa459ea-ee8a-3ca4-894e-db77e160355e");
        var secondId = Guid.Parse("7c9e6679-7425-40de-944b-e07fc1f90ae7");

        return new Dictionary<Guid, TorrentRecord>
        {
            [firstId] = new()
            {
                TorrentId                  = firstId,
                Name                       = "Ubuntu 24.04 LTS",
                MagnetUri                  = "magnet:?xt=urn:btih:0123456789ABCDEF0123456789ABCDEF01234567&dn=Ubuntu%2024.04%20LTS",
                InfoHash                   = "0123456789ABCDEF0123456789ABCDEF01234567",
                SavePath                   = Path.Combine("downloads", "Ubuntu 24.04 LTS"),
                State                      = TorrentState.Downloading,
                ProgressPercent            = 42.5,
                DownloadedBytes            = 1_824_500_000,
                TotalBytes                 = 4_293_918_720,
                DownloadRateBytesPerSecond = 5_120_000,
                UploadRateBytesPerSecond   = 185_000,
                AddedAtUtc                 = now.AddMinutes(-35),
                LastActivityAtUtc          = now.AddSeconds(-8),
            },
            [secondId] = new()
            {
                TorrentId                  = secondId,
                Name                       = "Fedora Workstation ISO",
                MagnetUri                  = "magnet:?xt=urn:btih:89ABCDEF0123456789ABCDEF0123456789ABCDEF&dn=Fedora%20Workstation%20ISO",
                InfoHash                   = "89ABCDEF0123456789ABCDEF0123456789ABCDEF",
                SavePath                   = Path.Combine("downloads", "Fedora Workstation ISO"),
                State                      = TorrentState.Paused,
                ProgressPercent            = 88.2,
                DownloadedBytes            = 1_986_000_000,
                TotalBytes                 = 2_251_799_813,
                DownloadRateBytesPerSecond = 0,
                UploadRateBytesPerSecond   = 0,
                AddedAtUtc                 = now.AddHours(-3),
                LastActivityAtUtc          = now.AddMinutes(-22),
            },
        };
    }

    private TorrentRecord GetRequiredRecord(Guid torrentId)
    {
        if (_torrents.TryGetValue(torrentId, out var record))
        {
            return record;
        }

        throw new ServiceOperationException(
            "torrent_not_found",
            $"Torrent '{torrentId}' was not found.",
            StatusCodes.Status404NotFound,
            nameof(torrentId));
    }

    private static MagnetMetadata ParseMagnet(string magnetUri)
    {
        if (string.IsNullOrWhiteSpace(magnetUri))
        {
            throw new ServiceOperationException(
                "invalid_magnet",
                "MagnetUri is required.",
                StatusCodes.Status400BadRequest,
                nameof(AddMagnetRequest.MagnetUri));
        }

        if (!Uri.TryCreate(magnetUri.Trim(), UriKind.Absolute, out var uri) ||
            !string.Equals(uri.Scheme, "magnet", StringComparison.OrdinalIgnoreCase))
        {
            throw new ServiceOperationException(
                "invalid_magnet",
                "MagnetUri must be a valid magnet URI.",
                StatusCodes.Status400BadRequest,
                nameof(AddMagnetRequest.MagnetUri));
        }

        var query = QueryHelpers.ParseQuery(uri.Query);
        var infoHash = ExtractInfoHash(query);
        var displayName = query.TryGetValue("dn", out var names) && !StringValues.IsNullOrEmpty(names)
            ? names[0]!
            : $"Magnet {infoHash[..8]}";

        return new MagnetMetadata(infoHash, displayName);
    }

    private static string ExtractInfoHash(Dictionary<string, StringValues> query)
    {
        if (!query.TryGetValue("xt", out var exactTopics) || StringValues.IsNullOrEmpty(exactTopics))
        {
            throw new ServiceOperationException(
                "invalid_magnet",
                "MagnetUri must include an exact topic info hash (xt=urn:btih:...).",
                StatusCodes.Status400BadRequest,
                nameof(AddMagnetRequest.MagnetUri));
        }

        foreach (var exactTopic in exactTopics)
        {
            const string prefix = "urn:btih:";

            if (exactTopic is not null && exactTopic.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                var infoHash = exactTopic[prefix.Length..].Trim();

                if (infoHash.Length is 32 or 40)
                {
                    return infoHash.ToUpperInvariant();
                }
            }
        }

        throw new ServiceOperationException(
            "invalid_magnet",
            "MagnetUri must include a btih exact topic value.",
            StatusCodes.Status400BadRequest,
            nameof(AddMagnetRequest.MagnetUri));
    }

    private static TorrentSummaryDto MapSummary(TorrentRecord record)
    {
        return new TorrentSummaryDto
        {
            TorrentId                  = record.TorrentId,
            Name                       = record.Name,
            State                      = record.State,
            ProgressPercent            = record.ProgressPercent,
            DownloadedBytes            = record.DownloadedBytes,
            TotalBytes                 = record.TotalBytes,
            DownloadRateBytesPerSecond = record.DownloadRateBytesPerSecond,
            UploadRateBytesPerSecond   = record.UploadRateBytesPerSecond,
            AddedAtUtc                 = record.AddedAtUtc,
            CompletedAtUtc             = record.CompletedAtUtc,
            LastActivityAtUtc          = record.LastActivityAtUtc,
            ErrorMessage               = record.ErrorMessage,
            CanPause                   = CanPause(record.State),
            CanResume                  = CanResume(record.State),
            CanRemove                  = CanRemove(record.State),
        };
    }

    private static TorrentDetailDto MapDetail(TorrentRecord record)
    {
        return new TorrentDetailDto
        {
            TorrentId                  = record.TorrentId,
            Name                       = record.Name,
            State                      = record.State,
            MagnetUri                  = record.MagnetUri,
            InfoHash                   = record.InfoHash,
            SavePath                   = record.SavePath,
            ProgressPercent            = record.ProgressPercent,
            DownloadedBytes            = record.DownloadedBytes,
            TotalBytes                 = record.TotalBytes,
            DownloadRateBytesPerSecond = record.DownloadRateBytesPerSecond,
            UploadRateBytesPerSecond   = record.UploadRateBytesPerSecond,
            AddedAtUtc                 = record.AddedAtUtc,
            CompletedAtUtc             = record.CompletedAtUtc,
            LastActivityAtUtc          = record.LastActivityAtUtc,
            ErrorMessage               = record.ErrorMessage,
            CanPause                   = CanPause(record.State),
            CanResume                  = CanResume(record.State),
            CanRemove                  = CanRemove(record.State),
        };
    }

    private static bool CanPause(TorrentState state) => state is TorrentState.Downloading or TorrentState.Seeding or TorrentState.Queued or TorrentState.ResolvingMetadata;

    private static bool CanResume(TorrentState state) => state is TorrentState.Paused or TorrentState.Error;

    private static bool CanRemove(TorrentState state) => state is not TorrentState.Removed;

    private static string SanitizePathSegment(string value)
    {
        var invalidCharacters = Path.GetInvalidFileNameChars();
        var sanitized = new string(value.Select(character => invalidCharacters.Contains(character) ? '_' : character).ToArray()).Trim();
        return string.IsNullOrWhiteSpace(sanitized) ? "torrent" : sanitized;
    }

    private sealed record MagnetMetadata(string InfoHash, string DisplayName);

    private sealed class TorrentRecord
    {
        public required Guid TorrentId { get; init; }
        public required string Name { get; set; }
        public required string MagnetUri { get; init; }
        public string? InfoHash { get; init; }
        public required string SavePath { get; init; }
        public required TorrentState State { get; set; }
        public required double ProgressPercent { get; init; }
        public required long DownloadedBytes { get; init; }
        public required long? TotalBytes { get; init; }
        public required long DownloadRateBytesPerSecond { get; set; }
        public required long UploadRateBytesPerSecond { get; set; }
        public required DateTimeOffset AddedAtUtc { get; init; }
        public DateTimeOffset? CompletedAtUtc { get; init; }
        public DateTimeOffset? LastActivityAtUtc { get; set; }
        public string? ErrorMessage { get; init; }
    }
}
