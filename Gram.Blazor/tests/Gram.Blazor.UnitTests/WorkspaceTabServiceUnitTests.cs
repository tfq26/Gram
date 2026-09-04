using System;
using System.Collections.Generic;
using System.IO;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gram.Blazor.UnitTests;

public sealed class WorkspaceTabServiceUnitTests : IDisposable
{
    private readonly string _tempDir;
    private readonly StorageService _storageService;

    public WorkspaceTabServiceUnitTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "GramTabsTest_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_tempDir);
        var config = new Microsoft.Extensions.Configuration.ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["GRAM_DATA_DIR"] = _tempDir })
            .Build();
        _storageService = new StorageService(config);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch {}
    }

    [Fact]
    public void ToggleSplitView_EnablesAndDisablesSplitMode()
    {
        var tabService = new WorkspaceTabService(_storageService);
        Assert.False(tabService.IsSplitView);
        Assert.Null(tabService.SecondaryTabId);

        tabService.ToggleSplitView();
        Assert.True(tabService.IsSplitView);
        Assert.NotNull(tabService.SecondaryTabId);

        tabService.ToggleSplitView();
        Assert.False(tabService.IsSplitView);
        Assert.Null(tabService.SecondaryTabId);
    }

    [Fact]
    public void SwapSplitTabs_SwapsActiveAndSecondaryTab()
    {
        var tabService = new WorkspaceTabService(_storageService);
        tabService.OpenChatTab("chat-1", "Chat 1");
        tabService.OpenChatTab("chat-2", "Chat 2");
        tabService.SelectTab(tabService.OpenTabs[0].Id);

        tabService.ToggleSplitView(tabService.OpenTabs[1].Id);
        Assert.True(tabService.IsSplitView);

        var primaryBefore = tabService.ActiveTabId;
        var secondaryBefore = tabService.SecondaryTabId;

        tabService.SwapSplitTabs();

        Assert.Equal(secondaryBefore, tabService.ActiveTabId);
        Assert.Equal(primaryBefore, tabService.SecondaryTabId);
    }

    [Fact]
    public void RequestCrossChatMessage_FiresOnCrossChatRequestedEvent()
    {
        var tabService = new WorkspaceTabService(_storageService);
        string? receivedFrom = null;
        string? receivedTo = null;
        string? receivedText = null;

        tabService.OnCrossChatRequested += (from, to, text) =>
        {
            receivedFrom = from;
            receivedTo = to;
            receivedText = text;
        };

        tabService.RequestCrossChatMessage("chat-a", "chat-b", "Hello from Gram A");

        Assert.Equal("chat-a", receivedFrom);
        Assert.Equal("chat-b", receivedTo);
        Assert.Equal("Hello from Gram A", receivedText);
    }

    [Fact]
    public void ReorderTab_ReordersTabsInOpenTabsList()
    {
        var tabService = new WorkspaceTabService(_storageService);
        tabService.OpenChatTab("chat-1", "Chat 1");
        tabService.OpenChatTab("chat-2", "Chat 2");
        tabService.OpenChatTab("chat-3", "Chat 3");

        var tab1Id = tabService.OpenTabs[0].Id;
        var tab2Id = tabService.OpenTabs[1].Id;
        var tab3Id = tabService.OpenTabs[2].Id;

        // Move tab3 to position of tab1
        tabService.ReorderTab(tab3Id, tab1Id);

        Assert.Equal(tab3Id, tabService.OpenTabs[0].Id);
        Assert.Equal(tab1Id, tabService.OpenTabs[1].Id);
        Assert.Equal(tab2Id, tabService.OpenTabs[2].Id);
    }

    [Fact]
    public void SetDraggingTab_And_SplitWithDraggedTab_ActivatesSplitMode()
    {
        var tabService = new WorkspaceTabService(_storageService);
        tabService.OpenChatTab("chat-1", "Chat 1");
        tabService.OpenChatTab("chat-2", "Chat 2");

        var tab2Id = tabService.OpenTabs[1].Id;

        bool dragStateNotified = false;
        tabService.OnDragStateChanged += () => dragStateNotified = true;

        tabService.SetDraggingTab(tab2Id);
        Assert.True(tabService.IsDraggingTab);
        Assert.Equal(tab2Id, tabService.DraggedTabId);
        Assert.True(dragStateNotified);

        tabService.SplitWithDraggedTab();
        Assert.False(tabService.IsDraggingTab);
        Assert.Null(tabService.DraggedTabId);
        Assert.True(tabService.IsSplitView);
        Assert.Equal(tab2Id, tabService.SecondaryTabId);
    }

    [Fact]
    public void TestSystemKeysVsUserKeysSeparation()
    {
        var modelService = new ModelService(_storageService);

        // 1. Before any user key is set
        Assert.False(modelService.HasUserKey("groq"));
        Assert.Null(modelService.GetUserApiKey("groq"));
        Assert.False(modelService.HasUserKey("google"));
        Assert.Null(modelService.GetUserApiKey("google"));

        // 2. Setting a user Groq key routes strictly to the user key
        modelService.SetApiKey("groq", "gsk_custom_user_groq_key");
        Assert.True(modelService.HasUserKey("groq"));
        Assert.Equal("gsk_custom_user_groq_key", modelService.GetUserApiKey("groq"));
        Assert.Equal("gsk_custom_user_groq_key", modelService.GetApiKey("groq"));

        // 3. Setting a user Google (Gemini) key routes strictly to the user key
        modelService.SetApiKey("google", "AIzaSyCustomUserGeminiKey");
        Assert.True(modelService.HasUserKey("google"));
        Assert.Equal("AIzaSyCustomUserGeminiKey", modelService.GetUserApiKey("google"));
        Assert.Equal("AIzaSyCustomUserGeminiKey", modelService.GetApiKey("google"));

        // 4. Clearing user keys restores clean fallback state
        modelService.SetApiKey("groq", null);
        modelService.SetApiKey("google", null);
        Assert.False(modelService.HasUserKey("groq"));
        Assert.Null(modelService.GetUserApiKey("groq"));
        Assert.False(modelService.HasUserKey("google"));
        Assert.Null(modelService.GetUserApiKey("google"));
    }

    [Fact]
    public void TestExtractUrls_And_UrlCleaning()
    {
        var sampleText = "Check documentation at https://dotnet.microsoft.com/en-us/ and https://github.com/dotnet/aspnetcore";
        var urls = WebContentService.ExtractUrls(sampleText);

        Assert.Equal(2, urls.Count);
        Assert.Contains("https://dotnet.microsoft.com/en-us/", urls);
        Assert.Contains("https://github.com/dotnet/aspnetcore", urls);
    }

    [Fact]
    public void MarkdownService_RendersMathAndAdvancedExtensions()
    {
        var markdownService = new MarkdownService();

        // 1. Math formulas (inline and block)
        var mathMarkdown = "Energy equation: $E = mc^2$\n\n$$\\int_0^\\infty e^{-x} dx = 1$$";
        var mathHtml = markdownService.RenderToHtml(mathMarkdown);
        Assert.Contains("math", mathHtml);
        Assert.Contains("E = mc^2", mathHtml);

        // 2. Subscript, Superscript, and Strikethrough
        var extraMarkdown = "Water H~2~O, Power x^2^, and ~~deprecated~~";
        var extraHtml = markdownService.RenderToHtml(extraMarkdown);
        Assert.Contains("<sub>2</sub>", extraHtml);
        Assert.Contains("<sup>2</sup>", extraHtml);
        Assert.Contains("<del>deprecated</del>", extraHtml);

        // 3. Tables
        var tableMarkdown = "| Syntax | Description |\n| ----------- | ----------- |\n| Header | Title |";
        var tableHtml = markdownService.RenderToHtml(tableMarkdown);
        Assert.Contains("<table>", tableHtml);
        Assert.Contains("<th>Syntax</th>", tableHtml);

        // 4. Mermaid Diagrams
        var mermaidMarkdown = "```mermaid\ngraph TD;\n    A-->B;\n```";
        var mermaidHtml = markdownService.RenderToHtml(mermaidMarkdown);
        Assert.Contains("mermaid-wrapper", mermaidHtml);
        Assert.Contains("class=\"mermaid\"", mermaidHtml);
        Assert.Contains("A-->B;", mermaidHtml);
    }

    [Fact]
    public void MarkdownService_PreservesSoftBreaks_AndProviderEscapedNewlines()
    {
        var markdownService = new MarkdownService();

        var softBreakHtml = markdownService.RenderToHtml("First line\nSecond line");
        Assert.Contains("First line<br", softBreakHtml);
        Assert.Contains("Second line", softBreakHtml);

        var escapedHtml = markdownService.RenderToHtml("**Heading**\\n\\n- One\\n- Two");
        Assert.Contains("<strong>Heading</strong>", escapedHtml);
        Assert.Contains("<ul>", escapedHtml);
        Assert.Contains("<li>One</li>", escapedHtml);
        Assert.Contains("<li>Two</li>", escapedHtml);
    }

    [Fact]
    public void MarkdownService_NormalizesProviderMathDelimiters()
    {
        var markdownService = new MarkdownService();
        var markdown = "Quadratic formula\\n\\n[ ax^{2}+bx+c = 0 ]\\n\\nThe solution is (x) and (\\Delta > 0).\\n\\n[ \\boxed{,x = \\frac{-b ;\\pm; \\sqrt{,b^{2} - 4ac,}}{2a},} ]";

        var html = markdownService.RenderToHtml(markdown);

        Assert.Contains("math", html);
        Assert.Contains("boxed", html);
        Assert.Contains("frac", html);
        Assert.Contains("pm", html);
        Assert.DoesNotContain(";\\pm;", html);
    }

    [Fact]
    public void MarkdownService_RepairsEscapedBoxedClosingBrace()
    {
        var markdownService = new MarkdownService();
        var markdown = @"\[ \boxed{\,x = \frac{-b \;\pm\; \sqrt{\,b^{2} - 4ac\,}}{2a}\} \]";

        var html = markdownService.RenderToHtml(markdown);

        Assert.Contains("math", html);
        Assert.DoesNotContain("\\}", html);
    }

    [Fact]
    public void MarkdownService_NormalizesUnicodeMinusInsideFractions()
    {
        var markdownService = new MarkdownService();
        var markdown = @"\[x = \frac{-−4−4 \pm \sqrt{64}}{222} = \frac{4 \pm 8}{4}\]";

        var html = markdownService.RenderToHtml(markdown);

        Assert.Contains("math", html);
        Assert.DoesNotContain("−", html);
        Assert.Contains("\\frac", html);
    }

    [Fact]
    public void CloseTabsForChat_ClosesAllAssociatedTabsAndResetsSplitModeIfNecessary()
    {
        var tabService = new WorkspaceTabService(_storageService);
        tabService.OpenChatTab("chat-a", "Chat A");
        tabService.OpenChatTab("chat-b", "Chat B");
        tabService.OpenChatTab("chat-c", "Chat C");

        Assert.Contains(tabService.OpenTabs, t => t.ChatId == "chat-b");

        // Set up split view with chat-b as secondary
        var tabB = tabService.OpenTabs.First(t => t.ChatId == "chat-b");
        tabService.ToggleSplitView(tabB.Id);
        Assert.True(tabService.IsSplitView);
        Assert.Equal(tabB.Id, tabService.SecondaryTabId);

        // Delete chat-b -> tabs should be closed and split view reset
        tabService.CloseTabsForChat("chat-b");

        Assert.DoesNotContain(tabService.OpenTabs, t => t.ChatId == "chat-b");
        Assert.False(tabService.IsSplitView);
        Assert.Null(tabService.SecondaryTabId);
    }

    [Fact]
    public void SessionStateRestoration_PreservesActiveTabAndSplitStateAcrossRestarts()
    {
        // 1. First session opens tabs, sets specific active tab, and enables split view
        var tabService1 = new WorkspaceTabService(_storageService);
        tabService1.OpenChatTab("chat-1", "Chat 1");
        tabService1.OpenChatTab("chat-2", "Chat 2");
        tabService1.OpenChatTab("chat-3", "Chat 3");

        var tab2 = tabService1.OpenTabs.First(t => t.ChatId == "chat-2");
        var tab3 = tabService1.OpenTabs.First(t => t.ChatId == "chat-3");

        // Select tab-2 as active tab
        tabService1.SelectTab(tab2.Id);
        Assert.Equal(tab2.Id, tabService1.ActiveTabId);

        // Split with tab-3 as secondary
        tabService1.ToggleSplitView(tab3.Id);
        Assert.True(tabService1.IsSplitView);
        Assert.Equal(tab3.Id, tabService1.SecondaryTabId);

        // 2. Second session (simulating restart / reopening after crash)
        var tabService2 = new WorkspaceTabService(_storageService);

        Assert.Equal(tabService1.OpenTabs.Count, tabService2.OpenTabs.Count);
        Assert.Equal(tab2.Id, tabService2.ActiveTabId);
        Assert.True(tabService2.IsSplitView);
        Assert.Equal(tab3.Id, tabService2.SecondaryTabId);
    }

    [Fact]
    public void SessionStateRestoration_HandlesLegacyJsonArrayBackwardsCompatibly()
    {
        // Write raw JSON array to open_tabs.json (older storage format)
        var tabsFilePath = _storageService.GetFilePath("open_tabs.json");
        var legacyJson = "[{\"Id\":\"legacy-1\",\"Title\":\"Legacy Chat 1\",\"Type\":0,\"ChatId\":\"c-1\"},{\"Id\":\"legacy-2\",\"Title\":\"Legacy Chat 2\",\"Type\":0,\"ChatId\":\"c-2\"}]";
        File.WriteAllText(tabsFilePath, legacyJson);

        var tabService = new WorkspaceTabService(_storageService);

        Assert.Equal(2, tabService.OpenTabs.Count);
        Assert.Equal("legacy-1", tabService.ActiveTabId);
        Assert.Equal("Legacy Chat 1", tabService.OpenTabs[0].Title);
        Assert.Equal("Legacy Chat 2", tabService.OpenTabs[1].Title);
    }
}
