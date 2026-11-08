using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using ReportExpert.Reporting.Design;
using Brush = System.Windows.Media.Brush;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using DragEventArgs = System.Windows.DragEventArgs;
using DragDropEffects = System.Windows.DragDropEffects;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Cursors = System.Windows.Input.Cursors;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Rectangle = System.Windows.Shapes.Rectangle;
using ContextMenu = System.Windows.Controls.ContextMenu;
using MenuItem = System.Windows.Controls.MenuItem;

namespace ReportExpert.App.Reports;

/// <summary>
/// The REX V3 design surface: rulers in pixels at 96 dpi, the page header, body and footer bands drawn from the
/// <see cref="ReportDesignModel"/>, and the report items on them. Items are selected by click (Ctrl adds),
/// moved by dragging, resized from the corner handle; bands resize from their bottom edge. Toolbox items and
/// dataset fields are dropped onto a band. Every gesture is committed through <see cref="ReportCenterViewModel"/>.
/// </summary>
public sealed class DesignSurfaceControl : Grid
{
    public const string ToolDragFormat = "rex-design-kind";
    private const double Dpi = 96;
    private const double RulerSize = 22;
    private const double Snap = 1.0 / 16;

    private readonly Canvas _canvas = new() { ClipToBounds = false, Background = Brushes.Transparent, AllowDrop = true };
    private readonly Canvas _hRuler = new() { Height = RulerSize, ClipToBounds = true };
    private readonly Canvas _vRuler = new() { Width = RulerSize, ClipToBounds = true };
    private readonly ScrollViewer _scroll;
    private readonly List<(string Region, double Top, double Height)> _bands = [];

    private ReportCenterViewModel? _model;
    private FrameworkElement? _dragging;
    private Point _dragStart;
    private Rect _dragOrigin;
    private bool _resizing;
    private string? _bandResizing;
    private readonly List<UIElement> _guides = [];
    private (double Left, double Top, bool X, bool Y)? _snapped;

