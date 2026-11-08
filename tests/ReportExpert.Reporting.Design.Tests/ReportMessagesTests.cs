using System.Reflection;

namespace ReportExpert.Reporting.Design.Tests;

public sealed class ReportMessagesTests
{
    [Fact]
    public void EveryCodeHasAText()
    {
        var codes = typeof(ReportCodes).GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(field => field.IsLiteral && field.FieldType == typeof(string))
            .Select(field => (string)field.GetRawConstantValue()!);
        foreach (var code in codes)
        {
            Assert.NotEqual(code, ReportMessages.For(code));
        }
    }

    [Fact]
    public void DetailFollowsTheText()
    {
        var text = ReportMessages.For(new ReportDesignException(ReportCodes.RenderFailed, "Textbox1: bad expression"));
        Assert.Equal("The report could not be rendered. Textbox1: bad expression", text);
    }
}
