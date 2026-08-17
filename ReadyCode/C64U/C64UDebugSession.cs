// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Linq;
using ReadyCode.Debugger;

namespace ReadyCode.C64U;

/// <summary>
/// A live BASIC line-level debug session against a real C64 Ultimate, implemented entirely
/// through its REST API (no native breakpoint/memory-watch capability exists there, unlike
/// VICE's binary monitor) by injecting <see cref="C64UDebugStub"/> into the running machine and
/// polling its status byte every 250ms - see the debugger spec's C64 Ultimate section and
/// <see cref="C64UDebugStub"/>'s own remarks for the stub's design.
///
/// Every enabled breakpoint is pushed to the stub's own breakpoint table (up to
/// <see cref="C64UDebugStub.MaxBreakpoints"/> of them - the stub checks each one on every GONE
/// call) whenever the set changes, so - unlike an earlier version of this session, which tracked
/// only a single active line and had to guess which one to re-arm after each hit - every enabled
/// breakpoint stays watched simultaneously, the same as VICE's independent per-breakpoint
/// checkpoints. Breakpoints beyond the table's capacity simply aren't armed on the device; this
/// session doesn't warn about that today.
/// </summary>
public sealed class C64UDebugSession : IDebugSession
{
    #region Private Fields

    private static readonly TimeSpan _pollInterval = TimeSpan.FromMilliseconds(250);

    private readonly string _baseUrl;
    private readonly C64UltimateClient _client;
    private readonly SortedSet<ushort> _breakpointLines = new();

    private readonly ushort _origGoneAddress;
    private readonly ushort _lastLineLoAddress;
    private readonly ushort _breakCountAddress;
    private readonly ushort _breakLinesLoAddress;
    private readonly ushort _breakLinesHiAddress;
    private readonly ushort _stepModeAddress;
    private readonly ushort _haltedFlagAddress;
    private readonly ushort _resumeFlagAddress;
    private readonly ushort _haltCountAddress;

    private readonly byte[] _originalGoneVector;

    private CancellationTokenSource? _pollCts;
    private Task _pollTask = Task.CompletedTask;
    private bool _wasHalted;
    private byte? _lastSeenHaltCount;
    private bool _awaitingLineBoundary;
    private bool _disposed;

    #endregion

    #region Constructors

    private C64UDebugSession(string baseUrl, IReadOnlyDictionary<string, ushort> labels, byte[] originalGoneVector)
    {
        _baseUrl = baseUrl;
        _client = new C64UltimateClient();
        _origGoneAddress = labels["ORIG_GONE_LO"];
        _lastLineLoAddress = labels["LAST_LINE_LO"];
        _breakCountAddress = labels["BREAK_COUNT"];
        _breakLinesLoAddress = labels["BREAK_LINES_LO"];
        _breakLinesHiAddress = labels["BREAK_LINES_HI"];
        _stepModeAddress = labels["STEP_MODE"];
        _haltedFlagAddress = labels["HALTED_FLAG"];
        _resumeFlagAddress = labels["RESUME_FLAG"];
        _haltCountAddress = labels["HALT_COUNT"];
        _originalGoneVector = originalGoneVector;
    }

    #endregion

    #region Public Properties

    /// <summary>
    /// Always false - the C64 Ultimate's REST API has no way to read CPU registers (including the
    /// stack pointer), which both the GOSUB call stack and Step Out depend on.
    /// </summary>
    public bool SupportsCallStackAndStepOut => false;

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
    /// Occurs when polling the device fails unexpectedly (not as a result of
    /// <see cref="DisposeAsync"/>) - e.g. the device becomes unreachable.
    /// </summary>
    public event EventHandler<string>? ConnectionLost;

    #endregion

    #region Public Methods

