using System.Text;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using UglyToad.PdfPig.Content;
using UglyToad.PdfPig.Core;
using UglyToad.PdfPig.Fonts.Standard14Fonts;
using UglyToad.PdfPig.Writer;
using Xunit;
using Xunit.Abstractions;

namespace Gram.Blazor.UnitTests;

public class DocumentIngestionIntegrationTests
{
    private readonly ITestOutputHelper _output;

    public DocumentIngestionIntegrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task EndToEnd_MockPdfAndCsvIngestion_ExtractsFullContentForModelContext()
    {
        using var data = new TemporaryDirectory();
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", data.Path);

        var storage = new StorageService();
        var sessionStorage = new SessionStorageService(storage);
        var parser = new DocumentParserService();
        var settings = new SettingsService(new ConfigurationBuilder().Build(), storage);
        var repoMap = new RepositoryMapService();
        var contextBuilder = new ContextBuilder(sessionStorage, settings, repoMap, documentParser: parser);

        var conversationId = "conv_test_doc_01";
        var conversation = new Conversation(conversationId, "Document Analysis Test", DateTimeOffset.UtcNow);

        // 1. Generate a real multi-page binary PDF
        var pdfBuilder = new PdfDocumentBuilder();
        var page1 = pdfBuilder.AddPage(PageSize.A4);
        var font = pdfBuilder.AddStandard14Font(Standard14Font.Helvetica);
        page1.AddText("Quarterly Business Review Q3", 14, new PdfPoint(50, 750), font);
        page1.AddText("Net Revenue: $4,500,000 (Growth: +32%)", 12, new PdfPoint(50, 710), font);
        page1.AddText("Active Users: 120,000 MAU", 12, new PdfPoint(50, 680), font);

        var page2 = pdfBuilder.AddPage(PageSize.A4);
        page2.AddText("Q4 Roadmap & Engineering Goals", 14, new PdfPoint(50, 750), font);
        page2.AddText("Deliver Real-Time Voice and Multi-Format PDF Viewer.", 12, new PdfPoint(50, 710), font);
        page2.AddText("Targeting sub-500ms voice latency across all desktop clients.", 12, new PdfPoint(50, 680), font);

        var pdfBytes = pdfBuilder.Build();
        using (var pdfStream = new MemoryStream(pdfBytes))
        {
            var attPdf = await sessionStorage.SaveAttachmentAsync(conversationId, "Quarterly_Report_Q3.pdf", pdfStream, "application/pdf");
            conversation.Attachments.Add(attPdf);
        }

        // 2. Generate a structured CSV file
        var csvContent = "Metric,Q1,Q2,Q3,Target\nARR,$1.2M,$2.4M,$4.5M,$5.0M\nChurn,2.1%,1.8%,1.2%,<1.5%\nNPS,68,72,79,>75";
        using (var csvStream = new MemoryStream(Encoding.UTF8.GetBytes(csvContent)))
        {
            var attCsv = await sessionStorage.SaveAttachmentAsync(conversationId, "performance_metrics.csv", csvStream, "text/csv");
            conversation.Attachments.Add(attCsv);
        }

        // 3. Directly test DocumentParserService extraction
        var pdfFilePath = sessionStorage.GetAttachmentPath(conversationId, "Quarterly_Report_Q3.pdf");
        var parsedPdf = await parser.ParseDocumentAsync(pdfFilePath, "Quarterly_Report_Q3.pdf", "application/pdf");

        Assert.True(parsedPdf.IsSuccess);
        Assert.Equal(2, parsedPdf.PageCount);
        Assert.Equal(DocumentFormat.Pdf, parsedPdf.Format);
        Assert.Contains("Quarterly Business Review Q3", parsedPdf.Pages[0].Text);
        Assert.Contains("Net Revenue: $4,500,000", parsedPdf.Pages[0].Text);
        Assert.Contains("Q4 Roadmap & Engineering Goals", parsedPdf.Pages[1].Text);
        Assert.Contains("Deliver Real-Time Voice and Multi-Format PDF Viewer", parsedPdf.Pages[1].Text);

        // 4. Test Full Context Assembly for LLM Prompt
        conversation.Messages.Add(new Message("user", "Summarize the Q3 revenue growth and Q4 engineering goals from the attached files."));
        var context = await contextBuilder.AssembleContextAsync(conversation, relevanceQuery: "Summarize Q3 revenue growth");

        _output.WriteLine("=== ASSEMBLED SYSTEM PROMPT CONTEXT ===");
        _output.WriteLine(context.SystemPrompt);

        // Verify prompt contains both documents with structured XML tags
        Assert.Contains("<workspace_attachments>", context.SystemPrompt);
        Assert.Contains("<pdf_document name=\"Quarterly_Report_Q3.pdf\" pages=\"2\"", context.SystemPrompt);
        Assert.Contains("<page number=\"1\">", context.SystemPrompt);
        Assert.Contains("Net Revenue: $4,500,000", context.SystemPrompt);
        Assert.Contains("<page number=\"2\">", context.SystemPrompt);
        Assert.Contains("Deliver Real-Time Voice and Multi-Format PDF Viewer.", context.SystemPrompt);
        Assert.Contains("<tabular_data name=\"performance_metrics.csv\"", context.SystemPrompt);
        Assert.Contains("| Metric | Q1 | Q2 | Q3 | Target |", context.SystemPrompt);
        Assert.Contains("| ARR | $1.2M | $2.4M | $4.5M | $5.0M |", context.SystemPrompt);
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "gram-doc-test-" + Guid.NewGuid().ToString("N"));

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                try { Directory.Delete(Path, recursive: true); } catch { }
            }
        }
    }
}
