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
        const bitmap = await createImageBitmap(new Blob([bytes], { type: 'image/png' }));
        if (this.canvas.width !== bitmap.width || this.canvas.height !== bitmap.height) {
            this.canvas.width = bitmap.width;
            this.canvas.height = bitmap.height;
        }
        this.ctx.drawImage(bitmap, 0, 0);
        bitmap.close();
    }

    dispose() { /* nothing to release: the canvas element itself is owned by Blazor */ }
}

export function createPixelCanvas(canvasElement) {
    return new PixelCanvas(canvasElement);
}
