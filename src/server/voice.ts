import crypto from 'node:crypto';
import { existsSync } from 'node:fs';
import fs from 'node:fs/promises';
import os from 'node:os';
import path from 'node:path';
import {
  externalServices,
  type SpeechSynthesizer,
  type SpeechTranscriber,
  type SynthesizeSpeechOptions,
  type TranscribeOptions,
  type VoiceInfo,
} from '../services/externalContracts';

export type { SynthesizeSpeechOptions, TranscribeOptions, VoiceInfo };
export type ElevenLabsVoiceInfo = VoiceInfo;

const MAX_VOICE_BYTES = 25 * 1024 * 1024;
const MAX_TRANSCRIPT_WAIT_MS = 120_000;

const MACOS_NOVELTY_VOICES = new Set([
  'albert',
  'bad news',
  'bahh',
  'bells',
  'boing',
  'bubbles',
  'cellos',
  'fred',
  'good news',
  'jester',
  'junior',
  'kathy',
  'organ',
  'ralph',
  'superstar',
  'trinoids',
  'whisper',
  'wobble',
  'zarvox',
]);

export async function readAudioBody(req: Request): Promise<Blob> {
  if (!req.body) throw new Error('Audio request body is empty.');
  const reader = req.body.getReader();
  const chunks: Uint8Array[] = [];
  let size = 0;
  while (true) {
    const { done, value } = await reader.read();
    if (done) break;
    size += value.byteLength;
    if (size > MAX_VOICE_BYTES) {
      await reader.cancel();
      throw new Error(`Audio exceeds the ${MAX_VOICE_BYTES} byte upload limit.`);
    }
    chunks.push(value);
  }
  const bytes = new Uint8Array(size);
  let offset = 0;
  for (const chunk of chunks) {
    bytes.set(chunk, offset);
    offset += chunk.byteLength;
  }
  return new Blob([bytes.buffer as ArrayBuffer], {
    type: req.headers.get('content-type') || 'application/octet-stream',
  });
}

function withTimeout(signal: AbortSignal | undefined, timeoutMs: number): AbortSignal {
  return signal ? AbortSignal.any([signal, AbortSignal.timeout(timeoutMs)]) : AbortSignal.timeout(timeoutMs);
}

