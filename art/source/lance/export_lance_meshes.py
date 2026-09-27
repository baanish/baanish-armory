"""Export the AGK-4 Lance meshes as Unity-axis JSON for the Unity importer (LanceAssetBuild.cs).

Run: blender --background --factory-startup <lance.blend> --python art/source/lance/export_lance_meshes.py -- <output_dir> <exported Assets dir>

Writes lance.json (flight round), lance_folded.json (carried round, fins folded), lance_4pod.json (the pod,
with the AGK-4 stencil decals as a second submesh) and seats.json (the four carried rounds' centres in the
pod prefab). Axes convert from Blender (X right, Y forward, Z up) to Unity (X right, Y up, Z forward); that swap
is a reflection, so triangle winding is reversed. Rounds are centred on their length, as the Kingpin's are.
"""
import json
import os
import sys

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
from lance_pod import add_lance_pod  # noqa: E402
from lance_shape import LENGTH  # noqa: E402
from stock_assets import StockAssets  # noqa: E402

OUT, ASSETS = sys.argv[sys.argv.index("--") + 1:][:2]
os.makedirs(OUT, exist_ok=True)


def to_unity(v):
    return [round(v[0], 6), round(v[2], 6), round(v[1], 6)]


def export(name, parts, offset=(0.0, 0.0, 0.0)):
    """parts: [(mesh, material slot -> submesh index)]. Splits vertices wherever normals or UVs differ."""
    vertices, normals, uvs, submeshes, index = [], [], [], {}, {}
    for mesh, slot_to_submesh in parts:
        mesh.calc_loop_triangles()
        uv_layer = mesh.uv_layers.active.data
        corner_normals = mesh.corner_normals
        for tri in mesh.loop_triangles:
            corners = []
            for loop in tri.loops:
                co = mesh.vertices[mesh.loops[loop].vertex_index].co
                key = (tuple(round(c, 6) for c in co), tuple(round(n, 4) for n in corner_normals[loop].vector),
                       tuple(round(u, 6) for u in uv_layer[loop].uv))
                if key not in index:
                    index[key] = len(vertices)
                    vertices.append(to_unity([co[i] + offset[i] for i in range(3)]))
                    normals.append(to_unity(corner_normals[loop].vector))
                    uvs.append([round(uv_layer[loop].uv[0], 6), round(uv_layer[loop].uv[1], 6)])
                corners.append(index[key])
            submesh = slot_to_submesh[tri.material_index]
            submeshes.setdefault(submesh, []).extend([corners[0], corners[2], corners[1]])
    flat = lambda rows: [x for row in rows for x in row]  # Unity's JsonUtility reads flat arrays only
    model = {"name": name, "vertices": flat(vertices), "normals": flat(normals), "uv": flat(uvs),
             "submeshes": [{"indices": submeshes[i]} for i in sorted(submeshes)]}
    with open(os.path.join(OUT, name + ".json"), "w", encoding="utf8", newline="\n") as f:
        json.dump(model, f, separators=(",", ":"))
    print(f"{name}: {len(vertices)} vertices, {[len(s['indices']) // 3 for s in model['submeshes']]} triangles per submesh")


centre = (0.0, -LENGTH / 2, 0.0)
export("lance", [(bpy.data.objects["Lance"].data, {0: 0, 1: 1})], centre)
export("lance_folded", [(bpy.data.objects["LanceFolded"].data, {0: 0, 1: 1})], centre)

collection = bpy.data.collections.new("pod export")
bpy.context.scene.collection.children.link(collection)
add_lance_pod(StockAssets(ASSETS), ASSETS, collection, np.eye(4), bpy.data.objects["LanceFolded"])
bpy.context.view_layer.update()
pod = next(o for o in collection.objects if o.data.name == "lance_4pod")
stencil = next(o for o in collection.objects if o.data.name == "agk4_stencil")
export("lance_4pod", [(pod.data, {0: 0}), (stencil.data, {0: 1})])

# Seats in the prefab root's space: the pod root sits at the mount, so world space here is prefab space.
rounds = sorted((o for o in collection.objects if o.name.startswith("lance_round")), key=lambda o: o.name)
seats = [to_unity(o.matrix_world @ Vector((0, LENGTH / 2, 0))) for o in rounds]
with open(os.path.join(OUT, "seats.json"), "w", encoding="utf8", newline="\n") as f:
    json.dump({"rounds": [x for seat in seats for x in seat]}, f, indent=1)
print("seats", seats)
