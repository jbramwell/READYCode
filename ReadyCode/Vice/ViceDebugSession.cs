// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Collections.Concurrent;
using System.Net.Sockets;
using ReadyCode.Debugger;

namespace ReadyCode.Vice;

/// <summary>
/// A live BASIC line-level debug session against a running VICE instance, holding one binary
/// monitor connection open for the session's whole lifetime so unsolicited stop/resume events can
/// be observed - unlike every <see cref="ViceClient"/> method, which opens and closes a fresh
/// connection per call.
///
/// Breakpoints are implemented as a single VICE "store" checkpoint watching writes to CURLIN's
/// high byte ($3A), filtered client-side by reading CURLIN on every hit and comparing it against
/// the active breakpoint lines. Per the confirmed C64 BASIC ROM disassembly (interpreter inner
/// loop around $A7AE-$A809), BASIC writes CURLIN ($39 then $3A, in that order) only on the "new
/// line" path - never for a `:`-separated statement continuing the same line - so this fires
/// exactly once per BASIC line actually dispatched, at exactly the granularity the debugger wants,
/// with no client-side same-line filtering needed. Watching the high byte specifically (rather
/// than the low byte, or an address range covering both) matters because it's written second:
/// by the time this fires, both bytes are guaranteed to hold the new line number already.
///
/// Two earlier designs were tried and abandoned after live testing:
/// - One VICE exec checkpoint per breakpoint at the GONE vector ($A7E4, runs before every
///   statement), each with a server-side `CONDITION_SET` comparing CURLIN to that breakpoint's
///   line - never independently verified against a live VICE instance, and it hung the whole
///   emulator: the checkpoint fired correctly on every GONE call, including ones during VICE's
///   own autostart-typed "LOAD"/"RUN" direct-mode commands (CURLIN = $FFFF there), but the
///   condition never filtered those out, so the CPU halted before the user's program even
///   started, with nothing left to ever resume it.
/// - A single unconditional exec checkpoint at GONE, filtered client-side exactly like this one -
///   correctness-wise sound (and the fallback this design's predecessor), but GONE fires on every
///   *statement*, not every line, meaning a busy multi-statement program round-trips to VICE many
///   times more often than necessary and became visibly unstable under that load. The CURLIN-write
///   checkpoint used here fires at the coarser, actually-wanted granularity instead.
/// </summary>
public sealed class ViceDebugSession : IDebugSession
{
    #region Private Fields

    // CURLIN's high byte ($39 = low, $3A = high) - written second by the BASIC interpreter's
    // "new line" path, so a store breakpoint here only ever fires once both bytes hold the new
    // line number.
    private const ushort _curlinHighByteAddress = 0x3A;

    // The program counter immediately after BASIC's MAIN routine writes $FF to $3A when
    // returning to direct mode (the STX $3A confirmed at ~$A490-$A492 in the ROM disassembly - see
    // HandleStoppedAsync's remarks; a 2-byte STX zp instruction there puts the next PC at $A494,
    // matching this range with room either side for ROM revision drift). Zero page $3A is not
    // exclusive to CURLIN - other KERNAL routines reuse the same byte as scratch space for
    // entirely unrelated purposes, and this checkpoint (a store watch with no way to filter by
    // *which* code is doing the writing) fires for those too - confirmed against a live VICE
    // instance at pc=$FD77-$FD79 (KERNAL reset/RAM-clear, immediately after the autostart
    // transfer's machine reset) and pc=$EE1E/$EE5A (moments after typing into the keyboard buffer,
    // before BASIC has even begun processing it). Both looked, from curlin alone, indistinguishable
    // from a genuine return to direct mode - one even left curlin holding the exact same $FF00
    // sentinel value already there from a real prior MAIN hit, since nothing had touched the byte
    // since. Gating on the program counter landing in this narrow, empirically-confirmed range as
    // well - not just curlin's value - is what actually tells a real "back at READY" apart from
    // incidental noise elsewhere in ROM.
    private const ushort _mainReturnToReadyPcMin = 0xA490;
    private const ushort _mainReturnToReadyPcMax = 0xA49F;

