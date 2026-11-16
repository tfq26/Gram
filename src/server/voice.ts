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

export async function synthesizeSpeech(text: string, signal?: AbortSignal): Promise<Response> {
  const apiKey = process.env.ELEVENLABS_API_KEY?.trim();
  const voiceId = process.env.ELEVENLABS_VOICE_ID?.trim();
  const modelId = process.env.ELEVENLABS_MODEL_ID?.trim() || 'eleven_flash_v2_5';
  if (!apiKey || !voiceId) throw new Error('ELEVENLABS_API_KEY and ELEVENLABS_VOICE_ID must be configured on the Gram server.');
  const spokenText = text.trim();
  if (!spokenText || spokenText.length > 4000) throw new Error('Speech text must contain 1 to 4000 characters.');
  const response = await fetch(`https://api.elevenlabs.io/v1/text-to-speech/${encodeURIComponent(voiceId)}/stream?output_format=mp3_22050_32`, {
    method: 'POST',
    headers: { 'xi-api-key': apiKey, 'content-type': 'application/json', accept: 'audio/mpeg' },
    body: JSON.stringify({ text: spokenText, model_id: modelId }),
    signal: withTimeout(signal, 60_000),
  });
  if (!response.ok || !response.body) {
    const detail = await response.text().catch(() => '');
    throw new Error(`ElevenLabs speech generation failed (${response.status})${detail ? `: ${detail.slice(0, 300)}` : ''}.`);
  }
  return response;
}
