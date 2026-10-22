#!/usr/bin/env python3
"""Converts the RallyX assets (Phaser, pixel art enlarged 4x) into samples/csharp/RallyX/Assets/rallyx.assets.

Usage:  python -I ops/build-rallyx-assets.py <RallyX folder> [--preview <folder>]

- Brings the art back to true resolution (1x: 24 px tiles, 16 px sprites, 320x240 screen).
- Game and HUD: one 16-colour EGA/VGA palette (nearby colours are merged); title: its own 16-colour palette.
- Removes the "NAONIAN RETROGAMING SOCIETY" logo from title.png (text and stripes) and from hud.png (at the bottom).
- Checks that the tileset + JSON map rebuild the exported map (rallyx-map.png).
Requires Pillow. File format: see RallyXAssets.cs.
"""
import json, struct, sys, os
from PIL import Image

SCALE = 4
TILE = 24
# black, white (the light grey of the HUD text), yellow, red, blue of the radar panel, green of the HUD: indices 0..5
# 6: road (sand), 7: grass (lime), 8: cyan of the score
ROAD = (228, 164, 89)
GRASS = (85, 194, 0)
FORCED = [(0, 0, 0), (222, 222, 222), (255, 255, 0), (255, 0, 0), (33, 71, 151), (71, 184, 0), ROAD, GRASS, (52, 223, 222)]
LOGO_TITLE_ROWS = 131    # 1x rows of the title taken by the logo (the RALLY-X banner starts below)
LOGO_HUD_FROM = 192      # 1x rows of the HUD taken by the logo (the last ones)


