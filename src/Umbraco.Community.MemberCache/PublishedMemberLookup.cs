using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.MemberCache;

internal sealed class PublishedMemberLookup : IPublishedMemberLookup, IRawMemberSeeder
{
    private readonly HybridCache _cache;
    private readonly IMemberCacheSource _source;
    private readonly IDistributedCache? _distributed;
    private readonly MemberCacheOptions _options;
    private readonly HybridCacheEntryOptions _entryOptions;
    private readonly HybridCacheEntryOptions _readOptions;
    private readonly TimeProvider _clock;
    private readonly string _prefix;
    private readonly object _sync = new();
    private readonly SemaphoreSlim _invalidations = new(1, 1);
    private readonly Dictionary<int, LinkedListNode<ModelEntry>> _models = [];
    private readonly LinkedList<ModelEntry> _recency = [];
    private Task _invalidation = Task.CompletedTask;
    private long _epoch;
    private string _localGeneration = NewGeneration();

    public PublishedMemberLookup(HybridCache cache, IMemberCacheSource source, IOptions<MemberCacheOptions> options,
        IOptions<GlobalSettings> globalSettings, IHostEnvironment environment,
        IDistributedCache? distributedCache = null, TimeProvider? timeProvider = null)
    {
        _cache = cache;
        _source = source;
        _distributed = distributedCache;
        _options = options.Value;
        var validation = new MemberCacheOptionsValidator().Validate(null, _options);
        if (validation.Failed) throw new OptionsValidationException(MemberCacheOptions.SectionName, typeof(MemberCacheOptions), validation.Failures);
        _clock = timeProvider ?? TimeProvider.System;
        _entryOptions = new HybridCacheEntryOptions
        {
            Expiration = _options.CacheExpiration, LocalCacheExpiration = _options.LocalCacheExpiration
        };
        _readOptions = new HybridCacheEntryOptions
        {
            Expiration = _options.CacheExpiration, LocalCacheExpiration = _options.LocalCacheExpiration,
            Flags = HybridCacheEntryFlags.DisableUnderlyingData
        };
        var installation = string.IsNullOrWhiteSpace(globalSettings.Value.Id)
            ? environment.ContentRootPath : globalSettings.Value.Id;
        _prefix = "Umbraco.Community.MemberCache:v1:" + Hash(installation + "\0" + environment.EnvironmentName);
    }

    internal int ModelCount { get { lock (_sync) return _models.Count; } }

