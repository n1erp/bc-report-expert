using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using ReportExpert.Modules.Copilot.ViewModels;

namespace ReportExpert.Modules.Copilot.Views;

/// <summary>
/// The Rex model picker: providers on the left, the chosen provider's models on the right, and a search box
/// that finds a model across every usable provider. Picking a model switches provider and model in one click;
/// a provider without a working key offers a way to set it up instead.
/// </summary>
internal sealed class ModelPickerPopup
{
    private const double PopupWidth = 600;
    private const double BodyHeight = 360;

    private readonly CopilotViewModel _viewModel;
    private readonly Popup _popup;
    private readonly StackPanel _providerList = new();
    private readonly StackPanel _modelList = new();
    private readonly TextBlock _modelHeader = new() { FontSize = 13, FontWeight = FontWeights.SemiBold };
    private readonly TextBlock _modelSubheader = new() { FontSize = 11, Opacity = 0.6, Margin = new Thickness(0, 2, 0, 0) };
    private readonly Wpf.Ui.Controls.TextBox _search;
    private ProviderChoiceViewModel? _shown;
    private ModelChoiceViewModel? _firstResult;

    public ModelPickerPopup(CopilotViewModel viewModel, UIElement target)
    {
        _viewModel = viewModel;
        _shown = viewModel.ProviderChoices.FirstOrDefault(p => p.IsActive)
            ?? viewModel.ProviderChoices.FirstOrDefault(p => p.IsUsable)
            ?? viewModel.ProviderChoices.FirstOrDefault();

        _search = new Wpf.Ui.Controls.TextBox
        {
            PlaceholderText = "Search all models…",
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Search24 },
            ClearButtonEnabled = true,
            MinHeight = 32,
            FontSize = 12.5,
        };
        _search.TextChanged += (_, _) => RenderModels();
        _search.KeyDown += OnSearchKeyDown;

        var title = new TextBlock { Text = "Choose a model", FontSize = 14, FontWeight = FontWeights.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        var hint = Secondary("Pick a provider, then a model. Rex switches both at once.", 11);
        hint.Margin = new Thickness(0, 2, 0, 10);
        var top = new StackPanel { Margin = new Thickness(16, 14, 16, 12), Children = { title, hint, _search } };

        // Left: providers.
        var providersHeader = Caption("PROVIDERS");
        providersHeader.Margin = new Thickness(12, 10, 12, 6);
        var left = new DockPanel { Width = 200 };
        DockPanel.SetDock(providersHeader, Dock.Top);
        left.Children.Add(providersHeader);
        left.Children.Add(new ScrollViewer { Content = _providerList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(6, 0, 6, 8) });
        var leftBorder = new Border { Child = left, BorderThickness = new Thickness(0, 0, 1, 0) };
        leftBorder.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        leftBorder.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorTertiaryBrush");

        // Right: models.
        var headerStack = new StackPanel { Margin = new Thickness(16, 10, 16, 8), Children = { _modelHeader, _modelSubheader } };
        var right = new DockPanel();
        DockPanel.SetDock(headerStack, Dock.Top);
        right.Children.Add(headerStack);
        right.Children.Add(new ScrollViewer { Content = _modelList, VerticalScrollBarVisibility = ScrollBarVisibility.Auto, Padding = new Thickness(8, 0, 8, 8) });

        var body = new Grid { Height = BodyHeight };
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        body.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        Grid.SetColumn(right, 1);
        body.Children.Add(leftBorder);
        body.Children.Add(right);

        // Footer: settings.
        var manage = new Wpf.Ui.Controls.Button
        {
            Content = "Manage providers and keys",
            Icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Settings24 },
            Appearance = Wpf.Ui.Controls.ControlAppearance.Transparent,
            Padding = new Thickness(10, 5, 10, 5),
            FontSize = 12,
        };
        manage.Click += (_, _) =>
        {
            Close();
            _viewModel.OpenSettingsCommand.Execute(null);
        };
        var keys = Secondary("Esc to close", 10.5);
        keys.VerticalAlignment = VerticalAlignment.Center;
        var footer = new DockPanel { Margin = new Thickness(8, 6, 14, 6), LastChildFill = false };
        DockPanel.SetDock(keys, Dock.Right);
        footer.Children.Add(manage);
        footer.Children.Add(keys);
        var footerBorder = new Border { Child = footer, BorderThickness = new Thickness(0, 1, 0, 0) };
        footerBorder.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");

