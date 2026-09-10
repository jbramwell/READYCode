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
    private uint? _masterCheckpointNumber;

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
    public Task ContinueAsync() => SendExpectingSuccessAsync(ViceBinaryMonitorProtocol.ExitCommand, Array.Empty<byte>());

    /// <summary>
    /// Types text into the keyboard buffer over this session's own connection - used instead of
    /// <see cref="ViceClient.TypeAsync"/>'s separate one-shot connection once a session already
    /// exists, since a second connection contending with this one while a checkpoint is active
    /// risks exactly the kind of cross-connection stall that motivated this method's existence.
    /// </summary>
    public Task TypeAsync(string text) =>
        SendExpectingSuccessAsync(ViceBinaryMonitorProtocol.KeyboardFeedCommand, ViceBinaryMonitorProtocol.BuildKeyboardFeedRequest(text));

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

        _writeLock.Dispose();
    }

    #endregion

    #region Private Methods

    private async Task<IReadOnlyDictionary<string, byte>> GetRegisterIdsAsync()
    {
        if (_registerIds != null) return _registerIds;

        byte[] responseBody = await SendExpectingSuccessAsync(ViceBinaryMonitorProtocol.RegistersAvailableCommand,
            ViceBinaryMonitorProtocol.BuildRegistersAvailableRequest());

        _registerIds = ViceBinaryMonitorProtocol.ParseRegistersAvailableResponse(responseBody);
        return _registerIds;
    }

    // Creates the single store checkpoint every breakpoint/Pause/Step relies on, the first time
    // any of them is used - a session that's never asked to stop at anything never pays even the
    // once-per-line round-trip cost at all.
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

    // The checkpoint fires exactly once per BASIC line dispatched (see class remarks), so every
    // hit here is a genuine line boundary - no same-line/mid-statement filtering needed.
    private async Task HandleStoppedAsync(ushort programCounter)
    {
        try
        {
            ushort curlin = await ((IDebugSession)this).ReadCurlinAsync();
            bool isBreakpointLine = _breakpointLines.ContainsKey(curlin);

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
                Stopped?.Invoke(this, new DebugStoppedEventArgs(programCounter, curlin, isBreakpointLine ? curlin : null));
                return;
            }

            if (_awaitingLineBoundary)
            {
                _awaitingLineBoundary = false;
                Stopped?.Invoke(this, new DebugStoppedEventArgs(programCounter, curlin, isBreakpointLine ? curlin : null));
                return;
            }

            if (!isBreakpointLine)
            {
                await ContinueAsync();
                return;
            }

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
