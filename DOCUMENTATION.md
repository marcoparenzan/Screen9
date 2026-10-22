# Documentation

Everything written about Screen9, in one place. Start with the first ones.

| Document | What it is |
|---|---|
| [README.md](README.md) | What the project is, the parts, the samples, how to build and test |
| [USAGE.md](USAGE.md) | Quick start for the whole repository: requirements, play the games, the site, the engine from your own code |
| [HISTORY.md](HISTORY.md) | The story, from QBasic's `SCREEN 9` to the engine and the web |
| [ROADMAP.md](ROADMAP.md) | Where it is going: mode 13, sprites, more games, performance |

## The engine (`docs/engine`)

| Document | What it is |
|---|---|
| [README](docs/engine/README.md) | Overview, versions, included fonts, devices |
| [USAGE](docs/engine/USAGE.md) | Using the canvas, the renderers, the devices; `PutPixels` and integer scaling |
| [SPEC](docs/engine/SPEC.md) | The contract: modes, palettes, memory, ports, fonts |
| [ARCHITECTURE](docs/engine/ARCHITECTURE.md) | How it is built |
| [ROADMAP](docs/engine/ROADMAP.md) | What is done and what comes next, with the log of each step |
| [OPENPOINTS](docs/engine/OPENPOINTS.md) | Gates, limits and what is not verified |
| [PROMPTS](docs/engine/PROMPTS.md) | The requests that shaped it |

## QBSharp, the QBasic/QBS language (`docs/qbsharp`)

| Document | What it is |
|---|---|
| [README](docs/qbsharp/README.md) | Overview, the editor, Ralf, embedding |
| [USAGE](docs/qbsharp/USAGE.md) | The CLI, the language by example, the editor, QBasic mode |
| [SPEC](docs/qbsharp/SPEC.md) | The language and CLI specification (sections 1–20) |
| [ARCHITECTURE](docs/qbsharp/ARCHITECTURE.md) | The interpreter, the editor, the QBasic dialect |
| [OPENPOINTS](docs/qbsharp/OPENPOINTS.md) | Gates, limits and what is not verified |
| [PROMPTS](docs/qbsharp/PROMPTS.md) | The requests that shaped it |

## The web site (`docs/web`)

| Document | What it is |
|---|---|
| [README](docs/web/README.md) | The pages and the menu, how it works, security of server-run code, known limits |

On the site itself: **Screen9 → Library** is the Coding Guide with runnable mode 9 snippets (they are compiled in
`src/Screen9.Tests/LibrarySnippetsTests.cs`), and **Screen9 → Mode 9 history** is [HISTORY.md](HISTORY.md).

## Samples

| Document | What it is |
|---|---|
| [samples/csharp/RallyX/README.md](samples/csharp/RallyX/README.md) | Rally-X (work in progress): rules, assets, differences from the original project |
| [samples/csharp/Demos/README.md](samples/csharp/Demos/README.md) | The plasma and cell-canvas demos |
| [samples/python/Gorilla/README.md](samples/python/Gorilla/README.md) | Gorillas in Python on PySharp |
| [docs/images](docs/images) | Screenshots of the games and demos, ready to reuse |

## Tools (`ops/`)

| File | What it does |
|---|---|
| `build-python-wheel.cs` | Builds the `screen9` wheel for PySharp |
| `csx.cs` | Runs a `.csx` script on the engine (Gorillas as a C# script) |
| `build-rallyx-assets.py` | Converts the RallyX art into `rallyx.assets` |
| `make-screenshots.cs` | Renders the screenshots in `docs/images` |
| `measure-gorilla-calibration.cs`, `measure-pixel-idle.cs` | Measurements used while tuning performance |

## Questions, comments, bugs

[GitHub Issues](https://github.com/marcoparenzan/Screen9/issues) — also linked from the footer of every page of the site.
