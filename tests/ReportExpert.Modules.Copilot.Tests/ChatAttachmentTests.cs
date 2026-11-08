using System.IO;
using System.Text.Json.Nodes;
using ReportExpert.Modules.Copilot.Services;

namespace ReportExpert.Modules.Copilot.Tests;

public sealed class ChatAttachmentTests : IDisposable
{
    private readonly string _folder = Directory.CreateTempSubdirectory("rex-attach-").FullName;

    public void Dispose() => Directory.Delete(_folder, recursive: true);

    private string Write(string name, string content)
    {
        string path = Path.Combine(_folder, name);
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Rdlc_file_is_inlined_as_text_with_its_type()
    {
        string path = Write("Customer.rdlc", "<Report><Body /></Report>");

        var attachment = ChatAttachmentLoader.LoadFile(path);

        Assert.Equal(ChatAttachmentKind.Text, attachment.Kind);
        Assert.Equal("RDLC", attachment.TypeLabel);
        Assert.Contains("<Report><Body /></Report>", attachment.ToPromptBlock());
        Assert.StartsWith("<attached_file name=\"Customer.rdlc\" type=\"RDLC\"", attachment.ToPromptBlock());
        Assert.EndsWith("</attached_file>", attachment.ToPromptBlock());
    }

    [Fact]
    public void Json_and_unknown_text_extensions_are_accepted()
    {
        Assert.Equal("JSON", ChatAttachmentLoader.LoadFile(Write("app.json", "{\"a\":1}")).TypeLabel);
        Assert.Equal("NOTES", ChatAttachmentLoader.LoadFile(Write("todo.notes", "plain text")).TypeLabel);
    }

    [Fact]
    public void Long_text_is_truncated_and_marked()
    {
        string path = Write("big.txt", new string('x', ChatAttachmentLoader.MaxTextChars + 500));

        var attachment = ChatAttachmentLoader.LoadFile(path);

        Assert.True(attachment.IsTruncated);
        Assert.Contains("truncated=\"true\"", attachment.ToPromptBlock());
        Assert.Contains("truncated", attachment.Detail);
    }

    [Fact]
    public void Binary_file_is_refused_with_a_reason()
    {
        string path = Path.Combine(_folder, "tool.exe");
        File.WriteAllBytes(path, [0x4D, 0x5A, 0x00, 0x00, 0x03]);

        var error = Assert.Throws<InvalidOperationException>(() => ChatAttachmentLoader.LoadFile(path));
        Assert.Contains("not a text file", error.Message);
    }

    [Fact]
    public void Missing_file_is_refused()
    {
        Assert.Throws<InvalidOperationException>(() => ChatAttachmentLoader.LoadFile(Path.Combine(_folder, "gone.xml")));
    }

    [Fact]
    public void Pdf_text_is_extracted()
    {
        var builder = new UglyToad.PdfPig.Writer.PdfDocumentBuilder();
        var font = builder.AddStandard14Font(UglyToad.PdfPig.Fonts.Standard14Fonts.Standard14Font.Helvetica);
        builder.AddPage(UglyToad.PdfPig.Content.PageSize.A4).AddText("Invoice total mismatch", 12, new UglyToad.PdfPig.Core.PdfPoint(40, 700), font);
        string path = Path.Combine(_folder, "spec.pdf");
        File.WriteAllBytes(path, builder.Build());

        var attachment = ChatAttachmentLoader.LoadFile(path);

        Assert.Equal(ChatAttachmentKind.Pdf, attachment.Kind);
        Assert.Contains("Invoice total mismatch", attachment.Text);
        Assert.Contains("--- Page 1 ---", attachment.Text);
    }

    [Fact]
    public void Broken_pdf_is_refused()
    {
        string path = Write("broken.pdf", "not really a pdf");
        Assert.Throws<InvalidOperationException>(() => ChatAttachmentLoader.LoadFile(path));
    }

    [Fact]
    public void Prompt_puts_the_text_first_then_files_then_names_the_images()
    {
        var file = ChatAttachmentLoader.FromText("<x/>", "a.xml", "XML", 4);
        var image = new ChatAttachment
        {
            FileName = "screen.png",
            Kind = ChatAttachmentKind.Image,
            Image = new ChatImage("image/png", "AAAA"),
            TypeLabel = "PNG",
        };

        string prompt = ChatAttachmentLoader.ComposePrompt("Why does this fail?", [file, image]);

        Assert.StartsWith("Why does this fail?", prompt);
        Assert.True(prompt.IndexOf("<attached_file name=\"a.xml\"", StringComparison.Ordinal) > 0);
        Assert.EndsWith("Attached image(s): screen.png.", prompt);
    }

    [Theory]
    [InlineData(512, "512 B")]
    [InlineData(43_008, "42 KB")]
    [InlineData(1_363_149, "1.3 MB")]
    public void Sizes_read_naturally(long bytes, string expected) =>
        Assert.Equal(expected, ChatAttachmentLoader.FormatSize(bytes));

    [Fact]
    public void Image_files_are_recognised_by_extension()
    {
        Assert.True(ChatAttachmentLoader.IsImageFile("shot.PNG"));
        Assert.True(ChatAttachmentLoader.IsImageFile("photo.jpeg"));
        Assert.False(ChatAttachmentLoader.IsImageFile("report.rdlc"));
    }
}

public sealed class MultimodalMessageTests
{
    [Fact]
    public void Message_without_images_keeps_plain_string_content()
    {
        var json = OpenAiCompatibleClient.BuildMessages([new ChatMessage("user", "hello")]);

        Assert.Equal("hello", json[0]!["content"]!.GetValue<string>());
    }

    [Fact]
    public void Message_with_images_becomes_text_and_image_url_parts()
    {
        var message = new ChatMessage("user", "What is wrong here?")
        {
            Images = [new ChatImage("image/png", "iVBORw0KGgo="), new ChatImage("image/jpeg", "/9j/4AAQ")],
        };

        var parts = OpenAiCompatibleClient.BuildMessages([message])[0]!["content"]!.AsArray();

        Assert.Equal(3, parts.Count);
        Assert.Equal("text", parts[0]!["type"]!.GetValue<string>());
        Assert.Equal("What is wrong here?", parts[0]!["text"]!.GetValue<string>());
        Assert.Equal("image_url", parts[1]!["type"]!.GetValue<string>());
        Assert.Equal("data:image/png;base64,iVBORw0KGgo=", parts[1]!["image_url"]!["url"]!.GetValue<string>());
        Assert.Equal("data:image/jpeg;base64,/9j/4AAQ", parts[2]!["image_url"]!["url"]!.GetValue<string>());
    }

    [Fact]
    public void Image_only_message_has_no_empty_text_part()
    {
        var message = new ChatMessage("user", string.Empty) { Images = [new ChatImage("image/png", "AA==")] };

        JsonArray parts = OpenAiCompatibleClient.BuildMessages([message])[0]!["content"]!.AsArray();

        Assert.Single(parts);
        Assert.Equal("image_url", parts[0]!["type"]!.GetValue<string>());
    }
}
