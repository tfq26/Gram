using System.Collections.Concurrent;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Content;

namespace Gram.Blazor.Shared;

public enum DocumentFormat
{
    Pdf,
    PowerPoint,
    Word,
    Excel,
    Csv,
    Json,
    Code,
    Markdown,
    Text,
    Image,
    Unknown
}

public sealed class ParsedDocumentResult
{
    public string FileName { get; set; } = string.Empty;
    public string Extension { get; set; } = string.Empty;
    public DocumentFormat Format { get; set; } = DocumentFormat.Unknown;
    public string ContentType { get; set; } = "application/octet-stream";
    public long FileSizeBytes { get; set; }
    public int PageCount { get; set; }
    public int SlideCount { get; set; }
    public int SheetCount { get; set; }
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public int CharacterCount { get; set; }
    public int EstimatedTokens => Math.Max(1, CharacterCount / 4);

    public string? DocumentTitle { get; set; }
    public string? DocumentAuthor { get; set; }

    public List<DocumentPageItem> Pages { get; set; } = [];
    public List<DocumentSlideItem> Slides { get; set; } = [];
    public List<DocumentSheetItem> Sheets { get; set; } = [];
    public List<string> CsvHeaders { get; set; } = [];
    public List<List<string>> CsvRows { get; set; } = [];

    public string RawExtractedText { get; set; } = string.Empty;
    public string FormattedContextText { get; set; } = string.Empty;

    public bool IsSuccess { get; set; } = true;
    public string? ErrorMessage { get; set; }
}

public sealed class DocumentPageItem
{
    public int PageNumber { get; set; }
    public string Text { get; set; } = string.Empty;
    public int WordCount { get; set; }
}

public sealed class DocumentSlideItem
{
    public int SlideNumber { get; set; }
    public string? Title { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? SpeakerNotes { get; set; }
}

public sealed class DocumentSheetItem
{
    public string SheetName { get; set; } = string.Empty;
    public int RowCount { get; set; }
    public int ColumnCount { get; set; }
    public List<string> Headers { get; set; } = [];
    public List<List<string>> Rows { get; set; } = [];
}

public sealed class DocumentParserService
{
    private readonly ConcurrentDictionary<string, (DateTime LastModified, ParsedDocumentResult Result)> _cache = new();

    public async Task<ParsedDocumentResult> ParseDocumentAsync(string filePath, string fileName, string? contentType = null)
    {
        if (!File.Exists(filePath))
        {
            return new ParsedDocumentResult
            {
                FileName = fileName,
                Extension = Path.GetExtension(fileName).ToLowerInvariant(),
                IsSuccess = false,
                ErrorMessage = $"File not found at path: {filePath}"
            };
        }

        var lastWrite = File.GetLastWriteTimeUtc(filePath);
        var cacheKey = $"{filePath}_{fileName}";

        if (_cache.TryGetValue(cacheKey, out var cached) && cached.LastModified == lastWrite)
        {
            return cached.Result;
        }

        var fi = new FileInfo(filePath);
        var ext = Path.GetExtension(fileName).ToLowerInvariant();
        var format = DetectFormat(ext, contentType);

        var result = new ParsedDocumentResult
        {
            FileName = fileName,
            Extension = ext,
            Format = format,
            ContentType = contentType ?? GetDefaultContentType(ext),
            FileSizeBytes = fi.Length
        };

        try
        {
            switch (format)
            {
                case DocumentFormat.Pdf:
                    ParsePdf(filePath, result);
                    break;
                case DocumentFormat.PowerPoint:
                    await ParsePowerPointAsync(filePath, result);
                    break;
                case DocumentFormat.Word:
                    await ParseWordAsync(filePath, result);
                    break;
                case DocumentFormat.Excel:
                    await ParseExcelAsync(filePath, result);
                    break;
                case DocumentFormat.Csv:
                    await ParseCsvAsync(filePath, result);
                    break;
                case DocumentFormat.Json:
                    await ParseJsonAsync(filePath, result);
                    break;
                case DocumentFormat.Markdown:
                case DocumentFormat.Code:
                case DocumentFormat.Text:
                    await ParseTextOrCodeAsync(filePath, result);
                    break;
                case DocumentFormat.Image:
                    ParseImageMetadata(result);
                    break;
                default:
                    await ParseGenericAsync(filePath, result);
                    break;
            }

            _cache[cacheKey] = (lastWrite, result);
            return result;
        }
        catch (Exception ex)
        {
            result.IsSuccess = false;
            result.ErrorMessage = $"Document parsing notice: {ex.Message}";
            result.FormattedContextText = $"<attached_document name=\"{fileName}\" type=\"{format.ToString().ToLowerInvariant()}\" size=\"{fi.Length}B\" error=\"{ex.Message}\" />";
            return result;
        }
    }