    public DesignSurfaceControl()
    {
        ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(RulerSize) });
        ColumnDefinitions.Add(new ColumnDefinition());
        RowDefinitions.Add(new RowDefinition { Height = new GridLength(RulerSize) });
        RowDefinitions.Add(new RowDefinition());

        var host = new Border { Padding = new Thickness(24), Child = _canvas, HorizontalAlignment = HorizontalAlignment.Left, VerticalAlignment = VerticalAlignment.Top };
        _scroll = new ScrollViewer { HorizontalScrollBarVisibility = ScrollBarVisibility.Auto, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Content = host };
        _scroll.ScrollChanged += (_, _) => DrawRulers();
        SetRow(_scroll, 1);
        SetColumn(_scroll, 1);
        SetRow(_hRuler, 0);
        SetColumn(_hRuler, 1);
        SetRow(_vRuler, 1);
        SetColumn(_vRuler, 0);
        Children.Add(_hRuler);
        Children.Add(_vRuler);
        Children.Add(_scroll);

        _canvas.Drop += OnDrop;
        _canvas.DragOver += (_, e) => e.Effects = e.Data.GetDataPresent(ToolDragFormat) ? DragDropEffects.Copy : DragDropEffects.None;
        _canvas.MouseMove += OnMouseMove;
        _canvas.MouseLeftButtonUp += OnMouseUp;
        DataContextChanged += (_, _) => Attach(DataContext as ReportCenterViewModel);
        SizeChanged += (_, _) => DrawRulers();
        Theming.SafiThemeManager.PaletteChanged += Redraw;
    }

    private double Scale => Dpi * (_model?.Zoom ?? 1);

    private void Attach(ReportCenterViewModel? model)
    {
        if (_model is not null)
        {
            _model.DesignChanged -= Redraw;
        }

        _model = model;
        if (_model is not null)
        {
            _model.DesignChanged += Redraw;
        }

        Redraw();
    }

    private void Redraw()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(Redraw);
            return;
        }

        _canvas.Children.Clear();
        _bands.Clear();
        var report = _model?.Current;
        if (report is null)
        {
            _canvas.Width = _canvas.Height = 0;
            DrawRulers();
            return;
        }

        var width = Math.Max(report.BodyWidthInches, 1) * Scale;
        var limit = report.Page?.MaxBodyWidthInches ?? 0;
        double top = 0;
        if (report.HasHeader)
        {
            top = DrawBand(report, "Header", report.HeaderHeightInches, top, width);
        }

        top = DrawBand(report, "Body", report.BodyHeightInches, top, width);
        if (report.HasFooter)
        {
            top = DrawBand(report, "Footer", report.FooterHeightInches, top, width);
        }

        DrawPageLimit(report, limit, top);
        _canvas.Width = Math.Max(width, limit * Scale + 2);
        _canvas.Height = top;
        DrawRulers();
    }

    /// <summary>
    /// A dashed line where the printable width (page − margins) ends. A body that reaches past it prints extra
    /// blank pages; the part beyond the line is shaded so the overflow is obvious.
    /// </summary>
    private void DrawPageLimit(ReportDesignModel report, double limitInches, double height)
    {
        if (limitInches <= 0 || report.Page is not { } page)
        {
            return;
        }

        var x = limitInches * Scale;
        var overflow = report.BodyWidthInches > limitInches + 0.005;
        var brush = overflow ? Res("Safi.ErrorBrush") : Res("Safi.PrimaryLightenBrush");
        if (overflow)
        {
            var shade = new Rectangle { Width = (report.BodyWidthInches - limitInches) * Scale, Height = height, Fill = brush, Opacity = 0.08, IsHitTestVisible = false };
            Place(shade, x, 0);
        }

        Place(new Line { X1 = 0, X2 = 0, Y1 = 0, Y2 = height, Stroke = brush, StrokeThickness = 1.2, StrokeDashArray = [6, 4], IsHitTestVisible = false, Opacity = overflow ? 0.9 : 0.55 }, x, 0);
        var label = new Border
        {
            Background = brush,
            CornerRadius = new CornerRadius(0, 0, 4, 4),
            Padding = new Thickness(6, 1, 6, 2),
            IsHitTestVisible = false,
            Child = new TextBlock
            {
                Text = (overflow ? "Page limit exceeded · " : "Page limit · ") + ReportPageSetup.Format(limitInches, page.IsMetric),
                FontSize = 10,
                Foreground = overflow ? Brushes.White : Res("Safi.PrimaryContrastTextBrush"),
            },
            ToolTip = "Body width must not exceed page width − (left + right margin), or the report prints extra blank pages.",
        };
        Place(label, x + 3, 0);
    }

    private double DrawBand(ReportDesignModel report, string region, double heightInches, double top, double width)
    {
        var height = Math.Max(heightInches, 0.1) * Scale;
        _bands.Add((region, top, height));

        var band = new Rectangle
        {
            Width = width,
            Height = height,
            Fill = ShowGridBrush(),
            Stroke = Res("Safi.DividerBrush"),
            StrokeThickness = 1,
        };
        band.MouseLeftButtonDown += (_, e) =>
        {
            _ = _model?.SelectAsync(null);
            e.Handled = true;
        };
        Place(band, 0, top);

        if (region != "Body")
        {
            var tag = new Border
            {
                Background = Res("Safi.PrimaryLightenBrush"),
                CornerRadius = new CornerRadius(0, 0, 6, 0),
                Padding = new Thickness(8, 1, 8, 2),
                Child = new TextBlock { Text = region == "Header" ? "Header" : "Footer", FontSize = 11, Foreground = Res("Safi.PrimaryContrastTextBrush") },
                IsHitTestVisible = false,
            };
            Place(tag, 0, top);
        }

        foreach (var item in report.Items.Where(item => item.Region == region && (_model?.ShowHiddenItems != false || !item.Hidden)))
        {
            DrawItem(item, top);
        }

        var grip = new Rectangle { Width = width, Height = 5, Fill = Res("Safi.PrimaryLightenBrush"), Opacity = 0.55, Cursor = Cursors.SizeNS, ToolTip = $"Drag to resize the {region.ToLowerInvariant()}" };
        grip.MouseLeftButtonDown += (_, e) =>
        {
            _bandResizing = region;
            _dragStart = e.GetPosition(_canvas);
            _dragOrigin = new Rect(0, top, width, height);
            _canvas.CaptureMouse();
            e.Handled = true;
        };
        Place(grip, 0, top + height - 2);
        return top + height;
    }

    private void DrawItem(DesignItem item, double bandTop)
    {
        var selected = _model?.SelectedItems.Contains(item.Name) == true;
        var element = Visual(item);
        element.Width = Math.Max(item.WidthInches * Scale, 4);
        element.Height = Math.Max(item.HeightInches * Scale, 4);
        element.Tag = item;
        element.Cursor = Cursors.SizeAll;
        element.ToolTip = ToolTipFor(item);
        if (item.Hidden)
        {
            // Hidden at run time (BC layouts park helper text boxes this way): dimmed so the visible layout reads clearly.
            element.Opacity = 0.35;
        }

        element.MouseLeftButtonDown += (_, e) =>
        {
            if (e.ClickCount == 2 && item.Kind == "Textbox" && _model is not null)
            {
                // Double-click edits the value in the expression editor, as Report Builder's placeholder dialog does.
                e.Handled = true;
                _ = _model.EditExpressionAsync("Value");
                return;
            }

            BeginDrag(element, e, resize: false);
        };
        var left = item.AbsoluteLeftInches * Scale;
        var top = bandTop + item.AbsoluteTopInches * Scale;
        Place(element, left, top);

        if (selected)
        {
            var outline = new Rectangle
            {
                Width = element.Width + 4,
                Height = element.Height + 4,
                Stroke = Res("Safi.SecondaryBrush"),
                StrokeThickness = 1.5,
                StrokeDashArray = [3, 2],
                IsHitTestVisible = false,
            };
            Place(outline, left - 2, top - 2);

            var handle = new Rectangle { Width = 9, Height = 9, Fill = Res("Safi.SecondaryBrush"), Cursor = Cursors.SizeNWSE, Tag = item };
            handle.MouseLeftButtonDown += (_, e) => BeginDrag(element, e, resize: true);
            Place(handle, left + element.Width - 4, top + element.Height - 4);
        }
    }

    private FrameworkElement Visual(DesignItem item)
    {
        var text = Res("Safi.TextPrimaryBrush");
        var caption = Res("Safi.TextSecondaryBrush");
        switch (item.Kind)
        {
            case "Line":
                var canvas = new Canvas { Background = Brushes.Transparent };
                canvas.Children.Add(new Line { X1 = 0, Y1 = 0, X2 = Math.Max(item.WidthInches * Scale, 1), Y2 = item.HeightInches * Scale, Stroke = text, StrokeThickness = 1.5 });
                return canvas;
            case "Tablix":
                return TableVisual(item, text, caption);
            case "Chart":
            case "Gauge":
            case "Image":
            case "Map":
                return new Border
                {
                    Background = Res("Safi.SurfaceBrush"),
                    BorderBrush = Res("Safi.DividerBrush"),
                    BorderThickness = new Thickness(1),
                    Child = new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        Children =
                        {
                            new Wpf.Ui.Controls.SymbolIcon { Symbol = item.Kind == "Image" ? Wpf.Ui.Controls.SymbolRegular.Image24 : Wpf.Ui.Controls.SymbolRegular.DataPie24, FontSize = 26, Foreground = Res("Safi.PrimaryLightenBrush"), HorizontalAlignment = HorizontalAlignment.Center },
                            new TextBlock { Text = Label(item), FontSize = 11, Foreground = caption, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) },
                        },
                    },
                };
            case "Rectangle":
            {
                var box = new Border { Background = Brushes.Transparent, BorderBrush = text, BorderThickness = new Thickness(1) };
                ApplyBox(box, item.Style, Res("Safi.TableLinesBrush"));
                return box;
            }

            default:
            {
                var block = new TextBlock
                {
                    Text = Display(item.Value),
                    Foreground = text,
                    FontWeight = item.Bold ? FontWeights.Bold : FontWeights.Normal,
                    FontSize = FontPixels(item.FontSize),
                    TextTrimming = TextTrimming.CharacterEllipsis,
                    TextAlignment = item.TextAlign switch { "Center" => TextAlignment.Center, "Right" => TextAlignment.Right, _ => TextAlignment.Left },
                };
                ApplyText(block, item.Style, VerticalAlignment.Top);
                var box = new Border
                {
                    Background = Brushes.Transparent,
                    BorderBrush = Res("Safi.TableLinesBrush"),
                    BorderThickness = new Thickness(1),
                    Padding = new Thickness(3, 1, 3, 1),
                    Child = block,
                };
                ApplyBox(box, item.Style, Res("Safi.TableLinesBrush"));
                return box;
            }
        }
    }

    /// <summary>
    /// A tablix drawn from its grid: corner, row-header columns, column-header rows and body cells with their
    /// spans, as a tablix designer shows it. The first click selects the data region (and drags it); once it
    /// is selected, a click selects a cell, a double-click edits the cell value and a right-click opens the
    /// tablix cell commands (insert row/column inside or outside a group, delete, merge, groups, totals).
    /// </summary>
    private Border TableVisual(DesignItem item, Brush text, Brush caption)
    {
        if (item.Grid is not { } layout || layout.ColumnWidths.Count == 0 || layout.RowHeights.Count == 0)
        {
            return LegacyTableVisual(item, text, caption);
        }

        var grid = new Grid();
        foreach (var width in layout.ColumnWidths)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(width, 0.05), GridUnitType.Star) });
        }

        foreach (var height in layout.RowHeights)
        {
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(Math.Max(height, 0.05), GridUnitType.Star) });
        }

        var selectedCell = _model?.SelectedCell;
        var tablixActive = _model?.SelectedTablix?.Name == item.Name;
        foreach (var cell in layout.Cells)
        {
            var rowPixels = layout.RowHeights.Skip(cell.Row).Take(Math.Max(1, cell.RowSpan)).Sum() * Scale;
            var header = cell.Area != TablixArea.Body;
            var isSelected = tablixActive && selectedCell is not null && selectedCell.TextboxName == cell.TextboxName && cell.TextboxName.Length > 0;
            var border = new Border
            {
                BorderBrush = isSelected ? Res("Safi.SecondaryBrush") : Res("Safi.TableLinesBrush"),
                BorderThickness = new Thickness(isSelected ? 2 : 0.5),
                Background = cell.Area == TablixArea.Corner ? Res("Safi.BackgroundBrush") : header ? Res("Safi.ChipBrush") : Brushes.Transparent,
                Child = CellText(cell, rowPixels, header ? text : caption, header),
                Tag = cell,
            };
            if (!isSelected)
            {
                ApplyBox(border, cell.Style, null);
            }
            else if (ColorBrush(cell.Style.BackgroundColor) is { } fill)
            {
                border.Background = fill;
            }
            SetRow(border, cell.Row);
            SetColumn(border, cell.Column);
            SetRowSpan(border, Math.Max(1, cell.RowSpan));
            SetColumnSpan(border, Math.Max(1, cell.ColSpan));
            border.MouseLeftButtonDown += (_, e) => OnCellDown(item, cell, e);
            border.MouseRightButtonUp += (_, e) => OnCellMenu(item, cell, border, e);
            grid.Children.Add(border);
        }

        return new Border { Background = Res("Safi.SurfaceBrush"), BorderBrush = Res("Safi.DividerBrush"), BorderThickness = new Thickness(1), Child = grid };
    }

    private static object CellToolTip(DesignGridCell cell)
    {
        var where = cell.Area switch
        {
            TablixArea.RowHeader => "Row header" + (cell.Group.Length > 0 ? " of group " + cell.Group : ""),
            TablixArea.ColumnHeader => "Column header" + (cell.Group.Length > 0 ? " of group " + cell.Group : ""),
            TablixArea.Corner => "Corner",
            _ => $"Row {cell.BodyRow + 1}, column {cell.BodyColumn + 1}" + (cell.ColSpan > 1 ? $" (merged ×{cell.ColSpan})" : ""),
        };
        return string.IsNullOrEmpty(cell.Value) ? where : where + "\n" + cell.Value;
    }

    private void OnCellDown(DesignItem item, DesignGridCell cell, MouseButtonEventArgs e)
    {
        if (_model is null || _model.SelectedTablix?.Name != item.Name || cell.TextboxName.Length == 0)
        {
            // First click: the data region itself (selected and dragged by the item handler).
            return;
        }

        e.Handled = true;
        if (e.ClickCount == 2)
        {
            _ = SelectThenAsync(cell.TextboxName, () => _model.EditExpressionAsync("Value"));
            return;
        }

        _ = _model.SelectAsync(cell.TextboxName);
    }

    private async Task SelectThenAsync(string name, Func<Task> then)
    {
        if (_model is null)
        {
            return;
        }

        if (_model.Current?.SelectedItem != name)
        {
            await _model.SelectAsync(name);
        }

        await then();
    }

    private void OnCellMenu(DesignItem item, DesignGridCell cell, FrameworkElement anchor, MouseButtonEventArgs e)
    {
        if (_model is null || cell.TextboxName.Length == 0)
        {
            return;
        }

        e.Handled = true;
        _ = OpenCellMenuAsync(cell, anchor);
    }

    private async Task OpenCellMenuAsync(DesignGridCell cell, FrameworkElement anchor)
    {
        var model = _model!;
        if (model.Current?.SelectedItem != cell.TextboxName)
        {
            await model.SelectAsync(cell.TextboxName);
        }

        var body = cell.Area == TablixArea.Body;
        var menu = new ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
        menu.Items.Add(Item("Text Box Properties...", null, () => model.EditExpressionAsync("Value"), "Edit the cell value in the expression editor"));
        menu.Items.Add(new Separator());
        var insertRow = Item("Insert Row", null, null);
        insertRow.Items.Add(Item("Inside Group - Above", "Row:InsideGroupBefore", null));
        insertRow.Items.Add(Item("Inside Group - Below", "Row:InsideGroupAfter", null));
        insertRow.Items.Add(Item("Outside Group - Above", "Row:OutsideGroupBefore", null));
        insertRow.Items.Add(Item("Outside Group - Below", "Row:OutsideGroupAfter", null));
        insertRow.IsEnabled = body;
        menu.Items.Add(insertRow);
        var insertColumn = Item("Insert Column", null, null);
        insertColumn.Items.Add(Item("Inside Group - Left", "Column:InsideGroupBefore", null));
        insertColumn.Items.Add(Item("Inside Group - Right", "Column:InsideGroupAfter", null));
        insertColumn.Items.Add(Item("Outside Group - Left", "Column:OutsideGroupBefore", null));
        insertColumn.Items.Add(Item("Outside Group - Right", "Column:OutsideGroupAfter", null));
        insertColumn.IsEnabled = body;
        menu.Items.Add(insertColumn);
        var delete = Item("Delete Rows", null, () => RunCommand(model.DeleteAtCellCommand, "Row"));
        delete.IsEnabled = body;
        menu.Items.Add(delete);
        var deleteColumns = Item("Delete Columns", null, () => RunCommand(model.DeleteAtCellCommand, "Column"));
        deleteColumns.IsEnabled = body;
        menu.Items.Add(deleteColumns);
        menu.Items.Add(new Separator());
        var merge = Item("Merge Cells", null, () => RunCommand(model.MergeRightCommand, null), "Merge with the cell to the right");
        merge.IsEnabled = body;
        menu.Items.Add(merge);
        var split = Item("Split Cells", null, () => RunCommand(model.SplitCellCommand, null));
        split.IsEnabled = body && cell.ColSpan > 1;
        menu.Items.Add(split);
        menu.Items.Add(new Separator());
        var addGroup = Item("Add Group", null, null);
        var rowGroup = Item("Row Group", null, null);
        rowGroup.Items.Add(Item("Parent Group...", null, () => RunCommand(model.AddRowGroupAtCellCommand, "Parent")));
        rowGroup.Items.Add(Item("Child Group...", null, () => RunCommand(model.AddRowGroupAtCellCommand, "Child")));
        rowGroup.Items.Add(Item("Adjacent Above...", null, () => RunCommand(model.AddRowGroupAtCellCommand, "AdjacentBefore")));
        rowGroup.Items.Add(Item("Adjacent Below...", null, () => RunCommand(model.AddRowGroupAtCellCommand, "AdjacentAfter")));
        var columnGroup = Item("Column Group", null, null);
        columnGroup.Items.Add(Item("Parent Group...", null, () => RunCommand(model.AddColumnGroupAtCellCommand, "Parent")));
        columnGroup.Items.Add(Item("Child Group...", null, () => RunCommand(model.AddColumnGroupAtCellCommand, "Child")));
        columnGroup.Items.Add(Item("Adjacent Left...", null, () => RunCommand(model.AddColumnGroupAtCellCommand, "AdjacentBefore")));
        columnGroup.Items.Add(Item("Adjacent Right...", null, () => RunCommand(model.AddColumnGroupAtCellCommand, "AdjacentAfter")));
        addGroup.Items.Add(rowGroup);
        addGroup.Items.Add(columnGroup);
        menu.Items.Add(addGroup);
        var addTotal = Item("Add Total", null, null);
        addTotal.Items.Add(Item("Row", null, () => RunCommand(model.AddTotalAtCellCommand, "Row")));
        addTotal.Items.Add(Item("Column", null, () => RunCommand(model.AddTotalAtCellCommand, "Column")));
        menu.Items.Add(addTotal);
        menu.Items.Add(Item("Group Properties...", null, () => RunCommand(model.GroupPropertiesCommand, null)));
        menu.IsOpen = true;

        MenuItem Item(string header, string? insertSpec, Func<Task>? action, string? tip = null)
        {
            var menuItem = new MenuItem { Header = header, ToolTip = tip };
            if (insertSpec is not null)
            {
                menuItem.Click += (_, _) => RunCommand(model.InsertAtCellCommand, insertSpec);
            }
            else if (action is not null)
            {
                menuItem.Click += async (_, _) => await action();
            }

            return menuItem;
        }
    }

    private static Task RunCommand(System.Windows.Input.ICommand command, object? parameter)
    {
        if (command is CommunityToolkit.Mvvm.Input.IAsyncRelayCommand asyncCommand)
        {
            return asyncCommand.ExecuteAsync(parameter);
        }

        command.Execute(parameter);
        return Task.CompletedTask;
    }

    private Border LegacyTableVisual(DesignItem item, Brush text, Brush caption)
    {
        var grid = new Grid();
        var columns = item.Columns ?? [];
        foreach (var column in columns)
        {
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(Math.Max(column.WidthInches, 0.1), GridUnitType.Star) });
        }

        var rows = item.Rows ?? [];
        for (var r = 0; r < rows.Count; r++)
        {
            grid.RowDefinitions.Add(new RowDefinition());
            for (var c = 0; c < rows[r].Cells.Count && c < Math.Max(columns.Count, 1); c++)
            {
                var header = rows[r].Role.Contains("Header", StringComparison.OrdinalIgnoreCase);
                var cell = new Border
                {
                    BorderBrush = Res("Safi.TableLinesBrush"),
                    BorderThickness = new Thickness(0.5),
                    Background = header ? Res("Safi.ChipBrush") : Brushes.Transparent,
                    Child = new TextBlock { Text = Display(rows[r].Cells[c]), ToolTip = rows[r].Cells[c].StartsWith('=') ? rows[r].Cells[c] : null, FontSize = 10.5 * (_model?.Zoom ?? 1), Foreground = header ? text : caption, Margin = new Thickness(3, 1, 3, 1), TextTrimming = TextTrimming.CharacterEllipsis, FontWeight = header ? FontWeights.SemiBold : FontWeights.Normal },
                };
                SetRow(cell, r);
                SetColumn(cell, c);
                grid.Children.Add(cell);
            }
        }

        return new Border { Background = Res("Safi.SurfaceBrush"), BorderBrush = Res("Safi.DividerBrush"), BorderThickness = new Thickness(1), Child = grid };
    }

    private void BeginDrag(FrameworkElement element, MouseButtonEventArgs e, bool resize)
    {
        if (element.Tag is not DesignItem item || _model is null)
        {
            return;
        }

        var add = Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || Keyboard.Modifiers.HasFlag(ModifierKeys.Shift);
        if (!_model.SelectedItems.Contains(item.Name) || add)
        {
            _ = _model.SelectAsync(item.Name, add);
        }

        _dragging = element;
        _resizing = resize;
        _dragStart = e.GetPosition(_canvas);
        _dragOrigin = new Rect(Canvas.GetLeft(element), Canvas.GetTop(element), element.Width, element.Height);
        _canvas.CaptureMouse();
        e.Handled = true;
    }

    private void OnMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(_canvas) - _dragStart;
        if (_bandResizing is not null)
        {
            return;
        }

        if (_dragging is null)
        {
            return;
        }

        if (_resizing)
        {
            _dragging.Width = Math.Max(6, _dragOrigin.Width + delta.X);
            _dragging.Height = Math.Max(4, _dragOrigin.Height + delta.Y);
        }
        else
        {
            var left = _dragOrigin.X + delta.X;
            var top = _dragOrigin.Y + delta.Y;
            ClearGuides();
            _snapped = null;
            if (_dragging.Tag is DesignItem item && _model?.Current is { } report && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                // Snap lines (hold Alt to move freely): align with the other items of the band, the band
                // edges, or the spacing already used between items.
                var band = _bands.FirstOrDefault(candidate => candidate.Region == item.Region);
                var moving = new SnapRect(item.AbsoluteLeftInches + delta.X / Scale, item.AbsoluteTopInches + delta.Y / Scale, item.WidthInches, item.HeightInches);
                var others = report.Items
                    .Where(other => other.Region == item.Region && !_model.SelectedItems.Contains(other.Name) && other.Name != item.Name)
                    .Select(other => new SnapRect(other.AbsoluteLeftInches, other.AbsoluteTopInches, other.WidthInches, other.HeightInches))
                    .ToList();
                var result = ReportSnapper.Snap(moving, others, 6 / Scale, report.BodyWidthInches, band.Height / Scale);
                if (result.SnappedX || result.SnappedY)
                {
                    _snapped = (result.Left, result.Top, result.SnappedX, result.SnappedY);
                    left = result.SnappedX ? result.Left * Scale : left;
                    top = result.SnappedY ? band.Top + result.Top * Scale : top;
                    DrawGuides(result.Guides, band.Top);
                }
            }

            Canvas.SetLeft(_dragging, left);
            Canvas.SetTop(_dragging, top);
        }
    }

    private void DrawGuides(IReadOnlyList<SnapGuide> guides, double bandTop)
    {
        foreach (var guide in guides)
        {
            var line = new Line
            {
                Stroke = guide.Kind == SnapKind.Spacing ? Res("Safi.PrimaryLightenBrush") : Res("Safi.SecondaryBrush"),
                StrokeThickness = 1,
                StrokeDashArray = guide.Kind == SnapKind.Spacing ? [2, 2] : [4, 2],
                IsHitTestVisible = false,
            };
            if (guide.Vertical)
            {
                line.X1 = line.X2 = guide.Position * Scale;
                line.Y1 = bandTop + guide.From * Scale - 6;
                line.Y2 = bandTop + guide.To * Scale + 6;
            }
            else
            {
                line.Y1 = line.Y2 = bandTop + guide.Position * Scale;
                line.X1 = guide.From * Scale - (guide.Kind == SnapKind.Spacing ? 0 : 6);
                line.X2 = guide.To * Scale + (guide.Kind == SnapKind.Spacing ? 0 : 6);
            }

            _guides.Add(line);
            _canvas.Children.Add(line);
        }
    }

    private void ClearGuides()
    {
        foreach (var guide in _guides)
        {
            _canvas.Children.Remove(guide);
        }

        _guides.Clear();
    }

    private void OnMouseUp(object sender, MouseButtonEventArgs e)
    {
        _canvas.ReleaseMouseCapture();
        if (_dragging is null || _resizing)
        {
            ClearGuides();
        }

        var delta = e.GetPosition(_canvas) - _dragStart;
        if (_bandResizing is { } region)
        {
            _bandResizing = null;
            if (Math.Abs(delta.Y) > 1 && _model is not null)
            {
                _ = _model.ResizeBandAsync(region, Round((_dragOrigin.Height + delta.Y) / Scale));
            }

            return;
        }

        if (_dragging?.Tag is not DesignItem item || _model is null)
        {
            _dragging = null;
            return;
        }

        _dragging = null;
        if (Math.Abs(delta.X) < 2 && Math.Abs(delta.Y) < 2)
        {
            return;
        }

        var band = _bands.FirstOrDefault(candidate => candidate.Region == item.Region);
        if (_resizing)
        {
            _ = _model.MoveAsync(item.Name, item.LeftInches, item.TopInches, Round(item.WidthInches + delta.X / Scale), Round(item.HeightInches + delta.Y / Scale));
        }
        else
        {
            var snap = _snapped;
            _snapped = null;
            ClearGuides();
            // Work in band coordinates, then store the position relative to the item's container (as RDL does).
            var left = snap is { X: true } ? Math.Round(snap.Value.Left, 4) : Round(item.AbsoluteLeftInches + delta.X / Scale);
            var top = snap is { Y: true } ? Math.Round(snap.Value.Top, 4) : Round(item.AbsoluteTopInches + delta.Y / Scale);
            top = Math.Min(top, Math.Max(0, band.Height / Scale - item.HeightInches));
            left = Math.Max(0, Math.Round(left - item.OffsetLeftInches, 4));
            top = Math.Max(0, Math.Round(top - item.OffsetTopInches, 4));
            _ = _model.MoveAsync(item.Name, left, top, item.WidthInches, item.HeightInches);
        }
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (_model is null || e.Data.GetData(ToolDragFormat) is not string kind)
        {
            return;
        }

        var point = e.GetPosition(_canvas);
        var band = _bands.FirstOrDefault(candidate => point.Y >= candidate.Top && point.Y < candidate.Top + candidate.Height);
        if (band.Region is null)
        {
            band = _bands.FirstOrDefault(candidate => candidate.Region == "Body");
        }

        _ = _model.PlaceAsync(kind, Round(point.X / Scale), Round((point.Y - band.Top) / Scale), band.Region ?? "Body");
    }

    /// <summary>
    /// Rulers in the report's own unit, as in Report Builder: centimetres with millimetre ticks, or inches with
    /// eighth-inch ticks, numbered from the body's origin. Band edges are marked on the vertical ruler and the
    /// printable width on the horizontal one.
    /// </summary>
    private void DrawRulers()
    {
        _hRuler.Children.Clear();
        _vRuler.Children.Clear();
        _hRuler.Background = Res("Safi.SurfaceBrush");
        _vRuler.Background = Res("Safi.SurfaceBrush");
        var offsetX = 24 - _scroll.HorizontalOffset;
        var offsetY = 24 - _scroll.VerticalOffset;
        var metric = _model?.Current?.Page?.IsMetric ?? true;
        var unit = metric ? Scale / 2.54 : Scale;          // pixels per cm or per inch
        var minor = metric ? unit / 10 : unit / 8;         // mm or 1/8 in
        var perLabel = metric ? 10 : 8;                    // label every cm / inch
        var perMajor = metric ? 5 : 4;                     // half cm / half inch
        var caption = Res("Safi.TextSecondaryBrush");

        // At small zoom, thin the ticks so they never crowd closer than 4px.
        var thin = minor < 4 ? (int)Math.Ceiling(4 / minor) : 1;
        for (var i = 0; offsetX + i * minor < ActualWidth; i += thin)
        {
            var x = offsetX + i * minor;
            if (x < 0)
            {
                continue;
            }

            var isLabel = i % perLabel == 0;
            var isMajor = i % perMajor == 0;
            _hRuler.Children.Add(new Line { X1 = x, X2 = x, Y1 = isLabel ? 6 : isMajor ? 12 : 16, Y2 = RulerSize, Stroke = caption, StrokeThickness = isLabel ? 0.9 : 0.5 });
            if (isLabel)
            {
                var label = new TextBlock { Text = (i / perLabel).ToString(CultureInfo.InvariantCulture), FontSize = 9.5, Foreground = caption };
                Canvas.SetLeft(label, x + 2);
                _hRuler.Children.Add(label);
            }
        }

        for (var i = 0; offsetY + i * minor < ActualHeight; i += thin)
        {
            var y = offsetY + i * minor;
            if (y < 0)
            {
                continue;
            }

            var isLabel = i % perLabel == 0;
            var isMajor = i % perMajor == 0;
            _vRuler.Children.Add(new Line { Y1 = y, Y2 = y, X1 = isLabel ? 6 : isMajor ? 12 : 16, X2 = RulerSize, Stroke = caption, StrokeThickness = isLabel ? 0.9 : 0.5 });
            if (isLabel)
            {
                var label = new TextBlock { Text = (i / perLabel).ToString(CultureInfo.InvariantCulture), FontSize = 9.5, Foreground = caption, LayoutTransform = new RotateTransform(-90) };
                Canvas.SetTop(label, y + 2);
                _vRuler.Children.Add(label);
            }
        }

        var unitLabel = new TextBlock { Text = metric ? "cm" : "in", FontSize = 9, Foreground = caption, Opacity = 0.8 };
        Canvas.SetLeft(unitLabel, 2);
        Canvas.SetTop(unitLabel, 1);
        _hRuler.Children.Add(unitLabel);

        foreach (var (_, top, height) in _bands)
        {
            _vRuler.Children.Add(new Line { Y1 = offsetY + top + height, Y2 = offsetY + top + height, X1 = 0, X2 = RulerSize, Stroke = Res("Safi.PrimaryLightenBrush"), StrokeThickness = 1.5 });
        }

        if (_model?.Current?.Page is { } page && page.MaxBodyWidthInches > 0)
        {
            var x = offsetX + page.MaxBodyWidthInches * Scale;
            _hRuler.Children.Add(new Line { X1 = x, X2 = x, Y1 = 0, Y2 = RulerSize, Stroke = Res("Safi.PrimaryLightenBrush"), StrokeThickness = 2 });
        }

        // The selection's extent, so position and size can be read off the rulers.
        if (_model?.SelectedDesignItem is { } item && _bands.FirstOrDefault(band => band.Region == item.Region) is { Region: not null } owner)
        {
            var mark = Res("Safi.SecondaryBrush");
            _hRuler.Children.Add(new Rectangle { Width = Math.Max(1, item.WidthInches * Scale), Height = 3, Fill = mark, Opacity = 0.8 });
            Canvas.SetLeft(_hRuler.Children[^1], offsetX + item.AbsoluteLeftInches * Scale);
            Canvas.SetTop(_hRuler.Children[^1], RulerSize - 3);
            _vRuler.Children.Add(new Rectangle { Width = 3, Height = Math.Max(1, item.HeightInches * Scale), Fill = mark, Opacity = 0.8 });
            Canvas.SetLeft(_vRuler.Children[^1], RulerSize - 3);
            Canvas.SetTop(_vRuler.Children[^1], offsetY + owner.Top + item.AbsoluteTopInches * Scale);
        }
    }

    private Brush ShowGridBrush()
    {
        var surface = Res("Safi.SurfaceBrush");
        if (_model?.ShowGrid != true)
        {
            return surface;
        }

        var cell = 0.125 * Scale;
        var lines = Res("Safi.TableLinesBrush");
        var drawing = new GeometryDrawing(surface, new System.Windows.Media.Pen(lines, 0.5), new RectangleGeometry(new Rect(0, 0, cell, cell)));
        var brush = new DrawingBrush(drawing) { TileMode = TileMode.Tile, Viewport = new Rect(0, 0, cell, cell), ViewportUnits = BrushMappingMode.Absolute };
        brush.Freeze();
        return brush;
    }

    private void Place(UIElement element, double left, double top)
    {
        Canvas.SetLeft(element, left);
        Canvas.SetTop(element, top);
        _canvas.Children.Add(element);
    }

    private TextBlock CellText(DesignGridCell cell, double rowPixels, Brush foreground, bool header)
    {
        var block = new TextBlock
        {
            Text = Display(cell.Value),
            ToolTip = CellToolTip(cell),
            FontSize = CellFontPixels(cell.FontSize, rowPixels),
            Foreground = foreground,
            Margin = new Thickness(2, 0, 2, 0),
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontWeight = header && cell.Style.FontSize.Length == 0 ? FontWeights.SemiBold : FontWeights.Normal,
        };
        if (cell.Style.Bold)
        {
            block.FontWeight = FontWeights.Bold;
        }

        block.TextAlignment = cell.Style.TextAlign switch { "Center" => TextAlignment.Center, "Right" => TextAlignment.Right, _ => TextAlignment.Left };
        ApplyText(block, cell.Style, VerticalAlignment.Center);
        return block;
    }

    /// <summary>
    /// Draws the formatting the toolbar and Properties pane set: font family, italic, underline/strikeout, text
    /// colour and vertical alignment. Black text (the RDL default) keeps the theme's text colour so the layout
    /// stays readable on a dark designer; any other colour is shown as it renders.
    /// </summary>
    private void ApplyText(TextBlock block, DesignTextStyle style, VerticalAlignment fallback)
    {
        if (style.FontFamily.Length > 0)
        {
            block.FontFamily = new System.Windows.Media.FontFamily(style.FontFamily + ", Segoe UI");
        }

        if (style.Bold)
        {
            block.FontWeight = FontWeights.Bold;
        }

        if (style.Italic)
        {
            block.FontStyle = FontStyles.Italic;
        }

        if (style.Underline)
        {
            block.TextDecorations = TextDecorations.Underline;
        }
        else if (style.Strikeout)
        {
            block.TextDecorations = TextDecorations.Strikethrough;
        }

        if (!IsDefaultInk(style.Color) && ColorBrush(style.Color) is { } ink)
        {
            block.Foreground = ink;
        }

        block.VerticalAlignment = style.VerticalAlign switch
        {
            "Top" => VerticalAlignment.Top,
            "Middle" => VerticalAlignment.Center,
            "Bottom" => VerticalAlignment.Bottom,
            _ => fallback,
        };
    }

    /// <summary>
    /// Draws an item's background colour and border. White and transparent backgrounds keep the designer's own
    /// surface; a black border uses the theme's line colour. Without a border the designer's thin outline stays.
    /// </summary>
    private void ApplyBox(Border box, DesignTextStyle style, Brush? outline)
    {
        if (style.BackgroundColor.Length > 0 && !IsPaper(style.BackgroundColor) && ColorBrush(style.BackgroundColor) is { } fill)
        {
            box.Background = fill;
        }

        if (style.BorderStyle.Length > 0 && !style.BorderStyle.Equals("None", StringComparison.OrdinalIgnoreCase))
        {
            box.BorderBrush = IsDefaultInk(style.BorderColor) ? Res("Safi.TextSecondaryBrush") : ColorBrush(style.BorderColor) ?? Res("Safi.TextSecondaryBrush");
            box.BorderThickness = new Thickness(Math.Clamp(PointsToPixels(style.BorderWidth, 1), 1, 6));
        }
        else if (outline is not null)
        {
            box.BorderBrush = outline;
        }
    }

    private static readonly Dictionary<string, Brush?> ColorCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>A brush for an RDL colour (a name such as Red, or #RRGGBB / #AARRGGBB); null when it is not a colour.</summary>
    private static Brush? ColorBrush(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (ColorCache.TryGetValue(value, out var known))
        {
            return known;
        }

        Brush? brush = null;
        try
        {
            brush = new BrushConverter().ConvertFromString(value.Trim()) as Brush;
            brush?.Freeze();
        }
        catch (FormatException)
        {
        }
        catch (NotSupportedException)
        {
        }

        ColorCache[value] = brush;
        return brush;
    }

    private static bool IsDefaultInk(string? color) =>
        string.IsNullOrWhiteSpace(color) || color.Trim() is "Black" or "black" or "#000000" or "#ff000000" or "#FF000000" or "Automatic";

    private static bool IsPaper(string? color) =>
        color?.Trim().ToLowerInvariant() is "white" or "#ffffff" or "#ffffffff" or "transparent" or "no color";

    private double PointsToPixels(string? size, double fallback)
    {
        var text = (size ?? "").Trim().ToLowerInvariant();
        if (text.EndsWith("pt", StringComparison.Ordinal) && double.TryParse(text[..^2], NumberStyles.Float, CultureInfo.InvariantCulture, out var points))
        {
            return points * 96 / 72 * (_model?.Zoom ?? 1);
        }

        return fallback;
    }

    /// <summary>
    /// A tablix cell's text size: the text box's own font (Business Central layouts mostly use 7–8pt), made
    /// smaller when needed so a line fits inside the row and is not cut off.
    /// </summary>
    private double CellFontPixels(string? size, double rowPixels)
    {
        var wanted = string.IsNullOrWhiteSpace(size) ? 10.5 * (_model?.Zoom ?? 1) : FontPixels(size);
        var fits = Math.Max(5, (rowPixels - 2) / 1.3);
        return Math.Min(wanted, fits);
    }

    private double FontPixels(string? size)
    {
        var value = double.TryParse((size ?? "").Replace("pt", "", StringComparison.OrdinalIgnoreCase), NumberStyles.Float, CultureInfo.InvariantCulture, out var points) ? points : 10;
        return Math.Max(6, value * 96 / 72 * (_model?.Zoom ?? 1));
    }

    /// <summary>The value as drawn: Report Builder placeholders ([Field], «Expr») unless full expressions are switched on.</summary>
    private string Display(string? value) =>
        _model?.ShowPlaceholders != false ? ReportExpressionDisplay.Placeholder(value) : value ?? "";

    private static object ToolTipFor(DesignItem item)
    {
        var panel = new StackPanel { MaxWidth = 520 };
        panel.Children.Add(new TextBlock { Text = $"{item.Name} · {Label(item)}{(item.Hidden ? " · hidden at run time" : "")}", FontWeight = FontWeights.SemiBold });
        if (!string.IsNullOrWhiteSpace(item.Value) && item.Kind == "Textbox")
        {
            panel.Children.Add(new TextBlock { Text = item.Value, TextWrapping = TextWrapping.Wrap, FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas"), FontSize = 11.5, Margin = new Thickness(0, 4, 0, 0) });
            panel.Children.Add(new TextBlock { Text = "Double-click to edit the expression", FontSize = 10.5, Opacity = 0.65, Margin = new Thickness(0, 4, 0, 0) });
        }

        return panel;
    }

    private static string Label(DesignItem item) =>
        item.Kind == "Chart" && item.Variant.Length > 0 ? item.Variant + " chart" : item.Kind;

    private static double Round(double inches) => Math.Round(inches / Snap) * Snap;

    private Brush Res(string key) => TryFindResource(key) as Brush ?? Brushes.Gray;
}
