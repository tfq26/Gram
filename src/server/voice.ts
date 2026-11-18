const MAX_VOICE_BYTES = 25 * 1024 * 1024;
const MAX_TRANSCRIPT_WAIT_MS = 120_000;

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
  for (const chunk of chunks) { bytes.set(chunk, offset); offset += chunk.byteLength; }
  return new Blob([bytes.buffer as ArrayBuffer], { type: req.headers.get('content-type') || 'application/octet-stream' });
}

function withTimeout(signal: AbortSignal | undefined, timeoutMs: number): AbortSignal {
  return signal ? AbortSignal.any([signal, AbortSignal.timeout(timeoutMs)]) : AbortSignal.timeout(timeoutMs);
}

export async function transcribeAudio(audio: Blob, signal?: AbortSignal): Promise<{ text: string }> {
  const apiKey = process.env.ASSEMBLYAI_API_KEY?.trim();
  if (!apiKey) throw new Error('ASSEMBLYAI_API_KEY is not configured on the Gram server.');
  if (!audio.size || audio.size > MAX_VOICE_BYTES) throw new Error(`Audio must be between 1 byte and ${MAX_VOICE_BYTES} bytes.`);

  const requestSignal = withTimeout(signal, MAX_TRANSCRIPT_WAIT_MS);
  const upload = await fetch('https://api.assemblyai.com/v2/upload', {
    method: 'POST',
    headers: { authorization: apiKey, 'content-type': audio.type || 'application/octet-stream' },
    body: audio,
    signal: requestSignal,
  });
  if (!upload.ok) throw new Error(`AssemblyAI audio upload failed (${upload.status}).`);
  const uploadData = await upload.json() as { upload_url?: string };
  if (!uploadData.upload_url) throw new Error('AssemblyAI did not return an upload URL.');

  const submitted = await fetch('https://api.assemblyai.com/v2/transcript', {
    method: 'POST',
    headers: { authorization: apiKey, 'content-type': 'application/json' },
    body: JSON.stringify({ audio_url: uploadData.upload_url, language_detection: true, punctuate: true, format_text: true }),
    signal: requestSignal,
  });
  if (!submitted.ok) throw new Error(`AssemblyAI transcription request failed (${submitted.status}).`);
  const job = await submitted.json() as { id?: string; status?: string };
  if (!job.id) throw new Error('AssemblyAI did not return a transcription job ID.');

  const deadline = Date.now() + MAX_TRANSCRIPT_WAIT_MS;
  while (Date.now() < deadline) {
    if (signal?.aborted) throw new DOMException('Transcription cancelled.', 'AbortError');
    const poll = await fetch(`https://api.assemblyai.com/v2/transcript/${encodeURIComponent(job.id)}`, {
      headers: { authorization: apiKey },
      signal: requestSignal,
    });
    if (!poll.ok) throw new Error(`AssemblyAI transcription poll failed (${poll.status}).`);
    const status = await poll.json() as { status?: string; text?: string | null; error?: string | null };
    if (status.status === 'completed') return { text: status.text || '' };
    if (status.status === 'error') throw new Error(status.error || 'AssemblyAI could not transcribe this audio.');
    await new Promise((resolve) => setTimeout(resolve, 1500));
  }
  throw new Error('Transcription timed out after two minutes.');
}

export interface ElevenLabsVoiceInfo {
  id: string;
  name: string;
  category?: string;
  previewUrl?: string;
}

export interface SynthesizeSpeechOptions {
  voiceId?: string;
  modelId?: string;
  stability?: number;
  similarityBoost?: number;
  speed?: number;
}

let cachedVoices: { at: number; voices: ElevenLabsVoiceInfo[] } | null = null;

export async function listElevenLabsVoices(signal?: AbortSignal): Promise<{
  configured: boolean;
  defaultVoiceId?: string;
  voices: ElevenLabsVoiceInfo[];
}> {
  const apiKey = process.env.ELEVENLABS_API_KEY?.trim();
  const defaultVoiceId = process.env.ELEVENLABS_VOICE_ID?.trim() || undefined;
  if (!apiKey) {
    return { configured: false, defaultVoiceId, voices: [] };
  }
  if (cachedVoices && Date.now() - cachedVoices.at < 5 * 60 * 1000) {
    return { configured: true, defaultVoiceId, voices: cachedVoices.voices };
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
  const voices: ElevenLabsVoiceInfo[] = (data.voices || [])
    .filter((v) => v.voice_id && v.name)
    .map((v) => ({
      id: String(v.voice_id),
      name: String(v.name),
      category: v.category,
      previewUrl: v.preview_url,
    }));
  cachedVoices = { at: Date.now(), voices };
  return { configured: true, defaultVoiceId, voices };
}

export async function synthesizeSpeech(
  text: string,
  signal?: AbortSignal,
  options: SynthesizeSpeechOptions = {}
): Promise<Response> {
  const apiKey = process.env.ELEVENLABS_API_KEY?.trim();
  const voiceId = (options.voiceId || process.env.ELEVENLABS_VOICE_ID || '').trim();
  const modelId = (options.modelId || process.env.ELEVENLABS_MODEL_ID || 'eleven_flash_v2_5').trim();
  if (!apiKey || !voiceId) throw new Error('ELEVENLABS_API_KEY and ELEVENLABS_VOICE_ID must be configured on the Gram server.');
  const spokenText = text.trim();
  if (!spokenText || spokenText.length > 4000) throw new Error('Speech text must contain 1 to 4000 characters.');
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