    public async Task<IPublishedContent?> GetByIdAsync(int id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (id <= 0) return null;
        while (true)
        {
            var epoch = await BeginReadAsync(cancellationToken).ConfigureAwait(false);
            var hit = FindModel(id);
            if (hit is not null)
            {
                var currentType = _source.GetModelType(hit.Alias);
                lock (_sync)
                {
                    if (epoch != _epoch) continue;
                    if (_models.TryGetValue(id, out var node) && ReferenceEquals(node.Value, hit))
                    {
                        if (currentType == hit.ModelType) return hit.Model;
                        RemoveModel(id);
                    }
                }
            }

            var snapshot = await GetSnapshotAsync(id, cancellationToken).ConfigureAwait(false);
            if (snapshot is null)
            {
                lock (_sync) { if (epoch == _epoch) return null; }
                continue;
            }
            var type = _source.GetModelType(snapshot.TypeAlias);
            var model = await _source.PublishAsync(snapshot, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (_source.GetModelType(snapshot.TypeAlias) != type) continue;
            lock (_sync)
            {
                if (epoch != _epoch) continue;
                if (model is null) return null;
                if (_models.TryGetValue(id, out var existing) && existing.Value.ModelType == type
                    && existing.Value.Expires > _clock.GetUtcNow()) return existing.Value.Model;
                RemoveModel(id);
                var entry = new ModelEntry(id, snapshot.TypeAlias, type, model, _clock.GetUtcNow() + _options.LocalCacheExpiration);
                _models.Add(id, _recency.AddFirst(entry));
                while (_models.Count > _options.MaxModels) RemoveModel(_recency.Last!.Value.Id);
                return model;
            }
        }
    }

    public async Task<IPublishedContent?> GetByUsernameAsync(string username, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(username);
        cancellationToken.ThrowIfCancellationRequested();
        while (true)
        {
            var epoch = await BeginReadAsync(cancellationToken).ConfigureAwait(false);
            var all = await ReadGenerationAsync("all", cancellationToken).ConfigureAwait(false);
            var users = await ReadGenerationAsync("usernames", cancellationToken).ConfigureAwait(false);
            int? id;
            try
            {
                // Exact input is intentional: only IMemberService knows the database's username comparison semantics.
                id = await _cache.GetOrCreateAsync<int>($"{_prefix}:u:{all}:{users}:{Hash(username)}", async token =>
                    await _source.FindIdAsync(username, token).ConfigureAwait(false) ?? throw new MemberNotFoundException(),
                    _entryOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
            }
            catch (MemberNotFoundException) { id = null; }
            var member = id is null ? null : await GetByIdAsync(id.Value, cancellationToken).ConfigureAwait(false);
            lock (_sync) { if (epoch == _epoch) return member; }
        }
    }

    public async Task<IReadOnlyList<IPublishedContent>> GetByIdsAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        cancellationToken.ThrowIfCancellationRequested();
        var input = ids.ToArray();
        while (true)
        {
            var epoch = await BeginReadAsync(cancellationToken).ConfigureAwait(false);
            var missing = new HashSet<int>();
            foreach (var batch in input.Where(id => id > 0).Distinct().Chunk(100))
                missing.UnionWith(await WarmRawBatchAsync(batch, true, cancellationToken).ConfigureAwait(false));
            var results = new List<IPublishedContent>();
            var found = new Dictionary<int, IPublishedContent?>();
            foreach (var id in input)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (missing.Contains(id)) continue;
                if (!found.TryGetValue(id, out var member)) found[id] = member = await GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
                if (member is not null) results.Add(member);
            }
            lock (_sync) { if (epoch == _epoch) return results; }
        }
    }

