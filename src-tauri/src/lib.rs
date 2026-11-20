use std::net::TcpStream;
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::sync::Mutex;
use std::time::{Duration, Instant};
use tauri::{window::Color, Manager, RunEvent, Theme};

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

    let target_url = if let Some(main) = app.get_webview_window("main") {
        if let Ok(mut url) = main.url() {
            url.set_query(Some("pip=1"));
            tauri::WebviewUrl::External(url)
        } else {
            tauri::WebviewUrl::App("index.html?pip=1".into())
        }
    } else {
        tauri::WebviewUrl::App("index.html?pip=1".into())
    };

    let pip = tauri::WebviewWindowBuilder::new(&app, "mission-pip", target_url)
        .title("Gram Miniplayer")
        .inner_size(348.0, 152.0)
        .min_inner_size(280.0, 128.0)
        .resizable(true)
        .decorations(false)
        .always_on_top(true)
        .visible_on_all_workspaces(true)
        .background_color(Color(15, 16, 18, 255))
        .build()
        .map_err(|e| e.to_string())?;

    let _ = pip.set_focus();
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
        .invoke_handler(tauri::generate_handler![set_window_theme, toggle_mission_pip])
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
