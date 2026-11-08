using System.ComponentModel;
using ModelContextProtocol.Server;
using ReportExpert.Reporting.Design;

namespace ReportExpert.Rdl.Mcp.Server.Tools;

/// <summary>
/// REX V3 designer tools. They drive the same design engine as the REX report center and designer, so an
/// agent can build a layout the way a person does: place toolbox items, set properties, align, group, preview
/// data and export. Business Central datasets, the code block and the data source are preserved.
/// </summary>
[McpServerToolType]
public static class DesignTools
{
    /// <summary>Reads the designer's view of a report.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="item">Item whose property sheet to include.</param>
    /// <returns>Items, datasets, parameters, bands, properties and checks.</returns>
    [McpServerTool(Name = "get_rdl_design_model", ReadOnly = true, Idempotent = true)]
    [Description(
        "Read a report the way the REX designer sees it: every item with its region (Header, Body, Footer), " +
        "kind, position and size in inches, the datasets and their provider, parameters, band heights, page size " +
        "and validation checks. Pass item to also get that item's property sheet.")]
    public static ToolResponse GetRdlDesignModel(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Optional item name whose property sheet to return, for example Textbox1. Use Report for the report itself.")]
        string? item = null) =>
        DesignSession.Read(filepath, async (service, model) =>
        {
            var selected = item is null ? model : await service.OpenAsync(model.Id, item, CancellationToken.None) ?? model;
            return new
            {
                selected.Name,
                selected.Revision,
                selected.PageWidthInches,
                selected.PageHeightInches,
                selected.BodyWidthInches,
                selected.BodyHeightInches,
                selected.HeaderHeightInches,
                selected.FooterHeightInches,
                selected.HasHeader,
                selected.HasFooter,
                selected.HasCustomCode,
                Items = selected.Items.Select(i => new { i.Name, i.Kind, i.Variant, i.Region, i.LeftInches, i.TopInches, i.WidthInches, i.HeightInches, i.Value, i.DataSetName }),
                selected.DataSets,
                Parameters = selected.Parameters.Select(p => new { p.Name, p.DataType, p.Prompt, p.DefaultValue, p.Hidden, p.MultiValue }),
                selected.Warnings,
                Properties = item is null ? null : selected.Properties,
            };
        });