    private static void ParsePdf(string filePath, ParsedDocumentResult result)
    {
        using var pdf = PdfDocument.Open(filePath);
        result.PageCount = pdf.NumberOfPages;
        result.DocumentTitle = pdf.Information?.Title;
        result.DocumentAuthor = pdf.Information?.Author;

        var sbContext = new StringBuilder();
        var sbRaw = new StringBuilder();

        sbContext.AppendLine($"<pdf_document name=\"{result.FileName}\" pages=\"{pdf.NumberOfPages}\" size=\"{result.FileSizeBytes}B\">");

        if (!string.IsNullOrWhiteSpace(result.DocumentTitle))
        {
            sbContext.AppendLine($"<metadata title=\"{result.DocumentTitle}\" author=\"{result.DocumentAuthor}\" />");
        }

        foreach (var page in pdf.GetPages())
        {
            var pageText = CleanExtractedText(page.Text);
            var pageItem = new DocumentPageItem
            {
                PageNumber = page.Number,
                Text = pageText,
                WordCount = string.IsNullOrWhiteSpace(pageText) ? 0 : pageText.Split([' ', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Length
            };

            result.Pages.Add(pageItem);
            sbRaw.AppendLine($"--- Page {page.Number} ---");
            sbRaw.AppendLine(pageText);

            sbContext.AppendLine($"<page number=\"{page.Number}\">");
            sbContext.AppendLine(pageText);
            sbContext.AppendLine("</page>");
        }

        sbContext.AppendLine("</pdf_document>");

        result.RawExtractedText = sbRaw.ToString();
        result.FormattedContextText = sbContext.ToString();
        result.CharacterCount = result.RawExtractedText.Length;
    }

    private static async Task ParsePowerPointAsync(string filePath, ParsedDocumentResult result)
    {
        using var archive = ZipFile.OpenRead(filePath);
        
        // Find slide XML entries ordered by number
        var slideEntries = archive.Entries
            .Where(e => Regex.IsMatch(e.FullName, @"^ppt/slides/slide\d+\.xml$", RegexOptions.IgnoreCase))
            .OrderBy(e => ExtractNumberFromPath(e.FullName))
            .ToList();

        result.SlideCount = slideEntries.Count;
        var sbContext = new StringBuilder();
        var sbRaw = new StringBuilder();

        sbContext.AppendLine($"<powerpoint_presentation name=\"{result.FileName}\" slides=\"{slideEntries.Count}\" size=\"{result.FileSizeBytes}B\">");

        int slideIdx = 1;
        foreach (var entry in slideEntries)
        {
            await using var stream = entry.Open();
            var xdoc = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);
            
            var textNodes = xdoc.Descendants()
                .Where(e => e.Name.LocalName == "p" || e.Name.LocalName == "t")
                .ToList();

            var paragraphs = new List<string>();
            foreach (var pNode in xdoc.Descendants().Where(e => e.Name.LocalName == "p"))
            {
                var pText = string.Concat(pNode.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value)).Trim();
                if (!string.IsNullOrWhiteSpace(pText))
                {
                    paragraphs.Add(pText);
                }
            }

            var slideTitle = paragraphs.FirstOrDefault() ?? $"Slide {slideIdx}";
            var slideBody = string.Join("\n• ", paragraphs.Skip(1));
            var fullSlideText = string.Join("\n", paragraphs);

            // Check for speaker notes
            var notesEntry = archive.Entries.FirstOrDefault(e => e.FullName.Equals($"ppt/notesSlides/notesSlide{slideIdx}.xml", StringComparison.OrdinalIgnoreCase));
            string? notesText = null;
            if (notesEntry != null)
            {
                await using var noteStream = notesEntry.Open();
                var noteDoc = await XDocument.LoadAsync(noteStream, LoadOptions.None, CancellationToken.None);
                var noteParas = noteDoc.Descendants()
                    .Where(e => e.Name.LocalName == "p")
                    .Select(p => string.Concat(p.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value)).Trim())
                    .Where(s => !string.IsNullOrWhiteSpace(s) && !s.StartsWith("Slide ", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (noteParas.Count > 0) notesText = string.Join(" ", noteParas);
            }

            var slideItem = new DocumentSlideItem
            {
                SlideNumber = slideIdx,
                Title = slideTitle,
                Content = fullSlideText,
                SpeakerNotes = notesText
            };
            result.Slides.Add(slideItem);

            sbRaw.AppendLine($"--- Slide {slideIdx}: {slideTitle} ---");
            sbRaw.AppendLine(fullSlideText);
            if (!string.IsNullOrWhiteSpace(notesText)) sbRaw.AppendLine($"[Notes]: {notesText}");

            sbContext.AppendLine($"<slide number=\"{slideIdx}\" title=\"{slideTitle}\">");
            if (!string.IsNullOrWhiteSpace(slideBody)) sbContext.AppendLine("• " + slideBody);
            if (!string.IsNullOrWhiteSpace(notesText)) sbContext.AppendLine($"<speaker_notes>{notesText}</speaker_notes>");
            sbContext.AppendLine("</slide>");

            slideIdx++;
        }

        sbContext.AppendLine("</powerpoint_presentation>");

        result.RawExtractedText = sbRaw.ToString();
        result.FormattedContextText = sbContext.ToString();
        result.CharacterCount = result.RawExtractedText.Length;
    }

    private static async Task ParseWordAsync(string filePath, ParsedDocumentResult result)
    {
        using var archive = ZipFile.OpenRead(filePath);
        var docEntry = archive.Entries.FirstOrDefault(e => e.FullName.Equals("word/document.xml", StringComparison.OrdinalIgnoreCase));
        
        if (docEntry == null)
        {
            result.FormattedContextText = $"<word_document name=\"{result.FileName}\" error=\"word/document.xml not found\" />";
            return;
        }

        await using var stream = docEntry.Open();
        var xdoc = await XDocument.LoadAsync(stream, LoadOptions.None, CancellationToken.None);

        var paragraphs = new List<string>();
        foreach (var pNode in xdoc.Descendants().Where(e => e.Name.LocalName == "p"))
        {
            var pText = string.Concat(pNode.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value)).Trim();
            if (!string.IsNullOrWhiteSpace(pText))
            {
                paragraphs.Add(pText);
            }
        }

        result.RawExtractedText = string.Join("\n\n", paragraphs);
        result.CharacterCount = result.RawExtractedText.Length;
        result.RowCount = paragraphs.Count;

        var sbContext = new StringBuilder();
        sbContext.AppendLine($"<word_document name=\"{result.FileName}\" paragraphs=\"{paragraphs.Count}\" size=\"{result.FileSizeBytes}B\">");
        if (result.RawExtractedText.Length > 40000)
        {
            sbContext.AppendLine(result.RawExtractedText[..40000] + $"\n... [{result.RawExtractedText.Length - 40000} characters truncated for token preservation]");
        }
        else
        {
            sbContext.AppendLine(result.RawExtractedText);
        }
        sbContext.AppendLine("</word_document>");

        result.FormattedContextText = sbContext.ToString();
    }

