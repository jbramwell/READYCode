// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.IO;
using System.Windows;
using System.Windows.Documents;

namespace ReadyCode.Views;

/// <summary>
/// Dialog showing the third-party font license text used by the application.
/// </summary>
public partial class LicensesWindow : Window
{
    #region Private Fields

    // Lines rendered in bold within the license text - the font name heading and the license title block.
    private static readonly HashSet<string> _boldLines = new(StringComparer.Ordinal)
    {
        "\"Pet Me 64\" font",
        "KREATIVE SOFTWARE RELAY FONTS FREE USE LICENSE",
        "version 1.2f"
    };

    #endregion

    #region Constructors

    /// <summary>
    /// Initializes a new instance of the <see cref="LicensesWindow"/> class.
    /// </summary>
    public LicensesWindow()
    {
        InitializeComponent();
        PopulateLicenseText();
    }

    #endregion

    #region Private Methods

    private static string BuildLicenseText()
    {
        string header = "\"Pet Me 64\" font" + Environment.NewLine + Environment.NewLine;
        string licensePath = Path.Combine(AppContext.BaseDirectory, "Assets", "Fonts", "LICENSE-PetMe64.txt");
        string license = File.Exists(licensePath)
            ? File.ReadAllText(licensePath)
            : "License file not found.";

        return header + license;
    }

    private void PopulateLicenseText()
    {
        string[] lines = BuildLicenseText().Replace("\r\n", "\n").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) LicenseText.Inlines.Add(new LineBreak());
            var run = new Run(lines[i]);
            if (_boldLines.Contains(lines[i]))
            {
                // Weight alone is subtle on a small monospace font; pairing it with a size
                // bump makes the heading lines unmistakable rather than barely perceptible.
                run.FontWeight = FontWeights.Bold;
                run.FontSize = LicenseText.FontSize + 2;
            }
            LicenseText.Inlines.Add(run);
        }
    }

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    #endregion
}