    public async Task<IReadOnlyList<IPublishedContent>> GetByUsernamesAsync(IEnumerable<string> usernames, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(usernames);
        cancellationToken.ThrowIfCancellationRequested();
        var input = usernames.ToArray();
        foreach (var username in input) ArgumentException.ThrowIfNullOrWhiteSpace(username);
        while (true)
        {
            var epoch = await BeginReadAsync(cancellationToken).ConfigureAwait(false);
            var results = new List<IPublishedContent>();
            var found = new Dictionary<string, IPublishedContent?>(StringComparer.Ordinal);
            foreach (var username in input)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!found.TryGetValue(username, out var member))
                    found[username] = member = await GetByUsernameAsync(username, cancellationToken).ConfigureAwait(false);
                if (member is not null) results.Add(member);
            }
            lock (_sync) { if (epoch == _epoch) return results; }
        }
    }

    public async Task SeedAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(ids);
        cancellationToken.ThrowIfCancellationRequested();
        foreach (var batch in ids.Where(id => id > 0).Distinct().Chunk(100))
        {
            while (true)
            {
                var epoch = await BeginReadAsync(cancellationToken).ConfigureAwait(false);
                await WarmRawBatchAsync(batch, false, cancellationToken).ConfigureAwait(false);
                lock (_sync) { if (epoch == _epoch) break; }
            }
        }
    }

    internal void ClearModels()
    {
        lock (_sync)
        {
            ++_epoch;
            _models.Clear();
            _recency.Clear();
        }
    }

    internal async Task InvalidateAsync(int[]? ids, CancellationToken cancellationToken)
    {
        if (ids is { Length: 0 }) return;
        await _invalidations.WaitAsync(cancellationToken).ConfigureAwait(false);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            _invalidation = completed.Task;
            ++_epoch;
            if (ids is null) { _models.Clear(); _recency.Clear(); }
            else foreach (var id in ids) RemoveModel(id);
        }
        try
        {
            var oldKeys = new List<string>();
            if (ids is not null)
                foreach (var id in ids) oldKeys.Add(await GetSnapshotKeyAsync(id, cancellationToken).ConfigureAwait(false));
            if (_distributed is not null)
            {
                if (ids is null) await RotateGenerationAsync("all", cancellationToken).ConfigureAwait(false);
                else
                {
                    foreach (var id in ids) await RotateGenerationAsync("member:" + id, cancellationToken).ConfigureAwait(false);
                    await RotateGenerationAsync("usernames", cancellationToken).ConfigureAwait(false);
                }
            }
            lock (_sync) _localGeneration = NewGeneration();
            foreach (var key in oldKeys) await _cache.RemoveAsync(key, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            lock (_sync) ++_epoch;
            completed.SetResult();
            _invalidations.Release();
        }
    }

    private ModelEntry? FindModel(int id)
    {
        lock (_sync)
        {
            if (!_models.TryGetValue(id, out var node)) return null;
            if (node.Value.Expires <= _clock.GetUtcNow()) { RemoveModel(id); return null; }
            _recency.Remove(node);
            _recency.AddFirst(node);
            return node.Value;
        }
    }

    private void RemoveModel(int id)
    {
        if (_models.Remove(id, out var node)) _recency.Remove(node);
    }

    private async ValueTask<long> BeginReadAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Task pending;
            lock (_sync)
            {
                pending = _invalidation;
                if (pending.IsCompleted) return _epoch;
            }
            await pending.WaitAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task<MemberSnapshot?> GetSnapshotAsync(int id, CancellationToken cancellationToken)
    {
        var key = await GetSnapshotKeyAsync(id, cancellationToken).ConfigureAwait(false);
        try
        {
            return await _cache.GetOrCreateAsync<MemberSnapshot>(key, async token =>
                await _source.LoadAsync(id, token).ConfigureAwait(false) ?? throw new MemberNotFoundException(),
                _entryOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        catch (MemberNotFoundException) { return null; }
    }

    private async Task<HashSet<int>> WarmRawBatchAsync(int[] ids, bool skipModels, CancellationToken cancellationToken)
    {
        var missing = new Dictionary<int, string>();
        foreach (var id in ids)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (skipModels && FindModel(id) is not null) continue;
            var key = await GetSnapshotKeyAsync(id, cancellationToken).ConfigureAwait(false);
            var cached = await _cache.GetOrCreateAsync<MemberSnapshot?>(key,
                _ => throw new InvalidOperationException("A cache-only read must not invoke its factory."),
                _readOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
            if (cached is null) missing.Add(id, key);
        }
        if (missing.Count == 0) return [];
        var snapshots = await _source.LoadManyAsync(missing.Keys.ToArray(), cancellationToken).ConfigureAwait(false);
        foreach (var snapshot in snapshots)
        {
            if (!missing.Remove(snapshot.Id, out var key)) continue;
            // The key was fenced before reading the source; invalidation can only make this write unreachable.
            await _cache.SetAsync(key, snapshot, _entryOptions, cancellationToken: cancellationToken).ConfigureAwait(false);
        }
        return missing.Keys.ToHashSet();
    }

    private async ValueTask<string> GetSnapshotKeyAsync(int id, CancellationToken cancellationToken)
    {
        var all = await ReadGenerationAsync("all", cancellationToken).ConfigureAwait(false);
        var member = await ReadGenerationAsync("member:" + id, cancellationToken).ConfigureAwait(false);
        return $"{_prefix}:m:{all}:{id}:{member}";
    }

    private async ValueTask<string> ReadGenerationAsync(string name, CancellationToken cancellationToken)
    {
        if (_distributed is null) { lock (_sync) return _localGeneration; }
        var key = _prefix + ":generation:" + name;
        var generation = await _distributed.GetStringAsync(key, cancellationToken).ConfigureAwait(false);
        if (generation is not null) return generation;
        // A random replacement also prevents resurrection when the backend evicts a generation marker.
        generation = NewGeneration();
        await _distributed.SetStringAsync(key, generation, new DistributedCacheEntryOptions(), cancellationToken).ConfigureAwait(false);
        return generation;
    }

    private Task RotateGenerationAsync(string name, CancellationToken cancellationToken) =>
        _distributed!.SetStringAsync(_prefix + ":generation:" + name, NewGeneration(), new DistributedCacheEntryOptions(), cancellationToken);

    private static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));
    private static string NewGeneration() => Guid.NewGuid().ToString("N");
    private sealed class MemberNotFoundException : Exception;
    private sealed record ModelEntry(int Id, string Alias, Type ModelType, IPublishedContent Model, DateTimeOffset Expires);
}
