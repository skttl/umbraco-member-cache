# Member Cache Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development or superpowers:executing-plans to implement this plan task-by-task.

**Goal:** Deliver the approved Umbraco.Community.MemberCache package in `C:/workspaces/umbraco/packages/umbraco-member-cache`, independently buildable on main (18) and v17/main (17).

**Architecture:** Bounded process-local published model cache over HybridCache snapshots, reconstructed using public Member setters, IPublishedMemberCache and IPublishedModelFactory. Distributed Umbraco refresher notifications invalidate both tiers; asynchronous ID/username lookup is shared by an automatic Member Picker converter. Optional scoped seed providers block startup and fail it on errors.

**Tech Stack:** .NET 10, Umbraco 18.0.0 / 17.0.0, Microsoft HybridCache, xUnit, official Umbraco templates, SQLite, GitHub Actions, NuGet OIDC.

**Spec:** `docs/design.md`, copied from the approved design and benchmark investigation.

## Global Constraints

- Repository and package names are umbraco-member-cache and Umbraco.Community.MemberCache.
- main targets 18 with interval [18.0.0,19.0.0); v17/main targets 17 with interval [17.0.0,18.0.0).
- No shared multi-major project or production conditional compilation. Both branches build independently.
- Public IPublishedMemberLookup has async ID/username single/batch calls; null/blank usernames reject, invalid IDs miss, batches preserve order and duplicates and omit misses.
- ModelsBuilder and Block List use public APIs. Never persist password hashes or serialize published objects.
- MaxModels=10000; LocalCacheExpiration=1 day; CacheExpiration=365 days; seeding off, batch 100, member type aliases and custom scoped providers.
- None is the chosen picker result cache level. Preserve custom converters and core legacy/external/Delivery API semantics.
- Local model cache invalidation, schema/model-factory changes, content/media references and distributed invalidation are required. In-flight loads/seeding must not reinsert accessible stale entries.
- No remote pushes, tags, publication, credentials or permanent NuGet API keys.
- License ruling: use MIT for this original community package, reversible before release.
- Work in the isolated staging checkout at `C:/Users/skttl/Documents/Codex/2026-11-10/jeg-x20/work/member-cache-repository` while implementing; controller syncs validated files to the requested empty destination.

## Review Focus

- Factory finishing after invalidation must not poison cache on another server.
- Invalidating an old username must not leave an alias pointing to a different/removed member.
- ModelsBuilder reload and referenced document/media changes must retire converted properties.
- Custom seed failure or cancellation must prevent successful readiness, with no providers resolved while disabled.
- Package installation in clean template sites must prove composer/schema discovery independent of project references.

## Task 1: Cache core and focused tests

**Files:** `src/Umbraco.Community.MemberCache/{IPublishedMemberLookup.cs,MemberCacheOptions.cs,MemberSnapshot.cs,MemberSnapshotMapper.cs,PublishedMemberLookup.cs,MemberCacheInvalidator.cs}` and focused tests under `tests/Umbraco.Community.MemberCache.Tests/`.

**Interfaces:** Public Task-based lookup contract from design. `MemberCacheInvalidator` exposes `Task InvalidateMembersAsync(IEnumerable<int>, CancellationToken)`, `Task InvalidateAllAsync(CancellationToken)`, and `void ClearModels()` for adapters. Member data loading goes through an internal seam for deterministic tests. Registered service is singleton and resolves scoped Umbraco services in scopes where needed. Options bind `Umbraco:Community:MemberCache`.

- [ ] Pin tests for lookup identity, username semantics, missing/duplicate/ordered batch behavior, typed snapshot roundtrip and bounded cache eviction.
- [ ] Implement public snapshot rehydration and both cache tiers, preserving source errors and cancellation.
- [ ] Test two independent L1/model caches sharing one L2: save/rename/delete/full invalidation and overlapping factories cannot expose stale entries after a refresher is processed.
- [ ] Verify focused tests and record concurrency/consistency limitations explicitly.

## Task 2: Umbraco adapters, converter, seeding and focused tests

**Files:** `src/Umbraco.Community.MemberCache/{MemberCacheComposer.cs,MemberCacheNotificationHandler.cs,CachedMemberPickerValueConverter.cs,IMemberSeedProvider.cs,MemberTypeSeedProvider.cs,MemberCacheStartup.cs}` and focused tests.

**Interfaces:** Consumes task 1 lookup and invalidator. Registers composer, configuration validation, scoped seed providers and handlers for Member/ContentType/DataType/Content/Media refresher notifications. Public `IMemberSeedProvider.GetMemberIdsAsync(CancellationToken)` returns IAsyncEnumerable<int>.

- [ ] Write focused adapter/converter/seed tests, including disabled providers, fail-fast, custom scoped providers, deduplication and cancellation.
- [ ] Implement automatic converter replacement while preserving custom converters, legacy ID/member UDI and 18 external fallback; Delivery API stays unsupported.
- [ ] Implement distributed refresher handlers and schema/model-factory/content/media model retirement.
- [ ] Implement startup seeding before readiness only in suitable installed runtime states, raw data only, no L0 warmup.
- [ ] Verify adapter tests and both minimum-version compile contracts; report exact v17 adaptation required.

## Task 3: Repository, official demo, metadata and release checks

**Files:** root solution/metadata/icon, `demo/`, `docs/`, schema/buildTransitive, `.github/workflows/`, `scripts/verify-package.py`.

- [ ] Generate official-template demo pinned 18.0.0 with SQLite, project reference and a reproducible example fixture.
- [ ] Create SDK library and xUnit test projects, minimal dependency interval, root/NuGet/Marketplace documentation, approved brand assets and honest prototype screenshot captions.
- [ ] Add configuration schema and Umbraco schema registration target, verify actual nupkg contents.
- [ ] Add read-only CI and OIDC publication workflow with whole SemVer/tag/major/branch checks.
- [ ] Build/test/pack and install built nupkg into a separate clean official-template site; boot and verify schema/composer plus ModelsBuilder/Block List behavior.

## Task 4: Independent v17 branch, review and destination delivery

- [ ] Review tasks 1 and 2 for spec compliance, public API compatibility, security and concurrency; fix substantive findings.
- [ ] Sync the isolated implementation to the requested empty directory, initialize Git and commit the independently working main branch locally.
- [ ] Create v17/main, adapt package interval/version/template and constructor differences without production cross-major conditionals, verify build/test/pack/clean install on 17.0.0.
- [ ] Return checkout to main, retain v17/main locally, configure supplied repository URL without pushing.
- [ ] Final review, record exact commands/results, package paths, version branches and unverified limitations in `docs/verification.md` and controller ledger.
