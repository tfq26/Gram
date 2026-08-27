using System.Text;
using System.Text.Json.Nodes;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class CodeEditingUnitTests : IDisposable
{
    private readonly string _testWorkspace;
    private readonly AgentToolRegistry _toolRegistry;
    private readonly StorageService _storageService;
    private readonly SessionStorageService _sessionStorage;
    private readonly SettingsService _settingsService;
    private readonly RepositoryMapService _repoMapService;
    private readonly ContextBuilder _contextBuilder;

    public CodeEditingUnitTests()
    {
        _testWorkspace = Path.Combine(Path.GetTempPath(), "gram_test_workspace_" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_testWorkspace);

        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GRAM_DATA_DIR"] = _testWorkspace }).Build();
        _storageService = new StorageService(config);
        _sessionStorage = new SessionStorageService(_storageService);
        _settingsService = new SettingsService(config, _storageService);
        _repoMapService = new RepositoryMapService();
        _contextBuilder = new ContextBuilder(_sessionStorage, _settingsService, _repoMapService);

        var securityService = new AgentSecurityService(_storageService);
        var gitService = new GitService(_storageService);
        var toolboxService = new ToolboxManagerService(_storageService);
        var toolKernel = new GramToolKernel(securityService, toolboxService, []);
        var webContentService = new WebContentService(new SimpleHttpClientFactory());

        _toolRegistry = new AgentToolRegistry(
            gitService,
            securityService,
            toolboxService,
            toolKernel,
            webContentService);
    }

    [Fact]
    public async Task Scenario1_AttachedCodeFile_AssemblesContext_AndEditsFileSuccessfully()
    {
        // 1. Arrange: Create a code file in the workspace
        var relativeFilePath = "Services/PaymentProcessor.cs";
        var fullPath = Path.Combine(_testWorkspace, relativeFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);

        var originalCode = """
        namespace App.Services;

        public class PaymentProcessor
        {
            public bool ProcessPayment(decimal amount)
            {
                // Insecure: no validation
                return true;
            }
        }
        """;
        await File.WriteAllTextAsync(fullPath, originalCode);

        // 2. Mock User Attaching the Code File in Conversation
        var conversation = new Conversation
        {
            Id = "conv-" + Guid.NewGuid().ToString("N")[..8],
            Title = "Fix Payment Processor"
        };
        
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(originalCode));
        var attachment = await _sessionStorage.SaveAttachmentAsync(conversation.Id, "PaymentProcessor.cs", stream, "text/plain");
        conversation.Attachments.Add(attachment);

        // 3. Verify Context Assembly parses and injects attachment
        var contextResult = await _contextBuilder.AssembleContextAsync(conversation, workspacePath: _testWorkspace);
        Assert.Contains("<workspace_attachments>", contextResult.SystemPrompt);
        Assert.Contains("PaymentProcessor.cs", contextResult.SystemPrompt);
        Assert.Contains("Insecure: no validation", contextResult.SystemPrompt);

        // 4. Mock Agent performing surgical edit_file based on attached file
        var editArgs = new JsonObject
        {
            ["filePath"] = relativeFilePath,
            ["targetContent"] = "        // Insecure: no validation\n        return true;",
            ["replacementContent"] = "        if (amount <= 0)\n        {\n            throw new ArgumentOutOfRangeException(nameof(amount), \"Amount must be positive.\");\n        }\n        return true;"
        };

        var toolResult = await _toolRegistry.ExecuteToolAsync(
            "edit_file",
            "call_1",
            editArgs.ToJsonString(),
            _testWorkspace);

        // 5. Assertions
        Assert.False(toolResult.IsError, $"Tool error: {toolResult.Output}");
        Assert.Contains("Successfully edited", toolResult.Output);

        var updatedContent = await File.ReadAllTextAsync(fullPath);
        Assert.Contains("Amount must be positive.", updatedContent);
        Assert.DoesNotContain("Insecure: no validation", updatedContent);
        Assert.Contains("public class PaymentProcessor", updatedContent);
    }

    [Fact]
    public async Task Scenario2_PastedLargeCodeChunk_WritesAndSurgicallyEdits()
    {
        // 1. Arrange: Mock user pasting a large 100+ line code chunk into a prompt
        var largeClassBuilder = new StringBuilder();
        largeClassBuilder.AppendLine("namespace App.Controllers;");
        largeClassBuilder.AppendLine("public class LargeOrderController");
        largeClassBuilder.AppendLine("{");
        for (int i = 1; i <= 60; i++)
        {
            largeClassBuilder.AppendLine($"    public void Method_{i}() => Console.WriteLine(\"Executing {i}\");");
        }
        largeClassBuilder.AppendLine("    // TARGET_BLOCK_TO_REPLACE");
        largeClassBuilder.AppendLine("    public async Task<string> ProcessOrderAsync(int orderId)");
        largeClassBuilder.AppendLine("    {");
        largeClassBuilder.AppendLine("        return \"Order \" + orderId;");
        largeClassBuilder.AppendLine("    }");
        largeClassBuilder.AppendLine("}");

        var largeOriginalCode = largeClassBuilder.ToString();
        var relativeFilePath = "Controllers/LargeOrderController.cs";

        // 2. Agent writes the initial large code chunk via write_file
        var writeArgs = new JsonObject
        {
            ["filePath"] = relativeFilePath,
            ["content"] = largeOriginalCode
        };

        var writeResult = await _toolRegistry.ExecuteToolAsync(
            "write_file",
            "call_write_1",
            writeArgs.ToJsonString(),
            _testWorkspace);

        Assert.False(writeResult.IsError);
        Assert.True(File.Exists(Path.Combine(_testWorkspace, relativeFilePath)));

        // 3. User requests a change to the ProcessOrderAsync block
        // Agent executes edit_file with precise surgical target and replacement
        var targetBlock = """
            // TARGET_BLOCK_TO_REPLACE
            public async Task<string> ProcessOrderAsync(int orderId)
            {
                return "Order " + orderId;
            }
        """;

        var replacementBlock = """
            // REPLACED_WITH_LOGGING_AND_VALIDATION
            public async Task<string> ProcessOrderAsync(int orderId)
            {
                if (orderId <= 0) throw new ArgumentException("Invalid order id");
                Console.WriteLine($"[Audit] Processing order {orderId}");
                await Task.Delay(10);
                return $"Processed: {orderId}";
            }
        """;

        var editArgs = new JsonObject
        {
            ["filePath"] = relativeFilePath,
            ["targetContent"] = targetBlock,
            ["replacementContent"] = replacementBlock
        };

        var editResult = await _toolRegistry.ExecuteToolAsync(
            "edit_file",
            "call_edit_1",
            editArgs.ToJsonString(),
            _testWorkspace);

        // 4. Assertions
        Assert.False(editResult.IsError, $"Edit failed: {editResult.Output}");
        var finalContent = await File.ReadAllTextAsync(Path.Combine(_testWorkspace, relativeFilePath));

        // Verify replacement succeeded
        Assert.Contains("REPLACED_WITH_LOGGING_AND_VALIDATION", finalContent);
        Assert.Contains("[Audit] Processing order", finalContent);
        Assert.DoesNotContain("TARGET_BLOCK_TO_REPLACE", finalContent);

        // Verify the other 60 methods are 100% intact (no truncation or corruption)
        for (int i = 1; i <= 60; i++)
        {
            Assert.Contains($"public void Method_{i}()", finalContent);
        }
    }

    [Fact]
    public async Task Scenario3_EditFile_FailsGracefully_WhenTargetNotFound()
    {
        var relativeFilePath = "Utilities/Helper.cs";
        var fullPath = Path.Combine(_testWorkspace, relativeFilePath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        await File.WriteAllTextAsync(fullPath, "public static class Helper { public static int Value = 42; }");

        // Agent tries to replace something that does not exist in the file
        var editArgs = new JsonObject
        {
            ["filePath"] = relativeFilePath,
            ["targetContent"] = "non_existent_method()",
            ["replacementContent"] = "replacement()"
        };

        var editResult = await _toolRegistry.ExecuteToolAsync(
            "edit_file",
            "call_err_1",
            editArgs.ToJsonString(),
            _testWorkspace);

        Assert.True(editResult.IsError);
        Assert.Contains("Target content was not found", editResult.Output);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testWorkspace))
            {
                Directory.Delete(_testWorkspace, recursive: true);
            }
        }
        catch {}
    }

    private sealed class SimpleHttpClientFactory : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new();
    }
}