    /// <summary>Returns the first rows of a dataset.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="dataset_name">Dataset to preview.</param>
    /// <returns>Columns and rows.</returns>
    [McpServerTool(Name = "get_rdl_data_preview", ReadOnly = true, Idempotent = true)]
    [Description(
        "Preview the first rows of a dataset as the designer would render it. Rows come from the Business Central " +
        "data file next to the layout (same name, .xml/.json/.csv), the file attached in REX, or sample rows " +
        "generated from the field types.")]
    public static ToolResponse GetRdlDataPreview(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Dataset name, for example DataSet_Result.")] string dataset_name) =>
        DesignSession.Read(filepath, (service, model) =>
            service.PreviewDataAsync(model.Id, dataset_name, CancellationToken.None));

    /// <summary>Places a toolbox item.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="kind">Toolbox item.</param>
    /// <param name="left">Left edge in inches.</param>
    /// <param name="top">Top edge in inches.</param>
    /// <param name="region">Header, Body or Footer.</param>
    /// <param name="dataset_name">Dataset for data regions and charts.</param>
    /// <param name="dry_run">Report the change without writing.</param>
    /// <returns>The edit outcome.</returns>
    [McpServerTool(Name = "place_rdl_report_item", Destructive = true)]
    [Description(
        "Drop a toolbox item on the layout: Textbox, Image, Line, Rectangle, Table, List, Matrix, a chart " +
        "(Chart:Column, Chart:Bar, Chart:StackedColumn, Chart:StackedBar, Chart:PercentColumn, Chart:PercentBar, " +
        "Chart:RangeColumn, Chart:RangeBar, Chart:Pie, Chart:ExplodedPie, Chart:Doughnut, Chart:Funnel, " +
        "Chart:Pyramid, Chart:Line, Chart:SmoothLine, Chart:SteppedLine, Chart:LineMarkers, Chart:Area, " +
        "Chart:SmoothArea, Chart:StackedArea, Chart:PercentArea, Chart:RangeArea, Chart:SmoothRangeArea, " +
        "Chart:Scatter, Chart:Bubble, Chart:Polar, Chart:Radar), a field textbox (Field:DataSet/FieldName), " +
        "or the Header/Footer band. Pass top = -1 to place it below the existing items.")]
    public static ToolResponse PlaceRdlReportItem(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Toolbox item, for example Textbox, Table or Chart:Pie.")] string kind,
        [Description("Left edge in inches from the region's left edge.")] double left = 0,
        [Description("Top edge in inches from the region's top, or -1 to place below existing items.")] double top = -1,
        [Description("Region to place in: Header, Body or Footer.")] string region = "Body",
        [Description("Dataset to bind a data region or chart to. Defaults to the first dataset.")] string? dataset_name = null,
        [Description("When true, return the change as a diff and write nothing.")] bool dry_run = false) =>
        DesignSession.Mutate(filepath, dry_run, (service, model) =>
            service.PlaceInAsync(model.Id, kind, left, top, region, dataset_name, CancellationToken.None),
            $"Placed {kind} in {region}");

    /// <summary>Moves or resizes an item.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="item">Item to move.</param>
    /// <param name="left">Left edge in inches.</param>
    /// <param name="top">Top edge in inches.</param>
    /// <param name="width">Width in inches.</param>
    /// <param name="height">Height in inches.</param>
    /// <param name="dry_run">Report the change without writing.</param>
    /// <returns>The edit outcome.</returns>
    [McpServerTool(Name = "move_rdl_report_item", Destructive = true)]
    [Description("Move and resize a report item. All values are in inches, relative to the item's region.")]
    public static ToolResponse MoveRdlReportItem(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Name of the item, as get_rdl_design_model lists it.")] string item,
        [Description("New left edge in inches.")] double left,
        [Description("New top edge in inches.")] double top,
        [Description("New width in inches.")] double width,
        [Description("New height in inches.")] double height,
        [Description("When true, return the change as a diff and write nothing.")] bool dry_run = false) =>
        DesignSession.Mutate(filepath, dry_run, (service, model) =>
            service.MoveAsync(model.Id, item, left, top, width, height, CancellationToken.None),
            $"Moved {item}");

    /// <summary>Sets one property of an item.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="item">Item, or Report.</param>
    /// <param name="key">Property key.</param>
    /// <param name="value">New value, or an expression.</param>
    /// <param name="dry_run">Report the change without writing.</param>
    /// <returns>The edit outcome.</returns>
    [McpServerTool(Name = "set_rdl_item_property", Destructive = true)]
    [Description(
        "Set one property from an item's property sheet, the same keys the REX properties panel shows " +
        "(get_rdl_design_model with item lists them): font, colour, border, padding, format, visibility, " +
        "value, chart titles and more. Values may be literals or expressions starting with =.")]
    public static ToolResponse SetRdlItemProperty(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Item name, or Report for report-level properties.")] string item,
        [Description("Property key from the item's property sheet.")] string key,
        [Description("New value; empty clears the property.")] string? value,
        [Description("When true, return the change as a diff and write nothing.")] bool dry_run = false) =>
        DesignSession.Mutate(filepath, dry_run, (service, model) =>
            service.SetPropertyAsync(model.Id, item, key, value, CancellationToken.None),
            $"Set {key} on {item}");

    /// <summary>Aligns, sizes or orders items.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="items">Items to arrange.</param>
    /// <param name="operation">Arrange operation.</param>
    /// <param name="dry_run">Report the change without writing.</param>
    /// <returns>The edit outcome.</returns>
    [McpServerTool(Name = "arrange_rdl_report_items", Destructive = true)]
    [Description(
        "Arrange items like the designer's Align and Layout menus. With one item: BringToFront, SendToBack, " +
        "BringForward, SendBackward, AlignLeft, AlignCenter, AlignRight, AlignTop, AlignMiddle, AlignBottom, " +
        "FitWidth, CenterHorizontally, CenterVertically, AlignToGrid, SizeToGrid. With several items (aligned to " +
        "the first): AlignLeft, AlignCenter, AlignRight, AlignTop, AlignMiddle, AlignBottom, SameSize, SameWidth, SameHeight.")]
    public static ToolResponse ArrangeRdlReportItems(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Item names; the first one is the reference for multi-item operations.")] string[] items,
        [Description("Arrange operation, for example AlignLeft or SameWidth.")] string operation,
        [Description("When true, return the change as a diff and write nothing.")] bool dry_run = false) =>
        DesignSession.Mutate(filepath, dry_run, (service, model) => items.Length == 1
                ? service.ArrangeAsync(model.Id, items[0], operation, CancellationToken.None)
                : service.ArrangeManyAsync(model.Id, items, operation, CancellationToken.None),
            $"{operation} on {string.Join(", ", items)}");

    /// <summary>Adds a row or column group to a data region.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="table">Data region name.</param>
    /// <param name="axis">Row or Column.</param>
    /// <param name="field">Field to group by.</param>
    /// <param name="dry_run">Report the change without writing.</param>
    /// <returns>The edit outcome.</returns>
    [McpServerTool(Name = "add_rdl_group", Destructive = true)]
    [Description("Add a row or column group to a table, list or matrix, grouped by a dataset field (the designer's Row Groups and Column Groups panel).")]
    public static ToolResponse AddRdlGroup(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Name of the table, list or matrix.")] string table,
        [Description("Row or Column.")] string axis,
        [Description("Dataset field to group by.")] string field,
        [Description("When true, return the change as a diff and write nothing.")] bool dry_run = false) =>
        DesignSession.Mutate(filepath, dry_run, (service, model) =>
            service.AddGroupAsync(model.Id, table, axis, field, CancellationToken.None),
            $"Added {axis} group on {field} to {table}");

    /// <summary>Adds a totals row to a table.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="table">Table name.</param>
    /// <param name="dry_run">Report the change without writing.</param>
    /// <returns>The edit outcome.</returns>
    [McpServerTool(Name = "add_rdl_total_row", Destructive = true)]
    [Description("Add a totals row at the bottom of a table, summing its numeric columns.")]
    public static ToolResponse AddRdlTotalRow(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("Name of the table.")] string table,
        [Description("When true, return the change as a diff and write nothing.")] bool dry_run = false) =>
        DesignSession.Mutate(filepath, dry_run, (service, model) =>
            service.AddTotalAsync(model.Id, table, CancellationToken.None),
            $"Added a totals row to {table}");