    private static async Task ParseExcelAsync(string filePath, ParsedDocumentResult result)
    {
        using var archive = ZipFile.OpenRead(filePath);

        // 1. Read shared strings
        var sharedStrings = new List<string>();
        var sharedEntry = archive.Entries.FirstOrDefault(e => e.FullName.Equals("xl/sharedStrings.xml", StringComparison.OrdinalIgnoreCase));
        if (sharedEntry != null)
        {
            await using var ssStream = sharedEntry.Open();
            var ssDoc = await XDocument.LoadAsync(ssStream, LoadOptions.None, CancellationToken.None);
            foreach (var si in ssDoc.Descendants().Where(e => e.Name.LocalName == "si"))
            {
                var text = string.Concat(si.Descendants().Where(e => e.Name.LocalName == "t").Select(t => t.Value));
                sharedStrings.Add(text);
            }
        }

        // 2. Read sheet files
        var sheetEntries = archive.Entries
            .Where(e => Regex.IsMatch(e.FullName, @"^xl/worksheets/sheet\d+\.xml$", RegexOptions.IgnoreCase))
            .OrderBy(e => ExtractNumberFromPath(e.FullName))
            .ToList();

        result.SheetCount = sheetEntries.Count;
        var sbContext = new StringBuilder();
        var sbRaw = new StringBuilder();

        sbContext.AppendLine($"<excel_workbook name=\"{result.FileName}\" sheets=\"{sheetEntries.Count}\" size=\"{result.FileSizeBytes}B\">");

        int sheetIdx = 1;
        foreach (var entry in sheetEntries)
        {
            await using var sStream = entry.Open();
            var sDoc = await XDocument.LoadAsync(sStream, LoadOptions.None, CancellationToken.None);
            var sheetName = $"Sheet{sheetIdx}";

            var rowsData = new List<List<string>>();
            foreach (var rowNode in sDoc.Descendants().Where(e => e.Name.LocalName == "row"))
            {
                var rowCells = new List<string>();
                foreach (var cNode in rowNode.Descendants().Where(e => e.Name.LocalName == "c"))
                {
                    var isShared = cNode.Attribute("t")?.Value == "s";
                    var valNode = cNode.Descendants().FirstOrDefault(e => e.Name.LocalName == "v");
                    var rawVal = valNode?.Value ?? "";

                    if (isShared && int.TryParse(rawVal, out var sIdx) && sIdx >= 0 && sIdx < sharedStrings.Count)
                    {
                        rowCells.Add(sharedStrings[sIdx]);
                    }
                    else
                    {
                        rowCells.Add(rawVal);
                    }
                }

                if (rowCells.Any(c => !string.IsNullOrWhiteSpace(c)))
                {
                    rowsData.Add(rowCells);
                }
            }

            var sheetItem = new DocumentSheetItem
            {
                SheetName = sheetName,
                RowCount = rowsData.Count,
                ColumnCount = rowsData.Count > 0 ? rowsData[0].Count : 0,
                Headers = rowsData.Count > 0 ? rowsData[0] : [],
                Rows = rowsData.Skip(1).Take(150).ToList()
            };
            result.Sheets.Add(sheetItem);

            sbContext.AppendLine($"<sheet name=\"{sheetName}\" rows=\"{rowsData.Count}\">");
            if (rowsData.Count > 0)
            {
                var headers = rowsData[0];
                sbContext.AppendLine("| " + string.Join(" | ", headers) + " |");
                sbContext.AppendLine("| " + string.Join(" | ", headers.Select(_ => "---")) + " |");

                foreach (var r in rowsData.Skip(1).Take(50))
                {
                    sbContext.AppendLine("| " + string.Join(" | ", r.Select(c => c.Replace("\n", " ").Replace("|", "/"))) + " |");
                }

                if (rowsData.Count > 51)
                {
                    sbContext.AppendLine($"... [{rowsData.Count - 51} more rows in sheet]");
                }
            }
            sbContext.AppendLine("</sheet>");

            sbRaw.AppendLine($"=== Sheet: {sheetName} ===");
            foreach (var r in rowsData.Take(100))
            {
                sbRaw.AppendLine(string.Join("\t", r));
            }

            sheetIdx++;
        }

        sbContext.AppendLine("</excel_workbook>");

        result.RawExtractedText = sbRaw.ToString();
        result.FormattedContextText = sbContext.ToString();
        result.CharacterCount = result.RawExtractedText.Length;
    }

