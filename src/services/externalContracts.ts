/**
 * Provider interfaces and registry for external-service-backed capabilities
 * (TTS, STT, Web Search, and Web Page Fetch) so Gram can swap cloud APIs for
 * local or self-hosted implementations without changing runtime or route code.
 */

export interface VoiceInfo {
  id: string;
  name: string;
  provider: string;
  locale?: string;
  category?: string;
  previewUrl?: string;
}

export interface SynthesizeSpeechOptions {
  provider?: string;
  voiceId?: string;
  modelId?: string;
  stability?: number;
  similarityBoost?: number;
  speed?: number;
}

export interface SpeechSynthesizer {
  readonly id: string;
  readonly label: string;
  isAvailable(): boolean | Promise<boolean>;
  listVoices(signal?: AbortSignal): Promise<{ defaultVoiceId?: string; voices: VoiceInfo[] }>;
  synthesize(text: string, signal?: AbortSignal, options?: SynthesizeSpeechOptions): Promise<Response>;
}

export interface TranscribeOptions {
  provider?: string;
  language?: string;
}

export interface SpeechTranscriber {
  readonly id: string;
  readonly label: string;
  isAvailable(): boolean | Promise<boolean>;
  transcribe(audio: Blob, signal?: AbortSignal, options?: TranscribeOptions): Promise<{ text: string; provider?: string }>;
}

export interface WebSearchResult {
  title: string;
  url: string;
  snippet: string;
  source: string;
}

export interface WebSearchProvider {
  readonly id: string;
  readonly label: string;
  isAvailable(query?: string): boolean;
  search(query: string, count: number, signal?: AbortSignal): Promise<WebSearchResult[]>;
}

export interface WebPageContent {
  title: string;
  url: string;
  content: string;
}

export interface WebPageFetcher {
  readonly id: string;
  fetchPage(url: string, signal?: AbortSignal): Promise<WebPageContent>;
}

export class ExternalServiceRegistry {
  private ttsProviders = new Map<string, SpeechSynthesizer>();
  private ttsOrder: string[] = [];

  private sttProviders = new Map<string, SpeechTranscriber>();
  private sttOrder: string[] = [];

  private searchProviders = new Map<string, WebSearchProvider>();
  private searchOrder: string[] = [];

  private webFetcher: WebPageFetcher | null = null;

  public registerTts(provider: SpeechSynthesizer, priority: 'append' | 'prepend' = 'append'): void {
    this.ttsProviders.set(provider.id, provider);
    this.ttsOrder = this.ttsOrder.filter((id) => id !== provider.id);
    if (priority === 'prepend') this.ttsOrder.unshift(provider.id);
    else this.ttsOrder.push(provider.id);
  }

  public getTts(id: string): SpeechSynthesizer | undefined {
    return this.ttsProviders.get(id);
  }

  public listTts(): SpeechSynthesizer[] {
    return this.ttsOrder.map((id) => this.ttsProviders.get(id)!).filter(Boolean);
  }

  public registerStt(provider: SpeechTranscriber, priority: 'append' | 'prepend' = 'append'): void {
    this.sttProviders.set(provider.id, provider);
    this.sttOrder = this.sttOrder.filter((id) => id !== provider.id);
    if (priority === 'prepend') this.sttOrder.unshift(provider.id);
    else this.sttOrder.push(provider.id);
  }

  public getStt(id: string): SpeechTranscriber | undefined {
    return this.sttProviders.get(id);
  }

  public listStt(): SpeechTranscriber[] {
    return this.sttOrder.map((id) => this.sttProviders.get(id)!).filter(Boolean);
  }

  public clearVoiceProviders(): void {
    this.ttsProviders.clear();
    this.ttsOrder = [];
    this.sttProviders.clear();
    this.sttOrder = [];
  }

  public registerWebSearch(provider: WebSearchProvider, priority: 'append' | 'prepend' = 'append'): void {
    this.searchProviders.set(provider.id, provider);
    this.searchOrder = this.searchOrder.filter((id) => id !== provider.id);
    if (priority === 'prepend') this.searchOrder.unshift(provider.id);
    else this.searchOrder.push(provider.id);
  }

  public clearWebSearch(): void {
    this.searchProviders.clear();
    this.searchOrder = [];
  }

  public listWebSearch(): WebSearchProvider[] {
    return this.searchOrder.map((id) => this.searchProviders.get(id)!).filter(Boolean);
  }

  public setWebFetcher(fetcher: WebPageFetcher): void {
    this.webFetcher = fetcher;
  }

  public getWebFetcher(): WebPageFetcher | null {
    return this.webFetcher;
  }
}

export const externalServices = new ExternalServiceRegistry();
