"""The Lance 4-round launcher, made from the stock AGR-24 Kingpin pod (Rocket2_4Pod).

The Kingpin tube pod keeps its stock adapter pylon and diamond of four tubes. For the sleeker Lance it is
slimmed to the Lance's diameter and its pointed front (with the tube mouths cut into it) is drawn out longer.
It carries the fins-folded Lance, the way the stock pod carries rocket2_folded.

Texture stays at stock density: the body is lengthened by repeating a slice of the stock body, not by
stretching it, and the longer pointed front is re-projected onto plain paint of its original shade.
The stock body paint carries an "AGR-24" stencil; an AGK-4 decal (lance_stencil.py) covers each one.
"""
import math
import os

import bmesh
from mathutils import Matrix, Vector

KINGPIN_POD = "Rocket2_4Pod.prefab"
LANCE_LENGTH = 3.92
SLIM = 0.72          # Lance diameter over Kingpin diameter (90 mm / 127 mm), rounded up for tube clearance
# Positions below are Kingpin pod-local Y (forward), measured from the stock rocket2_4launcher1 mesh.
SLICE = (-0.95, 0.75)  # a plain run of the stock body, repeated to lengthen the pod
NOSE_START = 0.87    # where the stock pod's pointed front begins
NOSE_TIP = 1.093     # the stock pod's front point
NOSE_LENGTH = 0.45   # the Lance pod's pointed front, in metres (stock: 0.22)
NOSE_STRETCH = NOSE_LENGTH / (NOSE_TIP - NOSE_START)
TIP_AT = 1.0         # how far up the angled tube mouths the Lance tip reaches, so its nose shows
REAR_INSET = 0.039   # how far the Kingpin's tail sits inside the pod's rear face; the Lance matches it
UV_PER_METRE = 0.33  # the stock pod body's texel density (316 px over 1.9 m on a 512 px atlas)
# Plain Missiles4 atlas patches for the re-projected front, by the shade the stock face had.
PAINT = {"dark": (0.02, 0.20, 0.62, 0.78), "mid": (0.615, 0.785, 0.01, 0.61), "light": (0.012, 0.232, 0.38, 0.56)}
# The stock "AGR-24" stencil on the Missiles4 atlas (u0, u1, v0, v1); its text reads toward -v, letters up +u.
OLD_STENCIL_UV = (0.7207, 0.7305, 0.2617, 0.3164)
STENCIL_MARGIN = (1.3, 1.8)   # decal size over the old stencil's, along and across the text
STENCIL_IMAGE = os.path.join(os.path.dirname(os.path.abspath(__file__)), "agk4_stencil.png")


def add_kingpin_pod(stock, assets_dir, collection, mount):
    """The stock Kingpin 4-pod at a hardpoint mount (a Unity-space matrix)."""
    stock.prefab(os.path.join(assets_dir, "GameObject", KINGPIN_POD), collection, root_matrix=mount)


def _nose_y(y):
    """Stretch the stock pod's pointed front forward; everything behind it keeps its position."""
    return NOSE_START + (y - NOSE_START) * NOSE_STRETCH if y > NOSE_START else y


def _shade(image, uv):
    """Dark, mid or light: the stock atlas brightness at a UV."""
    width, height = image.size
    x, y = int(uv[0] % 1 * (width - 1)), int(uv[1] % 1 * (height - 1))
    level = sum(image.pixels[(y * width + x) * 4:(y * width + x) * 4 + 3]) / 3
    return "dark" if level < 0.3 else "light" if level > 0.6 else "mid"