    // Without a timeout, a command VICE never replies to - stuck processing something, or the
    // connection wedged for any other reason - leaves this session (and, since VICE processes
    // monitor commands on its main thread, the entire VICE UI) hung forever, with no way to
    // recover except killing the process. Every request-response round trip is bounded by this
    // instead, so a stuck command surfaces as a clear, catchable error.
    private static readonly TimeSpan _commandTimeout = TimeSpan.FromSeconds(5);

    private readonly TcpClient _client;
    private readonly NetworkStream _stream;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<uint, TaskCompletionSource<(byte ErrorCode, byte[] Body)>> _pendingRequests = new();
    private readonly ConcurrentDictionary<ushort, byte> _breakpointLines = new(); // BASIC line numbers with an active breakpoint

    private long _nextRequestId;
    private Task _readLoopTask = Task.CompletedTask;
    private Task _curlinPollTask = Task.CompletedTask;
    private uint? _masterCheckpointNumber;

    // Set explicitly by MarkRunTyped, called right before "RUN" is typed into the keyboard buffer
    // (see MainViewModel.DebugStartOnViceAsync) - NOT inferred from checkpoint hits, because the
    // master checkpoint is armed (see StartAsync) while VICE's autostart transfer is still
    // settling from the machine reset it triggers, and that window produces its own incidental
    // writes to the exact byte this checkpoint watches: one at curlin=$0000 from still-resetting
    // KERNAL code (nowhere near BASIC's interpreter - pc was deep in KERNAL ROM), and another from
    // the autostart-typed LOAD command itself returning to direct mode (curlin high byte set to
    // $FF, same sentinel a genuinely finished program produces - see HandleStoppedAsync). Trying to
    // infer "the real program has started" from curlin's value treated that first incidental hit as
    // proof of a genuine line executing, which then made the LOAD-finishing hit look exactly like
    // "the debugged program already finished." Gating on this explicit flag instead sidesteps that
    // specific race entirely, regardless of what curlin happens to read during that startup window.
    private bool _runTyped;

    // Counts direct-mode-sentinel hits (see HandleStoppedAsync) observed after MarkRunTyped, to
    // sidestep a second, similarly-timed race: typing "RUN" itself makes BASIC's MAIN routine pass
    // through the exact same return-to-direct-mode write ONE MORE TIME, landing within roughly
    // 25-110ms of the keystroke, before the program's actual first line ever dispatches - as part
    // of processing the typed "RUN" command, before it locates and jumps to the program. That
    // pass-through is indistinguishable from a genuinely finished program by curlin or program
    // counter alone (both read identically to the real thing - see _mainReturnToReadyPcMin's
    // remarks), so only the second and later hits after MarkRunTyped are treated as the program
    // having actually finished; the first is assumed to be RUN's own pass-through and just resumed,
    // the same way the pre-RUN LOAD-finishing hit already is.
    private int _directModeReturnsSinceRunTyped;

    // Backstop for program-end detection: BASIC reaching READY through some end paths (confirmed
    // live for a machine-code program that re-enters BASIC without going through the normal CURLIN
    // reset) never writes to $3A at all, meaning _directModeReturnsSinceRunTyped's checkpoint-based
    // detection above can miss a genuine end entirely, not just mistime it. RunCurlinPollLoopAsync
    // polls CURLIN directly as a fallback, independent of the checkpoint, and needs to agree with
    // the checkpoint-based path on whether the CPU is presumed to be running right now - true once
    // RUN is typed or Continue succeeds, false once any Stopped is reported (by either path) - both
    // to know when polling is even worth doing and to arbitrate which path gets to report a given
    // stop via TryClaimStopped.
    private volatile bool _isRunning;
    private readonly object _stopClaimLock = new();

    // Set for the duration of RunCurlinPollLoopAsync's own CURLIN read - a plain memory read while
    // the CPU is running still makes VICE briefly halt (to safely read memory) and notify this
    // connection of a Stopped/Resumed pair (confirmed live: every poll produced exactly one, timed
    // to the millisecond) - indistinguishable from a genuine checkpoint hit by pc or curlin alone.
    // DispatchUnsolicitedEvent checks this to skip routing that specific Stopped notification
    // through HandleStoppedAsync's checkpoint-hit heuristics, since the poll loop already reads and
    // judges the same CURLIN value directly, and resumes the CPU itself when it isn't the end.
    private volatile bool _pollReadInProgress;

