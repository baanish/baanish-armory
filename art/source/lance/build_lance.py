"""Build the Lance rocket in Blender and render preview images.

Run: blender --background --factory-startup --python art/source/lance/build_lance.py -- <output_dir> <exported Assets dir>

Lance is built like the stock AGR-24 Kingpin: a 12-sided low-poly body in sections, textured only with stock
materials. The body samples the Kingpin atlas (Missiles4): bare metal for the motor and nose, dark grey paint
aft of the motor band, and the Kingpin's own band, nozzle, tip and canard strips fitted to the new shape.
The laser windows use the Eyeball optics patch (Weapons5), the same purple as the Eyeball-XL windows.
The rocket points along +Y with the nozzle exit at Y=0. Units are meters.
"""
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from cycles_gpu import enable_cycles_gpu  # noqa: E402
from lance_shape import (BOATTAIL, FIN_ROOT, FIN_SPAN, FIN_THICKNESS, FIN_TIP, LENGTH, NOZZLE,  # noqa: E402
                         NOZZLE_BELL, NOZZLE_LIP, NOZZLE_THROAT, RADIUS, TIP_LENGTH, TIP_Y, WINDOW_WIDTH, body_layout, ogive)
from stock_assets import StockAssets, read_unity_mesh, uv_islands  # noqa: E402

OUT, ASSETS = sys.argv[sys.argv.index("--") + 1:][:2]
os.makedirs(OUT, exist_ok=True)
bpy.ops.wm.read_factory_settings(use_empty=True)

SIDES = 12      # the Kingpin's ring count, so its half-ring UV strips map one to one
MISSILES4 = "ce7d6ad143c11934aad61cbd4b0f20d3"  # Kingpin body material
WEAPONS5 = "2f6bf9049589566438b752ff290ea93a"   # Eyeball optics material, as on the Eyeball-XL windows
OPTICS_UV = (0.1287, 0.7060)                   # the Eyeball-XL window patch on the Weapons5 atlas

KP_VERTS, KP_UVS, kp_submeshes = read_unity_mesh(os.path.join(ASSETS, "Mesh", "rocket2.asset"))
KP_TRIS = [t for sub in kp_submeshes for t in sub]
KP_TAIL = min(v[1] for v in KP_VERTS)
KP_ISLANDS = uv_islands(KP_VERTS, KP_UVS, KP_TRIS)


def kingpin_island(y0, y1, r0, r1):
    """The first copy (u < 1) of the Kingpin UV island spanning y0..y1 from its tail and radius r0..r1."""
    hits = [i for i in KP_ISLANDS
            if max(abs(i["y"][0] - y0), abs(i["y"][1] - y1), abs(i["r"][0] - r0), abs(i["r"][1] - r1)) < 0.004
            and i["u"] < 1]
    if len(hits) != 1:
        raise ValueError(f"expected one Kingpin island at y {y0}-{y1}, r {r0}-{r1}, found {len(hits)}")
    return hits[0]


def affine_uv(island, coords, name):
    """Least-squares affine map from per-vertex coordinates to the island's UVs."""
    a = np.array([[*coords(i), 1.0] for i in island["ids"]])
    b = np.array([KP_UVS[i] for i in island["ids"]])
    m = np.linalg.lstsq(a, b, rcond=None)[0]
    print(f"strip {name:12s} worst fit error {np.abs(a @ m - b).max() * 512:.2f} texels")
    return lambda *c: tuple(np.array([*c, 1.0]) @ m)


def ring_strip(name, y0, y1, r0, r1):
    """UV for (s, t): s runs 0..1 around half the ring, t runs 0..1 from the section's back to its front."""
    island = kingpin_island(y0, y1, r0, r1)
    angles = {i: math.atan2(KP_VERTS[i][2], KP_VERTS[i][0]) for i in island["ids"]}
    if max(angles.values()) - min(angles.values()) > math.pi + 0.1:
        angles = {i: a + math.tau if a < 0 else a for i, a in angles.items()}
    low = min(angles.values())
    ya, yb = island["y"]
    return affine_uv(island, lambda i: ((angles[i] - low) / math.pi,
                                        (KP_VERTS[i][1] - KP_TAIL - ya) / (yb - ya)), name)


