"""Render Lance beside the stock AGR-18 Lynchpin and AGR-24 Kingpin.

Run: blender --background --factory-startup <lance.blend> --python art/source/lance/render_comparison.py -- <output_dir> <exported Assets dir>
"""
import math
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cycles_gpu import enable_cycles_gpu  # noqa: E402
from stock_assets import StockAssets  # noqa: E402

OUT, ASSETS = sys.argv[sys.argv.index("--") + 1:][:2]
STOCK = (("AGR-18 Lynchpin", "Rocket1.prefab"), ("AGR-24 Kingpin", "Rocket2.prefab"))
SPACING = 0.35

scene = bpy.context.scene
enable_cycles_gpu(scene)
for cam in [o for o in scene.objects if o.type == "CAMERA"]:
    bpy.data.objects.remove(cam)
stock = StockAssets(ASSETS)


def label(text, x, y):
    curve = bpy.data.curves.new(text, "FONT")
    curve.body = text
    curve.size = 0.07
    curve.align_x = "RIGHT"
    obj = bpy.data.objects.new(text, curve)
    obj.location = (x, y, 0)
    obj.rotation_euler = (0, 0, math.pi / 2)  # flat in the XY plane, read from the top camera
    white = bpy.data.materials.new(text)
    white.use_nodes = True
    white.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.8, 0.8, 0.8, 1)
    curve.materials.append(white)
    scene.collection.objects.link(obj)


lengths = {"AGK-4 Lance": bpy.data.objects["Lance"].dimensions.y}
for row, (name, prefab) in enumerate(STOCK, start=1):
    collection = bpy.data.collections.new(name)
    scene.collection.children.link(collection)
    stock.prefab(os.path.join(ASSETS, "GameObject", prefab), collection)
    bpy.context.view_layer.update()
    corners = [o.matrix_world @ Vector(c) for o in collection.objects for c in o.bound_box]
    tail, nose = min(c.y for c in corners), max(c.y for c in corners)
    for obj in collection.objects:
        obj.location += Vector((row * SPACING, -tail, 0))
    lengths[name] = nose - tail
for row, name in enumerate(["AGK-4 Lance"] + [s[0] for s in STOCK]):
    label(f"{name}  {lengths[name]:.2f} m", row * SPACING, -0.08)


def render(name, loc, target, res, ortho=0.0, lens=50, rotation=None):
    cam_data = bpy.data.cameras.new(name)
    cam_data.lens = lens
    if ortho:
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = ortho
    cam = bpy.data.objects.new(name, cam_data)
    cam.location = loc
    cam.rotation_euler = rotation or (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.filepath = os.path.join(OUT, f"{name}.png")
    bpy.ops.render.render(write_still=True)


mid_x = SPACING
# Straight down, nose to the right, Lance on the top row.
render("comparison-top", (mid_x, 1.5, 4.0), None, (1920, 760), ortho=5.0, rotation=(0, 0, math.pi / 2))
render("comparison-three-quarter", (mid_x + 2.6, -1.5, 1.5), (mid_x, 1.5, 0), (1920, 1080), lens=40)
