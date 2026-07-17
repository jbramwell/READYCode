// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace ReadyCode.Vice;

/// <summary>
/// Client for running programs on the VICE emulator via its binary monitor interface,
/// reusing an already-running instance instead of opening a new emulator window each time.
/// </summary>
public class ViceClient
{
    #region Private Fields

    private const byte _apiVersion = 0x02;
    private const byte _autostartCommand = 0xdd;
    private const byte _advanceInstructionsCommand = 0x71;
    private const byte _exitCommand = 0xaa;
    private const byte _quitCommand = 0xbb;
    private const byte _resetCommand = 0xcc;
    private const byte _infoCommand = 0x85;
    private const uint _requestId = 1;
    private const int _swRestore = 9;

    // Holds a binary monitor connection across a Pause/Resume cycle. VICE has no dedicated
    // pause command; stepping a single instruction forces the CPU into the monitor's stopped
    // state, but that state only persists while the connection stays open, so it must be kept
    // alive here instead of being closed at the end of the call like every other command.
    private static TcpClient? _pausedClient;
    private static NetworkStream? _pausedStream;

    private readonly string _monitorHost;
    private readonly int _monitorPort;

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="ViceClient"/> class targeting the given
    /// binary monitor address.
    /// </summary>
    /// <param name="monitorHost">Host the VICE binary monitor listens on.</param>
    /// <param name="monitorPort">Port the VICE binary monitor listens on.</param>
    public ViceClient(string monitorHost, int monitorPort)
    {
        _monitorHost = monitorHost;
        _monitorPort = monitorPort;
    }

    #endregion

    #region Public Methods

    /// <summary>
    /// Loads a .prg program into VICE without running it.
    /// </summary>
    /// <param name="emulatorPath">Full path to the VICE emulator executable (e.g. x64sc.exe).</param>
    /// <param name="prgData">The PRG-format program data to transfer.</param>
    /// <param name="programName">
    /// The name shown by VICE's LOAD prompt while transferring, typically the open tab's
    /// file name (with or without extension).
    /// </param>
    /// <param name="bringToForeground">Whether to bring the VICE window to the foreground afterward.</param>
    public async Task TransferAsync(string emulatorPath, byte[] prgData, string programName, bool bringToForeground)
    {
        string prgFile = WritePrgToTempFile(prgData, programName);
        await SendAutostartAsync(emulatorPath, prgFile, runAfterLoading: false, bringToForeground);
    }

    /// <summary>
    /// Loads a .prg program into VICE and runs it immediately.
    /// </summary>
    /// <param name="emulatorPath">Full path to the VICE emulator executable (e.g. x64sc.exe).</param>
    /// <param name="prgData">The PRG-format program data to run.</param>
    /// <param name="programName">
    /// The name shown by VICE's LOAD prompt while transferring, typically the open tab's
    /// file name (with or without extension).
    /// </param>
    /// <param name="bringToForeground">Whether to bring the VICE window to the foreground afterward.</param>
    public async Task RunAsync(string emulatorPath, byte[] prgData, string programName, bool bringToForeground)
    {
        string prgFile = WritePrgToTempFile(prgData, programName);
        await SendAutostartAsync(emulatorPath, prgFile, runAfterLoading: true, bringToForeground);
    }

    /// <summary>
    /// Performs a soft reset of the machine currently running in VICE.
    /// </summary>
    /// <param name="emulatorPath">Full path to the VICE emulator executable (e.g. x64sc.exe).</param>
    public async Task ResetAsync(string emulatorPath)
    {
        await RequireViceRunningAsync();
        await SendOneShotCommandAsync(BuildRequest(_resetCommand, new byte[] { 0x00 }));
    }

    /// <summary>
    /// Performs a hard reset (power cycle) of the machine currently running in VICE.
    /// </summary>
    /// <param name="emulatorPath">Full path to the VICE emulator executable (e.g. x64sc.exe).</param>
    public async Task RebootAsync(string emulatorPath)
    {
        await RequireViceRunningAsync();
        await SendOneShotCommandAsync(BuildRequest(_resetCommand, new byte[] { 0x01 }));
    }

    /// <summary>
    /// Quits the running VICE process, closing the emulator window. VICE has no separate
    /// "power" state, so quitting is the closest equivalent to powering off the machine.
    /// </summary>
    /// <param name="emulatorPath">Full path to the VICE emulator executable (e.g. x64sc.exe).</param>
    public async Task PowerOffAsync(string emulatorPath)
    {
        await RequireViceRunningAsync();
        await SendOneShotCommandAsync(BuildRequest(_quitCommand, Array.Empty<byte>()));
        ClearPausedConnection();
    }

