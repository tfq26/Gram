use sha2::{Digest, Sha256};
use std::net::TcpStream;
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::sync::Mutex;
use std::time::{Duration, Instant};
use tauri::{
    menu::{Menu, MenuItem, PredefinedMenuItem},
    tray::{MouseButton, MouseButtonState, TrayIconBuilder, TrayIconEvent},
    window::{Color, Effect, EffectState, EffectsBuilder},
    Emitter, Manager, RunEvent, Theme, WindowEvent,
};
use tauri_plugin_deep_link::DeepLinkExt;
use tauri_plugin_global_shortcut::{GlobalShortcutExt, ShortcutState};
use tauri_plugin_notification::NotificationExt;

#[tauri::command]
fn set_window_theme(window: tauri::WebviewWindow, theme: String) {
    let normalized = theme.trim().to_ascii_lowercase();
    let (native_theme, bg_color) = match normalized.as_str() {
        "bone" | "light" => (Theme::Light, Color(247, 245, 240, 255)),
        "prism" => (Theme::Dark, Color(9, 10, 11, 255)),
        "obsidian" => (Theme::Dark, Color(14, 15, 16, 255)),
        _ => (Theme::Dark, Color(10, 10, 12, 255)),
    };
    let _ = window.set_theme(Some(native_theme));
    let _ = window.set_background_color(Some(bg_color));
}

#[tauri::command]
fn notify_agent_event(app: tauri::AppHandle, title: String, body: String) {
    let _ = app.notification().builder().title(title).body(body).show();
}

fn build_child_url(app: &tauri::AppHandle, query: &str) -> tauri::WebviewUrl {
    if let Some(main) = app.get_webview_window("main") {
        if let Ok(mut url) = main.url() {
            url.set_query(Some(query));
            return tauri::WebviewUrl::External(url);
        }
    }
    tauri::WebviewUrl::App(format!("index.html?{}", query).into())
}

#[tauri::command]
fn toggle_mission_pip(app: tauri::AppHandle, open: bool) -> Result<(), String> {
    if !open {
        if let Some(pip) = app.get_webview_window("mission-pip") {
            let _ = pip.close();
        }
        if let Some(main) = app.get_webview_window("main") {
            let _ = main.show();
            let _ = main.set_focus();
        }
        return Ok(());
    }

    if let Some(existing) = app.get_webview_window("mission-pip") {
        let _ = existing.show();
        let _ = existing.set_focus();
        return Ok(());
    }

    let target_url = build_child_url(&app, "pip=1");
    let effects = EffectsBuilder::new()
        .effect(Effect::HudWindow)
        .state(EffectState::Active)
        .build();

    let pip = tauri::WebviewWindowBuilder::new(&app, "mission-pip", target_url)
        .title("Gram Miniplayer")
        .inner_size(348.0, 152.0)
        .min_inner_size(280.0, 128.0)
        .resizable(true)
        .decorations(false)
        .transparent(true)
        .effects(effects)
        .always_on_top(true)
        .visible_on_all_workspaces(true)
        .build()
        .map_err(|e| e.to_string())?;

    let _ = pip.set_focus();
    Ok(())
}

#[tauri::command]
fn toggle_quick_capture(app: tauri::AppHandle, open: Option<bool>) -> Result<(), String> {
    let existing = app.get_webview_window("quick-capture");
    let should_open = open.unwrap_or_else(|| existing.is_none());

    if !should_open {
        if let Some(win) = existing {
            let _ = win.close();
        }
        return Ok(());
    }

    if let Some(win) = existing {
        let _ = win.show();
        let _ = win.set_focus();
        return Ok(());
    }

    let target_url = build_child_url(&app, "capture=1");
    let effects = EffectsBuilder::new()
        .effect(Effect::HudWindow)
        .state(EffectState::Active)
        .build();

    let win = tauri::WebviewWindowBuilder::new(&app, "quick-capture", target_url)
        .title("Gram Quick Capture")
        .inner_size(540.0, 88.0)
        .min_inner_size(400.0, 72.0)
        .resizable(false)
        .decorations(false)
        .transparent(true)
        .effects(effects)
        .always_on_top(true)
        .visible_on_all_workspaces(true)
        .center()
        .build()
        .map_err(|e| e.to_string())?;

    let _ = win.set_focus();
    Ok(())
}

struct SidecarState(Mutex<Option<Child>>);

fn server_is_reachable() -> bool {
    TcpStream::connect_timeout(&"127.0.0.1:7337".parse().unwrap(), Duration::from_millis(150)).is_ok()
}

fn find_workspace_root() -> PathBuf {
    if let Ok(explicit) = std::env::var("GRAM_WORKSPACE_ROOT") {
        let p = PathBuf::from(explicit);
        if p.exists() {
            return p;
        }
    }
    if let Ok(cwd) = std::env::current_dir() {
        for ancestor in cwd.ancestors() {
            if ancestor.join("src/server/index.ts").exists() {
                return ancestor.to_path_buf();
            }
        }
    }
    let manifest_dir = Path::new(env!("CARGO_MANIFEST_DIR"));
    if let Some(parent) = manifest_dir.parent() {
        if parent.join("src/server/index.ts").exists() {
            return parent.to_path_buf();
        }
    }
    std::env::current_dir().unwrap_or_else(|_| PathBuf::from("."))
}

