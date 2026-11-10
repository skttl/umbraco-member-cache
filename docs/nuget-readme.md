# Umbraco Community Member Cache

Published member lookups with ModelsBuilder support, automatic Member Picker integration, bounded local models and HybridCache snapshots.

## Install

| Package | Umbraco dependency | Framework | Branch |
| --- | --- | --- | --- |
| 18.x | `[18.0.0,19.0.0)` | .NET 10 | `main` |
| 17.x | `[17.0.0,18.0.0)` | .NET 10 | `v17/main` |

```sh
dotnet add package Umbraco.Community.MemberCache --version 17.0.0
```

Umbraco discovers the package composer automatically. No manual registration is needed. Existing Member Picker template calls use the shared cache:

```csharp
var member = Model.Value<IPublishedContent>("member");
var title = member?.Value<string>("profileTitle");
```

The converter preserves custom converters and core fallback behavior. It uses `PropertyCacheLevel.None` so an owning content item cannot retain a member after member-cache invalidation. Delivery API member expansion is not added.

## Direct lookups

Inject `Umbraco.Community.MemberCache.IPublishedMemberLookup` into your service or view:

```csharp
public sealed class MemberProfiles(IPublishedMemberLookup members)
{
    public Task<IPublishedContent?> FindAsync(int id, CancellationToken cancellationToken)
        => members.GetByIdAsync(id, cancellationToken);
}
```

The namespace for `IPublishedContent` is `Umbraco.Cms.Core.Models.PublishedContent`.

The four asynchronous methods are `GetByIdAsync`, `GetByUsernameAsync`, `GetByIdsAsync` and `GetByUsernamesAsync`. Singles return `null` for missing members. Batches preserve input order and duplicate matches, omit missing members and return an empty list for empty input. Invalid numeric IDs miss; null inputs and blank usernames are rejected. Username matching follows `IMemberService.GetByUsername` semantics.

Returned members use Umbraco's public published-member and model factories. Generated ModelsBuilder types and property converters, including Block List, remain available. Only member snapshots are serialized. Password hashes and finished published models are never stored in distributed cache.

## Configuration

All settings are optional. These are the defaults:

```json
{
  "Umbraco": {
    "Community": {
      "MemberCache": {
        "MaxModels": 10000,
        "LocalCacheExpiration": "1.00:00:00",
        "CacheExpiration": "365.00:00:00",
        "Seed": {
          "Enabled": false,
          "MemberTypes": [],
          "BatchSize": 100
        }
      }
    }
  }
}
```

`MaxModels` limits the number of published models per process, not their memory in bytes. Eviction recreates models from snapshots on later access. `LocalCacheExpiration` also bounds the local published models' lifetime. `CacheExpiration` applies to distributed snapshots. Set positive counts and durations, with `LocalCacheExpiration` no greater than `CacheExpiration`. These defaults still need workload-specific memory and performance measurements.

The package registers its appsettings schema through Umbraco's build targets. Rebuild the consuming web project to copy the schema and add it to `appsettings-schema.json`.

## Seeding

Set `Seed.Enabled` to `true` and put member type aliases in `Seed.MemberTypes` to warm raw snapshots. Seeding does not warm finished models. It runs before startup completes, and provider failures or cancellation prevent a successful startup. Disabled seeding does not resolve providers.

Register custom scoped providers with dependency injection:

```csharp
builder.Services.AddScoped<IMemberSeedProvider, FeaturedMemberSeedProvider>();
```

Implement `IAsyncEnumerable<int> GetMemberIdsAsync(CancellationToken cancellationToken)` on `FeaturedMemberSeedProvider`. The package deduplicates IDs from all providers and loads them in batches of `Seed.BatchSize`. Custom providers can use scoped application services.

## Multiple servers

Models stay local. Snapshots use the application's HybridCache and its configured `IDistributedCache` backend. Configure your shared cache using the backend's own integration; the package does not install a Redis client or create a cache connection. Without a distributed backend, HybridCache keeps snapshots locally.

Keep Umbraco's normal load balancing and distributed cache refresh messaging configured. Member save, rename and delete notifications invalidate lookups on each server. Schema, ModelsBuilder and referenced content/media changes retire converted models. There is a propagation window until a server processes the refresher; invalidation prevents an older in-flight load from putting an accessible stale entry back afterward.

Cache keys include the installation ID and environment name. Use distinct installation identities for independent sites sharing a backend. Older snapshot generations expire using the configured TTL; invalidation generation markers are retained.

Members can contain personal data, including username, email and custom properties. Use the same access restrictions and protection for the distributed cache as for the site's database. Cache data is a published read projection; Identity, login, authorization and member editing continue through Umbraco's normal services.

## Source and verification

[Repository and demo](https://github.com/skttl/umbraco-member-cache) · [Verification record](https://github.com/skttl/umbraco-member-cache/blob/main/docs/verification.md) · [Report an issue](https://github.com/skttl/umbraco-member-cache/issues)

Benchmark images document an older prototype, not the finished package. Package performance must be remeasured before making equivalent claims.
