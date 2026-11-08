using System.Xml.Linq;

namespace ReportExpert.Reporting.Design.Tests;

/// <summary>
/// The Properties pane lists only properties that apply: for every item of a layout, writing back each
/// listed property (its current value, or its first option) is never rejected as "does not apply".
/// </summary>
public sealed class PropertyApplicabilityTests
{
    public static TheoryData<string> Layouts() => ["bc-customer-list", "sample-report"];

    [Theory]
    [MemberData(nameof(Layouts))]
    public async Task Every_listed_property_applies_to_its_item(string layout)
    {
        using var fixture = new DesignFixture();
        var xml = layout == "sample-report" ? File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "sample-report.rdlc")) : null;
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(layout + ".rdlc", xml), CancellationToken.None);
        var definition = XDocument.Parse(System.Text.Encoding.UTF8.GetString((await fixture.Service.DefinitionAsync(model.Id, CancellationToken.None)).Content));
        var targets = definition.Descendants()
            .Where(element => element.Name.LocalName is "Textbox" or "Rectangle" or "Line" or "Image" or "Tablix" or "Chart" or "Subreport")
            .Select(element => element.Attribute("Name")?.Value)
            .Where(name => !string.IsNullOrEmpty(name))
            .Select(name => name!)
            .Concat(["@Report", "@PageHeader", "@PageFooter"])
            .Distinct()
            .ToList();
        var failures = new List<string>();
        var tried = 0;
        foreach (var target in targets)
        {
            ReportDesignModel? selected;
            try
            {
                selected = await fixture.Service.OpenAsync(model.Id, target, CancellationToken.None);
            }
            catch (ReportDesignException)
            {
                continue;
            }

            if (selected?.SelectedItem != target || selected.Properties is null)
            {
                continue;
            }

            foreach (var property in selected.Properties.SelectMany(section => section.Properties))
            {
                var value = property.Value.Length > 0 ? property.Value : property.Options?.FirstOrDefault(option => option.Length > 0);
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                tried++;
                try
                {
                    model = await fixture.Service.SetPropertyAsync(model.Id, target, property.Key, value, CancellationToken.None);
                }
                catch (ReportDesignException exception) when (exception.Message == ReportCodes.InvalidItem)
                {
                    failures.Add($"{target} ({selected.SelectedKind}).{property.Key} = {value}");
                }
                catch (ReportDesignException)
                {
                    // A value the validator refuses (for example an expression the guard blocks) is not an applicability problem.
                }
            }
        }

        Assert.True(tried > 40, $"only {tried} properties were tried");
        Assert.True(failures.Count == 0, "Listed but not applicable:\n" + string.Join("\n", failures));
    }

    [Theory]
    [InlineData(1, "AlignLeft", true)]
    [InlineData(1, "SameSize", false)]
    [InlineData(1, "DistributeHorizontally", false)]
    [InlineData(2, "SameWidth", true)]
    [InlineData(2, "DistributeVertically", false)]
    [InlineData(3, "DistributeVertically", true)]
    [InlineData(3, "BringToFront", true)]
    [InlineData(0, "AlignLeft", false)]
    public void Arrange_operations_match_the_selection_size(int count, string operation, bool expected) =>
        Assert.Equal(expected, ReportArrangeOperations.For(count).Contains(operation));

    [Fact]
    public async Task Every_arrange_operation_offered_for_a_selection_succeeds()
    {
        using var fixture = new DesignFixture();
        var model = await fixture.Service.OpenFileAsync(fixture.WriteLayout(), CancellationToken.None);
        for (var index = 0; index < 3; index++)
        {
            model = await fixture.Service.PlaceAsync(model.Id, "Textbox", 0.5 + index * 1.5, 0.2 + index * 0.3, CancellationToken.None, "Header");
        }

        var boxes = model.Items.Where(item => item.Region == "Header" && item.Kind == "Textbox").Select(item => item.Name).TakeLast(3).ToArray();
        for (var count = 1; count <= 3; count++)
        {
            var selection = boxes.Take(count).ToArray();
            foreach (var operation in ReportArrangeOperations.For(count))
            {
                model = count == 1
                    ? await fixture.Service.ArrangeAsync(model.Id, selection[0], operation, CancellationToken.None)
                    : await fixture.Service.ArrangeManyAsync(model.Id, selection, operation, CancellationToken.None);
            }
        }
    }
}
