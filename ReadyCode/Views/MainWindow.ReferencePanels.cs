// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using ReadyCode.Editor;
using ReadyCode.Sid;
using ReadyCode.Tokenizer;

namespace ReadyCode.Views;

// The reference side panels - PETSCII table, BASIC/assembly keyword lists, music notes - and inserting special characters.
public partial class MainWindow
{
    #region Reference Panels

    /// <summary>
    /// Populates the PETSCII Reference panel with three groups of character cells:
    /// printable (32-127), graphics SET 1 (96-127 overlap shown separately via description),
    /// and graphics SET 2/3 (160-223). Each cell shows the Pet Me 64 glyph + CHR$() code
    /// and inserts the character when clicked.
    /// </summary>
    private void BuildPetsciiTable()
    {
        PetsciiTablePanel.Children.Clear();

        var petMe64  = EditorFonts.Petscii;
        var segoeUi  = new FontFamily("Segoe UI");

        Brush R(string key) => (Brush)FindResource(key);

        var labelBg  = R("ThemePanelHeaderBg");
        var labelFg  = R("ThemePanelHeaderFg");
        var glyphFg  = R("ThemePetsciiGlyphFg");
        var hoverBg  = R("ThemePetsciiRowHoverBg");
        var rowBg0   = R("ThemePetsciiRowEvenBg");
        var rowBg1   = R("ThemePetsciiRowOddBg");
        var sepBrush = R("ThemePetsciiSeparator");
        var hdrBg    = R("ThemePetsciiHeaderBg");
        var codeFg   = R("ThemePetsciiCodeFg");
        var noteBg   = R("ThemePetsciiNoteBg");

        // null label = printable glyph; "" = undefined (no PRINT element); other = control chip label
        var controlLabels = new Dictionary<int, string>
        {
            [0]  = "", [1]  = "", [2]  = "", [3]  = "", [4]  = "",
            [5]  = "WHT",
            [6]  = "DISABLE SHIFT C=",
            [7]  = "ENABLE SHIFT C=",
            [8]  = "", [9]  = "", [10] = "", [11] = "", [12] = "",
            [13] = "RETURN",
            [14] = "LOWER CASE",
            [15] = "", [16] = "",
            [17] = "CRSR↓",
            [18] = "RVS ON",
            [19] = "CLR HOME",
            [20] = "INST DEL",
            [21] = "", [22] = "", [23] = "", [24] = "", [25] = "", [26] = "", [27] = "",
            [28] = "RED",
            [29] = "CRSR→",
            [30] = "GRN",
            [31] = "BLU",
            [32] = "SPACE",
            [128] = "",
            [129] = "ORANGE",
            [130] = "", [131] = "",
            [132] = "F7/8",
            [133] = "F1",
            [134] = "F3",
            [135] = "F5",
            [136] = "F7",
            [137] = "F2",
            [138] = "F4",
            [139] = "F6",
            [140] = "F8",
            [141] = "SHIFT RETURN",
            [142] = "UPPER CASE",
            [143] = "",
            [144] = "BLK",
            [145] = "CRSR↑",
            [146] = "RVS OFF",
            [147] = "CLR HOME",
            [148] = "INST DEL",
            [149] = "BROWN",
            [150] = "LT RED",
            [151] = "GRAY 1",
            [152] = "GRAY 2",
            [153] = "LT GREEN",
            [154] = "LT BLUE",
            [155] = "GRAY 3",
            [156] = "PUR",
            [157] = "←CRSR",
            [158] = "YEL",
            [159] = "CYN",
            [160] = "SPACE",
        };

        Border MakeRow(FrameworkElement? printElem, string codeText, Brush bg, bool clickable, int code)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });

            if (printElem != null)
            {
                Grid.SetColumn(printElem, 0);
                grid.Children.Add(printElem);
            }

            var codeBlock = new TextBlock
            {
                Text = codeText,
                FontFamily = segoeUi,
                FontSize = 10,
                Foreground = codeFg,
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 0)
            };
            Grid.SetColumn(codeBlock, 1);
            grid.Children.Add(codeBlock);

            var border = new Border
            {
                Background = bg,
                BorderBrush = sepBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                MinHeight = 24,
                Child = grid
            };

            if (clickable)
            {
                border.Cursor = Cursors.Hand;
                border.MouseLeftButtonDown += (_, _) => InsertSpecialChar((char)code);
                border.MouseEnter += (_, _) => border.Background = hoverBg;
                border.MouseLeave += (_, _) => border.Background = bg;
            }

            return border;
        }

        // Header row
        var hdrGrid = new Grid();
        hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        hdrGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(42) });
        hdrGrid.Background = labelBg;

        var hdrPrint = new TextBlock { Text = "PRINT", FontFamily = segoeUi, FontSize = 10,
            FontWeight = FontWeights.Bold, Foreground = labelFg,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
        Grid.SetColumn(hdrPrint, 0);
        hdrGrid.Children.Add(hdrPrint);

        var hdrChrs = new TextBlock { Text = "CHR$", FontFamily = segoeUi, FontSize = 10,
            FontWeight = FontWeights.Bold, Foreground = labelFg,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        Grid.SetColumn(hdrChrs, 1);
        hdrGrid.Children.Add(hdrChrs);

        PetsciiTablePanel.Children.Add(new Border
        {
            Background = hdrBg,
            BorderBrush = sepBrush,
            BorderThickness = new Thickness(0, 0, 0, 1),
            MinHeight = 22,
            Child = hdrGrid
        });

        // Data rows 0–191
        int rowIdx = 0;

        for (int code = 0; code <= 191; code++)
        {
            Brush bg = (rowIdx++ % 2 == 0) ? rowBg0 : rowBg0;
            FrameworkElement? printElem;
            bool clickable;

            if (controlLabels.TryGetValue(code, out string? label))
            {
                if (string.IsNullOrEmpty(label))
                {
                    printElem = null;
                    clickable = false;
                }
                else
                {
                    printElem = new Border
                    {
                        Background = labelBg,
                        CornerRadius = new CornerRadius(2),
                        Padding = new Thickness(4, 1, 4, 1),
                        HorizontalAlignment = HorizontalAlignment.Left,
                        VerticalAlignment = VerticalAlignment.Center,
                        Margin = new Thickness(6, 2, 4, 2),
                        Child = new TextBlock
                        {
                            Text = label,
                            Foreground = labelFg,
                            FontFamily = segoeUi,
                            FontSize = 9,
                            FontWeight = FontWeights.SemiBold
                        }
                    };

                    clickable = true;
                }
            }
            else
            {
                byte sc = PetsciiScreenCodeMap.ToScreenCode((byte)code);
                string glyph = ((char)(0xE000 + sc)).ToString();

                printElem = new TextBlock
                {
                    Text = glyph,
                    FontFamily = petMe64,
                    FontSize = 14,
                    Foreground = glyphFg,
                    HorizontalAlignment = HorizontalAlignment.Left,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(6, 2, 4, 2)
                };

                clickable = true;
            }

            PetsciiTablePanel.Children.Add(MakeRow(printElem, code.ToString(), bg, clickable, code));
        }

        // Footer notes for codes 192–255
        foreach (var note in new[]
        {
            "192–223: same as 96–127",
            "224–254: same as 160–190",
            "255: same as 126"
        })
        {
            PetsciiTablePanel.Children.Add(new Border
            {
                Background = noteBg,
                BorderBrush = sepBrush,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Child = new TextBlock
                {
                    Text = note,
                    FontFamily = segoeUi,
                    FontSize = 9,
                    Foreground = codeFg,
                    Margin = new Thickness(6, 3, 4, 3),
                    TextWrapping = TextWrapping.Wrap
                }
            });
        }
    }

    private void BuildBasicKeywordsList() =>
        BuildKeywordsList(BasicKeywordsListPanel, BasicCompletionProvider.AllItems, BasicCompletionProvider.CategoryOrder);

    private void BuildAsmKeywordsList() =>
        BuildKeywordsList(AsmKeywordsListPanel, AsmCompletionProvider.AllItems, AsmCompletionProvider.CategoryOrder);

    // Renders a reference panel (BASIC Keywords or ASM Mnemonics) as a series of category
    // headers followed by name/description rows, shared by both languages' completion tables.
    private void BuildKeywordsList(StackPanel targetPanel, IReadOnlyList<KeywordCompletionData> allItems, IReadOnlyList<string> categoryOrder)
    {
        targetPanel.Children.Clear();

        Brush R(string key) => (Brush)FindResource(key);
        var nameFg   = R("ThemeFileFg");
        var descFg   = R("ThemeSpecialCharShortcutFg");
        var labelBg  = R("ThemePanelHeaderBg");
        var labelFg  = R("ThemePanelHeaderFg");
        var itemsByCategory = allItems.ToLookup(i => i.Category);

        foreach (var category in categoryOrder)
        {
            targetPanel.Children.Add(new TextBlock
            {
                Text = category.ToUpperInvariant(),
                FontSize = 10,
                FontWeight = FontWeights.Bold,
                Background = labelBg,
                Foreground = labelFg,
                Margin = new Thickness(8, 8, 8, 4),
                Padding = new Thickness(8, 4, 0, 4),
                MinHeight = 22,
                VerticalAlignment = VerticalAlignment.Center
            });

            foreach (var item in itemsByCategory[category].OrderBy(i => i.Text, StringComparer.OrdinalIgnoreCase))
            {
                var row = new StackPanel { Margin = new Thickness(8, 0, 8, 6) };

                row.Children.Add(new TextBlock
                {
                    Text = item.Text,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = nameFg
                });

                row.Children.Add(new TextBlock
                {
                    Text = item.Description?.ToString() ?? string.Empty,
                    FontSize = 9,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = descFg,
                    Margin = new Thickness(0, 1, 0, 0)
                });
                targetPanel.Children.Add(row);
            }
        }
    }

    private void BuildMusicNotesTable()
    {
        MusicNotesGrid.RowDefinitions.Clear();
        MusicNotesGrid.Children.Clear();

        Brush R(string key) => (Brush)FindResource(key);
        var headerBg    = R("ThemePetsciiHeaderBg");
        var headerFg    = R("ThemeFileFg");
        var sepBrush    = R("ThemePetsciiSeparator");
        var rowEvenBg   = R("ThemePetsciiRowEvenBg");
        var cellFg      = R("ThemePetsciiCodeFg");
        var labelBg  = R("ThemePanelHeaderBg");
        var labelFg  = R("ThemePanelHeaderFg");

        MusicNotesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        MusicNotesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        void AddHeaderCell(string text, int column, int row, int columnSpan, int rowSpan)
        {
            var border = new Border
            {
                Background = labelBg, //headerBg,
                BorderBrush = sepBrush,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Child = new TextBlock
                {
                    Text = text,
                    FontSize = 10,
                    FontWeight = FontWeights.Bold,
                    Foreground = labelFg, //headerFg,
                    TextAlignment = TextAlignment.Center,
                    TextWrapping = TextWrapping.Wrap,
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(4, 4, 4, 4)
                }
            };
            Grid.SetColumn(border, column);
            Grid.SetRow(border, row);
            Grid.SetColumnSpan(border, columnSpan);
            Grid.SetRowSpan(border, rowSpan);
            MusicNotesGrid.Children.Add(border);
        }

        AddHeaderCell("MUSICAL NOTE", column: 0, row: 0, columnSpan: 2, rowSpan: 1);
        AddHeaderCell("NOTE",   column: 0, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("OCTAVE", column: 1, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("OSCILLATOR FREQ (NTSC)", column: 2, row: 0, columnSpan: 3, rowSpan: 1);
        AddHeaderCell("OSCILLATOR FREQ (PAL)",  column: 5, row: 0, columnSpan: 3, rowSpan: 1);
        AddHeaderCell("DECIMAL", column: 2, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("HI",      column: 3, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("LOW",     column: 4, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("DECIMAL", column: 5, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("HI",      column: 6, row: 1, columnSpan: 1, rowSpan: 1);
        AddHeaderCell("LOW",     column: 7, row: 1, columnSpan: 1, rowSpan: 1);

        int gridRow = 2;
        foreach (var note in SidNoteProvider.AllNotes)
        {
            MusicNotesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            void AddCell(string text, int column)
            {
                var border = new Border
                {
                    Background = rowEvenBg,
                    BorderBrush = sepBrush,
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Child = new TextBlock
                    {
                        Text = text,
                        FontSize = 9,
                        Foreground = cellFg,
                        TextAlignment = TextAlignment.Center,
                        Padding = new Thickness(3, 2, 3, 2)
                    }
                };
                
                Grid.SetColumn(border, column);
                Grid.SetRow(border, gridRow);
                MusicNotesGrid.Children.Add(border);
            }

            AddCell(note.Note.ToString(),        0);
            AddCell(note.Octave,                 1);
            AddCell(note.DecimalNtsc.ToString(), 2);
            AddCell(note.HiNtsc.ToString(),      3);
            AddCell(note.LowNtsc.ToString(),     4);
            AddCell(note.DecimalPal?.ToString() ?? "—", 5);
            AddCell(note.HiPal?.ToString()      ?? "—", 6);
            AddCell(note.LowPal?.ToString()     ?? "—", 7);

            gridRow++;
        }
    }

    #endregion

    #region Special Characters

    private void SpecialChar_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string tagStr && int.TryParse(tagStr, out int code))
            InsertSpecialChar((char)code);
    }

    // Targets whichever control the click is actually meant for: the Variables tree's value edit
    // box if that's focused, otherwise the main code editor (the historical, still-default
    // target). There's no real copy/paste from this panel to redirect instead - its preview
    // glyphs are static, non-selectable TextBlocks - so click-to-insert has to be the one
    // mechanism that reaches both places.
    private void InsertSpecialChar(char ch)
    {
        if (Keyboard.FocusedElement is TextBox textBox && FindAncestor<TreeView>(textBox) == DebugVariablesTree)
        {
            InsertSpecialCharIntoVariableEditBox(textBox, ch);
            return;
        }

        int start = Editor.SelectionStart;
        int length = Editor.SelectionLength;
        Editor.Document.Replace(start, length, ch.ToString());
        int newOffset = start + 1;
        Editor.CaretOffset = newOffset;
        Editor.Select(newOffset, 0);
        Editor.Focus();

        // Moving the caret above fires Editor_CaretPositionChanged -> UpdateGhostText, which can
        // show a spurious keyword-completion suggestion after a picker-inserted PETSCII byte
        // (raw high bytes satisfy char.IsLetterOrDigit for some Unicode categories, so
        // GetWordBeforeCaret can sweep them into a "word" that coincidentally matches one).
        // Ghost text only makes sense while actively typing - see the identical fix/reasoning in
        // NavigateToCurrentMatch.
        ClearGhostText();
    }

    // The Variables tree's value edit box shows PUA-substituted display text (see
    // DebugVariableValueConverter/PetsciiScreenCodeMap.ToDisplayText), not raw PETSCII bytes, so
    // the inserted character has to match that same convention - VariableWriteBack.EncodeString
    // converts it back on commit.
    private static void InsertSpecialCharIntoVariableEditBox(TextBox textBox, char ch)
    {
        char displayChar = (char)(0xE000 + PetsciiScreenCodeMap.ToScreenCode((byte)ch));
        int start = textBox.SelectionStart;
        textBox.Text = textBox.Text.Remove(start, textBox.SelectionLength).Insert(start, displayChar.ToString());
        int newOffset = start + 1;
        textBox.CaretIndex = newOffset;
        textBox.Select(newOffset, 0);
        textBox.Focus();
    }

    #endregion
}
