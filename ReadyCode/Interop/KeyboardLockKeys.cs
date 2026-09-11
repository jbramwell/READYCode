// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;

namespace ReadyCode.Interop;

/// <summary>
/// Reads and toggles the OS-level Caps Lock key state, for the status bar's lock-key indicator.
/// Reads go straight to the Win32 <c>GetKeyState</c> API rather than WPF's
/// <see cref="System.Windows.Input.Keyboard.IsKeyToggled"/>, which has been unreliable at app
/// startup on some machines. Toggling simulates a real key press via <c>keybd_event</c> so the
/// change is a true system-wide lock-key toggle (matching what pressing the physical key would
/// do), not just a visual flag local to this app.
/// </summary>
public static class KeyboardLockKeys
{
    private const int _vkCapital = 0x14;
    private const uint _keyEventFExtendedKey = 0x1;
    private const uint _keyEventFKeyUp = 0x2;
    private const uint _mapVkVkToVsc = 0;

    /// <summary>Gets whether Caps Lock is currently toggled on.</summary>
    public static bool IsCapsLockOn => IsToggled(_vkCapital);

    /// <summary>Toggles the system Caps Lock state, as if the physical key were pressed.</summary>
    public static void ToggleCapsLock() => SimulateKeyPress(_vkCapital);

    // The low-order bit of GetKeyState's return value is the key's toggle state - reliable
    // regardless of window focus/activation timing, unlike WPF's Keyboard.IsKeyToggled.
    private static bool IsToggled(int virtualKey) => (GetKeyState(virtualKey) & 1) != 0;

    private static void SimulateKeyPress(int virtualKey)
    {
        byte vk = (byte)virtualKey;
        byte scanCode = (byte)MapVirtualKey((uint)virtualKey, _mapVkVkToVsc);
        keybd_event(vk, scanCode, _keyEventFExtendedKey, UIntPtr.Zero);
        keybd_event(vk, scanCode, _keyEventFExtendedKey | _keyEventFKeyUp, UIntPtr.Zero);
    }

    [DllImport("user32.dll")]
    private static extern void keybd_event(byte bVk, byte bScan, uint dwFlags, UIntPtr dwExtraInfo);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern short GetKeyState(int nVirtKey);
}