/** Parse output of `say -v '?'` into VoiceInfo entries, filtering novelty sound-effect voices. */
export function parseMacOsSayVoices(stdout: string): VoiceInfo[] {
  const voices: VoiceInfo[] = [];
  for (const line of stdout.split(/\r?\n/)) {
    const match = line.match(/^(.+?)\s{2,}([a-z]{2,3}_[A-Z0-9]{2,4})\s+#/);
    if (!match) continue;
    const name = match[1].trim();
    const locale = match[2].trim();
    if (!name || MACOS_NOVELTY_VOICES.has(name.toLowerCase())) continue;
    const isEnglish = locale.toLowerCase().startsWith('en_');
    voices.push({
      id: name,
      name: `${name} (${locale})`,
      provider: 'macos-say',
      locale,
      category: isEnglish ? 'english' : 'other',
    });
  }
  return voices.sort((a, b) => {
    const aEn = a.category === 'english' ? 0 : 1;
    const bEn = b.category === 'english' ? 0 : 1;
    if (aEn !== bEn) return aEn - bEn;
    if (a.id === 'Samantha') return -1;
    if (b.id === 'Samantha') return 1;
    return a.name.localeCompare(b.name);
  });
}

export class ElevenLabsSpeechSynthesizer implements SpeechSynthesizer {
  public readonly id = 'elevenlabs';
  public readonly label = 'ElevenLabs';
  private cachedVoices: { at: number; voices: VoiceInfo[] } | null = null;

  public isAvailable(): boolean {
    return Boolean(process.env.ELEVENLABS_API_KEY?.trim());
  }

  public async listVoices(signal?: AbortSignal): Promise<{ defaultVoiceId?: string; voices: VoiceInfo[] }> {
    const apiKey = process.env.ELEVENLABS_API_KEY?.trim();
    const defaultVoiceId = process.env.ELEVENLABS_VOICE_ID?.trim() || undefined;
    if (!apiKey) {
      return { defaultVoiceId, voices: [] };
    }
    if (this.cachedVoices && Date.now() - this.cachedVoices.at < 5 * 60 * 1000) {
      return { defaultVoiceId, voices: this.cachedVoices.voices };
    }
    const response = await fetch('https://api.elevenlabs.io/v1/voices', {
      headers: { 'xi-api-key': apiKey, accept: 'application/json' },
      signal: withTimeout(signal, 15_000),
    });
    if (!response.ok) {
      throw new Error(`ElevenLabs voices lookup failed (${response.status}).`);
    }
    const data = (await response.json()) as {
      voices?: Array<{ voice_id?: string; name?: string; category?: string; preview_url?: string }>;
    };
    const voices: VoiceInfo[] = (data.voices || [])
      .filter((v) => v.voice_id && v.name)
      .map((v) => ({
        id: String(v.voice_id),
        name: String(v.name),
        provider: this.id,
        category: v.category,
        previewUrl: v.preview_url,
      }));
    this.cachedVoices = { at: Date.now(), voices };
    return { defaultVoiceId, voices };
  }

  public async synthesize(
    text: string,
    signal?: AbortSignal,
    options: SynthesizeSpeechOptions = {}
  ): Promise<Response> {
    const apiKey = process.env.ELEVENLABS_API_KEY?.trim();
    const voiceId = (options.voiceId || process.env.ELEVENLABS_VOICE_ID || '').trim();
    const modelId = (options.modelId || process.env.ELEVENLABS_MODEL_ID || 'eleven_flash_v2_5').trim();
    if (!apiKey || !voiceId) {
      throw new Error('ELEVENLABS_API_KEY and ELEVENLABS_VOICE_ID must be configured on the Gram server.');
    }
    const spokenText = text.trim();
    if (!spokenText || spokenText.length > 4000) {
      throw new Error('Speech text must contain 1 to 4000 characters.');
    }
    const voiceSettings: Record<string, number> = {
      stability: typeof options.stability === 'number' ? options.stability : 0.45,
      similarity_boost: typeof options.similarityBoost === 'number' ? options.similarityBoost : 0.75,
    };
    if (typeof options.speed === 'number' && Number.isFinite(options.speed)) {
      voiceSettings.speed = Math.min(1.3, Math.max(0.7, options.speed));
    }
    const response = await fetch(
      `https://api.elevenlabs.io/v1/text-to-speech/${encodeURIComponent(voiceId)}/stream?output_format=mp3_22050_32&optimize_streaming_latency=3`,
      {
        method: 'POST',
        headers: { 'xi-api-key': apiKey, 'content-type': 'application/json', accept: 'audio/mpeg' },
        body: JSON.stringify({ text: spokenText, model_id: modelId, voice_settings: voiceSettings }),
        signal: withTimeout(signal, 60_000),
      }
    );
    if (!response.ok || !response.body) {
      const detail = await response.text().catch(() => '');
      throw new Error(`ElevenLabs speech generation failed (${response.status})${detail ? `: ${detail.slice(0, 300)}` : ''}.`);
    }
    return response;
  }
}

function resolveSayBinary(): string | null {
  if (process.platform !== 'darwin') return null;
  return Bun.which('say') || (existsSync('/usr/bin/say') ? '/usr/bin/say' : null);
}

export class MacOsSaySpeechSynthesizer implements SpeechSynthesizer {
  public readonly id = 'macos-say';
  public readonly label = 'macOS System (say)';
  private cachedVoices: { at: number; voices: VoiceInfo[] } | null = null;

  public isAvailable(): boolean {
    return Boolean(resolveSayBinary());
  }

  public async listVoices(signal?: AbortSignal): Promise<{ defaultVoiceId?: string; voices: VoiceInfo[] }> {
    const defaultVoiceId = process.env.MACOS_TTS_VOICE?.trim() || 'Samantha';
    const sayBin = resolveSayBinary();
    if (!sayBin) {
      return { defaultVoiceId, voices: [] };
    }
    if (this.cachedVoices && Date.now() - this.cachedVoices.at < 5 * 60 * 1000) {
      return { defaultVoiceId, voices: this.cachedVoices.voices };
    }
    const proc = Bun.spawn([sayBin, '-v', '?'], { stdout: 'pipe', stderr: 'pipe' });
    const onAbort = () => {
      try { proc.kill(); } catch {}
    };
    signal?.addEventListener('abort', onAbort, { once: true });
    try {
      const stdout = await new Response(proc.stdout).text();
      const code = await proc.exited;
      if (code !== 0) {
        const err = await new Response(proc.stderr).text();
        throw new Error(`macOS say failed to list voices: ${err.trim() || `exit ${code}`}`);
      }
      const voices = parseMacOsSayVoices(stdout);
      this.cachedVoices = { at: Date.now(), voices };
      return { defaultVoiceId, voices };
    } finally {
      signal?.removeEventListener('abort', onAbort);
    }
  }

  public async synthesize(
    text: string,
    signal?: AbortSignal,
    options: SynthesizeSpeechOptions = {}
  ): Promise<Response> {
    const sayBin = resolveSayBinary();
    if (!sayBin) {
      throw new Error('macOS say TTS is only available on macOS.');
    }
    const spokenText = text.trim();
    if (!spokenText || spokenText.length > 4000) {
      throw new Error('Speech text must contain 1 to 4000 characters.');
    }
    const voiceId = (options.voiceId || process.env.MACOS_TTS_VOICE || 'Samantha').trim();
    const rateWpm =
      typeof options.speed === 'number' && Number.isFinite(options.speed)
        ? Math.round(185 * Math.min(1.6, Math.max(0.6, options.speed)))
        : 185;
    const tmpFile = path.join(os.tmpdir(), `gram-say-${crypto.randomUUID()}.wav`);
    const args = [sayBin, '-v', voiceId, '-r', String(rateWpm), '-o', tmpFile, '--data-format=LEI16@22050', '--', spokenText];
    const proc = Bun.spawn(args, { stdout: 'pipe', stderr: 'pipe' });
    const onAbort = () => {
      try { proc.kill(); } catch {}
    };
    signal?.addEventListener('abort', onAbort, { once: true });
    try {
      const code = await proc.exited;
      if (signal?.aborted) {
        throw new DOMException('Speech generation cancelled.', 'AbortError');
      }
      if (code !== 0) {
        const err = await new Response(proc.stderr).text();
        throw new Error(`macOS say synthesis failed (${code}): ${err.trim() || 'unknown error'}`);
      }
      const bytes = await fs.readFile(tmpFile);
      return new Response(bytes, {
        status: 200,
        headers: { 'Content-Type': 'audio/wav', 'Cache-Control': 'no-store' },
      });
    } finally {
      signal?.removeEventListener('abort', onAbort);
      await fs.unlink(tmpFile).catch(() => {});
    }
  }
}

export class AssemblyAiSpeechTranscriber implements SpeechTranscriber {
  public readonly id = 'assemblyai';
  public readonly label = 'AssemblyAI';

  public isAvailable(): boolean {
    return Boolean(process.env.ASSEMBLYAI_API_KEY?.trim());
  }

  public async transcribe(
    audio: Blob,
    signal?: AbortSignal,
    _options: TranscribeOptions = {}
  ): Promise<{ text: string; provider: string }> {
    const apiKey = process.env.ASSEMBLYAI_API_KEY?.trim();
    if (!apiKey) throw new Error('ASSEMBLYAI_API_KEY is not configured on the Gram server.');
    if (!audio.size || audio.size > MAX_VOICE_BYTES) {
      throw new Error(`Audio must be between 1 byte and ${MAX_VOICE_BYTES} bytes.`);
    }

    const requestSignal = withTimeout(signal, MAX_TRANSCRIPT_WAIT_MS);
    const upload = await fetch('https://api.assemblyai.com/v2/upload', {
      method: 'POST',
      headers: { authorization: apiKey, 'content-type': audio.type || 'application/octet-stream' },
      body: audio,
      signal: requestSignal,
    });
    if (!upload.ok) throw new Error(`AssemblyAI audio upload failed (${upload.status}).`);
    const uploadData = (await upload.json()) as { upload_url?: string };
    if (!uploadData.upload_url) throw new Error('AssemblyAI did not return an upload URL.');

    const submitted = await fetch('https://api.assemblyai.com/v2/transcript', {
      method: 'POST',
      headers: { authorization: apiKey, 'content-type': 'application/json' },
      body: JSON.stringify({ audio_url: uploadData.upload_url, language_detection: true, punctuate: true, format_text: true }),
      signal: requestSignal,
    });
    if (!submitted.ok) throw new Error(`AssemblyAI transcription request failed (${submitted.status}).`);
    const job = (await submitted.json()) as { id?: string; status?: string };
    if (!job.id) throw new Error('AssemblyAI did not return a transcription job ID.');

    const deadline = Date.now() + MAX_TRANSCRIPT_WAIT_MS;
    while (Date.now() < deadline) {
      if (signal?.aborted) throw new DOMException('Transcription cancelled.', 'AbortError');
      const poll = await fetch(`https://api.assemblyai.com/v2/transcript/${encodeURIComponent(job.id)}`, {
        headers: { authorization: apiKey },
        signal: requestSignal,
      });
      if (!poll.ok) throw new Error(`AssemblyAI transcription poll failed (${poll.status}).`);
      const status = (await poll.json()) as { status?: string; text?: string | null; error?: string | null };
      if (status.status === 'completed') return { text: status.text || '', provider: this.id };
      if (status.status === 'error') throw new Error(status.error || 'AssemblyAI could not transcribe this audio.');
      await new Promise((resolve) => setTimeout(resolve, 1500));
    }
    throw new Error('Transcription timed out after two minutes.');
  }
}

export class LocalWhisperSpeechTranscriber implements SpeechTranscriber {
  public readonly id = 'local-whisper';
  public readonly label = 'Local Whisper CLI';

  public isAvailable(): boolean {
    const hasBinary = Boolean(Bun.which('whisper-cpp') || Bun.which('whisper'));
    const hasModel = Boolean(process.env.WHISPER_CPP_MODEL?.trim() || Bun.which('whisper'));
    return hasBinary && hasModel;
  }

  public async transcribe(
    audio: Blob,
    signal?: AbortSignal,
    _options: TranscribeOptions = {}
  ): Promise<{ text: string; provider: string }> {
    const whisperCpp = Bun.which('whisper-cpp');
    const whisperPy = Bun.which('whisper');
    const modelPath = process.env.WHISPER_CPP_MODEL?.trim();
    if (!whisperCpp && !whisperPy) {
      throw new Error('Local Whisper CLI (whisper-cpp or whisper) is not installed.');
    }
    if (!audio.size || audio.size > MAX_VOICE_BYTES) {
      throw new Error(`Audio must be between 1 byte and ${MAX_VOICE_BYTES} bytes.`);
    }

    const base = path.join(os.tmpdir(), `gram-stt-${crypto.randomUUID()}`);
    const inputPath = `${base}.input`;
    const wavPath = `${base}.wav`;
    try {
      await fs.writeFile(inputPath, new Uint8Array(await audio.arrayBuffer()));
      let targetPath = inputPath;
      if (Bun.which('ffmpeg')) {
        const conv = Bun.spawn(
          ['ffmpeg', '-y', '-i', inputPath, '-ar', '16000', '-ac', '1', '-c:a', 'pcm_s16le', wavPath],
          { stdout: 'pipe', stderr: 'pipe' }
        );
        if ((await conv.exited) === 0) targetPath = wavPath;
      }

      if (whisperCpp && modelPath) {
        const proc = Bun.spawn([whisperCpp, '-m', modelPath, '-f', targetPath, '-nt'], {
          stdout: 'pipe',
          stderr: 'pipe',
        });
        const onAbort = () => { try { proc.kill(); } catch {} };
        signal?.addEventListener('abort', onAbort, { once: true });
        try {
          const out = await new Response(proc.stdout).text();
          const code = await proc.exited;
          if (code !== 0) throw new Error(`whisper-cpp exited with status ${code}.`);
          return { text: out.trim(), provider: this.id };
        } finally {
          signal?.removeEventListener('abort', onAbort);
        }
      }

      const outDir = path.dirname(base);
      const proc = Bun.spawn(
        [whisperPy!, targetPath, '--model', process.env.WHISPER_MODEL || 'base', '--output_format', 'txt', '--output_dir', outDir],
        { stdout: 'pipe', stderr: 'pipe' }
      );
      const code = await proc.exited;
      if (code !== 0) throw new Error(`whisper exited with status ${code}.`);
      const txtPath = `${targetPath.replace(/\.[^.]+$/, '')}.txt`;
      const text = await fs.readFile(txtPath, 'utf8').catch(() => '');
      await fs.unlink(txtPath).catch(() => {});
      return { text: text.trim(), provider: this.id };
    } finally {
      await fs.unlink(inputPath).catch(() => {});
      await fs.unlink(wavPath).catch(() => {});
    }
  }
}

export function registerDefaultVoiceProviders(): void {
  externalServices.clearVoiceProviders();
  externalServices.registerTts(new ElevenLabsSpeechSynthesizer());
  externalServices.registerTts(new MacOsSaySpeechSynthesizer());
  externalServices.registerStt(new AssemblyAiSpeechTranscriber());
  externalServices.registerStt(new LocalWhisperSpeechTranscriber());
}

registerDefaultVoiceProviders();

async function resolveTtsProvider(requested?: string): Promise<SpeechSynthesizer> {
  const preferred = (requested || process.env.GRAM_TTS_PROVIDER || 'auto').trim();
  if (preferred && preferred !== 'auto') {
    const explicit = externalServices.getTts(preferred);
    if (!explicit) throw new Error(`Unknown TTS provider: ${preferred}`);
    if (!(await explicit.isAvailable())) {
      throw new Error(`TTS provider "${explicit.label}" is not configured or available.`);
    }
    return explicit;
  }
  for (const candidate of externalServices.listTts()) {
    if (await candidate.isAvailable()) return candidate;
  }
  throw new Error('No TTS provider is configured or available on the Gram server.');
}

async function resolveSttProvider(requested?: string): Promise<SpeechTranscriber> {
  const preferred = (requested || process.env.GRAM_STT_PROVIDER || 'auto').trim();
  if (preferred && preferred !== 'auto') {
    const explicit = externalServices.getStt(preferred);
    if (!explicit) throw new Error(`Unknown STT provider: ${preferred}`);
    if (!(await explicit.isAvailable())) {
      throw new Error(`STT provider "${explicit.label}" is not configured or available.`);
    }
    return explicit;
  }
  for (const candidate of externalServices.listStt()) {
    if (await candidate.isAvailable()) return candidate;
  }
  throw new Error('No STT provider is configured or available on the Gram server.');
}

export async function transcribeAudio(
  audio: Blob,
  signal?: AbortSignal,
  options: TranscribeOptions = {}
): Promise<{ text: string; provider?: string }> {
  const provider = await resolveSttProvider(options.provider);
  return provider.transcribe(audio, signal, options);
}

export async function listAvailableVoices(
  signal?: AbortSignal,
  requestedProvider?: string
): Promise<{
  configured: boolean;
  defaultProvider?: string;
  defaultVoiceId?: string;
  providers: Array<{ id: string; label: string; available: boolean }>;
  voices: VoiceInfo[];
}> {
  const allTts = externalServices.listTts();
  const providers: Array<{ id: string; label: string; available: boolean }> = [];
  const voices: VoiceInfo[] = [];
  let defaultProvider: string | undefined;
  let defaultVoiceId: string | undefined;

  for (const tts of allTts) {
    const available = await tts.isAvailable();
    providers.push({ id: tts.id, label: tts.label, available });
    if (!available) continue;
    if (requestedProvider && requestedProvider !== 'auto' && tts.id !== requestedProvider) continue;
    try {
      const listed = await tts.listVoices(signal);
      voices.push(...listed.voices);
      if (!defaultProvider) {
        defaultProvider = tts.id;
        defaultVoiceId = listed.defaultVoiceId;
      }
    } catch (error) {
      if (requestedProvider && requestedProvider !== 'auto') throw error;
    }
  }

  return {
    configured: providers.some((p) => p.available),
    defaultProvider,
    defaultVoiceId,
    providers,
    voices,
  };
}

export async function listElevenLabsVoices(signal?: AbortSignal): Promise<{
  configured: boolean;
  defaultVoiceId?: string;
  voices: VoiceInfo[];
}> {
  const eleven = externalServices.getTts('elevenlabs');
  if (!eleven || !(await eleven.isAvailable())) {
    return {
      configured: false,
      defaultVoiceId: process.env.ELEVENLABS_VOICE_ID?.trim() || undefined,
      voices: [],
    };
  }
  const listed = await eleven.listVoices(signal);
  return { configured: true, defaultVoiceId: listed.defaultVoiceId, voices: listed.voices };
}

export async function synthesizeSpeech(
  text: string,
  signal?: AbortSignal,
  options: SynthesizeSpeechOptions = {}
): Promise<Response> {
  const provider = await resolveTtsProvider(options.provider);
  return provider.synthesize(text, signal, options);
}