fn spawn_gram_server() -> Option<Child> {
    if server_is_reachable() {
        return None;
    }

    let workspace_root = find_workspace_root();
    let bun_bin = std::env::var("GRAM_BUN_BIN").unwrap_or_else(|_| "bun".to_string());

    let child = Command::new(bun_bin)
        .args(["run", "src/server/index.ts"])
        .current_dir(&workspace_root)
        .stdin(Stdio::null())
        .stdout(Stdio::inherit())
        .stderr(Stdio::inherit())
        .spawn()
        .ok()?;

    let deadline = Instant::now() + Duration::from_secs(10);
    while Instant::now() < deadline {
        if server_is_reachable() {
            break;
        }
        std::thread::sleep(Duration::from_millis(100));
    }

    Some(child)
}

#[cfg_attr(mobile, tauri::mobile_entry_point)]
pub fn run() {
    let child = spawn_gram_server();

    let app = tauri::Builder::default()
        .manage(SidecarState(Mutex::new(child)))
        .plugin(tauri_plugin_notification::init())
        .plugin(tauri_plugin_deep_link::init())
        .plugin(tauri_plugin_updater::Builder::new().build())
        .plugin(
            tauri_plugin_stronghold::Builder::new(|password| {
                let mut hasher = Sha256::new();
                hasher.update(password.as_bytes());
                hasher.finalize().to_vec()
            })
            .build(),
        )
        .plugin(
            tauri_plugin_global_shortcut::Builder::new()
                .with_handler(|app, shortcut, event| {
                    if event.state != ShortcutState::Pressed {
                        return;
                    }
                    let key_str = shortcut.into_string().to_ascii_lowercase();
                    if key_str.contains("keyi") {
                        let _ = toggle_quick_capture(app.clone(), None);
                    } else if key_str.contains("keym") {
                        let is_open = app.get_webview_window("mission-pip").is_some();
                        let _ = toggle_mission_pip(app.clone(), !is_open);
                    }
                })
                .build(),
        )
        .setup(|app| {
            let show_item = MenuItem::with_id(app, "show", "Show Gram", true, None::<&str>)?;
            let capture_item = MenuItem::with_id(app, "capture", "Quick Capture (⌘⇧I)", true, None::<&str>)?;
            let pip_item = MenuItem::with_id(app, "pip", "Toggle Miniplayer (⌘⇧M)", true, None::<&str>)?;
            let sep = PredefinedMenuItem::separator(app)?;
            let quit_item = MenuItem::with_id(app, "quit", "Quit Gram", true, None::<&str>)?;
            let tray_menu = Menu::with_items(app, &[&show_item, &capture_item, &pip_item, &sep, &quit_item])?;

            if let Some(icon) = app.default_window_icon().cloned() {
                let _ = TrayIconBuilder::with_id("gram-tray")
                    .icon(icon)
                    .tooltip("Gram")
                    .menu(&tray_menu)
                    .on_menu_event(|app, event| match event.id.as_ref() {
                        "show" => {
                            if let Some(main) = app.get_webview_window("main") {
                                let _ = main.show();
                                let _ = main.set_focus();
                            }
                        }
                        "capture" => {
                            let _ = toggle_quick_capture(app.clone(), Some(true));
                        }
                        "pip" => {
                            let is_open = app.get_webview_window("mission-pip").is_some();
                            let _ = toggle_mission_pip(app.clone(), !is_open);
                        }
                        "quit" => {
                            app.exit(0);
                        }
                        _ => {}
                    })
                    .on_tray_icon_event(|tray, event| {
                        if let TrayIconEvent::Click {
                            button: MouseButton::Left,
                            button_state: MouseButtonState::Up,
                            ..
                        } = event
                        {
                            let app = tray.app_handle();
                            if let Some(main) = app.get_webview_window("main") {
                                let _ = main.show();
                                let _ = main.set_focus();
                            }
                        }
                    })
                    .build(app);
            }

            let _ = app.global_shortcut().register("CmdOrCtrl+Shift+I");
            let _ = app.global_shortcut().register("CmdOrCtrl+Shift+M");

            let app_handle = app.handle().clone();
            app.deep_link().on_open_url(move |event| {
                let urls: Vec<String> = event.urls().into_iter().map(|u| u.to_string()).collect();
                if let Some(main) = app_handle.get_webview_window("main") {
                    let _ = main.show();
                    let _ = main.set_focus();
                }
                let _ = app_handle.emit("gram://deep-link", urls);
            });

            if let Some(main) = app.get_webview_window("main") {
                let main_clone = main.clone();
                main.on_window_event(move |event| {
                    if let WindowEvent::CloseRequested { api, .. } = event {
                        #[cfg(target_os = "macos")]
                        {
                            api.prevent_close();
                            let _ = main_clone.hide();
                        }
                    }
                });
            }

            Ok(())
        })
        .invoke_handler(tauri::generate_handler![
            set_window_theme,
            toggle_mission_pip,
            toggle_quick_capture,
            notify_agent_event
        ])
        .build(tauri::generate_context!())
        .expect("error while building Gram Tauri application");

    app.run(|app_handle, event| {
        if let RunEvent::Exit = event {
            if let Some(state) = app_handle.try_state::<SidecarState>() {
                if let Ok(mut guard) = state.0.lock() {
                    if let Some(mut child) = guard.take() {
                        let _ = child.kill();
                        let _ = child.wait();
                    }
                }
            }
        }
    });
}
