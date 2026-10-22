# Demos

Two small demos of the engine. The drawing lives in a library (`Core/`, `Screen9.DemosCore`) with no device in it, so the same code
runs in a Windows console and on the website (**C# Demos** menu: Plasma and Scene).

| Demo | What it shows | Class |
|---|---|---|
| **Scene** | The cell canvas (a character and two of 16 colours per cell): text, a rectangle, a line, and a ball and a counter animated over a scene captured once. Only the changed cells are written. | `SceneDemo` |
| **Plasma** | A VGA mode 13 canvas (320×200, 256 colours): a plasma whose colours move by cycling the palette (`SetDac`), a bouncing sprite (`PutImage`), double buffering (`ManualPresent`) and one `PutPixels` call per frame. | `PlasmaDemo` |

```powershell
dotnet run --project samples/csharp/Demos                  # the scene, in a Windows console
dotnet run --project samples/csharp/Demos -- --vga         # the plasma, as truecolor half blocks (VT terminal)
dotnet run --project samples/csharp/Demos -- --smoke       # animate a fixed number of frames and check what the console shows
```

`--smoke` reads the console window back (`ReadConsoleOutputW`) and compares it with the canvas: exit 0 is fine, 2 means a difference.
On the site, the pages show each demo's code and have a **Save PNG** button under the screen.
