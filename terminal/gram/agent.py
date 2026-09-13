"""Conservative command extraction and execution for model responses."""

from __future__ import annotations

import re
import subprocess

from .logging_setup import setup_logging

logger = setup_logging()


def commands_from(text: str) -> list[str]:
    commands: list[str] = []
    for block in re.findall(r"```(?:powershell|pwsh|ps1|shell|bash|sh)?\s*\n(.*?)```", text, re.I | re.S):
        commands.extend(line.strip() for line in block.splitlines() if line.strip() and not line.lstrip().startswith("#"))
    for line in text.splitlines():
        if line.strip().lower().startswith("run: "):
            commands.append(line.split(":", 1)[1].strip())
    logger.info("Extracted %d candidate commands from model response", len(commands))
    return commands


def execute(command: str, cwd: str) -> tuple[int, str]:
    logger.warning("Executing approved PowerShell command cwd=%s command=%r", cwd, command)
    result = subprocess.run(
        ["powershell.exe", "-NoProfile", "-ExecutionPolicy", "Bypass", "-Command", command],
        cwd=cwd,
        capture_output=True,
        text=True,
        timeout=120,
    )
    output = (result.stdout + result.stderr).strip()
    logger.info("Command finished exit_code=%s output_chars=%d", result.returncode, len(output))
    return result.returncode, output[-4000:]