    /// <summary>
    /// Assembles and uploads the debug stub to a running C64 Ultimate, patches the GONE vector to
    /// hook it, and starts polling for halts.
    /// </summary>
    /// <param name="baseUrl">Base URL of the C64 Ultimate's REST API.</param>
    /// <param name="stubBaseAddress">
    /// The address to upload the stub to. Must not overlap the running program or its variables.
    /// </param>
    public static async Task<C64UDebugSession> StartAsync(string baseUrl, ushort stubBaseAddress = 0xCF00)
    {
        var (stubBytes, labels) = C64UDebugStub.Assemble(stubBaseAddress);
        var client = new C64UltimateClient();

        // Save the live GONE vector before touching anything, so it can both be restored on
        // Stop and be copied into the stub's own ORIG_GONE_LO/HI (which its final JMP indirect
        // reads to hand control back to the real interpreter for every statement it doesn't halt at).
        byte[] originalGoneVector = await client.ReadMemoryAsync(baseUrl, 0x0308, 2);

        await client.WriteMemoryAsync(baseUrl, stubBaseAddress, stubBytes); // verified to fit in one call - see C64UDebugStubTests

        // Read the stub straight back and compare - without this, a write that's silently
        // rejected or overwritten looks identical to success right up until breakpoints
        // mysteriously never fire, with nothing to say why.
        byte[] verifyStub = await client.ReadMemoryAsync(baseUrl, stubBaseAddress, stubBytes.Length);
        if (!verifyStub.AsSpan().SequenceEqual(stubBytes))
        {
            throw new InvalidOperationException(
                $"The debug stub did not read back correctly after being uploaded to ${stubBaseAddress:X4} - " +
                "the write may have been rejected or something overwrote it immediately after.");
        }

        await client.WriteMemoryAsync(baseUrl, labels["ORIG_GONE_LO"], originalGoneVector);

        var session = new C64UDebugSession(baseUrl, labels, originalGoneVector);

        // Patch the vector last, only once the stub and its own copy of the original target are
        // both fully in place - so a GONE call that lands mid-setup never jumps into a half-written stub.
        byte[] vectorBytes = { (byte)(stubBaseAddress & 0xFF), (byte)(stubBaseAddress >> 8) };
        await client.WriteMemoryAsync(baseUrl, 0x0308, vectorBytes);

        byte[] verifyVector = await client.ReadMemoryAsync(baseUrl, 0x0308, 2);
        if (!verifyVector.AsSpan().SequenceEqual(vectorBytes))
        {
            throw new InvalidOperationException(
                $"The GONE vector ($0308-$0309) read back as ${verifyVector[0]:X2}{verifyVector[1]:X2} " +
                $"instead of the ${vectorBytes[0]:X2}{vectorBytes[1]:X2} just written to it - something is " +
                "resetting or overwriting it.");
        }

        session._pollCts = new CancellationTokenSource();
        session._pollTask = Task.Run(() => session.RunPollLoopAsync(session._pollCts.Token));
        return session;
    }

    /// <summary>
    /// Sets a breakpoint that halts execution when the given BASIC line begins executing. Takes
    /// effect immediately on the device, whether or not a program is currently running - this is
    /// what arms the very first breakpoints before Start ever types RUN, since nothing else does.
    /// </summary>
    /// <returns>An id identifying the breakpoint (the BASIC line number itself), for use with <see cref="RemoveBreakpointAsync"/>.</returns>
    public async Task<int> SetLineBreakpointAsync(ushort basicLineNumber)
    {
        _breakpointLines.Add(basicLineNumber);
        await SyncBreakpointsAsync();
        return basicLineNumber;
    }

    /// <summary>
    /// Removes a breakpoint previously created with <see cref="SetLineBreakpointAsync"/>.
    /// </summary>
    public async Task RemoveBreakpointAsync(int breakpointId)
    {
        _breakpointLines.Remove((ushort)breakpointId);
        await SyncBreakpointsAsync();
    }

