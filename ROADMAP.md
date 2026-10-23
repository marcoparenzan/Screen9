# Roadmap

Where Screen9 is going. Each section says what exists **today** and what comes **next**; the "next" items are plans, not promises, and
are ordered roughly by how soon they are wanted. The log of what has already been done, step by step, is in
[docs/engine/ROADMAP.md](docs/engine/ROADMAP.md) and the known limits are in [docs/engine/OPENPOINTS.md](docs/engine/OPENPOINTS.md).

## 1. Mode 13

VGA mode 13 is 320×200 with 256 colours: one byte per pixel, a programmable palette of 256 entries.

**Today**
- `PCCanvas(13)`: a linear 64 000-byte framebuffer, the 6-bit VGA DAC (`SetDac`, ports `&H3C8`/`&H3C9`), double buffering (`ManualPresent`),
  and `PutPixels` to write a whole frame at once.
- The plasma demo (palette cycling over 240 colours with a bouncing sprite), on the site and in a terminal as truecolor half blocks.

**Next**
- A small library of mode 13 effects for the demos: fire, starfield, tunnel, scroller.
- Palette helpers: fades in and out, rotations, gradients — what every mode 13 program writes by hand.
- A first game in mode 13 (the engine has only 16-colour games so far).
- Not planned, and said so in the specification: chain-4 "mode X", CRTC scrolling, split screen. Only what games really use.

## 2. Sprites

**Today**
- `GetImage`/`PutImage` on indexed images, with the raster operations of `PUT` (copy, PRESET, XOR, AND, OR) and a transparent colour.
- `PCSprite`: the LONG-array buffers of QBasic's `GET`/`PUT`, in every mode.
- Rally-X animates its own sprites (a 12-frame rotation per car) from an asset file built by `ops/build-rallyx-assets.py`.

**Next**
- A real sprite layer in the engine, so a game does not write its own: sprite sheets, named animations, flip, draw order.
- Loading images: the engine has no PNG decoder, so art goes through a converter script today; a loader for indexed PNG would remove that step.
- Collision helpers: bounding boxes first, then pixel masks.
- Scaling and rotation for sprites (nearest-neighbour, so they stay pixel art).

## 3. More games

**Today**
- Gorillas in five forms (QBasic, Python, C# script, native C#, and the original `.bas` file), and Rally-X in C#.
- Rally-X is a **work in progress**: playable, with an autopilot, but speeds and difficulty are not balanced against the arcade.

**Next**
- Finish Rally-X: tuning, mouse and touch control, a saved high score, proper sound effects, a closer camera.
- Nibbles, the other classic that shipped with QBasic, running unchanged on QBSharp and then as a C# game.
- More programs from the QBasic archive running without changes — each one is a test of the interpreter and of the engine.
- A mode 13 game (see above), and a game that uses the sprite layer from the start.

## 4. Performance improvements

**Today**
- `PutPixels` writes a 640×480 frame in about 5 ms (against 45 ms for `PutImage`); the games run at 30 frames a second, in the browser as WebAssembly (the plasma at 60 with AOT; interpreted it is ~22).
- GORILLA.BAS pauses are brought back to their intended length by a calibration of the interpreter's speed (`QBS_TIMER_WAIT_SCALE=auto`).
- Benchmarks exist for the editor (`QBSharp.Benchmarks`); the pages still on the server (Python, C# script, editors) send one indexed PNG per frame at ~30 Hz, latest frame wins.

**Next**
- `PutImage`, `Box` and the fills on the fast path that `PutPixels` already uses, instead of `PSet` pixel by pixel.
- Planar video memory (modes 7–12): update four planes eight pixels at a time instead of pixel by pixel.
- The interpreter: the AST walk is the cost of QBasic programs; look at caching and cheaper dispatch for the hot statements.
- Debug builds: the engine is several times slower unoptimised (45 ms against 5 ms for a Rally-X frame). Optimise the libraries in Debug too,
  or warn when the site is started that way.
- Frames from the server (Python, C# script, editors): sending raw indices plus palette was measured and is not worth it (PNG encoding 2–7 ms, raw frames 2.5× larger);
  sending only what changed would be, or moving those pages to the browser too (PySharp needs an async `sleep`; Roslyn is heavy in WebAssembly).
- First load of a game page: the AOT runtime is ~28 MB on disk; measure it on a real network, and trim (linker, fewer assemblies) if it matters.
- Open: the QBasic editor on the site has been reported slow; the program runs as fast from the editor as from its own page in a headless
  measurement, so the cause has not been found yet. Needs a measurement in a real browser.
