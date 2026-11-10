using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Caching.Hybrid;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using NSubstitute;
using Umbraco.Cms.Core.Configuration.Models;
using Umbraco.Cms.Core.Models.PublishedContent;

namespace Umbraco.Community.MemberCache.Tests;

public class CacheCoreTests
{
    [Fact]
    public async Task IdAndUsernameReturnCanonicalModelAndBatchesPreserveOrderAndDuplicates()
    {
        using var node = new Node();
        node.Source.Members[1] = Snapshot(1, "Alice");
        node.Source.Members[2] = Snapshot(2, "Bob");
        var first = await node.Lookup.GetByIdAsync(1);
        Assert.Same(first, await node.Lookup.GetByUsernameAsync("Alice"));
        var batch = await node.Lookup.GetByIdsAsync([2, -1, 1, 404, 2]);
        Assert.Equal([2, 1, 2], batch.Select(x => x.Id));
        Assert.Same(batch[0], batch[2]);
        Assert.Same(first, batch[1]);
        Assert.Empty(await node.Lookup.GetByIdsAsync([]));
        Assert.Null(await node.Lookup.GetByIdAsync(0));
    }

    [Fact]
    public async Task UsernamePreservesSourceComparisonAndRejectsBlankInputs()
    {
        using var node = new Node();
        node.Source.Members[1] = Snapshot(1, "Alice");
        node.Source.Members[2] = Snapshot(2, "alice");
        Assert.Equal(1, (await node.Lookup.GetByUsernameAsync("Alice"))!.Id);
        Assert.Equal(2, (await node.Lookup.GetByUsernameAsync("alice"))!.Id);
        Assert.Null(await node.Lookup.GetByUsernameAsync("ALICE"));
        Assert.Null(await node.Lookup.GetByUsernameAsync(" Alice "));
        await Assert.ThrowsAsync<ArgumentException>(() => node.Lookup.GetByUsernameAsync(" "));
        await Assert.ThrowsAsync<ArgumentNullException>(() => node.Lookup.GetByUsernameAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => node.Lookup.GetByIdsAsync(null!));
        await Assert.ThrowsAsync<ArgumentException>(() => node.Lookup.GetByUsernamesAsync(["Alice", " "]));
        var batch = await node.Lookup.GetByUsernamesAsync(["alice", "missing", "Alice", "alice"]);
        Assert.Equal([2, 1, 2], batch.Select(x => x.Id));
        Assert.Same(batch[0], batch[2]);
    }

    [Fact]
    public async Task LruEvictionBoundsModelsAndReconstructsFromRawCache()
    {
        using var node = new Node(maxModels: 2);
        foreach (var id in new[] { 1, 2, 3 }) node.Source.Members[id] = Snapshot(id, $"user{id}");
        var one = await node.Lookup.GetByIdAsync(1);
        var two = await node.Lookup.GetByIdAsync(2);
        Assert.Same(one, await node.Lookup.GetByIdAsync(1));
        await node.Lookup.GetByIdAsync(3);
        Assert.Equal(2, node.Lookup.ModelCount);
        Assert.Same(one, await node.Lookup.GetByIdAsync(1));
        Assert.NotSame(two, await node.Lookup.GetByIdAsync(2));
        Assert.Equal(3, node.Source.LoadCount);
        Assert.Equal(2, node.Lookup.ModelCount);
    }

