```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Unknown processor
.NET SDK 10.0.401
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4

Toolchain=InProcessNoEmitToolchain  IterationCount=15  WarmupCount=5  

```
| Method       | Mean        | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |------------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| CorePicker   | 10,729.8 ns | 612.37 ns | 572.81 ns |  1.00 |    0.07 | 0.0610 |   17683 B |        1.00 |
| CachedPicker |    872.1 ns |  85.05 ns |  79.55 ns |  0.08 |    0.01 | 0.0019 |     320 B |        0.02 |
