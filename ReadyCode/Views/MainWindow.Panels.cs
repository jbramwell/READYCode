// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ReadyCode.Assembler;
using ReadyCode.Models;
using ReadyCode.Tokenizer;

namespace ReadyCode.Views;

// Showing, hiding and switching the side bars and the bottom panel.
public partial class MainWindow
{
    #region Side Panels

    private void ExplorerToggle_Click(object sender, RoutedEventArgs e) => ActivateLeftPanel(ExplorerToggle, ExplorerPanel, "Explorer");

    // Deliberately does not auto-connect - opening the tab just shows the "Not connected" state
    // (with its "Connect" button) until the user explicitly asks to connect.
    private void C64UToggle_Click(object sender, RoutedEventArgs e) => ActivateLeftPanel(C64UToggle, C64UPanel, "C64U");

    // All left-panel toggle/panel/settings-key triples. Centralized so adding a new tab only
    // means adding one entry here rather than touching every call site that needs to
    // enumerate, save, or restore which left-panel tab is active.
    private IEnumerable<(ToggleButton Toggle, Grid Panel, string SettingsKey)> LeftPanelToggles => new (ToggleButton, Grid, string)[]
    {
        (ExplorerToggle, ExplorerPanel, "Explorer"),
        (C64UToggle,     C64UPanel,     "C64U"),
        (SearchToggle,   SearchPanel,   "Search"),
    };

    private void ActivateLeftPanel(ToggleButton toggle, Grid panel, string settingsKey)
    {
        if (toggle.IsChecked == true)
        {
            foreach (var (otherToggle, otherPanel, _) in LeftPanelToggles)
            {
                if (ReferenceEquals(otherToggle, toggle)) continue;
                otherToggle.IsChecked = false;
                otherPanel.Visibility = Visibility.Collapsed;
            }
            panel.Visibility = Visibility.Visible;

            if (LeftPanelCol.Width.Value == 0)
            {
                LeftPanelCol.Width = new GridLength(ViewModel.Settings.LeftPanelWidth);
                LeftSplitterCol.Width = new GridLength(4);
            }
            ViewModel.Settings.ActiveLeftPanel = settingsKey;
        }
        else
        {
            if (LeftPanelCol.Width.Value > 0)
                ViewModel.Settings.LeftPanelWidth = LeftPanelCol.Width.Value;
            panel.Visibility = Visibility.Collapsed;
            LeftPanelCol.Width = new GridLength(0);
            LeftSplitterCol.Width = new GridLength(0);
        }
        ViewModel.IsLeftPanelOpen = LeftPanelToggles.Any(t => t.Toggle.IsChecked == true);
    }

    // All right-panel toggle/panel/settings-key triples, in activity-bar order. Centralized so
    // adding a new tab only means adding one entry here rather than touching every call site
    // that needs to enumerate, save, or restore which right-panel tab is active.
    private IEnumerable<(ToggleButton Toggle, DockPanel Panel, string SettingsKey)> RightPanelToggles => new (ToggleButton, DockPanel, string)[]
    {
        (SpecialCharsToggle,   SpecialCharsPanel,   "QuickKeys"),
        (PetsciiToggle,        PetsciiPanel,        "Petscii"),
        (BasicKeywordsToggle,  BasicKeywordsPanel,  "BasicKeywords"),
        (AsmKeywordsToggle,    AsmKeywordsPanel,    "AsmKeywords"),
        (MusicNotesToggle,     MusicNotesPanel,     "MusicNotes"),
    };

    private void ActivateRightPanel(ToggleButton toggle, DockPanel panel)
    {
        if (toggle.IsChecked == true)
        {
            foreach (var (otherToggle, otherPanel, _) in RightPanelToggles)
            {
                if (ReferenceEquals(otherToggle, toggle)) continue;
                otherToggle.IsChecked = false;
                otherPanel.Visibility = Visibility.Collapsed;
            }
            panel.Visibility = Visibility.Visible;

            // Only restore the saved width when opening the panel from closed - switching
            // between already-open tabs must not clobber a width resized via the splitter
            // earlier this session.
            if (RightPanelCol.Width.Value == 0)
            {
                RightPanelCol.Width = new GridLength(ViewModel.Settings.RightPanelWidth);
                RightSplitterCol.Width = new GridLength(4);
            }
        }
        else
        {
            CloseRightPanel(panel);
        }
        ViewModel.IsRightPanelOpen = RightPanelToggles.Any(t => t.Toggle.IsChecked == true);
    }

