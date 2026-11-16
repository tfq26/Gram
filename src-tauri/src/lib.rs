use std::net::TcpStream;
use std::path::{Path, PathBuf};
use std::process::{Child, Command, Stdio};
use std::sync::Mutex;
use std::time::{Duration, Instant};
use tauri::{Manager, RunEvent};

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
