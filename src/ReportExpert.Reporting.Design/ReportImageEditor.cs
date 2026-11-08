using System.Globalization;
using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design;

/// <summary>Manages the report's embedded images. Only PNG and JPEG are accepted, checked by their file signature.</summary>
internal static class ReportImageEditor
{
    public const int MaxBytes = 256 * 1024;

    public const int MaxImages = 20;

    public static IReadOnlyList<string> Names(RdlDocument document) =>
        Container(document, create: false)?.Elements(document.Ns + "EmbeddedImage")
            .Select(image => image.Attribute("Name")?.Value)
            .OfType<string>()
            .ToArray() ?? [];

    public static IReadOnlyList<DesignImage> Read(RdlDocument document)
    {
        var ns = document.Ns;
        var used = UsedNames(document);
        return Container(document, create: false)?.Elements(ns + "EmbeddedImage")
            .Select(image =>
            {
                var name = image.Attribute("Name")?.Value ?? "";
                var mime = image.Element(ns + "MIMEType")?.Value ?? "image/png";
                var data = image.Element(ns + "ImageData")?.Value ?? "";
                return new DesignImage(name, mime, "data:" + mime + ";base64," + data, data.Length * 3 / 4, used.Contains(name));
            })
            .ToArray() ?? [];
    }

    /// <summary>Embeds an image and returns the name it was stored under.</summary>
    public static string Add(RdlDocument document, string fileName, byte[] content)
    {
        if (content.Length is 0 or > MaxBytes)
        {
            throw new ReportDesignException(ReportCodes.InvalidImage);
        }

        var mime = Mime(content) ?? throw new ReportDesignException(ReportCodes.InvalidImage);
        var names = Names(document);
        if (names.Count >= MaxImages)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        var stem = new string(Path.GetFileNameWithoutExtension(fileName ?? "").Where(ch => char.IsAsciiLetterOrDigit(ch) || ch == '_').Take(30).ToArray());
        if (stem.Length == 0 || !char.IsAsciiLetter(stem[0]))
        {
            stem = "Image" + stem;
        }

        var name = stem;
        for (var index = 2; names.Contains(name, StringComparer.OrdinalIgnoreCase) || NameTaken(document, name); index++)
        {
            name = stem + index.ToString(CultureInfo.InvariantCulture);
        }

        var ns = document.Ns;
        Container(document, create: true)!.Add(new XElement(
            ns + "EmbeddedImage",
            new XAttribute("Name", name),
            new XElement(ns + "MIMEType", mime),
            new XElement(ns + "ImageData", Convert.ToBase64String(content))));
        return name;
    }

    public static void Remove(RdlDocument document, string name)
    {
        var image = Container(document, create: false)?.Elements(document.Ns + "EmbeddedImage")
            .FirstOrDefault(candidate => string.Equals(candidate.Attribute("Name")?.Value, name, StringComparison.Ordinal))
            ?? throw new ReportDesignException(ReportCodes.InvalidItem);
        if (UsedNames(document).Contains(name))
        {
            throw new ReportDesignException(ReportCodes.ImageInUse);
        }

        image.Remove();
    }

    private static string? Mime(byte[] content)
    {
        if (content.Length > 8 && content[0] == 0x89 && content[1] == 0x50 && content[2] == 0x4E && content[3] == 0x47)
        {
            return "image/png";
        }

        if (content.Length > 3 && content[0] == 0xFF && content[1] == 0xD8 && content[2] == 0xFF)
        {
            return "image/jpeg";
        }

        return null;
    }

    private static HashSet<string> UsedNames(RdlDocument document)
    {
        var ns = document.Ns;
        return document.Root.Descendants()
            .Where(element => element.Name.LocalName is "Image" or "BackgroundImage"
                && string.Equals(element.Element(ns + "Source")?.Value, "Embedded", StringComparison.OrdinalIgnoreCase))
            .Select(element => element.Element(ns + "Value")?.Value)
            .OfType<string>()
            .ToHashSet(StringComparer.Ordinal);
    }

    private static bool NameTaken(RdlDocument document, string name) =>
        document.Root.Descendants().Any(element => string.Equals(element.Attribute("Name")?.Value, name, StringComparison.OrdinalIgnoreCase));

    private static XElement? Container(RdlDocument document, bool create)
    {
        var ns = document.Ns;
        var images = document.Root.Element(ns + "EmbeddedImages");
        if (images is null && create)
        {
            images = new XElement(ns + "EmbeddedImages");
            document.Root.Add(images);
        }

        return images;
    }
}
