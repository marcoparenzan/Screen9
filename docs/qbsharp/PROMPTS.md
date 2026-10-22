# Goals

Note: a reconstructed historical log; long requests are summarised. Numbering normalised after the double number 35. The requests were given in Italian and are translated here.

## 1. Initial idea
> A modern QBasic interpreter QBSharp .NET, Spectre console, editor, global tool, classes, JSON/YAML, .bas inclusion, PySharp style, embedding and Ops productivity.
Intent: an embeddable modern language with the QuickBASIC experience.

## 2. Detailed specification and phased development
> An AST interpreter without Roslyn, VB.NET syntax, read PySharp, RalfAI, Spectre, classes/namespaces, templates, Import, standard library, mouse/keyboard editor, embedding, documentation and tests. Proceed one phase at a time.
Intent: structure, AST/API and specification.

## 3. Global registration
> did you register qbs as a dotnet global tool?
Intent: verify the installation.

## 4. Tool installation
> Go ahead then
Intent: build/test/pack/install/help/version.

## 5. Phase 2
> The next step is phase 2: lexer, parser and AST interpreter, with their tests.
Intent: core and embedding.

## 6. Phase 3
> Phase 3
Intent: an operational CLI.

## 7. Phase 3 fix
> ok fix it
Intent: BOM and release.

## 8. Phase 4
> The next step is phase 4: classes, namespaces and dynamic Import.
Intent: classes, generics and modules.

## 9. Phase 5
> go ahead
Intent: filesystem, JSON/YAML, config and UI.

## 10. Status
> Where were we?
Intent: summary.

## 11. Closing phase 5
> go ahead
Intent: suite, documentation and tool.

## 12. Phase 6
> go ahead
Intent: full-screen editor.

## 13. Editor status
> so how are we now?
Intent: ConPTY summary.

## 14. Closing the editor gate
> go ahead
Intent: ANSI/ConPTY and gate.

## 15. Editor release
> great. Go ahead
Intent: tool and smoke test.

## 16. Phase 7
> The next gate is phase 7: RalfAI in scripts and in the editor.
Intent: an AI adapter with consent.

## 17. External credentials
> You can take the Ralf credentials from D:\Configurations\RalfAI\config.json; go ahead with the rest
Intent: opt-in loading and an authorised smoke test.

## 18. Samples and Gorillas
> Move samples next to src and verify gorillas.bas.
Intent: a move without alterations, and compatibility.

## 19. ConsoleLib
> Extend D:\dev\2026\MarcoSoft\ConsoleLib, use it for runtime output, NuGet in D:\dev\LocalNuGetFeed.
Intent: ConsoleLib integration.

## 20. Opening the editor
> ok launch in editor gorillas for me thanks
Intent: open without running.

## 21. PCCanvas
> A derived canvas PCCanvas for CGA/EGA/VGA and a QBSharp wrapper.
Intent: framebuffer, palette, primitives and memory.

## 22. Retry tool
> try again
Intent: an update without closing user processes.

## 23. PC test
> run the test
Intent: test of the global tool.

## 24. Visible demo
> can I see it? you launch it (run)
Intent: a persistent demo.

## 25. QBasic foundations
> Suffixes, DEFINT, DECLARE, TYPE, arrays, DIM SHARED, ByRef, classic calls, FOR/DO/SELECT/GOSUB/label/error handler, maths/strings/randomness/DATA and numbered files.
Intent: an opt-in QBasic dialect.

## 26. QBasic suite verification
> Verify arrays, ByRef, loops, GOSUB, error handler, DATA and numbered files.
Intent: regressions and gate.

## 27. Gorillas runtime
> DEF SEG and BEEP missing; controlled memory, input, time/audio, graphics, GET/PUT, fonts and a complete match.
Intent: a virtualised runtime.

## 28. Raster gate
> Recompile and retest raster, VIEW PRINT, CLS and the full suite.
Intent: raster regressions.

## 29. Starting Gorillas
> go ahead and launch
Intent: a separate console.

## 30. Limit in the introduction
> Step limit exceeded during INSTRUCTIONS and a prompt mixed into the screen.
Intent: pacing, budget and restoration.

