"""Render an FS-20 Vortex with a Kingpin pod on the inner left pylon and a Lance pod on the outer left pylon.

Run: blender --background --factory-startup <lance.blend> --python art/source/lance/render_vortex_loadout.py -- <output_dir> <exported Assets dir>
"""
import os
import sys

import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cycles_gpu import enable_cycles_gpu  # noqa: E402
from lance_pod import add_kingpin_pod, add_lance_pod  # noqa: E402
from stock_assets import StockAssets  # noqa: E402

OUT, ASSETS = sys.argv[sys.argv.index("--") + 1:][:2]
VORTEX = "SmallFighter1.prefab"  # FS-20 Vortex
INNER, OUTER = "pylon_L_mount", "pylon_LL_mount"

scene = bpy.context.scene
enable_cycles_gpu(scene)
for obj in [o for o in scene.objects if o.type in ("CAMERA", "LIGHT") or o.name == "Lance"]:
    bpy.data.objects.remove(obj)
# Daylight for a whole aircraft: sun from above, a weaker bounce from below so the pods under the wing read.
scene.world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.45, 0.5, 0.58, 1)
for name, rotation, strength in (("sun", (0.6, 0.3, -0.8), 3.5), ("bounce", (3.0, -0.2, 0.6), 1.0)):
    sun = bpy.data.objects.new(name, bpy.data.lights.new(name, "SUN"))
    sun.data.energy = strength
    sun.rotation_euler = rotation
    scene.collection.objects.link(sun)
stock = StockAssets(ASSETS)


def new_collection(name):
    collection = bpy.data.collections.new(name)
    scene.collection.children.link(collection)
    return collection


mounts = stock.prefab(os.path.join(ASSETS, "GameObject", VORTEX), new_collection("Vortex"))
add_kingpin_pod(stock, ASSETS, new_collection("Kingpin pod"), mounts[INNER])
add_lance_pod(stock, ASSETS, new_collection("Lance pod"), mounts[OUTER], bpy.data.objects["LanceFolded"])
bpy.context.view_layer.update()

vortex = bpy.data.collections["Vortex"].objects
corners = [o.matrix_world @ Vector(c) for o in vortex for c in o.bound_box]
low = min(c.z for c in corners)
for floor in [o for o in scene.objects if o.type == "MESH" and o.name.startswith("Plane")]:
    floor.location.z = low - 1.2
pods = [o.matrix_world.translation for c in ("Kingpin pod", "Lance pod") for o in bpy.data.collections[c].objects]
focus = sum(pods, Vector()) / len(pods)
print("vortex bounds", [round(min(getattr(c, a) for c in corners), 2) for a in "xyz"],
      [round(max(getattr(c, a) for c in corners), 2) for a in "xyz"], "pods at", tuple(round(x, 2) for x in focus))


def render(name, offset, lens, res=(1920, 1080), target=None):
    cam_data = bpy.data.cameras.new(name)
    cam_data.lens = lens
    cam = bpy.data.objects.new(name, cam_data)
    aim = target if target is not None else focus
    cam.location = aim + Vector(offset)
    cam.rotation_euler = (aim - cam.location).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.filepath = os.path.join(OUT, f"{name}.png")
    bpy.ops.render.render(write_still=True)


lance_pod = next(o for o in bpy.data.collections["Lance pod"].objects if o.data.name == "lance_4pod")
pod_nose = max((lance_pod.matrix_world @ v.co for v in lance_pod.data.vertices), key=lambda p: p.y)
render("lance-pod-nose", (-0.9, 1.1, -0.45), 40, target=pod_nose - Vector((0, 0.25, 0)))
stencil = bpy.data.objects["agk4_stencil"]
visible = min(stencil.data.polygons, key=lambda p: (stencil.matrix_world @ p.center).x)  # the outboard side
centre = stencil.matrix_world @ visible.center
facing = (stencil.matrix_world.to_3x3() @ visible.normal).normalized()
render("lance-pod-stencil", tuple(facing * 0.7 + Vector((0, -0.25, -0.1))), 45, target=centre)
render("vortex-pods-front-low", (-3.2, 5.5, -1.4), 35)
render("vortex-pods-side-low", (-6.5, -0.4, -1.0), 30)
render("vortex-pods-rear-low", (-2.5, -5.5, -1.2), 35)
centre = sum(corners, Vector()) / len(corners)
render("vortex-overview", (-10.0, 12.0, 2.5), 35, target=centre)
for floor in [o for o in scene.objects if o.type == "MESH" and o.name.startswith("Plane")]:
    floor.hide_render = True  # the underside camera sits below floor level
render("vortex-underside", (-9.0, 7.0, -5.0), 35, target=centre)
