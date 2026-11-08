namespace ReportExpert.Reporting.Design;

/// <summary>
/// English texts for <see cref="ReportCodes"/>, the desktop counterpart of SafiPay's <c>ReportDesignerText</c>.
/// </summary>
public static class ReportMessages
{
    private static readonly Dictionary<string, string> Texts = new(StringComparer.Ordinal)
    {
        [ReportCodes.NeedDataSet] = "Add a dataset before placing a data region.",
        [ReportCodes.UnknownProvider] = "The report reads a data source REX does not know.",
        [ReportCodes.NotFound] = "The report or file was not found.",
        [ReportCodes.InvalidName] = "Use a name of 1 to 80 characters without / \\ or :.",
        [ReportCodes.InvalidItem] = "That change does not apply to the selected item.",
        [ReportCodes.UnsafeDefinition] = "The definition uses features the strict policy does not allow (custom code, external data or unsafe expressions).",
        [ReportCodes.InvalidDefinition] = "The file is not a valid RDL/RDLC report definition.",
        [ReportCodes.Limit] = "The report exceeds a size or time limit.",
        [ReportCodes.RenderFailed] = "The report could not be rendered.",
        [ReportCodes.Saved] = "Report saved.",
        [ReportCodes.Created] = "Report created.",
        [ReportCodes.Deleted] = "Report removed from the workspace.",
        [ReportCodes.LoadFailed] = "The report could not be loaded.",
        [ReportCodes.ParameterRequired] = "A required parameter has no value.",
        [ReportCodes.InvalidParameterValue] = "A parameter value does not match its type.",
        [ReportCodes.ParameterInUse] = "The parameter is still used by the layout.",
        [ReportCodes.ParameterNeedsDefault] = "A hidden parameter needs a default value.",
        [ReportCodes.TemplatesInstalled] = "Starter layouts installed.",
        [ReportCodes.TemplatesUpToDate] = "All starter layouts are already in the report center.",
        [ReportCodes.TemplatesUnavailable] = "Some starter layouts need data sources this installation does not have.",
        [ReportCodes.DataSetInUse] = "The dataset is still used by a data region or parameter.",
        [ReportCodes.NeedFields] = "Choose at least one field.",
        [ReportCodes.ImageInUse] = "The image is still used by the layout.",
        [ReportCodes.InvalidImage] = "Use a PNG or JPEG image.",
        [ReportCodes.ClipboardEmpty] = "Nothing has been copied yet.",
        [ReportCodes.InvalidValue] = "That value is not valid for this property.",
        [ReportCodes.DataUnavailable] = "The data for this dataset could not be read. Check the attached Business Central data file.",
        [ReportCodes.ParameterCycle] = "Parameters depend on each other in a loop.",
    };

    /// <summary>The text for a code, or the code itself when it is not a known code.</summary>
    public static string For(string code) => Texts.TryGetValue(code, out var text) ? text : code;

    /// <summary>The message for an exception thrown by <see cref="IReportDesignService"/>, with the engine's detail.</summary>
    public static string For(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        if (exception is ReportDesignException design)
        {
            var text = For(design.Message);
            return string.IsNullOrWhiteSpace(design.Detail) ? text : text + " " + design.Detail;
        }

        return exception.Message;
    }
}
