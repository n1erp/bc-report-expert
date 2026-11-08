using System.IO;
using System.Text;

namespace ReportExpert.Modules.Copilot.Services;

/// <summary>What kind of attachment a file is, which decides how it reaches the model.</summary>
public enum ChatAttachmentKind
{
    /// <summary>A text file (RDLC, XML, JSON, AL…), inlined into the message.</summary>
    Text,

    /// <summary>A PDF, whose extracted text is inlined into the message.</summary>
    Pdf,

    /// <summary>An image, sent as an image part the model can look at.</summary>
    Image,
}

/// <summary>
/// A file or image the user attached to a Rex message, already read and ready to send.
/// </summary>
public sealed class ChatAttachment
{
    public required string FileName { get; init; }

    public required ChatAttachmentKind Kind { get; init; }

    /// <summary>Where it came from, or <see langword="null"/> for a pasted image.</summary>
    public string? SourcePath { get; init; }

    /// <summary>The original size in bytes.</summary>
    public long SizeBytes { get; init; }

    /// <summary>The text sent to the model (text and PDF attachments).</summary>
    public string Text { get; init; } = string.Empty;

    /// <summary>Whether <see cref="Text"/> was cut to the size limit.</summary>
    public bool IsTruncated { get; init; }

    /// <summary>The image sent to the model (image attachments), after resizing.</summary>
    public ChatImage? Image { get; init; }

    /// <summary>The encoded image bytes, kept for the preview thumbnail.</summary>
    public byte[]? ImageBytes { get; init; }

    /// <summary>Short type label, such as <c>RDLC</c>, <c>JSON</c> or <c>PNG</c>.</summary>
    public string TypeLabel { get; init; } = string.Empty;

    /// <summary>"RDLC · 42 KB" — shown on the attachment chip.</summary>
    public string Detail => $"{TypeLabel} · {ChatAttachmentLoader.FormatSize(SizeBytes)}" + (IsTruncated ? " · truncated" : string.Empty);

    /// <summary>
    /// The block inlined into the user message for a text or PDF attachment. Tags rather than a
    /// code fence, so a file that itself contains <c>```</c> cannot end the block early.
    /// </summary>
    public string ToPromptBlock()
    {
        var builder = new StringBuilder();
        builder.Append("<attached_file name=\"").Append(FileName.Replace("\"", "'"))
            .Append("\" type=\"").Append(TypeLabel)
            .Append("\" size=\"").Append(ChatAttachmentLoader.FormatSize(SizeBytes)).Append('"');
        if (IsTruncated)
            builder.Append(" truncated=\"true\"");
        builder.Append(">\n").Append(Text.TrimEnd()).Append("\n</attached_file>");
        return builder.ToString();
    }
}

/// <summary>
/// Reads files and images into <see cref="ChatAttachment"/>s, with the limits that keep a message
/// within what the providers accept.
/// </summary>
public static partial class ChatAttachmentLoader
{
    /// <summary>Characters of one text file sent to the model; the rest is cut.</summary>
    public const int MaxTextChars = 60_000;

    /// <summary>Largest text or PDF file read at all.</summary>
    public const long MaxTextFileBytes = 8L * 1024 * 1024;

    /// <summary>Largest image file read at all.</summary>
    public const long MaxImageFileBytes = 20L * 1024 * 1024;

    /// <summary>Longest side of an image sent to the model. Larger screenshots are scaled down.</summary>
    public const int MaxImageSide = 2000;

    /// <summary>Most attachments on one message.</summary>
    public const int MaxAttachments = 10;

