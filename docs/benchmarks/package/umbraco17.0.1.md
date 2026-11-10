```

BenchmarkDotNet v0.15.8, Windows 11 (10.0.26300.9711)
Unknown processor
.NET SDK 10.0.401
  [Host] : .NET 10.0.14 (10.0.14, 10.0.1426.52326), X64 RyuJIT x86-64-v4

Toolchain=InProcessNoEmitToolchain  IterationCount=15  WarmupCount=5  

```
| Method       | Mean        | Error       | StdDev      | Ratio | RatioSD | Gen0   | Allocated | Alloc Ratio |
|------------- |------------:|------------:|------------:|------:|--------:|-------:|----------:|------------:|
| CorePicker   | 50,410.1 ns | 4,866.02 ns | 4,551.67 ns |  1.01 |    0.13 | 0.2441 |   39884 B |       1.000 |
| CachedPicker |    858.7 ns |    43.89 ns |    36.65 ns |  0.02 |    0.00 | 0.0019 |     320 B |       0.008 |
