# Verification record

Verified locally on Windows 11 with .NET SDK 10.0.401 and runtime 10.0.14 on 10 November 2026. The repository contains independent version branches and has not been pushed or published.

| Branch | Minimum CMS | Package interval | Release tests | Clean NuGet installation |
| --- | --- | --- | ---: | --- |
| main | 18.0.0 | [18.0.0,19.0.0) | 51 passed, 0 failed/skipped | 14 hosted checks passed |
| v17/main | 17.0.0 | [17.0.0,18.0.0) | 50 passed, 0 failed/skipped | 14 hosted checks passed |

Both solution builds and both clean consumer builds completed with zero warnings and zero errors. The only version-specific production changes are the package version/dependency interval and the Member Picker base constructor, which has no external-member service in 17. The external-member test exists only in 18; 17 retains its native null behavior for an absent member UDI.

## Build, tests and archive inspection

Executed on each branch:

```sh
dotnet restore Umbraco.Community.MemberCache.slnx
dotnet build Umbraco.Community.MemberCache.slnx -c Release --no-restore
dotnet test tests/Umbraco.Community.MemberCache.Tests -c Release --no-build --logger trx
dotnet pack src/Umbraco.Community.MemberCache -c Release --no-build -o artifacts
python scripts/verify-package.py self-test
python scripts/verify-package.py package artifacts
```

Restore during local verification used an explicit NuGet.org config and cache fallback folders, with NuGet audit disabled for the offline-cached dependencies. This does not claim a vulnerability audit. Repository NuGet.Config contains only the public feed and no credentials.

The actual nupkg inspections checked ID, version, framework, exclusive Umbraco interval, DLL, README, 128×128 PNG icon, appsettings schema and buildTransitive target. The archive contains no demo, databases, credentials or raw implementation sources. Package verifier self-tests reject invalid versions, intervals, missing files and unexpected content. Both workflow files passed actionlint; schema validation passed against the official Marketplace schema.

## Runtime proof from separate package consumers

Separate clean sites were generated with the official Umbraco.Templates 18.0.0 and 17.0.0 templates and SQLite. Each referenced only the built package, with no package project reference. The opt-in demo fixture was copied into the consumer as verification code. Composer and converter discovery therefore came from the installed package.

Observed checks in [18.0.0](install18.json) and [17.0.0](install17.json):

- PublicLookupPreservesIdentity, LookupUsesGeneratedModelsBuilderModel and CachedRawValuesKeepStorageTypes.
- CachedBlockListUsesGeneratedElementModel with actual InMemoryAuto member and element types.
- IdAndUsernameShareCanonicalModel and BatchOmitsMissingAndPreservesDuplicates.
- RealDocumentIsPublished, AutomaticPickerOnDocumentReturnsGeneratedMemberModel and AutomaticPickerReadsMemberProperty.
- MemberSaveInvalidatesLookup and AlreadyReadDocumentPickerSeesMemberSave on the same previously read document owner.
- MemberRenameInvalidatesUsernameAliases, MemberDeleteInvalidatesBothLookups and AlreadyReadDocumentPickerSeesMemberDelete.

Each installed site was restarted with seeding enabled and an unknown member type. Startup threw during BootUmbracoAsync and exited nonzero; it did not reach successful readiness. The package schema was physically copied into each consumer and referenced by appsettings-schema.json.

## Cache correctness and review

Unit tests exercise two independent HybridCache/model-cache instances over the same distributed backend, in-flight refresh races, save/rename/delete/full invalidation, exact username semantics, batch ordering and duplicates, raw-only seeding, capacity/expiry, model-factory CLR type changes, typed JSON and exclusion of identity credentials. ID L0 hits perform no distributed reads. Provider tests cover scoped services, disabled providers, type selection, paging, deduplication, failure and cancellation.

Review found and fixed a stale-source race: an unnotified server must not populate a fresh shared generation from Umbraco's old repository cache. All three raw source reads now use public None-mode repository scopes. Four regressions failed before this change and passed afterward, including a real-source two-cache-node scenario. Review and scoped re-review found no remaining actionable production defect. The generated imaging runtime key was removed from tracked demo settings and unpublished local history.

## Finished-package measurements

The [actual-package benchmark](benchmarks/package/README.md) installs each nupkg and measures ordinary picker reads on real published documents in 17.0.0, 17.0.1 and 18.0.0. All Dry and final runs passed setup/pipeline validation. The primary graphic uses the user-selected 17.0.1 and 18.0.0 baselines. The separate 17.0.0 result remains visible and is slower than native core for this already cached owner scenario. Earlier prototype images are not evidence for final package performance.

## Limits

Shared-cache correctness is tested using independent real HybridCache instances and a shared in-memory IDistributedCache. Real Redis, SQL Server, multiple Umbraco processes exchanging refresh instructions, production traffic and long-running memory behavior were not exercised. Standard Umbraco refresh messaging and a coherent shared cache remain deployment requirements. Consistency is eventual until each node processes its notification; previously returned object references cannot be revoked.

Generation markers remain in the backend, and unreachable old payloads can live until their configured TTL. Targeted invalidation removes known old raw payloads; full refresh and late factories can leave inaccessible older generations. Without a distributed backend, refresh conservatively rotates the local raw namespace. Custom external property converters retain their own dependency/lifetime responsibilities.

CI, OIDC trust policies and NuGet publication have not run against the remote repository. Benchmark and documentation image URLs become public only after the repository files are pushed. No Git tags, remote pushes or NuGet releases were created.
