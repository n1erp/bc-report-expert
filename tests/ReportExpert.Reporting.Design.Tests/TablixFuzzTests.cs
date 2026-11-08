namespace ReportExpert.Reporting.Design.Tests;

/// <summary>
/// Seeded random sequences of tablix structure commands. After every accepted command the layout must
/// still compile and render in the report processing engine; a rejected command must leave the
/// revision unchanged.
/// </summary>
public sealed class TablixFuzzTests
{
    private static readonly (string File, string Table, string[] Fields)[] Layouts =
    [
        ("", "CustomerTable", ["City_Customer", "Name_Customer", "No_Customer", "CompanyName"]),
        ("sample-report.rdlc", "MainTable", ["ID", "Name", "Amount", "CreatedDate"]),
    ];

    /// <summary>Set <c>REX_FUZZ_SEEDS</c> (for example 200) to run a longer campaign locally.</summary>
    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        var count = int.TryParse(Environment.GetEnvironmentVariable("REX_FUZZ_SEEDS"), out var requested) ? requested : 0;
        foreach (var seed in count > 0 ? Enumerable.Range(1, count) : [1, 7, 42, 2026, 5, 99])
        {
            data.Add(seed);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public async Task Random_structure_edits_always_leave_a_valid_layout(int seed)
    {
        System.Text.Encoding.RegisterProvider(System.Text.CodePagesEncodingProvider.Instance);
        using var fixture = new DesignFixture();
        var (file, Table, Fields) = Layouts[seed % Layouts.Length];
        var xml = file.Length == 0 ? null : Renderable(File.ReadAllText(Path.Combine(AppContext.BaseDirectory, file)));
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(file.Length == 0 ? "CustomerList.rdlc" : file, xml), CancellationToken.None);
        var random = new Random(seed);
        var accepted = 0;
        for (var step = 0; step < 24; step++)
        {
            var table = model.Items.Single(item => item.Name == Table);
            var groups = (table.RowHierarchy ?? []).Concat(table.ColumnHierarchy ?? []).SelectMany(Flatten).Where(member => member.Kind == "Group").Select(member => member.Name).ToList();
            var rowGroups = (table.RowHierarchy ?? []).SelectMany(Flatten).Where(member => member.IsDynamic).Select(member => member.Name).ToList();
            var rows = table.Grid!.BodyRowCount;
            var columns = table.Grid.BodyColumnCount;
            var field = Fields[random.Next(Fields.Length)];
            var revision = model.Revision;
            var description = "";
            try
            {
                switch (random.Next(9))
                {
                    case 0:
                    case 1:
                    {
                        var position = (TablixGroupPosition)random.Next(4);
                        var target = rowGroups.Count == 0 ? null : rowGroups[random.Next(rowGroups.Count)];
                        description = $"row group {position} of {target ?? "details"} by {field}";
                        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Row", target, position, field, AddHeader: random.Next(3) == 0, AddFooter: random.Next(3) == 0, HeaderCell: random.Next(4) != 0), CancellationToken.None);
                        break;
                    }

                    case 2:
                        description = "column parent group by " + field;
                        model = await fixture.Service.InsertGroupAsync(model.Id, new TablixGroupInsert(Table, "Column", null, TablixGroupPosition.Parent, field), CancellationToken.None);
                        break;
                    case 3 when groups.Count > 0:
                    {
                        var group = groups[random.Next(groups.Count)];
                        var mode = (TablixDeleteMode)random.Next(2);
                        description = $"delete {group} {mode}";
                        model = await fixture.Service.DeleteGroupAsync(model.Id, Table, group, mode, CancellationToken.None);
                        break;
                    }

                    case 4:
                    {
                        var placement = (TablixInsertPlacement)random.Next(4);
                        var index = random.Next(rows);
                        description = $"insert row {placement} at {index}";
                        model = await fixture.Service.InsertTablixSlotAsync(model.Id, Table, "Row", index, placement, CancellationToken.None);
                        break;
                    }

                    case 5:
                    {
                        var placement = (TablixInsertPlacement)random.Next(4);
                        var index = random.Next(columns);
                        description = $"insert column {placement} at {index}";
                        model = await fixture.Service.InsertTablixSlotAsync(model.Id, Table, "Column", index, placement, CancellationToken.None);
                        break;
                    }

                    case 6:
                    {
                        var index = random.Next(rows);
                        var mode = (TablixDeleteMode)random.Next(2);
                        description = $"delete row {index} {mode}";
                        model = await fixture.Service.DeleteTablixSlotAsync(model.Id, Table, "Row", index, mode, CancellationToken.None);
                        break;
                    }

                    case 7:
                    {
                        var target = rowGroups.Count == 0 ? null : rowGroups[random.Next(rowGroups.Count)];
                        var after = random.Next(2) == 0;
                        description = $"total {(after ? "after" : "before")} {target ?? "details"}";
                        model = await fixture.Service.AddGroupTotalAsync(model.Id, Table, "Row", target, after, CancellationToken.None);
                        break;
                    }

                    default:
                    {
                        var row = random.Next(rows);
                        var column = random.Next(Math.Max(1, columns - 1));
                        description = $"merge row {row} columns {column}-{column + 1}";
                        model = columns > 1
                            ? await fixture.Service.MergeTablixCellsAsync(model.Id, Table, row, column, column + 1, CancellationToken.None)
                            : model;
                        break;
                    }
                }
            }
            catch (ReportDesignException exception) when (exception.Message is ReportCodes.InvalidItem or ReportCodes.Limit or ReportCodes.InvalidValue)
            {
                var current = await fixture.Service.OpenAsync(model.Id, null, CancellationToken.None);
                Assert.Equal(revision, current!.Revision);
                continue;
            }

            accepted++;
            try
            {
                var parameters = model.Parameters.ToDictionary(
                    parameter => parameter.Name,
                    parameter => (string?)(parameter.DataType switch
                    {
                        "Integer" or "Float" => "1",
                        "DateTime" => "2026-01-01",
                        "Boolean" => "true",
                        _ => "x",
                    }));
                var output = await fixture.Service.RenderAsync(model.Id, parameters, ReportOutputFormat.Excel, CancellationToken.None);
                Assert.True(output.Content.Length > 100);
            }
            catch (ReportDesignException exception)
            {
                Assert.Fail($"seed {seed}, step {step} ({description}): {exception.Message} {exception.Detail} {exception.InnerException?.Message}");
            }
        }

        Assert.True(accepted >= 8, $"only {accepted} commands were accepted");
    }

    /// <summary>The shared parser sample omits the body height and section width the processor requires, and names a missing image.</summary>
    private static string Renderable(string xml)
    {
        var document = System.Xml.Linq.XDocument.Parse(xml);
        var ns = document.Root!.Name.Namespace;
        System.Xml.Linq.Extensions.Remove(document.Descendants(ns + "Image").ToList());
        foreach (var section in document.Descendants(ns + "ReportSection"))
        {
            if (section.Element(ns + "Width") is null)
            {
                section.Element(ns + "Body")!.AddAfterSelf(new System.Xml.Linq.XElement(ns + "Width", "6.5in"));
            }

            var body = section.Element(ns + "Body")!;
            if (body.Element(ns + "Height") is null)
            {
                body.Add(new System.Xml.Linq.XElement(ns + "Height", "3in"));
            }
        }

        return document.ToString();
    }

    private static IEnumerable<DesignMember> Flatten(DesignMember member) => [member, .. member.Children.SelectMany(Flatten)];
}
