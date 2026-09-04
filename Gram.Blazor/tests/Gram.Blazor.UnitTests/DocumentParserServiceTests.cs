using System.IO.Compression;
using System.Text;
using Gram.Blazor.Shared;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class DocumentParserServiceTests
{
    private readonly DocumentParserService _parser = new();

    [Fact]
    public async Task ParseDocumentAsync_CsvFile_ExtractsHeadersRowsAndMarkdown()
    {
        var tempFile = Path.GetTempFileName() + ".csv";
        try
        {
            var csvContent = "Name,Age,Role\nAlice,30,Engineer\nBob,25,Designer\nCharlie,35,Manager";
            await File.WriteAllTextAsync(tempFile, csvContent);

            var result = await _parser.ParseDocumentAsync(tempFile, "users.csv");

            Assert.True(result.IsSuccess);
            Assert.Equal(DocumentFormat.Csv, result.Format);
            Assert.Equal(3, result.ColumnCount);
            Assert.Equal(4, result.RowCount);
            Assert.Equal(["Name", "Age", "Role"], result.CsvHeaders);
            Assert.Equal(3, result.CsvRows.Count);
            Assert.Contains("| Name | Age | Role |", result.FormattedContextText);
            Assert.Contains("| Alice | 30 | Engineer |", result.FormattedContextText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseDocumentAsync_PowerPointFile_ExtractsSlidesAndSpeakerNotes()
    {
        var tempFile = Path.GetTempFileName() + ".pptx";
        try
        {
            using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
            {
                var slide1 = zip.CreateEntry("ppt/slides/slide1.xml");
                using (var writer = new StreamWriter(slide1.Open(), Encoding.UTF8))
                {
                    writer.Write("<p:sld xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>Gram AI Vision 2026</a:t></a:r></a:p><a:p><a:r><a:t>Autonomous Coding and Realtime Speech</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:sld>");
                }

                var note1 = zip.CreateEntry("ppt/notesSlides/notesSlide1.xml");
                using (var writer = new StreamWriter(note1.Open(), Encoding.UTF8))
                {
                    writer.Write("<p:notes xmlns:p=\"http://schemas.openxmlformats.org/presentationml/2006/main\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><p:cSld><p:spTree><p:sp><p:txBody><a:p><a:r><a:t>Emphasize sub-500ms voice response times to investors.</a:t></a:r></a:p></p:txBody></p:sp></p:spTree></p:cSld></p:notes>");
                }
            }

            var result = await _parser.ParseDocumentAsync(tempFile, "pitch.pptx");

            Assert.True(result.IsSuccess);
            Assert.Equal(DocumentFormat.PowerPoint, result.Format);
            Assert.Equal(1, result.SlideCount);
            Assert.Equal("Gram AI Vision 2026", result.Slides[0].Title);
            Assert.Contains("Autonomous Coding and Realtime Speech", result.Slides[0].Content);
            Assert.Contains("Emphasize sub-500ms voice response times to investors", result.Slides[0].SpeakerNotes);
            Assert.Contains("<powerpoint_presentation name=\"pitch.pptx\" slides=\"1\"", result.FormattedContextText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseDocumentAsync_WordFile_ExtractsHeadingsAndParagraphs()
    {
        var tempFile = Path.GetTempFileName() + ".docx";
        try
        {
            using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
            {
                var doc = zip.CreateEntry("word/document.xml");
                using (var writer = new StreamWriter(doc.Open(), Encoding.UTF8))
                {
                    writer.Write("<w:document xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"><w:body><w:p><w:r><w:t>Architecture Specification Document</w:t></w:r></w:p><w:p><w:r><w:t>This section details the DocumentParserService design.</w:t></w:r></w:p></w:body></w:document>");
                }
            }

            var result = await _parser.ParseDocumentAsync(tempFile, "spec.docx");

            Assert.True(result.IsSuccess);
            Assert.Equal(DocumentFormat.Word, result.Format);
            Assert.Equal(2, result.RowCount);
            Assert.Contains("Architecture Specification Document", result.RawExtractedText);
            Assert.Contains("This section details the DocumentParserService design.", result.RawExtractedText);
            Assert.Contains("<word_document name=\"spec.docx\"", result.FormattedContextText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseDocumentAsync_ExcelFile_ExtractsSheetsAndSharedStrings()
    {
        var tempFile = Path.GetTempFileName() + ".xlsx";
        try
        {
            using (var zip = ZipFile.Open(tempFile, ZipArchiveMode.Create))
            {
                var ss = zip.CreateEntry("xl/sharedStrings.xml");
                using (var writer = new StreamWriter(ss.Open(), Encoding.UTF8))
                {
                    writer.Write("<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><si><t>Quarter</t></si><si><t>Revenue</t></si><si><t>Q1</t></si><si><t>Q2</t></si></sst>");
                }

                var sheet1 = zip.CreateEntry("xl/worksheets/sheet1.xml");
                using (var writer = new StreamWriter(sheet1.Open(), Encoding.UTF8))
                {
                    writer.Write("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\"><sheetData><row r=\"1\"><c r=\"A1\" t=\"s\"><v>0</v></c><c r=\"B1\" t=\"s\"><v>1</v></c></row><row r=\"2\"><c r=\"A2\" t=\"s\"><v>2</v></c><c r=\"B2\"><v>1500000</v></c></row></sheetData></worksheet>");
                }
            }

            var result = await _parser.ParseDocumentAsync(tempFile, "financials.xlsx");

            Assert.True(result.IsSuccess);
            Assert.Equal(DocumentFormat.Excel, result.Format);
            Assert.Equal(1, result.SheetCount);
            Assert.Contains("Quarter", result.RawExtractedText);
            Assert.Contains("Revenue", result.RawExtractedText);
            Assert.Contains("1500000", result.RawExtractedText);
            Assert.Contains("<excel_workbook name=\"financials.xlsx\"", result.FormattedContextText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseDocumentAsync_JsonFile_FormatsCleanTree()
    {
        var tempFile = Path.GetTempFileName() + ".json";
        try
        {
            var jsonContent = "{\"project\":\"Gram\",\"active\":true,\"metrics\":{\"speed\":100}}";
            await File.WriteAllTextAsync(tempFile, jsonContent);

            var result = await _parser.ParseDocumentAsync(tempFile, "config.json");

            Assert.True(result.IsSuccess);
            Assert.Equal(DocumentFormat.Json, result.Format);
            Assert.Contains("<json_document name=\"config.json\"", result.FormattedContextText);
            Assert.Contains("\"project\": \"Gram\"", result.FormattedContextText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseDocumentAsync_CodeFile_IdentifiesLanguageAndLineCount()
    {
        var tempFile = Path.GetTempFileName() + ".cs";
        try
        {
            var code = "public class Calculator\n{\n    public int Add(int a, int b) => a + b;\n}\n";
            await File.WriteAllTextAsync(tempFile, code);

            var result = await _parser.ParseDocumentAsync(tempFile, "Calculator.cs");

            Assert.True(result.IsSuccess);
            Assert.Equal(DocumentFormat.Code, result.Format);
            Assert.Contains("<file name=\"Calculator.cs\" language=\"cs\"", result.FormattedContextText);
            Assert.Contains("public class Calculator", result.FormattedContextText);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task ParseDocumentAsync_NonExistentFile_ReturnsSafeFailure()
    {
        var result = await _parser.ParseDocumentAsync("/non/existent/path/doc.pdf", "doc.pdf");

        Assert.False(result.IsSuccess);
        Assert.NotNull(result.ErrorMessage);
    }
}
