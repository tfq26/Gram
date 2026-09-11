using System.IO;
using System.Runtime.InteropServices;

namespace Gram.Desktop.Services;

/// <summary>
/// Native macOS integration layer providing:
/// 1. NSProcessInfo process name ("Gram" instead of "dotnet" / "Gram.Desktop")
/// 2. Dock icon & window icon management via AppKit
/// 3. Cocoa Main Menu Bar (App, File, Edit, View, Window) with standard WebKit responder chain selectors
/// 4. Top-right Menu Bar Status Item (Tray) with quick actions
/// </summary>
public static class MacOsIntegration
{
    private const string LibObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(LibObjC)]
    private static extern IntPtr objc_getClass(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr sel_registerName(string name);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, IntPtr arg1, IntPtr arg2, IntPtr arg3);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, double arg1);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, ulong arg1);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_msgSend(IntPtr receiver, IntPtr selector, bool arg1);

    [DllImport(LibObjC)]
    private static extern IntPtr objc_allocateClassPair(IntPtr superclass, string name, IntPtr extraBytes);

    [DllImport(LibObjC)]
    private static extern void objc_registerClassPair(IntPtr cls);

    [DllImport(LibObjC)]
    private static extern bool class_addMethod(IntPtr cls, IntPtr name, Delegate imp, string types);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate void ActionCallback(IntPtr self, IntPtr _cmd, IntPtr sender);

    private static IntPtr _handlerInstance = IntPtr.Zero;
    private static DesktopActionService? _actionService;
    private static ActionCallback? _cbShowGram;
    private static ActionCallback? _cbNewChat;
    private static ActionCallback? _cbOpenSettings;

    private static IntPtr NSString(string str)
    {
        var cls = objc_getClass("NSString");
        var sel = sel_registerName("stringWithUTF8String:");
        var utf8 = Marshal.StringToHGlobalAnsi(str);
        try
        {
            return objc_msgSend(cls, sel, utf8);
        }
        finally
        {
            Marshal.FreeHGlobal(utf8);
        }
    }

    private static IntPtr AllocInit(string className)
    {
        var cls = objc_getClass(className);
        var alloc = objc_msgSend(cls, sel_registerName("alloc"));
        return objc_msgSend(alloc, sel_registerName("init"));
    }

    private static IntPtr CreateMenu(string title)
    {
        var cls = objc_getClass("NSMenu");
        var alloc = objc_msgSend(cls, sel_registerName("alloc"));
        return objc_msgSend(alloc, sel_registerName("initWithTitle:"), NSString(title));
    }

    private static IntPtr CreateMenuItem(string title, string actionSel, string keyEquiv, ulong modifierMask = 0)
    {
        var cls = objc_getClass("NSMenuItem");
        var alloc = objc_msgSend(cls, sel_registerName("alloc"));
        var selInit = sel_registerName("initWithTitle:action:keyEquivalent:");
        var act = string.IsNullOrEmpty(actionSel) ? IntPtr.Zero : sel_registerName(actionSel);
        var item = objc_msgSend(alloc, selInit, NSString(title), act, NSString(keyEquiv));
        if (modifierMask != 0)
        {
            objc_msgSend(item, sel_registerName("setKeyEquivalentModifierMask:"), modifierMask);
        }
        return item;
    }

    private static IntPtr CreateSeparator()
    {
        var cls = objc_getClass("NSMenuItem");
        return objc_msgSend(cls, sel_registerName("separatorItem"));
    }

    private static void SetupActionHandler()
    {
        if (_handlerInstance != IntPtr.Zero) return;

        var nsobject = objc_getClass("NSObject");
        var handlerClass = objc_allocateClassPair(nsobject, "GramNativeMenuHandler", IntPtr.Zero);

        _cbShowGram = (self, _cmd, sender) =>
        {
            ActivateApp();
            _actionService?.RequestFocus();
        };

        _cbNewChat = (self, _cmd, sender) =>
        {
            ActivateApp();
            _actionService?.RequestNewConversation();
        };

        _cbOpenSettings = (self, _cmd, sender) =>
        {
            ActivateApp();
            _actionService?.RequestSettings();
        };

        class_addMethod(handlerClass, sel_registerName("showGramAction:"), _cbShowGram, "v@:@");
        class_addMethod(handlerClass, sel_registerName("newChatAction:"), _cbNewChat, "v@:@");
        class_addMethod(handlerClass, sel_registerName("openSettingsAction:"), _cbOpenSettings, "v@:@");
        objc_registerClassPair(handlerClass);

        var alloc = objc_msgSend(handlerClass, sel_registerName("alloc"));
        _handlerInstance = objc_msgSend(alloc, sel_registerName("init"));
    }

    public static void ActivateApp()
    {
        if (!OperatingSystem.IsMacOS()) return;

        try
        {
            var appCls = objc_getClass("NSApplication");
            var app = objc_msgSend(appCls, sel_registerName("sharedApplication"));
            objc_msgSend(app, sel_registerName("activateIgnoringOtherApps:"), true);
            objc_msgSend(app, sel_registerName("unhide:"), IntPtr.Zero);

            var windows = objc_msgSend(app, sel_registerName("windows"));
            var count = (long)objc_msgSend(windows, sel_registerName("count"));
            if (count > 0)
            {
                var win = objc_msgSend(windows, sel_registerName("firstObject"));
                objc_msgSend(win, sel_registerName("makeKeyAndOrderFront:"), IntPtr.Zero);
            }
        }
        catch {}
    }

    public static void Initialize(DesktopActionService? actionService = null, string? iconPath = null)
    {
        if (!OperatingSystem.IsMacOS()) return;

        try
        {
            NativeLibrary.Load("/System/Library/Frameworks/AppKit.framework/AppKit");
            _actionService = actionService;

            // 1. Process Name -> "Gram"
            var piCls = objc_getClass("NSProcessInfo");
            var pi = objc_msgSend(piCls, sel_registerName("processInfo"));
            objc_msgSend(pi, sel_registerName("setProcessName:"), NSString("Gram"));

            // 2. Shared NSApplication
            var appCls = objc_getClass("NSApplication");
            var app = objc_msgSend(appCls, sel_registerName("sharedApplication"));

            // 3. Dock Icon
            if (!string.IsNullOrEmpty(iconPath) && File.Exists(iconPath))
            {
                var imgCls = objc_getClass("NSImage");
                var allocImg = objc_msgSend(imgCls, sel_registerName("alloc"));
                var img = objc_msgSend(allocImg, sel_registerName("initWithContentsOfFile:"), NSString(iconPath));
                if (img != IntPtr.Zero)
                {
                    objc_msgSend(app, sel_registerName("setApplicationIconImage:"), img);
                }
            }

            SetupActionHandler();

            // 4. Main Menu Bar (App, File, Edit, View, Window)
            var menubar = CreateMenu("");

            // --- App Menu ---
            var appMenuItem = AllocInit("NSMenuItem");
            objc_msgSend(menubar, sel_registerName("addItem:"), appMenuItem);
            var appMenu = CreateMenu("Gram");

            var aboutItem = CreateMenuItem("About Gram", "showGramAction:", "");
            objc_msgSend(aboutItem, sel_registerName("setTarget:"), _handlerInstance);
            objc_msgSend(appMenu, sel_registerName("addItem:"), aboutItem);

            objc_msgSend(appMenu, sel_registerName("addItem:"), CreateSeparator());

            var prefItem = CreateMenuItem("Preferences…", "openSettingsAction:", ",");
            objc_msgSend(prefItem, sel_registerName("setTarget:"), _handlerInstance);
            objc_msgSend(appMenu, sel_registerName("addItem:"), prefItem);

            objc_msgSend(appMenu, sel_registerName("addItem:"), CreateSeparator());

            var hideItem = CreateMenuItem("Hide Gram", "hide:", "h");
            objc_msgSend(appMenu, sel_registerName("addItem:"), hideItem);

            var hideOthersItem = CreateMenuItem("Hide Others", "hideOtherApplications:", "h", (1UL << 20) | (1UL << 19)); // ⌥⌘H
            objc_msgSend(appMenu, sel_registerName("addItem:"), hideOthersItem);

            var showAllItem = CreateMenuItem("Show All", "unhideAllApplications:", "");
            objc_msgSend(appMenu, sel_registerName("addItem:"), showAllItem);

            objc_msgSend(appMenu, sel_registerName("addItem:"), CreateSeparator());

            var quitItem = CreateMenuItem("Quit Gram", "terminate:", "q");
            objc_msgSend(appMenu, sel_registerName("addItem:"), quitItem);

            objc_msgSend(appMenuItem, sel_registerName("setSubmenu:"), appMenu);

            // --- File Menu ---
            var fileMenuItem = AllocInit("NSMenuItem");
            objc_msgSend(menubar, sel_registerName("addItem:"), fileMenuItem);
            var fileMenu = CreateMenu("File");

            var newChatItem = CreateMenuItem("New Chat", "newChatAction:", "n");
            objc_msgSend(newChatItem, sel_registerName("setTarget:"), _handlerInstance);
            objc_msgSend(fileMenu, sel_registerName("addItem:"), newChatItem);

            objc_msgSend(fileMenu, sel_registerName("addItem:"), CreateSeparator());

            var closeItem = CreateMenuItem("Close Window", "performClose:", "w");
            objc_msgSend(fileMenu, sel_registerName("addItem:"), closeItem);

            objc_msgSend(fileMenuItem, sel_registerName("setSubmenu:"), fileMenu);

            // --- Edit Menu (Essential for WebKit shortcuts: Cut/Copy/Paste/Undo/Redo) ---
            var editMenuItem = AllocInit("NSMenuItem");
            objc_msgSend(menubar, sel_registerName("addItem:"), editMenuItem);
            var editMenu = CreateMenu("Edit");

            objc_msgSend(editMenu, sel_registerName("addItem:"), CreateMenuItem("Undo", "undo:", "z"));
            objc_msgSend(editMenu, sel_registerName("addItem:"), CreateMenuItem("Redo", "redo:", "Z", (1UL << 20) | (1UL << 17)));
            objc_msgSend(editMenu, sel_registerName("addItem:"), CreateSeparator());
            objc_msgSend(editMenu, sel_registerName("addItem:"), CreateMenuItem("Cut", "cut:", "x"));
            objc_msgSend(editMenu, sel_registerName("addItem:"), CreateMenuItem("Copy", "copy:", "c"));
            objc_msgSend(editMenu, sel_registerName("addItem:"), CreateMenuItem("Paste", "paste:", "v"));
            objc_msgSend(editMenu, sel_registerName("addItem:"), CreateMenuItem("Select All", "selectAll:", "a"));

            objc_msgSend(editMenuItem, sel_registerName("setSubmenu:"), editMenu);

            // --- View Menu ---
            var viewMenuItem = AllocInit("NSMenuItem");
            objc_msgSend(menubar, sel_registerName("addItem:"), viewMenuItem);
            var viewMenu = CreateMenu("View");

            objc_msgSend(viewMenu, sel_registerName("addItem:"), CreateMenuItem("Toggle Full Screen", "toggleFullScreen:", "f", (1UL << 20) | (1UL << 18)));

            objc_msgSend(viewMenuItem, sel_registerName("setSubmenu:"), viewMenu);

            // --- Window Menu ---
            var windowMenuItem = AllocInit("NSMenuItem");
            objc_msgSend(menubar, sel_registerName("addItem:"), windowMenuItem);
            var windowMenu = CreateMenu("Window");

            objc_msgSend(windowMenu, sel_registerName("addItem:"), CreateMenuItem("Minimize", "performMiniaturize:", "m"));
            objc_msgSend(windowMenu, sel_registerName("addItem:"), CreateMenuItem("Zoom", "performZoom:", ""));
            objc_msgSend(windowMenu, sel_registerName("addItem:"), CreateSeparator());
            objc_msgSend(windowMenu, sel_registerName("addItem:"), CreateMenuItem("Bring All to Front", "arrangeInFront:", ""));

            objc_msgSend(windowMenuItem, sel_registerName("setSubmenu:"), windowMenu);
            objc_msgSend(app, sel_registerName("setWindowsMenu:"), windowMenu);

            // Apply menubar to application
            objc_msgSend(app, sel_registerName("setMainMenu:"), menubar);

            // 5. Menu Bar Status Item (Tray item in top-right)
            var barCls = objc_getClass("NSStatusBar");
            var systemBar = objc_msgSend(barCls, sel_registerName("systemStatusBar"));
            var statusItem = objc_msgSend(systemBar, sel_registerName("statusItemWithLength:"), -1.0); // NSVariableStatusItemLength = -1.0
            var button = objc_msgSend(statusItem, sel_registerName("button"));
            if (button != IntPtr.Zero)
            {
                objc_msgSend(button, sel_registerName("setTitle:"), NSString("✦ Gram"));
            }

            var statusMenu = CreateMenu("GramStatus");

            var statusShow = CreateMenuItem("Show Gram", "showGramAction:", "");
            objc_msgSend(statusShow, sel_registerName("setTarget:"), _handlerInstance);
            objc_msgSend(statusMenu, sel_registerName("addItem:"), statusShow);

            var statusNew = CreateMenuItem("New Chat", "newChatAction:", "n");
            objc_msgSend(statusNew, sel_registerName("setTarget:"), _handlerInstance);
            objc_msgSend(statusMenu, sel_registerName("addItem:"), statusNew);

            var statusPref = CreateMenuItem("Preferences…", "openSettingsAction:", ",");
            objc_msgSend(statusPref, sel_registerName("setTarget:"), _handlerInstance);
            objc_msgSend(statusMenu, sel_registerName("addItem:"), statusPref);

            objc_msgSend(statusMenu, sel_registerName("addItem:"), CreateSeparator());

            var statusQuit = CreateMenuItem("Quit Gram", "terminate:", "q");
            objc_msgSend(statusMenu, sel_registerName("addItem:"), statusQuit);

            objc_msgSend(statusItem, sel_registerName("setMenu:"), statusMenu);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[MacOsIntegration] Initialization warning: {ex.Message}");
        }
    }
}
