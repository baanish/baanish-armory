"""The AGK-4 Lance's shape, shared by the Blender model, the loadout icon and the pod stencil.

Pure Python so scripts outside Blender can use it. Distances are metres along the rocket, measured from the
body's own tail (the nozzle bell sticks out NOZZLE behind it). Radii are metres.
"""
import math

LENGTH = 3.92   # twice the Kingpin mesh (1.96 m), nozzle exit to tip
NOZZLE = 0.065  # how far the nozzle bell sticks out behind the body
RADIUS = 0.045  # half the Kingpin's frontal area (127 mm body)

NOZZLE_BELL = [(0.034, -NOZZLE), (0.029, -0.02), (0.028, 0.0)]
NOZZLE_THROAT = [(0.031, -NOZZLE + 0.001), (0.019, -0.03), (0.012, -0.008)]
NOZZLE_LIP = [NOZZLE_BELL[0], NOZZLE_THROAT[0]]  # closes the exit rim between the bell and the throat
BOATTAIL = [(0.028, 0.0), (0.038, 0.035), (RADIUS, 0.08)]

# Body, back to front: dark fin section, motor band, a long bare-metal motor in five casings, joint,
# guidance section carrying the laser windows, a thin warhead band for the small charge, then the warhead.
# Each entry is (atlas strip, length, flip the strip along the body).
GUIDANCE = ("silver_3", 0.215, False)
SECTIONS = [("dark", 0.32, False), ("band_orange", 0.043, False),
            ("silver_1", 0.484, False), ("silver_2", 0.484, True), ("silver_3", 0.484, False),
            ("silver_1", 0.484, True), ("silver_2", 0.484, False), ("band_black", 0.04, False),
            GUIDANCE, ("band_yellow", 0.033, False), ("silver_1", 0.188, True)]
BODY_START = BOATTAIL[-1][1]
TIP_Y = LENGTH - NOZZLE
TIP_LENGTH = 0.14   # dark penetrator tip at the end of the nose

FIN_ROOT = (0.10, 0.36)   # leading and trailing edge along the body at the root
FIN_TIP = (0.10, 0.19)    # the same at the tip: a clipped delta
FIN_SPAN = 0.055
FIN_THICKNESS = 0.004

WINDOW_INSET = 0.02       # laser windows stop this far short of each end of the guidance section
WINDOW_WIDTH = 0.014


def body_layout():
    """[(strip, back y, front y, flip)] for the straight body, then the nose start and the window span."""
    layout, y, windows = [], BODY_START, None
    for strip, length, flip in SECTIONS:
        if (strip, length, flip) == GUIDANCE:
            windows = (y + WINDOW_INSET, y + length - WINDOW_INSET)
        layout.append((strip, y, y + length, flip))
        y += length
    return layout, y, windows


def ogive(x, length):
    """Tangent-ogive radius at distance x from the tip of a nose this long."""
    rho = (RADIUS ** 2 + length ** 2) / (2 * RADIUS)
    return math.sqrt(max(rho ** 2 - (length - x) ** 2, 0)) + RADIUS - rho
