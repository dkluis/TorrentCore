using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using MudBlazor;
using TorrentCore.Contracts.Categories;
using TorrentCore.Contracts.Host;
using TorrentCore.Contracts.Maintenance;
using TorrentCore.WebUI2.Client.Services;

namespace TorrentCore.WebUI2.Client.Pages;

public partial class ServiceSettings : IDisposable
{
    [Inject] private BrowserSession Session { get; set; } = null!;
    [Inject] private NavigationManager Navigation { get; set; } = null!;
    [Inject] private IDialogService Dialogs { get; set; } = null!;
    [Inject] private ISnackbar Snackbar { get; set; } = null!;

    private enum Group { Downloads, SeedingCleanup, MetadataRecovery, Engine, VpnEgress, Diagnostics, CompletionCallback, Categories, Cleanup }
    private static readonly Group[] Groups = Enum.GetValues<Group>();
    private static string Title(Group group) => group switch
    {
        Group.Downloads => "Downloads", Group.SeedingCleanup => "Seeding & Cleanup",
        Group.MetadataRecovery => "Metadata Recovery", Group.Engine => "Engine",
        Group.VpnEgress => "VPN Egress", Group.Diagnostics => "Diagnostics",
        Group.CompletionCallback => "Completion Callback", Group.Categories => "Categories",
        _ => "Cleanup"
    };

    private Group selectedGroup = Group.Downloads;
    private bool allowNavigation;
    private RuntimeSettingsDto? snapshot;
    private RuntimeSettingsDraft? draft;
    private List<TorrentCategoryDto> categories = [];
    private Dictionary<string, CategoryDraft> categoryDrafts = [];
    private bool loading;
    private bool busy;
    private DateTime logDate = DateTime.Today.AddDays(-7);
    private DateTime historyDate = DateTime.Today.AddDays(-30);
    private string? message;
    private string? error;
    private long loadedRevision;
    private int loadGeneration;
    private bool disposed;
    private CancellationTokenSource targetCancellation = new();

    private enum CleanupKind { Logs, History, Orphans }
    private sealed class CategoryDraft
    {
        public string DisplayName { get; set; } = "";
        public string DownloadRootPath { get; set; } = "";
        public string CallbackLabel { get; set; } = "";
        public bool Enabled { get; set; }
        public bool InvokeCompletionCallback { get; set; }
        public int SortOrder { get; set; }
        public static CategoryDraft From(TorrentCategoryDto x) => new()
        {
            DisplayName = x.DisplayName, DownloadRootPath = x.DownloadRootPath,
            CallbackLabel = x.CallbackLabel, Enabled = x.Enabled,
            InvokeCompletionCallback = x.InvokeCompletionCallback, SortOrder = x.SortOrder
        };
        public UpdateTorrentCategoryRequest ToRequest() => new()
        {
            DisplayName = DisplayName, DownloadRootPath = DownloadRootPath,
            CallbackLabel = CallbackLabel, Enabled = Enabled,
            InvokeCompletionCallback = InvokeCompletionCallback, SortOrder = SortOrder
        };
    }

    protected override async Task OnInitializedAsync()
    {
        if (!Session.IsInitialized) await Session.InitializeAsync();
        Session.Changed += SessionChanged;
        await LoadAsync();
    }

    private void SessionChanged() => _ = InvokeAsync(async () =>
    {
        if (disposed) return;
        if (Session.Revision != loadedRevision)
        {
            var hadUnsavedChanges = IsDirty;
            targetCancellation.Cancel();
            targetCancellation.Dispose();
            targetCancellation = new CancellationTokenSource();
            snapshot = null;
            draft = null;
            categories = [];
            categoryDrafts = [];
            busy = false;
            loading = false;
            await LoadAsync();
            if (hadUnsavedChanges) Snackbar.Add("The active connection changed. Unsaved settings for the previous connection were cleared.", Severity.Warning);
        }
        else StateHasChanged();
    });

