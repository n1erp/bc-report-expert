using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using ReportExpert.App.Shell;
using ReportExpert.Domain.Models;

namespace ReportExpert.App.Workspace;

public partial class WorkspaceExplorerView : System.Windows.Controls.UserControl
{
    public WorkspaceExplorerView()
    {
        Resources.Add("EmptyListVis", new ZeroCountToVisibilityConverter());
        Resources.Add("SamePath", new SamePathConverter());
        InitializeComponent();
    }

    /// <summary>Raised by the « button: the window collapses the panel to its vertical Reports tab.</summary>
    public event Action? CollapseRequested;

    private void CollapseButton_Click(object sender, RoutedEventArgs e) => CollapseRequested?.Invoke();

    private void AddMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (Resources["AddContextMenu"] is not System.Windows.Controls.ContextMenu menu)
            return;

        menu.PlacementTarget = sender as UIElement;
        menu.DataContext = DataContext;
        menu.IsOpen = true;
    }

    private void WorkspaceNodeHeader_Click(object sender, MouseButtonEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: WorkspaceItemNode node } &&
            DataContext is ShellViewModel vm)
        {
            vm.OpenWorkspaceFileCommand.Execute(node.File);
            e.Handled = true;
        }
    }
}

/// <summary>True when both bound paths name the same file (the open report in the list).</summary>
internal sealed class SamePathConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is string a && values[1] is string b && a.Length > 0
        && string.Equals(a, b, StringComparison.OrdinalIgnoreCase);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

internal sealed class ZeroCountToVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is int count && count == 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