def _lengthen(bm, grow):
    """Cut the stock body at SLICE, move the front forward by grow, and fill the gap with slice copies."""
    for cut in SLICE:
        geom = list(bm.verts) + list(bm.edges) + list(bm.faces)
        bmesh.ops.bisect_plane(bm, geom=geom, dist=1e-5, plane_co=(0, cut, 0), plane_no=(0, 1, 0))
    back, front = SLICE
    inside = [f for f in bm.faces if all(back - 1e-4 <= v.co.y <= front + 1e-4 for v in f.verts)]
    length = front - back
    copies = max(1, round((length + grow) / length))
    step = (length + grow) / copies
    copied = [[g for g in bmesh.ops.duplicate(bm, geom=inside)["geom"] if isinstance(g, bmesh.types.BMVert)]
              for _ in range(copies)]
    placed = {v for verts in copied for v in verts}
    for v in bm.verts:
        if v not in placed and v.co.y > front - 1e-4:
            v.co.y = _nose_y(v.co.y) + grow
    for i, verts in enumerate(copied):
        for v in verts:
            v.co.y = back + i * step + (v.co.y - back) * step / length
    bmesh.ops.delete(bm, geom=inside, context="FACES")


def _reproject_front(bm, uv_layer, image, nose_start):
    """Flat-project the drawn-out front faces onto plain paint of their original shade, at stock density."""
    groups = {}
    for f in bm.faces:
        if max(v.co.y for v in f.verts) > nose_start + 1e-4:
            centre = sum((l[uv_layer].uv for l in f.loops), Vector((0, 0))) / len(f.loops)
            groups.setdefault(_shade(image, centre), []).append(f)
    for shade, faces in groups.items():
        u0, u1, v0, v1 = PAINT[shade]
        coords = {}
        for f in faces:
            n = f.normal
            axes = (0, 2) if abs(n.y) >= max(abs(n.x), abs(n.z)) else (1, 2) if abs(n.x) >= abs(n.z) else (1, 0)
            for l in f.loops:
                coords[l] = (l.vert.co[axes[0]], l.vert.co[axes[1]])
        a0, b0 = min(c[0] for c in coords.values()), min(c[1] for c in coords.values())
        span = max(max(c[0] for c in coords.values()) - a0, max(c[1] for c in coords.values()) - b0, 1e-6)
        scale = min(UV_PER_METRE, (u1 - u0) / span, (v1 - v0) / span)
        for loop, (a, b) in coords.items():
            loop[uv_layer].uv = (u0 + (a - a0) * scale, v0 + (b - b0) * scale)


def _stencil_spots(bm, uv_layer):
    """Where the pod's UVs land on the old stencil: (centre, reading direction, letter-up, normal, length, height)."""
    u0, u1, v0, v1 = OLD_STENCIL_UV
    centre_uv = Vector(((u0 + u1) / 2, (v0 + v1) / 2))
    spots = []
    for tri in bm.calc_loop_triangles():
        uvs = [l[uv_layer].uv.copy() for l in tri]
        shift = math.floor(min(uv.x for uv in uvs))  # mirrored halves repeat the atlas at u + 1
        a, b, c = (uv - Vector((shift, 0)) for uv in uvs)
        e1, e2, p = b - a, c - a, centre_uv - a
        det = e1.x * e2.y - e1.y * e2.x
        if abs(det) < 1e-12:
            continue
        w1, w2 = (p.x * e2.y - p.y * e2.x) / det, (e1.x * p.y - e1.y * p.x) / det
        if w1 < 0 or w2 < 0 or w1 + w2 > 1:
            continue
        p0, p1, p2 = (l.vert.co for l in tri)
        d1, d2 = p1 - p0, p2 - p0
        along_u = (d1 * e2.y - d2 * e1.y) / det   # 3D metres per unit of u
        along_v = (d2 * e1.x - d1 * e2.x) / det   # 3D metres per unit of v
        normal = d1.cross(d2).normalized()
        reading, up = -along_v.normalized(), along_u.normalized()
        if reading.cross(up).dot(normal) < 0:     # keep the new text readable from outside
            reading = -reading
        spots.append((p0 + d1 * w1 + d2 * w2, reading, up, normal,
                      (v1 - v0) * along_v.length * STENCIL_MARGIN[0], (u1 - u0) * along_u.length * STENCIL_MARGIN[1]))
    return spots


