using System.Globalization;
using System.Xml.Linq;
using ReportExpert.Rdl.Core;
using ReportExpert.Rdl.Core.Editing;

namespace ReportExpert.Reporting.Design;

internal static class ReportLayoutEditor
{
    private static readonly string[] Fonts = ["Arial", "Calibri", "Times New Roman", "Courier New", "Segoe UI", "Tahoma"];

    private static readonly string[] Alignments = ["Left", "Center", "Right"];

    private static readonly string[] Borders = ["None", "Solid", "Dashed", "Dotted"];

    public static string Place(RdlDocument document, string kind, double left, double top, string? dataSetName, IReadOnlyList<ReportField> fields, string? region = null)
    {
        left = Math.Clamp(left, 0, 20);
        top = Math.Clamp(top, 0, 20);
        if (kind == "Table")
        {
            return AddTable(document, left, Nudge(BodyItems(document), document.Ns, top), dataSetName, fields);
        }

        if (kind == "Chart" || kind.StartsWith("Chart:", StringComparison.Ordinal))
        {
            // RDL does not allow data regions in a page header or footer, so a chart always goes in the body.
            var body = BodyItems(document);
            var chartType = kind.Length > 6 ? kind[6..] : "Column";
            return ReportChartEditor.Add(document, body, left, Nudge(body, document.Ns, top), dataSetName, fields, chartType);
        }

        if (kind is "List" or "Matrix")
        {
            var body = BodyItems(document);
            return kind == "List"
                ? AddList(document, left, Nudge(body, document.Ns, top), dataSetName, fields)
                : AddMatrix(document, left, Nudge(body, document.Ns, top), dataSetName, fields);
        }

        var band = kind is "Header" or "Footer" ? kind : region is "Header" or "Footer" ? region : null;
        var parent = band switch
        {
            "Header" => BandItems(document, "PageHeader", 0.5),
            "Footer" => BandItems(document, "PageFooter", 0.4),
            _ => BodyItems(document),
        };
        top = Nudge(parent, document.Ns, top);
        return kind switch
        {
            "Textbox" => AddTextbox(document, parent, "Textbox", "Text", left, top, 2.6, 0.35),
            "Header" => AddTextbox(document, parent, "Header", "Header", left, top, 2.6, 0.3),
            "Footer" => AddTextbox(document, parent, "Footer", "Footer", left, top, 2.6, 0.3),
            "Rectangle" => AddRectangle(document, parent, left, top),
            "Line" => AddLine(document, parent, left, top),
            "Image" => AddImage(document, parent, left, top),
            _ => throw new ReportDesignException(ReportCodes.InvalidItem),
        };
    }

    public static void ResizeBand(RdlDocument document, string region, double height)
    {
        if (region is not ("Header" or "Footer"))
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        var items = region == "Header"
            ? BandItems(document, "PageHeader", height)
            : BandItems(document, "PageFooter", height);
        Set(items.Parent!, document.Ns, "Height", Inches(Math.Clamp(height, 0.3, 4)));
    }

    public static void Move(RdlDocument document, string name, double left, double top, double width, double height)
    {
        var item = RequireSurfaceItem(document, name);
        var minHeight = item.Name.LocalName == "Line" ? 0 : 0.15;
        var nextLeft = Math.Clamp(left, 0, 20);
        var nextTop = Math.Clamp(top, 0, 20);
        var nextWidth = Math.Clamp(width, 0.15, 20);
        var nextHeight = Math.Clamp(height, minHeight, 20);
        Set(item, document.Ns, "Left", Inches(nextLeft));
        Set(item, document.Ns, "Top", Inches(nextTop));
        if (item.Name.LocalName == "Tablix")
        {
            ReportTableEditor.Scale(item, document.Ns, nextWidth, nextHeight);
        }
        else
        {
            Set(item, document.Ns, "Width", Inches(nextWidth));
            Set(item, document.Ns, "Height", Inches(nextHeight));
        }

        GrowBand(item, document.Ns, nextTop, nextHeight);
        FitBody(document);
    }

