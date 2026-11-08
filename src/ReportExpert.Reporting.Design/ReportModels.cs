namespace ReportExpert.Reporting.Design;

public enum ReportOutputFormat
{
    Pdf,
    Excel,
    Word,
}

public static class ReportOrigins
{
    public const string Blank = "blank";

    public const string Import = "import";

    /// <summary>Installed from <see cref="ReportTemplates"/>.</summary>
    public const string Template = "template";
}

public sealed record ReportListItem(
    Guid Id,
    string Name,
    int Revision,
    DateTimeOffset UpdatedAt,
    string? Category,
    string UpdatedBy,
    string? Origin)
{
    /// <summary>The RDLC file on disk this report is linked to; null for a workspace-only report.</summary>
    public string? SourcePath { get; init; }

    public bool IsCustom => string.Equals(Origin, ReportOrigins.Template, StringComparison.OrdinalIgnoreCase)
        ? Revision > 1
        : string.Equals(Origin, ReportOrigins.Import, StringComparison.OrdinalIgnoreCase)
            || Revision > 1
            || !string.IsNullOrWhiteSpace(Category);
}

public sealed record ReportRevisionEntry(int Number, DateTimeOffset SavedAt, bool Current);

public sealed record ReportField(string Name, string DataType);

public sealed record ReportItemEdit(
    string Name,
    string? Value,
    string? FontFamily,
    string? FontSize,
    bool Bold,
    string? TextAlign,
    string? BorderStyle,
    bool Hidden,
    double LeftInches,
    double TopInches,
    double WidthInches,
    double HeightInches);

public sealed record DesignField(string Name, string DataType);

public sealed record DesignColumn(int Index, string Header, string Value, double WidthInches);

public sealed record DesignRow(int Index, string Role, double HeightInches, IReadOnlyList<string> Cells, IReadOnlyList<string>? CellNames = null);

/// <summary>One property row in the properties panel. <see cref="Editor"/> picks the input: text, textarea, select, color, length, points, integer, check or format.</summary>
public sealed record DesignProperty(string Key, string Editor, string Value, IReadOnlyList<string>? Options = null, bool AllowExpression = true)
{
    public bool IsExpression => Value.TrimStart().StartsWith('=');
}

/// <summary>A collapsible group of properties, for example Font or Position.</summary>
public sealed record DesignPropertySection(string Key, IReadOnlyList<DesignProperty> Properties);

/// <summary>An embedded image. <see cref="DataUri"/> is a <c>data:</c> URI for the image manager thumbnail.</summary>
public sealed record DesignImage(string Name, string MimeType, string DataUri, int Bytes, bool InUse);

/// <summary>A node of the report explorer tree.</summary>
public sealed record DesignExplorerNode(string Name, string Kind, IReadOnlyList<DesignExplorerNode> Children);

/// <summary>The first rows of a dataset, shown in the data panel.</summary>
public sealed record DesignDataPreview(string DataSetName, IReadOnlyList<string> Columns, IReadOnlyList<IReadOnlyList<string>> Rows, bool Truncated);

public sealed record DesignGroup(string Name, string Expression, bool Details);

