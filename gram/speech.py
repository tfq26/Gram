"""Windows-native speech bridges.

Text-to-speech uses SAPI through PowerShell, so the base install needs no
Python audio dependency. Recognition is best-effort and can be enabled when
the Windows Speech Recognition assembly is available.
"""

from __future__ import annotations

import subprocess
import tempfile
import wave
import json
from pathlib import Path

from .config import load
from .logging_setup import setup_logging

logger = setup_logging()


class Speech:
    def __init__(self) -> None:
        self.powershell = "powershell.exe"
        self.output_device = load().get("output_device")
        self.last_confidence = None

    def set_output_device(self, device: str | None) -> None:
        self.output_device = device

    def speak(self, text: str) -> bool:
        logger.info("TTS requested chars=%d output_device=%s", len(text), self.output_device)
        safe = text.replace("'", "''").replace("\r", " ").replace("\n", " ")
        if self.output_device is not None:
            return self._speak_on_device(safe)
        script = (
            "Add-Type -AssemblyName System.Speech; "
            "$s=New-Object System.Speech.Synthesis.SpeechSynthesizer; "
            "$s.Rate=0; $s.Volume=90; "
            f"$s.Speak('{safe}'); $s.Dispose()"
        )
        try:
            subprocess.Popen(
                [self.powershell, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
            )
            return True
        except OSError:
            logger.exception("TTS process could not start")
            return False

    def _speak_on_device(self, safe: str) -> bool:
        """Route SAPI's generated WAV through sounddevice when available."""
        try:
            import numpy as np  # type: ignore
            import sounddevice as sd  # type: ignore
            with tempfile.NamedTemporaryFile(suffix=".wav", delete=False) as handle:
                wav_path = Path(handle.name)
            escaped_path = str(wav_path).replace("'", "''")
            script = (
                "Add-Type -AssemblyName System.Speech; "
                "$s=New-Object System.Speech.Synthesis.SpeechSynthesizer; "
                f"$s.SetOutputToWaveFile('{escaped_path}'); $s.Speak('{safe}'); $s.Dispose()"
            )
            result = subprocess.run(
                [self.powershell, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-Command", script],
                stdout=subprocess.DEVNULL,
                stderr=subprocess.DEVNULL,
                timeout=30,
            )
            if result.returncode != 0:
                return False
            with wave.open(str(wav_path), "rb") as audio:
                data = np.frombuffer(audio.readframes(audio.getnframes()), dtype=np.int16)
                sd.play(data, audio.getframerate(), device=int(self.output_device), blocking=True)
            wav_path.unlink(missing_ok=True)
            return True
        except (ImportError, OSError, ValueError, subprocess.SubprocessError):
            logger.exception("Selected output device playback failed")
            return False

    def listen_once(self) -> str | None:
        """Use Windows Speech Recognition when the assembly is available.

        This is a deliberately bounded one-shot recognizer. Streaming ASR,
        VAD, and correction handling will replace it in the next voice layer.
        """
        script_path = Path(__file__).with_name("windows_listen.ps1")
        try:
            logger.info("Speech recognition started script=%s", script_path)
            result = subprocess.run(
                [self.powershell, "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", str(script_path)],
                capture_output=True,
                text=True,
                timeout=30,
            )
        except (OSError, subprocess.TimeoutExpired):
            logger.exception("Speech recognition process failed")
            return None
        if result.returncode != 0:
            logger.error("Speech recognition exited with code=%s stderr=%s", result.returncode, result.stderr.strip()[:500])
            return None
        value = result.stdout.strip()
        if not value:
            return None
        try:
            payload = json.loads(value)
            self.last_confidence = payload.get("confidence")
            logger.info("Speech recognition transcript=%r confidence=%r", payload.get("text"), self.last_confidence)
            return payload.get("text") or None
        except json.JSONDecodeError:
            self.last_confidence = None
            logger.warning("Speech recognition returned non-JSON output: %r", value[:500])
            return value