    // The program counter DispatchUnsolicitedEvent captured from the Stopped notification produced
    // by RunCurlinPollLoopAsync's own read (see _pollReadInProgress) - null if the read didn't
    // happen to trigger one (the CPU may already have been paused for some other reason at that
    // exact instant). Since that pc is effectively a random sample of wherever the CPU was at the
    // moment of the read, it doubles as a way to tell whether the machine is genuinely idle: while
    // BASIC's CURLIN write (watched by the checkpoint) turned out NOT to happen on every return-to-
    // READY path - confirmed live for a machine-code program that re-enters BASIC without going
    // through the normal CURLIN reset, leaving CURLIN frozen at its last real value forever even
    // with the machine genuinely idle at READY - a random sample landing repeatedly in the KERNAL's
    // keyboard/cursor idle loop ($E5CD-$E5D4, confirmed live across every idle sample taken so far)
    // is a direct, CURLIN-independent signal that the CPU has nothing left to do but wait for input.
    private ushort? _lastPollInducedPc;

    // The empirically-observed program counter range for the KERNAL's idle-at-READY loop (see
    // _lastPollInducedPc's remarks) - not from ROM disassembly like _mainReturnToReadyPcMin, since
    // this is a courser, purely empirical signal meant as a backstop, not a precise instruction
    // address. Widened a little past the exact observed samples ($E5CD, $E5CF, $E5D1, $E5D4) for
    // margin against minor sampling variation within the same tight loop.
    private const ushort _idleAtReadyPcMin = 0xE5C0;
    private const ushort _idleAtReadyPcMax = 0xE5E0;

    // Set by Pause/StepLine: the very next line-boundary hit should be reported regardless of
    // whether it's a known breakpoint line.
    private bool _awaitingLineBoundary;

    // Set by StepOut: the 6502 stack pointer (SP) at the moment it was requested. The 6502 stack
    // grows downward - pushing decrements SP, popping increments it - and BASIC pushes a frame
    // onto this same hardware stack for both GOSUB and FOR (see GosubStackParser's remarks), so
    // comparing SP against this baseline at each line boundary tells us when we've popped back
    // out of whatever was on top when StepOut was invoked, without needing to parse either kind
    // of frame's actual layout (in particular sidestepping the FOR frame's size, which - unlike
    // GOSUB's - was never verified against a live system; see GosubStackParser).
    private byte? _stepOutStartStackPointer;

    // Set by StepOver: the SP at the moment it was requested, i.e. before whatever statement is
    // about to run. If the very next line boundary reached is INSIDE a newly-pushed frame (SP
    // has gone strictly lower than this baseline - the current statement turned out to be a
    // GOSUB), that call is run to completion (same technique as _stepOutStartStackPointer) before
    // reporting the stop, rather than stopping on its first line. If no push happened, the first
    // line boundary reached is already at the same depth, and is reported immediately.
    private byte? _stepOverStartStackPointer;

    private bool _disposed;

    // Cancelled by DisposeAsync so RunCurlinPollLoopAsync's Task.Delay wakes immediately instead
    // of DisposeAsync having to wait out whatever is left of the current 2-second poll interval.
    private readonly CancellationTokenSource _disposeCts = new();

    // Cached on first use - the VICE binary monitor protocol docs explicitly warn register ids
    // aren't guaranteed stable across versions, so these must be looked up per session rather
    // than hardcoded.
    private IReadOnlyDictionary<string, byte>? _registerIds;

    #endregion

    #region Constructors