def plane_strip(name, y0, y1, r0, r1, u_axis, v_axis):
    """UV for a flat part, from two normalized in-plane coordinates of the Kingpin island."""
    island = kingpin_island(y0, y1, r0, r1)
    ids = island["ids"]
    lo_u, hi_u = min(u_axis(i) for i in ids), max(u_axis(i) for i in ids)
    lo_v, hi_v = min(v_axis(i) for i in ids), max(v_axis(i) for i in ids)
    return affine_uv(island, lambda i: ((u_axis(i) - lo_u) / (hi_u - lo_u),
                                        (v_axis(i) - lo_v) / (hi_v - lo_v)), name)


def rect_strip(u0, u1, v0, v1):
    """UV for (s, t) over an atlas rectangle: t runs along u, s runs along v."""
    return lambda s, t: (u0 + t * (u1 - u0), v0 + s * (v1 - v0))


def kp_y(i):
    return KP_VERTS[i][1] - KP_TAIL


def kp_r(i):
    return math.hypot(KP_VERTS[i][0], KP_VERTS[i][2])


# Kingpin sections, measured from its tail (y range, then radius range), and plain atlas patches.
STRIPS = {
    "nozzle": ring_strip("nozzle", 0.000, 0.072, 0.036, 0.043),
    "throat": plane_strip("throat", 0.000, 0.072, 0.000, 0.043,
                          lambda i: KP_VERTS[i][0], lambda i: KP_VERTS[i][2]),
    "band_orange": ring_strip("band_orange", 0.473, 0.516, 0.064, 0.064),
    "band_black": ring_strip("band_black", 1.000, 1.040, 0.063, 0.064),
    "band_yellow": ring_strip("band_yellow", 1.588, 1.621, 0.063, 0.064),
    "tip": ring_strip("tip", 1.938, 1.956, 0.015, 0.032),
    "fin": plane_strip("fin", 1.388, 1.442, 0.064, 0.197, kp_y, kp_r),
    # The atlas's bare-metal patch (light base colour, full metallic), split into bands so casings vary.
    "silver_1": rect_strip(0.012, 0.232, 0.38, 0.44),
    "silver_2": rect_strip(0.012, 0.232, 0.44, 0.50),
    "silver_3": rect_strip(0.012, 0.232, 0.50, 0.56),
    # Dark grey paint, like the Lynchpin's aft body.
    "dark": rect_strip(0.02, 0.20, 0.62, 0.78),
}

verts, faces, face_uvs, face_smooth = [], [], [], []


def lathe(profile, strip, flip=False):
    """Revolve (radius, y) rings into a 12-sided section; UVs repeat per half ring like the Kingpin."""
    uv = STRIPS[strip]
    half = SIDES // 2
    base = len(verts)
    y_back, y_front = profile[0][1], profile[-1][1]
    for r, y in profile:
        for k in range(SIDES):
            a = math.tau * k / SIDES
            verts.append((r * math.cos(a), y, r * math.sin(a)))
    for j in range(len(profile) - 1):
        for k in range(SIDES):
            side = k // half
            loop = ((j, k), (j, k + 1), (j + 1, k + 1), (j + 1, k))
            faces.append([base + jj * SIDES + kk % SIDES for jj, kk in loop])
            uvs = []
            for jj, kk in loop:
                t = (profile[jj][1] - y_back) / (y_front - y_back)
                uvs.append(uv((kk - side * half) / half, 1 - t if flip else t))
            face_uvs.append(uvs)
            face_smooth.append(True)


def cap(radius, y, strip):
    """A flat disk facing backward, mapped onto a flat Kingpin island."""
    uv = STRIPS[strip]
    center = len(verts)
    verts.append((0, y, 0))
    for k in range(SIDES):
        a = math.tau * k / SIDES
        verts.append((radius * math.cos(a), y, radius * math.sin(a)))
    for k in range(SIDES):
        ring = [center + 1 + k, center + 1 + (k + 1) % SIDES]
        faces.append([center, *ring])
        face_uvs.append([uv(0.5 + 0.5 * verts[i][0] / radius, 0.5 + 0.5 * verts[i][2] / radius)
                         for i in (center, *ring)])
        face_smooth.append(False)


