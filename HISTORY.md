# Mode 9 history

> The same story is on the site, under **Screen9 → Mode 9 history**.

`SCREEN 9` was the best graphics mode of QBasic on an EGA card: 640×350 pixels, 16 colours out of a palette of 64, two video
pages, text in an 80×25 grid of 8×14 cells. Its pixels are not square, so the picture is shown at 4:3. This engine started as a
way to run the games written for it, and kept growing from there.

## 1 · QBasic

The starting point is `GORILLA.BAS`, the game Microsoft shipped with QBasic in 1990: two gorillas on a skyline, a banana, the
wind. A whole program in a single file, with pixel graphics, text, sounds and keyboard. The first question was simple: *can it
still be run today, untouched?*

That is how **QBSharp** was born: a QBasic interpreter written in C#, with a modern dialect next to the classic one (`qbs`) and a
full-screen editor. GORILLA.BAS runs without changes.

## 2 · The engine

To run a game of that kind a machine was needed, not an interpreter: an indexed framebuffer with the CGA/EGA/VGA modes, the
palette, pages, primitives (point, line, circle, fill), `GET`/`PUT` sprites, the IBM ROM fonts, `PLAY` and the speaker, the pixel
window. All this was split out of the interpreter and called **Screen9**.

The point is that the engine knows nothing about QBasic: it is used from C# alone, as a retro engine for games. See the
[Coding Guide](docs/engine/USAGE.md) and the [specification](docs/engine/SPEC.md).

## 3 · Embedding

At that point Gorilla could live in several forms on the same engine. In C#, directly (`GorillasCore`: the game logic, with
nothing native, and the executable reduced to a simple "Run"). In QBasic, run by the interpreter. And the next step: **having an
embeddable language run Gorillas**, on a par with any other, because the engine is a neutral host: whoever embeds it hands it
screen, keyboard and sound, and the language does the rest.

## 4 · Python and C# scripting

The second embeddable language is **PySharp**, a Python interpreter written in C# that can be embedded too. Screen9 became a
`screen9` package installed with its mini-pip (`pysharp install screen9`) and used with `import screen9`: `gorillas.py` plays the
same match as the C# one, with the same seed and the same frames, launched by `pysharp run`, with no C# host in between.

And C# itself, as an embeddable language: `gorillas.csx` is the same game as a script, run by C# scripting (Roslyn) with the same
devices (`dotnet run ops/csx.cs -- gorillas.csx`). On the web, Monaco picks the interpreter from the file extension: `.bas` and `.py` run in the browser, `.csx` only on the server.

## 5 · More C# games and demos

A second game shows the engine is not about one title: **Rally-X**, brought from a Phaser project to true pixel art in mode 12
(640×480, 16 colours), with its own device-free simulation and an autopilot used for tests and for the demo. It is still a work
in progress ([README](samples/csharp/RallyX/README.md)). Two small demos — a VGA mode 13 plasma and the cell canvas — show the
rest of the engine ([Demos](samples/csharp/Demos/README.md)).

## 6 · The web

The site: the same engine and the same interpreters, served by a browser (Blazor Server): each tab is an independent session.
The screen is a canvas, the keyboard the browser's, the audio Web Audio. The program opens in a window in front of the page.
Later the screens moved into the browser (Blazor WebAssembly): the games and GORILLA.BAS run there, with no frame crossing the network, 60 frames a second
for the plasma once ahead-of-time compiled; Python too, once PySharp learned to wait instead of blocking a thread (`PyEngine.RunAsync`); only the C# script game and the editors stay on the server.
See [docs/web](docs/web/README.md).
