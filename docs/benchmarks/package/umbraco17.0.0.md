```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Unknown processor
.NET SDK 10.0.401
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4

Toolchain=InProcessNoEmitToolchain  IterationCount=15  WarmupCount=5  

```
| Method       | Mean     | Error     | StdDev    | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |---------:|----------:|----------:|------:|--------:|-------:|----------:|------------:|
| CorePicker   | 234.5 ns |  32.80 ns |  30.68 ns |  1.02 |    0.20 |      - |      40 B |        1.00 |
| CachedPicker | 753.4 ns | 143.98 ns | 134.68 ns |  3.28 |    0.77 | 0.0010 |     216 B |        5.40 |
