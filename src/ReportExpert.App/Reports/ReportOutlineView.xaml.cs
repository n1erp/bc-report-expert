using System.Windows;
using ReportExpert.Reporting.Design;

namespace ReportExpert.App.Reports;

/// <summary>Tree of the report's items; selecting a node selects it on the design surface.</summary>
public partial class ReportOutlineView : System.Windows.Controls.UserControl
{
    public ReportOutlineView()
    {
        InitializeComponent();
    }

    private void OnSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is DesignExplorerNode node && DataContext is ReportCenterViewModel model && node.Kind != "Body")
        {
            // The report, page header and page footer nodes open their own property sheets.
            _ = model.SelectAsync(node.Kind == "Report" ? null : node.Name);
        }
    }
}