    private static async Task ParseCsvAsync(string filePath, ParsedDocumentResult result)
    {
        var lines = await File.ReadAllLinesAsync(filePath, Encoding.UTF8);
        result.RowCount = lines.Length;

        if (lines.Length == 0)
        {
            result.FormattedContextText = $"<csv_document name=\"{result.FileName}\" rows=\"0\" columns=\"0\" />";
            return;
        }

        // Parse header
        var delimiter = DetectDelimiter(lines[0]);
        var headers = SplitCsvLine(lines[0], delimiter);
        result.CsvHeaders = headers;
        result.ColumnCount = headers.Count;

        var sbContext = new StringBuilder();
        sbContext.AppendLine($"<tabular_data name=\"{result.FileName}\" format=\"csv\" rows=\"{lines.Length - 1}\" columns=\"{headers.Count}\">");
        sbContext.AppendLine("Schema: " + string.Join(" | ", headers));
        sbContext.AppendLine();
        sbContext.AppendLine("Data Preview (Markdown Table):");
        sbContext.AppendLine("| " + string.Join(" | ", headers) + " |");
        sbContext.AppendLine("| " + string.Join(" | ", headers.Select(_ => "---")) + " |");

        // Keep up to 200 rows for display / preview
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var row = SplitCsvLine(lines[i], delimiter);
            if (result.CsvRows.Count < 250)
            {
                result.CsvRows.Add(row);
            }

            if (i <= 60) // Inject top 60 rows directly to LLM context
            {
                sbContext.AppendLine("| " + string.Join(" | ", row.Select(c => c.Replace("\n", " ").Replace("|", "/"))) + " |");
            }
        }

