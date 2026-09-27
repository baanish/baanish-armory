"""Draw the AGK-4 Lance loadout icon from its real shape, in the stock weapon icons' style.

Run: python art/source/lance/lance_icon.py [output.png]
Light line art on black, like the stock AGR-24 Kingpin icon (weaponIcon_rocket2): the side outline, nose left,
with panel lines at the band edges, casing joints and penetrator tip, and the laser window. The game tints the
light lines green on its dark panel. Same 1774 x 887 canvas as the Eyeball-XL icon, so the same import settings apply.
"""
import math
import os
import sys

from PIL import Image, ImageDraw

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lance_shape import (BOATTAIL, FIN_ROOT, FIN_SPAN, FIN_TIP, NOZZLE, NOZZLE_BELL, RADIUS, TIP_LENGTH,  # noqa: E402
                         TIP_Y, WINDOW_WIDTH, body_layout, ogive)

WIDTH, HEIGHT = 1774, 887
MARGIN = 70
THICKEN = 2.0          # icons exaggerate diameter so a 43:1 rocket still reads at loadout size
LINE = 7               # line width at full size: the stock icons' 2 px at 512
INK = (214, 219, 214)  # the stock icons' line colour
SUPERSAMPLE = 2

scale = (WIDTH - 2 * MARGIN) / (TIP_Y + NOZZLE)
s = SUPERSAMPLE
width = LINE * s


def x_at(y):
    """Canvas x for a distance along the rocket: tip at the left margin."""
    return (MARGIN + (TIP_Y - y) * scale) * s


def half_height(radius):
    return radius * scale * THICKEN * s


mid = HEIGHT / 2 * s
layout, nose_start, windows = body_layout()
nose_length = TIP_Y - nose_start
profile = NOZZLE_BELL + BOATTAIL[1:] + [(RADIUS, nose_start)]
profile += [(ogive(TIP_Y - nose_start - i * nose_length / 40, nose_length), nose_start + i * nose_length / 40)
            for i in range(1, 41)]

image = Image.new("RGB", (WIDTH * s, HEIGHT * s), "black")
draw = ImageDraw.Draw(image)

# Two of the four 45-degree fins show in side view, one above and one below, at their projected span.
# Drawn first so the body outline covers their roots.
projected = FIN_SPAN * math.cos(math.pi / 4) * THICKEN
for sign in (-1, 1):
    root = mid + sign * half_height(RADIUS)
    edge = mid + sign * (half_height(RADIUS) + projected * scale * s)
    draw.polygon([(x_at(FIN_ROOT[0]), root), (x_at(FIN_ROOT[1]), root),
                  (x_at(FIN_TIP[1]), edge), (x_at(FIN_TIP[0]), edge)], fill="black", outline=INK, width=width)

top = [(x_at(y), mid - half_height(r)) for r, y in profile]
bottom = [(x_at(y), mid + half_height(r)) for r, y in reversed(profile)]
draw.polygon(top + bottom, fill="black", outline=INK, width=width)


def panel_line(y, radius=RADIUS):
    h = half_height(radius)
    draw.line([(x_at(y), mid - h), (x_at(y), mid + h)], fill=INK, width=width)


panel_line(0.0, BOATTAIL[0][0])            # nozzle bell into the boat-tail
panel_line(BOATTAIL[-1][1])                # boat-tail into the body
for i, (strip, back, front, _) in enumerate(layout):
    if strip.startswith("band"):
        panel_line(back)
        panel_line(front)
    elif i + 1 < len(layout) and strip.startswith("silver") and layout[i + 1][0].startswith("silver"):
        panel_line(front)                  # motor casing joints
panel_line(TIP_Y - TIP_LENGTH, ogive(TIP_LENGTH, nose_length))

window_half = max(half_height(WINDOW_WIDTH / 2), 7 * s)
draw.rounded_rectangle([x_at(windows[1]), mid - window_half, x_at(windows[0]), mid + window_half],
                       radius=window_half, outline=INK, width=width)

out = sys.argv[1] if len(sys.argv) > 1 else os.path.join(os.path.dirname(os.path.abspath(__file__)),
                                                          "agk4_lance_icon.png")
image.resize((WIDTH, HEIGHT), Image.LANCZOS).save(out)
print("wrote", out)