    public static void Edit(RdlDocument document, ReportItemEdit edit)
    {
        if (!ReportDefinitionGuard.ValidIdentifier(edit.Name))
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        Move(document, edit.Name, edit.LeftInches, edit.TopInches, edit.WidthInches, edit.HeightInches);
        var item = RequireSurfaceItem(document, edit.Name);
        if (item.Name.LocalName == "Textbox" && edit.Value is not null)
        {
            TextboxEditor.SetTextboxValue(document, edit.Name, edit.Value.Length > 500 ? edit.Value[..500] : edit.Value);
        }

        var font = Fonts.Contains(edit.FontFamily ?? "", StringComparer.Ordinal) ? edit.FontFamily! : "Arial";
        var size = FontSize(edit.FontSize);
        var align = Alignments.Contains(edit.TextAlign ?? "", StringComparer.Ordinal) ? edit.TextAlign! : "Left";
        var border = Borders.Contains(edit.BorderStyle ?? "", StringComparer.Ordinal) ? edit.BorderStyle! : "None";
        var weight = edit.Bold ? "Bold" : "Normal";
        ApplyBorder(item, document.Ns, border);
        IEnumerable<XElement> textboxes = item.Name.LocalName == "Textbox"
            ? [item]
            : item.Descendants(document.Ns + "Textbox");
        foreach (var textbox in textboxes)
        {
            ApplyTextStyle(textbox, document.Ns, font, size, weight, align);
            ApplyBorder(textbox, document.Ns, border);
        }

        ReportItemEditor.SetVisibility(document, edit.Name, edit.Hidden ? "true" : "false");
        FitBody(document);
    }

    public static void Remove(RdlDocument document, string name)
    {
        RequireSurfaceItem(document, name);
        ReportItemEditor.RemoveReportItem(document, name);
        FitBody(document);
    }

    private static string AddTable(RdlDocument document, double left, double top, string? dataSetName, IReadOnlyList<ReportField> fields)
    {
        if (string.IsNullOrEmpty(dataSetName) || fields.Count == 0)
        {
            throw new ReportDesignException(ReportCodes.NeedDataSet);
        }

        var ns = document.Ns;
        var name = NextName(document, "Table");
        var width = Math.Min(1.5, Math.Max(0.8, 7.2 / fields.Count));
        var columns = new XElement(ns + "TablixColumns");
        var headerCells = new XElement(ns + "TablixCells");
        var detailCells = new XElement(ns + "TablixCells");
        var members = new XElement(ns + "TablixMembers");
        for (var index = 0; index < fields.Count; index++)
        {
            var field = fields[index];
            columns.Add(new XElement(ns + "TablixColumn", new XElement(ns + "Width", Inches(width))));
            headerCells.Add(Cell(document, $"{name}_H{index.ToString(CultureInfo.InvariantCulture)}", field.Name, bold: true));
            detailCells.Add(Cell(document, $"{name}_D{index.ToString(CultureInfo.InvariantCulture)}", "=Fields!" + field.Name + ".Value", bold: false));
            members.Add(new XElement(ns + "TablixMember"));
        }

        var tablix = new XElement(
            ns + "Tablix",
            new XAttribute("Name", name),
            new XElement(
                ns + "TablixBody",
                columns,
                new XElement(
                    ns + "TablixRows",
                    Row(document, "0.28", headerCells),
                    Row(document, "0.25", detailCells))),
            new XElement(ns + "TablixColumnHierarchy", members),
            new XElement(
                ns + "TablixRowHierarchy",
                new XElement(
                    ns + "TablixMembers",
                    new XElement(ns + "TablixMember", new XElement(ns + "KeepWithGroup", "After")),
                    new XElement(ns + "TablixMember", new XElement(ns + "Group", new XAttribute("Name", name + "Details"))))),
            new XElement(ns + "DataSetName", dataSetName),
            new XElement(ns + "Top", Inches(top)),
            new XElement(ns + "Left", Inches(left)),
            new XElement(ns + "Height", "0.53in"),
            new XElement(ns + "Width", Inches(width * fields.Count)),
            new XElement(ns + "Style"));
        BodyItems(document).Add(tablix);
        FitBody(document);
        return name;
    }