    // Collapses a single right panel and reclaims its column width. Shared by
    // ActivateRightPanel's manual-toggle-off path and by language-driven auto-close
    // (e.g. switching to an Asm tab force-closes an open BASIC Keywords panel).
    private void CloseRightPanel(DockPanel panel)
    {
        if (RightPanelCol.Width.Value > 0)
            ViewModel.Settings.RightPanelWidth = RightPanelCol.Width.Value;
        panel.Visibility = Visibility.Collapsed;
        RightPanelCol.Width = new GridLength(0);
        RightSplitterCol.Width = new GridLength(0);
    }

    private void SpecialCharsToggle_Click(object sender, RoutedEventArgs e) => ActivateRightPanel(SpecialCharsToggle, SpecialCharsPanel);

    private void PetsciiToggle_Click(object sender, RoutedEventArgs e) => ActivateRightPanel(PetsciiToggle, PetsciiPanel);

    private void BasicKeywordsToggle_Click(object sender, RoutedEventArgs e) => ActivateRightPanel(BasicKeywordsToggle, BasicKeywordsPanel);

    private void AsmKeywordsToggle_Click(object sender, RoutedEventArgs e) => ActivateRightPanel(AsmKeywordsToggle, AsmKeywordsPanel);

    private void MusicNotesToggle_Click(object sender, RoutedEventArgs e) => ActivateRightPanel(MusicNotesToggle, MusicNotesPanel);

    // Toggles the whole primary side bar open/closed - unlike ExplorerToggle_Click, this must act
    // on whichever left-panel tab (Explorer/C64U/Search) is actually active, not hardcode
    // Explorer: hiding while C64U or Search is open needs to hide that panel, not switch to
    // Explorer and leave the bar open (which previously took a second Ctrl+B press to fix).
    private void TogglePrimarySideBar()
    {
        var openToggle = LeftPanelToggles.FirstOrDefault(t => t.Toggle.IsChecked == true);
        if (openToggle.Toggle != null)
        {
            openToggle.Toggle.IsChecked = false;
            ActivateLeftPanel(openToggle.Toggle, openToggle.Panel, openToggle.SettingsKey);
            return;
        }

        // Nothing open - reopen whichever tab was active before the bar was last hidden.
        var toOpen = LeftPanelToggles.FirstOrDefault(t => t.SettingsKey == ViewModel.Settings.ActiveLeftPanel);
        if (toOpen.Toggle == null) toOpen = LeftPanelToggles.First();
        toOpen.Toggle.IsChecked = true;
        ActivateLeftPanel(toOpen.Toggle, toOpen.Panel, toOpen.SettingsKey);
    }

    private void ToggleSecondarySideBar()
    {
        bool currentlyOpen = SpecialCharsToggle.IsChecked == true || PetsciiToggle.IsChecked == true;
        if (currentlyOpen)
        {
            SpecialCharsToggle.IsChecked = false;
            PetsciiToggle.IsChecked = false;
            SpecialCharsToggle_Click(this, new RoutedEventArgs());
        }
        else
        {
            SpecialCharsToggle.IsChecked = true;
            SpecialCharsToggle_Click(this, new RoutedEventArgs());
        }
    }

    private void FocusExplorer()
    {
        if (ExplorerToggle.IsChecked != true)
        {
            ExplorerToggle.IsChecked = true;
            ExplorerToggle_Click(this, new RoutedEventArgs());
        }
        FileTree.Focus();
    }