    private async Task LoadAsync()
    {
        var generation = ++loadGeneration;
        if (disposed || Session.ActiveProfile is null) { loading = false; return; }
        var revision = Session.Revision;
        var token = targetCancellation.Token;
        loading = true; error = null; message = null;
        try
        {
            var client = Session.CreateClient(revision);
            var settings = await client.GetRuntimeSettingsAsync(token) ?? throw new InvalidOperationException("TorrentCore did not return service settings.");
            var categoryList = await client.GetCategoriesAsync(token);
            if (disposed || generation != loadGeneration || revision != Session.Revision) return;
            loadedRevision = revision;
            snapshot = settings;
            draft = RuntimeSettingsDraft.From(settings);
            categories = categoryList.ToList();
            categoryDrafts = categories.ToDictionary(x => x.Key, CategoryDraft.From);
        }
        catch (Exception exception) { if (!disposed && generation == loadGeneration && revision == Session.Revision) error = exception.Message; }
        finally { if (!disposed && generation == loadGeneration && revision == Session.Revision) loading = false; }
    }

    private async Task RequestRefreshAsync()
    {
        var revision = loadedRevision;
        if (IsDirty)
        {
            var choice = await Dialogs.ShowMessageBox("Save Service Settings Before Refreshing?", "Unsaved changes are in the current settings group.", yesText: "Save", noText: "Discard Changes", cancelText: "Cancel");
            if (disposed || revision != Session.Revision || revision != loadedRevision) return;
            if (choice == true) { if (!await SaveAsync()) return; }
            else if (choice == false) Revert();
            else return;
        }
        await LoadAsync();
    }

    private bool IsDirty
    {
        get
        {
            if (selectedGroup == Group.Cleanup) return false;
            if (selectedGroup == Group.Categories)
                return categories.Any(x => categoryDrafts.TryGetValue(x.Key, out var y)
                    && JsonSerializer.Serialize(y.ToRequest()) != JsonSerializer.Serialize(CategoryDraft.From(x).ToRequest()));
            return snapshot is not null && draft is not null
                && JsonSerializer.Serialize(draft.ToRequest()) != JsonSerializer.Serialize(RuntimeSettingsDraft.From(snapshot).ToRequest());
        }
    }

    private string? ValidationError
    {
        get
        {
            if (selectedGroup == Group.Categories)
            {
                foreach (var x in categories)
                {
                    if (!categoryDrafts.TryGetValue(x.Key, out var y)) return $"Category '{x.Key}' could not be edited.";
                    if (string.IsNullOrWhiteSpace(y.DisplayName)) return $"{x.Key}: display name is required.";
                    if (string.IsNullOrWhiteSpace(y.DownloadRootPath)) return $"{x.Key}: download root path is required.";
                    if (string.IsNullOrWhiteSpace(y.CallbackLabel)) return $"{x.Key}: callback label is required.";
                    if (y.SortOrder < 0) return $"{x.Key}: sort order must be 0 or greater.";
                }
                return null;
            }
            return draft is null ? null : RuntimeSettingsValidation.Error(draft);
        }
    }

    private async Task SelectGroupAsync(Group group)
    {
        if (group == selectedGroup || busy || loading) return;
        var revision = loadedRevision;
        if (IsDirty)
        {
            var choice = await Dialogs.ShowMessageBox("Save Changes Before Switching Groups?", "Only one service-settings group can have unsaved changes at a time.", yesText: "Save", noText: "Discard Changes", cancelText: "Cancel");
            if (disposed || revision != Session.Revision || revision != loadedRevision) return;
            if (choice == true) { if (!await SaveAsync()) return; }
            else if (choice == false) Revert();
            else return;
        }
        FinishGroupChange(group);
    }

    private void FinishGroupChange(Group group)
    {
        selectedGroup = group;
        if (group == Group.Cleanup)
        {
            logDate = DateTime.Today.AddDays(-7);
            historyDate = DateTime.Today.AddDays(-30);
        }
        message = null;
    }

    private void Revert()
    {
        if (selectedGroup == Group.Categories)
            categoryDrafts = categories.ToDictionary(x => x.Key, CategoryDraft.From);
        else if (snapshot is not null)
            draft = RuntimeSettingsDraft.From(snapshot);
        Snackbar.Add($"Reverted unsaved {Title(selectedGroup)} changes.", Severity.Info);
    }

