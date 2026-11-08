using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ReportExpert.Common;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using HorizontalAlignment = System.Windows.HorizontalAlignment;
using Orientation = System.Windows.Controls.Orientation;
using TextBox = System.Windows.Controls.TextBox;

namespace ReportExpert.App.Shell;

/// <summary>
/// The error popup: what failed in one or two sentences, the technical details (expandable, selectable),
/// <b>Copy details</b>, <b>Report a bug</b> (opens a prefilled GitHub bug report) and <b>Close</b>.
/// </summary>
public sealed class ErrorReportWindow : Window
{
    private readonly ErrorReport _report;
    private readonly TextBlock _status = new() { FontSize = 12, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center, TextWrapping = TextWrapping.Wrap };

    public ErrorReportWindow(ErrorReport report)
    {
        _report = report;
        Title = $"{AppConstants.ApplicationName} — {report.Title}";
        Width = 720;
        SizeToContent = SizeToContent.Height;
        MaxHeight = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        ResizeMode = ResizeMode.CanResizeWithGrip;
        ShowInTaskbar = Owner is null;
        SetResourceReference(BackgroundProperty, "Safi.SurfaceBrush");
        SetResourceReference(ForegroundProperty, "Safi.TextPrimaryBrush");
        if (TryFindResource("Safi.FontFamily") is System.Windows.Media.FontFamily font)
        {
            FontFamily = font;
        }

        var icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.ErrorCircle24, FontSize = 30, Margin = new Thickness(0, 2, 14, 0), VerticalAlignment = VerticalAlignment.Top };
        icon.SetResourceReference(System.Windows.Controls.Control.ForegroundProperty, "Safi.ErrorBrush");
        var heading = new TextBlock { Text = report.Title, FontSize = 17, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 6) };
        var message = new TextBlock { Text = report.Message, TextWrapping = TextWrapping.Wrap, FontSize = 13 };
        var hint = new TextBlock
        {
            Text = "Your work is safe up to the last saved revision (Undo still works). If this keeps happening, " +
                   "copy the details and report a bug so it can be fixed. Personal paths and keys are removed from the details.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12,
            Opacity = 0.75,
            Margin = new Thickness(0, 8, 0, 0),
        };
        var top = new DockPanel { Margin = new Thickness(22, 20, 22, 10) };
        DockPanel.SetDock(icon, Dock.Left);
        top.Children.Add(icon);
        top.Children.Add(new StackPanel { Children = { heading, message, hint } });

        var details = new TextBox
        {
            Text = report.Details,
            IsReadOnly = true,
            TextWrapping = TextWrapping.NoWrap,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            FontFamily = new System.Windows.Media.FontFamily("Cascadia Mono, Consolas, Courier New"),
            FontSize = 11.5,
            MinHeight = 160,
            MaxHeight = 380,
            Padding = new Thickness(8),
        };
        var expander = new Expander { Header = "Details for the developer", Content = details, Margin = new Thickness(22, 0, 22, 0), IsExpanded = false };

        var copy = Action("Copy details", "Copy24", primary: false, () =>
        {
            try
            {
                Clipboard.SetText(_report.Details);
                Status("Details copied to the clipboard.", error: false);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
                Status("The clipboard is busy; select the details and press Ctrl+C.", error: true);
                expander.IsExpanded = true;
            }
        });
        var reportBug = Action("Report a bug", "Bug24", primary: true, () =>
        {
            // Copy too: GitHub may cut a long prefilled report, and the full text is then one paste away.
            try
            {
                Clipboard.SetText(_report.Details);
            }
            catch (System.Runtime.InteropServices.COMException)
            {
            }

            Status(
                ProjectLinks.Open(_report.BugReportUrl())
                    ? "A bug report opened in your browser (details are also on the clipboard)."
                    : "Could not open a browser. Go to " + ProjectLinks.IssuesUrl,
                error: false);
        });
        var close = Action("Close", null, primary: false, Close);
        close.IsCancel = true;
        close.IsDefault = true;

        var buttons = new DockPanel { Margin = new Thickness(22, 14, 22, 18), LastChildFill = true };
        var right = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { copy, reportBug, close } };
        DockPanel.SetDock(right, Dock.Right);
        buttons.Children.Add(right);
        buttons.Children.Add(_status);

        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(buttons, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(buttons);
        root.Children.Add(expander);
        Content = root;
    }

    private void Status(string text, bool error)
    {
        _status.Text = text;
        _status.SetResourceReference(TextBlock.ForegroundProperty, error ? "Safi.ErrorBrush" : "Safi.TextSecondaryBrush");
    }

    private static Button Action(string label, string? symbol, bool primary, Action click)
    {
        var content = new StackPanel { Orientation = Orientation.Horizontal };
        if (symbol is not null && Enum.TryParse<Wpf.Ui.Controls.SymbolRegular>(symbol, out var value))
        {
            content.Children.Add(new Wpf.Ui.Controls.SymbolIcon { Symbol = value, FontSize = 15, Margin = new Thickness(0, 0, 6, 0) });
        }

        content.Children.Add(new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center });
        var button = new Button { Content = content, Padding = new Thickness(14, 7, 14, 7), Margin = new Thickness(8, 0, 0, 0), MinWidth = 90 };
        button.SetResourceReference(StyleProperty, primary ? "Safi.PrimaryButton" : "Safi.OutlinedButton");
        button.Click += (_, _) => click();
        return button;
    }
}

