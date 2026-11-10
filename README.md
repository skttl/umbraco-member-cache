# Umbraco Community Member Cache

Cache published members with ModelsBuilder and property value converter support. The package provides asynchronous ID and username lookups, automatically integrates with Member Picker, and responds to Umbraco's distributed refresh notifications.

[Installation, configuration and usage](docs/nuget-readme.md) · [Verification record](docs/verification.md) · [Design](docs/design.md)

`main` builds package 18.x for Umbraco `[18.0.0,19.0.0)`. `v17/main` independently builds package 17.x for `[17.0.0,18.0.0)`. Both target .NET 10. Each branch contains one packable server library, its tests and an official-template SQLite demo.

## Work locally

Install the .NET 10 SDK and Python 3. The solution includes `src/`, `tests/` and `demo/`.

```sh
dotnet restore Umbraco.Community.MemberCache.slnx
dotnet build Umbraco.Community.MemberCache.slnx -c Release --no-restore
dotnet test tests/Umbraco.Community.MemberCache.Tests -c Release --no-build
dotnet pack src/Umbraco.Community.MemberCache -c Release --no-build -o artifacts
python scripts/verify-package.py package artifacts
python scripts/verify-package.py self-test
dotnet run --project demo/Umbraco.Community.MemberCache.TestSite
```

Complete the local Umbraco installer when the demo first opens. Keep local passwords in user secrets or ignored development settings. Databases, logs and media are ignored.

The demo was generated with SDK 10.0.401 and the official `Umbraco.Templates` 17.0.0 template, then given a project reference to the package. The generated site targets `net10.0` and CMS 17.0.0. Its generation command was:

```sh
dotnet new install Umbraco.Templates::17.0.0
dotnet new umbraco -n Umbraco.Community.MemberCache.TestSite -o demo/Umbraco.Community.MemberCache.TestSite --development-database-type SQLite --no-restore
```

The demo also references `Umbraco.Cms.DevelopmentMode.Backoffice` 17.0.0 for the generated InMemoryAuto models mode. Installation checks are recorded in [verification](docs/verification.md).

After installing the demo database, create the example member type, member, Block List and Member Picker page with:

```sh
dotnet run --project demo/Umbraco.Community.MemberCache.TestSite -- --MemberCacheDemo:CreateSample=true
```

This fixture is opt-in and changes the local demo database. It checks the generated member and block models, picker conversion and member save/rename invalidation, and prints each successful assertion. Run it in a fresh demo database. Package installations do not include the fixture.

The project-reference demo checks development integration. Also install the built `.nupkg` into a separate clean official-template site on the minimum CMS version to check composer discovery and schema registration. A project reference alone does not verify package installation.

## Releases

CI restores and builds the solution, runs tests, packs the library and inspects the actual NuGet archive. It runs independently on `main` and `v17/main` with read-only repository permissions.

The publishing workflow accepts whole SemVer tags prefixed with `v`, such as `v18.0.0`, `v18.0.1-rc1` or `v17.0.1-beta.2`. Numeric identifiers cannot have leading zeroes. It verifies the tag's major against the package and Umbraco interval and checks that the tagged commit belongs to the expected remote version branch before it builds, tests and packs that exact version.

Before the first release, run `dotnet pack -c Release -o ./artifacts` and inspect the resulting package. Configure a GitHub environment named `release` with the `NUGET_USER` secret set to the NuGet profile name. Create a [NuGet trusted publishing policy](https://www.nuget.org/account/trustedpublishing) for owner `skttl`, repository `umbraco-member-cache`, workflow filename `publish.yml`, environment `release`. The workflow uses `NuGet/login@v1`, `contents: read` and `id-token: write`; it stores no permanent NuGet API key. Confirm policy setup before triggering a publication.

The workflow was authored against the official [checkout](https://github.com/actions/checkout), [setup-dotnet](https://github.com/actions/setup-dotnet), [setup-python](https://github.com/actions/setup-python) and [NuGet trusted publishing](https://learn.microsoft.com/en-us/nuget/nuget-org/trusted-publishing) documentation.

## Benchmarks and license

The [finished-package Member Picker benchmark](docs/benchmarks/package/README.md) uses real published documents and actual NuGet packages. On warm repeated reads, the measured time falls from 50.410 to 0.859 µs in CMS 17.0.1 and from 10.730 to 0.872 µs in 18.0.0. The 17.0.0 minimum-version result is an explicit counterexample: core's already cached value is faster. See the full reports for errors, allocations and limits; these are not page-load or production throughput claims. Earlier prototype graphics remain historical and are not used for current claims.

[MIT](LICENSE), copyright 2026 skttl.
