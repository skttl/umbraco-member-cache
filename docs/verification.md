# Verification record

This file records checks actually run against the repository and built NuGet package. Prototype benchmark results are not package verification.

Implementation and end-to-end package checks are in progress. Build, test, pack, minimum-version installation, composer discovery, generated schema registration, ModelsBuilder and Block List checks are not yet claimed here. The execution controller will replace this paragraph with exact commands and observed results after running them.

The schema target uses `UmbracoJsonSchemaFiles`, confirmed in the installed official `Umbraco.Cms.Targets` 17.0.0 and 18.0.0 build targets. Consumer installation must still prove that the packaged target and schema are imported and copied.

Local metadata checks completed on 2026-11-10:

- `python scripts/verify-package.py self-test` passed. It checks whole SemVer matching, numeric leading-zero rejection and rejection of archives with missing required content, incorrect package metadata, demo settings, raw sources and incorrect schema registration.
- `actionlint -shellcheck= -pyflakes= .github/workflows/ci.yml .github/workflows/publish.yml` exited 0 using the checksum-verified official Windows amd64 release 1.7.7. This checks workflow syntax; the workflows have not run on GitHub.
- Marketplace metadata validated with Ajv against the fetched official Marketplace 2020-12 schema. `unicodeRegExp=false` was required for the official schema's escaped colon pattern. The package configuration schema passed Ajv's draft-07 schema check.

No remote repository changes, tags, releases or NuGet publication have been performed as part of local verification.
