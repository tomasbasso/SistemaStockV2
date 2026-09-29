// Procesa el logo elegido en Configuración antes de guardarlo en la base:
//  - recorta los márgenes lisos (blanco o transparente) para que el logo aproveche el encabezado del PDF,
//  - lo limita a 1200 px por lado (una foto de 12 MP no hace falta para un encabezado),
//  - vuelve transparente el fondo blanco: los PDF son blancos, así que no cambia el aspecto, pero evita
//    que la recompresión JPEG del PDF deje una "caja" grisácea alrededor de los logos en JPG,
//  - detecta el color de marca: el tono saturado más frecuente del logo.
window.logoMarca = {
    procesar: async function (dataUrl) {
        const img = await new Promise((resolve, reject) => {
            const i = new Image();
            i.onload = () => resolve(i);
            i.onerror = () => reject(new Error('No se pudo leer la imagen.'));
            i.src = dataUrl;
        });

        const w = img.naturalWidth, h = img.naturalHeight;
        const original = document.createElement('canvas');
        original.width = w;
        original.height = h;
        const ctx = original.getContext('2d', { willReadFrequently: true });
        ctx.drawImage(img, 0, 0);
        const px = ctx.getImageData(0, 0, w, h).data;

        // ── Recorte de márgenes ─────────────────────────────────────────
        const esFondo = (i) => px[i + 3] < 16 || (px[i] > 238 && px[i + 1] > 238 && px[i + 2] > 238);
        let x0 = w, y0 = h, x1 = -1, y1 = -1;
        for (let y = 0; y < h; y++) {
            for (let x = 0; x < w; x++) {
                if (esFondo((y * w + x) * 4)) continue;
                if (x < x0) x0 = x;
                if (x > x1) x1 = x;
                if (y < y0) y0 = y;
                if (y > y1) y1 = y;
            }
        }
        if (x1 < 0) { x0 = 0; y0 = 0; x1 = w - 1; y1 = h - 1; } // imagen lisa: se deja entera

        const margen = Math.round(Math.max(x1 - x0, y1 - y0) * 0.015);
        x0 = Math.max(0, x0 - margen);
        y0 = Math.max(0, y0 - margen);
        x1 = Math.min(w - 1, x1 + margen);
        y1 = Math.min(h - 1, y1 + margen);
        const cw = x1 - x0 + 1, ch = y1 - y0 + 1;

        const escala = Math.min(1, 1200 / Math.max(cw, ch));
        const salida = document.createElement('canvas');
        salida.width = Math.max(1, Math.round(cw * escala));
        salida.height = Math.max(1, Math.round(ch * escala));
        const sctx = salida.getContext('2d');
        sctx.imageSmoothingEnabled = true;
        sctx.imageSmoothingQuality = 'high';
        sctx.drawImage(original, x0, y0, cw, ch, 0, 0, salida.width, salida.height);

        const final = sctx.getImageData(0, 0, salida.width, salida.height);
        const f = final.data;
        for (let i = 0; i < f.length; i += 4) {
            if (f[i] >= 242 && f[i + 1] >= 242 && f[i + 2] >= 242) f[i + 3] = 0;
        }
        sctx.putImageData(final, 0, 0);

        // ── Color de marca ──────────────────────────────────────────────
        // Histograma de 24 tonos sobre los píxeles opacos y saturados; se promedia el tono ganador.
        const baldes = Array.from({ length: 24 }, () => ({ n: 0, r: 0, g: 0, b: 0 }));
        const paso = Math.max(1, Math.floor(Math.sqrt((cw * ch) / 90000)));
        for (let y = y0; y <= y1; y += paso) {
            for (let x = x0; x <= x1; x += paso) {
                const i = (y * w + x) * 4;
                if (px[i + 3] < 128) continue;
                const r = px[i] / 255, g = px[i + 1] / 255, b = px[i + 2] / 255;
                const max = Math.max(r, g, b), min = Math.min(r, g, b), d = max - min;
                if (max < 0.25 || d / max < 0.45) continue; // grises, negros y blancos no cuentan
                let tono = max === r ? ((g - b) / d) % 6 : max === g ? (b - r) / d + 2 : (r - g) / d + 4;
                tono = (tono * 60 + 360) % 360;
                const balde = baldes[Math.floor(tono / 15)];
                balde.n++; balde.r += px[i]; balde.g += px[i + 1]; balde.b += px[i + 2];
            }
        }
        const ganador = baldes.reduce((a, b) => (b.n > a.n ? b : a));
        let color = null;
        if (ganador.n >= 20) {
            const hex = (v) => Math.round(v / ganador.n).toString(16).padStart(2, '0');
            color = ('#' + hex(ganador.r) + hex(ganador.g) + hex(ganador.b)).toUpperCase();
        }

        return { dataUrl: salida.toDataURL('image/png'), color: color };
    }
};
