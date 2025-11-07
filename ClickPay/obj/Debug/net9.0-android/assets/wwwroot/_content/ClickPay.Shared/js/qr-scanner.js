const overlayClass = "clickpay-qr-overlay";
const defaultJsQrSource = "https://cdn.jsdelivr.net/npm/jsqr@1.4.0/dist/jsQR.min.js";

let jsQrLoaderPromise = null;

const ensureJsQr = async (options) => {
    if (typeof window === "undefined") {
        return null;
    }

    if (typeof window.jsQR === "function") {
        return window.jsQR;
    }

    if (!jsQrLoaderPromise) {
        const scriptUrl = options?.fallbackScriptUrl || defaultJsQrSource;

        jsQrLoaderPromise = new Promise((resolve, reject) => {
            let resolved = false;
            const cleanup = () => {
                resolved = true;
            };

            if (document.querySelector(`script[data-clickpay-jsqr][data-src="${scriptUrl}"]`)) {
                if (typeof window.jsQR === "function") {
                    cleanup();
                    resolve(window.jsQR);
                } else {
                    jsQrLoaderPromise = null;
                    reject(new Error("jsQR script already present but global was not initialised"));
                }
                return;
            }

            const script = document.createElement("script");
            script.src = scriptUrl;
            script.async = true;
            script.crossOrigin = "anonymous";
            script.dataset.clickpayJsqr = "";
            script.dataset.src = scriptUrl;

            script.onload = () => {
                if (typeof window.jsQR === "function") {
                    cleanup();
                    resolve(window.jsQR);
                } else {
                    jsQrLoaderPromise = null;
                    reject(new Error("jsQR script loaded but global was not found"));
                }
            };

            script.onerror = (evt) => {
                if (!resolved) {
                    jsQrLoaderPromise = null;
                    reject(new Error(`Unable to load jsQR script: ${evt?.type ?? "unknown error"}`));
                }
            };

            document.head.appendChild(script);
        });
    }

    return jsQrLoaderPromise;
};

const setupNativeDetector = async () => {
    if (typeof window === "undefined" || typeof window.BarcodeDetector === "undefined") {
        return null;
    }

    try {
        if (typeof window.BarcodeDetector.getSupportedFormats === "function") {
            const formats = await window.BarcodeDetector.getSupportedFormats();
            if (!Array.isArray(formats) || !formats.includes("qr_code")) {
                return null;
            }
        }

        const detector = new window.BarcodeDetector({ formats: ["qr_code"] });

        return {
            detect: async (video) => {
                const codes = await detector.detect(video);
                if (!codes || codes.length === 0) {
                    return null;
                }

                const payload = codes[0]?.rawValue ?? null;
                return typeof payload === "string" && payload.length > 0 ? payload : null;
            },
            dispose: () => { /* native detector does not need disposal */ },
            scanInterval: 0
        };
    }
    catch (err) {
        console.warn("BarcodeDetector initialisation failed", err);
        return null;
    }
};

const setupJsQrFallback = async (options) => {
    try {
        const jsQR = await ensureJsQr(options);
        if (typeof jsQR !== "function") {
            return null;
        }

        const canvas = document.createElement("canvas");
        const context = canvas.getContext("2d", { willReadFrequently: true });

        if (!context) {
            return null;
        }

        return {
            detect: (video) => {
                if (video.readyState < HTMLMediaElement.HAVE_ENOUGH_DATA) {
                    return null;
                }

                if (canvas.width !== video.videoWidth || canvas.height !== video.videoHeight) {
                    canvas.width = video.videoWidth;
                    canvas.height = video.videoHeight;
                }

                context.drawImage(video, 0, 0, canvas.width, canvas.height);
                const imageData = context.getImageData(0, 0, canvas.width, canvas.height);
                const result = jsQR(imageData.data, imageData.width, imageData.height, { inversionAttempts: "dontInvert" });
                return result?.data ?? null;
            },
            dispose: () => {
                canvas.width = 0;
                canvas.height = 0;
            },
            scanInterval: 200
        };
    }
    catch (err) {
        console.warn("jsQR fallback initialisation failed", err);
        return null;
    }
};

