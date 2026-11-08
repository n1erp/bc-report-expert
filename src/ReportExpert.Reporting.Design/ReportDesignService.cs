using System.Data;
using System.Globalization;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ReportExpert.Rdl.Core;
using ReportExpert.Rdl.Core.Editing;

namespace ReportExpert.Reporting.Design;

public sealed class ReportDesignService(
    ReportStore store,
    ReportAuditLog audit,
    ReportRenderer renderer,
    ReportDataSources sources,
    BusinessCentralDataStore bcData,
    IOptions<ReportingOptions> options,
    ILogger<ReportDesignService> logger) : IReportDesignService
{
    private const int PreviewRows = 100;

    public Task<IReadOnlyList<ReportListItem>> ListAsync(CancellationToken cancellationToken) =>
        store.ListAsync(cancellationToken);

    public Task<IReadOnlyList<ReportRevisionEntry>> RevisionsAsync(Guid id, CancellationToken cancellationToken) =>
        store.RevisionsAsync(id, cancellationToken);

    public async Task<ReportDesignModel> CreateAsync(string name, CancellationToken cancellationToken)
    {
        var title = ReportDefinitionGuard.RequireName(name);
        var document = BlankReport.Create();
        var stored = await store.CreateAsync(title, document.Serialize(), cancellationToken, ReportOrigins.Blank);
        await audit.WriteAsync("Created", stored.Id, title, cancellationToken);
        return await ReadAsync(stored, null, cancellationToken);
    }

    public async Task<ReportDesignModel?> OpenAsync(Guid id, string? selectedItem, CancellationToken cancellationToken)
    {
        var stored = await store.ReadAsync(id, cancellationToken);
        return stored is null ? null : await ReadAsync(stored, selectedItem, cancellationToken);
    }

    public async Task<ReportDesignModel> StepAsync(Guid id, int direction, CancellationToken cancellationToken)
    {
        var stored = await store.StepAsync(id, direction, await sources.KeysAsync(cancellationToken), cancellationToken);
        await audit.WriteAsync(direction < 0 ? "Previous" : "Forward", id, stored.Name, cancellationToken);
        return await ReadAsync(stored, null, cancellationToken);
    }

    public async Task<ReportDesignModel> SaveAsync(Guid id, CancellationToken cancellationToken)
    {
        var stored = await store.TouchAsync(id, cancellationToken);
        await audit.WriteAsync("Saved", id, stored.Name, cancellationToken);
        logger.LogInformation("Report {ReportId} saved at revision {Revision}", id, stored.Revision);
        return await ReadAsync(stored, null, cancellationToken);
    }

    public async Task<ReportDesignModel> RenameAsync(Guid id, string name, CancellationToken cancellationToken)
    {
        var title = ReportDefinitionGuard.RequireName(name);
        var stored = await RequireAsync(id, cancellationToken);
        var saved = await store.SaveAsync(id, title, stored.Definition, cancellationToken);
        await audit.WriteAsync("Renamed", id, title, cancellationToken);
        return await ReadAsync(saved, null, cancellationToken);
    }

    public async Task DeleteAsync(Guid id, CancellationToken cancellationToken)
    {
        await store.DeleteAsync(id, cancellationToken);
        await audit.WriteAsync("Deleted", id, null, cancellationToken);
    }

    public async Task<ReportDesignModel> ImportAsync(string name, string definition, CancellationToken cancellationToken)
    {
        var title = ReportDefinitionGuard.RequireName(name);
        RdlDocument document;
        try
        {
            document = RdlDocument.Parse(definition);
        }
        catch (Exception exception) when (exception is InvalidRdlException or ResourceLimitExceededException)
        {
            logger.LogWarning(exception, "Rejected an imported report definition");
            throw new ReportDesignException(exception is ResourceLimitExceededException ? ReportCodes.Limit : ReportCodes.InvalidDefinition, exception);
        }

        Prepare(document, await sources.KeysAsync(cancellationToken));
        var stored = await store.CreateAsync(title, document.Serialize(), cancellationToken, ReportOrigins.Import);
        await audit.WriteAsync("Imported", stored.Id, title, cancellationToken);
        return await ReadAsync(stored, null, cancellationToken);
    }

    public Task<ReportDesignModel> PlaceAsync(Guid id, string kind, double leftInches, double topInches, CancellationToken cancellationToken, string? region = null) =>
        PlaceInAsync(id, kind, leftInches, topInches, region, null, cancellationToken);

    public Task<ReportDesignModel> PlaceInAsync(Guid id, string kind, double leftInches, double topInches, string? region, string? dataSetName, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            var names = ReportDataSetEditor.Names(document);
            if (kind.StartsWith("Field:", StringComparison.Ordinal))
            {
                // A dataset field dropped on the surface: a text box showing the field's first value.
                var parts = kind[6..].Split('/', 2);
                var fieldName = parts.Length == 2 ? parts[1] : "";
                var fieldSet = parts[0];
                if (!names.Contains(fieldSet, StringComparer.Ordinal)
                    || !ReportDataSetEditor.Fields(document, fieldSet).Any(field => field.Name == fieldName))
                {
                    throw new ReportDesignException(ReportCodes.InvalidItem);
                }

                // Dropped where the user dropped it: header, body or footer.
                var textbox = ReportLayoutEditor.Place(document, "Textbox", leftInches, Math.Max(0, topInches), fieldSet, [], region ?? "Body");
                ReportPropertySheet.Set(document, textbox, "Value", "=First(Fields!" + fieldName + ".Value, \"" + fieldSet + "\")");
                return textbox;
            }

            if (kind.StartsWith("Expr:", StringComparison.Ordinal))
            {
                // A parameter or built-in field from the Report Data pane: a text box with that expression.
                var expression = kind[5..];
                if (!expression.StartsWith('=') || expression.Length > 4000)
                {
                    throw new ReportDesignException(ReportCodes.InvalidItem);
                }

                var top = topInches < 0 ? ReportLayoutEditor.FreeTop(document, region) : topInches;
                var textbox = ReportLayoutEditor.Place(document, "Textbox", leftInches, top, names.FirstOrDefault(), [], region ?? "Body");
                ReportPropertySheet.Set(document, textbox, "Value", expression);
                return textbox;
            }

            var dataSet = !string.IsNullOrEmpty(dataSetName) && names.Contains(dataSetName, StringComparer.Ordinal) ? dataSetName : names.FirstOrDefault();
            if (topInches < 0)
            {
                // Placed from the toolbox without a drop position: below everything already in the region.
                topInches = ReportLayoutEditor.FreeTop(document, region);
            }

            return ReportLayoutEditor.Place(document, kind, leftInches, topInches, dataSet, ReportDataSetEditor.Fields(document, dataSet), region);
        }, "Placed", cancellationToken);

    public Task<ReportDesignModel> ResizeBandAsync(Guid id, string region, double heightInches, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            ReportLayoutEditor.ResizeBand(document, region, heightInches);
            return null;
        }, "ResizedBand", cancellationToken);

    public Task<ReportDesignModel> AddColumnAsync(Guid id, string table, string field, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            ReportTableEditor.AddColumn(document, table, field);
            return table;
        }, "AddedColumn", cancellationToken);

    public Task<ReportDesignModel> EditColumnAsync(Guid id, string table, int index, string? header, string? value, double widthInches, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            ReportTableEditor.EditColumn(document, table, index, header, value, widthInches);
            return table;
        }, "EditedColumn", cancellationToken);

    public Task<ReportDesignModel> MoveColumnAsync(Guid id, string table, int index, int direction, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            ReportTableEditor.MoveColumn(document, table, index, direction);
            return table;
        }, "MovedColumn", cancellationToken);

    public Task<ReportDesignModel> RemoveColumnAsync(Guid id, string table, int index, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            ReportTableEditor.RemoveColumn(document, table, index);
            return table;
        }, "RemovedColumn", cancellationToken);

    public Task<ReportDesignModel> AddGroupAsync(Guid id, string table, string axis, string field, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            ReportTableEditor.AddGroup(document, table, axis, field);
            return table;
        }, "AddedGroup", cancellationToken);

    public Task<ReportDesignModel> RemoveGroupAsync(Guid id, string table, string groupName, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            ReportTableEditor.RemoveGroup(document, table, groupName);
            return table;
        }, "RemovedGroup", cancellationToken);

    public Task<ReportDesignModel> AddTotalAsync(Guid id, string table, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            ReportTableEditor.AddTotal(document, table);
            return table;
        }, "AddedTotal", cancellationToken);

    public Task<ReportDesignModel> RepeatHeaderAsync(Guid id, string table, bool repeat, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            ReportTableEditor.RepeatHeader(document, table, repeat);
            return table;
        }, "RepeatedHeader", cancellationToken);

    public Task<ReportDesignModel> MoveAsync(Guid id, string itemName, double leftInches, double topInches, double widthInches, double heightInches, CancellationToken cancellationToken) =>
        ChangeAsync(id, itemName, document =>
        {
            ReportLayoutEditor.Move(document, itemName, leftInches, topInches, widthInches, heightInches);
            return itemName;
        }, "Moved", cancellationToken);

    public Task<ReportDesignModel> EditAsync(Guid id, ReportItemEdit edit, CancellationToken cancellationToken) =>
        ChangeAsync(id, edit.Name, document =>
        {
            ReportLayoutEditor.Edit(document, edit);
            return edit.Name;
        }, "Edited", cancellationToken);

    public Task<ReportDesignModel> RemoveItemAsync(Guid id, string itemName, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            ReportLayoutEditor.Remove(document, itemName);
            return null;
        }, "RemovedItem", cancellationToken);

    /// <summary>Replaces every dataset with one over <paramref name="providerKey"/>, rebinding existing regions.</summary>
    public async Task<ReportDesignModel> UseDataSetAsync(Guid id, string providerKey, CancellationToken cancellationToken)
    {
        var provider = await sources.FindAsync(providerKey, cancellationToken) ?? throw new ReportDesignException(ReportCodes.UnknownProvider);
        return await ChangeAsync(id, null, document =>
        {
            var previous = ReportDataSetEditor.Names(document);
            foreach (var name in previous)
            {
                DataSetEditor.RemoveDataSet(document, name, force: true);
            }

            var added = ReportDataSetEditor.Add(document, provider, provider.DataSetName, []);
            foreach (var reference in document.Descendants("DataSetName"))
            {
                if (previous.Contains(reference.Value, StringComparer.Ordinal) || string.IsNullOrEmpty(reference.Value))
                {
                    reference.Value = added;
                }
            }

            return null;
        }, "BoundDataSet", cancellationToken);
    }

    public async Task<ReportDesignModel> AddDataSetAsync(Guid id, string providerKey, string? name, IReadOnlyList<string> fields, CancellationToken cancellationToken)
    {
        var provider = await sources.FindAsync(providerKey, cancellationToken) ?? throw new ReportDesignException(ReportCodes.UnknownProvider);
        return await ChangeAsync(id, null, document =>
        {
            ReportDataSetEditor.Add(document, provider, name, fields);
            return null;
        }, "AddedDataSet", cancellationToken);
    }

    public async Task<ReportDesignModel> SetDataSetFieldsAsync(Guid id, string dataSetName, IReadOnlyList<string> fields, CancellationToken cancellationToken)
    {
        var stored = await RequireAsync(id, cancellationToken);
        var key = ReportDataSetEditor.ProviderKey(Parse(stored), dataSetName) ?? throw new ReportDesignException(ReportCodes.UnknownProvider);
        var provider = await sources.FindAsync(key, cancellationToken) ?? throw new ReportDesignException(ReportCodes.UnknownProvider);
        return await ChangeAsync(id, null, document =>
        {
            ReportDataSetEditor.SetFields(document, dataSetName, provider, fields);
            return null;
        }, "EditedDataSet", cancellationToken);
    }

    public Task<ReportDesignModel> RemoveDataSetAsync(Guid id, string dataSetName, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            ReportDataSetEditor.Remove(document, dataSetName);
            return null;
        }, "RemovedDataSet", cancellationToken);

    public async Task<DesignDataPreview> PreviewDataAsync(Guid id, string dataSetName, CancellationToken cancellationToken)
    {
        var document = Parse(await RequireAsync(id, cancellationToken));
        var key = ReportDataSetEditor.ProviderKey(document, dataSetName) ?? throw new ReportDesignException(ReportCodes.UnknownProvider);
        var provider = await sources.FindAsync(key, cancellationToken) ?? throw new ReportDesignException(ReportCodes.UnknownProvider);
        var declared = ReportDataSetEditor.Fields(document, dataSetName);
        var fields = declared.Select(field => field.Name).ToArray();
        DataTable table;
        try
        {
            var request = new ReportDataSetRequest(id, dataSetName, declared);
            table = await ReportDataSources.ReadAsync(provider, fields, new Dictionary<string, string?>(), PreviewRows + 1, cancellationToken, request);
        }
        catch (Exception exception) when (exception is not OperationCanceledException and not ReportDesignException)
        {
            logger.LogWarning(exception, "Preview of dataset {DataSet} in report {ReportId} failed", dataSetName, id);
            throw new ReportDesignException(ReportCodes.DataUnavailable, exception);
        }

        using (table)
        {
            var columns = table.Columns.Cast<DataColumn>().Select(column => column.ColumnName).ToArray();
            var rows = table.Rows.Cast<DataRow>()
                .Take(PreviewRows)
                .Select(row => (IReadOnlyList<string>)row.ItemArray.Select(Display).ToArray())
                .ToArray();
            return new DesignDataPreview(dataSetName, columns, rows, table.Rows.Count > PreviewRows);
        }
    }

    public Task<ReportDesignModel> SetPropertiesAsync(Guid id, IReadOnlyList<ReportPropertyChange> changes, string? selected, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(changes);
        if (changes.Count == 0 || changes.Count > 500)
        {
            throw new ReportDesignException(ReportCodes.InvalidItem);
        }

        return ChangeAsync(id, selected, document =>
        {
            foreach (var change in changes)
            {
                ReportPropertySheet.Set(document, change.Target, change.Key, change.Value);
            }

            return selected;
        }, "SetProperties", cancellationToken);
    }

    public Task<ReportDesignModel> InsertGroupAsync(Guid id, TablixGroupInsert insert, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(insert);
        return ChangeAsync(id, insert.Table, document =>
        {
            TablixStructure.Open(document, insert.Table).InsertGroup(insert);
            return insert.Table;
        }, "InsertedGroup", cancellationToken);
    }

    public Task<ReportDesignModel> DeleteGroupAsync(Guid id, string table, string group, TablixDeleteMode mode, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            TablixStructure.Open(document, table).DeleteGroup(group, mode);
            return table;
        }, "DeletedGroup", cancellationToken);

    public Task<ReportDesignModel> AddGroupTotalAsync(Guid id, string table, string axis, string? group, bool after, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            var tablix = TablixNavigator.RequireTablix(document, table);
            var fields = ReportDataSetEditor.Fields(document, tablix.Element(document.Ns + "DataSetName")?.Value);
            TablixStructure.Open(document, table).AddTotal(axis, group, after, field => IsNumeric(fields, field));
            return table;
        }, "AddedGroupTotal", cancellationToken);

    public async Task<DesignGroupDetail> ReadGroupAsync(Guid id, string table, string group, CancellationToken cancellationToken)
    {
        var stored = await RequireAsync(id, cancellationToken);
        return TablixStructure.Open(Parse(stored), table).ReadGroup(group);
    }

    public Task<ReportDesignModel> EditGroupAsync(Guid id, TablixGroupEdit edit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return ChangeAsync(id, edit.Table, document =>
        {
            TablixStructure.Open(document, edit.Table).EditGroup(edit);
            return edit.Table;
        }, "EditedGroup", cancellationToken);
    }

    public Task<ReportDesignModel> EditTablixMemberAsync(Guid id, TablixMemberEdit edit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(edit);
        return ChangeAsync(id, edit.Table, document =>
        {
            TablixStructure.Open(document, edit.Table).EditMember(edit);
            return edit.Table;
        }, "EditedTablixMember", cancellationToken);
    }

    public Task<ReportDesignModel> InsertTablixSlotAsync(Guid id, string table, string axis, int index, TablixInsertPlacement placement, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            TablixStructure.Open(document, table).InsertSlotNear(axis, index, placement);
            return table;
        }, TablixStructure.IsRowAxis(axis) ? "InsertedRow" : "InsertedColumn", cancellationToken);

    public Task<ReportDesignModel> DeleteTablixSlotAsync(Guid id, string table, string axis, int index, TablixDeleteMode mode, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            TablixStructure.Open(document, table).DeleteSlot(axis, index, mode);
            return table;
        }, TablixStructure.IsRowAxis(axis) ? "DeletedRow" : "DeletedColumn", cancellationToken);

    public Task<ReportDesignModel> MergeTablixCellsAsync(Guid id, string table, int row, int firstColumn, int lastColumn, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            TablixStructure.Open(document, table).MergeCells(row, firstColumn, lastColumn);
            return table;
        }, "MergedCells", cancellationToken);

    public Task<ReportDesignModel> SplitTablixCellAsync(Guid id, string table, int row, int column, CancellationToken cancellationToken) =>
        ChangeAsync(id, table, document =>
        {
            TablixStructure.Open(document, table).SplitCell(row, column);
            return table;
        }, "SplitCell", cancellationToken);

    private static bool IsNumeric(IReadOnlyList<ReportField> fields, string field)
    {
        var type = fields.FirstOrDefault(candidate => string.Equals(candidate.Name, field, StringComparison.OrdinalIgnoreCase))?.DataType ?? "";
        return type is "System.Decimal" or "System.Double" or "System.Single" or "System.Int16" or "System.Int32" or "System.Int64"
            or "System.Byte" or "System.UInt16" or "System.UInt32" or "System.UInt64" or "Decimal" or "Integer" or "Float";
    }

    public Task<ReportDesignModel> FitBodyToPageAsync(Guid id, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            var page = ReportPageSetup.Read(document);
            var body = document.Descendants("Body").FirstOrDefault()
                ?? throw new ReportDesignException(ReportCodes.InvalidDefinition);

            // The body cannot be narrower than its widest item; Report Builder refuses that too.
            var ns = document.Ns;
            var rightmost = body.Element(ns + "ReportItems")?.Elements()
                .Select(item => Dimension.ToInches(item.Element(ns + "Left")?.Value) + Dimension.ToInches(item.Element(ns + "Width")?.Value))
                .DefaultIfEmpty(0)
                .Max() ?? 0;
            var width = Math.Max(Math.Max(page.MaxBodyWidthInches, rightmost), 0.5);
            ReportPropertySheet.Set(document, ReportPropertySheet.ReportTarget, "ReportWidth", ReportPageSetup.Format(width, page.IsMetric));
            return null;
        }, "FitBodyToPage", cancellationToken);

    public Task<ReportDesignModel> SetPropertyAsync(Guid id, string target, string key, string? value, CancellationToken cancellationToken) =>
        ChangeAsync(id, target, document =>
        {
            ReportPropertySheet.Set(document, target, key, value);
            return target;
        }, "SetProperty", cancellationToken);

    public async Task<ReportDesignModel> CopyAsync(Guid id, string itemName, bool cut, CancellationToken cancellationToken)
    {
        var stored = await RequireAsync(id, cancellationToken);
        await store.WriteClipboardAsync(ReportItemOperations.Copy(Parse(stored), itemName), cancellationToken);
        if (!cut)
        {
            return await ReadAsync(stored, itemName, cancellationToken);
        }

        return await RemoveItemAsync(id, itemName, cancellationToken);
    }

    public async Task<ReportDesignModel> PasteAsync(Guid id, string? region, CancellationToken cancellationToken)
    {
        var xml = await store.ReadClipboardAsync(cancellationToken) ?? throw new ReportDesignException(ReportCodes.ClipboardEmpty);
        return await ChangeAsync(id, null, document => ReportItemOperations.Paste(document, xml, region), "Pasted", cancellationToken);
    }

    public Task<ReportDesignModel> ArrangeAsync(Guid id, string itemName, string operation, CancellationToken cancellationToken) =>
        ChangeAsync(id, itemName, document =>
        {
            if (!ReportItemOperations.Operations.Contains(operation, StringComparer.Ordinal))
            {
                throw new ReportDesignException(ReportCodes.InvalidItem);
            }

            ReportItemOperations.Arrange(document, itemName, operation);
            return itemName;
        }, "Arranged", cancellationToken);

    public Task<ReportDesignModel> ArrangeManyAsync(Guid id, IReadOnlyList<string> itemNames, string operation, CancellationToken cancellationToken) =>
        ChangeAsync(id, itemNames.Count == 0 ? null : itemNames[0], document =>
        {
            if (itemNames.Count == 0 || itemNames.Count > 50)
            {
                throw new ReportDesignException(ReportCodes.InvalidItem);
            }

            ReportItemOperations.ArrangeMany(document, itemNames, operation);
            return itemNames[0];
        }, "Arranged", cancellationToken);

    public Task<ReportDesignModel> AddImageAsync(Guid id, string fileName, byte[] content, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            ReportImageEditor.Add(document, fileName, content);
            return null;
        }, "AddedImage", cancellationToken);

    public Task<ReportDesignModel> RemoveImageAsync(Guid id, string imageName, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            ReportImageEditor.Remove(document, imageName);
            return null;
        }, "RemovedImage", cancellationToken);

    public Task<ReportDesignModel> ToggleBandAsync(Guid id, string region, bool show, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            ReportItemOperations.ToggleBand(document, region, show);
            return null;
        }, show ? "AddedBand" : "RemovedBand", cancellationToken);

    public Task<ReportDesignModel> AddParameterAsync(Guid id, string name, string dataType, string prompt, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            if (!ReportDefinitionGuard.ValidIdentifier(name))
            {
                throw new ReportDesignException(ReportCodes.InvalidName);
            }

            var type = dataType is "Integer" or "Float" or "DateTime" or "Boolean" or "String" ? dataType : "String";
            var label = string.IsNullOrWhiteSpace(prompt) ? name : prompt.Trim();
            if (label.Length > 80)
            {
                label = label[..80];
            }

            ParameterEditor.AddParameter(document, name, type, label);
            if (type == "String")
            {
                // A new text parameter may be left empty, so a filter that ignores blanks can return every row.
                ReportParameterEditor.Edit(document, new ReportParameterEdit(name, type, label, null, null, AllowBlank: true, Nullable: false, Hidden: false));
            }

            return null;
        }, "AddedParameter", cancellationToken);

    public Task<ReportDesignModel> RemoveParameterAsync(Guid id, string name, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            // Dataset filters and expressions both refer to a parameter as Parameters!Name.Value.
            var token = "Parameters!" + name + ".";
            var referenced = document.Root.Descendants()
                .Where(element => element.Name.LocalName != "ReportParameter" && !element.Ancestors().Any(ancestor => ancestor.Name.LocalName == "ReportParameter"))
                .SelectMany(element => element.Nodes().OfType<XText>())
                .Any(text => text.Value.Contains(token, StringComparison.OrdinalIgnoreCase));
            if (referenced)
            {
                throw new ReportDesignException(ReportCodes.ParameterInUse);
            }

            try
            {
                ParameterEditor.RemoveParameter(document, name);
            }
            catch (RdlRefusedException exception)
            {
                throw new ReportDesignException(ReportCodes.ParameterInUse, exception);
            }

            return null;
        }, "RemovedParameter", cancellationToken);

    public Task<ReportDesignModel> EditParameterAsync(Guid id, ReportParameterEdit edit, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            if (!ReportDefinitionGuard.ValidIdentifier(edit.Name))
            {
                throw new ReportDesignException(ReportCodes.InvalidName);
            }

            ReportParameterEditor.Edit(document, edit);
            return null;
        }, "EditedParameter", cancellationToken);

    public Task<ReportDesignModel> AddFilterAsync(Guid id, string field, string operation, string value, bool skipWhenBlank, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            ReportFilterEditor.Add(document, field, operation, value, skipWhenBlank);
            return null;
        }, "AddedFilter", cancellationToken);

    public Task<ReportDesignModel> RemoveFilterAsync(Guid id, int index, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            ReportFilterEditor.Remove(document, index);
            return null;
        }, "RemovedFilter", cancellationToken);

    public Task<ReportDesignModel> RemoveDataSetFilterAsync(Guid id, string dataSetName, int index, CancellationToken cancellationToken) =>
        ChangeAsync(id, null, document =>
        {
            ReportFilterEditor.Remove(document, index, dataSetName);
            return null;
        }, "RemovedFilter", cancellationToken);

    public Task<ReportDesignModel> EditChartAsync(Guid id, ReportChartEdit edit, CancellationToken cancellationToken) =>
        ChangeAsync(id, edit.Name, document =>
        {
            var chart = document.Root.Descendants(document.Ns + "Chart")
                .FirstOrDefault(candidate => string.Equals(candidate.Attribute("Name")?.Value, edit.Name, StringComparison.Ordinal));
            var dataSet = chart?.Element(document.Ns + "DataSetName")?.Value ?? ReportDataSetEditor.Names(document).FirstOrDefault();
            ReportChartEditor.Edit(document, edit, dataSet, ReportDataSetEditor.Fields(document, dataSet));
            return edit.Name;
        }, "EditedChart", cancellationToken);

    public async Task<ReportOutput> RenderAsync(Guid id, IReadOnlyDictionary<string, string?> parameters, ReportOutputFormat format, CancellationToken cancellationToken)
    {
        var stored = await RequireAsync(id, cancellationToken);
        var output = await renderer.RenderAsync(stored, parameters, format, cancellationToken);
        await audit.WriteAsync("Rendered", id, format.ToString(), cancellationToken);
        return output;
    }

    /// <summary>Values offered by parameters whose available values come from a dataset.</summary>
    public async Task<IReadOnlyDictionary<string, IReadOnlyList<ParameterOption>>> ParameterOptionsAsync(Guid id, CancellationToken cancellationToken)
    {
        var document = Parse(await RequireAsync(id, cancellationToken));
        var result = new Dictionary<string, IReadOnlyList<ParameterOption>>(StringComparer.Ordinal);
        foreach (var parameter in ReportParameterEditor.ReadAll(document))
        {
            if (parameter.OptionsDataSet is not { } dataSet || parameter.OptionsValueField is not { } valueField)
            {
                continue;
            }

            try
            {
                var key = ReportDataSetEditor.ProviderKey(document, dataSet);
                var provider = key is null ? null : await sources.FindAsync(key, cancellationToken);
                if (provider is null)
                {
                    continue;
                }

                var labelField = parameter.OptionsLabelField ?? valueField;
                using var table = await ReportDataSources.ReadAsync(provider, [valueField, labelField], new Dictionary<string, string?>(), 1000, cancellationToken);
                result[parameter.Name] = table.Rows.Cast<DataRow>()
                    .Select(row => new ParameterOption(Invariant(row[valueField]), Display(row[labelField])))
                    .Where(option => option.Value.Length > 0)
                    .DistinctBy(option => option.Value, StringComparer.Ordinal)
                    .Take(500)
                    .ToArray();
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Values for parameter {Parameter} in report {ReportId} could not be read", parameter.Name, id);
            }
        }

        return result;
    }

    public async Task<ReportTemplateInstall> InstallTemplatesAsync(CancellationToken cancellationToken)
    {
        var existing = (await store.ListAsync(cancellationToken)).Select(item => item.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keys = await sources.KeysAsync(cancellationToken);
        var installed = new List<string>();
        var present = new List<string>();
        var unavailable = new List<string>();
        foreach (var template in ReportTemplates.All)
        {
            if (existing.Contains(template.Name))
            {
                present.Add(template.Name);
                continue;
            }

            var document = RdlDocument.Parse(ReportTemplates.Read(template));
            try
            {
                Prepare(document, keys);
            }
            catch (ReportDesignException exception) when (exception.Message == ReportCodes.UnknownProvider)
            {
                unavailable.Add(template.Name);
                continue;
            }

            var stored = await store.CreateAsync(template.Name, document.Serialize(), cancellationToken, ReportOrigins.Template);
            await audit.WriteAsync("Installed", stored.Id, template.Name, cancellationToken);
            installed.Add(template.Name);
        }

        return new ReportTemplateInstall(installed, present, unavailable);
    }

    public async Task<ReportOutput> DefinitionAsync(Guid id, CancellationToken cancellationToken)
    {
        var stored = await RequireAsync(id, cancellationToken);
        var safe = new string(stored.Name.Where(ch => char.IsAsciiLetterOrDigit(ch) || ch is '-' or '_').Take(60).ToArray());
        await audit.WriteAsync("Downloaded", id, stored.Name, cancellationToken);

        // The downloaded file must open in other RDLC tools, so it gets the same repairs as the render copy.
        var document = Parse(stored);
        ReportChartEditor.NormalizeForRender(document);
        ReportDefinitionGuard.RemoveEmptyCollections(document);
        return new ReportOutput(Encoding.UTF8.GetBytes(document.Serialize()), "application/xml", (safe.Length == 0 ? "report" : safe) + ".rdlc");
    }

    public async Task<ReportDesignModel> OpenFileAsync(string path, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        var full = Path.GetFullPath(path);
        if (!File.Exists(full))
        {
            throw new ReportDesignException(ReportCodes.NotFound);
        }

        var length = new FileInfo(full).Length;
        if (length > options.Value.MaxDefinitionBytes)
        {
            throw new ReportDesignException(ReportCodes.Limit);
        }

        var definition = await File.ReadAllTextAsync(full, cancellationToken);
        var linked = await store.FindBySourceAsync(full, cancellationToken);
        if (linked is { } id && await store.ReadAsync(id, cancellationToken) is { } existing)
        {
            if (!SameDefinition(existing.Definition, definition))
            {
                var document = ParseDefinition(definition);
                Prepare(document, await sources.KeysAsync(cancellationToken));
                existing = await store.SaveAsync(id, existing.Name, document.Serialize(), cancellationToken);
                await audit.WriteAsync("Reloaded", id, full, cancellationToken);
            }

            return await ReadAsync(existing, null, cancellationToken);
        }

        var model = await ImportAsync(ReportNameFor(full), definition, cancellationToken);
        await store.LinkAsync(model.Id, full, cancellationToken);
        return await OpenAsync(model.Id, null, cancellationToken) ?? model;
    }

    public async Task<string> SaveToFileAsync(Guid id, string? path, CancellationToken cancellationToken)
    {
        var stored = await RequireAsync(id, cancellationToken);
        var target = path ?? await store.SourcePathAsync(id, cancellationToken)
            ?? throw new ReportDesignException(ReportCodes.NotFound);
        target = Path.GetFullPath(target);

        // Same repairs as the download, so the file opens in Visual Studio, Report Builder and Business Central.
        var document = Parse(stored);
        ReportChartEditor.NormalizeForRender(document);
        ReportDefinitionGuard.RemoveEmptyCollections(document);
        var xml = document.Serialize();

        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        var temp = target + ".rex-tmp";
        await File.WriteAllTextAsync(temp, xml, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), cancellationToken);
        if (File.Exists(target))
        {
            File.Copy(target, target + ".bak", overwrite: true);
        }

        File.Move(temp, target, overwrite: true);
        if (path is not null)
        {
            await store.LinkAsync(id, target, cancellationToken);
        }

        await store.TouchAsync(id, cancellationToken);
        await audit.WriteAsync("SavedToFile", id, target, cancellationToken);
        logger.LogInformation("Report {ReportId} written to {Path}", id, target);
        return target;
    }

    public async Task<ReportDesignModel> AttachDataAsync(Guid id, string? dataPath, CancellationToken cancellationToken)
    {
        var stored = await RequireAsync(id, cancellationToken);
        if (dataPath is not null && !File.Exists(dataPath))
        {
            throw new ReportDesignException(ReportCodes.NotFound);
        }

        await bcData.AttachAsync(id, dataPath, cancellationToken);
        await audit.WriteAsync(dataPath is null ? "DetachedData" : "AttachedData", id, dataPath, cancellationToken);
        return await ReadAsync(stored, null, cancellationToken);
    }

    private static bool SameDefinition(string left, string right) =>
        string.Equals(Canonical(left), Canonical(right), StringComparison.Ordinal);

    private static string Canonical(string xml)
    {
        try
        {
            return RdlDocument.Parse(xml).Serialize();
        }
        catch (InvalidRdlException)
        {
            return xml;
        }
    }

    private static RdlDocument ParseDefinition(string definition)
    {
        try
        {
            return RdlDocument.Parse(definition);
        }
        catch (InvalidRdlException exception)
        {
            throw new ReportDesignException(ReportCodes.InvalidDefinition, exception);
        }
    }

    private static string ReportNameFor(string path)
    {
        var name = Path.GetFileNameWithoutExtension(path);
        name = new string(name.Where(ch => !char.IsControl(ch) && ch is not '/' and not '\\' and not ':').ToArray()).Trim();
        if (name.Length == 0)
        {
            name = "Report";
        }

        return name.Length > 80 ? name[..80] : name;
    }

    private async Task<ReportDesignModel> ChangeAsync(Guid id, string? selected, Func<RdlDocument, string?> change, string action, CancellationToken cancellationToken)
    {
        var stored = await RequireAsync(id, cancellationToken);
        var document = Parse(stored);
        var keys = await sources.KeysAsync(cancellationToken);
        string? next;
        try
        {
            next = change(document);
        }
        catch (ReportDesignException)
        {
            throw;
        }
        catch (Exception exception) when (exception is InvalidRdlException or RdlNotFoundException or RdlRefusedException or ResourceLimitExceededException or ArgumentException or InvalidOperationException)
        {
            logger.LogWarning(exception, "Report {ReportId} {Action} was rejected", id, action);
            throw new ReportDesignException(exception is ResourceLimitExceededException ? ReportCodes.Limit : ReportCodes.InvalidItem, exception);
        }

        Prepare(document, keys);
        var saved = await store.SaveAsync(id, stored.Name, document.Serialize(), cancellationToken);
        await audit.WriteAsync(action, id, next, cancellationToken);
        return await ReadAsync(saved, next ?? selected, cancellationToken);
    }

    private void Prepare(RdlDocument document, IReadOnlySet<string> keys)
    {
        // A Business Central layout keeps its own data source; rewriting it would break the AL report binding.
        if (options.Value.Policy == ReportSafetyPolicy.Strict)
        {
            ReportDefinitionGuard.NormalizeDataSources(document);
        }

        ReportDefinitionGuard.EnsureSafe(document, keys, options.Value);
    }

    private static RdlDocument Parse(StoredReport stored)
    {
        try
        {
            return RdlDocument.Parse(stored.Definition);
        }
        catch (InvalidRdlException exception)
        {
            throw new ReportDesignException(ReportCodes.InvalidDefinition, exception);
        }
    }

    private static string Display(object? value) => value switch
    {
        null or DBNull => "",
        DateTime date => date.TimeOfDay == TimeSpan.Zero
            ? date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture)
            : date.ToString("dd/MM/yyyy HH:mm", CultureInfo.InvariantCulture),
        decimal number => number.ToString("0.####", CultureInfo.InvariantCulture),
        double number => number.ToString("0.####", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private static string Invariant(object? value) => value switch
    {
        null or DBNull => "",
        DateTime date => date.ToString(date.TimeOfDay == TimeSpan.Zero ? "yyyy-MM-dd" : "yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
        bool flag => flag ? "true" : "false",
        IFormattable formattable => formattable.ToString(null, CultureInfo.InvariantCulture),
        _ => value.ToString() ?? "",
    };

    private async Task<StoredReport> RequireAsync(Guid id, CancellationToken cancellationToken) =>
        await store.ReadAsync(id, cancellationToken) ?? throw new ReportDesignException(ReportCodes.NotFound);

    private async Task<ReportDesignModel> ReadAsync(StoredReport stored, string? selected, CancellationToken cancellationToken)
    {
        var history = store.History(stored.Id, stored.Revision);
        var providers = await sources.ListAsync(cancellationToken);
        return ReportDocumentReader.Read(stored, selected, providers) with
        {
            CanUndo = history.CanUndo,
            CanRedo = history.CanRedo,
            HasClipboard = store.HasClipboard(),
            SourcePath = await store.SourcePathAsync(stored.Id, cancellationToken),
            DataFile = await bcData.ResolveAsync(stored.Id, cancellationToken),
            HasCustomCode = stored.Definition.Contains("<Code>", StringComparison.Ordinal),
        };
    }
}
