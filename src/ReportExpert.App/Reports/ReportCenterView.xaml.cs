using System.Windows;

namespace ReportExpert.App.Reports;

public partial class ReportCenterView : System.Windows.Controls.UserControl
{
    public static readonly DependencyProperty IsEmbeddedProperty = DependencyProperty.Register(
        nameof(IsEmbedded), typeof(bool), typeof(ReportCenterView), new PropertyMetadata(false, (d, _) => ((ReportCenterView)d).ApplyEmbedded()));

    public ReportCenterView()
    {
        InitializeComponent();
        Theming.SafiXmlHighlighting.Attach(XmlEditor);
        DataContextChanged += (_, e) =>
        {
            if (e.OldValue is ReportCenterViewModel old)
            {
                old.PropertyChanged -= OnModelChanged;
            }

            if (e.NewValue is ReportCenterViewModel model)
            {
                model.PropertyChanged += OnModelChanged;
            }
        };
    }

    private void OnModelChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ReportCenterViewModel.XmlText) && sender is ReportCenterViewModel model)
        {
            Dispatcher.Invoke(() => XmlEditor.Text = model.XmlText);
        }
    }

    /// <summary>The Layout tab loads the RDLC XML while it is shown (and refreshes it after each edit).</summary>
    private void OnTabChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        if (ReferenceEquals(e.OriginalSource, Tabs) && DataContext is ReportCenterViewModel model)
        {
            model.IsXmlView = ReferenceEquals(Tabs.SelectedItem, LayoutTab);
            model.IsDesignTabActive = ReferenceEquals(Tabs.SelectedItem, DesignTab);
        }
    }

    /// <summary>
    /// Embedded in the workspace editor pane: one report, no report list or page header, and the host's
    /// toolbar provides Save. The designer tab fills the pane.
    /// </summary>
    public bool IsEmbedded
    {
        get => (bool)GetValue(IsEmbeddedProperty);
        set => SetValue(IsEmbeddedProperty, value);
    }

    private void ApplyEmbedded()
    {
        var embedded = IsEmbedded;
        var hidden = embedded ? Visibility.Collapsed : Visibility.Visible;
        RootGrid.Margin = embedded ? new Thickness(0) : new Thickness(24, 20, 24, 12);
        PageHeader.Visibility = hidden;
        ListCard.Visibility = hidden;
        ListColumn.Width = embedded ? new GridLength(0) : new GridLength(330);
        GapColumn.Width = embedded ? new GridLength(0) : new GridLength(16);
        DetailHeader.Visibility = hidden;
        SaveButton.Visibility = hidden;
        OpenInDesignerButton.Visibility = hidden;
        RemoveButton.Visibility = hidden;
        DetailCard.BorderThickness = embedded ? new Thickness(0) : new Thickness(1);
        DetailCard.CornerRadius = embedded ? new CornerRadius(0) : new CornerRadius(10);
        // The designer toolbar has undo/redo and the Export tab has PDF/Excel/Word, so the action bar
        // would only repeat them inside the workspace editor.
        ActionBar.Visibility = hidden;
        // The window status bar shows the designer's messages when embedded.
        StatusLine.Visibility = hidden;
        Tabs.Margin = embedded ? new Thickness(4, 0, 4, 0) : new Thickness(12, 4, 12, 12);
    }
}