public sealed record DesignItem(
    string Name,
    string Kind,
    string Region,
    double LeftInches,
    double TopInches,
    double WidthInches,
    double HeightInches,
    string Value,
    string FontFamily,
    string FontSize,
    bool Bold,
    string TextAlign,
    string BorderStyle,
    bool Hidden,
    IReadOnlyList<DesignColumn>? Columns = null,
    IReadOnlyList<DesignRow>? Rows = null,
    IReadOnlyList<DesignGroup>? RowGroups = null,
    IReadOnlyList<DesignGroup>? ColumnGroups = null,
    bool RepeatHeader = false,
    DesignChart? Chart = null,
    string? DataSetName = null,
    string Variant = "")
{
    /// <summary>The drawing grid of a tablix (corner, row/column header cells, body cells with spans).</summary>
    public DesignTablixGrid? Grid { get; init; }

    /// <summary>The row hierarchy of a tablix, as the grouping pane shows it.</summary>
    public IReadOnlyList<DesignMember>? RowHierarchy { get; init; }

    /// <summary>The column hierarchy of a tablix, as the grouping pane shows it.</summary>
    public IReadOnlyList<DesignMember>? ColumnHierarchy { get; init; }

    /// <summary>
    /// Where the item's container starts in its band (inches). <see cref="LeftInches"/> and
    /// <see cref="TopInches"/> are relative to the container, as RDL stores them; items directly in the body,
    /// header or footer have a zero offset. Items inside a rectangle or a tablix cell add the container's
    /// position, so the designer draws each item where it renders.
    /// </summary>
    public double OffsetLeftInches { get; init; }

    /// <inheritdoc cref="OffsetLeftInches"/>
    public double OffsetTopInches { get; init; }

    /// <summary>The rectangle or tablix that holds the item, or null for an item placed directly in a band.</summary>
    public string? Container { get; init; }

    /// <summary>The formatting the designer draws (font, colours, alignment, border).</summary>
    public DesignTextStyle Style { get; init; } = DesignTextStyle.Empty;

    /// <summary>The item's left edge in its band.</summary>
    public double AbsoluteLeftInches => OffsetLeftInches + LeftInches;

    /// <summary>The item's top edge in its band.</summary>
    public double AbsoluteTopInches => OffsetTopInches + TopInches;
}

/// <summary>
/// The bindings of a chart the designer can edit. <see cref="Editable"/> is false for an imported
/// chart with several series or category groups; such a chart can be moved but not rebound.
/// </summary>
public sealed record DesignChart(
    string ChartType,
    string Title,
    string Category,
    string Series,
    string ValueField,
    string Aggregate,
    bool ShowLegend,
    bool ShowLabels,
    bool Editable,
    string SecondField = "");

public sealed record ReportChartEdit(
    string Name,
    string? ChartType,
    string? Title,
    string? Category,
    string? Series,
    string? ValueField,
    string? Aggregate,
    bool ShowLegend,
    bool ShowLabels,
    string? Palette = null,
    string? SecondField = null);

public sealed record DesignFilter(int Index, string Expression, string Operator, string Value);

public sealed record DesignDataSet(string Name, string ProviderKey, IReadOnlyList<DesignField> Fields, IReadOnlyList<DesignFilter>? Filters = null);

public sealed record ParameterOption(string Value, string Label);

/// <summary>Where a parameter's available values or default values come from.</summary>
public static class ParameterValueSource
{
    public const string None = "None";

    public const string Specify = "Specify";

    public const string Query = "Query";
}

public sealed record DesignParameter(
    string Name,
    string DataType,
    string Prompt,
    string? DefaultValue = null,
    bool AllowBlank = false,
    bool Nullable = false,
    bool Hidden = false,
    IReadOnlyList<ParameterOption>? Options = null,
    bool MultiValue = false,
    string? OptionsDataSet = null,
    string? OptionsValueField = null,
    string? OptionsLabelField = null,
    string? DefaultDataSet = null,
    string? DefaultValueField = null,
    IReadOnlyList<string>? DefaultValues = null)
{
    /// <summary>True when a value may be left empty at preview.</summary>
    public bool Optional => DefaultValue is not null || DefaultDataSet is not null || Nullable || (AllowBlank && DataType == "String");

    public string AvailableSource => OptionsDataSet is not null ? ParameterValueSource.Query : (Options?.Count ?? 0) > 0 ? ParameterValueSource.Specify : ParameterValueSource.None;

    public string DefaultSource => DefaultDataSet is not null ? ParameterValueSource.Query : DefaultValue is not null ? ParameterValueSource.Specify : ParameterValueSource.None;
}

/// <summary>
/// A parameter edit from the designer. <see cref="Options"/> holds one available value per line,
/// written <c>value</c> or <c>value|label</c>; empty means any value of the type is accepted.
/// </summary>
public sealed record ReportParameterEdit(
    string Name,
    string? DataType,
    string? Prompt,
    string? DefaultValue,
    string? Options,
    bool AllowBlank,
    bool Nullable,
    bool Hidden,
    bool MultiValue = false,
    string? AvailableSource = null,
    string? OptionsDataSet = null,
    string? OptionsValueField = null,
    string? OptionsLabelField = null,
    string? DefaultSource = null,
    string? DefaultDataSet = null,
    string? DefaultValueField = null);

