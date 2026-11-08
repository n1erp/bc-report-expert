using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using Button = System.Windows.Controls.Button;
using Control = System.Windows.Controls.Control;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TreeView = System.Windows.Controls.TreeView;

namespace ReportExpert.App.Reports;

/// <summary>
/// Report Builder's Expression dialog: a large editor for one property, with the building blocks beside it —
/// the dataset's fields, the parameters, the built-in fields, operators and the common functions. Double-click
/// (or Enter) inserts the selected item at the caret; the description under the list says what it does.
/// </summary>
public sealed class ExpressionEditorDialog : Window
{
    private static readonly (string Category, (string Name, string Insert, string Help)[] Items)[] Functions =
    [
        ("Operators", [
            ("&", " & ", "Joins two values as text."), ("+", " + ", "Adds numbers (joins text, but fails when one side is a number)."),
            ("=", " = ", "Equal to."), ("<>", " <> ", "Not equal to."), ("And", " And ", "Both conditions are true."), ("Or", " Or ", "Either condition is true."),
            ("Not", "Not ", "Reverses a condition."), ("vbCrLf", " & vbCrLf & ", "A line break inside the text."),
        ]),
        ("Program flow", [
            ("IIf", "IIf(condition, whenTrue, whenFalse)", "Returns one of two values depending on a condition."),
            ("Switch", "Switch(condition1, value1, condition2, value2)", "Returns the value of the first true condition."),
            ("Choose", "Choose(index, value1, value2)", "Returns the value at a 1-based position."),
            ("IsNothing", "IsNothing(value)", "True when the value is missing (null)."),
        ]),
        ("Aggregate", [
            ("Sum", "Sum(Fields!Field.Value)", "Total of the values in the current scope."), ("Avg", "Avg(Fields!Field.Value)", "Average of the values."),
            ("Count", "Count(Fields!Field.Value)", "Number of values."), ("CountDistinct", "CountDistinct(Fields!Field.Value)", "Number of different values."),
            ("Min", "Min(Fields!Field.Value)", "Smallest value."), ("Max", "Max(Fields!Field.Value)", "Largest value."),
            ("First", "First(Fields!Field.Value, \"DataSet_Result\")", "First value; with a dataset name, works outside a data region."),
            ("Last", "Last(Fields!Field.Value)", "Last value."), ("RunningValue", "RunningValue(Fields!Field.Value, Sum, Nothing)", "Running total."),
            ("RowNumber", "RowNumber(Nothing)", "Row number in the scope."), ("Lookup", "Lookup(source, destination, result, \"DataSet\")", "Value from another dataset."),
        ]),
        ("Text", [
            ("Format", "Format(value, \"#,##0.00\")", "Formats a number or date with a .NET format string."),
            ("FormatNumber", "FormatNumber(value, 2)", "Number with a fixed number of decimals."), ("FormatDateTime", "FormatDateTime(value, DateFormat.ShortDate)", "Date in a standard format."),
            ("Left", "Left(text, 3)", "First characters."), ("Right", "Right(text, 3)", "Last characters."), ("Mid", "Mid(text, 2, 3)", "Characters from a position."),
            ("Len", "Len(text)", "Length of the text."), ("Trim", "Trim(text)", "Removes spaces at both ends."), ("UCase", "UCase(text)", "Upper case."),
            ("LCase", "LCase(text)", "Lower case."), ("Replace", "Replace(text, \"old\", \"new\")", "Replaces text."), ("InStr", "InStr(text, \"part\")", "Position of a part (0 when absent)."),
        ]),
        ("Conversion", [
            ("CStr", "CStr(value)", "To text."), ("CDec", "CDec(value)", "To a decimal number."), ("CInt", "CInt(value)", "To a whole number."),
            ("CDate", "CDate(value)", "To a date."), ("CBool", "CBool(value)", "To true/false."), ("Val", "Val(text)", "Number at the start of the text."),
        ]),
        ("Date & time", [
            ("Today", "Today()", "Today's date."), ("Now", "Now()", "Current date and time."), ("Year", "Year(date)", "Year of a date."),
            ("Month", "Month(date)", "Month of a date (1–12)."), ("Day", "Day(date)", "Day of the month."), ("DateAdd", "DateAdd(DateInterval.Day, 1, date)", "Adds to a date."),
            ("DateDiff", "DateDiff(DateInterval.Day, start, end)", "Difference between two dates."), ("MonthName", "MonthName(Month(date))", "Name of a month."),
        ]),
    ];

