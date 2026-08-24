"""Centralized diagnostic logging for Gram."""

from __future__ import annotations

import logging
import os
from logging.handlers import RotatingFileHandler
from pathlib import Path


def log_path() -> Path:
    root = os.getenv("APPDATA") or str(Path.home())
    return Path(root) / "Gram" / "logs" / "gram.log"


def setup_logging() -> logging.Logger:
    logger = logging.getLogger("gram")
    if logger.handlers:
        return logger
    target = log_path()
    target.parent.mkdir(parents=True, exist_ok=True)
    handler = RotatingFileHandler(target, maxBytes=5_000_000, backupCount=3, encoding="utf-8")
    handler.setFormatter(logging.Formatter("%(asctime)s %(levelname)s [%(name)s] %(message)s"))
    logger.addHandler(handler)
    logger.setLevel(logging.DEBUG)
    logger.propagate = False
    logger.info("Gram logging initialized")
    logger.info("Diagnostic log path: %s", target)
    return logger


def redact(value: str) -> str:
    """Avoid writing recognizable API credentials to the diagnostic log."""
    if not value:
        return value
    return value[:4] + "…REDACTED" if len(value) > 4 else "REDACTED"
