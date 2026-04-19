---
id: aot
title: AOT & Trimming
sidebar_position: 6
---

# AOT & Trimming

ZeroAlloc.StateMachine is fully compatible with .NET Native AOT and the IL trimmer. This page explains why, and what to watch out for when publishing AOT.

---

## Why it is AOT-safe

The generated code contains **no reflection**. Every state and trigger value is referenced by its fully-qualified enum literal (e.g. `global::OrderState.Idle`) in the `switch` expression arms. The JIT sees a constant pattern match; the trimmer sees ordinary field reads and enum comparisons.

This means:

- No `Activator.CreateInstance` or `Type.GetType`.
- No `Attribute` reflection at runtime — attributes are consumed only by the generator at compile time.
- No `DynamicAttribute` or `RequiresUnreferencedCode` on any generated member.
- No delegates, lambdas, or closures on the hot path (partial methods are direct calls).

---

## Publishing AOT

No extra configuration is required. The standard publish command works:

```bash
dotnet publish -r linux-x64 -c Release /p:PublishAot=true
```

The generated `TryFire` method and all partial stubs are concrete, non-generic, and statically linked. The linker can see every reachable code path.

---

## Struct machines and stack allocation

Declaring a `partial struct` eliminates even the machine instance from the heap:

```csharp
[StateMachine(InitialState = nameof(State.Off))]
[Transition<State, Trigger>(From = State.Off, On = Trigger.Flip, To = State.On)]
[Transition<State, Trigger>(From = State.On,  On = Trigger.Flip, To = State.Off)]
public partial struct LightSwitch { }

// Stack-allocated — no GC pressure whatsoever
var sw = new LightSwitch();
sw.TryFire(Trigger.Flip);
```

Struct machines are particularly useful in:
- Tight loops with many short-lived state machines
- High-throughput parsing / protocol decoding
- Embedded / WASM scenarios where GC pauses matter

Concurrent mode (`Concurrent = true`) is not available on structs. The generator emits **ZSM0004** (error) if you combine them.

---

## Partial methods and the trimmer

The `partial void OnExit*` / `partial void OnEnter*` stubs are declared in the generated file and implemented (or left empty) in your code. When a stub has no implementing declaration, the compiler removes it entirely — the trimmer never sees a dead method to worry about.

When you do implement a stub:

```csharp
partial void OnEnterDone(State from) => _logger.Log(LogLevel.Info, "Done");
```

The stub body is a direct call at the call site. The trimmer preserves it because it is statically reachable from `Fire`.

---

## Diagnostics

Neither `[StateMachine]` nor any generated member carries `[RequiresUnreferencedCode]` or `[RequiresDynamicCode]`. You will not see trimmer or AOT warnings from this library.

If your *own* hook implementations use reflection (e.g. `JsonSerializer.Serialize`), add the appropriate annotations to those methods — but that is unrelated to the state machine itself.

---

## Compatibility matrix

| Runtime | Supported | Notes |
|---------|-----------|-------|
| .NET 8+ | Yes | Full support |
| .NET Native AOT | Yes | No extra configuration |
| .NET WASM (Blazor) | Yes | Struct machines recommended |
| .NET Framework | No | Generator requires Roslyn 4+ |
| Unity (Mono) | Partial | Generator works; AOT depends on Unity's Roslyn version |
