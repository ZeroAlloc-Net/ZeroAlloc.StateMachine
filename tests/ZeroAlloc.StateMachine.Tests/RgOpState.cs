namespace ZeroAlloc.StateMachine.Tests;

// Ghost appears in no transition, so Restore refuses it.
public enum RgOpState { Idle, Running, Faulted, Ghost }
