"""Draw the AGK-4 stencil decal for the Lance pod.

Run: python art/source/lance/lance_stencil.py [output.png]
Original art that covers the stock pod's baked-in "AGR-24" stencil: dark stencil text on a patch of the pod's
body paint, matching the stock stencil's colours (measured from the Missiles4 atlas). Text runs along the
image's width; the decal quad in lance_pod.py lines that up with the old stencil's reading direction.
"""
import os
import sys

from PIL import Image, ImageDraw, ImageFont

TEXT = "AGK-4"
PAINT = (99, 101, 99)   # stock pod body paint around the old stencil
INK = (49, 51, 49)      # stock stencil text
SIZE = (512, 96)
FONT = "C:/Windows/Fonts/bahnschrift.ttf"

image = Image.new("RGB", SIZE, PAINT)
draw = ImageDraw.Draw(image)
font = ImageFont.truetype(FONT, 72)
font.set_variation_by_name("Bold")
left, top, right, bottom = draw.textbbox((0, 0), TEXT, font=font)
draw.text(((SIZE[0] - (right - left)) / 2 - left, (SIZE[1] - (bottom - top)) / 2 - top), TEXT, font=font, fill=INK)
out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                                          "agk4_stencil.png")
image.save(out)
print("wrote", out)
