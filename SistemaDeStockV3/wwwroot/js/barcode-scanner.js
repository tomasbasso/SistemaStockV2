// Barcode Scanner JS Interop
// Usa html5-qrcode para acceder a la cámara vía WebView2

let html5QrCode = null;
let isScanning = false;

window.barcodeScanner = {
    start: async function (elementId, dotNetHelper) {
        if (isScanning) return;

        try {
            html5QrCode = new Html5Qrcode(elementId);

            const devices = await Html5Qrcode.getCameras();
            if (!devices || devices.length === 0) {
                await dotNetHelper.invokeMethodAsync('OnScannerError', 'No se encontró ninguna cámara disponible.');
                return;
            }

            // Preferir cámara trasera, si no la primera disponible
            const backCamera = devices.find(d =>
                d.label.toLowerCase().includes('back') ||
                d.label.toLowerCase().includes('rear') ||
                d.label.toLowerCase().includes('trasera') ||
                d.label.toLowerCase().includes('environment')
            );
            const cameraId = backCamera ? backCamera.id : devices[0].id;

            await html5QrCode.start(
                cameraId,
                {
                    fps: 15,
                    qrbox: function (viewfinderWidth, viewfinderHeight) {
                        const minEdge = Math.min(viewfinderWidth, viewfinderHeight);
                        return { width: Math.floor(minEdge * 0.75), height: Math.floor(minEdge * 0.45) };
                    },
                    aspectRatio: 1.5,
                    formatsToSupport: [
                        Html5QrcodeSupportedFormats.EAN_13,
                        Html5QrcodeSupportedFormats.EAN_8,
                        Html5QrcodeSupportedFormats.CODE_128,
                        Html5QrcodeSupportedFormats.CODE_39,
                        Html5QrcodeSupportedFormats.QR_CODE,
                        Html5QrcodeSupportedFormats.UPC_A,
                        Html5QrcodeSupportedFormats.UPC_E,
                    ]
                },
                async (decodedText) => {
                    await dotNetHelper.invokeMethodAsync('OnBarcodeScanned', decodedText);
                },
                (errorMessage) => {
                    // Ignorar errores de frames sin código (es normal)
                }
            );

            isScanning = true;
            await dotNetHelper.invokeMethodAsync('OnScannerStarted');
        } catch (err) {
            let msg = err.toString();
            if (msg.includes('Permission') || msg.includes('permission') || msg.includes('NotAllowed')) {
                msg = 'Permiso de cámara denegado. Revisá la configuración del sistema.';
            }
            await dotNetHelper.invokeMethodAsync('OnScannerError', msg);
        }
    },

    stop: async function () {
        if (html5QrCode && isScanning) {
            try {
                await html5QrCode.stop();
                html5QrCode = null;
                isScanning = false;
            } catch (err) {
                console.error('Error al detener el scanner:', err);
                html5QrCode = null;
                isScanning = false;
            }
        }
    },

    isRunning: function () {
        return isScanning;
    }
};
