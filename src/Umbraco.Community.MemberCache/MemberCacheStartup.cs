using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Umbraco.Cms.Core;
using Umbraco.Cms.Core.Events;
using Umbraco.Cms.Core.Notifications;

namespace Umbraco.Community.MemberCache;

internal sealed class MemberCacheStartup(
    IServiceScopeFactory scopeFactory,
    IRawMemberSeeder seeder,
    IOptions<MemberCacheOptions> options,
    ILogger<MemberCacheStartup> logger) : INotificationAsyncHandler<UmbracoApplicationStartingNotification>
{
    public async Task HandleAsync(UmbracoApplicationStartingNotification notification, CancellationToken cancellationToken)
    {
        MemberCacheSeedOptions settings = options.Value.Seed;
        if (!settings.Enabled || notification.RuntimeLevel != RuntimeLevel.Run) return;

        try
        {
            cancellationToken.ThrowIfCancellationRequested();
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            var seen = new HashSet<int>();
            var batch = new List<int>(settings.BatchSize);
            foreach (IMemberSeedProvider provider in scope.ServiceProvider.GetServices<IMemberSeedProvider>())
            {
                await foreach (int id in provider.GetMemberIdsAsync(cancellationToken).WithCancellation(cancellationToken).ConfigureAwait(false))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (id <= 0 || !seen.Add(id)) continue;
                    batch.Add(id);
                    if (batch.Count < settings.BatchSize) continue;
                    await seeder.SeedAsync(batch, cancellationToken).ConfigureAwait(false);
                    batch.Clear();
                }
            }

            cancellationToken.ThrowIfCancellationRequested();
            if (batch.Count > 0) await seeder.SeedAsync(batch, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
        }
        catch (Exception exception)
        {
            logger.LogError(exception, "Member cache seeding failed; application startup cannot complete.");
            throw;
        }
    }
}