def fin(index, root=FIN_ROOT, tip=FIN_TIP, span=FIN_SPAN, thickness=FIN_THICKNESS):
    """A small clipped-delta tail fin: a flat plate with thin edges, mapped onto a Kingpin canard."""
    uv = STRIPS["fin"]
    outline = [(root[0], 0.0), (root[1], 0.0), (tip[1], span), (tip[0], span)]
    angle = math.tau * index / 4 + math.pi / 4
    c, s = math.cos(angle), math.sin(angle)
    base = len(verts)
    for side in (1, -1):
        for y, h in outline:
            r, w = RADIUS - 0.002 + h, side * thickness / 2
            verts.append((r * c - w * s, y, r * s + w * c))
    coords = [((y - root[0]) / (root[1] - root[0]), h / span) for y, h in outline]
    top, bottom = list(range(base, base + 4)), list(range(base + 4, base + 8))
    for loop in (top, bottom[::-1]):
        faces.append(loop)
        face_uvs.append([uv(*coords[(i - base) % 4]) for i in loop])
        face_smooth.append(False)
    for a in range(4):
        b = (a + 1) % 4
        faces.append([top[a], bottom[a], bottom[b], top[b]])
        face_uvs.append([uv(*coords[a]), uv(*coords[a]), uv(*coords[b]), uv(*coords[b])])
        face_smooth.append(False)


# Motor end: an exposed nozzle bell with a dark throat, then a short boat-tail.
# The body is built from its own tail at Y=0, then everything moves forward by NOZZLE so the exit sits at Y=0.
lathe(NOZZLE_BELL, "nozzle")
lathe(NOZZLE_LIP, "nozzle")
lathe(NOZZLE_THROAT, "nozzle", flip=True)
cap(NOZZLE_THROAT[-1][0], NOZZLE_THROAT[-1][1], "throat")
lathe(BOATTAIL, "dark")

layout, y, window_y = body_layout()
for strip, back, front, flip in layout:
    lathe([(RADIUS, back), (RADIUS, front)], strip, flip)

# Long tangent-ogive nose in bare metal (the Kingpin's own nose patch smears when stretched this far),
# ending in a dark penetrator tip.
nose_length = TIP_Y - y
stations = [y + (nose_length - TIP_LENGTH) * i / 6 for i in range(7)]
lathe([(ogive(TIP_Y - s, nose_length), s) for s in stations], "silver_2", flip=True)
tip_stations = [stations[-1] + TIP_LENGTH * i / 3 for i in range(4)]
lathe([(max(ogive(TIP_Y - s, nose_length), 0.0004), s) for s in tip_stations], "tip")

first_fin_face = len(faces)
for i in range(4):
    fin(i)


def laser_window(index, y0, y1, width=WINDOW_WIDTH, height=0.0012):
    """A long raised window on a flat face of the 12-sided body, between two fins."""
    angle = math.tau * (index * 3 + 0.5) / SIDES
    normal = (math.cos(angle), math.sin(angle))
    side = (-normal[1], normal[0])
    apothem = RADIUS * math.cos(math.pi / SIDES)
    base = len(verts)
    for h in (0.0, height):
        for y, w in ((y0, -1), (y1, -1), (y1, 1), (y0, 1)):
            d = apothem + h
            verts.append((normal[0] * d + side[0] * w * width / 2, y, normal[1] * d + side[1] * w * width / 2))
    top = [base + 4, base + 5, base + 6, base + 7]
    loops = [top] + [[base + a, base + (a + 1) % 4, base + 4 + (a + 1) % 4, base + 4 + a] for a in range(4)]
    for loop in loops:
        faces.append(loop)
        face_uvs.append([OPTICS_UV] * 4)
        face_smooth.append(False)


first_window_face = len(faces)
for i in range(4):
    laser_window(i, *window_y)

mesh = bpy.data.meshes.new("lance")
mesh.from_pydata([(x, y + NOZZLE, z) for x, y, z in verts], [], faces)
uv_layer = mesh.uv_layers.new(name="UVMap")
for poly, uvs, smooth in zip(mesh.polygons, face_uvs, face_smooth):
    poly.use_smooth = smooth
    poly.material_index = int(poly.index >= first_window_face)
    for loop_index, coord in zip(poly.loop_indices, uvs):
        uv_layer.data[loop_index].uv = coord
