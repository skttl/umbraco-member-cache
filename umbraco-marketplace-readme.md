# Member Cache

Cache published Umbraco members and reuse their ModelsBuilder models. Member Picker uses the cache automatically, and `IPublishedMemberLookup` provides asynchronous lookups by ID and username.

The package keeps a bounded cache of finished models on each server and serializable snapshots in HybridCache. It responds to Umbraco's distributed refresh notifications and can seed selected member types or custom member IDs before startup completes.

Use package 18.x with Umbraco 18, or 17.x with Umbraco 17. Both require .NET 10.

[Installation, configuration and examples](https://github.com/skttl/umbraco-member-cache/blob/main/docs/nuget-readme.md) · [Source and local demo](https://github.com/skttl/umbraco-member-cache) · [Verification record](https://github.com/skttl/umbraco-member-cache/blob/main/docs/verification.md)

Benchmark images in this repository describe an older prototype. The finished package must be measured separately before those figures can be used as package performance claims.