    private readonly TextEditor _editor = new()
    {
        ShowLineNumbers = true,
        WordWrap = true,
        FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"),
        FontSize = 13.5,
        Padding = new Thickness(6),
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
    };

    private readonly TreeView _tree = new() { BorderThickness = new Thickness(0), Background = System.Windows.Media.Brushes.Transparent };
    private readonly TextBlock _help = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Opacity = 0.8, Margin = new Thickness(0, 8, 0, 0), MinHeight = 34 };
    private readonly TextBlock _status = new() { FontSize = 12, Margin = new Thickness(0, 6, 0, 0), TextWrapping = TextWrapping.Wrap };

    public ExpressionEditorDialog(string title, string value, ExpressionContext context)
    {
        Title = "Expression — " + title;
        Width = 980;
        Height = 620;
        MinWidth = 700;
        MinHeight = 420;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        SetResourceReference(BackgroundProperty, "Safi.SurfaceBrush");
        SetResourceReference(ForegroundProperty, "Safi.TextPrimaryBrush");
        FontFamily = (System.Windows.Media.FontFamily)FindResource("Safi.FontFamily");

        _editor.Text = value;
        Theming.SafiXmlHighlighting.AttachVb(_editor);
        _editor.SetResourceReference(Control.BackgroundProperty, "Safi.BackgroundBrush");
        _editor.SetResourceReference(Control.ForegroundProperty, "Safi.TextPrimaryBrush");
        _editor.SetResourceReference(TextEditor.LineNumbersForegroundProperty, "Safi.TextSecondaryBrush");
        _editor.TextChanged += (_, _) => Check();

        BuildTree(context);
        _tree.SelectedItemChanged += (_, _) => _help.Text = (_tree.SelectedItem as TreeViewItem)?.ToolTip as string ?? "";
        _tree.MouseDoubleClick += (_, e) => { if (InsertSelected()) e.Handled = true; };
        _tree.KeyDown += (_, e) => { if (e.Key == Key.Enter && InsertSelected()) e.Handled = true; };

        var hint = new TextBlock
        {
            Text = "Start with = for an expression (=Fields!Amount.Value); without = the text is shown as typed.",
            FontSize = 12, Opacity = 0.7, Margin = new Thickness(0, 0, 0, 6),
        };
        var editorPanel = new DockPanel();
        DockPanel.SetDock(hint, Dock.Top);
        DockPanel.SetDock(_status, Dock.Bottom);
        editorPanel.Children.Add(hint);
        editorPanel.Children.Add(_status);
        editorPanel.Children.Add(new Border { Child = _editor, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6) }.WithBorder("Safi.DividerBrush"));

        var listPanel = new DockPanel { Margin = new Thickness(16, 0, 0, 0) };
        var listTitle = new TextBlock { Text = "Insert", FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        DockPanel.SetDock(listTitle, Dock.Top);
        DockPanel.SetDock(_help, Dock.Bottom);
        listPanel.Children.Add(listTitle);
        listPanel.Children.Add(_help);
        listPanel.Children.Add(new Border { Child = _tree, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(6), Padding = new Thickness(4) }.WithBorder("Safi.DividerBrush"));

        var body = new Grid { Margin = new Thickness(18, 16, 18, 4) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 260 });
        body.Children.Add(editorPanel);
        Grid.SetColumn(listPanel, 1);
        body.Children.Add(listPanel);

        var ok = new Button { Content = "OK", IsDefault = false, Padding = new Thickness(22, 7, 22, 7), Margin = new Thickness(0, 0, 8, 0), ToolTip = "Ctrl+Enter" };
        ok.SetResourceReference(StyleProperty, "Safi.PrimaryButton");
        ok.Click += (_, _) => DialogResult = true;
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(18, 7, 18, 7) };
        cancel.SetResourceReference(StyleProperty, "Safi.OutlinedButton");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(18, 12, 18, 16), Children = { ok, cancel } };

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(body);
        Content = root;

        // Enter is a new line in the editor; Ctrl+Enter confirms.
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter && Keyboard.Modifiers == ModifierKeys.Control)
            {
                DialogResult = true;
                e.Handled = true;
            }
        };
        Loaded += (_, _) =>
        {
            _editor.Focus();
            _editor.CaretOffset = _editor.Text.Length;
        };
        Check();
    }

    /// <summary>Shows the editor; returns the new value, or null when cancelled.</summary>
    public static string? Ask(Window? owner, string title, string value, ExpressionContext context)
    {
        var dialog = new ExpressionEditorDialog(title, value, context) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog._editor.Text : null;
    }

    private void BuildTree(ExpressionContext context)
    {
        TreeViewItem Folder(string name, bool expanded = false) => new() { Header = name, IsExpanded = expanded, FontWeight = FontWeights.SemiBold };
        TreeViewItem Leaf(string name, string insert, string help) => new() { Header = name, Tag = insert, ToolTip = help, FontWeight = FontWeights.Normal };

        var fields = Folder("Fields", expanded: true);
        foreach (var group in context.Fields.GroupBy(field => field.DataSet))
        {
            var parent = context.Fields.Select(f => f.DataSet).Distinct().Count() > 1 ? Folder(group.Key, expanded: true) : fields;
            foreach (var (_, field) in group.OrderBy(f => f.Field, StringComparer.OrdinalIgnoreCase))
            {
                parent.Items.Add(Leaf(field, $"Fields!{field}.Value", $"=Fields!{field}.Value — the value in the current row ({group.Key})."));
            }

            if (parent != fields)
            {
                fields.Items.Add(parent);
            }
        }

        _tree.Items.Add(fields);

        var parameters = Folder("Parameters");
        foreach (var name in context.Parameters)
        {
            parameters.Items.Add(Leaf(name, $"Parameters!{name}.Value", $"=Parameters!{name}.Value — the value chosen for this parameter (.Label for its caption)."));
        }

        _tree.Items.Add(parameters);

        var builtIn = Folder("Built-in Fields");
        foreach (var (name, expression) in ReportCenterViewModel.BuiltInFields)
        {
            builtIn.Items.Add(Leaf(name, expression[1..], expression + " — available in any text box, including the page header and footer."));
        }

        _tree.Items.Add(builtIn);

        foreach (var (category, items) in Functions)
        {
            var folder = Folder(category);
            foreach (var (name, insert, help) in items)
            {
                folder.Items.Add(Leaf(name, insert, help + "  " + insert));
            }

            _tree.Items.Add(folder);
        }

        if (context.HasCustomCode)
        {
            var code = Folder("Custom code");
            code.Items.Add(Leaf("Code.", "Code.", "Calls a function from the report's Code block, for example Code.GetData(1, 1)."));
            _tree.Items.Add(code);
        }
    }

    private bool InsertSelected()
    {
        if (_tree.SelectedItem is not TreeViewItem { Tag: string insert })
        {
            return false;
        }

        if (_editor.Text.Length == 0)
        {
            insert = "=" + insert;
        }

        _editor.Document.Insert(_editor.CaretOffset, insert);
        _editor.Focus();
        return true;
    }

    /// <summary>Quick syntax checks: balanced parentheses and quotes. The report processor does the full check at preview.</summary>
    private void Check()
    {
        var text = _editor.Text;
        var depth = 0;
        var inString = false;
        foreach (var ch in text)
        {
            if (ch == '"')
            {
                inString = !inString;
            }
            else if (!inString)
            {
                depth += ch == '(' ? 1 : ch == ')' ? -1 : 0;
                if (depth < 0)
                {
                    break;
                }
            }
        }

        var problem = inString ? "A text in quotes is not closed." : depth > 0 ? $"{depth} parenthesis(es) not closed." : depth < 0 ? "A closing parenthesis has no opening one." : null;
        _status.Text = problem ?? (text.TrimStart().StartsWith('=') ? "Expression looks well-formed. Preview runs the full check." : "Plain text (no =): shown exactly as typed.");
        _status.SetResourceReference(TextBlock.ForegroundProperty, problem is null ? "Safi.TextSecondaryBrush" : "Safi.ErrorBrush");
    }
}

internal static class BorderExtensions
{
    public static Border WithBorder(this Border border, string brushKey)
    {
        border.SetResourceReference(Border.BorderBrushProperty, brushKey);
        return border;
    }
}
