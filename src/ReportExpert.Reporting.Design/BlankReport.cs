using ReportExpert.Rdl.Core;

namespace ReportExpert.Reporting.Design;

internal static class BlankReport
{
    public const string MarkName = "SafiMark";

    public const string MarkPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    public static RdlDocument Create() => RdlDocument.Parse(
        """
        <?xml version="1.0" encoding="utf-8"?>
        <Report xmlns="http://schemas.microsoft.com/sqlserver/reporting/2016/01/reportdefinition" xmlns:rd="http://schemas.microsoft.com/SQLServer/reporting/reportdesigner">
          <DataSources>
            <DataSource Name="DataSource">
              <ConnectionProperties>
                <DataProvider>System.Data.DataSet</DataProvider>
                <ConnectString>/* Report Expert */</ConnectString>
              </ConnectionProperties>
            </DataSource>
          </DataSources>
          <DataSets />
          <ReportSections>
            <ReportSection>
              <Body>
                <ReportItems />
                <Height>2in</Height>
                <Style />
              </Body>
              <Width>7.5in</Width>
              <Page>
                <PageHeight>11in</PageHeight>
                <PageWidth>8.5in</PageWidth>
                <LeftMargin>0.5in</LeftMargin>
                <RightMargin>0.5in</RightMargin>
                <TopMargin>0.5in</TopMargin>
                <BottomMargin>0.5in</BottomMargin>
                <ColumnSpacing>0.13in</ColumnSpacing>
                <PageHeader>
                  <Height>0.5in</Height>
                  <PrintOnFirstPage>true</PrintOnFirstPage>
                  <PrintOnLastPage>true</PrintOnLastPage>
                  <Style />
                </PageHeader>
                <PageFooter>
                  <Height>0.4in</Height>
                  <PrintOnFirstPage>true</PrintOnFirstPage>
                  <PrintOnLastPage>true</PrintOnLastPage>
                  <Style />
                </PageFooter>
                <Style />
              </Page>
            </ReportSection>
          </ReportSections>
        </Report>
        """);
}
