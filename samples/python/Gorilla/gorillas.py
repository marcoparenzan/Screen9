# Porting Python di "QBasic Gorillas" (GORILLA.BAS, Copyright (C) Microsoft Corporation 1990) come demo di Screen9.
# Opera derivata a solo scopo dimostrativo: non fa parte del pacchetto. Solo il ramo EGA (SCREEN 9).
# Stessa struttura di samples/csharp/Gorilla/Gorillas.cs, sugli stessi dispositivi del motore, dal pacchetto screen9
# di PySharp (PCCanvas, PCTextDisplay, PCMusic, PCSpeaker, PCSprite, PixelWindow):
#   pysharp install screen9 --find-links ops/artifacts/python     (wheel creato da: dotnet run ops/build-python-wheel.cs)
#   pysharp run samples/python/Gorilla/gorillas.py                 gioca in una finestra a pixel (Windows)
#   pysharp run samples/python/Gorilla/gorillas.py --sound         con l'altoparlante
#   pysharp run samples/python/Gorilla/gorillas.py --selftest      partita da script senza finestra (seme: GORILLAS_SEED)
#   pysharp run samples/python/Gorilla/gorillas.py --smoke         partita da script nella finestra, con verifiche
# Incorporato in un sito (Screen9.Web, pagina "Gorillas (Python)"): se esiste la variabile `web_host`, gioca su quello.
import math
import os
import sys
import time

from screen9 import PCCanvas, PCMusic, PCSpeaker, PCSprite, PCTextDisplay, PCTone, PixelWindow, long_array
from System import Random, TimeSpan  # System.Random: stessa sequenza del sample C# a parità di seme

BACKATTR, OBJECTCOLOR, WINDOWCOLOR, SUNATTR, HITSELF = 0, 1, 14, 3, 1
SUNHAPPY, SUNSHOCK = False, True
RIGHTUP, LEFTUP, ARMSDOWN = 1, 2, 3
SCR_WIDTH, SCR_HEIGHT, G_HEIGHT, SUN_HT, EXPLOSION_COLOR, BACK_COLOR = 640, 350, 25, 39, 2, 1
DEFAULT_ASPECT = 4.0 / 3 * SCR_HEIGHT / SCR_WIDTH  # aspetto predefinito di CIRCLE in SCREEN 9


def cint(value):
    """CINT e assegnazioni a INTEGER: arrotondamento al pari, come Math.Round in C#."""
    return int(round(value))


def scl(n):
    return cint(n)  # ramo EGA di Scl


def val(value):
    end = 0
    while end < len(value) and (value[end].isdigit() or (value[end] == '.' and value.find('.') == end)):
        end += 1
    try:
        return float(value[:end])
    except ValueError:
        return 0.0


