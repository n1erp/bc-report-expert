using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReportExpert.Reporting.Design;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using TabControl = System.Windows.Controls.TabControl;
using TabItem = System.Windows.Controls.TabItem;
using TextBox = System.Windows.Controls.TextBox;

namespace ReportExpert.App.Reports;

/// <summary>Shared look of the tablix dialogs: Safi surface, headings, label rows and the button bar.</summary>
internal abstract class TablixDialogBase : Window
{
    protected TablixDialogBase(string title, double width)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        ShowInTaskbar = false;
        SetResourceReference(BackgroundProperty, "Safi.SurfaceBrush");
        SetResourceReference(ForegroundProperty, "Safi.TextPrimaryBrush");
        if (TryFindResource("Safi.FontFamily") is System.Windows.Media.FontFamily font)
        {
            FontFamily = font;
        }
    }

    protected void Compose(UIElement body, string accept = "OK", Func<bool>? validate = null)
    {
        var ok = new Button { Content = accept, IsDefault = true, Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(0, 0, 8, 0), MinWidth = 80 };
        ok.SetResourceReference(StyleProperty, "Safi.PrimaryButton");
        ok.Click += (_, _) =>
        {
            if (validate?.Invoke() ?? true)
            {
                DialogResult = true;
            }
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(18, 7, 18, 7), MinWidth = 80 };
        cancel.SetResourceReference(StyleProperty, "Safi.OutlinedButton");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(22, 14, 22, 18), Children = { ok, cancel } };
        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(new Border { Padding = new Thickness(22, 16, 22, 4), Child = body });
        Content = root;
    }

    protected static TextBlock Heading(string text) =>
        new() { Text = text, FontWeight = FontWeights.SemiBold, FontSize = 13, Margin = new Thickness(0, 10, 0, 6) };

    protected static TextBlock Hint(string text) =>
        new() { Text = text, FontSize = 11.5, Opacity = 0.75, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 8) };

    protected static FrameworkElement Row(string label, FrameworkElement control)
    {
        var grid = new Grid { Margin = new Thickness(0, 3, 0, 3) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(130) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        Grid.SetColumn(control, 1);
        grid.Children.Add(control);
        return grid;
    }

    protected static ComboBox ExpressionCombo(IEnumerable<string> fields, string value)
    {
        var combo = new ComboBox { IsEditable = true, MinWidth = 220, Text = value };
        foreach (var field in fields)
        {
            combo.Items.Add("[" + field + "]");
        }

        combo.Text = Display(value);
        return combo;
    }

    /// <summary><c>=Fields!X.Value</c> is shown as <c>[X]</c>, as in a Report Builder expression box.</summary>
    protected static string Display(string expression)
    {
        const string prefix = "=Fields!";
        const string suffix = ".Value";
        return expression.StartsWith(prefix, StringComparison.Ordinal) && expression.EndsWith(suffix, StringComparison.Ordinal)
            && expression[prefix.Length..^suffix.Length].All(ch => char.IsLetterOrDigit(ch) || ch == '_')
            ? "[" + expression[prefix.Length..^suffix.Length] + "]"
            : expression;
    }

    /// <summary>Turns <c>[X]</c> back into a field reference; other text is kept as typed.</summary>
    protected static string Expression(string? text)
    {
        var value = text?.Trim() ?? "";
        return value.Length > 2 && value[0] == '[' && value[^1] == ']' ? value[1..^1] : value;
    }

    protected Brush Res(string key) => TryFindResource(key) as Brush ?? System.Windows.Media.Brushes.Gray;
}

/// <summary>
/// "Tablix group": pick what to group by and whether the group gets a header and a footer row, as the
/// Add Parent/Child/Adjacent group commands ask.
/// </summary>
internal sealed class AddGroupDialog : TablixDialogBase
{
    private readonly ComboBox _groupBy;
    private readonly CheckBox _header = new() { Content = "Add group header", Margin = new Thickness(0, 4, 0, 2) };
    private readonly CheckBox _footer = new() { Content = "Add group footer", Margin = new Thickness(0, 2, 0, 2) };
    private readonly CheckBox _headerCell = new() { Content = "Show the group value in its own header column", IsChecked = true, Margin = new Thickness(0, 2, 0, 2) };
    private readonly TextBox _name = new() { MinWidth = 220 };
    private readonly TextBlock _error = new() { Foreground = System.Windows.Media.Brushes.IndianRed, TextWrapping = TextWrapping.Wrap };

