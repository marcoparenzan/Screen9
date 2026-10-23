// Logica del gioco Gorillas (GorillasHost, GorillasGame), scorporata da Gorillas.cs perché vada riusata da più host
// (finestra a pixel desktop in Gorillas.cs, pagina Blazor in Screen9.Web): nessuna dipendenza nativa, solo
// i dispositivi del motore Screen9 passati tramite GorillasHost. Copyright (C) Microsoft Corporation 1990
// per la logica originale di GORILLA.BAS; porting a solo scopo dimostrativo, non fa parte del pacchetto NuGet.
using System.Globalization;
using System.Text;
using Screen9;

namespace Gorilla;


/// <summary>Dispositivi del gioco: finestra, script o niente. INKEY$ restituisce "" se non c'è un tasto.</summary>
public sealed class GorillasHost
{
    public required Action<PCCanvas?, Canvas> Present { get; init; }
    public required Func<string> ReadKey { get; init; }
    public required Action FlushKeys { get; init; }
    /// <summary>Blocking wait, for hosts with a thread to spare (desktop window, tests).</summary>
    public Action<TimeSpan> Sleep { get; init; } = Thread.Sleep;
    /// <summary>Non-blocking wait, for hosts without one (a browser): when set, the game awaits it instead of calling Sleep.</summary>
    public Func<TimeSpan, ValueTask>? Delay { get; init; }
    public Action<PCTone> Tone { get; init; } = _ => { };
    /// <summary>Plays one tone and completes when it is over; when set, used instead of Tone.</summary>
    public Func<PCTone, ValueTask>? ToneAsync { get; init; }
    public Action<IReadOnlyList<PCTone>> Background { get; init; } = _ => { };
    public Func<int> Seed { get; init; } = () => Environment.TickCount;
    public Action<string, GorillasGame>? Checkpoint { get; init; }
}

/// <summary>Il gioco. Nomi, struttura e costanti seguono GORILLA.BAS; le istruzioni QBasic sono metodi con lo stesso nome
/// e la stessa semantica del runtime QBSharp (SCREEN, COLOR, CLS, LOCATE, PRINT, VIEW PRINT, PLAY, GET/PUT).</summary>
public sealed class GorillasGame(GorillasHost host)
{
    private const int BACKATTR = 0, OBJECTCOLOR = 1, WINDOWCOLOR = 14, SUNATTR = 3, HITSELF = 1;
    private const bool SUNHAPPY = false, SUNSHOCK = true;
    private const int RIGHTUP = 1, LEFTUP = 2, ARMSDOWN = 3;
    private const int ScrWidth = 640, ScrHeight = 350, GHeight = 25, SunHt = 39, ExplosionColor = 2, BackColor = 1;
    private const double DefaultAspect = 4.0 / 3 * ScrHeight / ScrWidth; // aspetto predefinito di CIRCLE in SCREEN 9

    // Stato dei dispositivi, come QBasicSession in QBSharp.
    private PCCanvas? graphics;
    private readonly PCTextDisplay text = new() { UseIbmFonts = true };
    private readonly PCMusic music = new();
    private int foreground = 7, background, maxCol = 80;
    private PCCanvas G => graphics ?? throw new InvalidOperationException("Graphics operation requires SCREEN graphics mode.");

    private Random random = new(host.Seed());
    private readonly int[] gorillaX = new int[3], gorillaY = new int[3];
    private int lastBuilding, wind;
    private double gravity;
    private bool sunHit;
    // Array LONG di GET/PUT come in GORILLA.BAS: GorD&(120) ecc.; banane dai DATA EGABanana.
    private readonly int[] gorD = new int[121], gorL = new int[121], gorR = new int[121];
    private readonly int[] lBan = [458758, 202116096, 471604224, 943208448, 943208448, 943208448, 471604224, 202116096, 0];
    private readonly int[] dBan = [262153, -2134835200, -2134802239, -2130771968, -2130738945, 8323072, 8323199, 4063232, 4063294];
    private readonly int[] uBan = [262153, 4063232, 4063294, 8323072, 8323199, -2130771968, -2130738945, -2134835200, -2134802239];
    private readonly int[] rBan = [458758, -1061109760, -522133504, 1886416896, 1886416896, 1886416896, -522133504, -1061109760, 0];
    public (int Player1, int Player2) FinalScore { get; private set; }
    public Canvas TextCells => text.Cells;
    public PCCanvas? Graphics => graphics;