    [Fact]
    public async Task MissingMembersAreNotCachedAndSourceErrorsAndCancellationPropagate()
    {
        using var node = new Node();
        Assert.Null(await node.Lookup.GetByIdAsync(1));
        node.Source.Members[1] = Snapshot(1, "new");
        Assert.NotNull(await node.Lookup.GetByIdAsync(1));
        node.Source.Load = (_, _) => throw new InvalidOperationException("database unavailable");
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => node.Lookup.GetByIdAsync(2));
        Assert.Equal("database unavailable", error.Message);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => node.Lookup.GetByIdAsync(1, cancellation.Token));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => node.Lookup.SeedAsync([1], cancellation.Token));
    }

    [Fact]
    public async Task SharedL2RehydratesOnAnotherNodeAndL0HitsDoNotContactDistributedCache()
    {
        var shared = new SharedCache();
        var source = new Source();
        source.Members[1] = Snapshot(1, "old");
        using var a = new Node(shared, source);
        using var b = new Node(shared, source);
        var original = await a.Lookup.GetByIdAsync(1);
        var remote = await b.Lookup.GetByIdAsync(1);
        Assert.NotSame(original, remote);
        Assert.Equal("old", remote!.Name);
        Assert.Equal(1, source.LoadCount);
        var reads = shared.Reads;
        Assert.Same(remote, await b.Lookup.GetByIdAsync(1));
        Assert.Equal(reads, shared.Reads);
    }

    [Fact]
    public async Task SaveRenameDeleteAndFullRefreshRetireBothTiersAcrossNodes()
    {
        var shared = new SharedCache();
        var source = new Source();
        source.Members[1] = Snapshot(1, "old");
        using var a = new Node(shared, source);
        using var b = new Node(shared, source);
        var first = await b.Lookup.GetByUsernameAsync("old");
        await a.Lookup.GetByIdAsync(1);
        source.Members[1] = source.Members[1] with { Name = "saved" };
        await a.Invalidator.InvalidateMembersAsync([1]);
        await b.Invalidator.InvalidateMembersAsync([1]);
        Assert.Equal("saved", (await b.Lookup.GetByIdAsync(1))!.Name);
        Assert.NotSame(first, await b.Lookup.GetByIdAsync(1));
        source.Members[1] = source.Members[1] with { Username = "renamed" };
        source.Members[2] = Snapshot(2, "old");
        await a.Invalidator.InvalidateMembersAsync([1, 2]);
        await b.Invalidator.InvalidateMembersAsync([1, 2]);
        Assert.Equal(2, (await b.Lookup.GetByUsernameAsync("old"))!.Id);
        Assert.Equal(1, (await b.Lookup.GetByUsernameAsync("renamed"))!.Id);
        source.Members.TryRemove(1, out _);
        await a.Invalidator.InvalidateMembersAsync([1]);
        await b.Invalidator.InvalidateMembersAsync([1]);
        Assert.Null(await b.Lookup.GetByIdAsync(1));
        Assert.Null(await b.Lookup.GetByUsernameAsync("renamed"));
        source.Members[2] = source.Members[2] with { Name = "full refresh" };
        await a.Invalidator.InvalidateAllAsync();
        await b.Invalidator.InvalidateAllAsync();
        Assert.Equal("full refresh", (await b.Lookup.GetByUsernameAsync("old"))!.Name);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OldFactoryCannotPoisonSharedL2AfterAnotherNodeProcessedInvalidation(bool full)
    {
        var shared = new SharedCache();
        var source = new Source();
        source.Members[1] = Snapshot(1, "old");
        using var a = new Node(shared, source);
        using var b = new Node(shared, source);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = 0;
        source.Load = async (id, token) =>
        {
            var result = source.Members.GetValueOrDefault(id);
            if (Interlocked.Increment(ref first) == 1)
            {
                started.SetResult();
                await release.Task.WaitAsync(token);
            }
            return result;
        };
        var staleFactory = a.Lookup.GetByIdAsync(1);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        source.Members[1] = source.Members[1] with { Name = "fresh" };
        if (full) await b.Invalidator.InvalidateAllAsync();
        else await b.Invalidator.InvalidateMembersAsync([1]);
        release.SetResult();
        await staleFactory;
        Assert.Equal("fresh", (await b.Lookup.GetByIdAsync(1))!.Name);
        Assert.Equal(2, source.LoadCount);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LocalInvalidationRetriesOverlappingLoadOrRawSeedWithoutPublishingStaleModels(bool seed)
    {
        var shared = new SharedCache();
        var source = new Source();
        source.Members[1] = Snapshot(1, "old");
        using var a = new Node(shared, source);
        using var b = new Node(shared, source);
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = 0;
        source.Load = async (id, token) =>
        {
            var result = source.Members.GetValueOrDefault(id);
            if (Interlocked.Increment(ref first) == 1)
            {
                started.SetResult();
                await release.Task.WaitAsync(token);
            }
            return result;
        };
        Task inFlight = seed ? a.Lookup.SeedAsync([1]) : a.Lookup.GetByIdAsync(1);
        await started.Task.WaitAsync(TimeSpan.FromSeconds(10));
        source.Members[1] = source.Members[1] with { Name = "fresh" };
        await a.Invalidator.InvalidateMembersAsync([1]);
        await b.Invalidator.InvalidateMembersAsync([1]);
        release.SetResult();
        await inFlight;
        if (seed)
        {
            Assert.Equal(0, a.Lookup.ModelCount);
            Assert.Equal(0, source.PublishCount);
        }
        else Assert.Equal("fresh", (await (Task<IPublishedContent?>)inFlight)!.Name);
        Assert.Equal("fresh", (await a.Lookup.GetByIdAsync(1))!.Name);
        Assert.Equal("fresh", (await b.Lookup.GetByIdAsync(1))!.Name);
    }

    [Fact]
    public async Task ModelFactoryGenerationChangeAndModelClearRetireOnlyConvertedModels()
    {
        using var node = new Node();
        node.Source.Members[1] = Snapshot(1, "person");
        var first = await node.Lookup.GetByIdAsync(1);
        node.Source.ModelType = typeof(string);
        var second = await node.Lookup.GetByIdAsync(1);
        Assert.NotSame(first, second);
        node.Invalidator.ClearModels();
        Assert.NotSame(second, await node.Lookup.GetByIdAsync(1));
        Assert.Equal(1, node.Source.LoadCount);
        Assert.Equal(3, node.Source.PublishCount);
    }

    [Fact]
    public async Task ModelExpiresAtConfiguredLocalDuration()
    {
        var clock = new Clock();
        using var node = new Node(clock: clock);
        node.Source.Members[1] = Snapshot(1, "person");
        var first = await node.Lookup.GetByIdAsync(1);
        clock.Now += TimeSpan.FromDays(1) - TimeSpan.FromTicks(1);
        Assert.Same(first, await node.Lookup.GetByIdAsync(1));
        clock.Now += TimeSpan.FromTicks(1);
        Assert.NotSame(first, await node.Lookup.GetByIdAsync(1));
        Assert.Equal(1, node.Source.LoadCount);
    }

    [Fact]
    public async Task InstallationAndEnvironmentPartitionSharedBackend()
    {
        var shared = new SharedCache();
        using var a = new Node(shared, installationId: "site-a");
        using var b = new Node(shared, installationId: "site-b");
        using var c = new Node(shared, installationId: "site-a", environmentName: "Production");
        a.Source.Members[1] = Snapshot(1, "a");
        b.Source.Members[1] = Snapshot(1, "b");
        c.Source.Members[1] = Snapshot(1, "c");
        Assert.Equal("a", (await a.Lookup.GetByIdAsync(1))!.Name);
        Assert.Equal("b", (await b.Lookup.GetByIdAsync(1))!.Name);
        Assert.Equal("c", (await c.Lookup.GetByIdAsync(1))!.Name);
    }

    [Fact]
    public async Task BatchesLoadOnlyRawMissesInBoundedGroupsAndSeedDoesNotPublishModels()
    {
        using var node = new Node();
        foreach (var id in Enumerable.Range(1, 103)) node.Source.Members[id] = Snapshot(id, "user" + id);
        await node.Lookup.GetByIdAsync(1);
        var members = await node.Lookup.GetByIdsAsync([1, 2, 3, 2, -1, 404]);
        Assert.Equal([1, 2, 3, 2], members.Select(m => m.Id));
        Assert.Single(node.Source.Batches);
        Assert.Equal([2, 3, 404], node.Source.Batches[0]);
        node.Invalidator.ClearModels();
        node.Source.Batches.Clear();
        var publications = node.Source.PublishCount;
        await node.Lookup.SeedAsync(Enumerable.Range(1, 103).Append(3));
        Assert.Equal(publications, node.Source.PublishCount);
        Assert.Equal(0, node.Lookup.ModelCount);
        Assert.All(node.Source.Batches, batch => Assert.InRange(batch.Length, 1, 100));
        Assert.Equal(Enumerable.Range(4, 100), node.Source.Batches.SelectMany(x => x));
        node.Source.Members[404] = Snapshot(404, "newly-created");
        Assert.Equal(404, (await node.Lookup.GetByIdAsync(404))!.Id);
    }

    [Fact]
    public async Task TargetedInvalidationRemovesOldRawPayloadAndLeavesOtherModelsUsable()
    {
        var shared = new SharedCache();
        using var node = new Node(shared);
        node.Source.Members[1] = Snapshot(1, "one");
        node.Source.Members[2] = Snapshot(2, "two");
        await node.Lookup.GetByIdAsync(1);
        var other = await node.Lookup.GetByIdAsync(2);
        var removals = shared.Removals;
        await node.Invalidator.InvalidateMembersAsync([1]);
        Assert.True(shared.Removals > removals);
        Assert.Same(other, await node.Lookup.GetByIdAsync(2));
    }

    internal static MemberSnapshot Snapshot(int id, string username) => new()
    {
        Id = id, Key = Guid.NewGuid(), TypeId = 7, TypeAlias = "person", Name = username, Username = username,
        Email = username + "@example.test", Values = []
    };

    internal sealed class Source : IMemberCacheSource
    {
        public ConcurrentDictionary<int, MemberSnapshot> Members { get; } = new();
        public Func<int, CancellationToken, Task<MemberSnapshot?>>? Load { get; set; }
        public int LoadCount;
        public int PublishCount;
        public List<int[]> Batches { get; } = [];
        public Type ModelType { get; set; } = typeof(IPublishedContent);
        public async Task<MemberSnapshot?> LoadAsync(int id, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref LoadCount);
            return Load is null ? Members.GetValueOrDefault(id) : await Load(id, cancellationToken);
        }
        public Task<int?> FindIdAsync(string username, CancellationToken cancellationToken) =>
            Task.FromResult(Members.Values.FirstOrDefault(x => x.Username == username)?.Id);
        public async Task<IReadOnlyList<MemberSnapshot>> LoadManyAsync(int[] ids, CancellationToken cancellationToken)
        {
            Batches.Add(ids);
            var result = new List<MemberSnapshot>();
            foreach (var id in ids)
            {
                var snapshot = await LoadAsync(id, cancellationToken);
                if (snapshot is not null) result.Add(snapshot);
            }
            return result;
        }
        public Task<IPublishedContent?> PublishAsync(MemberSnapshot snapshot, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref PublishCount);
            var content = Substitute.For<IPublishedContent>();
            content.Id.Returns(snapshot.Id);
            content.Name.Returns(snapshot.Name);
            content.ContentType.Alias.Returns("person");
            return Task.FromResult<IPublishedContent?>(content);
        }
        public Type GetModelType(string alias) => ModelType;
    }

    internal sealed class Node : IDisposable
    {
        private readonly ServiceProvider _services;
        public Source Source { get; }
        public PublishedMemberLookup Lookup { get; }
        public MemberCacheInvalidator Invalidator { get; }
        public Node(IDistributedCache? distributed = null, Source? source = null, int maxModels = 10000,
            TimeProvider? clock = null, string installationId = "test-installation", string environmentName = "Tests",
            IMemberCacheSource? actualSource = null)
        {
            Source = source ?? new Source();
            var services = new ServiceCollection();
            services.AddLogging();
            if (distributed is not null) services.AddSingleton(distributed);
            services.AddHybridCache();
            _services = services.BuildServiceProvider();
            var environment = Substitute.For<IHostEnvironment>();
            environment.EnvironmentName.Returns(environmentName);
            environment.ContentRootPath.Returns("test-root");
            Lookup = new PublishedMemberLookup(_services.GetRequiredService<HybridCache>(), actualSource ?? Source,
                Options.Create(new MemberCacheOptions { MaxModels = maxModels }),
                Options.Create(new GlobalSettings { Id = installationId }), environment, distributed, clock);
            Invalidator = new MemberCacheInvalidator(Lookup);
        }
        public void Dispose() => _services.Dispose();
    }

    internal sealed class Clock : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class SharedCache : IDistributedCache
    {
        private readonly ConcurrentDictionary<string, byte[]> _entries = new();
        public int Reads;
        public int Removals;
        public byte[]? Get(string key)
        {
            Interlocked.Increment(ref Reads);
            return _entries.TryGetValue(key, out var value) ? value.ToArray() : null;
        }
        public Task<byte[]?> GetAsync(string key, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Get(key));
        }
        public void Set(string key, byte[] value, DistributedCacheEntryOptions options) => _entries[key] = value.ToArray();
        public Task SetAsync(string key, byte[] value, DistributedCacheEntryOptions options, CancellationToken token = default)
        {
            token.ThrowIfCancellationRequested();
            Set(key, value, options);
            return Task.CompletedTask;
        }
        public void Remove(string key) { Interlocked.Increment(ref Removals); _entries.TryRemove(key, out _); }
        public Task RemoveAsync(string key, CancellationToken token = default) { Remove(key); return Task.CompletedTask; }
        public void Refresh(string key) { }
        public Task RefreshAsync(string key, CancellationToken token = default) => Task.CompletedTask;
    }
}