## 31. Gorillas editor
> can you launch the editor with gorillas?
Intent: opening.

## 32. Editor dialect
> can you connect it?
Intent: F5 QBasic and input/output/Stop.

## 33. Performance
> it is a bit slow or am I wrong?
Intent: benchmark and optimisation.

## 34. Feature balance
> How far are we from QBasic and which extras do we have? I would keep the classes.
Intent: distinguish compatibility from extensions without removals.

## 35. Updated sample
> ok I changed gorilla.bas....removed the others. Can you verify?
Intent: respect updates and removals.

## 36. QB45 download
> Can you download all the games on the site https://www.qb45.org/?
Intent: catalogue and archives without running.

## 37. QB45 extraction
> can you unzip the files and turn the html pages into md?
Intent: safe extraction and Markdown.

## 38. Modern consolidation
> resume
Intent: For/Next, Do/Loop, Exit keeping classes and embedding.

## 39. Closing the gate and modern consolidation
> Close the documentation and release of the current gate, then add the three missing modern constructs. We did not remove anything and the classes stay
Intent: documentation/release and ElseIf, Select Case, For Each.

## 40. Gorillas input fix
> go ahead
Context: QBasic error 62 on the INPUT of the points in samples/gorilla.bas after entering the players.
Intent: read the reader and runtime directly, fix the empty answer with regressions and verify build and the full suite.

## 41. Gorillas framebuffer diagnostics
> go ahead; so?
Intent: export the original PNG to tell graphics errors from console reduction; a separate pixel presenter.

## 42. Windows pixel presenter
> go ahead
Context: imprecise console graphics, a clipped canvas and a slow game.
Intent: a native Windows presenter with the full framebuffer, aspect ratio, input and limited refresh; less frequent diagnostic PNGs.

## 43. Limit during a Gorillas throw
> ralf . — gorilla.bas(2087,14): QBS2401 step limit exceeded; I was able to enter angle and velocity.
Intent: diagnose the instruction and the budget during the throw, add a regression and fix without hiding infinite loops.

## 44. Slow banana
> The banana is VERY SLOW!
Intent: measure throw/calibration/waits, optimise keeping the original source and verify regressions.

## 45. Double buffering and slowdowns
> we have to implement double buffering in the meantime. Then there are still slowdowns anyway
Intent: a GDI backbuffer for atomic presentation, reduce idle captures and verify performance without modifying game/physics.

## 46. Pixel renderer and IBM fonts
> Right. pixel renderer and IBM fonts
Intent: real IBM CP437 glyphs in the raster text of graphics modes without redistributing fonts, the compact fallback preserved.

## 47. Fonts as a device theme
> Yes but conceptually would these fonts be a theme of the display device or of QBasic? … Can you access ConsoleLib's memory? … Go ahead.
Intent: move fonts and glyph drawing into ConsoleLib 2.3.0, leaving the semantics of PRINT to QBasic.

## 48. Exact IBM fonts
> No4 fonts
Context: the open point of the original 8x14/8x16 fonts only with .rom dumps provided by the user.
Intent: obtain the EGA/VGA ROM glyphs from a source with a clear licence, install them per user without including them in the repository.

## 49. Fonts in the repo and then in ConsoleLib
> put the fonts in the repo....don't put me in who knows what folders. Bring the licence file and that's it
> maybe these fonts should go into ConsoleLib? … go ahead
Intent: IBM fonts included with licence, finally in the ConsoleLib 2.4.0 device.

## 50. A single window
> It opens two windows...is it not possible to open a single window?
Intent: text, graphics and INPUT in the pixel window only, without a visible console.

## 51. Background music
> Audio. The scheduler for background music is missing (PLAY "MB..."); sounds stay sequential.
Intent: a QBasic music buffer with non-blocking PLAY MB, PLAY(n), end and interruption correct.

## 52. Global tool alpha.4
> 7
Context: the open point "global tool stuck at alpha.3".
Intent: pack, global installation and smoke test of 0.7.0-alpha.4, with the font licence in the package.
