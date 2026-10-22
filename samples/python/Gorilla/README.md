# Gorillas in Python

The third gorilla: the same game as `samples/csharp/Gorilla` and `samples/qbsharp/Gorilla`, written in Python and run by **PySharp** (a Python interpreter in .NET) on the Screen9 engine, through the `screen9` package.

`gorillas.py` is a faithful port of `Gorillas.cs`: same names, same integer arithmetic and `CINT`-style rounding, `System.Random` for the same random sequence. It uses the engine types directly (`PCCanvas`, `PCTextDisplay`, `PCMusic`, `PCSpeaker`, `PCSprite`, `PixelWindow`) imported from `screen9`; there is no C# host.

## The `screen9` package

A `py3-none-any` wheel distributed outside PyPI and installed with PySharp's mini-pip. It contains `screen9/__init__.py` (source in `src/Screen9.Python`), `Screen9.dll` with its dependencies and the IBM font licences (CC BY-SA 4.0). `__init__.py` loads the DLL with PySharp's `clr` module and re-exports the types; `long_array(n)` creates the LONG array used by `GET`/`PUT`.

```powershell
dotnet run ops/build-python-wheel.cs                        # -> ops/artifacts/python/screen9-<Version>-py3-none-any.whl
pysharp install screen9 --find-links ops/artifacts/python   # into ./site-packages (or: pysharp install <.whl file>)
```

## Playing

From the folder where the package was installed (PySharp looks in `./site-packages`):

```powershell
pysharp run samples/python/Gorilla/gorillas.py               # pixel window (Windows)
pysharp run samples/python/Gorilla/gorillas.py --sound       # with the speaker
pysharp run samples/python/Gorilla/gorillas.py --selftest    # scripted match without a window (seed: GORILLAS_SEED)
pysharp run samples/python/Gorilla/gorillas.py --smoke       # scripted match in the window, with checks
```

With the same seed the self-test gives exactly the result of the C# sample (score, frames presented, keys read): verified with seeds 1990, 1 and 42.

**PySharp 2.5.0** or later is required (the `clr` module, a mini-pip with local wheels and `--find-links`, script options in `sys.argv`). Install or update the tool from a local NuGet feed built from the public PySharp repository (see `LocalFeed/README.md`), for example `dotnet tool update --global --add-source <feed folder> PySharp`.