    /// <summary>A list: one free-form cell, repeated once per row, holding a rectangle with the first field.</summary>
    private static string AddList(RdlDocument document, double left, double top, string? dataSetName, IReadOnlyList<ReportField> fields)
    {
        if (string.IsNullOrEmpty(dataSetName) || fields.Count == 0)
        {
            throw new ReportDesignException(ReportCodes.NeedDataSet);
        }

        var ns = document.Ns;
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = NextName(document, "List", reserved);
        var rectangle = NextName(document, "Rectangle", reserved);
        var textbox = NextName(document, "Textbox", reserved);
        var content = new XElement(
            ns + "Textbox",
            new XAttribute("Name", textbox),
            new XElement(ns + "CanGrow", "true"),
            Paragraph(document, "=Fields!" + fields[0].Name + ".Value", bold: false),
            new XElement(ns + "Top", "0.05in"),
            new XElement(ns + "Left", "0.05in"),
            new XElement(ns + "Height", "0.3in"),
            new XElement(ns + "Width", "3.9in"),
            new XElement(ns + "Style", new XElement(ns + "PaddingLeft", "2pt"), new XElement(ns + "PaddingTop", "2pt")));
        var tablix = new XElement(
            ns + "Tablix",
            new XAttribute("Name", name),
            new XElement(
                ns + "TablixBody",
                new XElement(ns + "TablixColumns", new XElement(ns + "TablixColumn", new XElement(ns + "Width", "4in"))),
                new XElement(
                    ns + "TablixRows",
                    new XElement(
                        ns + "TablixRow",
                        new XElement(ns + "Height", "0.6in"),
                        new XElement(
                            ns + "TablixCells",
                            new XElement(
                                ns + "TablixCell",
                                new XElement(
                                    ns + "CellContents",
                                    new XElement(
                                        ns + "Rectangle",
                                        new XAttribute("Name", rectangle),
                                        new XElement(ns + "ReportItems", content),
                                        new XElement(ns + "KeepTogether", "true"),
                                        new XElement(ns + "Style", new XElement(ns + "Border", new XElement(ns + "Style", "Solid"), new XElement(ns + "Color", "LightGrey")))))))))),
            new XElement(ns + "TablixColumnHierarchy", new XElement(ns + "TablixMembers", new XElement(ns + "TablixMember"))),
            new XElement(
                ns + "TablixRowHierarchy",
                new XElement(ns + "TablixMembers", new XElement(ns + "TablixMember", new XElement(ns + "Group", new XAttribute("Name", name + "_Details"))))),
            new XElement(ns + "DataSetName", dataSetName),
            new XElement(ns + "Top", Inches(top)),
            new XElement(ns + "Left", Inches(left)),
            new XElement(ns + "Height", "0.6in"),
            new XElement(ns + "Width", "4in"),
            new XElement(ns + "Style"));
        BodyItems(document).Add(tablix);
        FitBody(document);
        return name;
    }

    /// <summary>A matrix: rows grouped by the first field, columns by the second, cells counting rows.</summary>
    private static string AddMatrix(RdlDocument document, double left, double top, string? dataSetName, IReadOnlyList<ReportField> fields)
    {
        if (string.IsNullOrEmpty(dataSetName) || fields.Count == 0)
        {
            throw new ReportDesignException(ReportCodes.NeedDataSet);
        }

        var ns = document.Ns;
        var reserved = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var name = NextName(document, "Matrix", reserved);
        var rowField = fields[0].Name;
        var columnField = fields.Count > 1 ? fields[1].Name : fields[0].Name;
        XElement Contents(string value, bool bold) =>
            new(ns + "CellContents", Cell(document, NextName(document, "Textbox", reserved), value, bold).Element(ns + "CellContents")!.Elements());
        XElement Header(string field, string size) =>
            new(
                ns + "TablixHeader",
                new XElement(ns + "Size", size),
                Contents("=Fields!" + field + ".Value", bold: true));
        XElement Grouped(string groupName, string field, string size) =>
            new(
                ns + "TablixMember",
                new XElement(
                    ns + "Group",
                    new XAttribute("Name", groupName),
                    new XElement(ns + "GroupExpressions", new XElement(ns + "GroupExpression", "=Fields!" + field + ".Value"))),
                new XElement(ns + "SortExpressions", new XElement(ns + "SortExpression", new XElement(ns + "Value", "=Fields!" + field + ".Value"))),
                Header(field, size));
        var tablix = new XElement(
            ns + "Tablix",
            new XAttribute("Name", name),
            new XElement(
                ns + "TablixCorner",
                new XElement(
                    ns + "TablixCornerRows",
                    new XElement(
                        ns + "TablixCornerRow",
                        new XElement(ns + "TablixCornerCell", Contents(rowField, bold: true))))),
            new XElement(
                ns + "TablixBody",
                new XElement(ns + "TablixColumns", new XElement(ns + "TablixColumn", new XElement(ns + "Width", "1in"))),
                new XElement(
                    ns + "TablixRows",
                    new XElement(
                        ns + "TablixRow",
                        new XElement(ns + "Height", "0.25in"),
                        new XElement(ns + "TablixCells", new XElement(ns + "TablixCell", Contents("=Count(Fields!" + rowField + ".Value)", bold: false)))))),
            new XElement(ns + "TablixColumnHierarchy", new XElement(ns + "TablixMembers", Grouped(name + "_ColumnGroup", columnField, "0.25in"))),
            new XElement(ns + "TablixRowHierarchy", new XElement(ns + "TablixMembers", Grouped(name + "_RowGroup", rowField, "1.5in"))),
            new XElement(ns + "DataSetName", dataSetName),
            new XElement(ns + "Top", Inches(top)),
            new XElement(ns + "Left", Inches(left)),
            new XElement(ns + "Height", "0.5in"),
            new XElement(ns + "Width", "2.5in"),
            new XElement(ns + "Style"));
        BodyItems(document).Add(tablix);
        FitBody(document);
        return name;
    }

