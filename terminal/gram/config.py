"""Small user configuration store for Gram."""

from __future__ import annotations

import json
import os
from pathlib import Path

DEFAULT_BINDINGS = {
    "1": "listen",
    "2": "type",
    "3": "model",
    "4": "help",
    "5": "settings",
    "6": "quit",
}

DEFAULT_MODEL = "qwen3.5:9b"
DEFAULT_API_URL = "http://localhost:11434/api/chat"
DEFAULT_ALLOWED_MODELS = [DEFAULT_MODEL]


def path() -> Path:
    root = os.getenv("APPDATA") or str(Path.home())
    return Path(root) / "Gram" / "config.json"


def load() -> dict[str, str]:
    try:
        return json.loads(path().read_text(encoding="utf-8"))
    except (OSError, ValueError):
        return {}


def save(values: dict[str, str]) -> None:
    target = path()
    target.parent.mkdir(parents=True, exist_ok=True)
    target.write_text(json.dumps(values, indent=2), encoding="utf-8")


def bindings() -> dict[str, str]:
    saved = load().get("bindings", {})
    result = DEFAULT_BINDINGS.copy()
    if isinstance(saved, dict):
        result.update({str(key): str(value) for key, value in saved.items() if str(key) in result})
    return result


def save_binding(button: str, action: str) -> None:
    values = load()
    current = bindings()
    current[button] = action
    values["bindings"] = current
    save(values)
