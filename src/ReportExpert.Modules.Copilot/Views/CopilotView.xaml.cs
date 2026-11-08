using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ReportExpert.Modules.Copilot.Services;
using ReportExpert.Modules.Copilot.ViewModels;

namespace ReportExpert.Modules.Copilot.Views;

public partial class CopilotView : System.Windows.Controls.UserControl
{
    public CopilotViewModel ViewModel { get; }

    public CopilotView()
    {
        InitializeComponent();
        ViewModel = new CopilotViewModel();
        DataContext = ViewModel;

        ((INotifyCollectionChanged)ViewModel.Messages).CollectionChanged += (_, _) =>
            ChatScroll.ScrollToEnd();

        // Dictated text lands in the box: keep the caret at the end so the user can carry on editing.
        ViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(CopilotViewModel.InputText) && ViewModel.IsRecording)
                InputBox.CaretIndex = InputBox.Text.Length;
        };
    }

    private void InputBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        // Ctrl+V with an image or files on the clipboard attaches them instead of pasting text.
        if (e.Key == Key.V && Keyboard.Modifiers == ModifierKeys.Control && TryPasteAttachments())
        {
            e.Handled = true;
            return;
        }

        if (e.Key != Key.Enter)
            return;

        // Shift+Enter inserts a new line; Enter alone sends.
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            return;

        e.Handled = true;
        if (ViewModel.SendCommand.CanExecute(null))
            ViewModel.SendCommand.Execute(null);
    }

    private bool TryPasteAttachments()
    {
        try
        {
            if (Clipboard.ContainsFileDropList())
            {
                var files = Clipboard.GetFileDropList().Cast<string>().ToList();
                if (files.Count > 0)
                {
                    ViewModel.AddAttachmentFiles(files);
                    return true;
                }
            }

            if (Clipboard.ContainsImage() && Clipboard.GetImage() is { } image)
            {
                ViewModel.AddPastedImage(image);
                return true;
            }
        }
        catch (System.Runtime.InteropServices.ExternalException)
        {
            // Another application holds the clipboard; fall back to a normal paste.
        }

        return false;
    }

    private void AttachFile_Click(object sender, RoutedEventArgs e) =>
        PickFiles("Attach files for Rex", ChatAttachmentLoader.FileDialogFilter);

    private void AttachImage_Click(object sender, RoutedEventArgs e) =>
        PickFiles("Attach images for Rex", ChatAttachmentLoader.ImageDialogFilter);

    private void PickFiles(string title, string filter)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = title,
            Filter = filter,
            Multiselect = true,
            CheckFileExists = true,
        };

        if (dialog.ShowDialog(Window.GetWindow(this)) == true)
            ViewModel.AddAttachmentFiles(dialog.FileNames);

        InputBox.Focus();
    }

    private void ComposerBox_DragOver(object sender, DragEventArgs e)
    {
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = files ? DragDropEffects.Copy : DragDropEffects.None;
        ComposerBox.BorderBrush = files
            ? (System.Windows.Media.Brush)FindResource("AccentTextFillColorPrimaryBrush")
            : (System.Windows.Media.Brush)FindResource("CardStrokeColorDefaultBrush");
        e.Handled = true;
    }

    private void ComposerBox_DragLeave(object sender, DragEventArgs e) =>
        ComposerBox.BorderBrush = (System.Windows.Media.Brush)FindResource("CardStrokeColorDefaultBrush");

    private void ComposerBox_Drop(object sender, DragEventArgs e)
    {
        ComposerBox_DragLeave(sender, e);
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files)
        {
            ViewModel.AddAttachmentFiles(files);
            InputBox.Focus();
        }

        e.Handled = true;
    }

    /// <summary>The provider + model picker: providers, then their models, with search across all of them.</summary>
    private void ModelPicker_Click(object sender, RoutedEventArgs e) =>
        new ModelPickerPopup(ViewModel, ModelPickerButton).Show();
}
