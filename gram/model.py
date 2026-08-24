"""Small OpenAI-compatible model adapter with a safe local preview fallback."""

from __future__ import annotations

import json
import os
from urllib.request import Request, urlopen

from .config import DEFAULT_API_URL, DEFAULT_ALLOWED_MODELS, DEFAULT_MODEL, load
from .logging_setup import redact, setup_logging
from .ollama import installed_models

logger = setup_logging()


class ModelClient:
    def __init__(self) -> None:
        saved = load()
        self.provider = os.getenv("GRAM_PROVIDER", saved.get("provider", "ollama"))
        default_url = "https://openrouter.ai/api/v1/chat/completions" if self.provider == "openrouter" else DEFAULT_API_URL
        self.url = os.getenv("GRAM_API_URL", saved.get("api_url", default_url))
        self.key = os.getenv("GRAM_API_KEY", saved.get("api_key", ""))
        self.model = os.getenv("GRAM_MODEL", saved.get("model", DEFAULT_MODEL))
        configured = saved.get("allowed_models", DEFAULT_ALLOWED_MODELS)
        if isinstance(configured, list) and configured:
            self.allowed_models = [str(item) for item in configured]
        else:
            self.allowed_models = DEFAULT_ALLOWED_MODELS.copy()
        logger.info("Model client initialized provider=%s model=%s url=%s allowed=%s key=%s", self.provider, self.model, self.url, self.allowed_models, redact(self.key))

    @property
    def configured(self) -> bool:
        return bool(self.url)

    def is_allowed(self, model: str | None = None) -> bool:
        return (model or self.model) in self.allowed_models

    def allowed(self) -> str:
        return ", ".join(self.allowed_models)

    def installed(self) -> list[str]:
        if self.provider != "ollama":
            return []
        base_url = self.url.split("/api/", 1)[0]
        return [str(item.get("name")) for item in installed_models(base_url) if item.get("name")]

    def describe(self) -> str:
        if self.configured:
            return f"{self.model} @ {self.url}"
        return "local-preview (no endpoint configured)"

    def complete(self, prompt: str) -> str:
        if not self.is_allowed():
            logger.warning("Blocked model not in allowlist: %s", self.model)
            return f"Model '{self.model}' is blocked by Gram's allowlist. Allowed models: {self.allowed()}"
        if self.provider == "ollama":
            available = self.installed()
            if available and self.model not in available:
                logger.warning("Configured Ollama model is not installed model=%s installed=%s", self.model, available)
                return f"Ollama model '{self.model}' is not installed. Installed models: {', '.join(available)}"
        if not self.configured:
            return (
                "Preview only: I received this request:\n"
                f"  {prompt}\n\n"
                "Configure GRAM_API_URL to connect a coding model."
            )

        messages = [
            {"role": "system", "content": "You are a careful terminal coding assistant. The user transcript may contain speech-recognition errors; infer the intended meaning from context and ask a clarification question when ambiguity is material. Explain intended file and command changes before execution."},
            {"role": "user", "content": prompt},
        ]
        if self.provider == "ollama":
            body = json.dumps({"model": self.model, "messages": messages, "stream": False, "options": {"temperature": 0.2}}).encode("utf-8")
        else:
            body = json.dumps({"model": self.model, "messages": messages, "temperature": 0.2}).encode("utf-8")
        headers = {"Content-Type": "application/json"}
        if self.provider == "openrouter":
            headers["HTTP-Referer"] = "https://github.com/gram-terminal"
            headers["X-Title"] = "Gram Terminal Assistant"
        if self.key:
            headers["Authorization"] = f"Bearer {self.key}"
        request = Request(self.url, data=body, headers=headers, method="POST")
        logger.debug("API request start provider=%s model=%s url=%s prompt_chars=%d", self.provider, self.model, self.url, len(prompt))
        try:
            with urlopen(request, timeout=90) as response:
                payload = json.loads(response.read().decode("utf-8"))
                status = response.status
        except Exception:
            logger.exception("API request failed provider=%s model=%s url=%s", self.provider, self.model, self.url)
            raise
        if self.provider == "ollama":
            answer = payload["message"]["content"]
        else:
            answer = payload["choices"][0]["message"]["content"]
        logger.debug("API response received status=%s response_chars=%d", status, len(answer))
        return answer