    /// <summary>
    /// Resumes execution after a stop, running until the next breakpoint (or another stop).
    /// </summary>
    public async Task ContinueAsync()
    {
        await _client.WriteMemoryAsync(_baseUrl, _stepModeAddress, new byte[] { 0x00 });
        await _client.WriteMemoryAsync(_baseUrl, _resumeFlagAddress, new byte[] { 0x01 });
    }

    /// <summary>
    /// Arms a trap that halts execution at the start of the next BASIC line, without resuming -
    /// call this while the program is already running (e.g. right after Continue/Start), since
    /// there's nothing to resume yet.
    /// </summary>
    public async Task PauseAsync()
    {
        _awaitingLineBoundary = true;
        await _client.WriteMemoryAsync(_baseUrl, _stepModeAddress, new byte[] { 0x01 });
        await _client.WriteMemoryAsync(_baseUrl, _resumeFlagAddress, new byte[] { 0x01 });
    }

    /// <summary>
    /// Executes from the current stop point until the next BASIC line begins, then stops again -
    /// call this only while already stopped, since it resumes execution itself.
    /// </summary>
    public Task StepIntoAsync() => PauseAsync(); // identical stub-side behavior - see class remarks

    /// <summary>
    /// Not supported - see <see cref="SupportsCallStackAndStepOut"/>.
    /// </summary>
    public Task StepOverAsync() =>
        throw new NotSupportedException("Step Over isn't available when debugging the C64 Ultimate - skipping past a GOSUB requires reading the 6502 stack pointer, which its REST API has no way to do. Use Step Into instead.");

    /// <summary>
    /// Not supported - see <see cref="SupportsCallStackAndStepOut"/>.
    /// </summary>
    public Task StepOutAsync() =>
        throw new NotSupportedException("Step Out isn't available when debugging the C64 Ultimate - its REST API has no way to read the 6502 stack pointer.");

    /// <summary>
    /// Reads the BASIC line number currently executing (CURLIN, $39-$3A).
    /// </summary>
    public async Task<ushort> ReadCurlinAsync()
    {
        byte[] data = await ReadMemoryAsync(0x39, 2);
        return (ushort)(data[0] | (data[1] << 8)); // CURLIN is stored little-endian
    }

    /// <summary>
    /// Not supported - see <see cref="SupportsCallStackAndStepOut"/>.
    /// </summary>
    public Task<byte> ReadStackPointerAsync() =>
        throw new NotSupportedException("The GOSUB call stack isn't available when debugging the C64 Ultimate - its REST API has no way to read the 6502 stack pointer.");

    /// <summary>
    /// Reads raw bytes from the machine.
    /// </summary>
    public Task<byte[]> ReadMemoryAsync(ushort startAddress, int length) => _client.ReadMemoryAsync(_baseUrl, startAddress, length);

    /// <summary>
    /// Writes raw bytes to the machine.
    /// </summary>
    public Task WriteMemoryAsync(ushort startAddress, byte[] data) => _client.WriteMemoryAsync(_baseUrl, startAddress, data);

    /// <summary>
    /// Stops polling and restores the original GONE vector, detaching from the program without
    /// resetting or otherwise disturbing it - like a normal IDE debugger. If currently halted,
    /// resumes it first (via the stub's own still-valid saved copy of the original vector) so the
    /// program doesn't stay frozen forever once nothing is left to send it a resume.
    /// </summary>
    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        if (_pollCts != null)
        {
            await _pollCts.CancelAsync();
            try { await _pollTask; } catch { /* cancellation is expected here */ }
            _pollCts.Dispose();
        }

        try { await _client.WriteMemoryAsync(_baseUrl, _resumeFlagAddress, new byte[] { 0x01 }); }
        catch { /* best-effort - if it wasn't halted this is a harmless no-op anyway */ }

