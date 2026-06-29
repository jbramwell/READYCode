// Copyright (c) 2026 Moonspace Labs, LLC
// Licensed under the MIT License. See LICENSE in the project root for license information.

using System.Drawing.Printing;
using System.Printing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Xps;
using ReadyCode.Tokenizer;
using FormsPageSetupDialog = System.Windows.Forms.PageSetupDialog;
using FormsPrintDialog = System.Windows.Forms.PrintDialog;
using FormsDialogResult = System.Windows.Forms.DialogResult;
using IWin32Window = System.Windows.Forms.IWin32Window;

namespace ReadyCode.Printing;

/// <summary>
/// Prints plain source text as a FlowDocument via the XPS print pipeline. The printer, copies, and
/// page-range picker use the classic WinForms print dialog with UseEXDialog = false, which shows the
/// legacy dialog rather than Windows 11's "UnifiedPrintDialog" (whose embedded preview pane does not
/// render content for any app outside the UWP/WinRT print pipeline — see <see cref="PrintPreview"/>
/// for a real, working preview).
/// Page setup also uses the classic WinForms page setup dialog, since WPF has no equivalent; it is
/// used purely to capture paper size, margins, and orientation, which are applied to the
/// FlowDocument before printing.
/// </summary>
public class SourcePrinter
{
    #region Private Fields

    private const double _pointsToDeviceUnits = 96.0 / 72.0;
    private const double _hundredthsInchToDeviceUnits = 96.0 / 100.0;

    // Embedded (not system-installed) so PETSCII control characters in the source text render
    // correctly even on a machine that hasn't installed the Pet Me 64 font separately.
    private static readonly FontFamily _petMe64Font = new(new Uri("pack://application:,,,/ReadyCode;component/Assets/Fonts/"), "./#Pet Me 64");

    private readonly PrintDocument _pageSettings = new();

    #endregion

    #region Public Methods

    /// <summary>
    /// Shows the Windows page setup dialog for configuring paper size, margins, and orientation.
    /// </summary>
    /// <param name="owner">The window that owns the dialog.</param>
    public void ShowPageSetupDialog(Window owner)
    {
        using var dialog = new FormsPageSetupDialog { Document = _pageSettings };
        dialog.ShowDialog(GetOwner(owner));
    }

    /// <summary>
    /// Shows the print dialog and, if confirmed, prints the given text via the XPS print pipeline.
    /// </summary>
    /// <param name="owner">The window that owns the dialog.</param>
    /// <param name="text">The source text to print.</param>
    /// <param name="documentName">The document name shown in the print queue.</param>
    public void Print(Window owner, string text, string documentName)
    {
        var (width, height) = GetPageSize();
        var document = BuildFlowDocument(text, width, height);
        DocumentPaginator paginator = ((IDocumentPaginatorSource)document).DocumentPaginator;
        if (!paginator.IsPageCountValid)
            paginator.ComputePageCount();

        var settings = _pageSettings.PrinterSettings;
        settings.MinimumPage = 1;
        settings.MaximumPage = Math.Max(1, paginator.PageCount);
        settings.FromPage = 1;
        settings.ToPage = settings.MaximumPage;

        using var dialog = new FormsPrintDialog { Document = _pageSettings, UseEXDialog = false, AllowSomePages = true };
        if (dialog.ShowDialog(GetOwner(owner)) != FormsDialogResult.OK)
            return;

        if (settings.PrintRange == PrintRange.SomePages)
            paginator = new PageRangeDocumentPaginator(paginator, settings.FromPage, settings.ToPage);

        using var server = new LocalPrintServer();
        PrintQueue queue = server.GetPrintQueue(settings.PrinterName);
        queue.CurrentJobSettings.Description = documentName;
        PrintQueue.CreateXpsDocumentWriter(queue).Write(paginator);
    }

    /// <summary>
    /// Shows a print preview window for the given text.
    /// </summary>
    /// <param name="owner">The window that owns the preview window.</param>
    /// <param name="text">The source text to preview.</param>
    /// <param name="documentName">The document name shown in the preview window title.</param>
    public void PrintPreview(Window owner, string text, string documentName)
    {
        var (width, height) = GetPageSize();
        var document = BuildFlowDocument(text, width, height);
        var reader = new FlowDocumentReader { Document = document, ViewingMode = FlowDocumentReaderViewingMode.Page };

        new Window
        {
            Title = $"Print Preview - {documentName}",
            Content = reader,
            Owner = owner,
            Width = Math.Min(950, SystemParameters.WorkArea.Width * 0.9),
            Height = Math.Min(1000, SystemParameters.WorkArea.Height * 0.9),
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        }.ShowDialog();
    }

