// WebAuthn / FIDO2 Passkey Integration for Gram
window.gramPasskey = {
    // Check if WebAuthn & platform biometric authenticators are available
    isAvailable: async function () {
        if (!window.PublicKeyCredential) return false;
        try {
            if (typeof PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable === 'function') {
                const available = await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
                return available;
            }
            return true;
        } catch (e) {
            console.warn('[gramPasskey] isAvailable check:', e);
            return true;
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
            console.log('[gramPasskey] Starting registration for user:', { userId, userName, userDisplayName });
            if (!navigator.credentials || !window.PublicKeyCredential) {
                return { success: false, error: 'WebAuthn passkeys are not supported on this browser or context (requires HTTPS or localhost).' };
            }

            // Generate secure random 32-byte challenge
            const challenge = new Uint8Array(32);
            window.crypto.getRandomValues(challenge);

            // User ID buffer (max 64 bytes)
            const enc = new TextEncoder();
            const safeUserId = (userId && typeof userId === 'string' && userId.trim()) ? userId.trim() : ('user_' + Date.now());
            const userIdBuffer = enc.encode(safeUserId.substring(0, 64));

            const host = window.location.hostname;
            const rp = { name: 'Gram' };
            // Only set rp.id if valid domain name (WebAuthn forbids IP addresses)
            if (host && !/^[0-9.]+$/.test(host) && host !== 'localhost') {
                rp.id = host;
            }

            const publicKeyCredentialCreationOptions = {
                challenge: challenge,
                rp: rp,
                user: {
                    id: userIdBuffer,
                    name: userName || 'Gram User',
                    displayName: userDisplayName || userName || 'Gram User'
                },
                pubKeyCredParams: [
                    { alg: -7, type: 'public-key' },   // ES256 (NIST P-256)
                    { alg: -257, type: 'public-key' }, // RS256 (RSA)
                    { alg: -8, type: 'public-key' },   // Ed25519
                    { alg: -37, type: 'public-key' }   // PS256
                ],
                authenticatorSelection: {
                    userVerification: 'preferred',
                    residentKey: 'preferred'
                },
                timeout: 60000,
                attestation: 'none'
            };

            console.log('[gramPasskey] Prompting OS native passkey creation dialog...');
            const credential = await navigator.credentials.create({
                publicKey: publicKeyCredentialCreationOptions
            });

            if (!credential) {
                return { success: false, error: 'Passkey creation was cancelled.' };
            }

            console.log('[gramPasskey] Passkey credential created successfully:', credential);
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
            console.error('[gramPasskey] Registration error:', err);
            return {
                success: false,
                error: err.name === 'NotAllowedError'
                    ? 'Passkey registration was cancelled or timed out.'
                    : (err.message || 'Passkey setup failed.')
            };
        }
    },

    // Authenticate using a Passkey (navigator.credentials.get)
    authenticate: async function (allowedCredentialIds) {
        try {
            console.log('[gramPasskey] Starting passkey authentication...');
            if (!navigator.credentials || !window.PublicKeyCredential) {
                return { success: false, error: 'WebAuthn is not supported on this browser or context.' };
            }

            const challenge = new Uint8Array(32);
            window.crypto.getRandomValues(challenge);

            const host = window.location.hostname;
            let rpId = undefined;
            if (host && !/^[0-9.]+$/.test(host) && host !== 'localhost') {
                rpId = host;
            }

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
                rpId: rpId,
                allowCredentials: allowCredentials,
                userVerification: 'preferred',
                timeout: 60000
            };

            console.log('[gramPasskey] Prompting OS native passkey authentication dialog...');
            const assertion = await navigator.credentials.get({
                publicKey: publicKeyCredentialRequestOptions
            });

            if (!assertion) {
                return { success: false, error: 'Passkey sign-in was cancelled.' };
            }

            console.log('[gramPasskey] Passkey assertion received:', assertion);
            const credentialId = this.bufferToBase64Url(assertion.rawId);

            return {
                success: true,
                credentialId: credentialId
            };
        } catch (err) {
            console.error('[gramPasskey] Authentication error:', err);
            return {
                success: false,
                error: err.name === 'NotAllowedError'
                    ? 'Passkey prompt was cancelled or timed out.'
                    : (err.message || 'Passkey authentication failed.')
            };
        }
    }
};