    /// <summary>
    /// Pauses the machine currently running in VICE by stepping a single instruction and
    /// holding the binary monitor connection open, keeping the CPU in the stopped state
    /// until <see cref="ResumeAsync"/> is called.
    /// </summary>
    /// <param name="emulatorPath">Full path to the VICE emulator executable (e.g. x64sc.exe).</param>
    public async Task PauseAsync(string emulatorPath)
    {
        await RequireViceRunningAsync();

        if (_pausedClient is not null)
            throw new InvalidOperationException("VICE is already paused.");

        var client = new TcpClient();
        await client.ConnectAsync(_monitorHost, _monitorPort);
        var stream = client.GetStream();

        byte[] request = BuildRequest(_advanceInstructionsCommand, new byte[] { 0x00, 0x01, 0x00 }); // SO=0, IC=1
        await stream.WriteAsync(request);

        byte errorCode = await ReadResponseErrorCodeAsync(stream);
        if (errorCode != 0)
        {
            client.Dispose();
            throw new InvalidOperationException($"VICE rejected the request (binary monitor error code {errorCode}).");
        }

        _pausedClient = client;
        _pausedStream = stream;
    }

    /// <summary>
    /// Retrieves version information from the VICE binary monitor.
    /// </summary>
    public async Task<ViceInfo> GetInfoAsync()
    {
        await RequireViceRunningAsync();

        using var client = new TcpClient();
        await client.ConnectAsync(_monitorHost, _monitorPort);
        using var stream = client.GetStream();

        byte[] request = BuildRequest(_infoCommand, Array.Empty<byte>());
        await stream.WriteAsync(request);

        var (errorCode, body) = await ReadResponseAsync(stream);
        if (errorCode != 0)
            throw new InvalidOperationException($"VICE rejected the request (binary monitor error code {errorCode}).");

        return ParseInfoResponse(body);
    }

    /// <summary>
    /// Resumes a machine previously paused with <see cref="PauseAsync"/>.
    /// </summary>
    public async Task ResumeAsync()
    {
        if (_pausedClient is null || _pausedStream is null)
            throw new InvalidOperationException("VICE is not paused.");

        try
        {
            byte[] request = BuildRequest(_exitCommand, Array.Empty<byte>());
            await _pausedStream.WriteAsync(request);

            byte errorCode = await ReadResponseErrorCodeAsync(_pausedStream);
            if (errorCode != 0)
                throw new InvalidOperationException($"VICE rejected the request (binary monitor error code {errorCode}).");
        }
        finally
        {
            ClearPausedConnection();
        }
    }

    #endregion

    #region Private Methods

    // Sends the binary monitor's "Autostart/Autoload" command (0xdd) to a running VICE
    // instance, starting one first if the monitor isn't already listening.
    private async Task SendAutostartAsync(string emulatorPath, string prgFilePath, bool runAfterLoading, bool bringToForeground)
    {
        await EnsureViceRunningAsync(emulatorPath);

        byte[] fileNameBytes = System.Text.Encoding.ASCII.GetBytes(prgFilePath);
        byte[] body = new byte[4 + fileNameBytes.Length];
        body[0] = runAfterLoading ? (byte)1 : (byte)0; // RL: run after loading?
        // bytes 1-2: FI, file index within the image - always 0 for a standalone .prg
        body[3] = (byte)fileNameBytes.Length;           // FL: filename length
        fileNameBytes.CopyTo(body, 4);                  // FN: filename

        await SendOneShotCommandAsync(BuildRequest(_autostartCommand, body));

        if (bringToForeground)
            BringViceToForeground(emulatorPath);
    }