public sealed record ProviderChoice(string Key, string ResourceKey, IReadOnlyList<DesignField> Fields, string Group = "REX", string Label = "", bool IsView = false);

public sealed record ReportDesignModel(
    Guid Id,
    string Name,
    int Revision,
    double BodyWidthInches,
    double BodyHeightInches,
    IReadOnlyList<DesignItem> Items,
    IReadOnlyList<DesignDataSet> DataSets,
    IReadOnlyList<DesignParameter> Parameters,
    IReadOnlyList<ProviderChoice> Providers,
    string? SelectedItem,
    IReadOnlyList<string> Warnings,
    bool CanUndo,
    bool CanRedo,
    double HeaderHeightInches = 0,
    double FooterHeightInches = 0,
    string? SelectedKind = null,
    IReadOnlyList<DesignPropertySection>? Properties = null,
    IReadOnlyList<DesignImage>? Images = null,
    IReadOnlyList<DesignExplorerNode>? Explorer = null,
    bool HasClipboard = false,
    double PageWidthInches = 8.5,
    double PageHeightInches = 11,
    bool HasHeader = true,
    bool HasFooter = true)
{
    /// <summary>The RDLC file on disk the report is linked to; Save writes the definition back to it.</summary>
    public string? SourcePath { get; init; }

    /// <summary>The Business Central data file that feeds previews, or null when previews use sample rows.</summary>
    public string? DataFile { get; init; }

    /// <summary>True when the layout carries a <c>Code</c> block (every Business Central layout does).</summary>
    public bool HasCustomCode { get; init; }

    /// <summary>Paper, orientation, margins, columns and the layout's unit (cm or inches).</summary>
    public DesignPageSetup? Page { get; init; }
}

public sealed record ReportOutput(byte[] Content, string ContentType, string FileName);

/// <summary>How the web layer passes the values of a multi-value parameter to <see cref="IReportDesignService.RenderAsync"/>.</summary>
public static class ReportParameterValues
{
    /// <summary>Joins the values of a multi-value parameter in one string.</summary>
    public const char Separator = '\u001f';
}

public interface IReportDesignService
{
    Task<IReadOnlyList<ReportListItem>> ListAsync(CancellationToken cancellationToken);

    Task<IReadOnlyList<ReportRevisionEntry>> RevisionsAsync(Guid id, CancellationToken cancellationToken);

    Task<ReportDesignModel> CreateAsync(string name, CancellationToken cancellationToken);

    Task<ReportDesignModel?> OpenAsync(Guid id, string? selectedItem, CancellationToken cancellationToken);

    Task<ReportDesignModel> RenameAsync(Guid id, string name, CancellationToken cancellationToken);

    Task<ReportDesignModel> StepAsync(Guid id, int direction, CancellationToken cancellationToken);

    Task<ReportDesignModel> SaveAsync(Guid id, CancellationToken cancellationToken);

    Task DeleteAsync(Guid id, CancellationToken cancellationToken);

    Task<ReportDesignModel> ImportAsync(string name, string definition, CancellationToken cancellationToken);

    Task<ReportDesignModel> PlaceAsync(Guid id, string kind, double leftInches, double topInches, CancellationToken cancellationToken, string? region = null);

    Task<ReportDesignModel> MoveAsync(Guid id, string itemName, double leftInches, double topInches, double widthInches, double heightInches, CancellationToken cancellationToken);

    Task<ReportDesignModel> ResizeBandAsync(Guid id, string region, double heightInches, CancellationToken cancellationToken);

    Task<ReportDesignModel> AddColumnAsync(Guid id, string table, string field, CancellationToken cancellationToken);

    Task<ReportDesignModel> EditColumnAsync(Guid id, string table, int index, string? header, string? value, double widthInches, CancellationToken cancellationToken);

