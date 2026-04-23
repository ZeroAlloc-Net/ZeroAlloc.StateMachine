```

BenchmarkDotNet v0.14.0, Windows 11 (10.0.26200.8246)
Unknown processor
.NET SDK 10.0.106
  [Host]   : .NET 10.0.6 (10.0.626.17701), X64 RyuJIT AVX2
  ShortRun : .NET 10.0.6 (10.0.626.17701), X64 RyuJIT AVX2

Job=ShortRun  IterationCount=3  LaunchCount=1  
WarmupCount=3  

```
| Method                                      | Mean       | Gen0   | Allocated |
|-------------------------------------------- |-----------:|-------:|----------:|
| &#39;TryFire – valid (non-concurrent)&#39;          | 19.7049 ns | 0.0019 |      24 B |
| &#39;TryFire – invalid (non-concurrent)&#39;        |  1.4316 ns |      - |         - |
| &#39;TryFire – guard allowed (non-concurrent)&#39;  |  8.3100 ns | 0.0019 |      24 B |
| &#39;TryFire – guard blocked (non-concurrent)&#39;  |  0.0000 ns |      - |         - |
| &#39;TryFire – CAS, no contention (concurrent)&#39; | 38.3465 ns |      - |         - |