    /// <summary>Plays to the end on the calling thread (with a host that has no Delay every wait is synchronous).</summary>
    public void Run() => RunAsync().AsTask().GetAwaiter().GetResult();

    public async ValueTask RunAsync()
    {
        await Intro();
        var (name1, name2, numGames) = await GetInputs();
        await GorillaIntro(name1, name2);
        await PlayGame(name1, name2, numGames);
    }

    // ---- Istruzioni QBasic ---------------------------------------------------------------------------------------------
    private void Screen(int mode)
    {
        graphics = mode == 0 ? null : new PCCanvas(mode); text.Resize(80, 25);
        foreground = graphics is null ? 7 : Math.Min(15, graphics.ColorCount - 1); background = 0;
    }
    private void Color(int fg, int? bg = null) { foreground = fg; background = bg ?? background; }
    private void Cls(int mode = 0)
    {
        if (mode != 2 && graphics is not null) graphics.ClearIndex(background);
        if (mode != 1) text.Clear(foreground, background, graphics, mode == 2);
    }
    private void Locate(int row, int column) => text.Locate(row, column);
    private void Print(string value, bool newLine = false) => text.Write(newLine ? value + "\n" : value, foreground, background, graphics);
    private void ViewPrint(int top, int bottom) => text.View(top, bottom);
    private void Palette(int index, int color) => G.SetPalette(index, color);
    private void Put(int[] sprite, double x, double y, string operation = "PSET") => PCSprite.Draw(G, sprite, CInt(x), CInt(y), PCSprite.ParseOperation(operation));
    private void Get(int[] sprite, int x1, int y1, int x2, int y2) => PCSprite.Capture(G, sprite, x1, y1, x2, y2);
    private void PSet(double x, double y, int color) => G.PSet(CInt(x), CInt(y), color);
    private void Line(double x1, double y1, double x2, double y2, int color) => G.Line(CInt(x1), CInt(y1), CInt(x2), CInt(y2), color);
    /// <summary>LINE ...,B / BF: angoli inclusi.</summary>
    private void LineBox(int x1, int y1, int x2, int y2, int color, bool fill)
        => G.Box(Math.Min(x1, x2), Math.Min(y1, y2), Math.Abs(x2 - x1) + 1, Math.Abs(y2 - y1) + 1, color, fill);
    /// <summary>CIRCLE: con aspetto > 1 il raggio è verticale (QBasic); angoli positivi, senza raggi.</summary>
    private void Circle(double x, double y, double radius, int color, double start = 0, double end = Math.PI * 2, double aspect = DefaultAspect)
    {
        aspect = Math.Abs(aspect);
        G.Circle(CInt(x), CInt(y), aspect <= 1 ? radius : radius / aspect, color, start, end, aspect);
    }
    private void Paint(int x, int y, int color, int border) => G.Paint(x, y, color, border);
    /// <summary>PLAY: MB/MF persistono tra le chiamate come in QBasic; la musica di sottofondo non ferma il gioco.</summary>
    private async ValueTask Play(string source)
    {
        var tones = music.Parse(source);
        if (tones.Count > 0 && tones[0].Background) { host.Background(tones); return; }
        foreach (var tone in tones) await PlayTone(tone);
    }
    private ValueTask PlayTone(PCTone tone)
    {
        if (host.ToneAsync is { } toneAsync) return toneAsync(tone);
        host.Tone(tone); return default;
    }
    private ValueTask Wait(TimeSpan time)
    {
        if (host.Delay is { } delay) return delay(time);
        host.Sleep(time); return default;
    }
    private string Inkey() => host.ReadKey();
    private async ValueTask<string> WaitKey() { string key; while ((key = Inkey()) == "") await Frame(15); return key; }
    private void Present() => host.Present(graphics, text.Cells);
    private ValueTask Frame(int milliseconds) { Present(); return Wait(TimeSpan.FromMilliseconds(milliseconds)); }
    /// <summary>Rest: in GORILLA.BAS dipende da CalcDelay; qui è il tempo nominale.</summary>
    private ValueTask Rest(double seconds) { Present(); return Wait(TimeSpan.FromSeconds(seconds)); }

