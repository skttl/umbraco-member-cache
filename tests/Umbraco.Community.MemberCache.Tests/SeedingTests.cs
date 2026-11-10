using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Notifications;
using Xunit;

namespace Umbraco.Community.MemberCache.Tests;

public class SeedingTests
{
    [Theory]
    [InlineData(false, RuntimeLevel.Run)]
    [InlineData(true, RuntimeLevel.Install)]
    [InlineData(true, RuntimeLevel.Upgrade)]
    [InlineData(true, RuntimeLevel.BootFailed)]
    public async Task Disabled_or_unsuitable_runtime_does_not_resolve_providers(bool enabled, RuntimeLevel level)
    {
        var services = new ServiceCollection();
        services.AddScoped<IMemberSeedProvider>(_ => throw new Exception("Must not resolve"));
        using var provider = services.BuildServiceProvider();
        var seeder = new RecordingSeeder();
        await Startup(provider, seeder, enabled).HandleAsync(new(level, false), default);
        Assert.Empty(seeder.Batches);
    }

    [Fact]
    public async Task Providers_are_scoped_disposed_deduplicated_and_batched()
    {
        var services = new ServiceCollection();
        TestProvider? first = null;
        services.AddScoped<IMemberSeedProvider>(_ => first = new TestProvider([1, 2, 1]));
        services.AddScoped<IMemberSeedProvider>(_ => new TestProvider([2, 3, 4]));
        using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        var seeder = new RecordingSeeder();
        await Startup(provider, seeder, true, 3).HandleAsync(new(RuntimeLevel.Run, false), default);
        Assert.Equal(2, seeder.Batches.Count);
        Assert.Equal([1, 2, 3], seeder.Batches[0]);
        Assert.Equal([4], seeder.Batches[1]);
        Assert.True(first!.Disposed);
    }

    [Fact]
    public async Task Provider_errors_stop_startup()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMemberSeedProvider>(_ => new TestProvider([], fail: true));
        using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() => Startup(provider, new RecordingSeeder(), true)
            .HandleAsync(new(RuntimeLevel.Run, false), default));
    }

    [Fact]
    public async Task Raw_seed_errors_stop_startup()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMemberSeedProvider>(_ => new TestProvider([1]));
        using var provider = services.BuildServiceProvider();
        var seeder = new RecordingSeeder { Seed = _ => Task.FromException(new InvalidOperationException("cache failure")) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Startup(provider, seeder, true)
            .HandleAsync(new(RuntimeLevel.Run, false), default));
    }

    [Fact]
    public async Task Cancellation_stops_startup_and_reaches_providers_and_seeder()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMemberSeedProvider>(_ => new TestProvider([1, 2]));
        using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var seeder = new RecordingSeeder { Seed = token => { Assert.Equal(cancellation.Token, token); cancellation.Cancel(); return Task.CompletedTask; } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Startup(provider, seeder, true, 1)
            .HandleAsync(new(RuntimeLevel.Run, false), cancellation.Token));
        Assert.Single(seeder.Batches);
    }

    [Fact]
    public async Task Cancellation_during_final_partial_batch_prevents_successful_startup()
    {
        var services = new ServiceCollection();
        services.AddScoped<IMemberSeedProvider>(_ => new TestProvider([1]));
        using var provider = services.BuildServiceProvider();
        using var cancellation = new CancellationTokenSource();
        var seeder = new RecordingSeeder { Seed = _ => { cancellation.Cancel(); return Task.CompletedTask; } };
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Startup(provider, seeder, true)
            .HandleAsync(new(RuntimeLevel.Run, false), cancellation.Token));
    }

    private static MemberCacheStartup Startup(ServiceProvider provider, IRawMemberSeeder seeder, bool enabled, int batchSize = 100) =>
        new(provider.GetRequiredService<IServiceScopeFactory>(), seeder,
            Options.Create(new MemberCacheOptions { Seed = new() { Enabled = enabled, BatchSize = batchSize } }),
            NullLogger<MemberCacheStartup>.Instance);

    private sealed class TestProvider(int[] ids, bool fail = false) : IMemberSeedProvider, IDisposable
    {
        public bool Disposed { get; private set; }
        public async IAsyncEnumerable<int> GetMemberIdsAsync([EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            await Task.CompletedTask;
            if (fail) throw new InvalidOperationException("provider failure");
            foreach (int id in ids) { cancellationToken.ThrowIfCancellationRequested(); yield return id; }
        }
        public void Dispose() => Disposed = true;
    }

    private sealed class RecordingSeeder : IRawMemberSeeder
    {
        public List<int[]> Batches { get; } = [];
        public Func<CancellationToken, Task>? Seed { get; init; }
        public Task SeedAsync(IEnumerable<int> ids, CancellationToken cancellationToken = default)
        {
            Batches.Add(ids.ToArray());
            return Seed?.Invoke(cancellationToken) ?? Task.CompletedTask;
        }
    }
}