/// <summary>
/// Shows <see cref="ErrorReportWindow"/> for any failure, from any thread. Repeated identical failures in a
/// short time are shown once, so a failing timer or render loop cannot stack dialogs.
/// </summary>
public static class ErrorReporter
{
    private static readonly object Gate = new();
    private static string? _lastKey;
    private static DateTime _lastShown;
    private static int _open;

    /// <summary>Shows the error popup. Never throws.</summary>
    public static void Show(Exception exception, string? operation = null, string? title = null, string? summary = null)
    {
        try
        {
            var report = ErrorReport.Create(exception, operation, title, summary);
            var key = exception.GetType().FullName + "|" + exception.Message + "|" + operation;
            lock (Gate)
            {
                if (_open > 0 || (key == _lastKey && DateTime.UtcNow - _lastShown < TimeSpan.FromSeconds(3)))
                {
                    return;
                }

                _lastKey = key;
                _lastShown = DateTime.UtcNow;
            }

            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null)
            {
                return;
            }

            if (dispatcher.CheckAccess())
            {
                ShowNow(report);
            }
            else
            {
                dispatcher.Invoke(() => ShowNow(report));
            }
        }
        catch (Exception)
        {
            // Never let the error dialog itself bring the application down.
        }
    }

    /// <summary>
    /// Shows a preview or export failure. Problems in the report itself (an expression that does not
    /// evaluate, an invalid definition, data that cannot be read) and defects get the dialog with the
    /// engine's words, the full exception chain and Report a bug; a missing parameter value only needs the
    /// status line. Returns the one-line message for the status line.
    /// </summary>
    public static string ShowPreviewFailure(Exception exception, string operation)
    {
        var message = ReportExpert.Reporting.Design.ReportMessages.For(exception);
        if (exception is OperationCanceledException)
        {
            return message;
        }

        if (exception is ReportExpert.Reporting.Design.ReportDesignException design && design.Message is
            ReportExpert.Reporting.Design.ReportCodes.ParameterRequired
            or ReportExpert.Reporting.Design.ReportCodes.InvalidParameterValue
            or ReportExpert.Reporting.Design.ReportCodes.ParameterNeedsDefault
            or ReportExpert.Reporting.Design.ReportCodes.ParameterCycle)
        {
            return message;
        }

        Show(exception, operation, "Preview failed", message);
        return message;
    }

    private static void ShowNow(ErrorReport report)
    {
        Interlocked.Increment(ref _open);
        try
        {
            var owner = System.Windows.Application.Current?.Windows.OfType<Window>().FirstOrDefault(window => window.IsActive)
                ?? System.Windows.Application.Current?.MainWindow;
            var window = new ErrorReportWindow(report);
            if (owner is not null && owner.IsLoaded && owner.IsVisible)
            {
                window.Owner = owner;
            }

            window.ShowDialog();
        }
        catch (Exception)
        {
            System.Windows.MessageBox.Show(report.Message + "\n\n" + report.Details, AppConstants.ApplicationName + " — " + report.Title, MessageBoxButton.OK, MessageBoxImage.Error);
        }
        finally
        {
            Interlocked.Decrement(ref _open);
        }
    }
}
