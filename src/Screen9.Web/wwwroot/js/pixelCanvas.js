// Draws Screen9.PngFrame bytes (sent from .NET as one byte[] argument, arriving here as a Uint8Array) onto a
// <canvas>, resizing the canvas to the frame's native resolution the first time it changes. The browser's own
// PNG decoder does the pixel work; this class only owns the 2D context and the resize bookkeeping.
export class PixelCanvas {
    constructor(canvasElement) {
        this.canvas = canvasElement;
        this.ctx = canvasElement.getContext('2d');
        this.ctx.imageSmoothingEnabled = false; // retro pixels stay sharp when the canvas is scaled by CSS
    }

    async drawPng(bytes) {
        this.lastPng = bytes; // kept so the screen can be saved as it is
        const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
        if (this.canvas.width !== bitmap.width || this.canvas.height !== bitmap.height) {
            this.canvas.width = bitmap.width;
            this.canvas.height = bitmap.height;
        }
        this.ctx.drawImage(bitmap, 0, 0);
        bitmap.close();
    }

    /** Downloads the frame on the screen as a PNG (its native pixels: 640×350 in mode 9). */
    save(name) {
        if (!this.lastPng) return;
        const url = URL.createObjectURL(new Blob([this.lastPng], { type: 'image/png' }));
        const link = document.createElement('a');
        link.href = url;
        link.download = name || `screen9-${new Date().toISOString().replace(/[:.]/g, '-')}.png`;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }

    dispose() { /* nothing to release: the canvas element itself is owned by Blazor */ }
}

export function createPixelCanvas(canvasElement) {
    return new PixelCanvas(canvasElement);
}
