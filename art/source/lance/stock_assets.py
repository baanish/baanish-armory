"""Load stock Nuclear Option meshes, materials and prefabs from an AssetRipper export into Blender.

Positions convert from Unity (left-handed: X right, Y up, Z forward) to Blender (X right, Y forward, Z up)
by swapping Y and Z. That swap is a reflection, so triangle winding is reversed to keep faces outward.
"""
import math
import os
import re
import struct

import numpy as np

FORMAT_SIZES = {0: 4, 1: 2, 2: 1, 3: 1, 4: 2, 5: 2, 6: 1, 7: 1, 8: 2, 9: 2, 10: 4, 11: 4}


def unity_to_blender(p):
    return (p[0], p[2], p[1])


def _yaml_value(text, key):
    match = re.search(rf"^\s*{key}: (.*)$", text, re.M)
    return match.group(1).strip() if match else None


def read_unity_mesh(path, with_normals=False):
    """Positions (Blender axes), UV0 and triangles per submesh from a Unity mesh asset, plus normals if asked."""
    with open(path, encoding="utf8") as f:
        text = f.read()
    count = int(_yaml_value(text, "m_VertexCount"))
    data = bytes.fromhex(_yaml_value(text, "_typelessdata") or "")
    channels = re.findall(r"- stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)", text)
    channels = [tuple(int(x) for x in c) for c in channels]
    strides = {}
    for stream, offset, fmt, dim in channels:
        if dim:
            strides[stream] = max(strides.get(stream, 0), offset + FORMAT_SIZES[fmt] * (dim & 0xF))
    starts, cursor = {}, 0
    for stream in sorted(strides):
        strides[stream] = (strides[stream] + 3) // 4 * 4
        starts[stream] = cursor
        cursor = (cursor + strides[stream] * count + 15) // 16 * 16

    def attribute(index, width):
        stream, offset, fmt, dim = channels[index]
        if not dim:
            return None
        code = {0: "f", 1: "e"}[fmt]
        base = starts[stream] + offset
        return [struct.unpack_from(f"<{width}{code}", data, base + i * strides[stream]) for i in range(count)]

    positions = attribute(0, 3)
    uvs = attribute(4, 2) or [(0.0, 0.0)] * count
    index_bytes = bytes.fromhex(_yaml_value(text, "m_IndexBuffer") or "")
    wide = _yaml_value(text, "m_IndexFormat") == "1"
    indices = struct.unpack(f"<{len(index_bytes) // (4 if wide else 2)}{'I' if wide else 'H'}", index_bytes)
    submeshes = []
    for first_byte, index_count, base_vertex in re.findall(
            r"- serializedVersion: 2\s+firstByte: (\d+)\s+indexCount: (\d+)\s+topology: 0\s+baseVertex: (\d+)", text):
        first = int(first_byte) // (4 if wide else 2)
        flat = [i + int(base_vertex) for i in indices[first:first + int(index_count)]]
        submeshes.append([(flat[i], flat[i + 2], flat[i + 1]) for i in range(0, len(flat), 3)])
    verts = [unity_to_blender(p) for p in positions]
    if with_normals:
        return verts, uvs, submeshes, [unity_to_blender(n) for n in attribute(1, 3)]
    return verts, uvs, submeshes


def uv_islands(verts, uvs, tris):
    """Group triangles that share vertices. Y and radius ranges are measured from the mesh's tail."""
    parent = list(range(len(verts)))

    def root(i):
        while parent[i] != i:
            parent[i] = parent[parent[i]]
            i = parent[i]
        return i

    for tri in tris:
        for i in tri[1:]:
            parent[root(i)] = root(tri[0])
    groups = {}
    for tri in tris:
        groups.setdefault(root(tri[0]), []).append(tri)
    tail = min(v[1] for v in verts)
    islands = []
    for group in groups.values():
        ids = sorted({i for tri in group for i in tri})
        radii = [math.hypot(verts[i][0], verts[i][2]) for i in ids]
        ys = [verts[i][1] - tail for i in ids]
        islands.append({"ids": ids, "y": (min(ys), max(ys)), "r": (min(radii), max(radii)),
                        "u": min(uvs[i][0] for i in ids)})
    return islands


