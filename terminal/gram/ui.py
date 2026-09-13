"""ANSI terminal UI, designed for Windows Terminal and modern PowerShell."""

from __future__ import annotations

import threading
import time

ESC = "\x1b["
RESET = ESC + "0m"
CYAN = ESC + "38;5;51m"
PINK = ESC + "38;5;213m"
BLUE = ESC + "38;5;117m"
MUTED = ESC + "38;5;103m"
WHITE = ESC + "38;5;255m"


class TerminalUI:
    def __init__(self) -> None:
        self.state = "idle"
        self.level = 0.04
        self.spectrum = [0.04] * 24
        self.transcript = ""
        self.message = "Press /help for commands."
        self.running = True
        self.lock = threading.Lock()

    def update(self, **values) -> None:
        with self.lock:
            for key, value in values.items():
                setattr(self, key, value)

    def _audio_field(self) -> list[str]:
        pulse = int(self.level * 10)
        core = "·" * (8 + pulse) + "◈" + "·" * (8 + pulse)
        return [
            f"        {CYAN}╭────────────────────────────╮{RESET}",
            f"        {CYAN}│{PINK}      A U D I O   F I E L D      {CYAN}│{RESET}",
            f"        {CYAN}│{BLUE}       {core[:27]:^27}       {CYAN}│{RESET}",
            f"        {CYAN}│{PINK}       {self._spectrum():^27}       {CYAN}│{RESET}",
            f"        {CYAN}╰────────────────────────────╯{RESET}",
        ]

    def _meter(self) -> str:
        width = 34
        filled = min(width, max(1, int(self.level * width)))
        return f"{CYAN}{'▰' * filled}{'▱' * (width - filled)}{RESET}"

    def _spectrum(self) -> str:
        shades = "▁▂▃▄▅▆▇█"
        return "".join(shades[min(7, int(value * 8))] for value in self.spectrum)

    def render(self) -> None:
        with self.lock:
            state, transcript, message = self.state, self.transcript, self.message
            level = self.level
        status_color = CYAN if state == "listening" else PINK if state == "thinking" else BLUE
        lines = [
            ESC + "2J" + ESC + "H",
            f"{PINK}  G R A M {RESET}{MUTED} // voice coding terminal{RESET}",
            f"{MUTED}  ─────────────────────────────────────────────────────────────{RESET}",
            "",
            *[f"  {line}" for line in self._audio_field()],
            "",
            f"  {status_color}● {state.upper():10}{RESET}  mic level  {self._meter()}",
            f"  {MUTED}spectrum{RESET}     {PINK}{self._spectrum()}{RESET}",
            "",
            f"  {MUTED}transcript{RESET}  {WHITE}{transcript[-72:]}{RESET}",
            f"  {MUTED}system{RESET}      {message}",
            "",
            f"  {MUTED}1 Listen   2 Type   3 Model   4 Help   5 Settings   6 Quit{RESET}",
        ]
        print("\n".join(lines), end="", flush=True)

    def loop(self, interval: float = 0.08) -> None:
        while self.running:
            self.render()
            time.sleep(interval)

    def close(self) -> None:
        self.running = False
        print(RESET + "\n")
