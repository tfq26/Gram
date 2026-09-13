"""Small Ollama local registry client."""

from __future__ import annotations

import json
from urllib.request import urlopen

from .logging_setup import setup_logging

logger = setup_logging()


def installed_models(base_url: str = "http://localhost:11434") -> list[dict]:
    try:
        with urlopen(f"{base_url}/api/tags", timeout=5) as response:
            payload = json.loads(response.read().decode("utf-8"))
        models = payload.get("models", [])
        logger.info("Ollama discovery found %d installed models", len(models))
        return models
    except Exception:
        logger.exception("Ollama model discovery failed")
        return []