    private static XElement Row(RdlDocument document, string height, XElement cells) =>
        new(document.Ns + "TablixRow", new XElement(document.Ns + "Height", height + "in"), cells);

    internal static XElement Cell(RdlDocument document, string name, string value, bool bold)
    {
        var ns = document.Ns;
        return new XElement(
            ns + "TablixCell",
            new XElement(
                ns + "CellContents",
                new XElement(
                    ns + "Textbox",
                    new XAttribute("Name", name),
                    new XElement(ns + "CanGrow", "true"),
                    Paragraph(document, value, bold),
                    new XElement(
                        ns + "Style",
                        new XElement(ns + "Border", new XElement(ns + "Style", "Solid"), new XElement(ns + "Color", "Black")),
                        new XElement(ns + "PaddingLeft", "2pt"),
                        new XElement(ns + "PaddingRight", "2pt"),
                        new XElement(ns + "PaddingTop", "2pt"),
                        new XElement(ns + "PaddingBottom", "2pt")))));
    }

    private static string AddTextbox(RdlDocument document, XElement parent, string prefix, string value, double left, double top, double width, double height)
    {
        var name = NextName(document, prefix);
        var ns = document.Ns;
        parent.Add(new XElement(
            ns + "Textbox",
            new XAttribute("Name", name),
            new XElement(ns + "CanGrow", "true"),
            new XElement(ns + "KeepTogether", "true"),
            Paragraph(document, value, bold: false),
            new XElement(ns + "Top", Inches(top)),
            new XElement(ns + "Left", Inches(left)),
            new XElement(ns + "Height", Inches(height)),
            new XElement(ns + "Width", Inches(width)),
            new XElement(
                ns + "Style",
                new XElement(ns + "Border", new XElement(ns + "Style", "None")),
                new XElement(ns + "PaddingLeft", "2pt"),
                new XElement(ns + "PaddingRight", "2pt"),
                new XElement(ns + "PaddingTop", "2pt"),
                new XElement(ns + "PaddingBottom", "2pt"))));
        FitBody(document);
        return name;
    }

    private static string AddRectangle(RdlDocument document, XElement parent, double left, double top)
    {
        var name = NextName(document, "Rectangle");
        var ns = document.Ns;
        parent.Add(new XElement(
            ns + "Rectangle",
            new XAttribute("Name", name),
            new XElement(ns + "ReportItems"),
            new XElement(ns + "Top", Inches(top)),
            new XElement(ns + "Left", Inches(left)),
            new XElement(ns + "Height", "1.00in"),
            new XElement(ns + "Width", "2.00in"),
            new XElement(ns + "Style", new XElement(ns + "Border", new XElement(ns + "Style", "Solid")))));
        FitBody(document);
        return name;
    }

    private static string AddLine(RdlDocument document, XElement parent, double left, double top)
    {
        var name = NextName(document, "Line");
        var ns = document.Ns;
        parent.Add(new XElement(
            ns + "Line",
            new XAttribute("Name", name),
            new XElement(ns + "Top", Inches(top)),
            new XElement(ns + "Left", Inches(left)),
            new XElement(ns + "Height", "0.00in"),
            new XElement(ns + "Width", "2.50in"),
            new XElement(ns + "Style", new XElement(ns + "Border", new XElement(ns + "Style", "Solid")))));
        FitBody(document);
        return name;
    }