    private static readonly Dictionary<string, string> TextTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".rdlc"] = "RDLC", [".rdl"] = "RDL", [".xml"] = "XML", [".xsd"] = "XSD", [".xslt"] = "XSLT",
        [".txt"] = "TXT", [".log"] = "LOG", [".md"] = "MD", [".json"] = "JSON", [".csv"] = "CSV",
        [".al"] = "AL", [".cs"] = "C#", [".sql"] = "SQL", [".xaml"] = "XAML", [".config"] = "CONFIG",
        [".yml"] = "YAML", [".yaml"] = "YAML", [".html"] = "HTML", [".htm"] = "HTML", [".css"] = "CSS",
        [".js"] = "JS", [".ts"] = "TS", [".ps1"] = "PS1", [".ini"] = "INI", [".editorconfig"] = "CONFIG",
        [".resx"] = "RESX", [".xlf"] = "XLIFF", [".props"] = "MSBUILD", [".csproj"] = "MSBUILD",
    };

    private static readonly Dictionary<string, string> ImageTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".png"] = "PNG", [".jpg"] = "JPEG", [".jpeg"] = "JPEG", [".gif"] = "GIF", [".bmp"] = "BMP",
        [".tif"] = "TIFF", [".tiff"] = "TIFF", [".webp"] = "WEBP",
    };

    /// <summary>Filter for the "attach files" dialog.</summary>
    public const string FileDialogFilter =
        "Development files|*.rdlc;*.rdl;*.xml;*.xsd;*.txt;*.log;*.md;*.json;*.csv;*.al;*.cs;*.sql;*.xaml;*.config;*.yml;*.yaml;*.html;*.css;*.js;*.ts;*.ps1;*.pdf;*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.webp" +
        "|Reports (*.rdlc;*.rdl)|*.rdlc;*.rdl|PDF (*.pdf)|*.pdf|All files (*.*)|*.*";

    /// <summary>Filter for the "attach image" dialog.</summary>
    public const string ImageDialogFilter = "Images|*.png;*.jpg;*.jpeg;*.gif;*.bmp;*.tif;*.tiff;*.webp|All files (*.*)|*.*";

    /// <summary>Whether a file name looks like an image REX can attach.</summary>
    public static bool IsImageFile(string path) => ImageTypes.ContainsKey(Path.GetExtension(path));

    /// <summary>Reads a file into an attachment.</summary>
    /// <exception cref="InvalidOperationException">The file cannot be attached; the message says why.</exception>
    public static ChatAttachment LoadFile(string path)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new InvalidOperationException($"{info.Name} no longer exists.");

        string extension = info.Extension;

        if (ImageTypes.TryGetValue(extension, out string? imageType))
        {
            if (info.Length > MaxImageFileBytes)
                throw new InvalidOperationException($"{info.Name} is larger than {FormatSize(MaxImageFileBytes)}.");

            return FromImageBytes(File.ReadAllBytes(path), info.Name, imageType, path);
        }

        if (info.Length > MaxTextFileBytes)
            throw new InvalidOperationException($"{info.Name} is larger than {FormatSize(MaxTextFileBytes)}.");

        if (extension.Equals(".pdf", StringComparison.OrdinalIgnoreCase))
            return LoadPdf(info);

        string typeLabel = TextTypes.TryGetValue(extension, out string? known)
            ? known
            : (extension.Length > 1 ? extension[1..].ToUpperInvariant() : "FILE");

        byte[] bytes = File.ReadAllBytes(path);
        if (!LooksLikeText(bytes))
            throw new InvalidOperationException($"{info.Name} is not a text file. Attach text files, PDFs, or images.");

        return FromText(ReadText(bytes), info.Name, typeLabel, info.Length, path);
    }

    /// <summary>Builds a text attachment, cutting it to <see cref="MaxTextChars"/>.</summary>
    public static ChatAttachment FromText(string text, string fileName, string typeLabel, long sizeBytes, string? sourcePath = null)
    {
        bool truncated = text.Length > MaxTextChars;
        if (truncated)
            text = text[..MaxTextChars] + "\n… (truncated: the file continues beyond what was sent)";

        return new ChatAttachment
        {
            FileName = fileName,
            Kind = ChatAttachmentKind.Text,
            SourcePath = sourcePath,
            SizeBytes = sizeBytes,
            Text = text,
            IsTruncated = truncated,
            TypeLabel = typeLabel,
        };
    }

    /// <summary>"512 B", "42 KB", "1.3 MB".</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => $"{bytes / 1024.0:0} KB",
        _ => $"{bytes / (1024.0 * 1024):0.#} MB",
    };

    /// <summary>
    /// Composes the text sent for a user turn: what they typed, then each text attachment.
    /// </summary>
    public static string ComposePrompt(string text, IReadOnlyList<ChatAttachment> attachments)
    {
        var builder = new StringBuilder(text.Trim());
        var images = attachments.Where(a => a.Kind == ChatAttachmentKind.Image).ToList();

        foreach (var attachment in attachments.Where(a => a.Kind != ChatAttachmentKind.Image))
        {
            if (builder.Length > 0)
                builder.Append("\n\n");
            builder.Append(attachment.ToPromptBlock());
        }

        if (images.Count > 0)
        {
            if (builder.Length > 0)
                builder.Append("\n\n");
            builder.Append("Attached image(s): ").Append(string.Join(", ", images.Select(i => i.FileName))).Append('.');
        }

        return builder.ToString();
    }

    private static ChatAttachment LoadPdf(FileInfo info)
    {
        var text = new StringBuilder();
        try
        {
            using var document = UglyToad.PdfPig.PdfDocument.Open(info.FullName);
            foreach (var page in document.GetPages())
            {
                string pageText = UglyToad.PdfPig.DocumentLayoutAnalysis.TextExtractor.ContentOrderTextExtractor.GetText(page);
                text.Append("--- Page ").Append(page.Number).Append(" ---\n").Append(pageText.Trim()).Append("\n\n");
                if (text.Length > MaxTextChars)
                    break;
            }
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            throw new InvalidOperationException($"{info.Name} could not be read as a PDF.");
        }

        string content = text.ToString();
        if (content.Replace("--- Page", string.Empty).Trim().Trim('-').Length == 0 || !content.Any(char.IsLetterOrDigit))
            content = "(This PDF has no extractable text — it is probably scanned. Attach a screenshot of the page instead.)";

        var attachment = FromText(content, info.Name, "PDF", info.Length, info.FullName);
        return new ChatAttachment
        {
            FileName = attachment.FileName,
            Kind = ChatAttachmentKind.Pdf,
            SourcePath = attachment.SourcePath,
            SizeBytes = attachment.SizeBytes,
            Text = attachment.Text,
            IsTruncated = attachment.IsTruncated,
            TypeLabel = "PDF",
        };
    }

    /// <summary>Text unless it holds NUL bytes (outside a UTF-16 byte-order mark).</summary>
    private static bool LooksLikeText(byte[] bytes)
    {
        if (bytes.Length >= 2 && ((bytes[0] == 0xFF && bytes[1] == 0xFE) || (bytes[0] == 0xFE && bytes[1] == 0xFF)))
            return true;

        int length = Math.Min(bytes.Length, 8192);
        for (int i = 0; i < length; i++)
        {
            if (bytes[i] == 0)
                return false;
        }

        return true;
    }

    private static string ReadText(byte[] bytes)
    {
        using var reader = new StreamReader(new MemoryStream(bytes), Encoding.UTF8, detectEncodingFromByteOrderMarks: true);
        return reader.ReadToEnd();
    }
}