    private async Task<bool> SaveAsync()
    {
        if (!IsDirty) return true;
        if (ValidationError is not null || busy || Session.Revision != loadedRevision) return false;
        busy = true; error = null; message = null;
        var revision = loadedRevision;
        var token = targetCancellation.Token;
        try
        {
            var client = Session.CreateClient(revision);
            if (selectedGroup == Group.Categories)
            {
                var changed = categories.Where(x => categoryDrafts.TryGetValue(x.Key, out var y)
                    && JsonSerializer.Serialize(y.ToRequest()) != JsonSerializer.Serialize(CategoryDraft.From(x).ToRequest())).ToList();
                foreach (var x in changed)
                {
                    if (revision != Session.Revision) return false;
                    var saved = await client.UpdateCategoryAsync(x.Key, categoryDrafts[x.Key].ToRequest(), token);
                    if (disposed || revision != Session.Revision || revision != loadedRevision) return false;
                    categories[categories.FindIndex(c => c.Key == x.Key)] = saved;
                    categoryDrafts[x.Key] = CategoryDraft.From(saved);
                }
                Snackbar.Add($"Saved {changed.Count} {(changed.Count == 1 ? "category" : "categories")}.", Severity.Success);
            }
            else
            {
                var saved = await client.UpdateRuntimeSettingsAsync(draft!.ToRequest(), token);
                if (disposed || revision != Session.Revision || revision != loadedRevision) return false;
                snapshot = saved;
                draft = RuntimeSettingsDraft.From(snapshot);
                Snackbar.Add($"Saved {Title(selectedGroup)}.", Severity.Success);
            }
            return true;
        }
        catch (Exception exception) { if (!disposed && revision == Session.Revision && revision == loadedRevision) error = exception.Message; return false; }
        finally { if (!disposed && revision == Session.Revision && revision == loadedRevision) busy = false; }
    }

    private async Task OnBeforeNavigate(LocationChangingContext context)
    {
        if (busy) { context.PreventNavigation(); return; }
        if (allowNavigation || !IsDirty) return;
        context.PreventNavigation();
        var revision = loadedRevision;
        var choice = await Dialogs.ShowMessageBox("Save Service Settings Before Leaving?", "Unsaved changes are in the current settings group.", yesText: "Save", noText: "Discard Changes", cancelText: "Cancel");
        if (disposed || revision != Session.Revision || revision != loadedRevision) return;
        if (choice == true) { if (!await SaveAsync()) return; }
        else if (choice == false) Revert();
        else return;
        allowNavigation = true;
        Navigation.NavigateTo(context.TargetLocation);
    }

    private async Task ConfirmRestartAsync()
    {
        var revision = loadedRevision;
        if (IsDirty || busy || Session.Revision != revision) return;
        var confirmed = await Dialogs.ShowMessageBox("Restart TorrentCore Service?", "TorrentCore will be briefly unavailable. The app will wait up to about 30 seconds for it to return.", yesText: "Restart Service", cancelText: "Cancel");
        if (confirmed != true || disposed || Session.Revision != revision || loadedRevision != revision) return;
        await RestartAsync(revision);
    }

