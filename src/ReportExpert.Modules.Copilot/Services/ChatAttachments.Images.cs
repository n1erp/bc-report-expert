using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ReportExpert.Modules.Copilot.Services;

/// <summary>Image attachments: decoding, scaling and re-encoding with WPF imaging.</summary>
public static partial class ChatAttachmentLoader
{
    /// <summary>
    /// Builds an image attachment. The image is decoded, scaled so its longest side is at most
    /// <see cref="MaxImageSide"/>, and re-encoded as PNG (or JPEG for large photos).
    /// </summary>
    public static ChatAttachment FromImageBytes(byte[] bytes, string fileName, string typeLabel, string? sourcePath = null)
    {
        BitmapSource bitmap;
        try
        {
            using var stream = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
            bitmap = decoder.Frames[0];
        }
        catch (Exception exception) when (exception is NotSupportedException or FileFormatException or ArgumentException)
        {
            throw new InvalidOperationException($"{fileName} could not be read as an image ({typeLabel}).");
        }

        return FromBitmap(bitmap, fileName, typeLabel, bytes.LongLength, sourcePath);
    }

    /// <summary>Builds an image attachment from a bitmap, such as a pasted screenshot.</summary>
    public static ChatAttachment FromBitmap(BitmapSource bitmap, string fileName, string typeLabel = "PNG", long? sizeBytes = null, string? sourcePath = null)
    {
        BitmapSource scaled = bitmap;
        int longest = Math.Max(bitmap.PixelWidth, bitmap.PixelHeight);
        if (longest > MaxImageSide)
        {
            double factor = (double)MaxImageSide / longest;
            scaled = new TransformedBitmap(bitmap, new ScaleTransform(factor, factor));
        }

        byte[] png = Encode(new PngBitmapEncoder(), scaled);
        string mediaType = "image/png";
        byte[] payload = png;

        // Photos compress poorly as PNG; JPEG keeps them small enough for every provider.
        if (png.Length > 3 * 1024 * 1024)
        {
            payload = Encode(new JpegBitmapEncoder { QualityLevel = 85 }, scaled);
            mediaType = "image/jpeg";
        }

        return new ChatAttachment
        {
            FileName = fileName,
            Kind = ChatAttachmentKind.Image,
            SourcePath = sourcePath,
            SizeBytes = sizeBytes ?? payload.LongLength,
            Image = new ChatImage(mediaType, Convert.ToBase64String(payload)),
            ImageBytes = payload,
            TypeLabel = typeLabel,
        };
    }

    private static byte[] Encode(BitmapEncoder encoder, BitmapSource bitmap)
    {
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var output = new MemoryStream();
        encoder.Save(output);
        return output.ToArray();
    }
}
