using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ReportExpert.Modules.Copilot.Services;
using Wpf.Ui.Controls;

namespace ReportExpert.Modules.Copilot.ViewModels;

/// <summary>An attachment chip: in the composer before sending, and on the sent message.</summary>
public sealed class ChatAttachmentViewModel
{
    private ImageSource? _thumbnail;

    public ChatAttachmentViewModel(ChatAttachment attachment)
    {
        Attachment = attachment;
    }

    public ChatAttachment Attachment { get; }

    public string Name => Attachment.FileName;

    public string Detail => Attachment.Detail;

    public bool IsImage => Attachment.Kind == ChatAttachmentKind.Image;

    public bool IsFile => !IsImage;

    /// <summary>Hover text: the full path when there is one.</summary>
    public string ToolTip => Attachment.SourcePath ?? Attachment.FileName;

    /// <summary>The icon shown for a file attachment.</summary>
    public SymbolRegular Icon => Attachment.TypeLabel switch
    {
        "PDF" => SymbolRegular.DocumentPdf24,
        "RDLC" or "RDL" => SymbolRegular.DocumentRibbon24,
        "JSON" => SymbolRegular.Braces24,
        "XML" or "XSD" or "XSLT" or "XAML" or "HTML" or "RESX" or "XLIFF" or "MSBUILD" => SymbolRegular.Code24,
        "AL" or "C#" or "SQL" or "JS" or "TS" or "CSS" or "PS1" => SymbolRegular.Code24,
        "CSV" => SymbolRegular.DocumentData24,
        _ => SymbolRegular.DocumentText24,
    };

    /// <summary>A small decoded copy of the image for the chip, created on first use.</summary>
    public ImageSource? Thumbnail
    {
        get
        {
            if (_thumbnail is not null || Attachment.ImageBytes is not { } bytes)
                return _thumbnail;

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.DecodePixelWidth = 320;
            image.StreamSource = new MemoryStream(bytes);
            image.EndInit();
            image.Freeze();
            _thumbnail = image;
            return _thumbnail;
        }
    }
}
