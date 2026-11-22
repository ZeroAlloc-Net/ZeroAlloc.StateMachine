# AGENTS.md: ZeroAlloc.StateMachine

Source-generated finite state machines for .NET. One NuGet package, `ZeroAlloc.StateMachine`, ships the
attributes and bundles the Roslyn generator as an analyzer. Consumers annotate a `partial` class, struct
or record with `[StateMachine]` and `[Transition]`, and the generator emits `TryFire` as a `switch`.

## Layout

- `src/ZeroAlloc.StateMachine` is the only packable project (`net10.0` and `net11.0`). The generator
  (`src/ZeroAlloc.StateMachine.Generator`, `netstandard2.0`) has `IsPackable=false`: its DLL is copied into
  the main package under `analyzers/dotnet/cs` by the `IncludeGeneratorInPackage` target. Never publish the
  generator as its own package.
- The generator's output is what consumers run, so judge a change by the generated code in
  `tests/ZeroAlloc.StateMachine.Generator.Tests/Snapshots/*.verified.cs`, not only by the generator source.
  A snapshot diff in a PR is a change in shipped behaviour. Review it as such.
- `tests/ZeroAlloc.StateMachine.Tests` compiles the generator as an analyzer into the test project, so its
  machines are generated code, not hand-written stand-ins.
- Public API and diagnostics are tracked files. A new public type or member goes into
  `src/ZeroAlloc.StateMachine/PublicAPI.Unshipped.txt` (RS0016 and RS0017 are errors). A new `ZSM` diagnostic
  goes into `src/ZeroAlloc.StateMachine.Generator/AnalyzerReleases.Unshipped.md` and gets a page under
  `docs/diagnostics/`. Both `Shipped` files are moved by the release workflow, not by hand.

## Build and test

Prerequisites: the SDK pinned in `global.json` (11.0.100, roll forward to the latest feature band). The test
projects target `net10.0` and `net11.0`, so both runtimes must be installed. The benchmark project targets
`net10.0` and needs the .NET 10 runtime. The AOT sample targets `net11.0` and needs the .NET 11 runtime plus
`clang` and `zlib1g-dev` for NativeAOT.

Job `build` in `.github/workflows/ci.yml`:

```bash
dotnet tool restore
dotnet restore ZeroAlloc.StateMachine.slnx
dotnet build ZeroAlloc.StateMachine.slnx --configuration Release --no-restore
dotnet test ZeroAlloc.StateMachine.slnx --configuration Release --no-build --verbosity normal --logger "trx;LogFilePrefix=test-results" --collect "XPlat Code Coverage" --settings coverage.runsettings --results-directory TestResults
```

The same job then runs a coverage gate (`MarcelRoozekrans/ci-baseline/actions/coverage-gate`) over the
Cobertura reports under `TestResults/`. It is a shared action, so the commands above do not reproduce it.

Job `aot-smoke` in `ci.yml` publishes `samples/ZeroAlloc.StateMachine.AotSmoke` with `PublishAot=true`, and
fails on IL2026, IL2067, IL2075, IL2091, IL3050 and IL3051:

```bash
dotnet restore samples/ZeroAlloc.StateMachine.AotSmoke/ZeroAlloc.StateMachine.AotSmoke.csproj
dotnet publish samples/ZeroAlloc.StateMachine.AotSmoke/ZeroAlloc.StateMachine.AotSmoke.csproj -r linux-x64 -c Release -o ./aot-out
./aot-out/ZeroAlloc.StateMachine.AotSmoke
```

Job `full` in `.github/workflows/benchmarks.yml` (manual only; pull requests run the shared
`benchmark-smoke.yml` against `benchmarks/ZeroAlloc.StateMachine.Benchmarks`):

```bash
dotnet build benchmarks/ZeroAlloc.StateMachine.Benchmarks -c Release
dotnet run --project benchmarks/ZeroAlloc.StateMachine.Benchmarks -c Release --no-build -- --filter "${FILTER:-*}"
```

Inner loop (no CI job runs these; each passed on this machine):

```bash
dotnet test tests/ZeroAlloc.StateMachine.Tests -c Release -f net10.0 --filter "FullyQualifiedName~AllocationTests"
dotnet test tests/ZeroAlloc.StateMachine.Generator.Tests -c Release -f net10.0
SHA=$(git rev-parse --short HEAD); dotnet pack src/ZeroAlloc.StateMachine -c Release -p:Version=9.9.9-local.$SHA -o /tmp/sm-pack
```

