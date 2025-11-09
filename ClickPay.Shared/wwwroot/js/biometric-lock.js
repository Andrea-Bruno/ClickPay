const encoder = new TextEncoder();

function toBase64Url(buffer) {
    const bytes = new Uint8Array(buffer);
    let binary = "";
    for (let i = 0; i < bytes.length; i++) {
        binary += String.fromCharCode(bytes[i]);
    }
    return btoa(binary).replace(/\+/g, "-").replace(/\//g, "_").replace(/=+$/g, "");
}

function fromBase64Url(value) {
    const padding = value.length % 4 === 0 ? "" : "=".repeat(4 - (value.length % 4));
    const base64 = value.replace(/-/g, "+").replace(/_/g, "/") + padding;
    const binary = atob(base64);
    const buffer = new ArrayBuffer(binary.length);
    const view = new Uint8Array(buffer);
    for (let i = 0; i < binary.length; i++) {
        view[i] = binary.charCodeAt(i);
    }
    return buffer;
}

export async function isBiometricAvailable() {
    if (typeof window === "undefined" || !window.PublicKeyCredential) {
        return false;
    }

    try {
        return await PublicKeyCredential.isUserVerifyingPlatformAuthenticatorAvailable();
    } catch (err) {
        console.warn("ClickPay biometrics availability check failed", err);
        return false;
    }
}

export async function registerBiometricCredential() {
    if (typeof window === "undefined" || !navigator.credentials || !window.PublicKeyCredential) {
        return { success: false, message: "Dispositivo non supportato." };
    }

    try {
        const challenge = crypto.getRandomValues(new Uint8Array(32));
        const userId = crypto.getRandomValues(new Uint8Array(32));
        const publicKey = {
            challenge,
            rp: {
                name: "ClickPay",
                id: window.location.hostname
            },
            user: {
                id: userId,
                name: `clickpay-${window.location.hostname}`,
                displayName: "ClickPay"
            },
            pubKeyCredParams: [
                { type: "public-key", alg: -7 }
            ],
            authenticatorSelection: {
                authenticatorAttachment: "platform",
                requireResidentKey: false,
                userVerification: "required"
            },
            timeout: 60000
        };

        const credential = await navigator.credentials.create({ publicKey });
        if (!credential) {
            return { success: false, message: "Registrazione biometrica non riuscita." };
        }

        return {
            success: true,
            credentialId: toBase64Url(credential.rawId)
        };
    } catch (err) {
        if (err instanceof DOMException && err.name === "NotAllowedError") {
            return { success: false, canceled: true };
        }

        console.warn("ClickPay biometrics registration failed", err);
        return { success: false, message: err?.message ?? "Registrazione biometrica non riuscita." };
    }
}

export async function authenticateBiometricCredential(credentialId) {
    if (typeof window === "undefined" || !navigator.credentials || !window.PublicKeyCredential) {
        return { success: false, message: "Dispositivo non supportato." };
    }

    if (!credentialId) {
        return { success: false, message: "Credenziale biometrica non trovata." };
    }

    try {
        const challenge = crypto.getRandomValues(new Uint8Array(32));
        const request = {
            challenge,
            allowCredentials: [
                {
                    id: new Uint8Array(fromBase64Url(credentialId)),
                    type: "public-key"
                }
            ],
            timeout: 60000,
            userVerification: "required"
        };

        const assertion = await navigator.credentials.get({ publicKey: request });
        if (!assertion) {
            return { success: false, message: "Autenticazione biometrica non riuscita." };
        }

        return { success: true };
    } catch (err) {
        if (err instanceof DOMException && err.name === "NotAllowedError") {
            return { success: false, canceled: true };
        }

        console.warn("ClickPay biometrics authentication failed", err);
        return { success: false, message: err?.message ?? "Autenticazione biometrica non riuscita." };
    }
}