def _add_stencils(spots, pod, collection):
    """One AGK-4 decal quad per old stencil, 1.5 mm proud of the pod skin."""
    import bpy
    verts, faces, uvs = [], [], []
    for centre, reading, up, normal, length, height in spots:
        base = len(verts)
        for s, t in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            verts.append(centre + normal * 0.0015 + reading * s * length / 2 + up * t * height / 2)
            uvs.append(((s + 1) / 2, (t + 1) / 2))
        faces.append(list(range(base, base + 4)))
    mesh = bpy.data.meshes.new("agk4_stencil")
    mesh.from_pydata(verts, [], faces)
    layer = mesh.uv_layers.new()
    for loop in mesh.loops:
        layer.data[loop.index].uv = uvs[loop.vertex_index]
    mat = bpy.data.materials.new("agk4_stencil")
    mat.use_nodes = True
    image = mat.node_tree.nodes.new("ShaderNodeTexImage")
    image.image = bpy.data.images.load(STENCIL_IMAGE, check_existing=True)
    bsdf = mat.node_tree.nodes["Principled BSDF"]
    mat.node_tree.links.new(image.outputs["Color"], bsdf.inputs["Base Color"])
    bsdf.inputs["Roughness"].default_value = 0.51  # the pod paint there: metallic 0, smoothness 0.49
    mesh.materials.append(mat)
    decal = bpy.data.objects.new("agk4_stencil", mesh)
    decal.matrix_world = pod.matrix_world
    collection.objects.link(decal)
    print(f"placed {len(spots)} AGK-4 stencils")


def add_lance_pod(stock, assets_dir, collection, mount, lance_folded):
    """The Lance 4-pod at a hardpoint mount, loaded with four copies of the folded Lance object."""
    attach = stock.prefab(os.path.join(assets_dir, "GameObject", KINGPIN_POD), collection, root_matrix=mount,
                          skip=lambda name: name.startswith("rocket"))
    pod = next(o for o in collection.objects if o.data.name == "rocket2_4launcher1.asset")
    pod.data = pod.data.copy()
    pod.data.name = "lance_4pod"
    ys = [v.co.y for v in pod.data.vertices]
    rear, top = min(ys), max(v.co.z for v in pod.data.vertices)
    # Grow the body so the Lance tail sits REAR_INSET inside the rear face, as the Kingpin's does.
    grow = LANCE_LENGTH + rear + REAR_INSET - _nose_y(TIP_AT)
    tail_y = _nose_y(TIP_AT) + grow - LANCE_LENGTH
    centre_shift = (min(ys) + max(ys)) / 2 - (_nose_y(max(ys)) + grow + rear) / 2  # centred on the pylon

    bm = bmesh.new()
    bm.from_mesh(pod.data)
    uv_layer = bm.loops.layers.uv.active
    _lengthen(bm, grow)
    for v in bm.verts:
        v.co = Vector((v.co.x * SLIM, v.co.y + centre_shift, top + (v.co.z - top) * SLIM))
    bm.normal_update()
    base_image = pod.data.materials[0].node_tree.nodes["Image Texture"].image
    _reproject_front(bm, uv_layer, base_image, _nose_y(NOSE_START) + grow + centre_shift)
    spots = _stencil_spots(bm, uv_layer)
    bm.to_mesh(pod.data)
    bm.free()
    _add_stencils(spots, pod, collection)

    swap = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, 1, 0, 0), (0, 0, 0, 1)))
    to_local = pod.matrix_world.inverted()
    for i in range(1, 5):
        seat = to_local @ (swap @ Matrix([list(r) for r in attach[f"rocket{i}"]]) @ swap).translation
        round_ = lance_folded.copy()
        round_.name = f"lance_round{i}"
        round_.hide_render = False
        round_.matrix_world = pod.matrix_world @ Matrix.Translation(
            (seat.x * SLIM, tail_y + centre_shift, top + (seat.z - top) * SLIM))
        collection.objects.link(round_)
