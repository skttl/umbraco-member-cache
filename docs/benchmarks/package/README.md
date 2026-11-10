# Member Picker: finished-package benchmark

These measurements install the actual NuGet packages into separate consumers and use the automatically registered converter on a real published document. Both paths read the selected member's profileTitle through normal Value calls.

| CMS | Package | Standard picker, µs | With Member Cache, µs | Less time |
| --- | --- | ---: | ---: | ---: |
| **17.0.1** | 17.0.0 | **50.410** | **0.859** | **98.3%** |
| **18.0.0** | 18.0.0 | **10.730** | **0.872** | **91.9%** |
| 17.0.0 minimum | 17.0.0 | 0.235 | 0.753 | No gain; 3.21× more time |

The 17.0.0 result must remain visible: its native picker already caches the result on the reused owner. That behavior was [corrected in 17.0.1](https://github.com/umbraco/Umbraco-CMS/pull/21006). The package supports 17.0.0, but this particular warm-read scenario is slower there. The two primary rows compare versions after the correction.

The earlier prototype numbers are not used for current package claims. The production path includes the real lookup, bounded cache and model-type validation. Core returns its native PublishedMember; the package returns its generated ModelsBuilder member model. Both produce the same identity and property value. The benchmark preserves normal core behavior rather than adding an artificial wrapper.

## Method and results

BenchmarkDotNet 0.15.8, SDK 10.0.401, .NET 10.0.14, Windows 11, Release, InProcessNoEmitToolchain and MemoryDiagnoser. Each job requests five warmup and fifteen measurement iterations. Default upper-outlier removal retained thirteen observations for 17.0.1's cached case and fifteen for every other case. All three versions first passed Dry and fixture/pipeline validation. Runs were sequential.

| CMS | Core mean ± error, ns | Package mean ± error, ns | Allocated core → package |
| --- | ---: | ---: | ---: |
| 17.0.0 | 234.5 ± 32.80 | 753.4 ± 143.98 | 40 → 216 B |
| 17.0.1 | 50,410.1 ± 4,866.02 | 858.7 ± 43.89 | 39,884 → 320 B |
| 18.0.0 | 10,729.8 ± 612.37 | 872.1 ± 85.05 | 17,683 → 320 B |

Error is the half-width of BDN's 99.9% confidence interval. Full JSON and Markdown reports and validation records are beside this file. The [portable source and exact commands](source/README.md) reproduce the measured two-property document fixture. The control converter inherits core unchanged except for selecting the control property; the package composer supplies the other property converter.

Each host uses fresh SQLite, InMemoryAuto ModelsBuilder, a single member, DefaultHybridCache and MemoryDistributedCache with package defaults. Creation, publishing, first lookup, model generation and assertions happen outside timing. Both methods return a string and include identical EnsureUmbracoContext handling. One reused document and member are read repeatedly with a warm model cache.

This is not a page-rendering, cold-cache, seeding, Redis/network, multi-server throughput or production-load measurement. Actual invalidation correctness is covered separately by package tests and hosted save/rename/delete checks. Regenerate graphics with `python scripts/render-benchmarks.py` after installing Pillow; the script validates the expected measured methods and sample counts.