`dotnet pack` fails with ZAVERSION001 or ZAVERSION002 unless `-p:Version` is passed to the command that builds,
so a pack with `--no-build` needs it on the build step too. Build and test do not need it. Pack rebuilds.

CI job `sdk10-consumer` needs a 10.0.1xx SDK; `tests/ZeroAlloc.StateMachine.Sdk10Smoke/global.json` pins
10.0.100 with `rollForward: latestPatch`, so a newer band cannot stand in for it. It builds and runs a
consumer of the packed packages with Roslyn 5.0:

```bash
dotnet pack src/ZeroAlloc.StateMachine -c Release -o artifacts -p:Version=9.9.9-local.sha$(git rev-parse --short HEAD)
cd tests/ZeroAlloc.StateMachine.Sdk10Smoke/Consumer
dotnet run -c Release -p:ZeroAllocStateMachineVersion=9.9.9-local.sha$(git rev-parse --short HEAD)
```

CI job `roslyn-floor` calls the shared `roslyn-floor.yml` from ZeroAlloc-Net/.github: it packs the
solution and fails when a packed DLL references Roslyn above 5.0.0.

## Conventions and gotchas

- The generator references `Microsoft.CodeAnalysis.CSharp` 5.0.0, the compiler of SDK band 10.0.1xx, the
  oldest supported SDK. A compiler older than the Roslyn the DLL references skips the generator with
  `CS9057`, and nothing is generated. The shared Renovate preset holds it below 5.1. If a newer Roslyn API
  is needed, change the code; raise the floor only together with the minimum SDK. The generator test
  project may use a newer Roslyn.
- Exception to the org rule on benchmark frameworks: the benchmark project targets only `net10.0`, not the
  LTS set, because BenchmarkDotNet 0.15.8 cannot run on `net11.0` (see the comment in its csproj).
- The 0 B budget covers `TryFire` for valid, invalid, guard-allowed and guard-blocked transitions, and
  concurrent mode (`AllocationTests`, `AllocationGate.AssertBudget(0, ...)`). Allocating configurations: a
  machine with `AfterMs` timed transitions allocates one `System.Threading.Timer` per timed edge on its
  first arm, reuses it afterwards, and implements `IDisposable`. The tests do not assert a 0 B budget
  for timed machines. Do not write "0 B" about them.
- Generated code must stay AOT-safe and free of reflection. Timed edges require `Concurrent = true` or a
  `[StateMachinePart]` (ZSM0012), and `AfterMs` must be positive (ZSM0013).
- The generator runs on `netstandard2.0` with `EnforceExtendedAnalyzerRules`: do not use APIs missing there
  and keep the incremental pipeline cacheable (`IncrementalCachingTests`).
- `TreatWarningsAsErrors` is on everywhere, with Meziantou, Roslynator and ZeroAlloc analyzers. A new
  warning fails the build.

<!-- org-core:start sha256=8faf417fa86f11bd8ad9bc98dfa4683fba44e440291e84c6cb9b0ec3ca0c0e7e (generated from docker-nuc scripts/agents-md/org-core-zeroalloc.md; do not edit here) -->
## Org rules

These rules hold in every ZeroAlloc-Net repo. Each library makes two promises: it works under
NativeAOT and trimming, and its hot path allocates nothing. The 2026-10-11 sweep found 16 of 28 repos
allocating on the hot path, most with docs claiming 0 B.

### Hot path

The operation a consumer runs repeatedly: `Send`, a cache hit, `Validate`, `Map`, `Serialize`, a
resilience call, a state transition, an append, a worker poll, including the idle poll. Startup and
registration are not hot path. For a source generator, the hot path is the code it emits: judge the
snapshots and the generated output.

### Allocation traps the sweep found