class StockAssets:
    """Guid lookup and Blender loaders over an exported Assets directory."""

    def __init__(self, assets_dir):
        self.dir = assets_dir
        self.paths = {}
        for folder in ("Mesh", "Material", "Texture2D", "GameObject"):
            for name in os.listdir(os.path.join(assets_dir, folder)):
                if name.endswith(".meta"):
                    with open(os.path.join(assets_dir, folder, name), encoding="utf8") as f:
                        guid = _yaml_value(f.read(), "guid")
                    self.paths[guid] = os.path.join(assets_dir, folder, name[:-5])
        self._materials = {}
        self._meshes = {}

    def texture(self, material_text, slot):
        match = re.search(rf"(?<![A-Za-z_]){slot}:\s+m_Texture: {{fileID: \d+, guid: ([0-9a-f]+)", material_text)
        return self.paths.get(match.group(1)) if match else None

    def material(self, guid):
        """A Blender material from a stock .mat: base color map, plus metallic (R) and smoothness (A) maps."""
        import bpy
        if guid in self._materials:
            return self._materials[guid]
        path = self.paths.get(guid)
        mat = bpy.data.materials.new(os.path.basename(path or guid))
        mat.use_nodes = True
        nodes, links = mat.node_tree.nodes, mat.node_tree.links
        bsdf = nodes["Principled BSDF"]
        bsdf.inputs["Metallic"].default_value = 0.3
        bsdf.inputs["Roughness"].default_value = 0.5
        if path and path.endswith(".mat"):
            with open(path, encoding="utf8") as f:
                text = f.read()
            base = next(filter(None, (self.texture(text, slot) for slot in
                                      ("_BaseColor", "_Basecolor", "_BaseMap", "_MainTex"))), None)
            if base:
                image = nodes.new("ShaderNodeTexImage")
                image.image = bpy.data.images.load(base, check_existing=True)
                links.new(image.outputs["Color"], bsdf.inputs["Base Color"])
            metal = next(filter(None, (self.texture(text, slot) for slot in
                                       ("_Metallic", "_MetallicGlossMap", "_MetallicSmoothness"))), None)
            if metal:
                image = nodes.new("ShaderNodeTexImage")
                image.image = bpy.data.images.load(metal, check_existing=True)
                image.image.colorspace_settings.name = "Non-Color"
                split = nodes.new("ShaderNodeSeparateColor")
                links.new(image.outputs["Color"], split.inputs["Color"])
                links.new(split.outputs["Red"], bsdf.inputs["Metallic"])
                invert = nodes.new("ShaderNodeMath")
                invert.operation = "SUBTRACT"
                invert.inputs[0].default_value = 1.0
                links.new(image.outputs["Alpha"], invert.inputs[1])
                links.new(invert.outputs["Value"], bsdf.inputs["Roughness"])
        self._materials[guid] = mat
        return mat

    def mesh(self, guid, material_guids):
        """A Blender mesh from a stock mesh asset, one material slot per submesh."""
        import bpy
        key = (guid, tuple(material_guids))
        if key in self._meshes:
            return self._meshes[key]
        verts, uvs, submeshes, normals = read_unity_mesh(self.paths[guid], with_normals=True)
        tris = [t for sub in submeshes for t in sub]
        mesh = bpy.data.meshes.new(os.path.basename(self.paths[guid]))
        mesh.from_pydata(verts, [], tris)
        layer = mesh.uv_layers.new()
        for loop in mesh.loops:
            layer.data[loop.index].uv = uvs[loop.vertex_index]
        for mat_guid in material_guids:
            mesh.materials.append(self.material(mat_guid))
        face = 0
        for slot, sub in enumerate(submeshes):
            for _ in sub:
                mesh.polygons[face].material_index = min(slot, len(material_guids) - 1)
                face += 1
        mesh.shade_smooth()
        mesh.normals_split_custom_set_from_vertices(normals)  # the game's hard and soft edges, not Blender's
        self._meshes[key] = mesh
        return mesh

    def prefab(self, guid_or_path, collection, root_matrix=None, skip=lambda name: False):
        """Instance a stock prefab's visible meshes (LOD 0 only) into a Blender collection.

        Returns {GameObject name: world matrix (Unity axes)} so callers can find attach points.
        """
        import bpy
        from mathutils import Matrix
        path = self.paths.get(guid_or_path, guid_or_path)
        with open(path, encoding="utf8") as f:
            docs = re.split(r"^--- !u!(\d+) &(-?\d+).*$", f.read(), flags=re.M)
        objects = {docs[i + 1]: (int(docs[i]), docs[i + 2]) for i in range(1, len(docs), 3)}

        def ref(text, key):
            match = re.search(rf"{key}: {{fileID: (-?\d+)", text)
            return match.group(1) if match else "0"

        game_objects, transforms, filters, renderers, hidden = {}, {}, {}, {}, set()
        for fid, (cls, body) in objects.items():
            if cls == 1:
                game_objects[fid] = (_yaml_value(body, "m_Name") or "", _yaml_value(body, "m_IsActive") == "1")
            elif cls == 4:
                transforms[ref(body, "m_GameObject")] = (fid, body)
            elif cls == 33:
                mesh_guid = re.search(r"m_Mesh: {fileID: \d+, guid: ([0-9a-f]+)", body)
                if mesh_guid:
                    filters[ref(body, "m_GameObject")] = mesh_guid.group(1)
            elif cls == 23 and _yaml_value(body, "m_Enabled") == "1":
                renderers[ref(body, "m_GameObject")] = (
                    fid, re.findall(r"- {fileID: \d+, guid: ([0-9a-f]+)", body.split("m_Materials:")[1].split("m_")[0]))
            elif cls == 205:
                for lod in body.split("- screenRelativeHeight")[2:]:
                    hidden.update(re.findall(r"renderer: {fileID: (-?\d+)", lod))
        by_transform = {tf: (go, body) for go, (tf, body) in transforms.items()}
        world = {}

        def matrix(tf):
            if tf in world:
                return world[tf]
            go, body = by_transform[tf]
            nums = lambda key: [float(x) for x in re.findall(r"[-\d.eE]+", _yaml_value(body, key))]
            x, y, z, w = nums("m_LocalRotation")
            rot = np.array([[1 - 2 * (y * y + z * z), 2 * (x * y - z * w), 2 * (x * z + y * w)],
                            [2 * (x * y + z * w), 1 - 2 * (x * x + z * z), 2 * (y * z - x * w)],
                            [2 * (x * z - y * w), 2 * (y * z + x * w), 1 - 2 * (x * x + y * y)]])
            local = np.eye(4)
            local[:3, :3] = rot * np.array(nums("m_LocalScale"))
            local[:3, 3] = nums("m_LocalPosition")
            parent = ref(body, "m_Father")
            active = game_objects[go][1] and (parent == "0" or matrix(parent)[1])
            base = root_matrix if root_matrix is not None else np.eye(4)
            world[tf] = ((base if parent == "0" else matrix(parent)[0]) @ local, active)
            return world[tf]

        swap = np.array([[1, 0, 0, 0], [0, 0, 1, 0], [0, 1, 0, 0], [0, 0, 0, 1]], dtype=float)
        attach = {}
        for go, (tf, _) in transforms.items():
            name = game_objects[go][0]
            m, active = matrix(tf)
            attach[name] = m
            if not active or go not in filters or go not in renderers or renderers[go][0] in hidden or skip(name):
                continue
            if filters[go] not in self.paths:  # Unity built-in meshes are not in the export
                continue
            mesh = self.mesh(filters[go], renderers[go][1])
            obj = bpy.data.objects.new(name, mesh)
            obj.matrix_world = Matrix([list(r) for r in swap @ m @ swap])  # rows; a bare list reads as columns
            collection.objects.link(obj)
        return attach