    private static string AddImage(RdlDocument document, XElement parent, double left, double top)
    {
        EnsureMark(document);
        var name = NextName(document, "Image");
        var ns = document.Ns;
        parent.Add(new XElement(
            ns + "Image",
            new XAttribute("Name", name),
            new XElement(ns + "Source", "Embedded"),
            new XElement(ns + "Value", BlankReport.MarkName),
            new XElement(ns + "Sizing", "FitProportional"),
            new XElement(ns + "Top", Inches(top)),
            new XElement(ns + "Left", Inches(left)),
            new XElement(ns + "Height", "0.60in"),
            new XElement(ns + "Width", "1.20in"),
            new XElement(ns + "Style", new XElement(ns + "Border", new XElement(ns + "Style", "None")))));
        FitBody(document);
        return name;
    }

    private static XElement BandItems(RdlDocument document, string bandName, double bandHeight)
    {
        var ns = document.Ns;
        var page = document.Descendants("Page").First();
        var band = page.Element(ns + bandName);
        if (band is null)
        {
            band = new XElement(
                ns + bandName,
                new XElement(ns + "Height", Inches(bandHeight)),
                new XElement(ns + "PrintOnFirstPage", "true"),
                new XElement(ns + "PrintOnLastPage", "true"),
                new XElement(ns + "ReportItems"),
                new XElement(ns + "Style"));
            page.Add(band);
        }

        var items = band.Element(ns + "ReportItems");
        if (items is not null)
        {
            return items;
        }

        items = new XElement(ns + "ReportItems");
        band.AddFirst(items);
        return items;
    }

    private static void GrowBand(XElement item, XNamespace ns, double top, double height)
    {
        var band = item.Parent?.Parent;
        if (band?.Name.LocalName is not ("PageHeader" or "PageFooter"))
        {
            return;
        }

        var needed = top + height + 0.08;
        if (needed > Dimension.ToInches(band.Element(ns + "Height")?.Value))
        {
            Set(band, ns, "Height", Inches(Math.Min(4, needed)));
        }
    }

    private static void EnsureMark(RdlDocument document)
    {
        var ns = document.Ns;
        var images = document.Root.Element(ns + "EmbeddedImages");
        if (images is null)
        {
            images = new XElement(ns + "EmbeddedImages");
            document.Root.Add(images);
        }

        if (images.Elements().Any(image => image.Attribute("Name")?.Value == BlankReport.MarkName))
        {
            return;
        }

        images.Add(new XElement(
            ns + "EmbeddedImage",
            new XAttribute("Name", BlankReport.MarkName),
            new XElement(ns + "MIMEType", "image/png"),
            new XElement(ns + "ImageData", BlankReport.MarkPng)));
    }

    internal static XElement Paragraph(RdlDocument document, string value, bool bold)
    {
        var ns = document.Ns;
        var style = new XElement(ns + "Style", new XElement(ns + "FontFamily", "Arial"), new XElement(ns + "FontSize", "11pt"));
        if (bold)
        {
            style.Add(new XElement(ns + "FontWeight", "Bold"));
        }

        return new XElement(
            ns + "Paragraphs",
            new XElement(
                ns + "Paragraph",
                new XElement(ns + "TextRuns", new XElement(ns + "TextRun", new XElement(ns + "Value", value), style)),
                new XElement(ns + "Style", new XElement(ns + "TextAlign", "Left"))));
    }

    private static void ApplyTextStyle(XElement textbox, XNamespace ns, string font, string size, string weight, string align)
    {
        var runStyle = textbox.Descendants(ns + "TextRun").FirstOrDefault()?.Element(ns + "Style");
        if (runStyle is null)
        {
            return;
        }

        Set(runStyle, ns, "FontFamily", font);
        Set(runStyle, ns, "FontSize", size);
        Set(runStyle, ns, "FontWeight", weight);
        var paragraphStyle = textbox.Descendants(ns + "Paragraph").FirstOrDefault()?.Element(ns + "Style");
        if (paragraphStyle is not null)
        {
            Set(paragraphStyle, ns, "TextAlign", align);
        }
    }

