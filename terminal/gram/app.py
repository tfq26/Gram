from __future__ import annotations

import threading
import os

try:
    import msvcrt
except ImportError:
    msvcrt = None

from .agent import commands_from, execute
from .audio import AudioLevel
from .config import bindings, load, save, save_binding
from .devices import list_devices, select_input
from .model import ModelClient
from .logging_setup import setup_logging
from .speech import Speech
from .transcript import normalize
from .ui import TerminalUI


def main() -> None:
    logger = setup_logging()
    logger.info("Application starting")
    ui = TerminalUI()
    model = ModelClient()
    speech = Speech()
    saved_config = load()
    if saved_config.get("input_device", "").isdigit():
        select_input(int(saved_config["input_device"]))
    audio = AudioLevel(
        lambda level: ui.update(level=level),
        lambda spectrum: ui.update(spectrum=spectrum),
    )
    audio.start()
    logger.info("Audio level sampler started; source=%s", audio.source)
    threading.Thread(target=ui.loop, daemon=True).start()
    speech.speak("Hi. I'm Gram, your terminal coding assistant. Tell me what you want to build.")
    logger.info("Startup introduction requested")

    def show_help() -> None:
        ui.update(message="Press 1 to listen, 2 to type, 3 for model, 4 for help, 5 for settings, or 6 to quit. /vocab add heard=intended adds a correction.")

    def settings_menu() -> None:
        current = bindings()
        ui.update(state="idle", message="Settings: enter a button and action, e.g. 1=type. Type back to return.")
        print("\n  Button settings")
        print("  " + " | ".join(f"{key}={value}" for key, value in current.items()))
        print("  Actions: listen, type, model, help, settings, quit")
        print("  Settings: input, output, test")
        while True:
            choice = input("  settings > ").strip().lower()
            if choice in {"back", "b", ""}:
                return
            if choice in {"input", "output"}:
                inputs, outputs = list_devices()
                devices = inputs if choice == "input" else outputs
                if not devices:
                    print("  No devices found. Install sounddevice to enumerate devices.")
                    continue
                for device in devices:
                    print(f"  {device['index']}: {device['name']}")
                selected = input(f"  {choice} device number > ").strip()
                if selected.isdigit() and any(device["index"] == int(selected) for device in devices):
                    values = load()
                    values[f"{choice}_device"] = selected
                    save(values)
                    if choice == "input":
                        select_input(int(selected))
                    else:
                        speech.set_output_device(selected)
                    ui.update(message=f"Saved {choice} device {selected}.")
                else:
                    print("  Invalid device number.")
                continue
            if choice == "test":
                ui.update(state="listening", message="Audio test: speak a short sentence.")
                phrase = speech.listen_once()
                if phrase:
                    ui.update(state="speaking", transcript=phrase, message=f"I heard: {phrase}")
                    speech.speak(f"I heard: {phrase}")
                else:
                    ui.update(state="warning", message="I couldn't recognize anything from the selected input.")
                    speech.speak("I couldn't recognize anything from the selected input.")
                continue
            if "=" not in choice:
                ui.update(message="Use the format button=action, for example 1=type.")
                continue
            button, action = [part.strip() for part in choice.split("=", 1)]
            if button not in {"1", "2", "3", "4", "5", "6"} or action not in {"listen", "type", "model", "help", "settings", "quit"}:
                ui.update(message="Unknown button or action. Use one of the six buttons and listed actions.")
                continue
            save_binding(button, action)
            ui.update(message=f"Saved button {button} → {action}.")
            print(f"  Saved {button}={action}")

    def read_menu_input() -> str:
        """Read number buttons immediately, while preserving typed fallback."""
        if msvcrt is None:
            return input("\n  > ").strip()
        print("\n  > ", end="", flush=True)
        first = msvcrt.getwch()
        if first in {"1", "2", "3", "4", "5", "6"}:
            print(first)
            return first
        if first in {"\r", "\n"}:
            return ""
        print(first, end="", flush=True)
        return (first + input()).strip()

    def handle_answer(answer: str) -> None:
        spoken = answer.replace("\n", " ")[:400]
        ui.update(state="speaking", message=spoken[:180])
        speech.speak(spoken)
        for command in commands_from(answer):
            logger.info("Command proposed for approval: %r", command)
            ui.update(state="warning", message=f"Proposed command: {command[:140]}")
            try:
                approval = input(f"\n  Execute this command? [y/N] {command}\n  > ").strip().lower()
            except (KeyboardInterrupt, EOFError):
                approval = ""
            if approval not in {"y", "yes"}:
                logger.info("Command rejected by user: %r", command)
                ui.update(message="Command skipped.")
                continue
            try:
                code, output = execute(command, os.getcwd())
                ui.update(state="speaking" if code == 0 else "warning", message=(output or f"Exit code {code}")[:180])
                speech.speak((output or f"Command finished with exit code {code}")[:300])
            except Exception as exc:
                ui.update(state="warning", message=f"Execution error: {exc}")

    try:
        while ui.running:
            raw = read_menu_input()
            logger.debug("Menu input received: %r", raw)
            if not raw:
                continue
            if raw in {"1", "2", "3", "4", "5", "6"}:
                action = bindings()[raw]
                if action == "listen":
                    raw = "/voice"
                elif action == "type":
                    raw = "/listen"
                elif action == "model":
                    raw = "/model swap"
                elif action == "help":
                    raw = "/help"
                elif action == "settings":
                    raw = "/settings"
                elif action == "quit":
                    raw = "/quit"
            if raw == "/quit":
                logger.info("Quit requested")
                break
            if raw == "/help":
                show_help()
                continue
            if raw == "/settings":
                settings_menu()
                continue
            if raw == "/model":
                ui.update(message=f"{model.describe()} | allowed: {model.allowed()}")
                continue
            if raw == "/models":
                installed = model.installed()
                ui.update(message=f"Installed models: {', '.join(installed) if installed else 'none detected'}")
                continue
            if raw == "/model swap":
                current = input(f"  model [{model.model}] > ").strip()
                if current:
                    if not model.is_allowed(current):
                        ui.update(message=f"Blocked: {current} is not in the allowlist. Use /allowlist add first.")
                        continue
                    values = load()
                    values["model"] = current
                    save(values)
                    model = ModelClient()
                    ui.update(message=f"Saved model: {model.model}")
                continue
            if raw.startswith("/provider "):
                provider = raw[len("/provider "):].strip().lower()
                if provider not in {"ollama", "openrouter"}:
                    ui.update(message="Supported providers: ollama, openrouter")
                    continue
                values = load()
                values["provider"] = provider
                if "GRAM_API_URL" not in os.environ:
                    values["api_url"] = "https://openrouter.ai/api/v1/chat/completions" if provider == "openrouter" else "http://localhost:11434/api/chat"
                save(values)
                model = ModelClient()
                ui.update(message=f"Saved provider: {provider}. Allowed models: {model.allowed()}")
                continue
            if raw == "/allowlist show":
                ui.update(message=f"Allowed models: {model.allowed()}")
                continue
            if raw.startswith("/allowlist add "):
                candidate = raw[len("/allowlist add "):].strip()
                values = load()
                allowed = values.get("allowed_models", model.allowed_models.copy())
                if candidate and candidate not in allowed:
                    allowed.append(candidate)
                    values["allowed_models"] = allowed
                    save(values)
                    model = ModelClient()
                ui.update(message=f"Allowed models: {model.allowed()}")
                continue
            if raw.startswith("/allowlist remove "):
                candidate = raw[len("/allowlist remove "):].strip()
                values = load()
                allowed = [item for item in model.allowed_models if item != candidate]
                if not allowed:
                    ui.update(message="The allowlist cannot be empty.")
                    continue
                values["allowed_models"] = allowed
                save(values)
                model = ModelClient()
                ui.update(message=f"Allowed models: {model.allowed()}")
                continue
            if raw.startswith("/model set "):
                candidate = raw[len("/model set "):].strip()
                if not model.is_allowed(candidate):
                    ui.update(message=f"Blocked: {candidate} is not in the allowlist. Use /allowlist add first.")
                    continue
                values = load()
                values["model"] = candidate
                save(values)
                model = ModelClient()
                ui.update(message=f"Saved model: {model.model}")
                continue
            if raw.startswith("/endpoint set "):
                values = load()
                values["api_url"] = raw[len("/endpoint set "):].strip()
                save(values)
                model = ModelClient()
                ui.update(message=f"Saved endpoint: {model.url}")
                continue
            if raw.startswith("/vocab add ") and "=" in raw:
                source, target = raw[len("/vocab add "):].split("=", 1)
                values = load()
                vocabulary = values.get("corrections", {})
                if not isinstance(vocabulary, dict):
                    vocabulary = {}
                vocabulary[source.strip().lower()] = target.strip()
                values["corrections"] = vocabulary
                save(values)
                ui.update(message=f"Saved speech correction: {source.strip()} → {target.strip()}")
                continue
            if raw == "/audio system":
                os.environ["GRAM_AUDIO_SOURCE"] = "system"
                ui.update(message="System-audio mode is selected for the next launch.")
                continue
            if raw == "/demo":
                ui.update(state="listening", message="Demo audio active; no speech is being sent.")
                continue
            if raw == "/voice":
                ui.update(state="listening", transcript="", message="Listening… speak your request.")
                prompt = speech.listen_once()
                logger.info("Voice recognition returned=%s confidence=%r", bool(prompt), speech.last_confidence)
                if not prompt:
                    ui.update(state="warning", message="Windows speech recognition was unavailable. Use /listen for typed fallback.")
                    speech.speak("I couldn't hear a request. You can type it with the listen command.")
                    continue
                prompt = normalize(prompt, load().get("corrections", {}))
                logger.info("Normalized voice transcript: %s", prompt)
                confidence = speech.last_confidence
                ui.update(state="thinking", transcript=prompt, message=f"I heard you{f' ({confidence:.0%} confidence)' if isinstance(confidence, (int, float)) else ''}. Thinking…")
                try:
                    logger.info("Delivering voice request to model=%s provider=%s", model.model, model.provider)
                    handle_answer(model.complete(prompt))
                except Exception as exc:
                    logger.exception("Voice request failed")
                    ui.update(state="warning", message=f"Model error: {exc}")
                    speech.speak("I ran into a model error.")
                continue
            if raw == "/listen":
                ui.update(state="listening", transcript="", message="Speak or type the request, then press Enter.")
                prompt = input("  voice > ").strip()
                if not prompt:
                    ui.update(state="idle", message="No request captured.")
                    continue
                ui.update(state="thinking", transcript=prompt, message="Sending finalized request to the coding model…")
                try:
                    logger.info("Delivering typed request to model=%s provider=%s", model.model, model.provider)
                    handle_answer(model.complete(prompt))
                except Exception as exc:
                    logger.exception("Typed listen request failed")
                    ui.update(state="warning", message=f"Model error: {exc}")
                continue
            ui.update(state="thinking", transcript=raw, message="Typed request captured. Use /listen for voice mode.")
            try:
                logger.info("Delivering direct request to model=%s provider=%s", model.model, model.provider)
                handle_answer(model.complete(raw))
            except Exception as exc:
                logger.exception("Direct request failed")
                ui.update(state="warning", message=f"Model error: {exc}")
    except (KeyboardInterrupt, EOFError):
        pass
    finally:
        logger.info("Application shutting down")
        audio.stop()
        ui.close()
