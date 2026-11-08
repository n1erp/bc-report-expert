using System.Windows;
using System.Windows.Threading;
using ReportExpert.Modules.Preview.Services;
using Wpf.Ui.Appearance;
using Wpf.Ui.Controls;

namespace ReportExpert.App;

/// <summary>WPF application entry — global exception handlers and startup theme.</summary>
public partial class App : System.Windows.Application
{
    public App()
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        Common.ErrorReporting.Handler = Shell.ErrorReporter.Show;
        AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
    }

    protected override void OnStartup(StartupEventArgs e)
    {
        // ReportViewer PDF rendering needs Windows code pages (e.g. 1252).
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        base.OnStartup(e);
        Theming.SafiThemeManager.Initialize();
        ApplySavedTheme();
    }

    private static void ApplySavedTheme()
    {
        var settings = new SettingsService().Settings;
        Theming.SafiThemeManager.ApplyTheme(settings.Theme);
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        // The UI keeps running: the error is shown with its details and a way to report it.
        Shell.ErrorReporter.Show(e.Exception, title: "Unexpected error");
        e.Handled = true;
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            Shell.ErrorReporter.Show(ex, title: e.IsTerminating ? "Fatal error — Report Expert has to close" : "Unexpected error");
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        e.SetObserved();
        Shell.ErrorReporter.Show(e.Exception, "Background task", "Background error");
    }
}