    // Activates the VICE window so it's visible immediately after a transfer/run, whether it was
    // just launched or an already-running instance was reused (in which case no Process handle
    // from this call is available, so the window is located by the emulator's image name instead).
    private static void BringViceToForeground(string emulatorPath)
    {
        string imageName = Path.GetFileNameWithoutExtension(emulatorPath);
        var process = Process.GetProcessesByName(imageName)
            .FirstOrDefault(p => p.MainWindowHandle != IntPtr.Zero);
        if (process == null) return;

        if (IsIconic(process.MainWindowHandle))
            ShowWindow(process.MainWindowHandle, _swRestore);

        SetForegroundWindow(process.MainWindowHandle);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(IntPtr hWnd);

    // Connects to VICE's binary monitor, sends a prebuilt request, reads the error code,
    // and disposes the connection. Callers are responsible for ensuring VICE is running first.
    private async Task SendOneShotCommandAsync(byte[] request)
    {
        using var client = new TcpClient();
        await client.ConnectAsync(_monitorHost, _monitorPort);
        using var stream = client.GetStream();

        await stream.WriteAsync(request);

        byte errorCode = await ReadResponseErrorCodeAsync(stream);
        if (errorCode != 0)
            throw new InvalidOperationException($"VICE rejected the request (binary monitor error code {errorCode}).");
    }

    // Throws if VICE's binary monitor isn't already reachable. Used by admin commands that
    // operate on an existing instance and should not launch a new one.
    private async Task RequireViceRunningAsync()
    {
        if (!await IsMonitorListeningAsync())
            throw new InvalidOperationException("VICE is not running. Use Transfer or Run to start it first.");
    }

    // Disposes and clears any binary monitor connection held open by PauseAsync.
    private static void ClearPausedConnection()
    {
        _pausedClient?.Dispose();
        _pausedClient = null;
        _pausedStream = null;
    }

    // Reuses an already-running VICE instance if its binary monitor is reachable; otherwise
    // launches a new one and waits for the monitor to come online.
    private async Task EnsureViceRunningAsync(string emulatorPath)
    {
        if (await IsMonitorListeningAsync())
            return;

        if (string.IsNullOrWhiteSpace(emulatorPath))
            throw new InvalidOperationException("The VICE emulator path has not been configured. Set it in Settings - Preferences.");

        if (!File.Exists(emulatorPath))
            throw new InvalidOperationException($"The VICE emulator executable was not found at '{emulatorPath}'.");

        Process.Start(new ProcessStartInfo(
            emulatorPath,
            $"-binarymonitor -binarymonitoraddress {_monitorHost}:{_monitorPort}")
        { UseShellExecute = true });

        for (int i = 0; i < 30; i++)
        {
            if (await IsMonitorListeningAsync())
                return;

            await Task.Delay(300);
        }

        throw new InvalidOperationException("Timed out waiting for VICE to start.");
    }

    private async Task<bool> IsMonitorListeningAsync()
    {
        try
        {
            using var client = new TcpClient();
            await client.ConnectAsync(_monitorHost, _monitorPort);
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static byte[] BuildRequest(byte commandId, byte[] body)
    {
        byte[] request = new byte[11 + body.Length];
        request[0] = 0x02;         // STX
        request[1] = _apiVersion;
        BitConverter.GetBytes((uint)body.Length).CopyTo(request, 2);
        BitConverter.GetBytes(_requestId).CopyTo(request, 6);
        request[10] = commandId;
        body.CopyTo(request, 11);

        return request;
    }

    // Reads binary monitor response messages until the direct reply to our request arrives,
    // skipping any unsolicited async events (JAM/STOPPED/RESUMED, sent with request ID 0xffffffff).
    private static async Task<byte> ReadResponseErrorCodeAsync(NetworkStream stream)
    {
        var (errorCode, _) = await ReadResponseAsync(stream);
        return errorCode;
    }

    // Same as ReadResponseErrorCodeAsync, but also returns the response body (needed by
    // commands like VICE info that return data beyond a plain success/failure code).
    private static async Task<(byte ErrorCode, byte[] Body)> ReadResponseAsync(NetworkStream stream)
    {
        for (int i = 0; i < 20; i++)
        {
            byte[] header = await ReadExactlyAsync(stream, 12);
            int bodyLength = BitConverter.ToInt32(header, 2);
            byte errorCode = header[7];
            uint requestId = BitConverter.ToUInt32(header, 8);

            byte[] body = await ReadExactlyAsync(stream, bodyLength);

            if (requestId == _requestId)
                return (errorCode, body);
        }

        throw new InvalidOperationException("VICE did not respond to the request.");
    }

    // Parses the VICE info response body: ML | MV[0..ML-1] | SL | SV[0..SL-1], where MV is the
    // version number's components (e.g. 3.5.0.0). The trailing SVN revision (SL/SV) isn't used.
    private static ViceInfo ParseInfoResponse(byte[] body)
    {
        byte versionLength = body[0];
        byte[] versionBytes = body[1..(1 + versionLength)];

        return new ViceInfo
        {
            Version = string.Join(".", versionBytes),
        };
    }

    private static async Task<byte[]> ReadExactlyAsync(NetworkStream stream, int count)
    {
        byte[] buffer = new byte[count];
        int offset = 0;
        while (offset < count)
        {
            int read = await stream.ReadAsync(buffer, offset, count - offset);
            if (read == 0)
                throw new IOException("Connection to VICE was closed unexpectedly.");
            offset += read;
        }

        return buffer;
    }

    // Names the temp file after the given program name (stripped of any extension and
    // sanitized for the filesystem) since VICE's autostart LOAD prompt displays the host
    // file's base name rather than anything embedded in the PRG data itself. Lower-cased
    // because VICE's autostart "types" the name as unshifted keystrokes, which is how it
    // produces uppercase PETSCII on screen - uppercase ASCII input maps to shifted
    // keystrokes instead and renders as graphics symbols.
    private static string WritePrgToTempFile(byte[] prgData, string programName)
    {
        string baseName = Path.GetFileNameWithoutExtension(programName).ToLowerInvariant();
        foreach (char c in Path.GetInvalidFileNameChars())
            baseName = baseName.Replace(c, '_');

        if (string.IsNullOrWhiteSpace(baseName))
            baseName = "readycode";

        string path = Path.Combine(Path.GetTempPath(), $"{baseName}.prg");
        File.WriteAllBytes(path, prgData);
        return path;
    }

    #endregion
}