    private AddGroupDialog(string title, IReadOnlyList<string> fields, string? field, bool rowAxis, bool allowDetails)
        : base(title, 460)
    {
        _groupBy = ExpressionCombo(fields, field is null ? "" : "[" + field + "]");
        _headerCell.Content = rowAxis ? "Show the group value in a row header column" : "Show the group value in a column header row";
        _header.Content = rowAxis ? "Add group header row" : "Add group header column";
        _footer.Content = rowAxis ? "Add group footer row" : "Add group footer column";
        var body = new StackPanel();
        body.Children.Add(Heading("Group by"));
        body.Children.Add(Hint(allowDetails
            ? "Choose a field or type an expression (=…). Leave it empty to add a details group."
            : "Choose a field or type an expression (=…)."));
        body.Children.Add(_groupBy);
        body.Children.Add(Row("Group name", _name));
        body.Children.Add(Heading("Options"));
        body.Children.Add(_header);
        body.Children.Add(_footer);
        body.Children.Add(_headerCell);
        body.Children.Add(_error);
        Compose(body, validate: () =>
        {
            if (!allowDetails && Expression(_groupBy.Text).Length == 0)
            {
                _error.Text = "Choose a field to group by.";
                return false;
            }

            return true;
        });
        Loaded += (_, _) => _groupBy.Focus();
    }

    public sealed record Choice(string GroupBy, string? Name, bool Header, bool Footer, bool HeaderCell);

    public static Choice? Ask(Window? owner, string title, IReadOnlyList<string> fields, string? field, bool rowAxis, bool allowDetails = false)
    {
        var dialog = new AddGroupDialog(title, fields, field, rowAxis, allowDetails) { Owner = owner };
        return dialog.ShowDialog() == true
            ? new Choice(Expression(dialog._groupBy.Text), dialog._name.Text.Trim().Length == 0 ? null : dialog._name.Text.Trim(), dialog._header.IsChecked == true, dialog._footer.IsChecked == true, dialog._headerCell.IsChecked == true)
            : null;
    }
}

/// <summary>A small option dialog (Delete group / Delete rows / Delete columns options).</summary>
internal sealed class ChoiceDialog : TablixDialogBase
{
    private readonly List<RadioButton> _options = [];

    private ChoiceDialog(string title, string header, IReadOnlyList<string> options)
        : base(title, 380)
    {
        var body = new StackPanel();
        body.Children.Add(Heading(header));
        for (var index = 0; index < options.Count; index++)
        {
            var option = new RadioButton { Content = options[index], GroupName = "choice", IsChecked = index == 0, Margin = new Thickness(0, 3, 0, 3) };
            _options.Add(option);
            body.Children.Add(option);
        }

        Compose(body);
    }

    public static int? Ask(Window? owner, string title, string header, params string[] options)
    {
        var dialog = new ChoiceDialog(title, header, options) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog._options.FindIndex(option => option.IsChecked == true) : null;
    }
}

/// <summary>
/// Group Properties: General (name, group expressions), Sorting, Filters, Visibility (initial state and
/// toggle item), Page Breaks and Variables, applied as one revision.
/// </summary>
internal sealed class GroupPropertiesDialog : TablixDialogBase
{
    private static readonly string[] Operators = ["Equal", "NotEqual", "Like", "GreaterThan", "GreaterThanOrEqual", "LessThan", "LessThanOrEqual", "TopN", "BottomN", "TopPercent", "BottomPercent", "In", "Between"];

