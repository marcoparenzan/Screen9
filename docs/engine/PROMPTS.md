# Log of goals

The requests below were given in Italian and are translated here.

## 1. Windows Console as an XY canvas

**Request:** "I would like to create a .NET library that lets me manage the Windows Console (for now) as an XY canvas"

**Intent:** Build a .NET 10 library with a cell canvas, XY drawing, colours, rendering on the Windows Console, a demo and xUnit tests.

## 2. Starting the demo

**Request:** "You launch it"

**Intent:** Start the demo with dotnet and check whether the execution environment has an interactive Console.

## 3. QBSharp runtime backend and local package

Request: extend ConsoleLib for QBSharp execution output, keeping Spectre for the CLI, and create the package in D:\dev\LocalNuGetFeed.

Intent: add a TextConsole/text renderer for pipelines, cell ellipses/sprite regions, tests and NuGet 1.1.0; no DOS QBasic or pixel CGA/EGA emulation.

## 4. PCCanvas CGA/EGA/VGA for QBSharp
> From the ConsoleLib project, a Canvas-derived class called PCCanvas that emulates CGA EGA VGA behaviour, with a wrapper in QBSharp.

Intent: an indexed framebuffer, modes/palettes/pages/memory layouts/primitives/sprites/terminal projection, tests and NuGet 1.2.0; not full hardware emulation or legacy syntax.

## IBM fonts from the device (QBSharp session)
> Yes, but conceptually would these fonts be a theme of the display device or of QBasic? … Go ahead.

Intent: move CP437 fonts, the per-mode text grid and glyph drawing/scrolling into ConsoleLib; PRINT/cursor/control codes stay in QBSharp. Package 2.3.0.

## Fonts included in the device (QBSharp session)
> maybe these fonts should go into ConsoleLib? … go ahead

Intent: move the IBM EGA/VGA ROMs, with the CC BY-SA licence and attribution, from QBSharp to ConsoleLib 2.4.0, and expose the font per mode.
