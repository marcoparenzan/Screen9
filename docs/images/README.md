# Screenshots

Ready to reuse in documents, slides and the README. Each picture comes in two files:

- `name.png` — 1280×960, shown as a monitor showed it (4:3: the pixels of EGA mode 9 are not square), nearest-neighbour scaled.
- `name-native.png` — the framebuffer's own pixels, unscaled.

| Picture | What it shows | Native size |
|---|---|---|
| `gorillas-city` | Gorillas in C#, mode 9 (EGA): the city, the gorillas, the wind arrow, waiting for the angle | 640×350 |
| `rallyx-title` | Rally-X title screen (mode 12, its own 16-colour palette), without the third-party logo | 640×480 |
| `rallyx-play` | Rally-X a few seconds into a round, the autopilot driving; HUD and radar on the right | 640×480 |
| `rallyx-crash` | Rally-X, the car hits a rock (the BANG frame, one life less) | 640×480 |
| `plasma` | The VGA mode 13 plasma demo (palette cycling) with its ball | 320×200 |
| `scene` | The cell canvas demo (text mode, 80×25 cells with the IBM VGA ROM font) | 640×400 |
| `editor-code` | The QBasic editor with GORILLA.BAS (QBasic dialect), syntax colours | 640×384 |
| `editor-menu` | The same editor with the Run menu open | 640×384 |
| `editor-run` | The editor after running a small program: the output in the lower pane | 640×384 |

They are rendered by `dotnet run ops/make-screenshots.cs` straight from the engine (no window, no browser), so they can be made again
after any change. The Rally-X art is the original Rally-X (Namco/Midway), used for demonstration only. The Monaco editor is a web page and is
not here: a screenshot of it needs a browser (the **Save PNG** button on the site's screens saves the program screen, not the page).
