"""Conservative cleanup for speech-recognition output."""

from __future__ import annotations

import re

from .logging_setup import setup_logging

logger = setup_logging()

DEFAULT_CORRECTIONS = {
    "westerners day": "today's date",
    "what is the today's date": "what is today's date",
    "what's the date today": "what is today's date",
}


def normalize(text: str, corrections: dict[str, str] | None = None) -> str:
    result = " ".join(text.split()).strip()
    table = DEFAULT_CORRECTIONS | (corrections or {})
    for source, target in table.items():
        result = re.sub(rf"\b{re.escape(source)}\b", target, result, flags=re.I)
    logger.debug("Transcript normalized input=%r output=%r", text, result)
    return result
