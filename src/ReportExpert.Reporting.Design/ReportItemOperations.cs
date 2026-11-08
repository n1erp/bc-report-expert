using System.Globalization;
using System.Xml.Linq;
using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design;

/// <summary>Toolbar operations on whole report items: clipboard, z-order, alignment, page bands and the explorer tree.</summary>
internal static class ReportItemOperations
{
    public static readonly string[] Operations =
    [
        "BringToFront", "SendToBack", "BringForward", "SendBackward",
        "AlignLeft", "AlignCenter", "AlignRight", "AlignTop", "AlignMiddle", "AlignBottom",
        "FitWidth", "CenterHorizontally", "CenterVertically", "AlignToGrid", "SizeToGrid",
    ];

    /// <summary>Operations on several selected items. Alignment and sizing follow the first item, as in the reference designer.</summary>
    public static readonly string[] GroupOperations =
    [
        "AlignLeft", "AlignCenter", "AlignRight", "AlignTop", "AlignMiddle", "AlignBottom",
        "SameSize", "SameWidth", "SameHeight", "DistributeHorizontally", "DistributeVertically",
    ];

    /// <summary>
    /// The arrange operations that apply to a selection of <paramref name="count"/> surface items: one item
    /// can be aligned in its container, centred, fitted, snapped and reordered; two or more can also be made
    /// the same size; three or more can also be spaced evenly.
    /// </summary>
    public static IReadOnlyList<string> Applicable(int count) => count switch
    {
        <= 0 => [],
        1 => Operations,
        2 => [.. Operations, "SameSize", "SameWidth", "SameHeight"],
        _ => [.. Operations, "SameSize", "SameWidth", "SameHeight", "DistributeHorizontally", "DistributeVertically"],
    };

    /// <summary>The grid the designer draws and snaps to, in inches.</summary>
    public const double Grid = 0.125;

    private static readonly string[] SurfaceKinds = ["Textbox", "Rectangle", "Line", "Image", "Tablix", "Chart", "Gauge", "Map"];

    /// <summary>Serializes a surface item for the clipboard.</summary>
    public static string Copy(RdlDocument document, string name) => Require(document, name).ToString(SaveOptions.DisableFormatting);

    /// <summary>Inserts a copied item with fresh names, offset from the original, and returns its new name.</summary>
    public static string Paste(RdlDocument document, string xml, string? region)
    {
        XElement item;
        try
        {
            item = XElement.Parse(xml, LoadOptions.None);
        }
        catch (System.Xml.XmlException exception)
        {
            throw new ReportDesignException(ReportCodes.ClipboardEmpty, exception);
        }

        var ns = document.Ns;
        if (item.Name.Namespace != ns || !SurfaceKinds.Contains(item.Name.LocalName, StringComparer.Ordinal))
        {
            throw new ReportDesignException(ReportCodes.ClipboardEmpty);
        }

        var dataRegion = item.Name.LocalName is "Tablix" or "Chart" or "Gauge" or "Map";
        if (dataRegion)
        {
            var dataSet = item.Element(ns + "DataSetName")?.Value;
            if (string.IsNullOrEmpty(dataSet) || !ReportDataSetEditor.Names(document).Contains(dataSet, StringComparer.Ordinal))
            {
                throw new ReportDesignException(ReportCodes.NeedDataSet);
            }
        }

        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var named in item.DescendantsAndSelf().Where(element => element.Attribute("Name") is not null).ToArray())
        {
            var current = named.Attribute("Name")!.Value;
            // Chart areas, legends, titles and axes are named within their chart, so their names stay.
            if (named.Name.LocalName is "ChartArea" or "ChartLegend" or "ChartTitle" or "ChartNoDataMessage" or "ChartAxis")
            {
                continue;
            }

            var prefix = new string(current.TakeWhile(char.IsAsciiLetter).ToArray());

            named.SetAttributeValue("Name", ReportLayoutEditor.NextName(document, prefix.Length == 0 ? "Item" : prefix, reserved));
        }