    Task<ReportDesignModel> MoveColumnAsync(Guid id, string table, int index, int direction, CancellationToken cancellationToken);

    Task<ReportDesignModel> RemoveColumnAsync(Guid id, string table, int index, CancellationToken cancellationToken);

    Task<ReportDesignModel> AddGroupAsync(Guid id, string table, string axis, string field, CancellationToken cancellationToken);

    Task<ReportDesignModel> RemoveGroupAsync(Guid id, string table, string groupName, CancellationToken cancellationToken);

    Task<ReportDesignModel> AddTotalAsync(Guid id, string table, CancellationToken cancellationToken);

    Task<ReportDesignModel> RepeatHeaderAsync(Guid id, string table, bool repeat, CancellationToken cancellationToken);

    Task<ReportDesignModel> EditAsync(Guid id, ReportItemEdit edit, CancellationToken cancellationToken);

    Task<ReportDesignModel> RemoveItemAsync(Guid id, string itemName, CancellationToken cancellationToken);

    Task<ReportDesignModel> UseDataSetAsync(Guid id, string providerKey, CancellationToken cancellationToken);

    Task<ReportDesignModel> AddParameterAsync(Guid id, string name, string dataType, string prompt, CancellationToken cancellationToken);

    Task<ReportDesignModel> RemoveParameterAsync(Guid id, string name, CancellationToken cancellationToken);

    Task<ReportDesignModel> EditParameterAsync(Guid id, ReportParameterEdit edit, CancellationToken cancellationToken);

    Task<ReportDesignModel> AddFilterAsync(Guid id, string field, string operation, string value, bool skipWhenBlank, CancellationToken cancellationToken);

    Task<ReportDesignModel> RemoveFilterAsync(Guid id, int index, CancellationToken cancellationToken);

    Task<ReportDesignModel> RemoveDataSetFilterAsync(Guid id, string dataSetName, int index, CancellationToken cancellationToken);

    Task<ReportDesignModel> EditChartAsync(Guid id, ReportChartEdit edit, CancellationToken cancellationToken);

    Task<ReportOutput> RenderAsync(Guid id, IReadOnlyDictionary<string, string?> parameters, ReportOutputFormat format, CancellationToken cancellationToken);

    Task<ReportDesignModel> PlaceInAsync(Guid id, string kind, double leftInches, double topInches, string? region, string? dataSetName, CancellationToken cancellationToken);

    Task<ReportDesignModel> AddDataSetAsync(Guid id, string providerKey, string? name, IReadOnlyList<string> fields, CancellationToken cancellationToken);

    Task<ReportDesignModel> SetDataSetFieldsAsync(Guid id, string dataSetName, IReadOnlyList<string> fields, CancellationToken cancellationToken);

    Task<ReportDesignModel> RemoveDataSetAsync(Guid id, string dataSetName, CancellationToken cancellationToken);

    Task<DesignDataPreview> PreviewDataAsync(Guid id, string dataSetName, CancellationToken cancellationToken);

    Task<ReportDesignModel> SetPropertyAsync(Guid id, string target, string key, string? value, CancellationToken cancellationToken);

    Task<ReportDesignModel> CopyAsync(Guid id, string itemName, bool cut, CancellationToken cancellationToken);

    Task<ReportDesignModel> PasteAsync(Guid id, string? region, CancellationToken cancellationToken);

    Task<ReportDesignModel> ArrangeAsync(Guid id, string itemName, string operation, CancellationToken cancellationToken);

    Task<ReportDesignModel> ArrangeManyAsync(Guid id, IReadOnlyList<string> itemNames, string operation, CancellationToken cancellationToken);

    Task<ReportDesignModel> AddImageAsync(Guid id, string fileName, byte[] content, CancellationToken cancellationToken);

    Task<ReportDesignModel> RemoveImageAsync(Guid id, string imageName, CancellationToken cancellationToken);

