#!/usr/bin/env python3
"""Cut static Regular and Bold TTFs out of the variable Kode Mono in assets/fonts.

LaunchHeim's UI runs on Qt 5, which reads neither woff2 nor the weights of a variable font, so it
loads these two files instead (src/Desktop/qml/fonts, loaded in Main.qml). The site and the app then
use the same font from one source. The variable font was copied from GroceryTracker in MyMonoRepo. Needs fontTools and brotli (for woff2).

  Scripts/kodemono_static.py
"""
from pathlib import Path

from fontTools.ttLib import TTFont
from fontTools.varLib.instancer import instantiateVariableFont

REPO = Path(__file__).resolve().parent.parent
SOURCE = REPO / "assets/fonts/KodeMono.woff2"
TARGET = REPO / "src/Desktop/qml/fonts"

for style, weight in (("Regular", 400), ("Bold", 700)):
  # updateFontNames renames the instance ("Kode Mono" / "Bold") so Qt matches it by weight.
  font = instantiateVariableFont(TTFont(SOURCE), {"wght": weight}, updateFontNames=True)
  font.flavor = None
  font.save(TARGET / f"KodeMono-{style}.ttf")
  print(f"Wrote {TARGET / f'KodeMono-{style}.ttf'}")
