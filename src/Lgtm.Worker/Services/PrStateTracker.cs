using System.Text.Json;
using System.Text.Json.Serialization;
using Lgtm.Worker.Models;
using Microsoft.Extensions.Options;

namespace Lgtm.Worker.Services;

/// <summary>
/// Tracks PR state using a JSON file to determine when processing is needed.
/// </summary>
public class PrStateTracker : IPrStateTracker
{
    private const int CurrentVersion = 1;
    private const string StateFileName = "state.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    private readonly string _stateFilePath;
    private PrStateStore _store;

    public PrStateTracker(IOptions<WorkerOptions> options)
    {
        var workspaceDir = PathUtilities.ExpandPath(options.Value.WorkspaceDirectory);
        _stateFilePath = Path.Combine(workspaceDir, StateFileName);
        _store = new PrStateStore(CurrentVersion, new Dictionary<string, ProcessedPrState>(), DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public async Task LoadStateAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_stateFilePath))
        {
            Console.WriteLine("No existing state file found, starting fresh");
            _store = new PrStateStore(CurrentVersion, new Dictionary<string, ProcessedPrState>(), DateTimeOffset.UtcNow);
            return;
        }

        try
        {
            var json = await File.ReadAllTextAsync(_stateFilePath, cancellationToken);
            var loaded = JsonSerializer.Deserialize<PrStateStore>(json, JsonOptions);

            if (loaded is null)
            {
                Console.WriteLine("Warning: State file was empty, starting fresh");
                _store = new PrStateStore(CurrentVersion, new Dictionary<string, ProcessedPrState>(), DateTimeOffset.UtcNow);
                return;
            }

            _store = loaded with { States = loaded.States ?? new Dictionary<string, ProcessedPrState>() };
            Console.WriteLine($"Loaded state for {_store.States.Count} PR(s)");
        }
        catch (JsonException ex)
        {
            Console.WriteLine($"Warning: Failed to parse state file ({ex.Message}), starting fresh");
            _store = new PrStateStore(CurrentVersion, new Dictionary<string, ProcessedPrState>(), DateTimeOffset.UtcNow);
        }
    }

    /// <inheritdoc/>
    public async Task SaveStateAsync(CancellationToken cancellationToken)
    {
        var directory = Path.GetDirectoryName(_stateFilePath);
        if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _store = _store with { LastUpdatedAt = DateTimeOffset.UtcNow };
        var json = JsonSerializer.Serialize(_store, JsonOptions);
        await File.WriteAllTextAsync(_stateFilePath, json, cancellationToken);
    }

    /// <inheritdoc/>
    public PrStateFingerprint GetCurrentFingerprint(string prUrl, PrStatus status, long? latestCommentId)
    {
        return new PrStateFingerprint(
            status.HeadCommitSha,
            status.Mergeable,
            status.IsDraft,
            latestCommentId,
            DateTimeOffset.UtcNow);
    }

    /// <inheritdoc/>
    public bool ShouldResolveConflicts(string prUrl, PrStateFingerprint currentFingerprint)
    {
        // Only resolve if currently conflicting
        if (currentFingerprint.MergeableState != "CONFLICTING")
            return false;

        // Check if we have previous state for this PR
        if (!_store.States.TryGetValue(prUrl, out var previousState))
            return true; // First time seeing this PR

        var lastConflictState = previousState.LastConflictResolutionState;
        if (lastConflictState is null)
            return true; // Never resolved conflicts before

        // Resolve if the head SHA changed (new commits) since last conflict resolution
        if (lastConflictState.HeadCommitSha != currentFingerprint.HeadCommitSha)
            return true;

        // Resolve if we weren't conflicting before (base branch updated)
        if (lastConflictState.MergeableState != "CONFLICTING")
            return true;

        // Same conflict state as before, skip
        Console.WriteLine("Conflicts already resolved for this state, skipping");
        return false;
    }

    /// <inheritdoc/>
    public bool ShouldAddressReviews(string prUrl, long? latestCommentId)
    {
        // No comments to address
        if (latestCommentId is null)
            return false;

        // Check if we have previous state for this PR
        if (!_store.States.TryGetValue(prUrl, out var previousState))
            return true; // First time seeing this PR

        var lastAddressed = previousState.LastAddressedCommentId;
        if (lastAddressed is null)
            return true; // Never addressed comments before

        // Address if there are newer comments
        if (latestCommentId > lastAddressed)
            return true;

        Console.WriteLine("All comments already addressed, skipping");
        return false;
    }

    /// <inheritdoc/>
    public long? GetLastAddressedCommentId(string prUrl)
    {
        if (_store.States.TryGetValue(prUrl, out var state))
            return state.LastAddressedCommentId;

        return null;
    }

    /// <inheritdoc/>
    public void RecordConflictResolution(string prUrl, PrStateFingerprint fingerprint)
    {
        var existing = _store.States.GetValueOrDefault(prUrl);
        var updated = new ProcessedPrState(
            prUrl,
            fingerprint,
            existing?.LastReviewResolutionState,
            existing?.LastAddressedCommentId,
            DateTimeOffset.UtcNow,
            existing?.SeenAsMergedAt);

        _store.States[prUrl] = updated;
    }

    /// <inheritdoc/>
    public void RecordReviewResolution(string prUrl, PrStateFingerprint fingerprint, long lastAddressedCommentId)
    {
        var existing = _store.States.GetValueOrDefault(prUrl);
        var updated = new ProcessedPrState(
            prUrl,
            existing?.LastConflictResolutionState,
            fingerprint,
            lastAddressedCommentId,
            DateTimeOffset.UtcNow,
            existing?.SeenAsMergedAt);

        _store.States[prUrl] = updated;
    }

    /// <inheritdoc/>
    public bool IsFirstSeenAsMerged(string prUrl)
    {
        if (_store.States.TryGetValue(prUrl, out var state))
            return state.SeenAsMergedAt is null;

        return true;
    }

    /// <inheritdoc/>
    public void RecordMerge(string prUrl)
    {
        var existing = _store.States.GetValueOrDefault(prUrl);
        var updated = new ProcessedPrState(
            prUrl,
            existing?.LastConflictResolutionState,
            existing?.LastReviewResolutionState,
            existing?.LastAddressedCommentId,
            existing?.LastProcessedAt,
            DateTimeOffset.UtcNow);

        _store.States[prUrl] = updated;
    }
}
