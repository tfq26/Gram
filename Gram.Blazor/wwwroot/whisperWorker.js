// Embedded Local Whisper Speech Recognition Web Worker using Transformers.js
import { pipeline, env } from 'https://cdn.jsdelivr.net/npm/@xenova/transformers@2.17.2';

// Configure transformers.js for browser client
env.allowLocalModels = false;
env.useBrowserCache = true;

class WhisperPipelineFactory {
    static task = 'automatic-speech-recognition';
    static model = 'Xenova/whisper-tiny.en';
    static instance = null;

    static async getInstance(progressCallback = null) {
        if (!this.instance) {
            this.instance = await pipeline(this.task, this.model, {
                progress_callback: progressCallback,
                quantized: true,
            });
        }
        return this.instance;
    }
}

self.addEventListener('message', async (event) => {
    const message = event.data;

    switch (message.type) {
        case 'load': {
            try {
                self.postMessage({ status: 'loading', message: 'Loading local Whisper model...' });
                
                await WhisperPipelineFactory.getInstance((progress) => {
                    self.postMessage({ 
                        status: 'downloading', 
                        file: progress.file, 
                        progress: progress.progress || 0,
                        total: progress.total,
                        loaded: progress.loaded
                    });
                });

                self.postMessage({ status: 'ready', message: 'Local Whisper model ready' });
            } catch (err) {
                console.error('[WhisperWorker] Error loading model:', err);
                self.postMessage({ status: 'error', error: err.message || String(err) });
            }
            break;
        }

        case 'transcribe': {
            try {
                const transcriber = await WhisperPipelineFactory.getInstance();
                const audio = message.audio; // Float32Array at 16000Hz

                if (!audio || audio.length === 0) {
                    self.postMessage({ status: 'complete', text: '' });
                    return;
                }

                const output = await transcriber(audio, {
                    language: 'english',
                    task: 'transcribe',
                    chunk_length_s: 30,
                    stride_length_s: 5,
                    return_timestamps: false
                });

                const cleanText = (output && output.text ? output.text : '').trim();
                self.postMessage({ status: 'complete', text: cleanText });
            } catch (err) {
                console.error('[WhisperWorker] Error during transcription:', err);
                self.postMessage({ status: 'error', error: err.message || String(err) });
            }
            break;
        }

        default:
            console.warn('[WhisperWorker] Unknown message type:', message.type);
    }
});