const ensureStyles = () => {
    if (document.querySelector(`style[data-clickpay-qr]`)) {
        return;
    }

    const style = document.createElement("style");
    style.dataset.clickpayQr = "";
    style.textContent = `
.${overlayClass} {
    position: fixed;
    inset: 0;
    z-index: 2147483647;
    background: rgba(0, 0, 0, 0.85);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    gap: 1rem;
    padding: 1.5rem;
    color: #fff;
    text-align: center;
}
.${overlayClass} video {
    width: min(480px, 90vw);
    border-radius: 1rem;
    box-shadow: 0 1rem 3rem rgba(0,0,0,0.6);
}
.${overlayClass} button {
    background: #ff6b6b;
    border: none;
    color: #fff;
    padding: 0.75rem 1.5rem;
    border-radius: 999px;
    font-size: 1rem;
    font-weight: 600;
    cursor: pointer;
}
.${overlayClass} button:hover {
    background: #ff4c4c;
}
`; 
    document.head.appendChild(style);
};

export async function scanQr(options) {
    if (typeof window === "undefined") {
        return { success: false, errorCode: "QrScan_Error_NotAvailable", cancelled: false };
    }

    const cameraAvailable = !!(navigator.mediaDevices && navigator.mediaDevices.getUserMedia);
    if (!cameraAvailable) {
        return { success: false, errorCode: "QrScan_Error_NoCamera", cancelled: false };
    }

    let detectorHooks = await setupNativeDetector();
    if (!detectorHooks) {
        detectorHooks = await setupJsQrFallback(options);
    }

    if (!detectorHooks) {
        return { success: false, errorCode: "QrScan_Error_NoBarcodeApi", cancelled: false };
    }

    let stream;
    try {
        stream = await navigator.mediaDevices.getUserMedia({
            video: {
                facingMode: { ideal: "environment" }
            },
            audio: false
        });
    }
    catch (err) {
        console.warn("getUserMedia failed", err);
        let errorCode = "QrScan_Error_PermissionDenied";
        if (err && err.name === "NotFoundError") {
            errorCode = "QrScan_Error_NoCamera";
        }
        else if (err && err.name === "NotAllowedError") {
            errorCode = "QrScan_Error_PermissionDenied";
        }
        else if (err && err.name === "SecurityError") {
            errorCode = "QrScan_Error_PermissionDenied";
        }
        return { success: false, errorCode, cancelled: false };
    }

    ensureStyles();
    const overlay = document.createElement("div");
    overlay.className = overlayClass;

    const heading = document.createElement("h3");
    heading.textContent = options?.promptTitle ?? "";

    const instructions = document.createElement("p");
    instructions.textContent = options?.promptMessage ?? "";

    const video = document.createElement("video");
    video.setAttribute("playsinline", "true");
    video.muted = true;
    video.srcObject = stream;

    const cancel = document.createElement("button");
    cancel.type = "button";
    cancel.textContent = options?.cancelLabel ?? "";

    overlay.append(heading, instructions, video, cancel);
    document.body.appendChild(overlay);

    const stop = () => {
        if (stream) {
            stream.getTracks().forEach(track => track.stop());
        }
        video.srcObject = null;
        overlay.remove();
        try {
            detectorHooks?.dispose?.();
        }
        catch (err) {
            console.warn("Detector dispose failed", err);
        }
    };

    return await new Promise((resolve) => {
        let active = true;
        let frameId = 0;
        const scanInterval = typeof detectorHooks.scanInterval === "number" && detectorHooks.scanInterval > 0 ? detectorHooks.scanInterval : 0;
        let lastScan = 0;

        const cleanupAndResolve = (result) => {
            if (!active) {
                return;
            }
            active = false;
            if (frameId) {
                cancelAnimationFrame(frameId);
            }
            stop();
            resolve(result);
        };

        const detectLoop = async () => {
            if (!active) {
                return;
            }

            try {
                if (video.readyState >= HTMLMediaElement.HAVE_ENOUGH_DATA) {
                    const now = typeof performance !== "undefined" ? performance.now() : Date.now();
                    if (!scanInterval || now - lastScan >= scanInterval) {
                        lastScan = now;
                        const payload = await detectorHooks.detect(video);
                        if (typeof payload === "string" && payload.length > 0) {
                            cleanupAndResolve({ success: true, payload, errorCode: null, cancelled: false });
                            return;
                        }
                    }
                }
            }
            catch (err) {
                console.warn("QR detection failed", err);
            }

            frameId = requestAnimationFrame(detectLoop);
        };

        cancel.addEventListener("click", () => cleanupAndResolve({ success: false, payload: null, errorCode: null, cancelled: true }));

        video.addEventListener("loadedmetadata", () => {
            video.play().catch(err => console.warn("Video play() failed", err));
            detectLoop();
        }, { once: true });

        video.addEventListener("error", (evt) => {
            console.error("Video error", evt);
            cleanupAndResolve({ success: false, payload: null, errorCode: "QrScan_Error_Stream", cancelled: false });
        }, { once: true });
    });
}