    private ViceDebugSession(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
        // The machine is already executing (VICE's autostart transfer triggers this before a
        // session even connects) - see _isRunning's remarks.
        _isRunning = true;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Gets every BASIC line number that currently has an active breakpoint.
    /// </summary>
    public IReadOnlyCollection<ushort> BreakpointLines => (IReadOnlyCollection<ushort>)_breakpointLines.Keys;

    /// <summary>
    /// Always true for VICE - the binary monitor exposes registers (including SP) directly.
    /// </summary>
    public bool SupportsCallStackAndStepOut => true;

    /// <summary>
    /// Occurs when the CPU stops at a genuine line boundary - either a user breakpoint, or a
    /// completed Pause/Step Line.
    /// </summary>
    public event EventHandler<DebugStoppedEventArgs>? Stopped;

    /// <summary>
    /// Occurs when the CPU resumes execution.
    /// </summary>
    public event EventHandler? Resumed;

    /// <summary>
    /// Occurs when the connection to VICE is lost unexpectedly (not as a result of
    /// <see cref="DisposeAsync"/>).
    /// </summary>
    public event EventHandler<string>? ConnectionLost;

    #endregion

    #region Public Methods

    /// <summary>
    /// Connects to a running VICE instance's binary monitor and starts a debug session.
    /// </summary>
    public static async Task<ViceDebugSession> StartAsync(string host, int port)
    {
        var client = new TcpClient();
        await client.ConnectAsync(host, port);

        var session = new ViceDebugSession(client);
        session._readLoopTask = Task.Run(session.RunReadLoopAsync);

        // Armed unconditionally here, not left lazy (see EnsureMasterCheckpointAsync's own
        // comment) - a session with zero breakpoints still needs this checkpoint to ever see the
        // one hit that matters to it: CURLIN going to $FFFF when the program returns to READY
        // (END, falling off the end, STOP, or a runtime error - see HandleStoppedAsync). Without
        // it, nothing VICE ever sends distinguishes "still running" from "finished long ago," and
        // the session is stuck looking like it's still debugging forever.
        await session.EnsureMasterCheckpointAsync();

        // Backstop for program-end detection, independent of the checkpoint - see _isRunning's
        // remarks.
        session._curlinPollTask = Task.Run(session.RunCurlinPollLoopAsync);

        return session;
    }

    /// <summary>
    /// Sets a breakpoint that halts execution when the given BASIC line begins executing.
    /// </summary>
    /// <returns>An id identifying the breakpoint (the BASIC line number itself), for use with <see cref="RemoveBreakpointAsync"/>.</returns>
    public async Task<int> SetLineBreakpointAsync(ushort basicLineNumber)
    {
        await EnsureMasterCheckpointAsync();
        _breakpointLines[basicLineNumber] = 0;
        return basicLineNumber;
    }

    /// <summary>
    /// Removes a breakpoint previously created with <see cref="SetLineBreakpointAsync"/>.
    /// </summary>
    public Task RemoveBreakpointAsync(int breakpointId)
    {
        _breakpointLines.TryRemove((ushort)breakpointId, out _);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Resumes execution after a stop, running until the next breakpoint (or another stop).
    /// </summary>
    public async Task ContinueAsync()
    {
        await SendExpectingSuccessAsync(ViceBinaryMonitorProtocol.ExitCommand, Array.Empty<byte>());
        _isRunning = true;
    }

    /// <summary>
    /// Types text into the keyboard buffer over this session's own connection - used instead of
    /// <see cref="ViceClient.TypeAsync"/>'s separate one-shot connection once a session already
    /// exists, since a second connection contending with this one while a checkpoint is active
    /// risks exactly the kind of cross-connection stall that motivated this method's existence.
    /// </summary>
    public Task TypeAsync(string text) =>
        SendExpectingSuccessAsync(ViceBinaryMonitorProtocol.KeyboardFeedCommand, ViceBinaryMonitorProtocol.BuildKeyboardFeedRequest(text));

    /// <summary>
    /// Marks the debugged program as genuinely starting - call this right before typing "RUN" (see
    /// <c>MainViewModel.DebugStartOnViceAsync</c>). Until this is called, a checkpoint hit showing
    /// the direct-mode sentinel (see <see cref="HandleStoppedAsync"/>) is assumed to be leftover
    /// startup noise from the autostart transfer's own machine reset - such as the LOAD command
    /// itself returning to direct mode - and is silently resumed rather than reported as the
    /// program having finished.
    /// </summary>
    public void MarkRunTyped() => _runTyped = true;

    /// <summary>
    /// Arms a trap that halts execution at the start of the next BASIC line, without resuming -
    /// call this while the program is already running (e.g. right after Continue/Start), since
    /// there's nothing to resume yet.
    /// </summary>
    public async Task PauseAsync()
    {
        await EnsureMasterCheckpointAsync();
        _awaitingLineBoundary = true;
    }

    /// <summary>
    /// Executes from the current stop point until the next BASIC line begins, then stops again -
    /// entering a GOSUB called along the way, if any. Call this only while already stopped, since
    /// it resumes execution itself after arming the trap.
    /// </summary>
    public async Task StepIntoAsync()
    {
        await EnsureMasterCheckpointAsync();
        _awaitingLineBoundary = true;
        await ContinueAsync();
    }

    /// <summary>
    /// Executes from the current stop point until the next BASIC line begins, then stops again -
    /// but if that requires entering a GOSUB, runs it to completion first ("step over") instead of
    /// stopping on its first line. Also stops early if an active breakpoint is hit along the way.
    /// Call this only while already stopped, since it resumes execution itself.
    /// </summary>
    public async Task StepOverAsync()
    {
        await EnsureMasterCheckpointAsync();
        _stepOverStartStackPointer = await ReadStackPointerAsync();
        await ContinueAsync();
    }

    /// <summary>
    /// Runs until execution returns from the innermost GOSUB or FOR loop active at the current
    /// stop point, then stops again - "step out." Also stops early if an active breakpoint is
    /// hit first. Call this only while already stopped, since it resumes execution itself.
    /// </summary>
    public async Task StepOutAsync()
    {
        await EnsureMasterCheckpointAsync();
        _stepOutStartStackPointer = await ReadStackPointerAsync();
        await ContinueAsync();
    }

    /// <summary>
    /// Reads the 6502 stack pointer (SP), used to walk the GOSUB call stack from $0100+SP.
    /// </summary>
    public async Task<byte> ReadStackPointerAsync()
    {
        var registerIds = await GetRegisterIdsAsync();
        if (!registerIds.TryGetValue("SP", out byte spId))
            throw new InvalidOperationException("VICE did not report an SP register for this memspace.");

        byte[] responseBody = await SendExpectingSuccessAsync(ViceBinaryMonitorProtocol.RegistersGetCommand,
            ViceBinaryMonitorProtocol.BuildRegistersGetRequest());
        var values = ViceBinaryMonitorProtocol.ParseRegistersGetResponse(responseBody);

        return (byte)values[spId];
    }

    /// <summary>
    /// Reads raw bytes from the machine over this session's own persistent connection.
    /// </summary>
    public async Task<byte[]> ReadMemoryAsync(ushort startAddress, int length)
    {
        int endAddress = startAddress + length - 1;
        if (endAddress > 0xFFFF)
            throw new ArgumentOutOfRangeException(nameof(length), "The requested range extends past $FFFF.");

        byte[] responseBody = await SendExpectingSuccessAsync(ViceBinaryMonitorProtocol.MemoryGetCommand,
            ViceBinaryMonitorProtocol.BuildMemoryGetRequest(startAddress, (ushort)endAddress));

        return ViceBinaryMonitorProtocol.ParseMemoryGetResponse(responseBody);
    }

    /// <summary>
    /// Writes raw bytes to the machine over this session's own persistent connection.
    /// </summary>
    public async Task WriteMemoryAsync(ushort startAddress, byte[] data)
    {
        await SendExpectingSuccessAsync(ViceBinaryMonitorProtocol.MemorySetCommand,
            ViceBinaryMonitorProtocol.BuildMemorySetRequest(startAddress, data));
    }

    /// <summary>
    /// Deletes the checkpoint this session created and closes the connection. Does not reset or
    /// otherwise disturb the running machine - stopping a debug session detaches from the
    /// program rather than killing it, like a normal IDE debugger.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        _disposeCts.Cancel();

        if (_masterCheckpointNumber is { } checkpointNumber)
        {
            try
            {
                await SendExpectingSuccessAsync(ViceBinaryMonitorProtocol.CheckpointDeleteCommand,
                    ViceBinaryMonitorProtocol.BuildCheckpointDeleteRequest(checkpointNumber));
            }
            catch
            {
                // Best-effort cleanup - the connection may already be going away.
            }
        }

        _client.Close();
        FailAllPendingRequests(new ObjectDisposedException(nameof(ViceDebugSession)));

        try { await _readLoopTask; }
        catch { /* the read loop's own exit, once the socket closes, is expected here */ }

        try { await _curlinPollTask; }
        catch { /* the poll loop's own exit, once _disposed is set or the socket closes, is expected here */ }

        _disposeCts.Dispose();
        _writeLock.Dispose();
    }

    #endregion

    #region Private Methods

    // Arbitrates between HandleStoppedAsync's checkpoint-based detection and
    // RunCurlinPollLoopAsync's independent poll-based backstop, both of which can decide the CPU
    // has genuinely stopped (a breakpoint, a completed step, or the program ending) - only the
    // first to call this actually gets to report it, so the two paths can never both fire Stopped
    // for the same stop.
    private bool TryClaimStopped()
    {
        lock (_stopClaimLock)
        {
            if (!_isRunning) return false;
            _isRunning = false;
            return true;
        }
    }

    private async Task<IReadOnlyDictionary<string, byte>> GetRegisterIdsAsync()
    {
        if (_registerIds != null) return _registerIds;

        byte[] responseBody = await SendExpectingSuccessAsync(ViceBinaryMonitorProtocol.RegistersAvailableCommand,
            ViceBinaryMonitorProtocol.BuildRegistersAvailableRequest());

        _registerIds = ViceBinaryMonitorProtocol.ParseRegistersAvailableResponse(responseBody);
        return _registerIds;
    }

    // Creates the single store checkpoint every breakpoint/Pause/Step relies on. Idempotent
    // (returns immediately once already armed) since it's called both eagerly - StartAsync arms
    // it for every session up front, needed to ever detect the program returning to READY even
    // with zero breakpoints set - and lazily, from SetLineBreakpointAsync/PauseAsync/Step*Async,
    // which would otherwise be the first callers to need it on an older session.
    private async Task EnsureMasterCheckpointAsync()
    {
        if (_masterCheckpointNumber.HasValue) return;

        byte[] body = ViceBinaryMonitorProtocol.BuildCheckpointSetRequest(
            _curlinHighByteAddress, _curlinHighByteAddress, stopWhenHit: true, enabled: true,
            cpuOperation: ViceBinaryMonitorProtocol.StoreOperation, temporary: false);

        byte[] responseBody = await SendExpectingSuccessAsync(ViceBinaryMonitorProtocol.CheckpointSetCommand, body);
        CheckpointInfo info = ViceBinaryMonitorProtocol.ParseCheckpointResponse(responseBody);

        _masterCheckpointNumber = info.CheckpointNumber;
    }

    private async Task<byte[]> SendExpectingSuccessAsync(byte commandId, byte[] body)
    {
        var (errorCode, responseBody) = await SendCommandAsync(commandId, body);
        if (errorCode != 0)
            throw new InvalidOperationException($"VICE rejected the request (binary monitor error code {errorCode}).");

        return responseBody;
    }

    private async Task<(byte ErrorCode, byte[] Body)> SendCommandAsync(byte commandId, byte[] body)
    {
        uint requestId = (uint)Interlocked.Increment(ref _nextRequestId);
        var tcs = new TaskCompletionSource<(byte, byte[])>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pendingRequests[requestId] = tcs;

        byte[] request = ViceClient.BuildRequest(requestId, commandId, body);

        await _writeLock.WaitAsync();
        try
        {
            await _stream.WriteAsync(request);
        }
        finally
        {
            _writeLock.Release();
        }

        try
        {
            return await tcs.Task.WaitAsync(_commandTimeout);
        }
        catch (TimeoutException)
        {
            _pendingRequests.TryRemove(requestId, out _);
            throw new TimeoutException(
                $"VICE did not respond to command 0x{commandId:X2} within {_commandTimeout.TotalSeconds:0}s - " +
                "it may not support this command, or has gotten stuck handling it.");
        }
    }

    // Backstop for "the program genuinely ended" detection, independent of the checkpoint - see
    // _isRunning's remarks for why the checkpoint alone isn't sufficient. Runs for the session's
    // whole lifetime, but only actually reads CURLIN while _isRunning and _runTyped are both true
    // (skipped entirely while genuinely stopped at a breakpoint/step, so this never disturbs an
    // intentional pause - and skipped before RUN is typed, since the pre-RUN LOAD-finishing case is
    // already handled by the checkpoint path).
    private async Task RunCurlinPollLoopAsync()
    {
        while (!_disposed)
        {
            try
            {
                await Task.Delay(2000, _disposeCts.Token);
                if (_disposed || !_isRunning || !_runTyped) continue;

                _lastPollInducedPc = null;
                _pollReadInProgress = true;
                ushort curlin;
                try
                {
                    curlin = await ((IDebugSession)this).ReadCurlinAsync();
                }
                finally
                {
                    _pollReadInProgress = false;
                }

                ushort? sampledPc = _lastPollInducedPc;
                bool looksIdleAtReady = (curlin & 0xFF00) == 0xFF00
                    || (sampledPc is { } pc && pc >= _idleAtReadyPcMin && pc <= _idleAtReadyPcMax);

                if (looksIdleAtReady && TryClaimStopped())
                {
                    Stopped?.Invoke(this, new DebugStoppedEventArgs(sampledPc ?? 0, 0xFFFF, null));
                }
                else if (_isRunning)
                {
                    // The read above may have paused the CPU (see _pollReadInProgress's remarks) -
                    // resume it, since this isn't being treated as the program having ended.
                    await ContinueAsync();
                }
            }
            catch
            {
                // The connection is going away (Dispose closed the socket) or _disposeCts was
                // cancelled - either way, nothing left for this loop to do.
                return;
            }
        }
    }

    // Runs for the whole session lifetime, dispatching every reply (matched by request id) to
    // its waiting caller, and every unsolicited event (stopped/resumed, request id 0xffffffff)
    // to the appropriate internal handler.
    private async Task RunReadLoopAsync()
    {
        try
        {
            while (true)
            {
                byte[] header = await ViceClient.ReadExactlyAsync(_stream, 12);
                int bodyLength = BitConverter.ToInt32(header, 2);
                byte responseType = header[6];
                byte errorCode = header[7];
                uint requestId = BitConverter.ToUInt32(header, 8);
                byte[] body = bodyLength > 0 ? await ViceClient.ReadExactlyAsync(_stream, bodyLength) : Array.Empty<byte>();

                if (requestId != 0xFFFFFFFF && _pendingRequests.TryRemove(requestId, out var tcs))
                {
                    tcs.TrySetResult((errorCode, body));
                    continue;
                }

                DispatchUnsolicitedEvent(responseType, body);
            }
        }
        catch (Exception ex)
        {
            FailAllPendingRequests(ex);
            if (!_disposed)
                ConnectionLost?.Invoke(this, ex.Message);
        }
    }

    private void DispatchUnsolicitedEvent(byte responseType, byte[] body)
    {
        switch (responseType)
        {
            case ViceBinaryMonitorProtocol.StoppedResponseType:
                ushort pc = ViceBinaryMonitorProtocol.ParseStoppedEventProgramCounter(body);

                // A plain memory read while the CPU is running still makes VICE briefly halt and
                // send this same notification (see _pollReadInProgress's remarks) - RunCurlinPollLoopAsync
                // already owns judging and resuming from its own read, so this specific hit isn't a
                // genuine checkpoint event and shouldn't run through HandleStoppedAsync's heuristics.
                if (_pollReadInProgress)
                {
                    _lastPollInducedPc = pc;
                    break;
                }

                // Dispatched onto a separate task so the read loop can keep servicing replies -
                // handling a stop issues its own commands (reading CURLIN, possibly resuming)
                // that only this same loop can complete, which would deadlock if awaited inline.
                _ = Task.Run(() => HandleStoppedAsync(pc));
                break;

            case ViceBinaryMonitorProtocol.ResumedResponseType:
                Resumed?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    // The checkpoint fires once per BASIC line genuinely dispatched (see class remarks), but also
    // for any other, unrelated code that happens to write to the same zero-page byte (see
    // _mainReturnToReadyPcMin's remarks) - curlin alone can't tell those apart, since an unrelated
    // write can coincidentally land on a value that looks exactly like a real line number or the
    // direct-mode sentinel.
    private async Task HandleStoppedAsync(ushort programCounter)
    {
        try
        {
            ushort curlin = await ((IDebugSession)this).ReadCurlinAsync();
            bool isBreakpointLine = _breakpointLines.ContainsKey(curlin);
            bool isDirectModeSentinel = (curlin & 0xFF00) == 0xFF00
                && programCounter >= _mainReturnToReadyPcMin && programCounter <= _mainReturnToReadyPcMax;

            if (_stepOutStartStackPointer is { } startStackPointer)
            {
                // Still at or deeper than the frame StepOut was invoked from - a higher SP means
                // at least one push (from that starting point) has since been popped back off.
                byte currentStackPointer = await ReadStackPointerAsync();
                if (currentStackPointer <= startStackPointer && !isBreakpointLine)
                {
                    await ContinueAsync();
                    return;
                }

                _stepOutStartStackPointer = null;
                if (TryClaimStopped())
                    Stopped?.Invoke(this, new DebugStoppedEventArgs(programCounter, curlin, isBreakpointLine ? curlin : null));
                return;
            }

            if (_stepOverStartStackPointer is { } stepOverBaseline)
            {
                // Strictly lower than the baseline means a new frame was pushed reaching this line
                // - the statement StepOver was called for turned out to be a GOSUB, so run it to
                // completion (same mechanism as StepOut) instead of stopping on its first line.
                byte currentStackPointer = await ReadStackPointerAsync();
                if (currentStackPointer < stepOverBaseline && !isBreakpointLine)
                {
                    await ContinueAsync();
                    return;
                }

                _stepOverStartStackPointer = null;
                if (TryClaimStopped())
                    Stopped?.Invoke(this, new DebugStoppedEventArgs(programCounter, curlin, isBreakpointLine ? curlin : null));
                return;
            }

            if (_awaitingLineBoundary)
            {
                _awaitingLineBoundary = false;
                if (TryClaimStopped())
                    Stopped?.Invoke(this, new DebugStoppedEventArgs(programCounter, curlin, isBreakpointLine ? curlin : null));
                return;
            }

            if (!isBreakpointLine)
            {
                // High byte $FF is BASIC's own sentinel for "no program running" (direct/immediate
                // mode) - confirmed against the actual ROM disassembly (MAIN, ~$A490-$A492): it
                // sets ONLY $3A (the byte this checkpoint watches) to $FF when returning to READY,
                // whether from END, falling off the end of the listing, STOP, or a runtime error.
                // It deliberately does NOT also touch $39 (the low byte) - that's left holding
                // whatever the program's last-executed line's low byte happened to be, so
                // comparing the full 16-bit curlin against exactly 0xFFFF only ever matched by
                // coincidence (this was the actual bug: a run with no breakpoints, or one that
                // finished after its last breakpoint, kept looking like it was still debugging
                // forever, since curlin was near-never literally 0xFFFF even though the high byte
                // genuinely was $FF). Reported as the canonical 0xFFFF regardless of the low
                // byte's leftover value, so nothing downstream needs to know about this quirk too.
                //
                // This same sentinel write also happens before "RUN" is even typed - the autostart
                // transfer's own LOAD command returns to direct mode the exact same way (see
                // _runTyped's remarks) - and once more right as "RUN" itself is dispatched, before
                // the program's first real line ever runs (see _directModeReturnsSinceRunTyped's
                // remarks). So it's only ever reported as "the program ended" on the SECOND such
                // hit after MarkRunTyped - the first is assumed to be RUN's own pass-through and
                // just resumed, same as a pre-RUN hit.
                if (isDirectModeSentinel && _runTyped)
                {
                    _directModeReturnsSinceRunTyped++;
                    if (_directModeReturnsSinceRunTyped >= 2)
                    {
                        if (TryClaimStopped())
                            Stopped?.Invoke(this, new DebugStoppedEventArgs(programCounter, 0xFFFF, null));
                        return;
                    }

                    await ContinueAsync();
                    return;
                }

                await ContinueAsync();
                return;
            }

            if (TryClaimStopped())
                Stopped?.Invoke(this, new DebugStoppedEventArgs(programCounter, curlin, curlin));
        }
        catch (Exception ex)
        {
            ConnectionLost?.Invoke(this, ex.Message);
        }
    }

    private void FailAllPendingRequests(Exception ex)
    {
        foreach (uint requestId in _pendingRequests.Keys)
        {
            if (_pendingRequests.TryRemove(requestId, out var tcs))
                tcs.TrySetException(ex);
        }
    }

    #endregion
}
