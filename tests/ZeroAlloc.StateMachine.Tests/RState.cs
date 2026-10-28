namespace ZeroAlloc.StateMachine.Tests;

// Fixtures for RestoreTests. Orphan appears in no transition, so Restore refuses it.
public enum RState { Idle, Working, Failed, Done, Orphan }
