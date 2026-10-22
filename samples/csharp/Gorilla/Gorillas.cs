#!/usr/bin/env dotnet
#:project ../../../src/Screen9/Screen9.csproj
#:project GorillasCore.csproj
#:property TargetFramework=net10.0
#:property Nullable=enable
#:property ImplicitUsings=enable

// Porting C# di "QBasic Gorillas" (GORILLA.BAS, Copyright (C) Microsoft Corporation 1990) come demo di Screen9.
// Opera derivata a solo scopo dimostrativo: non fa parte del pacchetto NuGet. Solo il ramo EGA (SCREEN 9).
// Grafica, testo, sprite, tastiera e audio passano dai dispositivi del motore Screen9, gli stessi usati da QBSharp.
// Il gioco (GorillasHost, GorillasGame) è in GorillasCore.csproj: questo file è solo l'avvio desktop a
// finestra a pixel, così lo stesso gioco si riusa anche in Screen9.Web senza duplicarlo.
//
// App file-based .NET 10 (un solo file, motore e logica del gioco con #:project):
//   dotnet run samples/csharp/Gorilla/Gorillas.cs                  gioca in una finestra a pixel (Windows)
//   dotnet run samples/csharp/Gorilla/Gorillas.cs -- --sound       con l'altoparlante (Console.Beep)
//   dotnet run samples/csharp/Gorilla/Gorillas.cs -- --selftest    partita da script senza finestra (seme: GORILLAS_SEED)
//   dotnet run samples/csharp/Gorilla/Gorillas.cs -- --smoke       partita da script nella finestra, con verifiche
using Gorilla;
using Screen9;

return GorillasDemo.Run(args);

/// <summary>Avvio: finestra a pixel (anche in smoke, con input da script) o selftest senza dispositivi.</summary>
static class GorillasDemo
{
    // Partita da script: introduzione, nomi predefiniti, 2 punti, gravità predefinita, P = gioca; poi sempre angolo 45 e velocità 1.
    // Il giocatore 1 a velocità 1 colpisce sempre se stesso; il 2 dipende dal vento (il controllo parte oltre il suo corpo),
    // quindi il numero di tiri varia con la mappa ma la partita finisce comunque: si verifica solo che i punti totali siano 2.
    private static IEnumerator<char> Script()
    {
        foreach (char c in " \r\r2\r\rp") yield return c;
        for (int shot = 0; shot < 60; shot++) foreach (char c in "45\r1\r") yield return c; // ogni tasto chiude anche la schermata finale
    }

    private static string ScriptKey(IEnumerator<char> keys)
        => keys.MoveNext() ? keys.Current.ToString() : throw new InvalidOperationException("script di input esaurito: la partita non è finita come previsto");

    public static int Run(string[] args)
    {
        if (args.Contains("--selftest")) return SelfTest();
        if (!OperatingSystem.IsWindows()) { Console.Error.WriteLine("La finestra a pixel richiede Windows (GDI)."); return 1; }
        bool smoke = args.Contains("--smoke"), sound = args.Contains("--sound") && !smoke;
        var keys = smoke ? Script() : null;
        using var window = new PixelWindow("Gorillas — Screen9 (close to stop)");
        string? failure = null;
        void Sleep(TimeSpan delay)
        {
            // A fette, aggiornando la finestra: l'ultimo frame compare anche se la cattura era limitata a 30 Hz.
            long end = Environment.TickCount64 + (long)(smoke ? delay / 4 : delay).TotalMilliseconds;
            do { window.Refresh(); Thread.Sleep((int)Math.Clamp(end - Environment.TickCount64, 0, 10)); } while (Environment.TickCount64 < end);
            window.Refresh();
        }
        var host = new GorillasHost
        {
            Present = (graphics, cells) => { if (graphics is not null) window.Present(graphics); else window.Present(cells); },
            ReadKey = () => keys is not null ? ScriptKey(keys) : window.ReadKey(),
            FlushKeys = () => { if (keys is null) while (window.ReadKey() != "") { } },
            Sleep = Sleep,
            Tone = tone => { if (sound) { window.Refresh(); PCSpeaker.Beep(tone, default).AsTask().GetAwaiter().GetResult(); } else Sleep(tone.Duration); },
            Background = tones => { if (sound) _ = Task.Run(async () => { foreach (var tone in tones) await PCSpeaker.Beep(tone, default); }); },
            Checkpoint = (name, game) =>
            {
                if (!smoke || name != "gameover") return;
                bool found = Enumerable.Range(0, 25).Any(row => new string(Enumerable.Range(0, 80).Select(column => game.TextCells[column, row].Character).ToArray()).Contains("GAME OVER!"));
                if (!found) failure = "testo 'GAME OVER!' assente dalle celle presentate";
                else if (window.CaptureCount == 0) failure = "la finestra non ha catturato frame";
            }
        };
        try
        {
            var game = new GorillasGame(host); game.Run();
            if (!smoke) return 0;
            Sleep(TimeSpan.FromMilliseconds(400));
            if (failure is null && window.CompositionCount == 0) failure = "la finestra non ha composto frame (WM_PAINT)";
            if (failure is null && game.FinalScore.Player1 + game.FinalScore.Player2 != 2) failure = $"punteggio {game.FinalScore}, attesi 2 punti in totale";
            if (failure is not null) { Console.Error.WriteLine("Smoke Gorillas fallito: " + failure); return 2; }
            Console.WriteLine($"Smoke Gorillas: punteggio {game.FinalScore}, {window.CaptureCount} frame catturati, {window.CompositionCount} composizioni.");
            return 0;
        }
        catch (OperationCanceledException) { return 0; } // finestra chiusa dall'utente
        catch (InvalidOperationException e) when (keys is not null) { Console.Error.WriteLine("Smoke Gorillas: " + e.Message); return 5; }
    }

    /// <summary>Partita intera senza finestra né attese, seme fisso: verifica logica, testo raster e sprite.</summary>
    private static int SelfTest()
    {
        var keys = Script(); int pressed = 0, frames = 0;
        int seed = int.TryParse(Environment.GetEnvironmentVariable("GORILLAS_SEED"), out int fixedSeed) ? fixedSeed : 1990;
        var host = new GorillasHost
        {
            Present = (_, _) => frames++, ReadKey = () => { pressed++; return ScriptKey(keys); }, FlushKeys = () => { },
            Sleep = _ => { }, Tone = _ => { }, Seed = () => seed++
        };
        try
        {
            var game = new GorillasGame(host); game.Run();
            Console.WriteLine($"Selftest Gorillas: punteggio finale {game.FinalScore}, {frames} frame, {pressed} tasti.");
            return game.FinalScore.Player1 + game.FinalScore.Player2 == 2 ? 0 : 2;
        }
        catch (InvalidOperationException e) { Console.Error.WriteLine("Selftest Gorillas: " + e.Message); return 5; }
    }
}
