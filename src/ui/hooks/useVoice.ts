import React, {
  useState,
  useRef,
  useEffect,
} from 'react';
import {
  emitSystemToast,
} from '../components/ui/toast';



export interface UseVoiceParams {
  activeRequestRef: React.RefObject<AbortController | null>;
  activeRunId: string | null;
  setIsExecuting: React.Dispatch<React.SetStateAction<boolean>>;
  prompt: string;
  setPrompt: React.Dispatch<React.SetStateAction<string>>;
  textareaRef: React.RefObject<HTMLTextAreaElement | null>;
  formRef: React.RefObject<HTMLFormElement | null>;
}

export function useVoice({ activeRequestRef, activeRunId, setIsExecuting, prompt, setPrompt, textareaRef, formRef }: UseVoiceParams) {
const [voiceMode, setVoiceMode] = useState(false);
  const [voiceRecording, setVoiceRecording] = useState(false);
  const [voiceError, setVoiceError] = useState<string | null>(null);
  const speechRequestRef = useRef<AbortController | null>(null);
  const speechAudioRef = useRef<HTMLAudioElement | null>(null);
  const speechUrlRef = useRef<string | null>(null);
  const recorderRef = useRef<MediaRecorder | null>(null);
  const speechRecognitionRef = useRef<any>(null);
  const voiceDraftBaseRef = useRef<string>('');
  const voiceSilenceTimerRef = useRef<number | null>(null);
  const voiceStreamRef = useRef<MediaStream | null>(null);
  const voiceChunksRef = useRef<Blob[]>([]);
  const [speakingMessageId, setSpeakingMessageId] = useState<string | null>(null);

  function stopSpeechPlayback() {
    speechRequestRef.current?.abort();
    speechRequestRef.current = null;
    if (speechAudioRef.current) {
      speechAudioRef.current.pause();
      speechAudioRef.current.src = '';
      speechAudioRef.current = null;
    }
    if (speechUrlRef.current) {
      URL.revokeObjectURL(speechUrlRef.current);
      speechUrlRef.current = null;
    }
    if (typeof window !== 'undefined' && 'speechSynthesis' in window) {
      try {
        window.speechSynthesis.cancel();
      } catch {}
    }
    setSpeakingMessageId(null);
  }

  function cancelActiveTurnForBargeIn() {
    stopSpeechPlayback();
    activeRequestRef.current?.abort();
    activeRequestRef.current = null;
    if (activeRunId) {
      void fetch('/api/cancel', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ runId: activeRunId }),
      }).catch(() => {});
    }
    setIsExecuting(false);
  }

  function speechSummary(raw: string): string {
    let summary = raw
      .replace(/<thinking>[\s\S]*?<\/thinking>/gi, ' ')
      .replace(/```[\s\S]*?```/g, ' ')
      .replace(/`[^`]*`/g, ' ')
      .replace(/\[([^\]]+)\]\([^)]*\)/g, '$1')
      .replace(/^\s*(?:diff --git|index |--- |\+\+\+ |@@ ).*$/gm, ' ')
      .replace(/^\s*[+-](?=\S).*$/gm, ' ')
      .replace(/^\s*#{1,6}\s*/gm, '')
      .replace(/[>*_~]/g, ' ')
      .replace(/\s+/g, ' ')
      .trim();
    if (summary.length > 1100) {
      summary = summary.slice(0, 1050).replace(/\s+\S*$/, '') + '. I can show the full response in the chat.';
    }
    return summary;
  }

  function playVoiceChime() {
    try {
      if (localStorage.getItem('gram.settings.playVoiceChime') !== 'true') return;
      const AudioCtx = window.AudioContext || (window as unknown as { webkitAudioContext: typeof AudioContext }).webkitAudioContext;
      const context = new AudioCtx();
      const oscillator = context.createOscillator();
      const gain = context.createGain();
      oscillator.frequency.value = 660;
      gain.gain.value = 0.035;
      oscillator.connect(gain);
      gain.connect(context.destination);
      oscillator.start();
      oscillator.stop(context.currentTime + 0.07);
      oscillator.onended = () => {
        void context.close().catch(() => {});
      };
    } catch {}
  }

  async function speakSummary(text: string, messageId?: string) {
    const summary = speechSummary(text);
    if (!summary) return;
    stopSpeechPlayback();
    setSpeakingMessageId(messageId || '__auto__');
    const controller = new AbortController();
    speechRequestRef.current = controller;
    let provider: string | undefined;
    let voiceId: string | undefined;
    let speed: number | undefined;
    try {
      provider = localStorage.getItem('gram.settings.ttsProvider') || undefined;
      voiceId = localStorage.getItem('gram.settings.ttsVoiceId') || localStorage.getItem('gram.settings.elevenLabsVoiceId') || undefined;
      const rawSpeed = Number(localStorage.getItem('gram.settings.elevenLabsSpeed'));
      if (Number.isFinite(rawSpeed) && rawSpeed > 0) speed = rawSpeed;
    } catch {}
    try {
      const response = await fetch('/api/voice/speak', {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ text: summary, provider, voiceId, speed }),
        signal: controller.signal,
      });
      if (!response.ok) {
        if (response.status === 503 && typeof window !== 'undefined' && 'speechSynthesis' in window) {
          const utterance = new SpeechSynthesisUtterance(summary);
          utterance.onend = () => setSpeakingMessageId(null);
          utterance.onerror = () => setSpeakingMessageId(null);
          window.speechSynthesis.speak(utterance);
          return;
        }
        throw new Error((await response.json().catch(() => null))?.error || `Speech request failed (${response.status}).`);
      }
      const audioBlob = await response.blob();
      if (controller.signal.aborted) return;
      const url = URL.createObjectURL(audioBlob);
      speechUrlRef.current = url;
      const audio = new Audio(url);
      speechAudioRef.current = audio;
      audio.onended = () => stopSpeechPlayback();
      await audio.play();
    } catch (error: any) {
      if (error?.name !== 'AbortError') {
        setSpeakingMessageId(null);
        const msg = error?.message || 'Could not play voice response.';
        setVoiceError(msg);
        emitSystemToast({ title: 'Voice playback failed', description: msg, variant: 'error' });
      }
    }
  }

  function stopLiveDictation() {
    if (voiceSilenceTimerRef.current) {
      window.clearTimeout(voiceSilenceTimerRef.current);
      voiceSilenceTimerRef.current = null;
    }
    if (speechRecognitionRef.current) {
      try {
        speechRecognitionRef.current.onend = null;
        speechRecognitionRef.current.stop();
      } catch {}
      speechRecognitionRef.current = null;
    }
    if (recorderRef.current?.state === 'recording') {
      recorderRef.current.stop();
    }
    setVoiceRecording(false);
  }

  async function toggleVoiceRecording() {
    setVoiceError(null);
    if (voiceRecording || speechRecognitionRef.current || recorderRef.current?.state === 'recording') {
      stopLiveDictation();
      playVoiceChime();
      return;
    }
    cancelActiveTurnForBargeIn();
    voiceDraftBaseRef.current = prompt.trim();

    const startMediaRecorderFallback = async () => {
      try {
        const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
        voiceStreamRef.current = stream;
        voiceChunksRef.current = [];
        const recorder = new MediaRecorder(stream);
        recorderRef.current = recorder;
        recorder.ondataavailable = (event) => { if (event.data.size) voiceChunksRef.current.push(event.data); };
        recorder.onstop = async () => {
          stream.getTracks().forEach((track) => track.stop());
          voiceStreamRef.current = null;
          recorderRef.current = null;
          setVoiceRecording(false);
          const audio = new Blob(voiceChunksRef.current, { type: recorder.mimeType || 'audio/webm' });
          voiceChunksRef.current = [];
          if (!audio.size) return;
          const controller = new AbortController();
          try {
            const response = await fetch('/api/voice/transcribe', { method: 'POST', headers: { 'Content-Type': audio.type }, body: audio, signal: controller.signal });
            const data = await response.json();
            if (!response.ok) throw new Error(data.error || `Transcription failed (${response.status}).`);
            const transcript = String(data.text || '').trim();
            if (transcript) {
              setPrompt([voiceDraftBaseRef.current, transcript].filter(Boolean).join(' '));
            }
            textareaRef.current?.focus();
          } catch (error: any) {
            if (error?.name !== 'AbortError') {
              const msg = error?.message || 'Transcription failed.';
              setVoiceError(msg);
              emitSystemToast({ title: 'Transcription failed', description: msg, variant: 'error' });
            }
          }
        };
        recorder.start();
        setVoiceRecording(true);
        playVoiceChime();
      } catch (error: any) {
        setVoiceRecording(false);
        const msg = error?.message || 'Microphone access is unavailable.';
        setVoiceError(msg);
        emitSystemToast({ title: 'Microphone unavailable', description: msg, variant: 'error' });
      }
    };

    const SpeechRec =
      (window as unknown as { SpeechRecognition?: any; webkitSpeechRecognition?: any }).SpeechRecognition ||
      (window as unknown as { webkitSpeechRecognition?: any }).webkitSpeechRecognition;

    if (SpeechRec) {
      try {
        const recognition = new SpeechRec();
        recognition.continuous = true;
        recognition.interimResults = true;
        recognition.lang = navigator.language || 'en-US';
        speechRecognitionRef.current = recognition;
        setVoiceRecording(true);
        playVoiceChime();
        textareaRef.current?.focus();

        recognition.onresult = (event: any) => {
          let finalPart = '';
          let interimPart = '';
          for (let i = 0; i < event.results.length; i++) {
            const segment = String(event.results[i]?.[0]?.transcript || '');
            if (event.results[i]?.isFinal) finalPart += segment;
            else interimPart += segment;
          }
          const spoken = `${finalPart}${interimPart}`.trim();
          setPrompt([voiceDraftBaseRef.current, spoken].filter(Boolean).join(' '));

          let autoSend = false;
          try {
            autoSend = localStorage.getItem('gram.settings.autoSendVoice') === 'true';
          } catch {}
          if (autoSend && finalPart.trim()) {
            if (voiceSilenceTimerRef.current) window.clearTimeout(voiceSilenceTimerRef.current);
            voiceSilenceTimerRef.current = window.setTimeout(() => {
              stopLiveDictation();
              formRef.current?.requestSubmit();
            }, 1300);
          }
        };

        recognition.onerror = (event: any) => {
          const code = String(event?.error || '');
          if (code === 'network' || code === 'service-not-allowed' || code === 'audio-capture') {
            stopLiveDictation();
            void startMediaRecorderFallback();
          } else if (code !== 'aborted' && code !== 'no-speech') {
            stopLiveDictation();
            setVoiceError(`Speech recognition error: ${code}`);
          }
        };

        recognition.onend = () => {
          if (speechRecognitionRef.current === recognition) {
            try {
              recognition.start();
            } catch {
              stopLiveDictation();
            }
          }
        };

        recognition.start();
        return;
      } catch {
        speechRecognitionRef.current = null;
      }
    }

    await startMediaRecorderFallback();
  }

  useEffect(() => () => {
    activeRequestRef.current?.abort();
    stopSpeechPlayback();
    stopLiveDictation();
    voiceStreamRef.current?.getTracks().forEach((track) => track.stop());
  }, []);

  return { voiceMode, speakSummary, stopSpeechPlayback, speakingMessageId, voiceError, voiceRecording, setVoiceMode, setVoiceError, toggleVoiceRecording };
}
