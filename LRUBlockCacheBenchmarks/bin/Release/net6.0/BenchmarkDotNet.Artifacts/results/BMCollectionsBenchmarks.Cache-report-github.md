``` ini

BenchmarkDotNet=v0.13.1, OS=Windows 10.0.19044.1526 (21H2)
AMD Ryzen 7 5800H with Radeon Graphics, 1 CPU, 16 logical and 8 physical cores
.NET SDK=6.0.101
  [Host]     : .NET 6.0.1 (6.0.121.56705), X64 RyuJIT
  DefaultJob : .NET 6.0.1 (6.0.121.56705), X64 RyuJIT


```
| Method |       N |             Mean |          Error |         StdDev |
|------- |-------- |-----------------:|---------------:|---------------:|
|    **Add** |       **1** |               **NA** |             **NA** |             **NA** |
| Remove |       1 |               NA |             NA |             NA |
| TryGet |       1 |         32.45 ns |       0.193 ns |       0.171 ns |
|    **Add** |    **1000** |               **NA** |             **NA** |             **NA** |
| Remove |    1000 |               NA |             NA |             NA |
| TryGet |    1000 |     36,554.18 ns |     206.809 ns |     193.450 ns |
|    **Add** |  **100000** |               **NA** |             **NA** |             **NA** |
| Remove |  100000 |               NA |             NA |             NA |
| TryGet |  100000 |  3,644,331.54 ns |  25,427.511 ns |  23,784.910 ns |
|    **Add** | **1000000** |               **NA** |             **NA** |             **NA** |
| Remove | 1000000 |               NA |             NA |             NA |
| TryGet | 1000000 | 37,102,747.14 ns | 562,757.602 ns | 648,072.492 ns |

Benchmarks with issues:
  Cache.Add: DefaultJob [N=1]
  Cache.Remove: DefaultJob [N=1]
  Cache.Add: DefaultJob [N=1000]
  Cache.Remove: DefaultJob [N=1000]
  Cache.Add: DefaultJob [N=100000]
  Cache.Remove: DefaultJob [N=100000]
  Cache.Add: DefaultJob [N=1000000]
  Cache.Remove: DefaultJob [N=1000000]