class GorillasGame:
    """Il gioco. Nomi, struttura e costanti seguono GORILLA.BAS; le istruzioni QBasic sono metodi con lo stesso nome
    e la stessa semantica del runtime QBSharp (SCREEN, COLOR, CLS, LOCATE, PRINT, VIEW PRINT, PLAY, GET/PUT)."""

    def __init__(self, host):
        self.host = host
        self.graphics = None
        self.text = PCTextDisplay()
        self.text.UseIbmFonts = True
        self.music = PCMusic()
        self.foreground, self.background, self.max_col = 7, 0, 80
        self.random = Random(host.seed())
        self.gorilla_x, self.gorilla_y = [0] * 3, [0] * 3
        self.last_building, self.wind, self.gravity, self.sun_hit = 0, 0, 0.0, False
        # Array LONG di GET/PUT come in GORILLA.BAS: GorD&(120) ecc. (int[] .NET, GET li riempie); banane dai DATA EGABanana.
        self.gor_d, self.gor_l, self.gor_r = long_array(121), long_array(121), long_array(121)
        self.l_ban = [458758, 202116096, 471604224, 943208448, 943208448, 943208448, 471604224, 202116096, 0]
        self.d_ban = [262153, -2134835200, -2134802239, -2130771968, -2130738945, 8323072, 8323199, 4063232, 4063294]
        self.u_ban = [262153, 4063232, 4063294, 8323072, 8323199, -2130771968, -2130738945, -2134835200, -2134802239]
        self.r_ban = [458758, -1061109760, -522133504, 1886416896, 1886416896, 1886416896, -522133504, -1061109760, 0]
        self.final_score = (0, 0)

    def run(self):
        self.intro()
        player1, player2, num_games = self.get_inputs()
        self.gorilla_intro(player1, player2)
        self.play_game(player1, player2, num_games)

    # ---- Istruzioni QBasic -------------------------------------------------------------------------------------------
    @property
    def g(self):
        if self.graphics is None:
            raise RuntimeError("Graphics operation requires SCREEN graphics mode.")
        return self.graphics

    def screen(self, mode):
        self.graphics = None if mode == 0 else PCCanvas(mode)
        self.text.Resize(80, 25)
        self.foreground = 7 if self.graphics is None else min(15, self.graphics.ColorCount - 1)
        self.background = 0

    def color(self, fg, bg=None):
        self.foreground = fg
        if bg is not None:
            self.background = bg

    def cls(self, mode=0):
        if mode != 2 and self.graphics is not None:
            self.graphics.ClearIndex(self.background)
        if mode != 1:
            self.text.Clear(self.foreground, self.background, self.graphics, mode == 2)

    def locate(self, row, column):
        self.text.Locate(row, column)

    def print_(self, value, new_line=False):
        self.text.Write(value + "\n" if new_line else value, self.foreground, self.background, self.graphics)

    def view_print(self, top, bottom):
        self.text.View(top, bottom)

    def palette(self, index, color):
        self.g.SetPalette(index, color)

    def put(self, sprite, x, y, operation="PSET"):
        PCSprite.Draw(self.g, sprite, cint(x), cint(y), PCSprite.ParseOperation(operation))

    def get(self, sprite, x1, y1, x2, y2):
        PCSprite.Capture(self.g, sprite, x1, y1, x2, y2)

    def pset(self, x, y, color):
        self.g.PSet(cint(x), cint(y), color)

    def line(self, x1, y1, x2, y2, color):
        self.g.Line(cint(x1), cint(y1), cint(x2), cint(y2), color)

    def line_box(self, x1, y1, x2, y2, color, fill):
        """LINE ...,B / BF: angoli inclusi."""
        self.g.Box(min(x1, x2), min(y1, y2), abs(x2 - x1) + 1, abs(y2 - y1) + 1, color, fill)

    def circle(self, x, y, radius, color, start=0.0, end=math.pi * 2, aspect=DEFAULT_ASPECT):
        """CIRCLE: con aspetto > 1 il raggio è verticale (QBasic); angoli positivi, senza raggi."""
        aspect = abs(aspect)
        self.g.Circle(cint(x), cint(y), radius if aspect <= 1 else radius / aspect, color, start, end, aspect)

    def paint(self, x, y, color, border):
        self.g.Paint(x, y, color, border)

    def play(self, source):
        """PLAY: MB/MF persistono tra le chiamate come in QBasic; la musica di sottofondo non ferma il gioco."""
        tones = list(self.music.Parse(source))
        if len(tones) > 0 and tones[0].Background:
            self.host.background(tones)
            return
        for tone in tones:
            self.host.tone(tone)

    def inkey(self):
        return self.host.read_key()

    def wait_key(self):
        key = self.inkey()
        while key == "":
            self.frame(15)
            key = self.inkey()
        return key

    def present(self):
        self.host.present(self.graphics, self.text.Cells)

    def frame(self, milliseconds):
        self.present()
        self.host.sleep(milliseconds / 1000.0)

    def rest(self, seconds):
        """Rest: in GORILLA.BAS dipende da CalcDelay; qui è il tempo nominale."""
        self.present()
        self.host.sleep(seconds)

    def fn_ran(self, x):
        return int(self.random.NextDouble() * x) + 1

    def center(self, row, value):
        self.locate(row, cint(self.max_col // 2 - (len(value) / 2.0 + .5)))
        self.print_(value)

    # ---- Schermate di testo ------------------------------------------------------------------------------------------
    def intro(self):
        self.screen(0)
        self.max_col = 80
        self.color(15, 0)
        self.cls()
        self.center(4, "Q B a s i c    G O R I L L A S")
        self.color(7)
        self.center(6, "Copyright (C) Microsoft Corporation 1990")
        self.center(8, "Your mission is to hit your opponent with the exploding")
        self.center(9, "banana by varying the angle and power of your throw, taking")
        self.center(10, "into account wind speed, gravity, and the city skyline.")
        self.center(11, "The wind speed is shown by a directional arrow at the bottom")
        self.center(12, "of the playing field, its length relative to its strength.")
        self.center(24, "Press any key to continue")
        self.play("MBT160O1L8CDEDCDL4ECC")
        self.sparkle_pause()

    def sparkle_pause(self):
        self.color(4, 0)
        sparkles = "*    " * 17
        self.host.flush_keys()
        while self.inkey() == "":
            for a in range(1, 6):
                self.locate(1, 1)
                self.print_(sparkles[a - 1:a - 1 + 80])
                self.locate(22, 1)
                self.print_(sparkles[6 - a - 1:6 - a - 1 + 80])
                for b in range(2, 22):
                    mark = "*" if (a + b) % 5 == 1 else " "
                    self.locate(b, 80)
                    self.print_(mark)
                    self.locate(23 - b, 1)
                    self.print_(mark)
                self.frame(40)

    def line_input(self, prompt, max_length=30):
        """LINE INPUT / INPUT con eco e cursore "_"; INPUT aggiunge "? " al prompt."""
        self.print_(prompt)
        row, column = self.text.Row, self.text.Column
        value = ""
        while True:
            self.locate(row, column)
            self.print_(value + "_ ")
            key = self.wait_key()
            if key == "\r":
                break
            if key == "\b":
                value = value[:-1]
            elif len(key) == 1 and key >= " " and len(value) < max_length:
                value += key
        self.locate(row, column)
        self.print_(value + "  ")
        return value

    def get_inputs(self):
        self.color(7, 0)
        self.cls()
        self.locate(8, 15)
        player1 = self.line_input("Name of Player 1 (Default = 'Player 1'): ")
        player1 = "Player 1" if player1 == "" else player1[:10]
        self.locate(10, 15)
        player2 = self.line_input("Name of Player 2 (Default = 'Player 2'): ")
        player2 = "Player 2" if player2 == "" else player2[:10]
        while True:
            self.locate(12, 56)
            self.print_(" " * 25)
            self.locate(12, 13)
            game = self.line_input("Play to how many total points (Default = 3)? ", 5)
            num_games = cint(val(game[:2]))
            if (num_games > 0 and len(game) < 3) or len(game) == 0:
                break
        if num_games == 0:
            num_games = 3
        while True:
            self.locate(14, 53)
            self.print_(" " * 28)
            self.locate(14, 17)
            grav = self.line_input("Gravity in Meters/Sec (Earth = 9.8)? ", 8)
            self.gravity = val(grav)
            if self.gravity > 0 or len(grav) == 0:
                break
        if self.gravity == 0:
            self.gravity = 9.8
        return player1, player2, num_games

    def gorilla_intro(self, player1, player2):
        self.locate(16, 34)
        self.print_("--------------", True)
        self.locate(18, 34)
        self.print_("V = View Intro", True)
        self.locate(19, 34)
        self.print_("P = Play Game", True)
        self.locate(21, 35)
        self.print_("Your Choice?", True)
        choice = self.wait_key()
        x, y = 278, 175
        self.screen(9)
        self.set_screen()
        self.view_print(9, 24)
        self.palette(OBJECTCOLOR, BACK_COLOR)  # gorilla disegnato invisibile, solo per catturarne le sprite
        self.draw_gorilla(x, y, ARMSDOWN)
        self.cls(2)
        self.draw_gorilla(x, y, LEFTUP)
        self.cls(2)
        self.draw_gorilla(x, y, RIGHTUP)
        self.cls(2)
        self.view_print(1, 25)
        self.palette(OBJECTCOLOR, 46)
        if choice.upper() != "V":
            return
        self.center(2, "Q B A S I C   G O R I L L A S")
        self.center(5, "             STARRING:               ")
        self.center(7, player1 + " AND " + player2)
        self.put(self.gor_d, x - 13, y)
        self.put(self.gor_d, x + 47, y)
        self.rest(1)
        self.put(self.gor_l, x - 13, y)
        self.put(self.gor_r, x + 47, y)
        self.play("t120o1l16b9n0baan0bn0bn0baaan0b9n0baan0b")
        self.rest(.3)
        self.put(self.gor_r, x - 13, y)
        self.put(self.gor_l, x + 47, y)
        self.play("o2l16e-9n0e-d-d-n0e-n0e-n0e-d-d-d-n0e-9n0e-d-d-n0e-")
        self.rest(.3)
        self.put(self.gor_l, x - 13, y)
        self.put(self.gor_r, x + 47, y)
        self.play("o2l16g-9n0g-een0g-n0g-n0g-eeen0g-9n0g-een0g-")
        self.rest(.3)
        self.put(self.gor_r, x - 13, y)
        self.put(self.gor_l, x + 47, y)
        self.play("o2l16b9n0baan0g-n0g-n0g-eeen0o1b9n0baan0b")
        self.rest(.3)
        for _ in range(4):
            self.put(self.gor_l, x - 13, y)
            self.put(self.gor_r, x + 47, y)
            self.play("T160O0L32EFGEFDC")
            self.rest(.1)
            self.put(self.gor_r, x - 13, y)
            self.put(self.gor_l, x + 47, y)
            self.play("T160O0L32EFGEFDC")
            self.rest(.1)

    def set_screen(self):
        for index, color in ((0, 1), (1, 46), (2, 44), (3, 54), (5, 7), (6, 4), (7, 3), (9, 63)):
            self.palette(index, color)

    # ---- Disegno -----------------------------------------------------------------------------------------------------
    def draw_gorilla(self, x, y, arms):
        self.line_box(x - scl(4), y, x + scl(2.9), y + scl(6), OBJECTCOLOR, True)  # testa
        self.line_box(x - scl(5), y + scl(2), x + scl(4), y + scl(4), OBJECTCOLOR, True)
        self.line(x - scl(3), y + scl(2), x + scl(2), y + scl(2), 0)  # occhi/sopracciglia
        for i in (-2.0, -1.0):  # naso
            self.pset(x + i, y + 4, 0)
            self.pset(x + i + 3, y + 4, 0)
        self.line(x - scl(3), y + scl(7), x + scl(2), y + scl(7), OBJECTCOLOR)  # collo
        self.line_box(x - scl(8), y + scl(8), x + scl(6.9), y + scl(14), OBJECTCOLOR, True)  # corpo
        self.line_box(x - scl(6), y + scl(15), x + scl(4.9), y + scl(20), OBJECTCOLOR, True)
        for i in (0.0, 1.0, 2.0, 3.0, 4.0):  # gambe
            self.circle(x + scl(i), y + scl(25), scl(10), OBJECTCOLOR, 3 * math.pi / 4, 9 * math.pi / 8)
            self.circle(x + scl(-6) + scl(i - .1), y + scl(25), scl(10), OBJECTCOLOR, 15 * math.pi / 8, math.pi / 4)
        self.circle(x - scl(4.9), y + scl(10), scl(4.9), 0, 3 * math.pi / 2, 0)  # petto
        self.circle(x + scl(4.9), y + scl(10), scl(4.9), 0, math.pi, 3 * math.pi / 2)
        for i in (-5.0, -4.0, -3.0, -2.0, -1.0):  # braccia
            self.circle(x + scl(i - .1), y + scl(4 if arms == LEFTUP else 14), scl(9), OBJECTCOLOR, 3 * math.pi / 4, 5 * math.pi / 4)
            self.circle(x + scl(4.9) + scl(i), y + scl(4 if arms == RIGHTUP else 14), scl(9), OBJECTCOLOR, 7 * math.pi / 4, math.pi / 4)
        sprite = self.gor_r if arms == RIGHTUP else self.gor_l if arms == LEFTUP else self.gor_d
        self.get(sprite, x - scl(15), y - scl(1), x + scl(14), y + scl(28))

    def do_sun(self, mouth):
        x, y = SCR_WIDTH // 2, scl(25)
        self.line_box(x - scl(22), y - scl(18), x + scl(22), y + scl(18), BACKATTR, True)
        self.circle(x, y, scl(12), SUNATTR)
        self.paint(x, y, SUNATTR, SUNATTR)
        self.line(x - scl(20), y, x + scl(20), y, SUNATTR)
        self.line(x, y - scl(15), x, y + scl(15), SUNATTR)
        self.line(x - scl(15), y - scl(10), x + scl(15), y + scl(10), SUNATTR)
        self.line(x - scl(15), y + scl(10), x + scl(15), y - scl(10), SUNATTR)
        self.line(x - scl(8), y - scl(13), x + scl(8), y + scl(13), SUNATTR)
        self.line(x - scl(8), y + scl(13), x + scl(8), y - scl(13), SUNATTR)
        self.line(x - scl(18), y - scl(5), x + scl(18), y + scl(5), SUNATTR)
        self.line(x - scl(18), y + scl(5), x + scl(18), y - scl(5), SUNATTR)
        if mouth:
            self.circle(x, y + scl(5), scl(2.9), 0)
            self.paint(x, y + scl(5), 0, 0)
        else:
            self.circle(x, y, scl(8), 0, 210 * math.pi / 180, 330 * math.pi / 180)
        self.circle(x - 3, y - 2, 1, 0)
        self.circle(x + 3, y - 2, 1, 0)
        self.pset(x - 3, y - 2, 0)
        self.pset(x + 3, y - 2, 0)

    def draw_ban(self, xc, yc, r, draw):
        sprite = (self.l_ban, self.u_ban, self.d_ban)[r] if r < 3 else self.r_ban
        self.put(sprite, xc, yc, "PSET" if draw else "XOR")

    def make_city_scape(self, bcoor):
        x, slope = 2, self.fn_ran(6)
        new_ht = 15 if slope == 1 else 130 if slope == 2 else 15 if slope <= 5 else 130
        bottom_line, ht_inc, def_b_width, random_height = 335, 10, 37, 120
        w_width, w_height, w_dif_v, w_dif_h = 3, 6, 15, 10
        max_height, background = 0, 0  # variabili mai assegnate in GORILLA.BAS: valgono 0
        cur_building = 1
        while True:
            if slope == 1:
                new_ht += ht_inc
            elif slope == 2:
                new_ht -= ht_inc
            elif 3 <= slope <= 5:
                new_ht += -2 * ht_inc if x > SCR_WIDTH // 2 else 2 * ht_inc  # CASE 4 dell'originale è irraggiungibile
            b_width = self.fn_ran(def_b_width) + def_b_width
            if x + b_width > SCR_WIDTH:
                b_width = SCR_WIDTH - x - 2
            b_height = self.fn_ran(random_height) + new_ht
            if b_height < ht_inc:
                b_height = ht_inc
            if bottom_line - b_height <= max_height + G_HEIGHT:
                b_height = max_height + G_HEIGHT - 5
            bcoor[cur_building] = (x, bottom_line - b_height)
            building_color = self.fn_ran(3) + 4
            self.line_box(x - 1, bottom_line + 1, x + b_width + 1, bottom_line - b_height - 1, background, False)
            self.line_box(x, bottom_line, x + b_width, bottom_line - b_height, building_color, True)
            c = x + 3
            while True:
                i = b_height - 3
                while i >= 7:
                    self.line_box(c, bottom_line - i, c + w_width, bottom_line - i + w_height,
                                  8 if self.fn_ran(4) == 1 else WINDOWCOLOR, True)
                    i -= w_dif_v
                c += w_dif_h
                if not c < x + b_width - 3:
                    break
            x += b_width + 2
            cur_building += 1
            if not x <= SCR_WIDTH - ht_inc:
                break
        self.last_building = cur_building - 1

        self.wind = self.fn_ran(10) - 5
        if self.fn_ran(3) == 1:
            self.wind += self.fn_ran(10) if self.wind > 0 else -self.fn_ran(10)
        if self.wind != 0:
            wind_line, arrow_dir = self.wind * 3 * (SCR_WIDTH // 320), -2 if self.wind > 0 else 2
            self.line(SCR_WIDTH // 2, SCR_HEIGHT - 5, SCR_WIDTH // 2 + wind_line, SCR_HEIGHT - 5, EXPLOSION_COLOR)
            self.line(SCR_WIDTH // 2 + wind_line, SCR_HEIGHT - 5, SCR_WIDTH // 2 + wind_line + arrow_dir, SCR_HEIGHT - 5 - 2, EXPLOSION_COLOR)
            self.line(SCR_WIDTH // 2 + wind_line, SCR_HEIGHT - 5, SCR_WIDTH // 2 + wind_line + arrow_dir, SCR_HEIGHT - 5 + 2, EXPLOSION_COLOR)

    def place_gorillas(self, bcoor):
        x_adj, y_adj = 14, 30
        for i in (1, 2):
            b_num = self.fn_ran(2) + 1 if i == 1 else self.last_building - self.fn_ran(2)
            b_width = bcoor[b_num + 1][0] - bcoor[b_num][0]
            self.gorilla_x[i] = cint(bcoor[b_num][0] + b_width / 2.0 - x_adj)
            self.gorilla_y[i] = bcoor[b_num][1] - y_adj
            self.put(self.gor_d, self.gorilla_x[i], self.gorilla_y[i])

    # ---- Gioco -------------------------------------------------------------------------------------------------------
    def play_game(self, player1, player2, num_games):
        bcoor = [(0, 0)] * 31
        total_wins = [0] * 3
        j = 1
        for _ in range(num_games):
            self.cls()
            self.random = Random(self.host.seed())  # RANDOMIZE TIMER
            self.make_city_scape(bcoor)
            self.place_gorillas(bcoor)
            self.do_sun(SUNHAPPY)
            hit = False
            while not hit:
                j = 1 - j
                self.locate(1, 1)
                self.print_(player1, True)
                self.locate(1, self.max_col - 1 - len(player2))
                self.print_(player2, True)
                self.center(23, f"{total_wins[1]}>Score<{total_wins[2]}")
                tosser = j + 1
                hit, tosser = self.do_shot(tosser, self.gorilla_x[tosser], self.gorilla_y[tosser])  # DoShot può cambiare tosser
                if self.sun_hit:
                    self.do_sun(SUNHAPPY)
                if hit:
                    self.update_scores(total_wins, tosser, -1)
            self.rest(1)  # SLEEP 1
        self.final_score = (total_wins[1], total_wins[2])

        self.screen(0)
        self.max_col = 80
        self.color(7, 0)
        self.cls()
        self.center(8, "GAME OVER!")
        self.center(10, "Score:")
        self.locate(11, 30)
        self.print_(player1)
        self.locate(11, 50)
        self.print_(f" {total_wins[1]} ", True)  # PRINT Player1$; TAB(50); TotalWins(1)
        self.locate(12, 30)
        self.print_(player2)
        self.locate(12, 50)
        self.print_(f" {total_wins[2]} ", True)
        self.center(24, "Press any key to continue")
        self.present()
        self.host.checkpoint("gameover", self.text.Cells)
        self.sparkle_pause()
        self.color(7, 0)
        self.cls()
        self.present()

    def do_shot(self, player_num, x, y):
        locate_col = 1 if player_num == 1 else 66
        self.locate(2, locate_col)
        self.print_("Angle:")
        angle = self.get_num(2, locate_col + 7)
        self.locate(3, locate_col)
        self.print_("Velocity:")
        velocity = cint(self.get_num(3, locate_col + 10))
        if player_num == 2:
            angle = 180 - angle
        width = 30 // (80 // self.max_col)
        for i in range(1, 5):
            self.locate(i, 1)
            self.print_(" " * width)
            self.locate(i, 50 // (80 // self.max_col))
            self.print_(" " * width)
        self.sun_hit = False
        player_hit = self.plot_shot(x, y, angle, velocity, player_num)
        if player_hit == 0:
            return False, player_num
        if player_hit == player_num:
            player_num = 3 - player_num
        self.victory_dance(player_num)
        return True, player_num

    def get_num(self, row, col):
        result, done = "", False
        self.host.flush_keys()
        while not done:
            self.locate(row, col)
            self.print_(result + "_    ")
            key = self.inkey()
            if key == "":
                self.frame(15)
                continue
            if len(key) == 1 and key.isdigit():
                result += key
            elif key == ".":
                if "." not in result:
                    result += key
            elif key == "\r":
                if val(result) > 360:
                    result = ""
                else:
                    done = True
            elif key == "\b":
                result = result[:-1]
            else:
                self.host.tone(PCTone(800.0, TimeSpan.FromMilliseconds(200)))  # BEEP
        self.locate(row, col)
        self.print_(result + " ")
        return val(result)

    def plot_shot(self, start_x, start_y, angle, velocity, player_num):
        angle = angle / 180 * math.pi
        init_x_vel, init_y_vel = math.cos(angle) * velocity, math.sin(angle) * velocity
        oldx, oldy = float(start_x), float(start_y)
        self.put(self.gor_l if player_num == 1 else self.gor_r, start_x, start_y)
        self.play("MBo0L32A-L64CL16BL64A+")
        self.rest(.1)
        self.put(self.gor_d, start_x, start_y)
        adjust = scl(4)
        impact, shot_in_sun, on_screen, need_erase = False, False, True, False
        player_hit, start_x_pos, start_y_pos, direction = 0, start_x, start_y - adjust - 3, scl(-4)
        if player_num == 2:
            start_x_pos += scl(25)
            direction = scl(4)
        x, y, t = 0.0, 0.0, 0.0
        pointval, rot, oldrot = 0, 0, 0
        if velocity < 2:  # troppo lento: colpisce se stesso
            x, y, pointval = float(start_x), float(start_y), OBJECTCOLOR
        while not impact and on_screen:
            self.rest(.02)
            if need_erase:
                need_erase = False
                self.draw_ban(oldx, oldy, oldrot, False)
            x = start_x_pos + init_x_vel * t + .5 * (self.wind / 5.0) * t * t
            y = start_y_pos + (-1 * (init_y_vel * t) + .5 * self.gravity * t * t) * (SCR_HEIGHT / 350.0)
            if x >= SCR_WIDTH - scl(10) or x <= 3 or y >= SCR_HEIGHT - 3:
                on_screen = False
            if on_screen and y > 0:
                look_y, look_x = 0, scl(8 * (2 - player_num))
                while True:
                    pointval = self.g.Point(cint(x + look_x), cint(y + look_y))  # -1 fuori schermo, come POINT
                    if pointval == 0:
                        impact = False
                        if shot_in_sun and (abs(SCR_WIDTH // 2 - x) > scl(20) or y > SUN_HT):
                            shot_in_sun = False
                    elif pointval == SUNATTR and y < SUN_HT:
                        if not self.sun_hit:
                            self.do_sun(SUNSHOCK)
                        self.sun_hit = True
                        shot_in_sun = True
                    else:
                        impact = True
                    look_x += direction
                    look_y += scl(6)
                    if impact or look_x != scl(4):
                        break
                if not shot_in_sun and not impact:
                    rot = cint(t * 10) % 4
                    self.draw_ban(x, y, rot, True)
                    need_erase = True
                oldx, oldy, oldrot = x, y, rot
            t += .1
        if pointval != OBJECTCOLOR and impact:
            self.do_explosion(x + adjust, y + adjust)
        elif pointval == OBJECTCOLOR:
            player_hit = self.explode_gorilla(x)
        return player_hit

    def do_explosion(self, x, y):
        self.play("MBO0L32EFGEFDC")
        radius, inc = SCR_HEIGHT // 50, .5
        c = 0.0
        while c <= radius:
            self.circle(x, y, c, EXPLOSION_COLOR)
            self.frame(4)
            c += inc
        c = float(radius)
        while c >= 0:
            self.circle(x, y, c, BACKATTR)
            self.rest(.005)
            c -= inc

    def explode_gorilla(self, x):
        y_adj, x_adj, scl_x, scl_y = scl(12), scl(5), SCR_WIDTH / 320.0, SCR_HEIGHT / 200.0
        player_hit = 1 if x < SCR_WIDTH / 2.0 else 2
        gx, gy = self.gorilla_x[player_hit], self.gorilla_y[player_hit]
        self.play("MBO0L16EFGEFDC")
        i = 1
        while i <= 8 * scl_x:
            self.circle(gx + 3.5 * scl_x + x_adj, gy + 7 * scl_y + y_adj, i, EXPLOSION_COLOR, aspect=-1.57)
            self.line(gx + 7 * scl_x, gy + 9 * scl_y - i, gx, gy + 9 * scl_y - i, EXPLOSION_COLOR)
            self.frame(8)
            i += 1
        i = 1
        while i <= 16 * scl_x:
            if i < 8 * scl_x:
                self.circle(gx + 3.5 * scl_x + x_adj, gy + 7 * scl_y + y_adj, 8 * scl_x + 1 - i, BACKATTR, aspect=-1.57)
            self.circle(gx + 3.5 * scl_x + x_adj, gy + y_adj, i, i % 2 + 1, aspect=-1.57)
            self.frame(8)
            i += 1
        i = cint(24 * scl_x)
        while i >= 1:
            self.circle(gx + 3.5 * scl_x + x_adj, gy + y_adj, i, BACKATTR, aspect=-1.57)
            self.frame(6)
            i -= 1
        return player_hit

    @staticmethod
    def update_scores(record, player_num, results):
        if results == HITSELF:
            record[abs(player_num - 3)] += 1
        else:
            record[player_num] += 1

    def victory_dance(self, player):
        for _ in range(4):
            self.put(self.gor_l, self.gorilla_x[player], self.gorilla_y[player])
            self.play("MFO0L32EFGEFDC")
            self.rest(.2)
            self.put(self.gor_r, self.gorilla_x[player], self.gorilla_y[player])
            self.play("MFO0L32EFGEFDC")
            self.rest(.2)


# ---- Dispositivi e avvio -----------------------------------------------------------------------------------------------
def script_keys():
    """Partita da script, come nel sample C#: introduzione, nomi predefiniti, 2 punti, gravità predefinita, P = gioca;
    poi sempre angolo 45 e velocità 1. Il giocatore 1 a velocità 1 colpisce sempre se stesso; il 2 dipende dal vento,
    quindi il numero di tiri varia con la mappa ma la partita finisce comunque: si verifica che i punti totali siano 2."""
    for key in " \r\r2\r\rp":
        yield key
    for _ in range(60):
        for key in "45\r1\r":
            yield key


def next_key(keys):
    try:
        return next(keys)
    except StopIteration:
        raise RuntimeError("script di input esaurito: la partita non è finita come previsto")


class WindowHost:
    """Finestra a pixel del motore, tasti dalla finestra (o da script), altoparlante del motore."""

    def __init__(self, smoke, sound):
        self.window = PixelWindow("Gorillas (Python) — Screen9 (close to stop)")
        self.smoke, self.keys = smoke, script_keys() if smoke else None
        self.speaker = PCSpeaker(PCSpeaker.Beep) if sound else None
        self.failure = None

    def present(self, graphics, cells):
        self.window.Present(graphics if graphics is not None else cells)

    def read_key(self):
        return next_key(self.keys) if self.keys is not None else self.window.ReadKey()

    def flush_keys(self):
        if self.keys is None:
            while self.window.ReadKey() != "":
                pass

    def sleep(self, seconds):
        # A fette, aggiornando la finestra: l'ultimo frame compare anche se la cattura era limitata a 30 Hz.
        end = time.monotonic() + (seconds / 4 if self.smoke else seconds)
        while True:
            self.window.Refresh()
            left = end - time.monotonic()
            if left <= 0:
                break
            time.sleep(min(left, .01))
        self.window.Refresh()

    def tone(self, tone):
        if self.speaker is None:
            self.sleep(tone.Duration.TotalSeconds)
        else:
            self.window.Refresh()
            self.speaker.Play(tone)

    def background(self, tones):
        if self.speaker is not None:
            for tone in tones:
                self.speaker.Play(tone)  # PLAY "MB": ritorna appena accodato, suona mentre il gioco continua

    def seed(self):
        return int(time.monotonic() * 1000) % 2147483647

    def checkpoint(self, name, cells):
        if not self.smoke or name != "gameover":
            return
        rows = ("".join(cells[column, row].Character for column in range(cells.Width)) for row in range(cells.Height))
        if not any("GAME OVER!" in row for row in rows):
            self.failure = "testo 'GAME OVER!' assente dalle celle presentate"
        elif self.window.CaptureCount == 0:
            self.failure = "la finestra non ha catturato frame"

    def close(self):
        if self.speaker is not None:
            self.speaker.Stop()
        self.window.Dispose()


class ScriptHost:
    """Selftest: nessuna finestra né attesa, tasti da script, seme fisso e crescente."""

    def __init__(self, seed):
        self.keys, self.next_seed, self.frames, self.pressed = script_keys(), seed, 0, 0

    def present(self, graphics, cells):
        self.frames += 1

    def read_key(self):
        self.pressed += 1
        return next_key(self.keys)

    def flush_keys(self):
        pass

    def sleep(self, seconds):
        pass

    def tone(self, tone):
        pass

    def background(self, tones):
        pass

    def seed(self):
        self.next_seed += 1
        return self.next_seed - 1

    def checkpoint(self, name, cells):
        pass


def self_test():
    """Partita intera senza finestra: con lo stesso seme deve dare lo stesso risultato del sample C#."""
    seed = os.environ.get("GORILLAS_SEED", "")
    host = ScriptHost(int(seed) if seed.lstrip("-").isdigit() else 1990)
    started = time.monotonic()
    try:
        game = GorillasGame(host)
        game.run()
    except RuntimeError as error:
        print("Selftest Gorillas (Python):", error)
        return 5
    score = game.final_score
    print(f"Selftest Gorillas (Python): punteggio finale ({score[0]}, {score[1]}), {host.frames} frame, "
          f"{host.pressed} tasti, {time.monotonic() - started:.1f} s.")
    return 0 if score[0] + score[1] == 2 else 2


def main(args):
    if "--selftest" in args:
        return self_test()
    smoke = "--smoke" in args
    host = WindowHost(smoke, "--sound" in args and not smoke)
    try:
        game = GorillasGame(host)
        game.run()
        if not smoke:
            return 0
        host.sleep(.4)
        if host.failure is None and host.window.CompositionCount == 0:
            host.failure = "la finestra non ha composto frame (WM_PAINT)"
        score = game.final_score
        if host.failure is None and score[0] + score[1] != 2:
            host.failure = f"punteggio ({score[0]}, {score[1]}), attesi 2 punti in totale"
        if host.failure is not None:
            print("Smoke Gorillas (Python) fallito:", host.failure)
            return 2
        print(f"Smoke Gorillas (Python): punteggio ({score[0]}, {score[1]}), {host.window.CaptureCount} frame catturati, "
              f"{host.window.CompositionCount} composizioni.")
        return 0
    except KeyboardInterrupt:  # finestra chiusa dall'utente (OperationCanceledException del motore)
        return 0
    except RuntimeError as error:
        if not smoke:
            raise
        print("Smoke Gorillas (Python):", error)
        return 5
    finally:
        host.close()


def run_in_host(host):
    """Il gioco su uno schermo/tastiera/altoparlante forniti da chi incorpora PySharp (il sito web): niente finestra
    nativa, niente argomenti. Chiudere lo schermo (KeyboardInterrupt dal motore) termina la partita."""
    try:
        GorillasGame(host).run()
    except KeyboardInterrupt:
        pass


try:
    web_host  # definito solo se PySharp è incorporato in un sito che passa i dispositivi
except NameError:
    sys.exit(main(sys.argv[1:]))
else:
    run_in_host(web_host)