def shrink(path):
    im = Image.open(path).convert('RGBA')
    w, h = im.size
    # the pixel art is an almost clean 4x: each true pixel is the most frequent colour of its 4x4 block
    out = Image.new('RGBA', (w // SCALE, h // SCALE))
    src, dst = im.load(), out.load()
    for y in range(h // SCALE):
        for x in range(w // SCALE):
            block = [src[x * SCALE + i, y * SCALE + j] for j in range(SCALE) for i in range(SCALE)]
            opaque = [p for p in block if p[3] >= 128]
            if len(opaque) * 2 < len(block):
                dst[x, y] = (0, 0, 0, 0)
            else:
                dst[x, y] = max(set(opaque), key=opaque.count)
    return out


def dist2(a, b):
    return sum((p - q) ** 2 for p, q in zip(a, b))


def reduce_palette(counts, size):
    """Ward merging: the forced colours weigh a lot, so they absorb their neighbours without moving."""
    clusters = [[c, n, [c]] for c, n in counts.items()]
    for c in FORCED:
        clusters = [k for k in clusters if k[0] != c]
        clusters.append([c, 10 ** 9, [c]])
    while len(clusters) > size:
        best = None
        for i in range(len(clusters)):
            for j in range(i + 1, len(clusters)):
                a, b = clusters[i], clusters[j]
                cost = a[1] * b[1] / (a[1] + b[1]) * dist2(a[0], b[0])
                if best is None or cost < best[0]:
                    best = (cost, i, j)
        _, i, j = best
        a, b = clusters[i], clusters[j]
        n = a[1] + b[1]
        mean = tuple(round((a[0][k] * a[1] + b[0][k] * b[1]) / n) for k in range(3))
        merged = [mean, n, a[2] + b[2]]
        clusters = [k for t, k in enumerate(clusters) if t not in (i, j)] + [merged]
    palette = [c for c, _, _ in clusters]
    for c in FORCED:      # the forced colours stay exact, in the first positions
        palette.remove(min(palette, key=lambda p: dist2(p, c)))
    palette = FORCED + sorted(palette)
    while len(palette) < size:
        palette.append((0, 0, 0))
    return palette


def nearest(palette, color):
    return min(range(len(palette)), key=lambda i: dist2(palette[i], color))


def indexed(im, palette, clear=255):
    cache, out = {}, bytearray()
    for r, g, b, a in im.getdata():
        if a < 128:
            out.append(clear)
            continue
        key = (r, g, b)
        if key not in cache:
            cache[key] = nearest(palette, key)
        out.append(cache[key])
    return bytes(out)


def main():
    if len(sys.argv) < 2:
        sys.exit(__doc__)
    root = sys.argv[1]
    preview = sys.argv[sys.argv.index('--preview') + 1] if '--preview' in sys.argv else None
    a = os.path.join(root, 'Assets')
    out_path = os.path.join(os.path.dirname(os.path.abspath(__file__)), '..', 'samples', 'csharp', 'RallyX', 'Assets', 'rallyx.assets')

    sheets = {n: shrink(os.path.join(a, f)) for n, f in [
        ('tiles', 'rallyx-map-tileset.png'), ('cars', 'car-spritesheet.png'), ('flags', 'flag-spritesheet.png'),
        ('rocks', 'rock-spritesheet.png'), ('smoke', 'smoke-spritesheet.png'), ('gameover', 'gameover-spritesheet.png'),
        ('hud', 'hud.png')]}
    title = shrink(os.path.join(a, 'title.png'))

    # the tileset green is more teal than the game one (rallyx-map.png): the lime one is used
    tiles = sheets['tiles']
    for y in range(tiles.height):
        for x in range(tiles.width):
            if tiles.getpixel((x, y))[:3] == (0, 186, 70):
                tiles.putpixel((x, y), GRASS + (255,))

    # HUD: remove the logo at the bottom (black)
    hud = sheets['hud']
    for y in range(LOGO_HUD_FROM, hud.height):
        for x in range(hud.width):
            hud.putpixel((x, y), (0, 0, 0, 255))

    # Map: gid 1 is the road (black in the exported map; in the tileset the first square is a yellow placeholder)
    m = json.load(open(os.path.join(a, 'rallyx-map.json')))
    cols, rows = m['width'], m['height']
    gids = m['layers'][0]['data']
    tiles = sheets['tiles']
    tile_cols = tiles.width // TILE
    rebuilt = Image.new('RGBA', (cols * TILE, rows * TILE), (0, 0, 0, 255))
    for i, g in enumerate(gids):
        if g > 1:
            k = g - 1
            rebuilt.paste(tiles.crop(((k % tile_cols) * TILE, (k // tile_cols) * TILE, (k % tile_cols + 1) * TILE, (k // tile_cols + 1) * TILE)),
                          ((i % cols) * TILE, (i // cols) * TILE))
    ref = os.path.join(root, '__other__', 'Stuff', 'rallyx-map.png')
    if os.path.exists(ref):
        r = Image.open(ref).convert('RGB')
        # the green of rallyx-map.png is more lime than the tileset one: only the shape is compared (black road / rest)
        black = (0, 0, 0)
        bad = sum(1 for p, q in zip(rebuilt.convert('RGB').getdata(), r.getdata()) if (p == black) != (q == black))
        print('rebuilt map vs rallyx-map.png (black road): %d different pixels out of %d' % (bad, r.width * r.height))

    # Game palette from all the game pixels (excluding the yellow placeholder road of the tileset)
    counts = {}
    def add(im, skip=None):
        for i, (r, g, b, al) in enumerate(im.getdata()):
            if al >= 128 and not (skip and skip(i % im.width, i // im.width)):
                counts[(r, g, b)] = counts.get((r, g, b), 0) + 1
    for n, im in sheets.items():
        add(im, (lambda x, y: x < TILE and y < TILE) if n == 'tiles' else None)
    palette = reduce_palette(counts, 16)
    print('game palette:', palette)

    # Title: remove the logo (the whole block above the banner becomes white), then 16 colours
    for y in range(LOGO_TITLE_ROWS):
        for x in range(title.width):
            title.putpixel((x, y), (255, 255, 255, 255))
    tq = title.convert('RGB').quantize(colors=16, method=Image.Quantize.MEDIANCUT, dither=Image.Dither.NONE)
    tp = tq.getpalette()[:48]
    title_palette = [tuple(tp[i * 3:i * 3 + 3]) for i in range(16)]
    title_pixels = bytes(tq.getdata())
    white_t = min(range(16), key=lambda i: dist2(title_palette[i], (255, 255, 255)))
    black_t = min(range(16), key=lambda i: dist2(title_palette[i], (0, 0, 0)))
    accent_t = min(range(16), key=lambda i: dist2(title_palette[i], (220, 80, 0)))

    sections = [(n, im.width, im.height, indexed(im, palette)) for n, im in sheets.items()]
    sections.append(('title', title.width, title.height, title_pixels))
    sections.append(('map', cols, rows, bytes(gids)))

    os.makedirs(os.path.dirname(out_path), exist_ok=True)
    with open(out_path, 'wb') as f:
        f.write(b'RLX1')
        for p in palette + title_palette:
            f.write(bytes(p))
        f.write(bytes([white_t, black_t, accent_t]))
        f.write(struct.pack('<H', len(sections)))
        for name, w, h, data in sections:
            nb = name.encode('ascii')
            f.write(bytes([len(nb)]) + nb + struct.pack('<HH', w, h) + data)
    print('written', os.path.normpath(out_path), os.path.getsize(out_path), 'bytes')

    if preview:
        os.makedirs(preview, exist_ok=True)
        def render(w, h, data, pal, name):
            im = Image.new('RGBA', (w, h))
            im.putdata([(0, 0, 0, 0) if v == 255 else pal[v] + (255,) for v in data])
            im.resize((w * 3, h * 3), Image.NEAREST).save(os.path.join(preview, name + '.png'))
        for name, w, h, data in sections:
            if name == 'map':
                continue
            render(w, h, data, title_palette if name == 'title' else palette, name)
        rebuilt.save(os.path.join(preview, 'map-rebuilt.png'))


main()
