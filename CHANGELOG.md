# Changelog

## 1.0.0 (2026-04-18)


### Features

* add BenchmarkDotNet benchmarks and performance documentation ([346d4cd](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/346d4cd14cc3f81b68a285d6b0c591c064eaac58))
* add generator package scaffold with model types and skeleton generator ([01a3c21](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/01a3c2125f7d18a1d669129458aea6ad61a4158f))
* add main package with StateMachine, Transition, Terminal attributes ([961f456](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/961f456f04c8bd214820444afacf072a156d9ca0))
* add ZSM0001/ZSM0002/ZSM0003/ZSM0004 diagnostics and fix concurrent stub XML docs ([3b2a6c4](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/3b2a6c474d4249f9935092c2a25bd8a95c34b9fd))
* emit XML-documented partial method stubs; add guarded machine snapshot test ([b11029c](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/b11029cb2f992c0869bdf76b0f3dae4ab3772b40))
* generator emits concurrent Interlocked.CompareExchange TryFire ([a992302](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/a992302e4cc5603ade68022b15f1a94da8623b00))
* generator emits non-concurrent TryFire switch expression ([470be17](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/470be17677299caf5dee0e711c1fa2b64fcbc6ca))
* implement generator attribute parser (Parse method) ([a1c6d53](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/a1c6d53ab5ab52f33c8d6a63bdd620abc3e55ec3))


### Bug Fixes

* add error guard in RegisterSourceOutput, suppress unused constant warnings ([ac5d873](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/ac5d87327546f3f8cd4f5a2b4027029cc79db32c))
* add private modifier to partial bool guard stubs (required by C# compiler) ([d9ce140](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/d9ce14094e75cf4cf8732ccd1b3552c238383942))
* emit compilable partial method declarations in WritePartialStubs ([644df80](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/644df802a40e68bc1d83920e5220656f78949cf5))
* GetEnumMemberName returns null on unresolvable values, pin Verify.SourceGenerators version ([5d93d7b](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/5d93d7b016e7a088e258a5f4bf887c1395c13f29))
* improve GetDiagnostics to capture post-generation compilation errors ([fd0d6c5](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/fd0d6c55e7099234159d938598d9e7c6fd1e810f))
* use FQN type names in generated code; document guard stub semantics ([8ec09f0](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/8ec09f0909e7c7f834a237e99d15e455f2bb5462))
* use ImmutableArray&lt;T&gt; in StateMachineModel for correct incremental caching; observe CancellationToken in Parse ([b319519](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/b31951947acfa2d1764dac54c5713b01a7b510c7))


### Documentation

* add documentation skeleton and backlog ([7c6521a](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/7c6521a9e4d68e2b9639e3b24e0c58e5e7b20f4b))


### Tests

* add runtime behaviour tests for non-concurrent and concurrent machines ([1fd3c15](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/1fd3c1516923e7c1808bce85961f5a146c97e923))
* scaffold runtime and generator test projects ([d5e3035](https://github.com/ZeroAlloc-Net/ZeroAlloc.StateMachine/commit/d5e3035c4365d0b58eea165be560f9058b0c8c4f))

## Changelog

All notable changes to this project will be documented in this file.

See [Conventional Commits](https://conventionalcommits.org) for commit guidelines.
