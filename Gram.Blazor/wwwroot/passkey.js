// WebAuthn / FIDO2 Passkey Integration for Gram
window.gramPasskey = {
    // Check if WebAuthn & platform biometric authenticators are available
    isAvailable: async function () {
        if (!window.PublicKeyCredential) return false;
        try {
            if (typeof PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable === 'function') {
                return await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
            }
            return true;
        } catch (e) {
            return false;
        }
    },

    // Convert Base64URL string to ArrayBuffer
    base64UrlToBuffer: function (base64Url) {
        if (!base64Url) return new ArrayBuffer(0);
        let base64 = base64Url.replace(/-/g, '+').replace(/_/g, '/');
        while (base64.length % 4) base64 += '=';
        const binary = atob(base64);
        const bytes = new Uint8Array(binary.length);
        for (let i = 0; i < binary.length; i++) {
            bytes[i] = binary.charCodeAt(i);
        }
        return bytes.buffer;
    },

    // Convert ArrayBuffer to Base64URL string
    bufferToBase64Url: function (buffer) {
        if (!buffer) return '';
        const bytes = new Uint8Array(buffer);
        let binary = '';
        for (let i = 0; i < bytes.byteLength; i++) {
            binary += String.fromCharCode(bytes[i]);
        }
        return btoa(binary)
            .replace(/\+/g, '-')
            .replace(/\//g, '_')
            .replace(/=+$/, '');
    },

    // Register a new Passkey (navigator.credentials.create)
    register: async function (userId, userName, userDisplayName) {
        try {
            if (!window.PublicKeyCredential) {
                return { success: false, error: 'WebAuthn is not supported on this device/browser.' };
            }

            // Generate secure random 32-byte challenge
            const challenge = new Uint8Array(32);
            window.crypto.getRandomValues(challenge);

            // User ID buffer
            const enc = new TextEncoder();
            const userIdBuffer = enc.encode(userId || ('user_' + Date.now()));

            const domain = window.location.hostname || 'localhost';

            const publicKeyCredentialCreationOptions = {
                challenge: challenge,
                rp: {
                    name: 'Gram',
                    id: domain
                },
                user: {
                    id: userIdBuffer,
                    name: userName || 'user@gram.local',
                    displayName: userDisplayName || userName || 'Gram User'
                },
                pubKeyCredParams: [
                    { alg: -7, type: 'public-key' },   // ES256
                    { alg: -257, type: 'public-key' }  // RS256
                ],
                authenticatorSelection: {
                    authenticatorAttachment: 'platform',
                    userVerification: 'preferred',
                    residentKey: 'preferred'
                },
                timeout: 60000,
                attestation: 'none'
            };

            const credential = await navigator.credentials.create({
                publicKey: publicKeyCredentialCreationOptions
            });

            if (!credential) {
                return { success: false, error: 'Passkey creation was cancelled.' };
            }

            const credentialId = this.bufferToBase64Url(credential.rawId);
            let publicKeyStr = '';
            if (credential.response && typeof credential.response.getPublicKey === 'function') {
                const pkBuffer = credential.response.getPublicKey();
                if (pkBuffer) {
                    publicKeyStr = this.bufferToBase64Url(pkBuffer);
                }
            }
            if (!publicKeyStr && credential.response && credential.response.attestationObject) {
                publicKeyStr = this.bufferToBase64Url(credential.response.attestationObject);
            }

            // Detect authenticator label
            let deviceName = 'Passkey';
            const ua = navigator.userAgent;
            if (/Macintosh|Mac OS X/.test(ua)) {
                deviceName = /Safari/.test(ua) && !/Chrome/.test(ua) ? 'Touch ID / Apple Keychain' : 'Touch ID / Passkey (Mac)';
            } else if (/iPhone|iPad|iPod/.test(ua)) {
                deviceName = 'Face ID / Touch ID (iOS)';
            } else if (/Windows/.test(ua)) {
                deviceName = 'Windows Hello / Security Key';
            } else if (/Android/.test(ua)) {
                deviceName = 'Fingerprint / Screen Lock (Android)';
            } else if (/Linux/.test(ua)) {
                deviceName = 'Security Key (Linux)';
            }

            return {
                success: true,
                credentialId: credentialId,
                publicKey: publicKeyStr || credentialId,
                name: deviceName
            };
        } catch (err) {
            console.error('[gramPasskey] register error:', err);
            return {
                success: false,
                error: err.name === 'NotAllowedError'
                    ? 'Passkey registration cancelled or timed out.'
                    : (err.message || 'Passkey setup failed.')
            };
        }
    },

    // Authenticate using a Passkey (navigator.credentials.get)
    authenticate: async function (allowedCredentialIds) {
        try {
            if (!window.PublicKeyCredential) {
                return { success: false, error: 'WebAuthn is not supported on this device/browser.' };
            }

            const challenge = new Uint8Array(32);
            window.crypto.getRandomValues(challenge);

            const domain = window.location.hostname || 'localhost';

            let allowCredentials = undefined;
            if (allowedCredentialIds && allowedCredentialIds.length > 0) {
                allowCredentials = allowedCredentialIds.map(id => ({
                    id: gramPasskey.base64UrlToBuffer(id),
                    type: 'public-key',
                    transports: ['internal', 'hybrid', 'usb', 'nfc', 'ble']
                }));
            }

            const publicKeyCredentialRequestOptions = {
                challenge: challenge,
                rpId: domain,
                allowCredentials: allowCredentials,
                userVerification: 'preferred',
                timeout: 60000
            };

            const assertion = await navigator.credentials.get({
                publicKey: publicKeyCredentialRequestOptions
            });

            if (!assertion) {
                return { success: false, error: 'Passkey sign-in cancelled.' };
            }

            const credentialId = this.bufferToBase64Url(assertion.rawId);

            return {
                success: true,
                credentialId: credentialId
            };
        } catch (err) {
            console.error('[gramPasskey] authenticate error:', err);
            return {
                success: false,
                error: err.name === 'NotAllowedError'
                    ? 'Passkey sign-in cancelled or timed out.'
                    : (err.message || 'Passkey authentication failed.')
            };
        }
    }
};
