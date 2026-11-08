using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using ReportExpert.Reporting.Design;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using ListBox = System.Windows.Controls.ListBox;
using Orientation = System.Windows.Controls.Orientation;
using SelectionMode = System.Windows.Controls.SelectionMode;
using TextBox = System.Windows.Controls.TextBox;

namespace ReportExpert.App.Reports;

/// <summary>
/// The filter shown before a preview: one input per visible report parameter, typed by the parameter
/// (date picker, check box, list of available values, multi-select, or text). A blank value means
/// "use the report's default", which may be an expression such as <c>=Month(Today())</c>.
/// </summary>
public sealed class ReportParametersDialog : Window
{
    private readonly List<(DesignParameter Parameter, Func<string?> Read)> _inputs = [];

    public ReportParametersDialog(
        string reportName,
        IReadOnlyList<DesignParameter> parameters,
        IReadOnlyDictionary<string, IReadOnlyList<ParameterOption>> options,
        IReadOnlyDictionary<string, string?> current)
    {
        Title = $"Parameters — {reportName}";
        Width = 520;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "Safi.SurfaceBrush");
        SetResourceReference(ForegroundProperty, "Safi.TextPrimaryBrush");
        FontFamily = (System.Windows.Media.FontFamily)FindResource("Safi.FontFamily");

        var form = new StackPanel { Margin = new Thickness(20, 16, 20, 8) };
        form.Children.Add(new TextBlock { Text = "Filter the report", FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 4) });
        form.Children.Add(new TextBlock
        {
            Text = "Choose the values to render with. Leave a field empty to use the report's default.",
            TextWrapping = TextWrapping.Wrap,
            Opacity = 0.75,
            Margin = new Thickness(0, 0, 0, 14),
        });

        foreach (var parameter in parameters)
        {
            current.TryGetValue(parameter.Name, out var value);
            var known = options.TryGetValue(parameter.Name, out var fromData) && fromData.Count > 0 ? fromData : parameter.Options ?? [];
            var label = string.IsNullOrWhiteSpace(parameter.Prompt) ? parameter.Name : parameter.Prompt;
            form.Children.Add(new TextBlock { Text = label, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 6, 0, 3), ToolTip = $"{parameter.Name} ({parameter.DataType})" });
            var (input, read) = Input(parameter, known, value);
            form.Children.Add(input);
            if (parameter.DefaultValue is { } fallback && string.IsNullOrEmpty(value))
            {
                form.Children.Add(new TextBlock { Text = "Default: " + fallback, FontSize = 11, Opacity = 0.6, Margin = new Thickness(0, 2, 0, 0), TextTrimming = TextTrimming.CharacterEllipsis });
            }

            _inputs.Add((parameter, read));
        }

        var ok = new Button { Content = "View report", IsDefault = true, Padding = new Thickness(16, 7, 16, 7), Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "Safi.PrimaryButton");
        ok.Click += (_, _) => { DialogResult = true; };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(16, 7, 16, 7) };
        cancel.SetResourceReference(StyleProperty, "Safi.OutlinedButton");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(20, 12, 20, 16) };
        buttons.Children.Add(ok);
        buttons.Children.Add(cancel);

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new ScrollViewer { Content = form, VerticalScrollBarVisibility = ScrollBarVisibility.Auto });
        Content = root;
    }

    /// <summary>The chosen values; blank entries are left out so the report default applies.</summary>
    public Dictionary<string, string?> Values =>
        _inputs
            .Select(input => (input.Parameter.Name, Value: input.Read()))
            .Where(pair => !string.IsNullOrEmpty(pair.Value))
            .ToDictionary(pair => pair.Name, pair => pair.Value, StringComparer.Ordinal);

    private static (FrameworkElement Input, Func<string?> Read) Input(DesignParameter parameter, IReadOnlyList<ParameterOption> options, string? value)
    {
        if (options.Count > 0 && parameter.MultiValue)
        {
            var chosen = (value ?? "").Split(ReportParameterValues.Separator, StringSplitOptions.RemoveEmptyEntries).ToHashSet(StringComparer.Ordinal);
            var list = new ListBox { SelectionMode = SelectionMode.Multiple, MaxHeight = 160, DisplayMemberPath = nameof(ParameterOption.Label) };
            foreach (var option in options)
            {
                list.Items.Add(option);
                if (chosen.Contains(option.Value))
                {
                    list.SelectedItems.Add(option);
                }
            }

            return (list, () => string.Join(ReportParameterValues.Separator, list.SelectedItems.Cast<ParameterOption>().Select(option => option.Value)));
        }

        if (options.Count > 0)
        {
            var combo = new ComboBox { DisplayMemberPath = nameof(ParameterOption.Label) };
            if (parameter.AllowBlank || parameter.Nullable || parameter.DefaultValue is not null)
            {
                combo.Items.Add(new ParameterOption("", "(default)"));
            }

            foreach (var option in options)
            {
                combo.Items.Add(option);
            }

            combo.SelectedItem = combo.Items.Cast<ParameterOption>().FirstOrDefault(option => option.Value == (value ?? "")) ?? combo.Items[0];
            return (combo, () => (combo.SelectedItem as ParameterOption)?.Value);
        }

        switch (parameter.DataType)
        {
            case "DateTime":
                var picker = new DatePicker();
                if (DateTime.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
                {
                    picker.SelectedDate = date;
                }

                return (picker, () => picker.SelectedDate?.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
            case "Boolean":
                var check = new CheckBox { Content = parameter.Name, IsThreeState = parameter.Nullable || parameter.DefaultValue is not null };
                check.IsChecked = value switch { "true" or "True" => true, "false" or "False" => false, _ => check.IsThreeState ? null : false };
                return (check, () => check.IsChecked switch { true => "true", false => "false", _ => null });
            default:
                var box = new TextBox { Text = value ?? "", Padding = new Thickness(6, 4, 6, 4) };
                return (box, () => box.Text.Trim());
        }
    }
}