    #endregion

    #region Private Methods

    private (double Width, double Height) GetPageSize()
    {
        var paper = _pageSettings.DefaultPageSettings.PaperSize;
        double width = paper.Width * _hundredthsInchToDeviceUnits;
        double height = paper.Height * _hundredthsInchToDeviceUnits;
        return _pageSettings.DefaultPageSettings.Landscape ? (height, width) : (width, height);
    }

    private FlowDocument BuildFlowDocument(string text, double pageWidth, double pageHeight)
    {
        var margins = _pageSettings.DefaultPageSettings.Margins;
        var document = new FlowDocument
        {
            FontFamily = _petMe64Font,
            FontSize = 10.0 * _pointsToDeviceUnits,
            PageWidth = pageWidth,
            PageHeight = pageHeight,
            ColumnWidth = double.PositiveInfinity,
            PagePadding = new Thickness(
                margins.Left * _hundredthsInchToDeviceUnits,
                margins.Top * _hundredthsInchToDeviceUnits,
                margins.Right * _hundredthsInchToDeviceUnits,
                margins.Bottom * _hundredthsInchToDeviceUnits)
        };

        var paragraph = new Paragraph { Margin = new Thickness(0) };
        string[] lines = text.Replace("\t", "    ").Split('\n');
        for (int i = 0; i < lines.Length; i++)
        {
            if (i > 0) paragraph.Inlines.Add(new LineBreak());
            paragraph.Inlines.Add(new Run(MapPetsciiGlyphs(lines[i].TrimEnd('\r'))));
        }
        document.Blocks.Add(paragraph);
        return document;
    }

    // Mirrors PetsciiGlyphGenerator (used by the AvalonEdit editor view) so printed output shows the
    // same C64 character-ROM glyphs as the screen, instead of the raw PETSCII byte values verbatim.
    private static string MapPetsciiGlyphs(string line)
    {
        var mapped = new char[line.Length];
        for (int i = 0; i < line.Length; i++)
        {
            char ch = line[i];
            if (ch > 0xFF || (ch >= 0x20 && ch <= 0x7E && ch != '^'))
            {
                mapped[i] = ch;
                continue;
            }
            byte screenCode = PetsciiScreenCodeMap.ToScreenCode((byte)ch);
            mapped[i] = (char)(0xE000 + screenCode);
        }
        return new string(mapped);
    }

    private static IWin32Window GetOwner(Window window) => new Win32WindowWrapper(new WindowInteropHelper(window).Handle);

    #endregion

    private sealed class Win32WindowWrapper(IntPtr handle) : IWin32Window
    {
        #region Public Properties

        public IntPtr Handle { get; } = handle;

        #endregion
    }

    // XpsDocumentWriter.Write(paginator) prints every page the paginator reports - it does not honor
    // PrintTicket page-range settings - so restricting to "from/to" requires wrapping the paginator
    // to only expose that slice of pages.
    private sealed class PageRangeDocumentPaginator : DocumentPaginator
    {
        #region Private Fields

        private readonly DocumentPaginator _inner;
        private readonly int _startIndex;

        #endregion

        #region Constructors

        public PageRangeDocumentPaginator(DocumentPaginator inner, int fromPage, int toPage)
        {
            _inner = inner;
            int from = Math.Max(1, fromPage);
            int to = Math.Min(inner.PageCount, toPage);
            _startIndex = from - 1;
            PageCount = Math.Max(0, to - from + 1);
        }

        #endregion

        #region Public Properties

        public override bool IsPageCountValid => true;
        public override int PageCount { get; }
        public override Size PageSize
        {
            get => _inner.PageSize;
            set => _inner.PageSize = value;
        }
        public override IDocumentPaginatorSource Source => _inner.Source;

        #endregion

        #region Public Methods

        public override DocumentPage GetPage(int pageNumber) => _inner.GetPage(_startIndex + pageNumber);

        #endregion
    }
}
