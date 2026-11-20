import { afterEach, describe, expect, test } from 'bun:test';
import {
  externalServices,
  type SpeechSynthesizer,
  type SpeechTranscriber,
  type WebPageFetcher,
  type WebSearchProvider,
} from '../src/services/externalContracts';
import {
  listAvailableVoices,
  MacOsSaySpeechSynthesizer,
  parseMacOsSayVoices,
  registerDefaultVoiceProviders,
  synthesizeSpeech,
  transcribeAudio,
} from '../src/server/voice';
import { fetchWebContent, registerDefaultWebProviders, searchWeb } from '../src/runtime/webTools';

afterEach(() => {
  registerDefaultVoiceProviders();
  registerDefaultWebProviders();
});

describe('ExternalServiceRegistry & Voice interfaces', () => {
  test('parseMacOsSayVoices filters novelty voices and prioritizes English + Samantha', () => {
    const sample = [
      'Boing               en_US    # Hello! My name is Boing.',
      'Daniel              en_GB    # Hello! My name is Daniel.',
      'Amélie              fr_CA    # Bonjour! Je m’appelle Amélie.',
      'Samantha            en_US    # Hello! My name is Samantha.',
      'Zarvox              en_US    # Hello! My name is Zarvox.',
    ].join('\n');

    const parsed = parseMacOsSayVoices(sample);
    expect(parsed.map((v) => v.id)).toEqual(['Samantha', 'Daniel', 'Amélie']);
    expect(parsed[0].provider).toBe('macos-say');
  });

  test('custom SpeechSynthesizer and SpeechTranscriber can be swapped in', async () => {
    const mockTts: SpeechSynthesizer = {
      id: 'mock-local-tts',
      label: 'Mock Local TTS',
      isAvailable: () => true,
      listVoices: async () => ({
        defaultVoiceId: 'local-1',
        voices: [{ id: 'local-1', name: 'Local One', provider: 'mock-local-tts' }],
      }),
      synthesize: async (text) =>
        new Response(`audio:${text}`, { status: 200, headers: { 'Content-Type': 'audio/wav' } }),
    };

    const mockStt: SpeechTranscriber = {
      id: 'mock-local-stt',
      label: 'Mock Local STT',
      isAvailable: () => true,
      transcribe: async () => ({ text: 'hello from local stt', provider: 'mock-local-stt' }),
    };

    externalServices.registerTts(mockTts, 'prepend');
    externalServices.registerStt(mockStt, 'prepend');

    const catalog = await listAvailableVoices(undefined, 'mock-local-tts');
    expect(catalog.configured).toBe(true);
    expect(catalog.defaultProvider).toBe('mock-local-tts');
    expect(catalog.voices.map((v) => v.id)).toEqual(['local-1']);

    const speechRes = await synthesizeSpeech('Testing swap', undefined, { provider: 'mock-local-tts' });
    expect(speechRes.headers.get('content-type')).toBe('audio/wav');
    expect(await speechRes.text()).toBe('audio:Testing swap');

    const sttRes = await transcribeAudio(new Blob(['123'], { type: 'audio/wav' }), undefined, {
      provider: 'mock-local-stt',
    });
    expect(sttRes).toEqual({ text: 'hello from local stt', provider: 'mock-local-stt' });
  });

  if (process.platform === 'darwin') {
    test('MacOsSaySpeechSynthesizer produces a non-empty WAV response on macOS', async () => {
      const macTts = new MacOsSaySpeechSynthesizer();
      expect(macTts.isAvailable()).toBe(true);
      const res = await macTts.synthesize('Hello Gram', undefined, { voiceId: 'Samantha' });
      expect(res.status).toBe(200);
      expect(res.headers.get('content-type')).toBe('audio/wav');
      const buf = new Uint8Array(await res.arrayBuffer());
      expect(buf.byteLength).toBeGreaterThan(44);
      expect(String.fromCharCode(buf[0], buf[1], buf[2], buf[3])).toBe('RIFF');
    });
  }
});

describe('WebSearchProvider & WebPageFetcher interfaces', () => {
  test('searchWeb and fetchWebContent delegate to registered providers', async () => {
    const mockSearch: WebSearchProvider = {
      id: 'mock-searxng',
      label: 'Mock SearXNG',
      isAvailable: () => true,
      search: async (query) => [
        {
          title: `Result for ${query}`,
          url: 'https://example.com/docs',
          snippet: 'Swappable search provider works',
          source: 'Mock SearXNG',
        },
      ],
    };

    const mockFetcher: WebPageFetcher = {
      id: 'mock-fetcher',
      fetchPage: async (url) => ({
        title: 'Mock Page',
        url,
        content: 'Mocked page body',
      }),
    };

    externalServices.clearWebSearch();
    externalServices.registerWebSearch(mockSearch);
    externalServices.setWebFetcher(mockFetcher);

    const results = await searchWeb('unique-interface-swap-query', 5);
    expect(results).toHaveLength(1);
    expect(results[0].source).toBe('Mock SearXNG');

    const page = await fetchWebContent('https://example.com/docs');
    expect(page.title).toBe('Mock Page');
    expect(page.content).toBe('Mocked page body');
  });
});