    private static void ApplyBorder(XElement item, XNamespace ns, string border)
    {
        var style = item.Element(ns + "Style") ?? new XElement(ns + "Style");
        if (style.Parent is null)
        {
            item.Add(style);
        }

        var borderElement = style.Element(ns + "Border") ?? new XElement(ns + "Border");
        if (borderElement.Parent is null)
        {
            style.Add(borderElement);
        }

        Set(borderElement, ns, "Style", border);
    }

    private static XElement RequireSurfaceItem(RdlDocument document, string name)
    {
        var item = ReportItemEditor.RequireReportItem(document, name);
        if (item.Parent?.Name.LocalName != "ReportItems")
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        return item;
    }

    internal static XElement BodyItems(RdlDocument document)
    {
        var body = document.Descendants("Body").First();
        var items = body.Element(document.Ns + "ReportItems");
        if (items is not null)
        {
            return items;
        }

        items = new XElement(document.Ns + "ReportItems");
        body.AddFirst(items);
        return items;
    }

    internal static void FitBody(RdlDocument document)
    {
        var ns = document.Ns;
        var body = document.Descendants("Body").First();
        double bottom = 2;
        foreach (var item in BodyItems(document).Elements())
        {
            bottom = Math.Max(bottom, Dimension.ToInches(item.Element(ns + "Top")?.Value) + Dimension.ToInches(item.Element(ns + "Height")?.Value) + 0.25);
        }

        Set(body, ns, "Height", Inches(bottom));
    }

    /// <summary>The first free position below the items of a region, used when an item is added without a drop point.</summary>
    public static double FreeTop(RdlDocument document, string? region)
    {
        var ns = document.Ns;
        var page = document.Descendants("Page").FirstOrDefault();
        var parent = region switch
        {
            "Header" => page?.Element(ns + "PageHeader")?.Element(ns + "ReportItems"),
            "Footer" => page?.Element(ns + "PageFooter")?.Element(ns + "ReportItems"),
            _ => BodyItems(document),
        };
        var bottom = parent?.Elements()
            .Select(item =>
            {
                var top = Dimension.ToInches(item.Element(ns + "Top")?.Value);
                var height = item.Name.LocalName == "Tablix"
                    ? TablixNavigator.Rows(item, ns).Sum(row => Dimension.ToInches(row.Element(ns + "Height")?.Value))
                    : Dimension.ToInches(item.Element(ns + "Height")?.Value);
                return top + height;
            })
            .DefaultIfEmpty(0)
            .Max() ?? 0;
        return bottom <= 0 ? 0.25 : Math.Round((bottom + 0.15) / 0.125, MidpointRounding.AwayFromZero) * 0.125;
    }

    private static double Nudge(XElement parent, XNamespace ns, double top)
    {
        var cursor = top;
        var occupied = parent.Elements()
            .Select(item => Dimension.ToInches(item.Element(ns + "Top")?.Value))
            .ToArray();
        while (occupied.Any(existing => Math.Abs(existing - cursor) < 0.05) && cursor < 18)
        {
            cursor += 0.2;
        }

        return cursor;
    }

    internal static string NextName(RdlDocument document, string prefix, ISet<string>? reserved = null)
    {
        var used = document.Root.Descendants()
            .Select(element => element.Attribute("Name")?.Value)
            .Where(name => !string.IsNullOrEmpty(name))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (reserved is not null)
        {
            used.UnionWith(reserved);
        }

        for (var index = 1; index < 1000; index++)
        {
            var name = prefix + index.ToString(CultureInfo.InvariantCulture);
            if (used.Contains(name))
            {
                continue;
            }

            reserved?.Add(name);
            return name;
        }

        throw new ReportDesignException(ReportCodes.Limit);
    }

    private static string FontSize(string? value)
    {
        var digits = new string((value ?? "").TakeWhile(ch => char.IsDigit(ch) || ch == '.').ToArray());
        if (!double.TryParse(digits, NumberStyles.Number, CultureInfo.InvariantCulture, out var points))
        {
            return "11pt";
        }

        points = Math.Clamp(points, 6, 72);
        return points.ToString("0", CultureInfo.InvariantCulture) + "pt";
    }

    private static string Inches(double value) => Dimension.FromInches(value);

    private static void Set(XElement parent, XNamespace ns, string name, string value)
    {
        var child = parent.Element(ns + name);
        if (child is null)
        {
            child = new XElement(ns + name);
            parent.Add(child);
        }

        child.Value = value;
    }
}