    private readonly DesignGroupDetail _detail;
    private readonly IReadOnlyList<string> _fields;
    private readonly TextBox _name = new() { MinWidth = 260 };
    private readonly ObservableCollection<Line> _groupOn = [];
    private readonly ObservableCollection<Line> _sorts = [];
    private readonly ObservableCollection<Line> _filters = [];
    private readonly ObservableCollection<Line> _variables = [];
    private readonly RadioButton _show = new() { Content = "Show", GroupName = "visibility", Margin = new Thickness(0, 2, 0, 2) };
    private readonly RadioButton _hide = new() { Content = "Hide", GroupName = "visibility", Margin = new Thickness(0, 2, 0, 2) };
    private readonly RadioButton _when = new() { Content = "Show or hide based on an expression", GroupName = "visibility", Margin = new Thickness(0, 2, 0, 2) };
    private readonly TextBox _hiddenExpression = new() { MinWidth = 260, Margin = new Thickness(20, 2, 0, 6) };
    private readonly CheckBox _toggle = new() { Content = "Display can be toggled by this report item:", Margin = new Thickness(0, 8, 0, 2) };
    private readonly ComboBox _toggleItem = new() { MinWidth = 220, Margin = new Thickness(20, 2, 0, 2) };
    private readonly CheckBox _between = new() { Content = "Between each instance of a group", Margin = new Thickness(0, 2, 0, 2) };
    private readonly CheckBox _atStart = new() { Content = "Also at the start of a group", Margin = new Thickness(0, 2, 0, 2) };
    private readonly CheckBox _atEnd = new() { Content = "Also at the end of a group", Margin = new Thickness(0, 2, 0, 2) };
    private readonly CheckBox _reset = new() { Content = "Reset page numbers at each break", Margin = new Thickness(0, 8, 0, 2) };
    private readonly TextBox _pageName = new() { MinWidth = 260 };

    private GroupPropertiesDialog(string table, DesignGroupDetail detail, IReadOnlyList<string> fields)
        : base("Group Properties", 620)
    {
        _detail = detail;
        _fields = fields;
        Table = table;
        _name.Text = detail.Name;
        foreach (var expression in detail.GroupExpressions)
        {
            _groupOn.Add(new Line(Display(expression), "", ""));
        }

        foreach (var sort in detail.Sorts)
        {
            _sorts.Add(new Line(Display(sort.Expression), sort.Direction == "Descending" ? "Z to A" : "A to Z", ""));
        }

        foreach (var filter in detail.Filters)
        {
            _filters.Add(new Line(Display(filter.Expression), filter.Operator, string.Join(", ", filter.Values)));
        }

        foreach (var variable in detail.Variables)
        {
            _variables.Add(new Line(variable.Name, variable.Value, ""));
        }

        var hidden = detail.Hidden.Trim();
        (hidden.Length == 0 || hidden == "false" ? _show : hidden == "true" ? _hide : _when).IsChecked = true;
        _hiddenExpression.Text = hidden.StartsWith('=') ? hidden : "";
        foreach (var candidate in detail.ToggleCandidates)
        {
            _toggleItem.Items.Add(candidate);
        }

        _toggle.IsChecked = detail.ToggleItem.Length > 0;
        _toggleItem.SelectedItem = detail.ToggleItem.Length > 0 ? detail.ToggleItem : null;
        _between.IsChecked = detail.BreakLocation is "Between" or "Start" or "End" or "StartAndEnd";
        _atStart.IsChecked = detail.BreakLocation is "Start" or "StartAndEnd";
        _atEnd.IsChecked = detail.BreakLocation is "End" or "StartAndEnd";
        _reset.IsChecked = detail.ResetPageNumber;
        _pageName.Text = detail.PageName;

        var tabs = new TabControl { MinHeight = 340 };
        tabs.Items.Add(Tab("General", General()));
        tabs.Items.Add(Tab("Sorting", Lines(_sorts, "Sort by", "Order", ["A to Z", "Z to A"], null, () => new Line("", "A to Z", ""))));
        tabs.Items.Add(Tab("Filters", Lines(_filters, "Expression", "Operator", Operators, "Value", () => new Line("", "Equal", ""))));
        tabs.Items.Add(Tab("Visibility", Visibility()));
        tabs.Items.Add(Tab("Page Breaks", PageBreaks()));
        tabs.Items.Add(Tab("Variables", Lines(_variables, "Name", "Value", null, null, () => new Line("", "", ""), nameIsText: true)));
        Compose(tabs);
    }

