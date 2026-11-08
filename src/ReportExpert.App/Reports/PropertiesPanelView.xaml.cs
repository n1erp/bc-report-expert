using System.Windows;
using System.Windows.Input;
using ReportExpert.Reporting.Design;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace ReportExpert.App.Reports;

/// <summary>Property sheet of the selected item, committed on Enter or when the field loses focus.</summary>
public partial class PropertiesPanelView : System.Windows.Controls.UserControl
{
    public PropertiesPanelView()
    {
        InitializeComponent();
    }

    private ReportCenterViewModel? Model => DataContext as ReportCenterViewModel;

    private void OnPropertyKey(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            Commit(sender);
            e.Handled = true;
        }
    }

    private void OnPropertyCommit(object sender, KeyboardFocusChangedEventArgs e) => Commit(sender);

    /// <summary>The fx button: the property in the expression editor.</summary>
    private void OnExpression(object sender, RoutedEventArgs e)
    {
        if (Model is { } model && sender is FrameworkElement { Tag: string key })
        {
            _ = model.EditExpressionAsync(key);
        }
    }

    private void Commit(object sender)
    {
        if (Model is not { } model || sender is not FrameworkElement { Tag: string key, DataContext: DesignProperty property })
        {
            return;
        }

        var value = sender switch
        {
            TextBox box => box.Text,
            ComboBox combo => combo.Text,
            _ => null,
        };

        if (value is not null && !string.Equals(value, property.Value, StringComparison.Ordinal))
        {
            _ = model.SetPropertyAsync(key, value);
        }
    }

}
