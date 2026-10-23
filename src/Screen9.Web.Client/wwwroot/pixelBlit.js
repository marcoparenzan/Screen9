// Draws RGBA frames from the in-process .NET runtime onto <canvas> elements, and paces loops on requestAnimationFrame.
// Called through [JSImport] (see Components/PixelBlit.cs); the frame is copied straight into the ImageData: no PNG,
// no encode, no network. One entry per canvas id, so several screens can live on a page.
const screens = new Map();

export function attach(id, width, height) {
    const canvas = document.getElementById(id);
    canvas.width = width;
    canvas.height = height;
    const ctx = canvas.getContext('2d');
    screens.set(id, { canvas, ctx, image: ctx.createImageData(width, height) });
}

export function draw(id, bytes) {
    const screen = screens.get(id);
    if (!screen) return;
    screen.image.data.set(bytes);
    screen.ctx.putImageData(screen.image, 0, 0);
}

export function detach(id) { screens.delete(id); }

/** Downloads the frame on the screen as a PNG (its native pixels). */
export function save(id, name) {
    const screen = screens.get(id);
    if (!screen) return;
    screen.canvas.toBlob(blob => {
        const url = URL.createObjectURL(blob);
        const link = document.createElement('a');
        link.href = url;
        link.download = name || `screen9-${new Date().toISOString().replace(/[:.]/g, '-')}.png`;
        document.body.appendChild(link);
        link.click();
        link.remove();
        setTimeout(() => URL.revokeObjectURL(url), 1000);
    }, 'image/png');
}

export function nextFrame() {
    return new Promise(resolve => requestAnimationFrame(resolve));
}
