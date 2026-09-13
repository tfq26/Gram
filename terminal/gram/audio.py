"""Optional microphone level sampler."""

from __future__ import annotations

import math
import random
import threading
import time
import os
from collections.abc import Callable

from .fft import magnitudes
from .logging_setup import setup_logging

logger = setup_logging()


class AudioLevel:
    def __init__(self, on_level: Callable[[float], None], on_spectrum: Callable[[list[float]], None] | None = None) -> None:
        self.on_level = on_level
        self.on_spectrum = on_spectrum or (lambda values: None)
        self.running = False
        self.thread: threading.Thread | None = None
        self.real_mic = False
        self.source = os.getenv("GRAM_AUDIO_SOURCE", "mic").lower()

    def start(self) -> None:
        if self.running:
            return
        self.running = True
        logger.info("Audio sampler starting source=%s", self.source)
        sd = None
        if self.source != "system":
            try:
                import sounddevice as sd  # type: ignore
            except ImportError:
                pass

        if self.source == "system":
            try:
                import soundcard as sc  # type: ignore
                import numpy as np  # type: ignore
                speaker = sc.default_speaker()
                self.loopback = speaker.recorder(samplerate=16000, channels=1)
                self.loopback.__enter__()
                self.real_mic = True

                def loopback_level() -> None:
                    while self.running:
                        samples = self.loopback.record(numframes=512)
                        mono = np.asarray(samples).reshape(-1).astype(float)
                        level = min(1.0, max(0.0, float(np.sqrt(np.mean(mono * mono))) * 7))
                        self.on_level(level)
                        self.on_spectrum(magnitudes(mono.tolist(), 24))

                self.thread = threading.Thread(target=loopback_level, daemon=True)
                self.thread.start()
                return
            except Exception:
                self.real_mic = False
                logger.exception("System audio loopback unavailable; falling back")

        if sd is not None:
            self.real_mic = True

            def callback(indata, frames, time_info, status):
                if not self.running:
                    return
                level = min(1.0, max(0.0, float((indata[:, 0] ** 2).mean()) ** 0.5 * 7))
                self.on_level(level)
                self.on_spectrum(magnitudes([float(value) for value in indata[:, 0]], 24))

            try:
                self.stream = sd.InputStream(channels=1, callback=callback, blocksize=512)
                self.stream.start()
                return
            except Exception:
                # A missing microphone or an unavailable Windows audio device
                # should not prevent the terminal assistant from starting.
                self.real_mic = False
                logger.exception("Microphone stream unavailable; falling back")

        def demo_level() -> None:
            phase = 0.0
            while self.running:
                phase += 0.16
                self.on_level(max(0.02, 0.10 + 0.08 * math.sin(phase) + random.random() * 0.04))
                self.on_spectrum([
                    min(1.0, max(0.02, 0.12 + 0.08 * math.sin(phase + i * 0.42) + random.random() * 0.08))
                    for i in range(24)
                ])
                time.sleep(0.05)

        self.thread = threading.Thread(target=demo_level, daemon=True)
        self.thread.start()
        logger.info("Audio sampler using demo signal real_mic=%s", self.real_mic)

    def stop(self) -> None:
        self.running = False
        logger.info("Audio sampler stopping")
        stream = getattr(self, "stream", None)
        if stream is not None:
            stream.stop()
            stream.close()
        loopback = getattr(self, "loopback", None)
        if loopback is not None:
            loopback.__exit__(None, None, None)