    Task<ReportDesignModel> ToggleBandAsync(Guid id, string region, bool show, CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<string, IReadOnlyList<ParameterOption>>> ParameterOptionsAsync(Guid id, CancellationToken cancellationToken);

    Task<ReportOutput> DefinitionAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Adds the standard reports that are not yet in the report center, see <see cref="ReportTemplates"/>.</summary>
    Task<ReportTemplateInstall> InstallTemplatesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Opens an RDLC file from disk. The first time, the file is imported into the workspace and linked to it;
    /// afterwards the linked report is reopened, and a change made to the file outside REX becomes a new revision.
    /// </summary>
    Task<ReportDesignModel> OpenFileAsync(string path, CancellationToken cancellationToken);

    /// <summary>
    /// Writes the current definition to the linked RDLC (or to <paramref name="path"/>, which becomes the link).
    /// The previous file is kept as <c>.bak</c>. Returns the path written.
    /// </summary>
    Task<string> SaveToFileAsync(Guid id, string? path, CancellationToken cancellationToken);

    /// <summary>Attaches a Business Central data file (XML, JSON or CSV) to a report; null detaches it.</summary>
    Task<ReportDesignModel> AttachDataAsync(Guid id, string? dataPath, CancellationToken cancellationToken);

    /// <summary>
    /// Applies several property changes as one revision, so one undo reverts them all: formatting a multiple
    /// selection, or the page setup dialog. <paramref name="selected"/> stays selected afterwards.
    /// </summary>
    Task<ReportDesignModel> SetPropertiesAsync(Guid id, IReadOnlyList<ReportPropertyChange> changes, string? selected, CancellationToken cancellationToken);

    /// <summary>Narrows the body to the width between the margins (it never cuts through an item).</summary>
    Task<ReportDesignModel> FitBodyToPageAsync(Guid id, CancellationToken cancellationToken);

    // Tablix structure: the grouping pane and the tablix cell commands.

    /// <summary>Adds a parent, child or adjacent group, with optional group header and footer rows.</summary>
    Task<ReportDesignModel> InsertGroupAsync(Guid id, TablixGroupInsert insert, CancellationToken cancellationToken);

    /// <summary>Deletes a group, alone or with its own header/footer rows and header cells.</summary>
    Task<ReportDesignModel> DeleteGroupAsync(Guid id, string table, string group, TablixDeleteMode mode, CancellationToken cancellationToken);

    /// <summary>Adds a total row (or column) before or after a group; null targets the details group.</summary>
    Task<ReportDesignModel> AddGroupTotalAsync(Guid id, string table, string axis, string? group, bool after, CancellationToken cancellationToken);

    /// <summary>The group properties (general, sorting, filters, visibility, page breaks, variables).</summary>
    Task<DesignGroupDetail> ReadGroupAsync(Guid id, string table, string group, CancellationToken cancellationToken);

    /// <summary>Edits the group properties as one revision.</summary>
    Task<ReportDesignModel> EditGroupAsync(Guid id, TablixGroupEdit edit, CancellationToken cancellationToken);

    /// <summary>Edits the static member (header, footer, total row or column) that owns a body row or column.</summary>
    Task<ReportDesignModel> EditTablixMemberAsync(Guid id, TablixMemberEdit edit, CancellationToken cancellationToken);

    /// <summary>Inserts a row ("Row") or column ("Column") inside or outside the group of a body cell.</summary>
    Task<ReportDesignModel> InsertTablixSlotAsync(Guid id, string table, string axis, int index, TablixInsertPlacement placement, CancellationToken cancellationToken);

    /// <summary>Deletes a body row or column, alone or with its associated group.</summary>
    Task<ReportDesignModel> DeleteTablixSlotAsync(Guid id, string table, string axis, int index, TablixDeleteMode mode, CancellationToken cancellationToken);

    /// <summary>Merges adjacent body cells of one row.</summary>
    Task<ReportDesignModel> MergeTablixCellsAsync(Guid id, string table, int row, int firstColumn, int lastColumn, CancellationToken cancellationToken);

    /// <summary>Splits a merged body cell.</summary>
    Task<ReportDesignModel> SplitTablixCellAsync(Guid id, string table, int row, int column, CancellationToken cancellationToken);
}