    public string Table { get; }

    public static TablixGroupEdit? Ask(Window? owner, string table, DesignGroupDetail detail, IReadOnlyList<string> fields)
    {
        var dialog = new GroupPropertiesDialog(table, detail, fields) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Result() : null;
    }

    private TablixGroupEdit Result()
    {
        var hidden = _show.IsChecked == true ? "" : _hide.IsChecked == true ? "true" : _hiddenExpression.Text.Trim();
        var location = _between.IsChecked != true
            ? "None"
            : _atStart.IsChecked == true && _atEnd.IsChecked == true ? "StartAndEnd"
            : _atStart.IsChecked == true ? "Start"
            : _atEnd.IsChecked == true ? "End"
            : "Between";
        return new TablixGroupEdit(
            Table,
            _detail.Name,
            NewName: _name.Text.Trim().Length == 0 ? null : _name.Text.Trim(),
            GroupExpressions: _detail.IsDetails ? null : _groupOn.Select(line => Expression(line.First)).Where(value => value.Length > 0).ToArray(),
            Sorts: _sorts.Select(line => new DesignSort(Expression(line.First), line.Second == "Z to A" ? "Descending" : "Ascending")).Where(sort => sort.Expression.Length > 0).ToArray(),
            Filters: _filters.Select(line => new DesignGroupFilter(Expression(line.First), line.Second, line.Third.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))).Where(filter => filter.Expression.Length > 0).ToArray(),
            Hidden: hidden,
            ToggleItem: _toggle.IsChecked == true ? _toggleItem.SelectedItem as string ?? "" : "",
            BreakLocation: location,
            ResetPageNumber: _reset.IsChecked == true,
            PageName: _pageName.Text,
            Variables: _variables.Select(line => new DesignVariable(line.First.Trim(), line.Second)).Where(variable => variable.Name.Length > 0).ToArray());
    }

    private UIElement General()
    {
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(Hint("Change name and group expressions."));
        panel.Children.Add(Row("Name", _name));
        if (_detail.IsDetails)
        {
            panel.Children.Add(Hint("This is a details group: it has no group expressions and repeats once per row of data."));
            return panel;
        }

        panel.Children.Add(Heading("Group expressions"));
        panel.Children.Add(Lines(_groupOn, "Group on", null, null, null, () => new Line("", "", "")));
        return panel;
    }

    private UIElement Visibility()
    {
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(Heading("When the report is initially run:"));
        panel.Children.Add(_show);
        panel.Children.Add(_hide);
        panel.Children.Add(_when);
        panel.Children.Add(_hiddenExpression);
        panel.Children.Add(_toggle);
        panel.Children.Add(_toggleItem);
        panel.Children.Add(Hint("A drilldown: hide this group and let a text box in a containing group show it (+/−)."));
        return panel;
    }

    private UIElement PageBreaks()
    {
        var panel = new StackPanel { Margin = new Thickness(12) };
        panel.Children.Add(Heading("Page break options:"));
        panel.Children.Add(_between);
        panel.Children.Add(_atStart);
        panel.Children.Add(_atEnd);
        panel.Children.Add(_reset);
        panel.Children.Add(Row("Page name", _pageName));
        return panel;
    }

    private UIElement Lines(ObservableCollection<Line> lines, string firstHeader, string? secondHeader, IReadOnlyList<string>? secondOptions, string? thirdHeader, Func<Line> create, bool nameIsText = false)
    {
        var rows = new StackPanel();
        void Rebuild()
        {
            rows.Children.Clear();
            var header = new Grid { Margin = new Thickness(0, 0, 0, 4) };
            Columns(header);
            header.Children.Add(Caption(firstHeader, 0));
            if (secondHeader is not null)
            {
                header.Children.Add(Caption(secondHeader, 1));
            }

            if (thirdHeader is not null)
            {
                header.Children.Add(Caption(thirdHeader, 2));
            }

            rows.Children.Add(header);
            foreach (var line in lines.ToList())
            {
                var grid = new Grid { Margin = new Thickness(0, 2, 0, 2) };
                Columns(grid);
                FrameworkElement first = nameIsText
                    ? new TextBox { Text = line.First }
                    : ExpressionCombo(_fields, line.First);
                if (first is TextBox textFirst)
                {
                    textFirst.TextChanged += (_, _) => line.First = textFirst.Text;
                }
                else if (first is ComboBox comboFirst)
                {
                    comboFirst.AddHandler(System.Windows.Controls.Primitives.TextBoxBase.TextChangedEvent, new TextChangedEventHandler((_, _) => line.First = comboFirst.Text));
                    comboFirst.SelectionChanged += (_, _) => line.First = comboFirst.SelectedItem as string ?? comboFirst.Text;
                }

                grid.Children.Add(first);
                if (secondHeader is not null)
                {
                    FrameworkElement second;
                    if (secondOptions is not null)
                    {
                        var combo = new ComboBox { Margin = new Thickness(6, 0, 0, 0) };
                        foreach (var option in secondOptions)
                        {
                            combo.Items.Add(option);
                        }

                        combo.SelectedItem = line.Second;
                        combo.SelectionChanged += (_, _) => line.Second = combo.SelectedItem as string ?? "";
                        second = combo;
                    }
                    else
                    {
                        var box = new TextBox { Text = line.Second, Margin = new Thickness(6, 0, 0, 0) };
                        box.TextChanged += (_, _) => line.Second = box.Text;
                        second = box;
                    }

                    Grid.SetColumn(second, 1);
                    grid.Children.Add(second);
                }

                if (thirdHeader is not null)
                {
                    var third = new TextBox { Text = line.Third, Margin = new Thickness(6, 0, 0, 0), ToolTip = "Separate several values with commas" };
                    third.TextChanged += (_, _) => line.Third = third.Text;
                    Grid.SetColumn(third, 2);
                    grid.Children.Add(third);
                }

                var remove = new Button { Content = "✕", Margin = new Thickness(6, 0, 0, 0), Padding = new Thickness(6, 1, 6, 1), ToolTip = "Delete" };
                remove.SetResourceReference(StyleProperty, "Safi.OutlinedButton");
                remove.Click += (_, _) =>
                {
                    lines.Remove(line);
                    Rebuild();
                };
                Grid.SetColumn(remove, 3);
                grid.Children.Add(remove);
                rows.Children.Add(grid);
            }
        }

        Rebuild();
        var add = new Button { Content = "Add", Padding = new Thickness(12, 4, 12, 4), Margin = new Thickness(0, 6, 0, 0), HorizontalAlignment = HorizontalAlignment.Left };
        add.SetResourceReference(StyleProperty, "Safi.OutlinedButton");
        add.Click += (_, _) =>
        {
            lines.Add(create());
            Rebuild();
        };
        return new StackPanel { Margin = new Thickness(12), Children = { new ScrollViewer { MaxHeight = 260, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = rows }, add } };

        void Columns(Grid grid)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = secondHeader is null ? new GridLength(0) : new GridLength(1.2, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = thirdHeader is null ? new GridLength(0) : new GridLength(1.4, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        }

        static TextBlock Caption(string text, int column)
        {
            var caption = new TextBlock { Text = text, FontWeight = FontWeights.SemiBold, Margin = new Thickness(column == 0 ? 0 : 6, 0, 0, 0) };
            Grid.SetColumn(caption, column);
            return caption;
        }
    }

    private static TabItem Tab(string header, UIElement content) =>
        new() { Header = header, Content = new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = content } };

    private sealed class Line(string first, string second, string third)
    {
        public string First { get; set; } = first;

        public string Second { get; set; } = second;

        public string Third { get; set; } = third;
    }
}