    /// <summary>Renders the report to a file.</summary>
    /// <param name="filepath">Path to the report definition.</param>
    /// <param name="output_path">Where to write the export.</param>
    /// <param name="format">Pdf, Excel or Word.</param>
    /// <param name="dry_run">Render without writing.</param>
    /// <returns>The written file and its size.</returns>
    [McpServerTool(Name = "export_rdl_report", Destructive = false)]
    [Description(
        "Render the report with its preview data (BC data file or sample rows) and parameter defaults to PDF, " +
        "Excel or Word, and write it to output_path. The layout file itself is not changed. PDF needs Windows.")]
    public static ToolResponse ExportRdlReport(
        [Description("Absolute or relative path to the .rdl or .rdlc file.")] string filepath,
        [Description("File to write, for example C:\\temp\\report.pdf.")] string output_path,
        [Description("Pdf, Excel or Word.")] string format = "Pdf",
        [Description("When true, render and report the size but write nothing.")] bool dry_run = false) =>
        DesignSession.Read(filepath, async (service, model) =>
        {
            var kind = Enum.TryParse<ReportOutputFormat>(format, ignoreCase: true, out var parsed) ? parsed : ReportOutputFormat.Pdf;
            var defaults = model.Parameters.ToDictionary(p => p.Name, p => p.DefaultValue, StringComparer.Ordinal);
            var output = await service.RenderAsync(model.Id, defaults, kind, CancellationToken.None);
            string target = Path.GetFullPath(output_path);
            if (!dry_run)
            {
                await File.WriteAllBytesAsync(target, output.Content);
            }

            return new { success = true, dry_run, output_path = target, bytes = output.Content.Length, content_type = output.ContentType };
        });
}
