"""Best-effort Windows audio device discovery and selection."""

from __future__ import annotations

from .logging_setup import setup_logging

logger = setup_logging()


def list_devices() -> tuple[list[dict], list[dict]]:
    inputs: list[dict] = []
    outputs: list[dict] = []
    try:
        import sounddevice as sd  # type: ignore
        for index, device in enumerate(sd.query_devices()):
            item = {"index": index, "name": str(device["name"])}
            if int(device["max_input_channels"]) > 0:
                inputs.append(item)
            if int(device["max_output_channels"]) > 0:
                outputs.append(item)
    except (ImportError, OSError):
        logger.exception("Audio device enumeration failed")
    logger.info("Audio devices enumerated inputs=%d outputs=%d", len(inputs), len(outputs))
    return inputs, outputs


def select_input(index: int) -> None:
    try:
        import sounddevice as sd  # type: ignore
        current = sd.default.device
        sd.default.device = (index, current[1])
    except (ImportError, OSError):
        logger.exception("Audio input selection failed index=%s", index)
