"""Screen9 for PySharp: the retro PC machine of the Screen9 engine as Python names.

Indexed CGA/EGA/VGA framebuffer (PCCanvas), IBM ROM fonts, text cells and raster text (PCTextDisplay), PLAY music
and PC speaker (PCMusic, PCSpeaker), INKEY$ key codes (PCKeyboard), GET/PUT sprites (PCSprite), the Windows pixel
window (PixelWindow) and the ANSI truecolor renderers. They are the .NET types of Screen9.dll, shipped in this
package and loaded with PySharp's clr module: the same objects used by C# and by QBSharp.

    pysharp install screen9 --find-links <folder with the wheel>

The IBM fonts embedded in Screen9.dll are CC BY-SA 4.0 (see fonts/LICENSE.TXT and fonts/SOURCE.TXT).
"""
import os

import clr

clr.AddReference(os.path.join(os.path.dirname(__file__), "Screen9.dll"))

from Screen9 import AnsiCanvasRenderer, AnsiFrameRenderer, Canvas, Cell, CgaPalette, PCCanvas, PCFont, PCImage
from Screen9 import PCKeyboard, PCMusic, PCSpeaker, PCSprite, PCSpriteOperation, PCTextDisplay, PCTone, PCVideoMode
from Screen9 import PixelWindow
from System import Array, Int32

__version__ = "0.0.0"  # replaced when the wheel is built


def long_array(length):
    """A QBasic LONG array for GET/PUT, as DIM a&(length - 1): a .NET int[] that PCSprite.Capture fills in place."""
    return Array.CreateInstance(clr.GetClrType(Int32), length)
