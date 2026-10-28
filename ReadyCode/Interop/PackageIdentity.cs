// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Runtime.InteropServices;
using System.Text;

namespace ReadyCode.Interop;

/// <summary>
/// Reports whether the app is running as a packaged (MSIX - e.g. Microsoft Store) app, as
/// opposed to an MSI install or a development build, via the Win32
/// <c>GetCurrentPackageFullName</c> API.
/// </summary>
public static class PackageIdentity
{
    #region Private Fields

    // Returned by GetCurrentPackageFullName when the process has no package identity.
    private const int _appModelErrorNoPackage = 15700;

    #endregion

    #region Public Properties

    /// <summary>Gets whether the app is running with an MSIX package identity.</summary>
    public static bool IsPackaged
    {
        get
        {
            // Asking for the length only: a packaged process answers "buffer too small", an
            // unpackaged one APPMODEL_ERROR_NO_PACKAGE.
            int length = 0;
            return GetCurrentPackageFullName(ref length, null) != _appModelErrorNoPackage;
        }
    }

    #endregion

    #region Private Methods

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetCurrentPackageFullName(ref int packageFullNameLength, StringBuilder? packageFullName);

    #endregion
}
