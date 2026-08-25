// Audio visualizer and Speech-to-Text integration for Gram

window.gramAudio = {
    audioContext: null,
    analyser: null,
    mediaStream: null,
    sourceNode: null,
    animationFrameId: null,
    recognition: null,
    dotNetRef: null,
    isListening: false,
    audioLevel: 0,
    outputDeviceId: '',

    startVoice: async function (dotNetHelper, visualizerContainerId, inputDeviceId) {
        this.dotNetRef = dotNetHelper;
        try {
            // 1. Initialize Microphone Audio Stream & Analyser
            const AudioCtx = window.AudioContext || window.webkitAudioContext;
            this.audioContext = new AudioCtx();
            
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

            this.isListening = true;
            this.startVisualizerLoop(visualizerContainerId);

            // 2. Initialize Speech Recognition (Speech to Text)
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
                    if (this.dotNetRef) {
                        this.dotNetRef.invokeMethodAsync('OnTranscriptReceived', transcript);
                    }
                };

                this.recognition.onerror = (event) => {
                    console.warn('Speech recognition notice:', event.error);
                };

                this.recognition.onend = () => {
                    if (this.isListening) {
                        try {
                            this.recognition.start();
                        } catch (e) {
                            // Ignored if already started
                        }
                    }
                };

                this.recognition.start();
            } else {
                console.warn('SpeechRecognition API is not supported in this browser.');
            }

            return true;
        } catch (err) {
            console.error('Error starting microphone or recognition:', err);
            this.stopVoice(visualizerContainerId);
            return false;
        }
    },

    restartVoiceCapture: async function (dotNetHelper, visualizerContainerId, inputDeviceId) {
        this.stopVoice(visualizerContainerId);
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
        // Output routing is applied by the future speech playback element.
        // Keep the selected sink here so the voice UI and playback share one source of truth.
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
                        const level = Math.min(1, (val / 255) * 2.1);
                        total += level;
                        // Keep the resting state calm while making speech feel immediate.
                        const height = Math.max(4, Math.min(15, Math.round(4 + level * 13)));
                        const opacity = Math.max(0.35, Math.min(1, 0.35 + level * 0.75));
                        bars[i].style.height = `${height}px`;
                        bars[i].style.opacity = opacity.toFixed(2);
                    }
                    this.audioLevel += ((total / bars.length) - this.audioLevel) * 0.24;
                    container.style.setProperty('--audio-level', this.audioLevel.toFixed(3));
                }
            }

            this.animationFrameId = requestAnimationFrame(updateBars);
        };

        updateBars();
    },

    stopVoice: function (visualizerContainerId) {
        this.isListening = false;
        this.audioLevel = 0;

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
    },

    resetBars: function (visualizerContainerId) {
        const container = document.getElementById(visualizerContainerId);
        if (container) {
            const bars = container.querySelectorAll('.v-bar');
            bars.forEach(bar => {
                bar.style.height = '4px';
                bar.style.opacity = '0.35';
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
    }
};