        if (lines.Length > 61)
        {
            sbContext.AppendLine($"... [{lines.Length - 61} more rows truncated for context budget]");
        }

        sbContext.AppendLine("</tabular_data>");

        result.RawExtractedText = string.Join("\n", lines);
        result.FormattedContextText = sbContext.ToString();
        result.CharacterCount = result.RawExtractedText.Length;
    }

    private static async Task ParseJsonAsync(string filePath, ParsedDocumentResult result)
    {
        var rawJson = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
        result.RawExtractedText = rawJson;
        result.CharacterCount = rawJson.Length;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var pretty = JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });

            var sbContext = new StringBuilder();
            sbContext.AppendLine($"<json_document name=\"{result.FileName}\" size=\"{result.FileSizeBytes}B\">");
            
            // Protect token context cap at 30k chars
            if (pretty.Length > 30000)
            {
                sbContext.AppendLine(pretty[..30000] + "\n... [remaining JSON truncated]");
            }
            else
            {
                sbContext.AppendLine(pretty);
            }
            sbContext.AppendLine("</json_document>");

            result.FormattedContextText = sbContext.ToString();
        }
        catch
        {
            result.FormattedContextText = $"<json_document name=\"{result.FileName}\">\n{rawJson}\n</json_document>";
        }
    }

    private static async Task ParseTextOrCodeAsync(string filePath, ParsedDocumentResult result)
    {
        var text = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
        result.RawExtractedText = text;
        result.CharacterCount = text.Length;
        result.RowCount = text.Split('\n').Length;

        var lang = result.Extension.TrimStart('.');
        var sbContext = new StringBuilder();
        sbContext.AppendLine($"<file name=\"{result.FileName}\" language=\"{lang}\" lines=\"{result.RowCount}\" size=\"{result.FileSizeBytes}B\">");
        
        if (text.Length > 40000)
        {
            sbContext.AppendLine(text[..40000] + $"\n... [{text.Length - 40000} characters truncated for token preservation]");
        }
        else
        {
            sbContext.AppendLine(text);
        }
        sbContext.AppendLine("</file>");

        result.FormattedContextText = sbContext.ToString();
    }

    private static void ParseImageMetadata(ParsedDocumentResult result)
    {
        result.FormattedContextText = $"<image_attachment name=\"{result.FileName}\" format=\"{result.ContentType}\" size=\"{result.FileSizeBytes}B\" note=\"Visual screenshot/image inspected by multimodal models.\" />";
    }

    private static async Task ParseGenericAsync(string filePath, ParsedDocumentResult result)
    {
        try
        {
            var text = await File.ReadAllTextAsync(filePath, Encoding.UTF8);
            result.RawExtractedText = text;
            result.CharacterCount = text.Length;
            result.FormattedContextText = $"<file name=\"{result.FileName}\" size=\"{result.FileSizeBytes}B\">\n{text}\n</file>";
        }
        catch
        {
            result.FormattedContextText = $"<binary_attachment name=\"{result.FileName}\" size=\"{result.FileSizeBytes}B\" format=\"{result.ContentType}\" />";
        }
    }

    private static DocumentFormat DetectFormat(string ext, string? contentType)
    {
        return ext switch
        {
            ".pdf" => DocumentFormat.Pdf,
            ".pptx" or ".ppt" => DocumentFormat.PowerPoint,
            ".docx" or ".doc" => DocumentFormat.Word,
            ".xlsx" or ".xls" => DocumentFormat.Excel,
            ".csv" or ".tsv" => DocumentFormat.Csv,
            ".json" or ".jsonl" => DocumentFormat.Json,
            ".md" or ".markdown" => DocumentFormat.Markdown,
            ".png" or ".jpg" or ".jpeg" or ".webp" or ".svg" or ".gif" => DocumentFormat.Image,
            ".cs" or ".ts" or ".js" or ".py" or ".cpp" or ".c" or ".h" or ".sql" or ".html" or ".css" or ".razor" or ".sh" or ".go" or ".rs" or ".xml" or ".yaml" or ".yml" or ".swift" or ".kt" or ".dart" or ".php" or ".rb" or ".java" => DocumentFormat.Code,
            ".txt" or ".log" or ".rtf" or ".diff" or ".patch" => DocumentFormat.Text,
            _ => DocumentFormat.Unknown
        };
    }

    private static string GetDefaultContentType(string ext) => ext switch
    {
        ".pdf" => "application/pdf",
        ".pptx" => "application/vnd.openxmlformats-officedocument.presentationml.presentation",
        ".docx" => "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        ".xlsx" => "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        ".csv" => "text/csv",
        ".tsv" => "text/tab-separated-values",
        ".json" => "application/json",
        ".png" => "image/png",
        ".jpg" or ".jpeg" => "image/jpeg",
        ".webp" => "image/webp",
        ".svg" => "image/svg+xml",
        ".md" => "text/markdown",
        ".txt" => "text/plain",
        _ => "application/octet-stream"
    };

    private static int ExtractNumberFromPath(string path)
    {
        var match = Regex.Match(path, @"\d+");
        return match.Success && int.TryParse(match.Value, out var num) ? num : 0;
    }

    private static char DetectDelimiter(string headerLine)
    {
        if (headerLine.Contains('\t')) return '\t';
        if (headerLine.Contains(';') && !headerLine.Contains(',')) return ';';
        return ',';
    }

    private static List<string> SplitCsvLine(string line, char delimiter)
    {
        var result = new List<string>();
        var current = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (inQuotes && i + 1 < line.Length && line[i + 1] == '"')
                {
                    current.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
            }
            else if (c == delimiter && !inQuotes)
            {
                result.Add(current.ToString().Trim());
                current.Clear();
            }
            else
            {
                current.Append(c);
            }
        }

        result.Add(current.ToString().Trim());
        return result;
    }

    private static string CleanExtractedText(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        var cleaned = raw.Replace("\0", string.Empty);
        return cleaned.Trim();
    }
}