    private void ShowCodeStatistics()
    {
        string text   = Editor.Text;
        int charCount = text.Length;
        int lineCount = Editor.Document.LineCount;
        int wordCount = text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

        string byteLabel, byteValue, byteDescription;

        if (ViewModel.ActiveTab?.Language == EditorLanguage.Asm)
        {
            var asmResult = new Asm6502Assembler().Assemble(
                text, ViewModel.Settings.AsmOutputMode == "Standalone", (ushort)ViewModel.Settings.AsmDefaultOriginAddress);
            byteLabel = "Assembled bytes";
            if (asmResult.Success)
            {
                byteValue = $"{asmResult.PrgBytes!.Length - 2:N0}";
                byteDescription = "Assembled bytes is the size of the machine code, excluding the 2-byte load address header.";
            }
            else
            {
                byteValue = "Assembly errors";
                byteDescription = "Fix the assembly errors in this file to see its assembled byte count.";
            }
        }
        else
        {
            var prgData = new PrgConverter().ConvertToPrg(text);
            int tokenBytes = prgData.Length - 2;  // subtract 2-byte load address header
            byteLabel = "Tokenized bytes";
            byteValue = $"{tokenBytes:N0} / 38,911";
            byteDescription = "Tokenized bytes is the C64 BASIC memory footprint (of 38,911 bytes available).";
        }

        var dlg = new CodeStatisticsWindow(charCount, wordCount, lineCount, byteLabel, byteValue, byteDescription) { Owner = this };
        dlg.ShowDialog();
    }

    #endregion

    #region Bottom Panel

    // Toggles the bottom panel's row height open/closed, mirroring how the left/right panel
    // columns are toggled (see e.g. ActivateRightPanel/CloseRightPanel).
    private void ApplyBottomPanelOpenState()
    {
        if (ViewModel.Settings.IsBottomPanelOpen)
        {
            BottomPanelRow.Height = new GridLength(ViewModel.Settings.BottomPanelHeight);
            BottomPanelSplitterRow.Height = new GridLength(4);
        }
        else
        {
            if (BottomPanelRow.Height.Value > 0)
                ViewModel.Settings.BottomPanelHeight = BottomPanelRow.Height.Value;
            BottomPanelRow.Height = new GridLength(0);
            BottomPanelSplitterRow.Height = new GridLength(0);
        }
    }

    // The outer bottom-panel tabs (Debug/Errors), paired with the content element each shows.
    private IEnumerable<(ToggleButton Toggle, UIElement Content, string Key)> BottomPanelTabs => new (ToggleButton, UIElement, string)[]
    {
        (BottomPanelDebugTabToggle,  DebugPanelContent,  "Debug"),
        (BottomPanelErrorsTabToggle, ErrorsPanelContent, "Errors"),
    };

    private void ActivateBottomPanelTab(string key)
    {
        var target = BottomPanelTabs.FirstOrDefault(t => t.Key == key);
        if (target.Toggle == null) target = BottomPanelTabs.First();

        foreach (var (toggle, content, _) in BottomPanelTabs)
        {
            bool isTarget = ReferenceEquals(toggle, target.Toggle);
            toggle.IsChecked = isTarget;
            content.Visibility = isTarget ? Visibility.Visible : Visibility.Collapsed;
        }

        ViewModel.Settings.ActiveBottomPanelTab = target.Key;
    }

    private void BottomPanelTab_Click(object sender, RoutedEventArgs e)
    {
        if (sender is ToggleButton { Tag: string key })
            ActivateBottomPanelTab(key);
    }

    // Unconditional close regardless of which outer tab (Debug/Errors) is currently showing -
    // unlike IsDebugPanelActive/IsErrorsPanelActive's setters, which only close if their own tab
    // is the one active. Writes straight to Settings (not through either of those two properties)
    // so this always closes the panel, then saves immediately to match how every other
    // settings-backed panel toggle in this app persists on change.
    private void CloseBottomPanel_Click(object sender, RoutedEventArgs e)
    {
        ViewModel.Settings.IsBottomPanelOpen = false;
        ViewModel.Settings.Save();
        ApplyBottomPanelOpenState();
    }

    #endregion
}