        Shift(item, ns, "Left");
        Shift(item, ns, "Top");
        var parent = !dataRegion && region is "Header" or "Footer"
            ? Band(document, region == "Header" ? "PageHeader" : "PageFooter")
            : ReportLayoutEditor.BodyItems(document);
        parent.Add(item);
        ReportLayoutEditor.FitBody(document);
        return item.Attribute("Name")!.Value;
    }

    public static void Arrange(RdlDocument document, string name, string operation)
    {
        var item = Require(document, name);
        var ns = document.Ns;
        var siblings = item.Parent!;
        switch (operation)
        {
            case "BringToFront":
                item.Remove();
                siblings.Add(item);
                break;
            case "SendToBack":
                item.Remove();
                siblings.AddFirst(item);
                break;
            case "BringForward":
                if (item.NextNode is XElement next)
                {
                    item.Remove();
                    next.AddAfterSelf(item);
                }

                break;
            case "SendBackward":
                if (item.PreviousNode is XElement previous)
                {
                    item.Remove();
                    previous.AddBeforeSelf(item);
                }

                break;
            case "AlignLeft" or "AlignCenter" or "AlignRight" or "AlignTop" or "AlignMiddle" or "AlignBottom" or "FitWidth"
                or "CenterHorizontally" or "CenterVertically" or "AlignToGrid" or "SizeToGrid":
                Align(document, item, operation);
                return;
            default:
                throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var index = 0;
        foreach (var sibling in siblings.Elements())
        {
            Set(sibling, ns, "ZIndex", (index++).ToString(CultureInfo.InvariantCulture));
        }
    }

    /// <summary>Adds or removes the page header or footer band.</summary>
    public static void ToggleBand(RdlDocument document, string region, bool show)
    {
        var ns = document.Ns;
        var page = document.Descendants("Page").First();
        var bandName = region switch
        {
            "Header" => "PageHeader",
            "Footer" => "PageFooter",
            _ => throw new ReportDesignException(ReportCodes.InvalidItem),
        };
        var band = page.Element(ns + bandName);
        if (show)
        {
            Band(document, bandName);
        }
        else
        {
            band?.Remove();
        }
    }

    public static IReadOnlyList<DesignExplorerNode> Explorer(RdlDocument document)
    {
        var ns = document.Ns;
        var page = document.Descendants("Page").FirstOrDefault();
        var nodes = new List<DesignExplorerNode>();
        if (page?.Element(ns + "PageHeader") is { } header)
        {
            nodes.Add(new DesignExplorerNode(ReportPropertySheet.HeaderTarget, "PageHeader", Items(header.Element(ns + "ReportItems"), ns)));
        }

        var body = document.Descendants("Body").FirstOrDefault();
        nodes.Add(new DesignExplorerNode("@Body", "Body", Items(body?.Element(ns + "ReportItems"), ns)));
        if (page?.Element(ns + "PageFooter") is { } footer)
        {
            nodes.Add(new DesignExplorerNode(ReportPropertySheet.FooterTarget, "PageFooter", Items(footer.Element(ns + "ReportItems"), ns)));
        }

        return [new DesignExplorerNode(ReportPropertySheet.ReportTarget, "Report", nodes)];
    }

    private static IReadOnlyList<DesignExplorerNode> Items(XElement? container, XNamespace ns) =>
        container?.Elements()
            .Where(element => SurfaceKinds.Contains(element.Name.LocalName, StringComparer.Ordinal))
            .Select(element => new DesignExplorerNode(
                element.Attribute("Name")?.Value ?? "",
                element.Name.LocalName,
                element.Name.LocalName == "Rectangle" ? Items(element.Element(ns + "ReportItems"), ns) : []))
            .ToArray() ?? [];

    private static void Align(RdlDocument document, XElement item, string operation)
    {
        var ns = document.Ns;
        var container = item.Parent?.Parent;
        var section = document.Descendants("ReportSection").First();
        var containerWidth = container?.Name.LocalName == "Rectangle"
            ? Dimension.ToInches(container.Element(ns + "Width")?.Value)
            : Dimension.ToInches(section.Element(ns + "Width")?.Value);
        var containerHeight = Dimension.ToInches(container?.Element(ns + "Height")?.Value);
        var name = item.Attribute("Name")!.Value;
        var left = Dimension.ToInches(item.Element(ns + "Left")?.Value);
        var top = Dimension.ToInches(item.Element(ns + "Top")?.Value);
        var (width, height) = Size(item, ns);
        switch (operation)
        {
            case "AlignLeft":
                left = 0;
                break;
            case "AlignCenter":
                left = Math.Max(0, (containerWidth - width) / 2);
                break;
            case "AlignRight":
                left = Math.Max(0, containerWidth - width);
                break;
            case "AlignTop":
                top = 0;
                break;
            case "AlignMiddle":
                top = Math.Max(0, (containerHeight - height) / 2);
                break;
            case "AlignBottom":
                top = Math.Max(0, containerHeight - height);
                break;
            case "FitWidth":
                left = 0;
                width = Math.Max(0.2, containerWidth);
                break;
            case "CenterHorizontally":
                left = Math.Max(0, (containerWidth - width) / 2);
                break;
            case "CenterVertically":
                top = Math.Max(0, (containerHeight - height) / 2);
                break;
            case "AlignToGrid":
                left = Snap(left);
                top = Snap(top);
                break;
            case "SizeToGrid":
                width = Math.Max(Grid, Snap(width));
                height = Math.Max(item.Name.LocalName == "Line" ? 0 : Grid, Snap(height));
                break;
        }

        ReportLayoutEditor.Move(document, name, left, top, width, height);
    }

    /// <summary>Aligns, sizes or spaces several items. The first name is the reference item.</summary>
    public static void ArrangeMany(RdlDocument document, IReadOnlyList<string> names, string operation)
    {
        if (!GroupOperations.Contains(operation, StringComparer.Ordinal))
        {
            // Order, centre, fit and grid operations act on each selected item on its own.
            if (!Operations.Contains(operation, StringComparer.Ordinal) || names.Count == 0)
            {
                throw new ReportDesignException(ReportCodes.InvalidItem);
            }

            var ordered = operation is "SendToBack" or "BringForward" ? names.Distinct(StringComparer.Ordinal).Reverse() : names.Distinct(StringComparer.Ordinal);
            foreach (var name in ordered)
            {
                Arrange(document, name, operation);
            }

            return;
        }

        var ns = document.Ns;
        var items = names.Distinct(StringComparer.Ordinal).Select(name => Require(document, name)).ToArray();
        if (items.Length < 2)
        {
            Arrange(document, names[0], operation);
            return;
        }

        var boxes = items.Select(item =>
        {
            var (width, height) = Size(item, ns);
            return (Item: item,
                Name: item.Attribute("Name")!.Value,
                Left: Dimension.ToInches(item.Element(ns + "Left")?.Value),
                Top: Dimension.ToInches(item.Element(ns + "Top")?.Value),
                Width: width,
                Height: height);
        }).ToArray();
        var first = boxes[0];
        if (operation is "DistributeHorizontally" or "DistributeVertically")
        {
            var horizontal = operation == "DistributeHorizontally";
            var ordered = boxes.OrderBy(box => horizontal ? box.Left : box.Top).ToArray();
            var start = horizontal ? ordered[0].Left : ordered[0].Top;
            var end = horizontal ? ordered[^1].Left + ordered[^1].Width : ordered[^1].Top + ordered[^1].Height;
            var used = ordered.Sum(box => horizontal ? box.Width : box.Height);
            var gap = Math.Max(0, (end - start - used) / (ordered.Length - 1));
            var cursor = start;
            foreach (var box in ordered)
            {
                ReportLayoutEditor.Move(document, box.Name, horizontal ? cursor : box.Left, horizontal ? box.Top : cursor, box.Width, box.Height);
                cursor += (horizontal ? box.Width : box.Height) + gap;
            }

            return;
        }

        foreach (var box in boxes.Skip(1))
        {
            var (left, top, width, height) = (box.Left, box.Top, box.Width, box.Height);
            switch (operation)
            {
                case "AlignLeft":
                    left = first.Left;
                    break;
                case "AlignCenter":
                    left = Math.Max(0, first.Left + (first.Width - width) / 2);
                    break;
                case "AlignRight":
                    left = Math.Max(0, first.Left + first.Width - width);
                    break;
                case "AlignTop":
                    top = first.Top;
                    break;
                case "AlignMiddle":
                    top = Math.Max(0, first.Top + (first.Height - height) / 2);
                    break;
                case "AlignBottom":
                    top = Math.Max(0, first.Top + first.Height - height);
                    break;
                case "SameSize":
                    width = first.Width;
                    height = first.Height;
                    break;
                case "SameWidth":
                    width = first.Width;
                    break;
                case "SameHeight":
                    height = first.Height;
                    break;
            }

            ReportLayoutEditor.Move(document, box.Name, left, top, width, height);
        }
    }

    private static double Snap(double inches) => Math.Round(inches / Grid, MidpointRounding.AwayFromZero) * Grid;

    private static (double Width, double Height) Size(XElement item, XNamespace ns) =>
        item.Name.LocalName == "Tablix"
            ? (TablixNavigator.Columns(item, ns).Sum(column => Dimension.ToInches(column.Element(ns + "Width")?.Value)),
                TablixNavigator.Rows(item, ns).Sum(row => Dimension.ToInches(row.Element(ns + "Height")?.Value)))
            : (Dimension.ToInches(item.Element(ns + "Width")?.Value), Dimension.ToInches(item.Element(ns + "Height")?.Value));

    private static void Shift(XElement item, XNamespace ns, string name)
    {
        var value = Dimension.ToInches(item.Element(ns + name)?.Value);
        Set(item, ns, name, Dimension.FromInches(Math.Min(19, value + 0.2)));
    }

    private static XElement Band(RdlDocument document, string bandName)
    {
        var ns = document.Ns;
        var page = document.Descendants("Page").First();
        var band = page.Element(ns + bandName);
        if (band is null)
        {
            band = new XElement(
                ns + bandName,
                new XElement(ns + "ReportItems"),
                new XElement(ns + "Height", bandName == "PageHeader" ? "0.5in" : "0.4in"),
                new XElement(ns + "PrintOnFirstPage", "true"),
                new XElement(ns + "PrintOnLastPage", "true"),
                new XElement(ns + "Style"));
            if (bandName == "PageHeader")
            {
                page.AddFirst(band);
            }
            else
            {
                page.Add(band);
            }
        }

        var items = band.Element(ns + "ReportItems");
        if (items is null)
        {
            items = new XElement(ns + "ReportItems");
            band.AddFirst(items);
        }

        return items;
    }

    private static XElement Require(RdlDocument document, string name)
    {
        if (!ReportDefinitionGuard.ValidIdentifier(name))
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var item = document.Root.Descendants()
            .FirstOrDefault(element => string.Equals(element.Attribute("Name")?.Value, name, StringComparison.Ordinal)
                && SurfaceKinds.Contains(element.Name.LocalName, StringComparer.Ordinal)
                && element.Parent?.Name.LocalName == "ReportItems");
        return item ?? throw new ReportDesignException(ReportCodes.InvalidItem);
    }

    private static void Set(XElement parent, XNamespace ns, string name, string value)
    {
        var child = parent.Element(ns + name);
        if (child is null)
        {
            parent.Add(new XElement(ns + name, value));
        }
        else
        {
            child.Value = value;
        }
    }
}
