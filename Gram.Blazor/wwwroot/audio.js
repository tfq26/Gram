// Audio visualizer and Speech-to-Text integration for Gram with Embedded Local Whisper ML Model

window.gramAudio = {
    audioContext: null,
    analyser: null,
    mediaStream: null,
    sourceNode: null,
    processorNode: null,
    pcmBuffers: [],
    animationFrameId: null,
    recognition: null,
    whisperWorker: null,
    isWhisperReady: false,
    dotNetRef: null,
    isListening: false,
    speechTranscript: '',
    lastRecognitionError: '',
    lastWhisperError: '',
    lastWhisperTranscript: '',
    whisperTranscriptionResolver: null,
    whisperAwaitingResult: false,
    audioLevel: 0,
    peakAudioLevel: 0,
    capturedSamples: 0,
    outputDeviceId: '',
    assemblySocket: null,
    assemblyAudioSink: null,
    assemblyTranscript: '',
    assemblyError: '',
    serverSpeechAudio: null,
    serverSpeechUrl: null,
    speechRequestController: null,

    initWhisper: function () {
        if (this.whisperWorker) return;

        try {
            this.whisperWorker = new Worker('/whisperWorker.js', { type: 'module' });

            this.whisperWorker.onmessage = (e) => {
                const data = e.data;
                if (data.status === 'ready') {
                    this.isWhisperReady = true;
                    if (this.dotNetRef) {
                        this.dotNetRef.invokeMethodAsync('OnWhisperStatusChanged', 'Ready');
                    }
                } else if (data.status === 'downloading') {
                    const pct = Math.round((data.progress || 0));
                    if (this.dotNetRef) {
                        this.dotNetRef.invokeMethodAsync('OnWhisperStatusChanged', `Downloading (${pct}%)`);
                    }
                } else if (data.status === 'complete') {
                    const whisperText = (data.text || '').trim();
                    this.lastWhisperTranscript = whisperText;
                    if (this.whisperAwaitingResult && whisperText && this.dotNetRef) {
                        this.dotNetRef.invokeMethodAsync('OnTranscriptReceived', whisperText);
                    }
                    if (this.whisperTranscriptionResolver) {
                        const resolve = this.whisperTranscriptionResolver;
                        this.whisperTranscriptionResolver = null;
                        this.whisperAwaitingResult = false;
                        resolve(whisperText);
                    }
                } else if (data.status === 'error') {
                    this.lastWhisperError = data.error || 'Whisper worker error';
                    if (this.whisperTranscriptionResolver) {
                        const resolve = this.whisperTranscriptionResolver;
                        this.whisperTranscriptionResolver = null;
                        this.whisperAwaitingResult = false;
                        resolve('');
                    }
                }
            };

            this.whisperWorker.postMessage({ type: 'load' });
        } catch (err) {
            console.warn('[gramAudio] Web Worker initialization notice:', err);
        }
    },

    startVoice: async function (dotNetHelper, visualizerContainerId, inputDeviceId) {
        this.dotNetRef = dotNetHelper;
        this.pcmBuffers = [];
        this.speechTranscript = '';
        this.lastRecognitionError = '';
        this.lastWhisperError = '';
        this.lastWhisperTranscript = '';
        this.peakAudioLevel = 0;
        this.capturedSamples = 0;
        this.initWhisper();

        try {
            // 1. Initialize Microphone Audio Stream & Analyser
            const AudioCtx = window.AudioContext || window.webkitAudioContext;
            this.audioContext = new AudioCtx({ sampleRate: 16000 });
            
            if (this.audioContext.state === 'suspended') {
                await this.audioContext.resume();
            }

            const audio = inputDeviceId
                ? { deviceId: { exact: inputDeviceId } }
                : true;
            this.mediaStream = await navigator.mediaDevices.getUserMedia({ audio, video: false });
            this.sourceNode = this.audioContext.createMediaStreamSource(this.mediaStream);
            this.analyser = this.audioContext.createAnalyser();
            this.analyser.fftSize = 64;
            this.analyser.smoothingTimeConstant = 0.8;
            this.sourceNode.connect(this.analyser);

            // 2. Setup Audio Buffer Capture for local Whisper
            if (this.audioContext.createScriptProcessor) {
                this.processorNode = this.audioContext.createScriptProcessor(4096, 1, 1);
                this.processorNode.onaudioprocess = (e) => {
                    if (!this.isListening) return;
                    const inputData = e.inputBuffer.getChannelData(0);
                    // Clone buffer chunk
                    this.pcmBuffers.push(new Float32Array(inputData));
                    this.capturedSamples += inputData.length;
                    let sumSquares = 0;
                    for (let i = 0; i < inputData.length; i++) {
                        sumSquares += inputData[i] * inputData[i];
                    }
                    const rmsLevel = Math.min(1, Math.sqrt(sumSquares / Math.max(1, inputData.length)) * 4);
                    this.peakAudioLevel = Math.max(this.peakAudioLevel, rmsLevel);
                };
                this.sourceNode.connect(this.processorNode);
                this.processorNode.connect(this.audioContext.destination);
            }

            this.isListening = true;
            this.startVisualizerLoop(visualizerContainerId);

            // 3. Initialize Speech Recognition (Instant typing fallback)
            const SpeechRec = window.SpeechRecognition || window.webkitSpeechRecognition;
            if (SpeechRec) {
                this.recognition = new SpeechRec();
                this.recognition.continuous = true;
                this.recognition.interimResults = true;
                this.recognition.lang = navigator.language || 'en-US';

                this.recognition.onresult = (event) => {
                    let transcript = '';
                    for (let i = 0; i < event.results.length; i++) {
                        transcript += event.results[i][0].transcript;
                    }
                    transcript = transcript.trim();
                    if (transcript) {
                        this.speechTranscript = transcript;
                    }
                    if (this.dotNetRef) {
                        this.dotNetRef.invokeMethodAsync('OnTranscriptReceived', transcript);
                    }
                };

                    this.recognition.onerror = (event) => {
                    this.lastRecognitionError = event.error || 'Speech recognition error';
                    console.warn('Speech recognition notice:', event.error);
                };

                this.recognition.onend = () => {
                    if (this.isListening) {
                        try {
                            this.recognition.start();
                        } catch (e) {}
                    }
                };

                this.recognition.start();
            }

            return true;
        } catch (err) {
            this.lastWhisperError = err?.message || String(err);
            console.error('Error starting microphone or recognition:', err);
            this.stopVoice(visualizerContainerId);
            return false;
        }
    },

    getDiagnostics: function () {
        return {
            speechRecognitionSupported: !!(window.SpeechRecognition || window.webkitSpeechRecognition),
            mediaDevicesSupported: !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia),
            audioContextSupported: !!(window.AudioContext || window.webkitAudioContext),
            whisperWorkerCreated: !!this.whisperWorker,
            whisperReady: this.isWhisperReady,
            isListening: this.isListening,
            speechTranscript: this.speechTranscript || '',
            lastWhisperTranscript: this.lastWhisperTranscript || '',
            lastRecognitionError: this.lastRecognitionError || '',
            lastWhisperError: this.lastWhisperError || '',
            audioLevel: this.audioLevel || 0,
            peakAudioLevel: this.peakAudioLevel || 0,
            capturedAudioSeconds: (this.capturedSamples || 0) / 16000,
            assemblyAiConnected: !!(this.assemblySocket && this.assemblySocket.readyState === WebSocket.OPEN),
            assemblyTranscript: this.assemblyTranscript || '',
            assemblyError: this.assemblyError || ''
        };
    },

    startAssemblyAiVoice: async function (dotNetHelper, visualizerContainerId, inputDeviceId) {
        this.dotNetRef = dotNetHelper;
        this.assemblyTranscript = '';
        this.assemblyError = '';
        this.peakAudioLevel = 0;
        this.capturedSamples = 0;

        try {
            const protocol = window.location.protocol === 'https:' ? 'wss:' : 'ws:';
            const socket = new WebSocket(`${protocol}//${window.location.host}/api/voice/assemblyai`);
            socket.binaryType = 'arraybuffer';
            this.assemblySocket = socket;

            socket.onmessage = (event) => {
                try {
                    const message = JSON.parse(event.data);
                    if (message.type === 'transcript' && message.text) {
                        this.assemblyTranscript = message.text.trim();
                        if (this.dotNetRef) {
                            this.dotNetRef.invokeMethodAsync('OnTranscriptReceived', this.assemblyTranscript);
                            this.dotNetRef.invokeMethodAsync('OnAssemblyAiTranscriptReceived', this.assemblyTranscript, !!message.isFinal);
                            this.dotNetRef.invokeMethodAsync('OnAssemblyAiStatusChanged', message.isFinal ? 'Final transcript received' : 'Partial transcript received');
                        }
                    } else if (message.type === 'status' && this.dotNetRef) {
                        this.dotNetRef.invokeMethodAsync('OnAssemblyAiStatusChanged', message.status || 'AssemblyAI connected');
                    } else if (message.type === 'error') {
                        this.assemblyError = message.message || 'AssemblyAI connection error';
                        if (this.dotNetRef) this.dotNetRef.invokeMethodAsync('OnAssemblyAiStatusChanged', this.assemblyError);
                    }
                } catch (e) {
                    console.warn('[gramAudio] Invalid AssemblyAI message:', e);
                }
            };

            await new Promise((resolve, reject) => {
                const timeout = setTimeout(() => reject(new Error('AssemblyAI connection timed out')), 10000);
                socket.onopen = () => {
                    clearTimeout(timeout);
                    resolve();
                };
                socket.onerror = () => {
                    clearTimeout(timeout);
                    reject(new Error('Could not connect to AssemblyAI'));
                };
                socket.onclose = (event) => {
                    if (socket.readyState !== WebSocket.OPEN) {
                        clearTimeout(timeout);
                        reject(new Error(event.reason || `AssemblyAI closed the connection (${event.code})`));
                    }
                };
            });

            const AudioCtx = window.AudioContext || window.webkitAudioContext;
            this.audioContext = new AudioCtx({ sampleRate: 16000 });
            if (this.audioContext.state === 'suspended') await this.audioContext.resume();

            const audio = inputDeviceId ? { deviceId: { exact: inputDeviceId } } : true;
            this.mediaStream = await navigator.mediaDevices.getUserMedia({ audio, video: false });
            this.sourceNode = this.audioContext.createMediaStreamSource(this.mediaStream);
            this.analyser = this.audioContext.createAnalyser();
            this.analyser.fftSize = 64;
            this.analyser.smoothingTimeConstant = 0.8;
            this.sourceNode.connect(this.analyser);

            this.processorNode = this.audioContext.createScriptProcessor(4096, 1, 1);
            this.processorNode.onaudioprocess = (event) => {
                if (!this.isListening || !this.assemblySocket || this.assemblySocket.readyState !== WebSocket.OPEN) return;

                const inputData = event.inputBuffer.getChannelData(0);
                const inputRate = this.audioContext.sampleRate || 16000;
                const outputLength = Math.max(1, Math.round(inputData.length * 16000 / inputRate));
                const pcm16 = new Int16Array(outputLength);
                let sumSquares = 0;

                for (let i = 0; i < outputLength; i++) {
                    const sourceIndex = Math.min(inputData.length - 1, Math.floor(i * inputRate / 16000));
                    const sample = inputData[sourceIndex] || 0;
                    sumSquares += sample * sample;
                    pcm16[i] = sample < 0 ? sample * 0x8000 : sample * 0x7fff;
                }

                this.capturedSamples += outputLength;
                const rmsLevel = Math.min(1, Math.sqrt(sumSquares / Math.max(1, outputLength)) * 4);
                this.peakAudioLevel = Math.max(this.peakAudioLevel, rmsLevel);

                if (this.assemblySocket.bufferedAmount < 512000) {
                    this.assemblySocket.send(pcm16.buffer);
                }
            };

            this.assemblyAudioSink = this.audioContext.createGain();
            this.assemblyAudioSink.gain.value = 0;
            this.sourceNode.connect(this.processorNode);
            this.processorNode.connect(this.assemblyAudioSink);
            this.assemblyAudioSink.connect(this.audioContext.destination);

            this.isListening = true;
            this.startVisualizerLoop(visualizerContainerId);
            return true;
        } catch (err) {
            this.assemblyError = err?.message || String(err);
            if (this.dotNetRef) this.dotNetRef.invokeMethodAsync('OnAssemblyAiStatusChanged', this.assemblyError);
            await this.stopAssemblyAiVoice(visualizerContainerId);
            return false;
        }
    },

    stopAssemblyAiVoice: async function (visualizerContainerId) {
        this.isListening = false;

        if (this.animationFrameId) {
            cancelAnimationFrame(this.animationFrameId);
            this.animationFrameId = null;
        }

        const socket = this.assemblySocket;
        if (socket && socket.readyState === WebSocket.OPEN) {
            try {
                socket.send(JSON.stringify({ type: 'terminate' }));
                await new Promise(resolve => setTimeout(resolve, 800));
                socket.close();
            } catch (e) {}
        }
        this.assemblySocket = null;

        if (this.processorNode) {
            try { this.processorNode.disconnect(); } catch (e) {}
            this.processorNode = null;
        }
        if (this.assemblyAudioSink) {
            try { this.assemblyAudioSink.disconnect(); } catch (e) {}
            this.assemblyAudioSink = null;
        }
        if (this.mediaStream) {
            this.mediaStream.getTracks().forEach(track => track.stop());
            this.mediaStream = null;
        }
        if (this.sourceNode) {
            try { this.sourceNode.disconnect(); } catch (e) {}
            this.sourceNode = null;
        }
        if (this.audioContext) {
            try { await this.audioContext.close(); } catch (e) {}
            this.audioContext = null;
        }

        this.resetBars(visualizerContainerId);
        return this.assemblyTranscript || '';
    },

    restartAssemblyAiVoice: async function (dotNetHelper, visualizerContainerId, inputDeviceId) {
        await this.stopAssemblyAiVoice(visualizerContainerId);
        return await this.startAssemblyAiVoice(dotNetHelper, visualizerContainerId, inputDeviceId);
    },

    restartVoiceCapture: async function (dotNetHelper, visualizerContainerId, inputDeviceId) {
        await this.stopVoice(visualizerContainerId);
        return await this.startVoice(dotNetHelper, visualizerContainerId, inputDeviceId);
    },

    getAudioDevices: async function () {
        if (!navigator.mediaDevices?.enumerateDevices) return [];

        const devices = await navigator.mediaDevices.enumerateDevices();
        return devices
            .filter(device => device.kind === 'audioinput' || device.kind === 'audiooutput')
            .map(device => ({
                kind: device.kind,
                deviceId: device.deviceId,
                label: device.label || ''
            }));
    },

    setOutputDevice: function (deviceId) {
        this.outputDeviceId = deviceId || '';
    },

    startVisualizerLoop: function (visualizerContainerId) {
        const bufferLength = this.analyser.frequencyBinCount;
        const dataArray = new Uint8Array(bufferLength);

        const updateBars = () => {
            if (!this.isListening || !this.analyser) {
                this.resetBars(visualizerContainerId);
                return;
            }

            this.analyser.getByteFrequencyData(dataArray);

            const container = document.getElementById(visualizerContainerId);
            if (container) {
                const bars = container.querySelectorAll('.v-bar');
                if (bars.length > 0) {
                    const step = Math.max(1, Math.floor(bufferLength / bars.length));
                    let total = 0;
                    for (let i = 0; i < bars.length; i++) {
                        const start = i * step;
                        const end = Math.min(bufferLength, start + Math.max(1, Math.floor(step * 0.8)));
                        let bandTotal = 0;
                        for (let j = start; j < end; j++) bandTotal += dataArray[j] || 0;
                        const val = bandTotal / Math.max(1, end - start);
                        const level = Math.min(1, (val / 255) * 2.2);
                        total += level;
                        const height = Math.max(8, Math.min(48, Math.round(8 + level * 40)));
                        const opacity = Math.max(0.4, Math.min(1, 0.4 + level * 0.6));
                        bars[i].style.height = `${height}px`;
                        bars[i].style.opacity = opacity.toFixed(2);
                    }
                    this.audioLevel += ((total / bars.length) - this.audioLevel) * 0.24;
                    this.peakAudioLevel = Math.max(this.peakAudioLevel, this.audioLevel);
                    container.style.setProperty('--audio-level', this.audioLevel.toFixed(3));
                }
            }

            this.animationFrameId = requestAnimationFrame(updateBars);
        };

        updateBars();
    },

    stopVoice: async function (visualizerContainerId) {
        this.isListening = false;
        this.audioLevel = 0;

        let finalTranscript = this.speechTranscript || '';

        if (this.animationFrameId) {
            cancelAnimationFrame(this.animationFrameId);
            this.animationFrameId = null;
        }

        if (this.recognition) {
            try {
                this.recognition.stop();
            } catch (e) {}
            this.recognition = null;
        }

        this.closeAssemblyAiSocket();

        // 3. Optional Whisper local fallback refinement
        if (this.whisperWorker && this.pcmBuffers.length > 0) {
            const totalLength = this.pcmBuffers.reduce((acc, buf) => acc + buf.length, 0);
            if (totalLength > 8000) {
                const mergedAudio = new Float32Array(totalLength);
                let offset = 0;
                for (const buf of this.pcmBuffers) {
                    mergedAudio.set(buf, offset);
                    offset += buf.length;
                }
                this.whisperWorker.postMessage({ type: 'transcribe', audio: mergedAudio });
            }
        }
        this.pcmBuffers = [];

        if (this.processorNode) {
            try { this.processorNode.disconnect(); } catch (e) {}
            this.processorNode = null;
        }

        if (this.mediaStream) {
            this.mediaStream.getTracks().forEach(track => track.stop());
            this.mediaStream = null;
        }

        if (this.sourceNode) {
            try { this.sourceNode.disconnect(); } catch (e) {}
            this.sourceNode = null;
        }

        if (this.audioContext) {
            try { this.audioContext.close(); } catch (e) {}
            this.audioContext = null;
        }

        this.resetBars(visualizerContainerId);
        return finalTranscript;
    },

    resetBars: function (visualizerContainerId) {
        const container = document.getElementById(visualizerContainerId);
        if (container) {
            const bars = container.querySelectorAll('.v-bar');
            bars.forEach(bar => {
                bar.style.height = '8px';
                bar.style.opacity = '0.4';
            });
            container.style.setProperty('--audio-level', '0');
        }
    },

    autoResizeTextarea: function (element) {
        if (!element) return;
        element.style.height = 'auto';
        const newHeight = Math.max(22, Math.min(element.scrollHeight, 160));
        element.style.height = `${newHeight}px`;
    },

    resetTextarea: function (element) {
        if (!element) return;
        element.value = '';
        element.style.height = '22px';
    },

    scrollToBottom: function (elementId) {
        const el = document.getElementById(elementId) || document.querySelector(elementId);
        if (el) {
            el.scrollTop = el.scrollHeight;
        }
    },

    attachComposerHandler: function (textareaElement, dotNetHelper) {
        if (!textareaElement) return;
        if (textareaElement._gramAttached) return;
        textareaElement._gramAttached = true;

        textareaElement.addEventListener('input', function () {
            gramAudio.autoResizeTextarea(textareaElement);
        });
    },

    copyCode: function (buttonElement) {
        if (!buttonElement) return;
        const wrapper = buttonElement.closest('.code-block-wrapper');
        if (!wrapper) return;
        const codeEl = wrapper.querySelector('code');
        if (!codeEl) return;
        const text = codeEl.innerText || codeEl.textContent;
        navigator.clipboard.writeText(text).then(() => {
            const span = buttonElement.querySelector('span');
            if (span) {
                const orig = span.innerText;
                span.innerText = 'Copied!';
                buttonElement.classList.add('copied');
                setTimeout(() => {
                    span.innerText = orig;
                    buttonElement.classList.remove('copied');
                }, 2000);
            }
        });
    },

    copyMessageText: function (text) {
        if (!text) return;
        navigator.clipboard.writeText(text);
    },

    initComposer: function (textareaId, dotNetHelper) {
        const el = document.getElementById(textareaId || 'composerTextarea');
        if (!el || el._composerInitialized) return;
        el._composerInitialized = true;

        el.addEventListener('keydown', function (e) {
            const mentionPopup = document.querySelector('.mention-popup');
            if (mentionPopup && ['ArrowDown', 'ArrowUp', 'Enter', 'Tab', 'Escape'].includes(e.key)) {
                e.preventDefault();
                e.stopImmediatePropagation();
                if (dotNetHelper) {
                    dotNetHelper.invokeMethodAsync('HandleMentionKey', e.key);
                }
                return;
            }

            if (e.key === 'Enter' && !e.shiftKey) {
                e.preventDefault();
                e.stopPropagation();
                const val = el.value.trim();
                el.value = '';
                el.style.height = 'auto';
                if (dotNetHelper && val) {
                    dotNetHelper.invokeMethodAsync('SubmitDirectText', val);
                }
            }
        });

        el.addEventListener('input', function () {
            el.style.height = 'auto';
            el.style.height = Math.min(el.scrollHeight, 200) + 'px';
        });
    },

    getMentionState: function (elementIdOrRef) {
        const el = typeof elementIdOrRef === 'string'
            ? document.getElementById(elementIdOrRef)
            : elementIdOrRef;
        if (!el) return { isActive: false, start: -1, end: -1, query: '' };

        const value = el.value || '';
        const caret = typeof el.selectionStart === 'number' ? el.selectionStart : value.length;
        let tokenStart = caret;
        while (tokenStart > 0 && !/\s/.test(value[tokenStart - 1])) tokenStart--;

        if (value[tokenStart] !== '@') {
            return { isActive: false, start: -1, end: -1, query: '' };
        }

        const query = value.slice(tokenStart + 1, caret);
        if (/\s/.test(query)) {
            return { isActive: false, start: -1, end: -1, query: '' };
        }

        return { isActive: true, start: tokenStart, end: caret, query: query };
    },

    replaceMentionAtCaret: function (elementIdOrRef, start, end) {
        const el = typeof elementIdOrRef === 'string'
            ? document.getElementById(elementIdOrRef)
            : elementIdOrRef;
        if (!el) return '';

        const value = el.value || '';
        const safeStart = Math.max(0, Math.min(Number(start), value.length));
        const safeEnd = Math.max(safeStart, Math.min(Number(end), value.length));
        const before = value.slice(0, safeStart);
        const after = value.slice(safeEnd);
        const needsSpace = before.length > 0 && after.length > 0 && !/\s$/.test(before) && !/^\s/.test(after);
        const nextValue = before + (needsSpace ? ' ' : '') + after;
        const nextCaret = before.length + (needsSpace ? 1 : 0);

        el.value = nextValue;
        el.setSelectionRange(nextCaret, nextCaret);
        el.style.height = 'auto';
        el.style.height = Math.min(el.scrollHeight, 200) + 'px';
        return nextValue;
    },

    focusComposer: function (elementIdOrRef) {
        const el = typeof elementIdOrRef === 'string'
            ? document.getElementById(elementIdOrRef)
            : elementIdOrRef;
        if (el) el.focus({ preventScroll: true });
    },

    scrollMentionIntoView: function (index) {
        document.getElementById(`mention-option-${index}`)?.scrollIntoView({ block: 'nearest' });
    },

    resetTextarea: function (elementIdOrRef) {
        let el = null;
        if (typeof elementIdOrRef === 'string') {
            el = document.getElementById(elementIdOrRef);
        } else if (elementIdOrRef && elementIdOrRef.nodeType) {
            el = elementIdOrRef;
        }
        if (!el) el = document.getElementById('composerTextarea');
        if (el) {
            el.value = '';
            el.style.height = 'auto';
        }
    },

    speakServerText: async function (text, dotNetHelper) {
        if (!text || text.trim() === '') return false;

        this.stopSpeech();
        this.speechRequestController = new AbortController();

        const response = await fetch('/api/tts/stream', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({ text: text }),
            signal: this.speechRequestController.signal
        });

        if (!response.ok) {
            const errorText = await response.text();
            throw new Error(errorText || `TTS request failed (${response.status})`);
        }

        const audioBlob = await response.blob();
        this.serverSpeechUrl = URL.createObjectURL(audioBlob);
        const audio = new Audio(this.serverSpeechUrl);
        this.serverSpeechAudio = audio;

        const notifyEnded = () => {
            if (this.serverSpeechAudio === audio) {
                this.serverSpeechAudio = null;
                if (this.serverSpeechUrl) {
                    URL.revokeObjectURL(this.serverSpeechUrl);
                    this.serverSpeechUrl = null;
                }
            }
            if (dotNetHelper) dotNetHelper.invokeMethodAsync('OnSpeechSynthesisEnded');
        };

        audio.onended = notifyEnded;
        audio.onerror = notifyEnded;
        await audio.play();
        this.speechRequestController = null;
        return true;
    },

    speakText: function (text, dotNetHelper, rate, pitch) {
        if (!('speechSynthesis' in window)) {
            console.warn('SpeechSynthesis API is not supported in this browser.');
            return false;
        }

        window.speechSynthesis.cancel();

        if (!text || text.trim() === '') return false;

        // Clean markdown formatting for natural spoken speech
        let cleanText = text
            .replace(/```[\s\S]*?```/g, ' Code snippet omitted. ')
            .replace(/`([^`]+)`/g, '$1')
            .replace(/\[([^\]]+)\]\([^\)]+\)/g, '$1')
            .replace(/[*_~#>-]/g, ' ')
            .replace(/\s+/g, ' ')
            .trim();

        if (!cleanText) return false;

        const utterance = new SpeechSynthesisUtterance(cleanText);
        utterance.rate = rate || 1.0;
        utterance.pitch = pitch || 1.0;
        utterance.lang = navigator.language || 'en-US';

        const voices = window.speechSynthesis.getVoices();
        if (voices.length > 0) {
            // Prefer natural English voices (e.g. Google, Samantha, Daniel, Siri, Natural)
            const preferred = voices.find(v => (v.name.includes('Natural') || v.name.includes('Google') || v.name.includes('Samantha') || v.name.includes('Daniel') || v.name.includes('Karen')) && v.lang.startsWith('en'))
                || voices.find(v => v.lang.startsWith('en'))
                || voices[0];
            if (preferred) utterance.voice = preferred;
        }

        utterance.onend = function () {
            if (dotNetHelper) {
                dotNetHelper.invokeMethodAsync('OnSpeechSynthesisEnded');
            }
        };

        utterance.onerror = function (e) {
            console.warn('Speech synthesis notice:', e);
            if (dotNetHelper) {
                dotNetHelper.invokeMethodAsync('OnSpeechSynthesisEnded');
            }
        };

        window.speechSynthesis.speak(utterance);
        return true;
    },

    stopSpeech: function () {
        if (this.speechRequestController) {
            try { this.speechRequestController.abort(); } catch (e) {}
            this.speechRequestController = null;
        }
        if (this.serverSpeechAudio) {
            try {
                this.serverSpeechAudio.pause();
                this.serverSpeechAudio.removeAttribute('src');
                this.serverSpeechAudio.load();
            } catch (e) {}
            this.serverSpeechAudio = null;
        }
        if (this.serverSpeechUrl) {
            URL.revokeObjectURL(this.serverSpeechUrl);
            this.serverSpeechUrl = null;
        }
        if ('speechSynthesis' in window) {
            window.speechSynthesis.cancel();
        }
    },

    isSpeaking: function () {
        return (this.serverSpeechAudio && !this.serverSpeechAudio.paused && !this.serverSpeechAudio.ended)
            || ('speechSynthesis' in window && window.speechSynthesis.speaking);
    },

    renderMath: function (element) {
        if (!element) return;
        
        // 1. KaTeX Math rendering
        const renderKaTeX = () => {
            if (window.renderMathInElement) {
                try {
                    window.renderMathInElement(element, {
                        delimiters: [
                            { left: '$$', right: '$$', display: true },
                            { left: '$', right: '$', display: false },
                            { left: '\\(', right: '\\)', display: false },
                            { left: '\\[', right: '\\]', display: true }
                        ],
                        throwOnError: false
                    });
                } catch (e) {
                    console.warn('[KaTeX] Error rendering math:', e);
                }
            }
        };

        if (window.renderMathInElement) {
            renderKaTeX();
        } else {
            setTimeout(renderKaTeX, 150);
        }

        // 2. Mermaid Diagrams rendering
        const renderMermaid = () => {
            if (window.mermaid) {
                try {
                    window.mermaid.initialize({
                        startOnLoad: false,
                        theme: 'dark',
                        securityLevel: 'loose',
                        fontFamily: 'inherit'
                    });
                    const mermaidElements = element.querySelectorAll('.mermaid');
                    if (mermaidElements.length > 0) {
                        window.mermaid.run({
                            nodes: mermaidElements
                        }).then(() => {
                            // Attach pinch-to-zoom and pan listeners to viewports
                            element.querySelectorAll('.mermaid-viewport').forEach(vp => {
                                gramAudio.initDiagramViewport(vp);
                            });
                        });
                    }
                } catch (e) {
                    console.warn('[Mermaid] Error rendering diagram:', e);
                }
            }
        };

        if (window.mermaid) {
            renderMermaid();
        } else {
            setTimeout(renderMermaid, 200);
        }
    },

    zoomDiagram: function (btn, delta) {
        const wrapper = btn.closest('.mermaid-wrapper');
        if (!wrapper) return;
        const vp = wrapper.querySelector('.mermaid-viewport');
        if (!vp) return;

        let scale = parseFloat(vp.dataset.scale || '1');
        let x = parseFloat(vp.dataset.x || '0');
        let y = parseFloat(vp.dataset.y || '0');

        scale = Math.min(4.0, Math.max(0.3, scale + delta));
        vp.dataset.scale = scale.toFixed(2);

        this.applyDiagramTransform(vp, scale, x, y);
    },

    resetDiagramZoom: function (btn) {
        const wrapper = btn.closest('.mermaid-wrapper');
        if (!wrapper) return;
        const vp = wrapper.querySelector('.mermaid-viewport');
        if (!vp) return;

        vp.dataset.scale = '1';
        vp.dataset.x = '0';
        vp.dataset.y = '0';

        this.applyDiagramTransform(vp, 1, 0, 0);
    },

    applyDiagramTransform: function (vp, scale, x, y) {
        const mermaid = vp.querySelector('.mermaid');
        if (mermaid) {
            mermaid.style.transform = `translate(${x}px, ${y}px) scale(${scale})`;
        }
        const wrapper = vp.closest('.mermaid-wrapper');
        if (wrapper) {
            const label = wrapper.querySelector('.diagram-zoom-level');
            if (label) {
                label.textContent = `${Math.round(scale * 100)}%`;
            }
        }
    },

    initDiagramViewport: function (vp) {
        if (!vp || vp._hasZoomInit) return;
        vp._hasZoomInit = true;

        let isDragging = false;
        let startX = 0, startY = 0;
        let initialDistance = 0;
        let initialScale = 1;

        // Mouse drag pan
        vp.addEventListener('mousedown', (e) => {
            if (e.button !== 0) return;
            isDragging = true;
            startX = e.clientX - parseFloat(vp.dataset.x || '0');
            startY = e.clientY - parseFloat(vp.dataset.y || '0');
            vp.classList.add('panning');
            e.preventDefault();
        });

        window.addEventListener('mousemove', (e) => {
            if (!isDragging) return;
            const x = e.clientX - startX;
            const y = e.clientY - startY;
            vp.dataset.x = x.toFixed(1);
            vp.dataset.y = y.toFixed(1);
            const scale = parseFloat(vp.dataset.scale || '1');
            gramAudio.applyDiagramTransform(vp, scale, x, y);
        });

        window.addEventListener('mouseup', () => {
            if (isDragging) {
                isDragging = false;
                vp.classList.remove('panning');
            }
        });

        // Touch pinch-to-zoom and pan
        vp.addEventListener('touchstart', (e) => {
            if (e.touches.length === 2) {
                initialDistance = Math.hypot(
                    e.touches[0].clientX - e.touches[1].clientX,
                    e.touches[0].clientY - e.touches[1].clientY
                );
                initialScale = parseFloat(vp.dataset.scale || '1');
            } else if (e.touches.length === 1) {
                isDragging = true;
                startX = e.touches[0].clientX - parseFloat(vp.dataset.x || '0');
                startY = e.touches[0].clientY - parseFloat(vp.dataset.y || '0');
            }
        }, { passive: true });

        vp.addEventListener('touchmove', (e) => {
            if (e.touches.length === 2 && initialDistance > 0) {
                const currentDist = Math.hypot(
                    e.touches[0].clientX - e.touches[1].clientX,
                    e.touches[0].clientY - e.touches[1].clientY
                );
                const factor = currentDist / initialDistance;
                const newScale = Math.min(4.0, Math.max(0.3, initialScale * factor));
                vp.dataset.scale = newScale.toFixed(2);
                const x = parseFloat(vp.dataset.x || '0');
                const y = parseFloat(vp.dataset.y || '0');
                gramAudio.applyDiagramTransform(vp, newScale, x, y);
                e.preventDefault();
            } else if (e.touches.length === 1 && isDragging) {
                const x = e.touches[0].clientX - startX;
                const y = e.touches[0].clientY - startY;
                vp.dataset.x = x.toFixed(1);
                vp.dataset.y = y.toFixed(1);
                const scale = parseFloat(vp.dataset.scale || '1');
                gramAudio.applyDiagramTransform(vp, scale, x, y);
            }
        }, { passive: false });

        vp.addEventListener('touchend', () => {
            isDragging = false;
            initialDistance = 0;
        });

        // Mouse wheel zoom with Ctrl or Cmd
        vp.addEventListener('wheel', (e) => {
            if (e.ctrlKey || e.metaKey) {
                e.preventDefault();
                const delta = e.deltaY < 0 ? 0.15 : -0.15;
                let scale = parseFloat(vp.dataset.scale || '1');
                scale = Math.min(4.0, Math.max(0.3, scale + delta));
                vp.dataset.scale = scale.toFixed(2);
                const x = parseFloat(vp.dataset.x || '0');
                const y = parseFloat(vp.dataset.y || '0');
                gramAudio.applyDiagramTransform(vp, scale, x, y);
            }
        }, { passive: false });
    },

    // Audio Devices Enumeration & Output Selection
    getAudioDevices: async function () {
        try {
            if (!navigator.mediaDevices || !navigator.mediaDevices.enumerateDevices) {
                return [];
            }
            let devices = await navigator.mediaDevices.enumerateDevices();
            const hasLabels = devices.some(d => (d.kind === 'audioinput' || d.kind === 'audiooutput') && d.label);
            if (!hasLabels && navigator.mediaDevices.getUserMedia) {
                try {
                    const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
                    stream.getTracks().forEach(t => t.stop());
                    devices = await navigator.mediaDevices.enumerateDevices();
                } catch (e) {}
            }
            return devices
                .filter(d => d.kind === 'audioinput' || d.kind === 'audiooutput')
                .map(d => ({
                    kind: d.kind,
                    deviceId: d.deviceId,
                    label: d.label || (d.kind === 'audioinput' ? 'Default Microphone' : 'Default Speaker')
                }));
        } catch (e) {
            console.warn('[gramAudio] getAudioDevices error:', e);
            return [];
        }
    },

    setOutputDevice: function (deviceId) {
        this.outputDeviceId = deviceId || '';
    },

    // Discord-like Realtime Microphone Test with Visual Meter & Optional Loopback
    micTestContext: null,
    micTestStream: null,
    micTestSource: null,
    micTestAnalyser: null,
    micTestGain: null,
    micTestAnimId: null,
    isTestingMic: false,
    micTestLoopback: false,

    startMicTest: async function (meterElementId, inputDeviceId, enableLoopback, sensitivityThreshold) {
        this.stopMicTest(meterElementId);
        this.isTestingMic = true;
        this.micTestLoopback = !!enableLoopback;

        try {
            const AudioCtx = window.AudioContext || window.webkitAudioContext;
            this.micTestContext = new AudioCtx();
            if (this.micTestContext.state === 'suspended') {
                await this.micTestContext.resume();
            }

            const constraints = {
                audio: inputDeviceId ? { deviceId: { exact: inputDeviceId } } : true,
                video: false
            };

            this.micTestStream = await navigator.mediaDevices.getUserMedia(constraints);
            this.micTestSource = this.micTestContext.createMediaStreamSource(this.micTestStream);
            this.micTestAnalyser = this.micTestContext.createAnalyser();
            this.micTestAnalyser.fftSize = 256;
            this.micTestAnalyser.smoothingTimeConstant = 0.5;
            this.micTestSource.connect(this.micTestAnalyser);

            if (enableLoopback) {
                this.micTestGain = this.micTestContext.createGain();
                this.micTestGain.gain.value = 0.85;
                this.micTestSource.connect(this.micTestGain);
                this.micTestGain.connect(this.micTestContext.destination);
            }

            const bufferLength = this.micTestAnalyser.frequencyBinCount;
            const dataArray = new Uint8Array(bufferLength);
            const meterEl = document.getElementById(meterElementId);

            const updateMeter = () => {
                if (!this.isTestingMic || !this.micTestAnalyser) return;

                this.micTestAnalyser.getByteFrequencyData(dataArray);

                let sum = 0;
                for (let i = 0; i < bufferLength; i++) {
                    sum += dataArray[i];
                }
                const avg = sum / bufferLength;
                const levelPct = Math.min(100, Math.round(Math.pow(avg / 120, 1.15) * 100));

                if (meterEl) {
                    const fillBar = meterEl.querySelector('.mic-meter-fill');
                    const valueLabel = meterEl.querySelector('.mic-meter-value');
                    if (fillBar) {
                        fillBar.style.width = `${levelPct}%`;
                        const thresh = sensitivityThreshold || 15;
                        if (levelPct >= thresh) {
                            fillBar.classList.add('active-speaking');
                        } else {
                            fillBar.classList.remove('active-speaking');
                        }
                    }
                    if (valueLabel) {
                        valueLabel.textContent = `${levelPct}%`;
                    }
                }

                this.micTestAnimId = requestAnimationFrame(updateMeter);
            };

            updateMeter();
            return true;
        } catch (err) {
            console.error('[gramAudio] startMicTest error:', err);
            this.stopMicTest(meterElementId);
            return false;
        }
    },

    setMicTestLoopback: function (enable) {
        this.micTestLoopback = !!enable;
        if (!this.micTestContext || !this.micTestSource) return;

        if (this.micTestLoopback) {
            if (!this.micTestGain) {
                this.micTestGain = this.micTestContext.createGain();
                this.micTestGain.gain.value = 0.85;
                this.micTestSource.connect(this.micTestGain);
                this.micTestGain.connect(this.micTestContext.destination);
            }
        } else {
            if (this.micTestGain) {
                try {
                    this.micTestGain.disconnect();
                } catch (e) {}
                this.micTestGain = null;
            }
        }
    },

    stopMicTest: function (meterElementId) {
        this.isTestingMic = false;
        if (this.micTestAnimId) {
            cancelAnimationFrame(this.micTestAnimId);
            this.micTestAnimId = null;
        }
        if (this.micTestGain) {
            try { this.micTestGain.disconnect(); } catch (e) {}
            this.micTestGain = null;
        }
        if (this.micTestSource) {
            try { this.micTestSource.disconnect(); } catch (e) {}
            this.micTestSource = null;
        }
        if (this.micTestStream) {
            this.micTestStream.getTracks().forEach(t => t.stop());
            this.micTestStream = null;
        }
        if (this.micTestContext) {
            try { this.micTestContext.close(); } catch (e) {}
            this.micTestContext = null;
        }

        const meterId = meterElementId || 'discordMicMeter';
        const meterEl = document.getElementById(meterId);
        if (meterEl) {
            const fillBar = meterEl.querySelector('.mic-meter-fill');
            const valueLabel = meterEl.querySelector('.mic-meter-value');
            if (fillBar) {
                fillBar.style.width = '0%';
                fillBar.classList.remove('active-speaking');
            }
            if (valueLabel) {
                valueLabel.textContent = '0%';
            }
        }
    },

    // Discord-like Harmonious Speaker Test Chime
    speakerTestContext: null,
    isTestingSpeaker: false,

    playSpeakerTest: async function (outputDeviceId, volume) {
        this.stopSpeakerTest();
        this.isTestingSpeaker = true;

        try {
            const AudioCtx = window.AudioContext || window.webkitAudioContext;
            this.speakerTestContext = new AudioCtx();
            if (this.speakerTestContext.state === 'suspended') {
                await this.speakerTestContext.resume();
            }

            if (outputDeviceId && typeof this.speakerTestContext.setSinkId === 'function') {
                try {
                    await this.speakerTestContext.setSinkId(outputDeviceId);
                } catch (e) {
                    console.warn('[gramAudio] setSinkId notice:', e);
                }
            }

            const ctx = this.speakerTestContext;
            const now = ctx.currentTime;
            const vol = Math.max(0.05, Math.min(1.0, (volume || 100) / 100));

            const masterGain = ctx.createGain();
            masterGain.gain.setValueAtTime(vol * 0.4, now);
            masterGain.connect(ctx.destination);

            // Play Discord-like bright multi-note chime: D5, F#5, A5, D6
            const notes = [
                { freq: 587.33, start: 0.00, dur: 0.20, type: 'sine' },
                { freq: 739.99, start: 0.11, dur: 0.22, type: 'sine' },
                { freq: 880.00, start: 0.22, dur: 0.26, type: 'sine' },
                { freq: 1174.66, start: 0.35, dur: 0.55, type: 'triangle' }
            ];

            notes.forEach(note => {
                const osc = ctx.createOscillator();
                const noteGain = ctx.createGain();

                osc.type = note.type;
                osc.frequency.setValueAtTime(note.freq, now + note.start);

                noteGain.gain.setValueAtTime(0, now + note.start);
                noteGain.gain.linearRampToValueAtTime(0.65, now + note.start + 0.02);
                noteGain.gain.exponentialRampToValueAtTime(0.001, now + note.start + note.dur);

                osc.connect(noteGain);
                noteGain.connect(masterGain);

                osc.start(now + note.start);
                osc.stop(now + note.start + note.dur + 0.04);
            });

            setTimeout(() => {
                this.isTestingSpeaker = false;
            }, 1000);

            return true;
        } catch (e) {
            console.error('[gramAudio] playSpeakerTest error:', e);
            this.isTestingSpeaker = false;
            return false;
        }
    },

    stopSpeakerTest: function () {
        this.isTestingSpeaker = false;
        if (this.speakerTestContext) {
            try { this.speakerTestContext.close(); } catch (e) {}
            this.speakerTestContext = null;
        }
    }
};
