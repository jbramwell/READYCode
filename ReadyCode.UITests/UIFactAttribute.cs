// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using Xunit;

// One app instance at a time: the tests send real keystrokes to whichever window is in front.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ReadyCode.UITests;

/// <summary>
/// Marks a UI smoke test. These launch the real app and take over the keyboard for a few
/// seconds, so they're skipped unless the <see cref="EnableVariable"/> environment variable is
/// "1" - which run-ui-tests.ps1 sets. A plain "Run All" or CI still compiles them but shows them
/// as skipped.
/// </summary>
public sealed class UIFactAttribute : FactAttribute
{
    #region Public Fields

    /// <summary>The environment variable that enables UI smoke tests.</summary>
    public const string EnableVariable = "READYCODE_UI_TESTS";

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="UIFactAttribute"/> class.
    /// </summary>
    public UIFactAttribute()
    {
        if (Environment.GetEnvironmentVariable(EnableVariable) != "1")
            Skip = $"UI smoke test - launches the app. Run with run-ui-tests.ps1 (or set {EnableVariable}=1).";
    }

    #endregion
}