        try { await _client.WriteMemoryAsync(_baseUrl, 0x0308, _originalGoneVector); }
        catch { /* best-effort cleanup - the device may already be unreachable */ }
    }

    #endregion

    #region Private Methods

    // Pushes the full enabled-breakpoint set to the stub's table - called whenever it changes, so
    // there's no "which one line to arm next" guessing at all (see class remarks); every enabled
    // breakpoint is watched simultaneously, up to C64UDebugStub.MaxBreakpoints of them. Writes the
    // line-number arrays before the count, so a GONE call landing mid-update only ever examines
    // entries the previous, still-valid count already covers - a momentary miss on one specific
    // call during the brief write window is the worst case, self-correcting on the next one.
    private async Task SyncBreakpointsAsync()
    {
        ushort[] lines = _breakpointLines.Take(C64UDebugStub.MaxBreakpoints).ToArray();
        byte[] lowBytes = lines.Select(line => (byte)(line & 0xFF)).ToArray();
        byte[] highBytes = lines.Select(line => (byte)(line >> 8)).ToArray();

        if (lines.Length > 0)
        {
            await _client.WriteMemoryAsync(_baseUrl, _breakLinesLoAddress, lowBytes);
            await _client.WriteMemoryAsync(_baseUrl, _breakLinesHiAddress, highBytes);
        }

        await _client.WriteMemoryAsync(_baseUrl, _breakCountAddress, new[] { (byte)lines.Length });
    }

    private async Task RunPollLoopAsync(CancellationToken token)
    {
        try
        {
            while (!token.IsCancellationRequested)
            {
                await Task.Delay(_pollInterval, token);

                byte[] haltedFlag = await _client.ReadMemoryAsync(_baseUrl, _haltedFlagAddress, 1);
                byte[] haltCountBytes = await _client.ReadMemoryAsync(_baseUrl, _haltCountAddress, 1);
                bool halted = haltedFlag[0] == 0xFF;
                byte haltCount = haltCountBytes[0];

                // HALT_COUNT is bumped on every halt, even one whose entire halted period falls
                // between two poll ticks - a Step's halt-resume-rehalt cycle routinely completes
                // in well under 250ms on real hardware, so HALTED_FLAG alone can look unchanged
                // (true before this poll, true again now) across the exact halt this poll exists
                // to catch. Without the counter, that step's Stopped event - and with it the
                // editor's current-line highlight - never fires at all.
                bool isNewHalt = _lastSeenHaltCount.HasValue && haltCount != _lastSeenHaltCount.Value;
                _lastSeenHaltCount = haltCount;

                if (isNewHalt)
                {
                    _wasHalted = true;
                    await HandleHaltedAsync();
                }
                else if (halted && !_wasHalted)
                {
                    _wasHalted = true;
                    await HandleHaltedAsync();
                }
                else if (!halted && _wasHalted)
                {
                    _wasHalted = false;
                    Resumed?.Invoke(this, EventArgs.Empty);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Normal shutdown via DisposeAsync.
        }
        catch (Exception ex)
        {
            if (!_disposed)
                ConnectionLost?.Invoke(this, ex.Message);
        }
    }

    private async Task HandleHaltedAsync()
    {
        byte[] lastLine = await ReadMemoryAsync(_lastLineLoAddress, 2);
        ushort curlin = (ushort)(lastLine[0] | (lastLine[1] << 8));

        bool wasAwaitingLineBoundary = _awaitingLineBoundary;
        _awaitingLineBoundary = false;

        bool isBreakpointLine = _breakpointLines.Contains(curlin);
        // If we weren't specifically waiting for the next line boundary (Pause/Step), the only
        // way the stub would have halted at all is STEP_MODE being 0 and CURLIN matching one of
        // the armed breakpoint lines - always a genuine breakpoint hit in that case.
        int? checkpointNumber = wasAwaitingLineBoundary ? (isBreakpointLine ? curlin : null) : curlin;

        Stopped?.Invoke(this, new DebugStoppedEventArgs(0, curlin, checkpointNumber));
    }

    #endregion
}
