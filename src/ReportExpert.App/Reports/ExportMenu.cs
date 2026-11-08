using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using ReportExpert.Reporting.Design;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;
using Orientation = System.Windows.Controls.Orientation;

namespace ReportExpert.App.Reports;

/// <summary>
/// The preview's Export menu: one button that opens the three formats, each with what it is good for, so the
/// toolbar keeps a single clear action instead of three similar buttons.
/// </summary>
internal static class ExportMenu
{
    private static readonly (ReportOutputFormat Format, string Title, string Extension, string Detail, Wpf.Ui.Controls.SymbolRegular Icon)[] Formats =
    [
        (ReportOutputFormat.Pdf, "PDF", ".pdf", "Print-ready, exactly as previewed", Wpf.Ui.Controls.SymbolRegular.DocumentPdf24),
        (ReportOutputFormat.Excel, "Excel workbook", ".xlsx", "Values in cells, to sort and analyse", Wpf.Ui.Controls.SymbolRegular.Table24),
        (ReportOutputFormat.Word, "Word document", ".docx", "Editable text and tables", Wpf.Ui.Controls.SymbolRegular.DocumentText24),
    ];

    /// <summary>Opens the menu under <paramref name="anchor"/>; <paramref name="export"/> runs with the chosen format.</summary>
    public static void Open(FrameworkElement anchor, Func<ReportOutputFormat, Task> export)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = anchor,
            Placement = PlacementMode.Bottom,
            HorizontalOffset = 0,
            VerticalOffset = 4,
            MinWidth = 280,
        };

        var heading = new TextBlock { Text = "EXPORT AS", FontSize = 10.5, FontWeight = FontWeights.SemiBold, Opacity = 0.6, Margin = new Thickness(0, 2, 0, 2) };
        menu.Items.Add(new MenuItem { Header = heading, IsHitTestVisible = false, Focusable = false });

        foreach (var (format, title, extension, detail, icon) in Formats)
        {
            var name = new TextBlock { Text = title, FontSize = 13, FontWeight = FontWeights.SemiBold };
            var ext = new TextBlock { Text = extension, FontSize = 11, Opacity = 0.5, Margin = new Thickness(6, 1, 0, 0), VerticalAlignment = VerticalAlignment.Center };
            var line = new StackPanel { Orientation = Orientation.Horizontal, Children = { name, ext } };
            var hint = new TextBlock { Text = detail, FontSize = 11.5, Opacity = 0.65, Margin = new Thickness(0, 1, 0, 0) };
            var item = new MenuItem
            {
                Header = new StackPanel { Margin = new Thickness(0, 3, 0, 3), Children = { line, hint } },
                Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = icon, FontSize = 20 },
            };
            item.Click += async (_, _) => await export(format);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    /// <summary>The save dialog filter for a format.</summary>
    public static string Filter(ReportOutputFormat format) => format switch
    {
        ReportOutputFormat.Excel => "Excel workbook (*.xlsx)|*.xlsx",
        ReportOutputFormat.Word => "Word document (*.docx)|*.docx",
        _ => "PDF document (*.pdf)|*.pdf",
    };
}