    private async Task RestartAsync(long revision)
    {
        var token = targetCancellation.Token;
        busy = true; error = null; message = null;
        try
        {
            var client = Session.CreateClient(revision);
            Guid? previousInstanceId = (await client.GetHostStatusAsync(token))?.ServiceInstanceId;
            if (disposed || revision != Session.Revision || revision != loadedRevision) return;
            await client.RequestServiceRestartAsync(token);
            if (disposed || revision != Session.Revision || revision != loadedRevision) return;
            message = "Restart requested. Waiting for TorrentCore to return…";
            await Task.Delay(TimeSpan.FromSeconds(2), token);
            for (var attempt = 0; attempt < 15; attempt++)
            {
                if (disposed || revision != Session.Revision || revision != loadedRevision) return;
                try
                {
                    var health = await client.GetHealthAsync(token);
                    var status = await client.GetHostStatusAsync(token);
                    if (disposed || revision != Session.Revision || revision != loadedRevision) return;
                    if (health is not null && status is not null && previousInstanceId is not null && status.ServiceInstanceId != previousInstanceId)
                    {
                        message = null;
                        Snackbar.Add("TorrentCore restarted and reconnected.", Severity.Success);
                        return;
                    }
                    if (health is not null && status is not null && previousInstanceId is null)
                    {
                        message = null;
                        Snackbar.Add("TorrentCore is reachable after the restart request. An instance change could not be confirmed.", Severity.Warning);
                        return;
                    }
                }
                catch (Exception) { /* Unavailability is expected during restart. */ }
                await Task.Delay(TimeSpan.FromSeconds(2), token);
            }
            message = null;
            Snackbar.Add("Restart was requested, but TorrentCore did not return within about 30 seconds. Refresh and recheck the connection.", Severity.Warning);
        }
        catch (Exception exception) { if (!disposed && revision == Session.Revision && revision == loadedRevision) error = exception.Message; }
        finally { if (!disposed && revision == Session.Revision && revision == loadedRevision) busy = false; }
    }

    private async Task ConfirmCleanupAsync(CleanupKind kind)
    {
        var revision = loadedRevision;
        if (busy || IsDirty || Session.Revision != revision) return;
        if ((kind == CleanupKind.Logs && logDate.Date > DateTime.Today) ||
            (kind == CleanupKind.History && historyDate.Date > DateTime.Today))
        {
            error = "Up To Date cannot be in the future.";
            return;
        }
        var title = kind switch { CleanupKind.Logs => "Delete Log Entries?", CleanupKind.History => "Delete History Records?", _ => "Delete Orphan Logs?" };
        var prompt = kind switch
        {
            CleanupKind.Logs => $"Permanently delete eligible log entries before {logDate:yyyy/MM/dd} at Service-local 00:00:00? Logs for live torrents are protected.",
            CleanupKind.History => $"Permanently delete eligible history records last updated before {historyDate:yyyy/MM/dd} at Service-local 00:00:00? History for live torrents is protected.",
            _ => "Permanently delete torrent-scoped logs whose Torrent ID is no longer present in the live torrent table?"
        };
        var confirmed = await Dialogs.ShowMessageBox(title, prompt, yesText: title.TrimEnd('?'), cancelText: "Cancel");
        if (confirmed != true || disposed || Session.Revision != revision || loadedRevision != revision) return;
        await CleanupAsync(kind, revision);
    }

    private async Task CleanupAsync(CleanupKind kind, long revision)
    {
        var token = targetCancellation.Token;
        busy = true; error = null; message = null;
        try
        {
            var client = Session.CreateClient(revision);
            if (kind == CleanupKind.Logs)
            {
                var result = await client.CleanupLogsAsync(new CleanupByDateRequest { UpToDate = DateOnly.FromDateTime(logDate) }, token);
                if (disposed || revision != Session.Revision || revision != loadedRevision) return;
                Snackbar.Add($"Deleted {result.DeletedRecordCount} log entries.", Severity.Success);
            }
            else if (kind == CleanupKind.History)
            {
                var result = await client.CleanupHistoryAsync(new CleanupByDateRequest { UpToDate = DateOnly.FromDateTime(historyDate) }, token);
                if (disposed || revision != Session.Revision || revision != loadedRevision) return;
                Snackbar.Add($"Deleted {result.DeletedRecordCount} history records.", Severity.Success);
            }
            else
            {
                var result = await client.DeleteOrphanedTorrentLogsAsync(token);
                if (disposed || revision != Session.Revision || revision != loadedRevision) return;
                Snackbar.Add($"Deleted {result.DeletedLogEntryCount} orphaned torrent log entries.", Severity.Success);
            }
        }
        catch (Exception exception) { if (!disposed && revision == Session.Revision && revision == loadedRevision) error = exception.Message; }
        finally { if (!disposed && revision == Session.Revision && revision == loadedRevision) busy = false; }
    }

    public void Dispose()
    {
        disposed = true;
        Session.Changed -= SessionChanged;
        targetCancellation.Cancel();
        targetCancellation.Dispose();
    }
}
