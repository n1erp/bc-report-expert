using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ReportExpert.Reporting.Design;
using ComboBox = System.Windows.Controls.ComboBox;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using ListBox = System.Windows.Controls.ListBox;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using TextBox = System.Windows.Controls.TextBox;

namespace ReportExpert.App.Reports;

/// <summary>The REX V3 visual report designer: toolbar, toolbox, surface, properties, data and groups.</summary>
public partial class ReportDesignerView : System.Windows.Controls.UserControl
{
    public ReportDesignerView()
    {
        InitializeComponent();
        PreviewKeyDown += OnShortcut;
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ReportCenterViewModel old)
            {
                old.PropertyChanged -= OnModelChanged;
            }

            if (e.NewValue is ReportCenterViewModel model)
            {
                model.PropertyChanged += OnModelChanged;
                ApplyToolboxPanel(model.IsToolboxOpen);

                // The dialogs belong to the view; the view model only applies their results.
                model.PageSetupRequested = (page, body) => PageSetupDialog.Ask(Window.GetWindow(this), page, body);
                model.ExpressionEditorRequested = (title, value, context) => ExpressionEditorDialog.Ask(Window.GetWindow(this), title, value, context);
            }
        };
    }

    private ReportCenterViewModel? Model => DataContext as ReportCenterViewModel;

    private void OnShortcut(object sender, KeyEventArgs e)
    {
        if (Model is not { } model || e.OriginalSource is TextBox || e.OriginalSource is ComboBox
            || e.OriginalSource is TreeViewItem || e.OriginalSource is ListBoxItem || e.OriginalSource is ICSharpCode.AvalonEdit.Editing.TextArea)
        {
            return;
        }

        // Arrow keys nudge the selection (Ctrl: an eighth of an inch); with Shift they resize it.
        if (e.Key is Key.Left or Key.Right or Key.Up or Key.Down && model.SelectedItems.Count > 0)
        {
            var dx = e.Key == Key.Left ? -1 : e.Key == Key.Right ? 1 : 0;
            var dy = e.Key == Key.Up ? -1 : e.Key == Key.Down ? 1 : 0;
            _ = model.NudgeAsync(dx, dy, Keyboard.Modifiers.HasFlag(ModifierKeys.Shift), Keyboard.Modifiers.HasFlag(ModifierKeys.Control));
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F2 && model.SelectedDesignItem is { Kind: "Textbox" })
        {
            _ = model.EditExpressionAsync("Value");
            e.Handled = true;
            return;
        }

        var ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        ICommandLike? command = (ctrl, e.Key) switch
        {
            (false, Key.Delete) => Wrap(model.DeleteItemCommand),
            (true, Key.Z) => Wrap(model.UndoCommand),
            (true, Key.Y) => Wrap(model.RedoCommand),
            (true, Key.C) => Wrap(model.CopyItemCommand),
            (true, Key.X) => Wrap(model.CutItemCommand),
            (true, Key.V) => Wrap(model.PasteItemCommand),
            (true, Key.S) => Wrap(model.SaveToFileCommand),
            (true, Key.OemPlus) or (true, Key.Add) => Wrap(model.ZoomInCommand),
            (true, Key.OemMinus) or (true, Key.Subtract) => Wrap(model.ZoomOutCommand),
            _ => null,
        };

        if (command is not null)
        {
            command.Execute();
            e.Handled = true;
        }
    }

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportCenterViewModel.IsToolboxOpen) && sender is ReportCenterViewModel toolboxModel)
        {
            ApplyToolboxPanel(toolboxModel.IsToolboxOpen);
        }
    }

    private void ApplyToolboxPanel(bool open)
    {
        ToolboxColumn.Width = open ? new GridLength(250) : new GridLength(0);
        ToolboxPanel.Visibility = open ? Visibility.Visible : Visibility.Collapsed;
    }


    private System.Windows.Point? _toolPress;

    private void OnToolPress(object sender, MouseButtonEventArgs e) => _toolPress = e.GetPosition(this);

    /// <summary>
    /// Starts dragging a widget only once the mouse has moved the system drag distance. Starting on the first
    /// move swallowed clicks — a click that wobbled by a pixel became an empty drag, so nothing was added and
    /// the Page header / Page footer switches never fired.
    /// </summary>
    private void OnToolDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _toolPress is not { } start || sender is not FrameworkElement { Tag: string kind } element)
        {
            return;
        }

        var offset = e.GetPosition(this) - start;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _toolPress = null;
        DragDrop.DoDragDrop(element, new System.Windows.DataObject(DesignSurfaceControl.ToolDragFormat, kind), System.Windows.DragDropEffects.Copy);
    }

    private void OnZoomToWidth(object sender, RoutedEventArgs e) => Model?.ZoomToWidth(Surface.ActualWidth);

    private void OnFontFamilyPicked(object sender, SelectionChangedEventArgs e)
    {
        // The box follows the selection (one-way); only a choice that differs from it is applied.
        if (Model is { } model && FontFamilyBox.IsDropDownOpen | FontFamilyBox.IsKeyboardFocusWithin
            && e.AddedItems is [string family] && family != model.FormatFontFamily)
        {
            model.SetFormatCommand.Execute("FontFamily|" + family);
        }
    }

    private void OnFontSizePicked(object sender, SelectionChangedEventArgs e)
    {
        if (Model is { } model && FontSizeBox.IsDropDownOpen | FontSizeBox.IsKeyboardFocusWithin
            && e.AddedItems is [string size] && size != model.FormatFontSize)
        {
            model.SetFormatCommand.Execute("FontSize|" + size);
        }
    }

    private static readonly (string Name, string Value)[] TextColors =
    [
        ("Automatic", ""), ("Black", "Black"), ("Dark grey", "#404040"), ("Grey", "Gray"), ("White", "White"),
        ("Navy", "#1F3864"), ("Blue", "#2F5597"), ("Teal", "#00796B"), ("Green", "#2E7D32"),
        ("Dark red", "#9C1C1C"), ("Red", "#D32F2F"), ("Orange", "#E67E22"), ("Purple", "#6A1B9A"),
    ];

    private void OnColorMenu(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model || sender is not FrameworkElement anchor)
        {
            return;
        }

        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (name, value) in TextColors)
        {
            var swatch = new System.Windows.Shapes.Rectangle { Width = 14, Height = 14, Stroke = System.Windows.Media.Brushes.Gray, StrokeThickness = 0.5 };
            if (value.Length > 0)
            {
                swatch.Fill = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(value)!;
            }

            var item = new System.Windows.Controls.MenuItem { Header = name, Icon = swatch };
            item.Click += (_, _) => model.SetFormatCommand.Execute("Color|" + value);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private void OnNumberFormatMenu(object sender, RoutedEventArgs e)
    {
        if (Model is not { } model || sender is not FrameworkElement anchor)
        {
            return;
        }

        var menu = new System.Windows.Controls.ContextMenu { PlacementTarget = anchor, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
        foreach (var (label, format) in ReportCenterViewModel.NumberFormats)
        {
            var item = new System.Windows.Controls.MenuItem { Header = label, ToolTip = format.Length == 0 ? "No format" : format };
            item.Click += (_, _) => model.SetFormatCommand.Execute("Format|" + format);
            menu.Items.Add(item);
        }

        menu.IsOpen = true;
    }

    private System.Windows.Point? _dataPress;

    private void OnDataPress(object sender, MouseButtonEventArgs e) => _dataPress = e.GetPosition(this);

    /// <summary>Drags a field, parameter or built-in field onto the page once the mouse has moved the drag distance.</summary>
    private void OnDataDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dataPress is not { } start || ReportDataTree.SelectedItem is not ReportDataNode { IsFolder: false } node)
        {
            return;
        }

        var offset = e.GetPosition(this) - start;
        if (Math.Abs(offset.X) < SystemParameters.MinimumHorizontalDragDistance && Math.Abs(offset.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        _dataPress = null;
        DragDrop.DoDragDrop(ReportDataTree, new System.Windows.DataObject(DesignSurfaceControl.ToolDragFormat, node.DropKind), System.Windows.DragDropEffects.Copy);
    }

    private void OnDataInsert(object sender, MouseButtonEventArgs e)
    {
        if (Model is { } model && ReportDataTree.SelectedItem is ReportDataNode { IsFolder: false } node)
        {
            _ = model.InsertDataNodeAsync(node);
            e.Handled = true;
        }
    }

    private void OnFieldDrag(object sender, MouseEventArgs e)
    {
        if (e.LeftButton == MouseButtonState.Pressed && sender is ListBox list && list.SelectedItem is DesignField field && Model?.SelectedDataSet is { } dataSet)
        {
            DragDrop.DoDragDrop(list, new System.Windows.DataObject(DesignSurfaceControl.ToolDragFormat, $"Field:{dataSet.Name}/{field.Name}"), System.Windows.DragDropEffects.Copy);
        }
    }

    private static ICommandLike Wrap(System.Windows.Input.ICommand command) => new CommandAdapter(command);

    private interface ICommandLike
    {
        void Execute();
    }

    private sealed class CommandAdapter(System.Windows.Input.ICommand command) : ICommandLike
    {
        public void Execute()
        {
            if (command.CanExecute(null))
            {
                command.Execute(null);
            }
        }
    }
}