| Pattern | Instead |
|---|---|
| `async ValueTask` that only awaits and returns | `return new ValueTask<T>(inner.XAsync(ct));` when the method only forwards a `Task<T>` (return a forwarded `ValueTask<T>` directly). The wrapper stores a synchronous throw in the returned task; the direct return lets it, and the cancellation status of the callee, reach the caller. Keep the `async` wrapper when callers depend on either |
| `Task.WhenAll` over tasks that already completed | skip the await only when every task has `IsCompletedSuccessfully` (`Status == TaskStatus.RanToCompletion` on `netstandard2.0`); otherwise `await Task.WhenAll`, which also reports faults of completed tasks |
| `TagList` or log `params object[]` built before `Enabled`/`IsEnabled` | check first, or `LoggerMessage.Define` |
| event args allocated before checking for subscribers | copy the handler to a local, check, then allocate |
| `new Serializer()` / `new Utf8JsonWriter()` per call | cache stateless instances, reuse writers with `Reset` |
| `GetString()` / `ToString()` round-trips in JSON converters | parse and format through spans |
| `ToArray()` / `ToList()` copies between layers | pass `ReadOnlyMemory<T>` or spans, copy once at most |
| LINQ in emitted code | emit `for` loops |
| `yield` iterator behind a public `GetEnumerator` | public struct enumerator, from the first release |
| `Enum.IsDefined(typeof(E), v)` with a boxed value, `decimal.GetBits(v)` to read the scale | `Enum.IsDefined<E>(v)` when `v` has type `E` (.NET 5 or later, so not on .NET Framework, `netstandard2.0`, `netstandard2.1` or .NET Core 3.1); `v.Scale` for a scale read only (.NET 7 or later, never for the coefficient or sign). Keep the old call on older targets |
| configuration parsed or read per call | parse once and cache |
| an `AsyncLocal` write per call | pass state explicitly |
| a capturing lambda or method-group delegate per call | cache the delegate in a field, or a static lambda |
| a per-key object that is never removed | remove on release; otherwise it is a leak |

Inherent allocations are fine when documented: the consumer's own result, an exception on a failure
path, a one-time cache fill.

### Tests and benchmarks

- Every hot path has an allocation gate test (`AllocationGate` from ZeroAlloc.TestHelpers) that measures
  the whole public call, including any state setup.
- Budgets are exact. If a path allocates, assert that number and comment what it is.
- Test the generated code, not a hand-written stand-in, and cover every shape: each attribute, nested
  collections, and the asynchronous case with a fake that returns pending tasks.
- Benchmarks run in CI through the shared `benchmark-smoke.yml` and target the library's LTS
  frameworks. The benchmark body allocates nothing besides the library itself. Run locally with
  `--inProcess`.
- Publish exact allocations and same-run ratios against a `Baseline`, never absolute timings. Run with
  `--launchCount 3`, pin CPUs with `taskset`, give the spread, and label every table.

### NativeAOT and trimming

- Packable runtime projects, and projects consumers publish, set `IsAotCompatible=true` on modern
  target frameworks. Analyzers and source generators target `netstandard2.0`.
- No reflection or runtime code generation on a supported path. An opt-in reflection fallback carries
  honest `[RequiresUnreferencedCode]` / `[RequiresDynamicCode]`. Every suppression must be true;
  remove dead ones.
- The AOT smoke sample publishes with `TreatWarningsAsErrors=true`. If a third-party warning really
  blocks it, use a narrow, justified suppression for that warning. `WarningsNotAsErrors` selects codes,
  not assemblies, so it would also downgrade the same warning from generated code.
- The smoke runs the hot path of every package in the repo. A package without a smoke has unknown AOT
  status. Adapters run against real services in CI service containers; publish-only is a last resort.
  App and template smokes make authenticated requests.

### Docs and shipping

- Docs match measurements. After a hot-path change, re-measure and update `docs/performance.md` and the
  README. Never write "0 B" without a test that asserts it.
- New public API is `feat:`, even for a performance change. Public API changes are additive: never add
  an optional parameter to a shipped signature. A breaking end state goes in a `next-major` issue.
- A behaviour change gets a `## Behaviour change` section in the PR body.
- Conventional Commits with release-please. Subjects start lowercase, body lines stay within 100
  characters, and a body never contains nested parentheses: release-please drops the whole commit.
- Local packs use `9.9.9-local.<sha>`. After a release, verify the package on NuGet.
- Fix review findings in the PR; file an issue only for what cannot go in it.

Full rules, template wiring lessons and the reasons behind each rule: https://github.com/ZeroAlloc-Net/.github/blob/main/AGENTS.md
<!-- org-core:end -->