bm = bmesh.new()
bm.from_mesh(mesh)
bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
bm.to_mesh(mesh)
bm.free()
print(f"lance mesh: {len(mesh.vertices)} vertices, {sum(len(p.vertices) - 2 for p in mesh.polygons)} triangles")

# The same stock materials the game uses, with their base colour and metallic maps.
stock = StockAssets(ASSETS)
mesh.materials.append(stock.material(MISSILES4))
mesh.materials.append(stock.material(WEAPONS5))
lance = bpy.data.objects.new("Lance", mesh)
bpy.context.collection.objects.link(lance)

# The carried round has its fins folded away, like the stock rocket2_folded inside the Kingpin pod.
folded = mesh.copy()
folded.name = "lance_folded"
bm = bmesh.new()
bm.from_mesh(folded)
bm.faces.ensure_lookup_table()
bmesh.ops.delete(bm, geom=[bm.faces[i] for i in range(first_fin_face, first_window_face)], context="FACES")
bm.to_mesh(folded)
bm.free()
lance_folded = bpy.data.objects.new("LanceFolded", folded)
lance_folded.hide_render = True
bpy.context.collection.objects.link(lance_folded)

# Scene: studio lights, dark world, floor.
scene = bpy.context.scene
world = bpy.data.worlds.new("studio")
world.use_nodes = True
world.node_tree.nodes["Background"].inputs["Color"].default_value = (0.035, 0.038, 0.045, 1)
scene.world = world
for name, loc, energy, size in (("key", (1.5, 1.4, 2.0), 310, 2.5),
                                ("fill", (-2.0, 2.8, 0.6), 70, 3.0),
                                ("rim", (0.0, -1.5, -1.8), 220, 2.0)):
    light = bpy.data.lights.new(name, "AREA")
    light.energy = energy
    light.size = size
    obj = bpy.data.objects.new(name, light)
    obj.location = loc
    obj.rotation_euler = (Vector((0, LENGTH / 2, 0)) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(obj)
floor = bpy.data.materials.new("floor")
floor.use_nodes = True
floor.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.025, 0.027, 0.03, 1)
bpy.ops.mesh.primitive_plane_add(size=30, location=(0, LENGTH / 2, -0.6))
bpy.context.active_object.data.materials.append(floor)

scene.render.engine = "CYCLES"
enable_cycles_gpu(scene)
scene.cycles.samples = 64
scene.cycles.use_denoising = True
scene.view_settings.view_transform = "AgX"


def render(name, loc, target, lens, res, ortho=0.0):
    cam_data = bpy.data.cameras.new(name)
    cam_data.lens = lens
    if ortho:
        cam_data.type = "ORTHO"
        cam_data.ortho_scale = ortho
    cam = bpy.data.objects.new(name, cam_data)
    cam.location = loc
    cam.rotation_euler = (Vector(target) - Vector(loc)).to_track_quat("-Z", "Y").to_euler()
    scene.collection.objects.link(cam)
    scene.camera = cam
    scene.render.resolution_x, scene.render.resolution_y = res
    scene.render.filepath = os.path.join(OUT, f"{name}.png")
    bpy.ops.render.render(write_still=True)


render("lance-three-quarter", (2.9, -1.2, 1.1), (0, 1.8, 0), 40, (1920, 1080))
render("lance-side", (4.5, LENGTH / 2, 0.0), (0, LENGTH / 2, 0), 50, (1920, 520), ortho=4.2)
render("lance-tail", (0.55, -0.6, 0.3), (0, 0.15, 0), 60, (1280, 960))
render("lance-nose", (0.8, LENGTH - 0.95, 0.3), (0, LENGTH - 0.5, 0), 55, (1280, 960))
clay = bpy.data.materials.new("clay")
clay.use_nodes = True
clay.node_tree.nodes["Principled BSDF"].inputs["Base Color"].default_value = (0.5, 0.5, 0.5, 1)
bpy.context.view_layer.material_override = clay
render("lance-clay", (2.9, -1.2, 1.1), (0, 1.8, 0), 40, (1920, 1080))
bpy.context.view_layer.material_override = None
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(OUT, "lance.blend"))