        var divider = new Border { BorderThickness = new Thickness(0, 1, 0, 0) };
        divider.SetResourceReference(Border.BorderBrushProperty, "CardStrokeColorDefaultBrush");
        var root = new DockPanel();
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(divider, Dock.Top);
        DockPanel.SetDock(footerBorder, Dock.Bottom);
        root.Children.Add(top);
        root.Children.Add(divider);
        root.Children.Add(footerBorder);
        root.Children.Add(body);

        var card = new Border
        {
            Width = PopupWidth,
            CornerRadius = new CornerRadius(10),
            BorderThickness = new Thickness(1),
            Margin = new Thickness(12),
            Child = root,
            ClipToBounds = true,
            Effect = new DropShadowEffect { BlurRadius = 18, ShadowDepth = 4, Opacity = 0.35, Color = Colors.Black },
        };
        // The popup is short-lived, so the theme brushes are resolved once (the same ones WPF UI menus use).
        card.Background = FindBrush(target, "FlyoutBackground", "ApplicationBackgroundBrush") ?? SystemColors.WindowBrush;
        card.BorderBrush = FindBrush(target, "FlyoutBorderBrush", "CardStrokeColorDefaultBrush") ?? SystemColors.ActiveBorderBrush;
        TextElement.SetForeground(card, FindBrush(target, "TextFillColorPrimaryBrush") ?? SystemColors.WindowTextBrush);
        card.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape)
            {
                Close();
                e.Handled = true;
            }
        };

        _popup = new Popup
        {
            PlacementTarget = target,
            Placement = PlacementMode.Top,
            HorizontalOffset = -12,
            StaysOpen = false,
            AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade,
            Child = card,
        };
        _popup.Opened += (_, _) => _search.Dispatcher.BeginInvoke(() => Keyboard.Focus(_search), System.Windows.Threading.DispatcherPriority.Input);

        RenderProviders();
        RenderModels();
    }

    public void Show() => _popup.IsOpen = true;

    private void Close() => _popup.IsOpen = false;

    private void RenderProviders()
    {
        _providerList.Children.Clear();
        foreach (var provider in _viewModel.ProviderChoices)
        {
            bool shown = ReferenceEquals(provider, _shown) && string.IsNullOrWhiteSpace(_search.Text);
            var dot = new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Margin = new Thickness(0, 0, 10, 0), VerticalAlignment = VerticalAlignment.Center };
            dot.Fill = provider.IsUsable ? new SolidColorBrush(Color.FromRgb(0x3F, 0xB9, 0x50)) : new SolidColorBrush(Color.FromArgb(0x70, 0x9A, 0x9A, 0x9A));

            var name = new TextBlock { Text = provider.DisplayName, FontSize = 12.5, FontWeight = shown ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
            var detail = Secondary(
                provider.IsUsable
                    ? provider.IsActive ? "In use" : $"{provider.Models.Count} models"
                    : provider.Status,
                10.5);
            var text = new StackPanel { Children = { name, detail }, Opacity = provider.IsUsable ? 1 : 0.7 };

            var chevron = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.ChevronRight16, FontSize = 11, Opacity = shown ? 0.8 : 0.35, VerticalAlignment = VerticalAlignment.Center };
            var row = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(dot, Dock.Left);
            DockPanel.SetDock(chevron, Dock.Right);
            row.Children.Add(dot);
            row.Children.Add(chevron);
            row.Children.Add(text);

            var item = Clickable(row, shown, () =>
            {
                _shown = provider;
                if (!string.IsNullOrWhiteSpace(_search.Text))
                    _search.Text = string.Empty;
                RenderProviders();
                RenderModels();
            });
            item.ToolTip = provider.IsUsable ? null : $"{provider.DisplayName}: {provider.Status}";
            _providerList.Children.Add(item);
        }
    }

    private void RenderModels()
    {
        _modelList.Children.Clear();
        _firstResult = null;
        string query = _search.Text?.Trim() ?? string.Empty;

        if (query.Length > 0)
        {
            var groups = _viewModel.ProviderChoices
                .Where(p => p.IsUsable)
                .Select(p => (Provider: p, Models: p.Models.Where(m => Matches(m, p, query)).ToList()))
                .Where(g => g.Models.Count > 0)
                .ToList();
            int count = groups.Sum(g => g.Models.Count);
            _modelHeader.Text = count == 0 ? "No models found" : $"Results for “{query}”";
            _modelSubheader.Text = count == 0
                ? "Try another name or id, or add a provider in Settings."
                : $"{count} model{(count == 1 ? "" : "s")} · press Enter to pick the first";
            foreach (var (provider, models) in groups)
            {
                var caption = Caption(provider.DisplayName.ToUpperInvariant());
                caption.Margin = new Thickness(8, 8, 8, 4);
                _modelList.Children.Add(caption);
                foreach (var model in models)
                {
                    _firstResult ??= model;
                    _modelList.Children.Add(ModelRow(model));
                }
            }

            RenderProviders();
            return;
        }

        if (_shown is null)
        {
            _modelHeader.Text = "No providers yet";
            _modelSubheader.Text = "Add an API key in Settings to chat with Rex.";
            _modelList.Children.Add(SetupCard(null));
            return;
        }

        _modelHeader.Text = _shown.DisplayName;
        if (!_shown.IsUsable)
        {
            _modelSubheader.Text = _shown.Status;
            _modelList.Children.Add(SetupCard(_shown));
            return;
        }

        _modelSubheader.Text = _shown.IsActive
            ? $"{_shown.Models.Count} models · in use now"
            : $"{_shown.Models.Count} models · picking one switches Rex to {_shown.DisplayName}";
        foreach (var model in _shown.Models)
            _modelList.Children.Add(ModelRow(model));
    }

    private FrameworkElement ModelRow(ModelChoiceViewModel model)
    {
        var name = new TextBlock { Text = model.DisplayName, FontSize = 12.5, FontWeight = model.IsSelected ? FontWeights.SemiBold : FontWeights.Normal, TextTrimming = TextTrimming.CharacterEllipsis };
        var nameLine = new StackPanel { Orientation = Orientation.Horizontal, Children = { name } };
        if (model.IsSelected)
            nameLine.Children.Add(Tag("Current", accent: true));
        else if (model.IsProviderModel)
            nameLine.Children.Add(Tag("Saved for provider", accent: false));

        var bestFor = Secondary(model.BestFor, 11);
        bestFor.TextWrapping = TextWrapping.Wrap;
        bestFor.Margin = new Thickness(0, 2, 0, 0);
        var id = Secondary(model.ModelId, 10.5);
        id.FontFamily = new FontFamily("Cascadia Mono, Consolas, Courier New");
        id.Opacity = 0.45;
        id.Margin = new Thickness(0, 2, 0, 0);
        var text = new StackPanel { Children = { nameLine, bestFor, id } };

        var check = new Wpf.Ui.Controls.SymbolIcon
        {
            Symbol = Wpf.Ui.Controls.SymbolRegular.Checkmark16,
            FontSize = 14,
            Margin = new Thickness(10, 0, 2, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Visibility = model.IsSelected ? Visibility.Visible : Visibility.Hidden,
        };
        check.SetResourceReference(Control.ForegroundProperty, "AccentTextFillColorPrimaryBrush");
        var row = new DockPanel();
        DockPanel.SetDock(check, Dock.Right);
        row.Children.Add(check);
        row.Children.Add(text);

        var item = Clickable(row, model.IsSelected, () => Pick(model));
        item.Padding = new Thickness(10, 8, 10, 8);
        return item;
    }

    private FrameworkElement SetupCard(ProviderChoiceViewModel? provider)
    {
        var icon = new Wpf.Ui.Controls.SymbolIcon { Symbol = Wpf.Ui.Controls.SymbolRegular.Key24, FontSize = 28, Opacity = 0.6, Margin = new Thickness(0, 0, 0, 10) };
        var heading = new TextBlock
        {
            Text = provider is null ? "Set up an AI provider" : $"{provider.DisplayName} isn't ready yet",
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        var message = Secondary(
            provider is null ? "Rex needs an API key from one provider."
                : provider.Status == "Key rejected" ? "Its API key was rejected. Fix it in Settings, then press Test."
                : "Add its API key in Settings to use its models.",
            11.5);
        message.TextAlignment = TextAlignment.Center;
        message.TextWrapping = TextWrapping.Wrap;
        message.Margin = new Thickness(0, 4, 0, 12);
        var button = new Wpf.Ui.Controls.Button
        {
            Content = provider is null ? "Open Settings" : $"Set up {provider.DisplayName}",
            Appearance = Wpf.Ui.Controls.ControlAppearance.Primary,
            HorizontalAlignment = HorizontalAlignment.Center,
            Padding = new Thickness(14, 6, 14, 6),
        };
        button.Click += (_, _) =>
        {
            Close();
            _viewModel.OpenSettingsCommand.Execute(null);
        };
        return new StackPanel { Margin = new Thickness(24, 40, 24, 0), Children = { icon, heading, message, button }, HorizontalAlignment = HorizontalAlignment.Center };
    }

    private void Pick(ModelChoiceViewModel model)
    {
        Close();
        _viewModel.SelectModelCommand.Execute(model);
    }

    private void OnSearchKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && _firstResult is not null)
        {
            Pick(_firstResult);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            Close();
            e.Handled = true;
        }
    }

    private static bool Matches(ModelChoiceViewModel model, ProviderChoiceViewModel provider, string query) =>
        model.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase)
        || model.ModelId.Contains(query, StringComparison.OrdinalIgnoreCase)
        || model.BestFor.Contains(query, StringComparison.OrdinalIgnoreCase)
        || provider.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);

    /// <summary>A row that highlights on hover and when selected, and runs <paramref name="click"/> on click.</summary>
    private static Border Clickable(UIElement content, bool selected, Action click)
    {
        var border = new Border
        {
            Child = content,
            CornerRadius = new CornerRadius(6),
            Padding = new Thickness(8, 7, 8, 7),
            Margin = new Thickness(0, 1, 0, 1),
            Cursor = Cursors.Hand,
            Background = Brushes.Transparent,
        };
        if (selected)
            border.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");

        border.MouseEnter += (_, _) => border.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
        border.MouseLeave += (_, _) =>
        {
            if (selected)
                border.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
            else
                border.Background = Brushes.Transparent;
        };
        border.MouseLeftButtonUp += (_, e) =>
        {
            click();
            e.Handled = true;
        };
        return border;
    }

    private static Brush? FindBrush(UIElement target, params string[] keys)
    {
        foreach (var key in keys)
        {
            switch ((target as FrameworkElement)?.TryFindResource(key))
            {
                case Brush brush:
                    return brush;
                case Color color:
                    return new SolidColorBrush(color);
            }
        }

        return null;
    }

    private static TextBlock Secondary(string text, double size) =>
        new() { Text = text, FontSize = size, Opacity = 0.62, TextTrimming = TextTrimming.CharacterEllipsis };

    private static TextBlock Caption(string text) =>
        new() { Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold, Opacity = 0.55 };

    private static Border Tag(string text, bool accent)
    {
        var tag = new Border
        {
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(6, 0, 6, 1),
            Margin = new Thickness(8, 0, 0, 0),
            VerticalAlignment = VerticalAlignment.Center,
            Child = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.SemiBold },
        };
        if (accent)
        {
            tag.SetResourceReference(Border.BackgroundProperty, "AccentFillColorDefaultBrush");
            ((TextBlock)tag.Child).SetResourceReference(TextBlock.ForegroundProperty, "TextOnAccentFillColorPrimaryBrush");
        }
        else
        {
            tag.SetResourceReference(Border.BackgroundProperty, "SubtleFillColorSecondaryBrush");
            ((TextBlock)tag.Child).Opacity = 0.75;
        }

        return tag;
    }
}