    private static int CInt(double value) => (int)Math.Round(value, MidpointRounding.ToEven); // CINT e assegnazioni a INTEGER
    private static int Scl(double n) => CInt(n);                                               // ramo EGA di Scl
    private int FnRan(int x) => (int)(random.NextDouble() * x) + 1;
    private void Center(int row, string value) { Locate(row, CInt(maxCol / 2 - (value.Length / 2.0 + .5))); Print(value); }
    private static double Val(string value)
    {
        int end = 0; while (end < value.Length && (char.IsAsciiDigit(value[end]) || value[end] == '.' && value.IndexOf('.') == end)) end++;
        return double.TryParse(value[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out double number) ? number : 0;
    }

    // ---- Schermate di testo --------------------------------------------------------------------------------------------
    private async ValueTask Intro()
    {
        Screen(0); maxCol = 80; Color(15, 0); Cls();
        Center(4, "Q B a s i c    G O R I L L A S");
        Color(7);
        Center(6, "Copyright (C) Microsoft Corporation 1990");
        Center(8, "Your mission is to hit your opponent with the exploding");
        Center(9, "banana by varying the angle and power of your throw, taking");
        Center(10, "into account wind speed, gravity, and the city skyline.");
        Center(11, "The wind speed is shown by a directional arrow at the bottom");
        Center(12, "of the playing field, its length relative to its strength.");
        Center(24, "Press any key to continue");
        await Play("MBT160O1L8CDEDCDL4ECC");
        await SparklePause();
    }

    private async ValueTask SparklePause()
    {
        Color(4, 0);
        const string sparkles = "*    *    *    *    *    *    *    *    *    *    *    *    *    *    *    *    *    ";
        host.FlushKeys();
        while (Inkey() == "")
        {
            for (int a = 1; a <= 5; a++)
            {
                Locate(1, 1); Print(sparkles.Substring(a - 1, 80));
                Locate(22, 1); Print(sparkles.Substring(6 - a - 1, 80));
                for (int b = 2; b <= 21; b++)
                {
                    string mark = (a + b) % 5 == 1 ? "*" : " ";
                    Locate(b, 80); Print(mark); Locate(23 - b, 1); Print(mark);
                }
                await Frame(40);
            }
        }
    }

    /// <summary>LINE INPUT / INPUT con eco e cursore "_"; INPUT aggiunge "? " al prompt.</summary>
    private async ValueTask<string> LineInput(string prompt, int maxLength = 30)
    {
        Print(prompt); int row = text.Row, column = text.Column; var value = new StringBuilder();
        while (true)
        {
            Locate(row, column); Print(value + "_ ");
            string key = await WaitKey();
            if (key == "\r") break;
            if (key == "\b") { if (value.Length > 0) value.Length--; }
            else if (key.Length == 1 && key[0] >= ' ' && value.Length < maxLength) value.Append(key);
        }
        Locate(row, column); Print(value + "  ");
        return value.ToString();
    }

    private async ValueTask<(string Player1, string Player2, int NumGames)> GetInputs()
    {
        string player1, player2; int numGames;
        Color(7, 0); Cls();
        Locate(8, 15); player1 = await LineInput("Name of Player 1 (Default = 'Player 1'): ");
        player1 = player1 == "" ? "Player 1" : player1[..Math.Min(10, player1.Length)];
        Locate(10, 15); player2 = await LineInput("Name of Player 2 (Default = 'Player 2'): ");
        player2 = player2 == "" ? "Player 2" : player2[..Math.Min(10, player2.Length)];
        string game;
        do
        {
            Locate(12, 56); Print(new string(' ', 25));
            Locate(12, 13); game = await LineInput("Play to how many total points (Default = 3)? ", 5);
            numGames = CInt(Val(game[..Math.Min(2, game.Length)]));
        } while (!(numGames > 0 && game.Length < 3 || game.Length == 0));
        if (numGames == 0) numGames = 3;
        string grav;
        do
        {
            Locate(14, 53); Print(new string(' ', 28));
            Locate(14, 17); grav = await LineInput("Gravity in Meters/Sec (Earth = 9.8)? ", 8);
            gravity = Val(grav);
        } while (!(gravity > 0 || grav.Length == 0));
        if (gravity == 0) gravity = 9.8;
        return (player1, player2, numGames);
    }

    private async ValueTask GorillaIntro(string player1, string player2)
    {
        Locate(16, 34); Print("--------------", true);
        Locate(18, 34); Print("V = View Intro", true);
        Locate(19, 34); Print("P = Play Game", true);
        Locate(21, 35); Print("Your Choice?", true);
        string choice = await WaitKey();
        int x = 278, y = 175;
        Screen(9); SetScreen();
        ViewPrint(9, 24);
        Palette(OBJECTCOLOR, BackColor); // gorilla disegnato invisibile, solo per catturarne le sprite
        DrawGorilla(x, y, ARMSDOWN); Cls(2);
        DrawGorilla(x, y, LEFTUP); Cls(2);
        DrawGorilla(x, y, RIGHTUP); Cls(2);
        ViewPrint(1, 25);
        Palette(OBJECTCOLOR, 46);
        if (choice.ToUpperInvariant() != "V") return;
        Center(2, "Q B A S I C   G O R I L L A S");
        Center(5, "             STARRING:               ");
        Center(7, player1 + " AND " + player2);
        Put(gorD, x - 13, y); Put(gorD, x + 47, y); await Rest(1);
        Put(gorL, x - 13, y); Put(gorR, x + 47, y); await Play("t120o1l16b9n0baan0bn0bn0baaan0b9n0baan0b"); await Rest(.3);
        Put(gorR, x - 13, y); Put(gorL, x + 47, y); await Play("o2l16e-9n0e-d-d-n0e-n0e-n0e-d-d-d-n0e-9n0e-d-d-n0e-"); await Rest(.3);
        Put(gorL, x - 13, y); Put(gorR, x + 47, y); await Play("o2l16g-9n0g-een0g-n0g-n0g-eeen0g-9n0g-een0g-"); await Rest(.3);
        Put(gorR, x - 13, y); Put(gorL, x + 47, y); await Play("o2l16b9n0baan0g-n0g-n0g-eeen0o1b9n0baan0b"); await Rest(.3);
        for (int i = 1; i <= 4; i++)
        {
            Put(gorL, x - 13, y); Put(gorR, x + 47, y); await Play("T160O0L32EFGEFDC"); await Rest(.1);
            Put(gorR, x - 13, y); Put(gorL, x + 47, y); await Play("T160O0L32EFGEFDC"); await Rest(.1);
        }
    }

    private void SetScreen()
    {
        Palette(0, 1); Palette(1, 46); Palette(2, 44); Palette(3, 54);
        Palette(5, 7); Palette(6, 4); Palette(7, 3); Palette(9, 63);
    }

    // ---- Disegno -------------------------------------------------------------------------------------------------------
    private void DrawGorilla(int x, int y, int arms)
    {
        LineBox(x - Scl(4), y, x + Scl(2.9), y + Scl(6), OBJECTCOLOR, true);           // testa
        LineBox(x - Scl(5), y + Scl(2), x + Scl(4), y + Scl(4), OBJECTCOLOR, true);
        Line(x - Scl(3), y + Scl(2), x + Scl(2), y + Scl(2), 0);                        // occhi/sopracciglia
        for (float i = -2; i <= -1; i++) { PSet(x + i, y + 4, 0); PSet(x + i + 3, y + 4, 0); } // naso
        Line(x - Scl(3), y + Scl(7), x + Scl(2), y + Scl(7), OBJECTCOLOR);              // collo
        LineBox(x - Scl(8), y + Scl(8), x + Scl(6.9), y + Scl(14), OBJECTCOLOR, true);  // corpo
        LineBox(x - Scl(6), y + Scl(15), x + Scl(4.9), y + Scl(20), OBJECTCOLOR, true);
        for (float i = 0; i <= 4; i++)                                                   // gambe
        {
            Circle(x + Scl(i), y + Scl(25), Scl(10), OBJECTCOLOR, 3 * Math.PI / 4, 9 * Math.PI / 8);
            Circle(x + Scl(-6) + Scl(i - .1), y + Scl(25), Scl(10), OBJECTCOLOR, 15 * Math.PI / 8, Math.PI / 4);
        }
        Circle(x - Scl(4.9), y + Scl(10), Scl(4.9), 0, 3 * Math.PI / 2, 0);              // petto
        Circle(x + Scl(4.9), y + Scl(10), Scl(4.9), 0, Math.PI, 3 * Math.PI / 2);
        for (float i = -5; i <= -1; i++)                                                  // braccia
        {
            Circle(x + Scl(i - .1), y + Scl(arms == LEFTUP ? 4 : 14), Scl(9), OBJECTCOLOR, 3 * Math.PI / 4, 5 * Math.PI / 4);
            Circle(x + Scl(4.9) + Scl(i), y + Scl(arms == RIGHTUP ? 4 : 14), Scl(9), OBJECTCOLOR, 7 * Math.PI / 4, Math.PI / 4);
        }
        Get(arms == RIGHTUP ? gorR : arms == LEFTUP ? gorL : gorD, x - Scl(15), y - Scl(1), x + Scl(14), y + Scl(28));
    }

    private void DoSun(bool mouth)
    {
        int x = ScrWidth / 2, y = Scl(25);
        LineBox(x - Scl(22), y - Scl(18), x + Scl(22), y + Scl(18), BACKATTR, true);
        Circle(x, y, Scl(12), SUNATTR); Paint(x, y, SUNATTR, SUNATTR);
        Line(x - Scl(20), y, x + Scl(20), y, SUNATTR); Line(x, y - Scl(15), x, y + Scl(15), SUNATTR);
        Line(x - Scl(15), y - Scl(10), x + Scl(15), y + Scl(10), SUNATTR); Line(x - Scl(15), y + Scl(10), x + Scl(15), y - Scl(10), SUNATTR);
        Line(x - Scl(8), y - Scl(13), x + Scl(8), y + Scl(13), SUNATTR); Line(x - Scl(8), y + Scl(13), x + Scl(8), y - Scl(13), SUNATTR);
        Line(x - Scl(18), y - Scl(5), x + Scl(18), y + Scl(5), SUNATTR); Line(x - Scl(18), y + Scl(5), x + Scl(18), y - Scl(5), SUNATTR);
        if (mouth) { Circle(x, y + Scl(5), Scl(2.9), 0); Paint(x, y + Scl(5), 0, 0); }
        else Circle(x, y, Scl(8), 0, 210 * Math.PI / 180, 330 * Math.PI / 180);
        Circle(x - 3, y - 2, 1, 0); Circle(x + 3, y - 2, 1, 0); PSet(x - 3, y - 2, 0); PSet(x + 3, y - 2, 0);
    }

    private void DrawBan(double xc, double yc, int r, bool draw)
    {
        var sprite = r switch { 0 => lBan, 1 => uBan, 2 => dBan, _ => rBan };
        Put(sprite, xc, yc, draw ? "PSET" : "XOR");
    }

    private void MakeCityScape((int X, int Y)[] bcoor)
    {
        int x = 2, slope = FnRan(6), newHt = slope switch { 1 => 15, 2 => 130, <= 5 => 15, _ => 130 };
        const int BottomLine = 335, HtInc = 10, DefBWidth = 37, RandomHeight = 120, WWidth = 3, WHeight = 6, WDifV = 15, WDifh = 10;
        const int MaxHeight = 0, Background = 0; // variabili mai assegnate in GORILLA.BAS: valgono 0
        int curBuilding = 1;
        do
        {
            switch (slope)
            {
                case 1: newHt += HtInc; break;
                case 2: newHt -= HtInc; break;
                case >= 3 and <= 5: newHt += x > ScrWidth / 2 ? -2 * HtInc : 2 * HtInc; break; // CASE 4 dell'originale è irraggiungibile
            }
            int bWidth = FnRan(DefBWidth) + DefBWidth;
            if (x + bWidth > ScrWidth) bWidth = ScrWidth - x - 2;
            int bHeight = FnRan(RandomHeight) + newHt;
            if (bHeight < HtInc) bHeight = HtInc;
            if (BottomLine - bHeight <= MaxHeight + GHeight) bHeight = MaxHeight + GHeight - 5;
            bcoor[curBuilding] = (x, BottomLine - bHeight);
            int buildingColor = FnRan(3) + 4;
            LineBox(x - 1, BottomLine + 1, x + bWidth + 1, BottomLine - bHeight - 1, Background, false);
            LineBox(x, BottomLine, x + bWidth, BottomLine - bHeight, buildingColor, true);
            int c = x + 3;
            do
            {
                for (int i = bHeight - 3; i >= 7; i -= WDifV)
                    LineBox(c, BottomLine - i, c + WWidth, BottomLine - i + WHeight, FnRan(4) == 1 ? 8 : WINDOWCOLOR, true);
                c += WDifh;
            } while (c < x + bWidth - 3);
            x += bWidth + 2; curBuilding++;
        } while (x <= ScrWidth - HtInc);
        lastBuilding = curBuilding - 1;

        wind = FnRan(10) - 5;
        if (FnRan(3) == 1) wind += wind > 0 ? FnRan(10) : -FnRan(10);
        if (wind != 0)
        {
            int windLine = wind * 3 * (ScrWidth / 320), arrowDir = wind > 0 ? -2 : 2;
            Line(ScrWidth / 2, ScrHeight - 5, ScrWidth / 2 + windLine, ScrHeight - 5, ExplosionColor);
            Line(ScrWidth / 2 + windLine, ScrHeight - 5, ScrWidth / 2 + windLine + arrowDir, ScrHeight - 5 - 2, ExplosionColor);
            Line(ScrWidth / 2 + windLine, ScrHeight - 5, ScrWidth / 2 + windLine + arrowDir, ScrHeight - 5 + 2, ExplosionColor);
        }
    }

    private void PlaceGorillas((int X, int Y)[] bcoor)
    {
        const int XAdj = 14, YAdj = 30;
        for (int i = 1; i <= 2; i++)
        {
            int bNum = i == 1 ? FnRan(2) + 1 : lastBuilding - FnRan(2);
            int bWidth = bcoor[bNum + 1].X - bcoor[bNum].X;
            gorillaX[i] = CInt(bcoor[bNum].X + bWidth / 2.0 - XAdj);
            gorillaY[i] = bcoor[bNum].Y - YAdj;
            Put(gorD, gorillaX[i], gorillaY[i]);
        }
    }

    // ---- Gioco ---------------------------------------------------------------------------------------------------------
    private async ValueTask PlayGame(string player1, string player2, int numGames)
    {
        var bcoor = new (int X, int Y)[31]; var totalWins = new int[3]; int j = 1;
        for (int round = 1; round <= numGames; round++)
        {
            Cls(); random = new Random(host.Seed()); // RANDOMIZE TIMER
            MakeCityScape(bcoor); PlaceGorillas(bcoor); DoSun(SUNHAPPY);
            bool hit = false;
            while (!hit)
            {
                j = 1 - j;
                Locate(1, 1); Print(player1, true);
                Locate(1, maxCol - 1 - player2.Length); Print(player2, true);
                Center(23, $"{totalWins[1]}>Score<{totalWins[2]}");
                int tosser = j + 1;
                (hit, tosser) = await DoShot(tosser, gorillaX[tosser], gorillaY[tosser]); // QBasic passa per riferimento: DoShot può cambiare tosser
                if (sunHit) DoSun(SUNHAPPY);
                if (hit) UpdateScores(totalWins, tosser, -1);
            }
            await Rest(1); // SLEEP 1
        }
        FinalScore = (totalWins[1], totalWins[2]);

        Screen(0); maxCol = 80; Color(7, 0); Cls();
        Center(8, "GAME OVER!");
        Center(10, "Score:");
        Locate(11, 30); Print(player1); Locate(11, 50); Print($" {totalWins[1]} ", true); // PRINT Player1$; TAB(50); TotalWins(1)
        Locate(12, 30); Print(player2); Locate(12, 50); Print($" {totalWins[2]} ", true);
        Center(24, "Press any key to continue");
        Present(); host.Checkpoint?.Invoke("gameover", this);
        await SparklePause();
        Color(7, 0); Cls(); Present();
    }

    private async ValueTask<(bool Hit, int PlayerNum)> DoShot(int playerNum, int x, int y)
    {
        int locateCol = playerNum == 1 ? 1 : 66;
        Locate(2, locateCol); Print("Angle:");
        double angle = await GetNum(2, locateCol + 7);
        Locate(3, locateCol); Print("Velocity:");
        int velocity = CInt(await GetNum(3, locateCol + 10));
        if (playerNum == 2) angle = 180 - angle;
        for (int i = 1; i <= 4; i++) { Locate(i, 1); Print(new string(' ', 30 / (80 / maxCol))); Locate(i, 50 / (80 / maxCol)); Print(new string(' ', 30 / (80 / maxCol))); }
        sunHit = false;
        int playerHit = await PlotShot(x, y, angle, velocity, playerNum);
        if (playerHit == 0) return (false, playerNum);
        if (playerHit == playerNum) playerNum = 3 - playerNum;
        await VictoryDance(playerNum);
        return (true, playerNum);
    }

    private async ValueTask<double> GetNum(int row, int col)
    {
        string result = ""; bool done = false;
        host.FlushKeys();
        while (!done)
        {
            Locate(row, col); Print(result + "_    ");
            string key = Inkey();
            if (key == "") { await Frame(15); continue; }
            if (key.Length == 1 && char.IsAsciiDigit(key[0])) result += key;
            else if (key == ".") { if (!result.Contains('.')) result += key; }
            else if (key == "\r") { if (Val(result) > 360) result = ""; else done = true; }
            else if (key == "\b") { if (result.Length > 0) result = result[..^1]; }
            else await PlayTone(new PCTone(800, TimeSpan.FromMilliseconds(200))); // BEEP
        }
        Locate(row, col); Print(result + " ");
        return Val(result);
    }

    private async ValueTask<int> PlotShot(int startX, int startY, double angle, int velocity, int playerNum)
    {
        angle = angle / 180 * Math.PI;
        double initXVel = Math.Cos(angle) * velocity, initYVel = Math.Sin(angle) * velocity;
        double oldx = startX, oldy = startY;
        Put(playerNum == 1 ? gorL : gorR, startX, startY);
        await Play("MBo0L32A-L64CL16BL64A+"); await Rest(.1);
        Put(gorD, startX, startY);
        int adjust = Scl(4);
        bool impact = false, shotInSun = false, onScreen = true, needErase = false;
        int playerHit = 0, startXPos = startX, startYPos = startY - adjust - 3, direction = Scl(-4);
        if (playerNum == 2) { startXPos += Scl(25); direction = Scl(4); }
        double x = 0, y = 0, t = 0; int pointval = 0, rot = 0, oldrot = 0;
        if (velocity < 2) { x = startX; y = startY; pointval = OBJECTCOLOR; } // troppo lento: colpisce se stesso
        while (!impact && onScreen)
        {
            await Rest(.02);
            if (needErase) { needErase = false; DrawBan(oldx, oldy, oldrot, false); }
            x = startXPos + initXVel * t + .5 * (wind / 5.0) * t * t;
            y = startYPos + (-1 * (initYVel * t) + .5 * gravity * t * t) * (ScrHeight / 350.0);
            if (x >= ScrWidth - Scl(10) || x <= 3 || y >= ScrHeight - 3) onScreen = false;
            if (onScreen && y > 0)
            {
                int lookY = 0, lookX = Scl(8 * (2 - playerNum));
                do
                {
                    pointval = G.Point(CInt(x + lookX), CInt(y + lookY)); // -1 fuori schermo, come POINT
                    if (pointval == 0)
                    {
                        impact = false;
                        if (shotInSun && (Math.Abs(ScrWidth / 2 - x) > Scl(20) || y > SunHt)) shotInSun = false;
                    }
                    else if (pointval == SUNATTR && y < SunHt)
                    {
                        if (!sunHit) DoSun(SUNSHOCK);
                        sunHit = true; shotInSun = true;
                    }
                    else impact = true;
                    lookX += direction; lookY += Scl(6);
                } while (!(impact || lookX != Scl(4)));
                if (!shotInSun && !impact) { rot = CInt(t * 10) % 4; DrawBan(x, y, rot, true); needErase = true; }
                oldx = x; oldy = y; oldrot = rot;
            }
            t += .1;
        }
        if (pointval != OBJECTCOLOR && impact) await DoExplosion(x + adjust, y + adjust);
        else if (pointval == OBJECTCOLOR) playerHit = await ExplodeGorilla(x);
        return playerHit;
    }

    private async ValueTask DoExplosion(double x, double y)
    {
        await Play("MBO0L32EFGEFDC");
        int radius = ScrHeight / 50; const double Inc = .5;
        for (double c = 0; c <= radius; c += Inc) { Circle(x, y, c, ExplosionColor); await Frame(4); }
        for (double c = radius; c >= 0; c -= Inc) { Circle(x, y, c, BACKATTR); await Rest(.005); }
    }

    private async ValueTask<int> ExplodeGorilla(double x)
    {
        int yAdj = Scl(12), xAdj = Scl(5); const double SclX = ScrWidth / 320.0, SclY = ScrHeight / 200.0;
        int playerHit = x < ScrWidth / 2.0 ? 1 : 2;
        int gx = gorillaX[playerHit], gy = gorillaY[playerHit];
        await Play("MBO0L16EFGEFDC");
        for (int i = 1; i <= 8 * SclX; i++)
        {
            Circle(gx + 3.5 * SclX + xAdj, gy + 7 * SclY + yAdj, i, ExplosionColor, aspect: -1.57);
            Line(gx + 7 * SclX, gy + 9 * SclY - i, gx, gy + 9 * SclY - i, ExplosionColor); await Frame(8);
        }
        for (int i = 1; i <= 16 * SclX; i++)
        {
            if (i < 8 * SclX) Circle(gx + 3.5 * SclX + xAdj, gy + 7 * SclY + yAdj, 8 * SclX + 1 - i, BACKATTR, aspect: -1.57);
            Circle(gx + 3.5 * SclX + xAdj, gy + yAdj, i, i % 2 + 1, aspect: -1.57); await Frame(8);
        }
        for (int i = CInt(24 * SclX); i >= 1; i--) { Circle(gx + 3.5 * SclX + xAdj, gy + yAdj, i, BACKATTR, aspect: -1.57); await Frame(6); }
        return playerHit;
    }

    private static void UpdateScores(int[] record, int playerNum, int results)
    {
        if (results == HITSELF) record[Math.Abs(playerNum - 3)]++; else record[playerNum]++;
    }

    private async ValueTask VictoryDance(int player)
    {
        for (int i = 1; i <= 4; i++)
        {
            Put(gorL, gorillaX[player], gorillaY[player]); await Play("MFO0L32EFGEFDC"); await Rest(.2);
            Put(gorR, gorillaX[player], gorillaY[player]); await Play("MFO0L32EFGEFDC"); await Rest(.2);
        }
    }
}
