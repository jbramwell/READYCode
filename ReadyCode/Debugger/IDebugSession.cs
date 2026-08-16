// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

namespace ReadyCode.Debugger;

/// <summary>
/// A live BASIC line-level debug session against a running target - implemented by
/// <see cref="ReadyCode.Vice.ViceDebugSession"/> (VICE's binary monitor) and
/// <see cref="ReadyCode.C64U.C64UDebugSession"/> (a software debug stub injected into the C64
/// Ultimate's running BASIC interpreter, driven by polling over its REST API, since it has no
/// native breakpoint/memory-watch capability of its own). The rest of the app (breakpoint gutter,
/// current-line highlight, Variables/Breakpoints/Call Stack panels, Debug menu commands) is
/// written against this interface and doesn't need to know which target is actually active.
/// </summary>
public interface IDebugSession : IAsyncDisposable
{
    #region Events

    /// <summary>
    /// Occurs when the CPU stops at a genuine line boundary - either a user breakpoint, or a
    /// completed Pause/Step Line/Step Out.
    /// </summary>
    event EventHandler<DebugStoppedEventArgs>? Stopped;

    /// <summary>
    /// Occurs when the CPU resumes execution.
    /// </summary>
    event EventHandler? Resumed;

    /// <summary>
    /// Occurs when contact with the target is lost unexpectedly (not as a result of
    /// <see cref="IAsyncDisposable.DisposeAsync"/>).
    /// </summary>
    event EventHandler<string>? ConnectionLost;

    #endregion

    #region Properties

    /// <summary>
    /// Gets whether this target can report the GOSUB call stack and support Step Out - both need
    /// the 6502 stack pointer, which VICE exposes as a register but the C64 Ultimate's REST API
    /// has no way to read at all (no CPU register access of any kind). When false, the Call
    /// Stack panel stays empty and Step Out stays disabled for the whole session.
    /// </summary>
    bool SupportsCallStackAndStepOut { get; }

    #endregion

    #region Methods

    /// <summary>
    /// Sets a breakpoint that halts execution when the given BASIC line begins executing.
    /// </summary>
    /// <returns>An id identifying the breakpoint, for use with <see cref="RemoveBreakpointAsync"/>.</returns>
    Task<int> SetLineBreakpointAsync(ushort basicLineNumber);

    /// <summary>
    /// Removes a breakpoint previously created with <see cref="SetLineBreakpointAsync"/>.
    /// </summary>
    Task RemoveBreakpointAsync(int breakpointId);

    /// <summary>
    /// Resumes execution after a stop, running until the next breakpoint (or another stop).
    /// </summary>
    Task ContinueAsync();

    /// <summary>
    /// Arms a trap that halts execution at the start of the next BASIC line, without resuming -
    /// call this while the program is already running (e.g. right after Continue/Start), since
    /// there's nothing to resume yet.
    /// </summary>
    Task PauseAsync();

    /// <summary>
    /// Executes from the current stop point until the next BASIC line begins, then stops again -
    /// call this only while already stopped, since it resumes execution itself.
    /// </summary>
    Task StepLineAsync();

    /// <summary>
    /// Runs until execution returns from the innermost GOSUB or FOR loop active at the current
    /// stop point, then stops again. Only valid when <see cref="SupportsCallStackAndStepOut"/> is
    /// true. Call this only while already stopped, since it resumes execution itself.
    /// </summary>
    Task StepOutAsync();

    /// <summary>
    /// Reads the BASIC line number currently executing (CURLIN, $39-$3A).
    /// </summary>
    Task<ushort> ReadCurlinAsync();

    /// <summary>
    /// Reads the 6502 stack pointer (SP), used to walk the GOSUB call stack from $0100+SP. Only
    /// valid when <see cref="SupportsCallStackAndStepOut"/> is true.
    /// </summary>
    Task<byte> ReadStackPointerAsync();

    /// <summary>
    /// Reads raw bytes from the target's memory.
    /// </summary>
    Task<byte[]> ReadMemoryAsync(ushort startAddress, int length);

    /// <summary>
    /// Writes raw bytes to the target's memory.
    /// </summary>
    Task WriteMemoryAsync(ushort startAddress, byte[] data);

    #endregion
}

/// <summary>
/// Describes why and where an <see cref="IDebugSession"/> stopped. <see cref="CheckpointNumber"/>
/// is non-null exactly when the stop was recognized as a breakpoint hit; despite the name (kept
/// for API stability from when VICE tracked real per-breakpoint checkpoint numbers), it now
/// carries the BASIC line number for both targets, since neither one tracks per-breakpoint ids
/// server-side anymore (VICE uses a single shared checkpoint; the C64U stub tracks only one
/// active breakpoint line at a time - see <see cref="ReadyCode.C64U.C64UDebugSession"/>).
/// </summary>
public sealed record DebugStoppedEventArgs(ushort ProgramCounter, ushort Curlin, int? CheckpointNumber);
