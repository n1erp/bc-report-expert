using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using ReportExpert.Reporting.Design;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using RadioButton = System.Windows.Controls.RadioButton;
using Rectangle = System.Windows.Shapes.Rectangle;
using TextBox = System.Windows.Controls.TextBox;

namespace ReportExpert.App.Reports;

/// <summary>
/// Report Builder's Report Properties › Page Setup: paper size, orientation, width and height, the four margins
/// and columns, in the report's own unit. A live preview draws the page, the margins and the body; when the
/// body is wider than the printable width (the cause of blank pages) the body turns red and the rule is shown.
/// </summary>
public sealed class PageSetupDialog : Window
{
    private readonly bool _metric;
    private readonly double _bodyWidth;
    private readonly ComboBox _paper = new() { MinWidth = 180 };
    private readonly RadioButton _portrait = new() { Content = "Portrait", GroupName = "orientation", Margin = new Thickness(0, 0, 16, 0) };
    private readonly RadioButton _landscape = new() { Content = "Landscape", GroupName = "orientation" };
    private readonly TextBox _width = Box();
    private readonly TextBox _height = Box();
    private readonly TextBox _top = Box();
    private readonly TextBox _bottom = Box();
    private readonly TextBox _left = Box();
    private readonly TextBox _right = Box();
    private readonly TextBox _columns = Box();
    private readonly TextBox _spacing = Box();
    private readonly Canvas _preview = new() { Width = 230, Height = 260 };
    private readonly TextBlock _check = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 10, 0, 0), MaxWidth = 240 };
    private bool _updating;

    public PageSetupDialog(DesignPageSetup page, double bodyWidthInches)
    {
        _metric = page.IsMetric;
        _bodyWidth = bodyWidthInches;
        Title = "Page setup";
        Width = 640;
        SizeToContent = SizeToContent.Height;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.NoResize;
        SetResourceReference(BackgroundProperty, "Safi.SurfaceBrush");
        SetResourceReference(ForegroundProperty, "Safi.TextPrimaryBrush");
        FontFamily = (System.Windows.Media.FontFamily)FindResource("Safi.FontFamily");

        foreach (var (name, _, _) in ReportPageSetup.PaperSizes)
        {
            _paper.Items.Add(name);
        }

        _paper.Items.Add("Custom");

        var unit = _metric ? "cm" : "in";
        var form = new StackPanel { Margin = new Thickness(0, 0, 24, 0), MinWidth = 320 };
        form.Children.Add(Heading("Paper"));
        form.Children.Add(Row("Size", _paper));
        form.Children.Add(Row("Orientation", new StackPanel { Orientation = Orientation.Horizontal, Children = { _portrait, _landscape } }));
        form.Children.Add(Row($"Width ({unit})", _width));
        form.Children.Add(Row($"Height ({unit})", _height));
        form.Children.Add(Heading("Margins"));
        form.Children.Add(Pair(("Top", _top), ("Bottom", _bottom), unit));
        form.Children.Add(Pair(("Left", _left), ("Right", _right), unit));
        form.Children.Add(Heading("Columns"));
        form.Children.Add(Pair(("Columns", _columns), ("Spacing", _spacing), unit, firstUnit: false));

        var previewPanel = new StackPanel();
        previewPanel.Children.Add(Heading("Preview"));
        previewPanel.Children.Add(new Border { Child = _preview, Padding = new Thickness(6), CornerRadius = new CornerRadius(6), Background = Res("Safi.BackgroundBrush") });
        previewPanel.Children.Add(_check);

        var body = new Grid { Margin = new Thickness(22, 16, 22, 4) };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition());
        body.Children.Add(form);
        Grid.SetColumn(previewPanel, 1);
        body.Children.Add(previewPanel);

        var ok = new Button { Content = "Apply", IsDefault = true, Padding = new Thickness(18, 7, 18, 7), Margin = new Thickness(0, 0, 8, 0) };
        ok.SetResourceReference(StyleProperty, "Safi.PrimaryButton");
        ok.Click += (_, _) =>
        {
            if (Read() is null)
            {
                _check.Text = "Enter positive numbers for the size, and margins of zero or more.";
                _check.Foreground = Res("Safi.ErrorBrush");
                return;
            }

            DialogResult = true;
        };
        var cancel = new Button { Content = "Cancel", IsCancel = true, Padding = new Thickness(18, 7, 18, 7) };
        cancel.SetResourceReference(StyleProperty, "Safi.OutlinedButton");
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(22, 14, 22, 18), Children = { ok, cancel } };

        var root = new DockPanel();
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(buttons);
        root.Children.Add(body);
        Content = root;

        Load(page);
        _paper.SelectionChanged += (_, _) => OnPaperChanged();
        _portrait.Checked += (_, _) => OnOrientation(landscape: false);
        _landscape.Checked += (_, _) => OnOrientation(landscape: true);
        foreach (var box in new[] { _width, _height, _top, _bottom, _left, _right, _columns, _spacing })
        {
            box.TextChanged += (_, _) => OnEdited(box);
        }

        Redraw();
    }

    /// <summary>The page setup entered, or null while a value is invalid.</summary>
    public DesignPageSetup? Result { get; private set; }

    /// <summary>Shows the dialog; returns the new page setup, or null when cancelled.</summary>
    public static DesignPageSetup? Ask(Window? owner, DesignPageSetup page, double bodyWidthInches)
    {
        var dialog = new PageSetupDialog(page, bodyWidthInches) { Owner = owner };
        return dialog.ShowDialog() == true ? dialog.Read() : null;
    }

    private void Load(DesignPageSetup page)
    {
        _updating = true;
        _paper.SelectedItem = _paper.Items.Contains(page.PaperSize) ? page.PaperSize : "Custom";
        (page.PageWidthInches > page.PageHeightInches ? _landscape : _portrait).IsChecked = true;
        Set(_width, page.PageWidthInches);
        Set(_height, page.PageHeightInches);
        Set(_top, page.TopMarginInches);
        Set(_bottom, page.BottomMarginInches);
        Set(_left, page.LeftMarginInches);
        Set(_right, page.RightMarginInches);
        _columns.Text = page.Columns.ToString(CultureInfo.InvariantCulture);
        Set(_spacing, page.ColumnSpacingInches);
        _updating = false;
    }

    private DesignPageSetup? Read()
    {
        var width = Inches(_width);
        var height = Inches(_height);
        var top = Inches(_top);
        var bottom = Inches(_bottom);
        var left = Inches(_left);
        var right = Inches(_right);
        var spacing = Inches(_spacing);
        if (width is not > 0 || height is not > 0 || top is not >= 0 || bottom is not >= 0 || left is not >= 0 || right is not >= 0
            || spacing is not >= 0
            || !int.TryParse(_columns.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var columns) || columns is < 1 or > 10)
        {
            return null;
        }

        return new DesignPageSetup(
            _paper.SelectedItem as string ?? "Custom",
            width > height ? "Landscape" : "Portrait",
            width.Value, height.Value, left.Value, right.Value, top.Value, bottom.Value,
            columns, spacing.Value, _metric ? "Cm" : "Inch");
    }

    private void OnPaperChanged()
    {
        if (_updating || _paper.SelectedItem is not string name || name == "Custom")
        {
            return;
        }

        var (_, w, h) = ReportPageSetup.PaperSizes.First(size => size.Name == name);
        var landscape = _landscape.IsChecked == true;
        _updating = true;
        Set(_width, landscape ? h : w);
        Set(_height, landscape ? w : h);
        _updating = false;
        Redraw();
    }

    private void OnOrientation(bool landscape)
    {
        if (_updating || Inches(_width) is not { } width || Inches(_height) is not { } height || (width > height) == landscape)
        {
            Redraw();
            return;
        }

        _updating = true;
        Set(_width, height);
        Set(_height, width);
        _updating = false;
        Redraw();
    }

    private void OnEdited(TextBox box)
    {
        if (_updating)
        {
            return;
        }

        if (box == _width || box == _height)
        {
            // Typing a size that matches no standard paper makes it Custom; a matching one selects it.
            _updating = true;
            _paper.SelectedItem = MatchPaper() ?? "Custom";
            if (Inches(_width) is { } w && Inches(_height) is { } h)
            {
                (w > h ? _landscape : _portrait).IsChecked = true;
            }

            _updating = false;
        }

        Redraw();
    }

    private string? MatchPaper()
    {
        if (Inches(_width) is not { } w || Inches(_height) is not { } h)
        {
            return null;
        }

        var (shortSide, longSide) = (Math.Min(w, h), Math.Max(w, h));
        return ReportPageSetup.PaperSizes.FirstOrDefault(size => Math.Abs(size.Width - shortSide) < 0.05 && Math.Abs(size.Height - longSide) < 0.05).Name;
    }

    private void Redraw()
    {
        _preview.Children.Clear();
        if (Read() is not { } page)
        {
            _check.Text = "Enter positive numbers.";
            _check.Foreground = Res("Safi.ErrorBrush");
            return;
        }

        var scale = Math.Min((_preview.Width - 10) / page.PageWidthInches, (_preview.Height - 10) / page.PageHeightInches);
        var (pw, ph) = (page.PageWidthInches * scale, page.PageHeightInches * scale);
        var (ox, oy) = ((_preview.Width - pw) / 2, (_preview.Height - ph) / 2);
        Add(new Rectangle { Width = pw, Height = ph, Fill = Brushes.White, Stroke = Res("Safi.DividerBrush"), StrokeThickness = 1 }, ox, oy);

        var printable = new Rect(ox + page.LeftMarginInches * scale, oy + page.TopMarginInches * scale,
            Math.Max(0, pw - (page.LeftMarginInches + page.RightMarginInches) * scale), Math.Max(0, ph - (page.TopMarginInches + page.BottomMarginInches) * scale));
        Add(new Rectangle { Width = printable.Width, Height = printable.Height, Stroke = Res("Safi.PrimaryLightenBrush"), StrokeDashArray = [4, 3], StrokeThickness = 1 }, printable.X, printable.Y);

        var overflow = ReportPageSetup.BlankPageWarning(page, _bodyWidth) is not null;
        var bodyBrush = overflow ? Res("Safi.ErrorBrush") : Res("Safi.PrimaryLightenBrush");
        for (var c = 0; c < page.Columns; c++)
        {
            var x = printable.X + c * (page.MaxBodyWidthInches + page.ColumnSpacingInches) * scale;
            Add(new Rectangle { Width = Math.Max(2, _bodyWidth * scale), Height = Math.Min(printable.Height, 40), Fill = bodyBrush, Opacity = 0.35 }, x, printable.Y);
        }

        string L(double inches) => ReportPageSetup.Format(inches, _metric);
        _check.Foreground = overflow ? Res("Safi.ErrorBrush") : Res("Safi.TextSecondaryBrush");
        _check.Text = overflow
            ? $"Body {L(_bodyWidth)} is wider than the {L(page.MaxBodyWidthInches)} available — the report will print blank pages. " +
              "Rule: body width ≤ page width − (left + right margin). Narrow the margins here, or use Fit body to page after applying."
            : $"Body {L(_bodyWidth)} fits: {L(page.MaxBodyWidthInches)} available between the margins.";
    }

    private void Add(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        _preview.Children.Add(element);
    }

    private double? Inches(TextBox box)
    {
        var text = box.Text.Trim().Replace(',', '.');
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            ? (_metric ? value / 2.54 : value)
            : null;
    }

    private void Set(TextBox box, double inches) =>
        box.Text = (_metric ? inches * 2.54 : inches).ToString(_metric ? "0.##" : "0.###", CultureInfo.InvariantCulture);

    private static TextBox Box() => new() { MinWidth = 90, Padding = new Thickness(6, 4, 6, 4) };

    private static TextBlock Heading(string text) =>
        new() { Text = text, FontSize = 13, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 10, 0, 6) };

    private static FrameworkElement Row(string label, FrameworkElement input)
    {
        var grid = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(110) });
        grid.ColumnDefinitions.Add(new ColumnDefinition());
        grid.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 });
        Grid.SetColumn(input, 1);
        input.HorizontalAlignment = HorizontalAlignment.Left;
        grid.Children.Add(input);
        return grid;
    }

    private static FrameworkElement Pair((string Label, TextBox Box) first, (string Label, TextBox Box) second, string unit, bool firstUnit = true)
    {
        var panel = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 0, 8) };
        panel.Children.Add(new TextBlock { Text = firstUnit ? $"{first.Label} ({unit})" : first.Label, Width = 110, VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 });
        panel.Children.Add(first.Box);
        panel.Children.Add(new TextBlock { Text = $"{second.Label} ({unit})", Margin = new Thickness(16, 0, 8, 0), VerticalAlignment = VerticalAlignment.Center, Opacity = 0.8 });
        panel.Children.Add(second.Box);
        return panel;
    }

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
}
