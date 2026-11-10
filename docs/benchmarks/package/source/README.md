# Finished-package benchmark harness

This is the source used for the retained package measurements, not the earlier prototype. Three consumer projects reference the actual NuGet package: 18.0.0 on CMS 18.0.0, and 17.0.0 on CMS 17.0.0 and 17.0.1. There are no project references or copied package implementation classes. The pinned BenchmarkDotNet 0.15.8 matches the recorded runs.

The program boots a fresh SQLite-backed Umbraco host, creates a member and real published document through public services, warms `IPublishedMemberLookup`, and checks identity, property value, generated package member type, selected pipeline converters and cache levels before measuring. The core control inherits the native converter unchanged except that `IsConverter` selects the `pickedMember` fixture property. The package composer automatically supplies the converter for `cachedPickerNone`.

Both methods run ordinary `Value<IPublishedContent>()` then `Value<string>("profileTitle")` and include the same `EnsureUmbracoContext()` overhead. Both properties use one Member Picker datatype and identical member UDI source. Native core returns its normal published member; the package returns its generated ModelsBuilder model. The core path is deliberately not wrapped artificially. Fixture creation, publishing, first lookup, validation, model generation and host startup are outside timing.

Run one version at a time in Release. Use a fresh runtime directory for every run. Create each project's `wwwroot/media` directory before booting: Umbraco 17 requires it. Restore from NuGet.org plus a directory containing the actual built `.nupkg` if the tested version has not been published. For example, from this source directory:

```powershell
$version = '17.0.1' # alternatively 17.0.0 or 18.0.0
$project = Join-Path $PWD "$version/PackageBenchmark.csproj"
New-Item -ItemType Directory -Force -Path "$version/wwwroot/media" | Out-Null
dotnet restore $project --source './local-nupkg-feed' --source 'https://api.nuget.org/v3/index.json'
dotnet build $project -c Release --no-restore
$env:PACKAGE_BENCHMARK_RUNTIME = Join-Path $env:TEMP ('member-cache-dry-' + [guid]::NewGuid().ToString('N'))
$env:PACKAGE_BENCHMARK_JOB = 'Dry'
dotnet run --project $project -c Release --no-build --no-restore -- --filter '*' --noOverwrite --exporters json --artifacts "$env:PACKAGE_BENCHMARK_RUNTIME/bdn"
$env:PACKAGE_BENCHMARK_RUNTIME = Join-Path $env:TEMP ('member-cache-measured-' + [guid]::NewGuid().ToString('N'))
$env:PACKAGE_BENCHMARK_JOB = 'Default'
dotnet run --project $project -c Release --no-build --no-restore -- --filter '*' --noOverwrite --exporters json --artifacts "$env:PACKAGE_BENCHMARK_RUNTIME/bdn"
```

`Default` selects `InProcessNoEmitToolchain`, five warmup iterations, fifteen measurement iterations and `MemoryDiagnoser`. Do not pass `--job Dry` in addition to the environment setting, because that adds another BDN job. The dry runs only validate correctness; their timings are not the reported results.

Limits: one warm member, one property read, one SQLite host, one process, in-memory distributed-cache implementation, no production traffic. This measures the completed converter's warm path, including its actual invalidation-generation and model-type checks. It does not measure startup, cold misses, Redis/network caches, multi-node operation, seeding or throughput under contention. The same document owner is reused and native cache levels are retained (`Snapshot` for core, `None` for package). Results are comparative microbenchmarks, not an end-to-end page response claim.

