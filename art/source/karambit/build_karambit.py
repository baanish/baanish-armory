"""Export SAAM-18 Karambit geometry in Unity metres with its nose along +Z.

Run: python art/source/karambit/build_karambit.py --output .local/karambit-art --assets unity/Assets/Blueprinter/_donotship
JSON and OBJ use the stock Scythe root offset and its existing Weapons4 atlas.
The exporter uses Python's standard library for geometry and Pillow for the HUD icon.
"""

import argparse
import hashlib
import json
import math
from pathlib import Path
import re
import struct


ROOT = Path(__file__).resolve().parents[3]
STOCK = ROOT / "unity/Assets/Blueprinter/_donotship"
PROFILE = {"length": 1.814801, "diameter": .12, "nose_fraction": .42,
           "fin_span": .28, "fin_thickness": .004, "strake_radius": .084,
           "strake_thickness": .003, "radial_segments": 16, "nose_segments": 8,
           "nose_tip_length": .012}
# Pixel fields are inside the Scythe's own islands. Their normal maps are flat;
# the long raised-spine island contains vents and cannot wrap around a cylinder.
UV_PIXELS = {"body": [249, 294, 266, 379], "nose": [213, 137, 245, 215],
             "fin": [218, 145, 244, 215], "strake": [218, 145, 244, 215],
             "nozzle": [264, 64, 303, 93], "metal": [225, 64, 242, 82]}
UV_REGIONS = {name: [(left + 2) / 512, (right - 2) / 512,
                     1 - (bottom - 2) / 512, 1 - (top + 2) / 512]
              for name, (left, top, right, bottom) in UV_PIXELS.items()}
# Stock cylindrical island vertices 0..13 span .6410945 m and .260781 UV.
STOCK_UV_PER_METRE = .260781 / .6410945
DETAIL_PIXELS = {"service_hatch": [248, 255, 266, 275],
                 "warning": [235, 218, 254, 238],
                 "fastener": [254, 246, 257, 249]}
SECTION_JOINTS = (.23, .72, PROFILE["length"] * (1 - PROFILE["nose_fraction"]) - .013)
BAND_CENTRES = (.33, 1.00)
BAND_WIDTH = .04
TRIANGLE_BUDGET = 2106  # Three times the stock Scythe's 702 triangles.
ROOT_RADIUS = .006


def subtract(a, b):
    return tuple(x - y for x, y in zip(a, b))


def cross(a, b):
    return (a[1] * b[2] - a[2] * b[1], a[2] * b[0] - a[0] * b[2],
            a[0] * b[1] - a[1] * b[0])


def dot(a, b):
    return sum(x * y for x, y in zip(a, b))


def unit(v):
    length = math.sqrt(dot(v, v))
    if length < 1e-12:
        raise ValueError("zero normal or degenerate face")
    return tuple(x / length for x in v)


def atlas(region, u, v):
    left, right, bottom, top = UV_REGIONS[region]
    return (left + u * (right - left), bottom + v * (top - bottom))


def coating_uv(region, distance, z, middle):
    left, right, bottom, top = UV_REGIONS[region]
    return ((left + right) / 2 + distance * STOCK_UV_PER_METRE,
            (bottom + top) / 2 + (z - middle) * STOCK_UV_PER_METRE)


def nose_profile(nose_segments, tip_segments):
    radius, length = PROFILE["diameter"] / 2, PROFILE["length"]
    shoulder = length * (1 - PROFILE["nose_fraction"])
    nose_length = length - shoulder
    ogive_radius = (nose_length * nose_length + radius * radius) / (2 * radius)
    nose = []
    for i in range(nose_segments):
        x = nose_length * i / nose_segments
        r = math.sqrt(ogive_radius * ogive_radius - x * x) + radius - ogive_radius
        nose.append((shoulder + x, max(0, r)))
    cap_start = length - PROFILE["nose_tip_length"]
    cap_x = cap_start - shoulder
    cap_r = math.sqrt(ogive_radius * ogive_radius - cap_x * cap_x) + radius - ogive_radius
    cap_slope = -cap_x / math.sqrt(ogive_radius * ogive_radius - cap_x * cap_x)
    controls = [(cap_start, cap_r), (cap_start + .007, cap_r + cap_slope * .007),
                (length, .0008), (length, 0)]
    for i in range(tip_segments + 1):
        t = i / tip_segments
        weights = [(1 - t) ** 3, 3 * (1 - t) ** 2 * t, 3 * (1 - t) * t * t, t ** 3]
        nose.append(tuple(sum(weight * point[a] for weight, point in zip(weights, controls))
                          for a in range(2)))
    return nose


def stock_positions(path):
    """Read Unity's float position stream without Blender or numpy."""
    text = path.read_text(encoding="utf8")
    count = int(re.search(r"m_VertexCount: (\d+)", text)[1])
    data = bytes.fromhex(re.search(r"_typelessdata: ([0-9a-f]+)", text)[1])
    channels = [tuple(map(int, values)) for values in re.findall(
        r"- stream: (\d+)\s+offset: (\d+)\s+format: (\d+)\s+dimension: (\d+)", text)]
    sizes = {0: 4, 1: 2, 2: 1, 3: 1, 4: 2, 5: 2,
             6: 1, 7: 1, 8: 2, 9: 2, 10: 4, 11: 4}
    strides = {}
    for stream, offset, fmt, dimension in channels:
        if dimension:
            strides[stream] = max(strides.get(stream, 0), offset + sizes[fmt] * (dimension & 15))
    starts, cursor = {}, 0
    for stream in sorted(strides):
        strides[stream] = (strides[stream] + 3) // 4 * 4
        starts[stream] = cursor
        cursor = (cursor + strides[stream] * count + 15) // 16 * 16
    stream, offset, fmt, dimension = channels[0]
    if fmt != 0 or dimension != 3:
        raise ValueError("stock position format changed")
    return [struct.unpack_from("<3f", data, starts[stream] + offset + i * strides[stream])
            for i in range(count)]


class Mesh:
    def __init__(self, tail):
        self.tail = tail
        self.vertices, self.normals, self.uv, self.indices = [], [], [], []
        self.parts = {}

    def triangle(self, points, normals, uvs, part):
        geometric = cross(subtract(points[1], points[0]), subtract(points[2], points[0]))
        if dot(geometric, geometric) < 1e-18:
            raise ValueError(f"degenerate triangle in {part}")
        average = tuple(sum(n[a] for n in normals) for a in range(3))
        if dot(geometric, average) < 0:
            points = [points[0], points[2], points[1]]
            normals = [normals[0], normals[2], normals[1]]
            uvs = [uvs[0], uvs[2], uvs[1]]
        start = len(self.vertices)
        for point, normal, uv in zip(points, normals, uvs):
            self.vertices.append((point[0], point[1], point[2] + self.tail))
            self.normals.append(unit(normal))
            self.uv.append(uv)
        self.indices.extend((start, start + 1, start + 2))
        self.parts[part] = self.parts.get(part, 0) + 1

    def face(self, points, outward, region, part):
        normal = unit(cross(subtract(points[1], points[0]), subtract(points[2], points[0])))
        if dot(normal, outward) < 0:
            points = list(reversed(points))
            normal = tuple(-n for n in normal)
        longest = max((subtract(a, b) for a in points for b in points), key=lambda v: dot(v, v))
        vertical = unit(longest)
        horizontal = unit(cross(vertical, normal))
        local = [(dot(subtract(p, points[0]), horizontal),
                  dot(subtract(p, points[0]), vertical)) for p in points]
        ranges = [(min(p[a] for p in local), max(p[a] for p in local)) for a in range(2)]
        left, right, bottom, top = UV_REGIONS[region]
        scale = STOCK_UV_PER_METRE
        # Tile long faces rather than enlarging stock roughness texels into visible reflection blocks.
        width = (right - left) / scale * .95
        height = (top - bottom) / scale * .95
        columns = max(1, math.ceil((ranges[0][1] - ranges[0][0]) / width))
        rows = max(1, math.ceil((ranges[1][1] - ranges[1][0]) / height))
        origin = points[0]
        def clip(polygon, boundary, sign, basis):
            if not polygon:
                return []
            clipped = []
            previous = polygon[-1]
            previous_distance = sign * (dot(subtract(previous, origin), basis) - boundary)
            for point in polygon:
                distance = sign * (dot(subtract(point, origin), basis) - boundary)
                if (distance > 1e-12) != (previous_distance > 1e-12):
                    fraction = previous_distance / (previous_distance - distance)
                    clipped.append(tuple(a + fraction * (b - a) for a, b in zip(previous, point)))
                if distance <= 1e-12:
                    clipped.append(point)
                previous, previous_distance = point, distance
            return [p for i, p in enumerate(clipped)
                    if dot(subtract(p, clipped[i - 1]), subtract(p, clipped[i - 1])) > 1e-18]
        for row in range(rows):
            z0 = ranges[1][0] + row / rows * (ranges[1][1] - ranges[1][0])
            z1 = ranges[1][0] + (row + 1) / rows * (ranges[1][1] - ranges[1][0])
            strip = clip(clip(points, z0, -1, vertical), z1, 1, vertical)
            for column in range(columns):
                x0 = ranges[0][0] + column / columns * (ranges[0][1] - ranges[0][0])
                x1 = ranges[0][0] + (column + 1) / columns * (ranges[0][1] - ranges[0][0])
                polygon = clip(clip(strip, x0, -1, horizontal), x1, 1, horizontal)
                uvs = [((left + right) / 2 + (dot(subtract(p, origin), horizontal) - (x0 + x1) / 2) * scale,
                        (bottom + top) / 2 + (dot(subtract(p, origin), vertical) - (z0 + z1) / 2) * scale)
                       for p in polygon]
                for i in range(1, len(polygon) - 1):
                    self.triangle([polygon[0], polygon[i], polygon[i + 1]], [normal] * 3,
                                  [uvs[0], uvs[i], uvs[i + 1]], part)

    def lathe(self, rings, region, part, inward=False):
        segments = PROFILE["radial_segments"]
        for k in range(len(rings) - 1):
            z0, r0 = rings[k]
            z1, r1 = rings[k + 1]
            for i in range(segments):
                angles = [2 * math.pi * i / segments, 2 * math.pi * (i + 1) / segments]
                points, normals, uvs = [], [], []
                for z, r, angle, u, ring in [(z0, r0, angles[0], i / segments, k),
                                             (z0, r0, angles[1], (i + 1) / segments, k),
                                             (z1, r1, angles[1], (i + 1) / segments, k + 1),
                                             (z1, r1, angles[0], i / segments, k + 1)]:
                    points.append((r * math.cos(angle), r * math.sin(angle), z))
                    if r == 0:
                        n = (0, 0, 1)
                    elif abs(z1 - z0) < 1e-10:
                        n = (0, 0, -1 if r1 < r0 else 1)
                    else:
                        previous = rings[max(0, ring - 1)]
                        following = rings[min(len(rings) - 1, ring + 1)]
                        slope = ((following[1] - previous[1]) /
                                 max(following[0] - previous[0], 1e-10))
                        n = (math.cos(angle), math.sin(angle), -slope)
                    normals.append(tuple(-x for x in n) if inward else n)
                    if part == "nozzle_lip":
                        outer = max(r0, r1)
                        uv = atlas(region, .5 + r * math.cos(angle) / (2 * outer),
                                   .5 + r * math.sin(angle) / (2 * outer))
                    elif part in ("body", "ogive"):
                        sector = 2 if part == "body" else 4
                        middle_angle = 2 * math.pi * (i // sector * sector + sector / 2) / segments
                        uv = coating_uv(region, r * (angle - middle_angle), z, (z0 + z1) / 2)
                    else:
                        uv = atlas(region, u, (z - rings[0][0]) /
                                   max(rings[-1][0] - rings[0][0], 1e-8))
                    uvs.append(uv)
                for ids in ((0, 1, 2), (0, 2, 3)):
                    if (r0 == 0 and ids == (0, 1, 2)) or (r1 == 0 and ids == (0, 2, 3)):
                        continue
                    self.triangle([points[j] for j in ids], [normals[j] for j in ids],
                                  [uvs[j] for j in ids], part)

    def shell_patch(self, angle, centre, detail):
        left, top, right, bottom = DETAIL_PIXELS[detail]
        width = (right - left) / (512 * STOCK_UV_PER_METRE)
        height = (bottom - top) / (512 * STOCK_UV_PER_METRE)
        radius = PROFILE["diameter"] / 2 + .00012
        half_angle = width / (2 * radius)
        angles = {angle - half_angle, angle + half_angle}
        step = 2 * math.pi / PROFILE["radial_segments"]
        angles.update(i * step for i in range(math.floor((angle - half_angle) / step),
                                            math.ceil((angle + half_angle) / step) + 1)
                      if angle - half_angle < i * step < angle + half_angle)
        angles = sorted(angles)
        for first, last in zip(angles, angles[1:]):
            points, normals, uvs = [], [], []
            for theta, v in ((first, 0), (last, 0), (last, 1), (first, 1)):
                u = .5 + (theta - angle) * radius / width
                normal = (math.cos(theta), math.sin(theta), 0)
                middle = (math.floor(theta / step) + .5) * step
                shell = PROFILE["diameter"] / 2 * math.cos(step / 2) / math.cos(theta - middle)
                points.append(((shell + .00012) * normal[0], (shell + .00012) * normal[1],
                               centre + (v - .5) * height))
                normals.append(normal)
                uvs.append(((left + u * (right - left)) / 512,
                            1 - (bottom - v * (bottom - top)) / 512))
            for ids in ((0, 1, 2), (0, 2, 3)):
                self.triangle([points[j] for j in ids], [normals[j] for j in ids],
                              [uvs[j] for j in ids], detail)

    def fin_root(self, polygon, angle, thickness, z0, z1, part):
        radial = (math.cos(angle), math.sin(angle), 0)
        tangent = (-math.sin(angle), math.cos(angle), 0)
        radius = PROFILE["diameter"] / 2
        half = thickness / 2
        def outer(z):
            intersections = [r0 + (r1 - r0) * (z - a) / (b - a)
                             for (a, r0), (b, r1) in zip(polygon, polygon[1:] + polygon[:1])
                             if abs(b - a) > 1e-12 and min(a, b) <= z <= max(a, b)]
            return max(intersections)
        # The toe penetrates the faceted shell; the concave arc meets the fin
        # side with its exact normal, without a floating parallel shoe layer.
        start, end = min(z for z, _ in polygon), max(z for z, _ in polygon)
        rings = {start, end}
        for (a, r0), (b, r1) in zip(polygon, polygon[1:] + polygon[:1]):
            if r0 != r1:
                level = radius + ROOT_RADIUS / .65
                fraction = (level - r0) / (r1 - r0)
                z = a + fraction * (b - a)
                if 0 < fraction < 1 and abs(outer(z) - level) < 1e-10:
                    rings.add(z)
        rings = sorted(rings)
        maximum_length = (UV_REGIONS["body"][3] - UV_REGIONS["body"][2]) / STOCK_UV_PER_METRE * .95
        rings = sorted({a + (b - a) * i / max(1, math.ceil((b - a) / maximum_length))
                        for a, b in zip(rings, rings[1:])
                        for i in range(max(1, math.ceil((b - a) / maximum_length)) + 1)})
        for side in (-1, 1):
            profiles = []
            for z in rings:
                fillet = min(ROOT_RADIUS, max(.00005, (outer(z) - radius) * .65))
                t0 = half + fillet
                alpha = math.pi / PROFILE["radial_segments"]
                shell_radius = min(radius, outer(z) - .0001)
                r0 = (shell_radius * math.cos(alpha) + fillet - t0 * math.sin(alpha)) / math.cos(alpha)
                profile = []
                for fraction in (0, 1 / 6, 1 / 3, 2 / 3, 1):
                    theta = alpha + (math.pi / 2 - alpha) * fraction
                    r, t = r0 - fillet * math.cos(theta), t0 - fillet * math.sin(theta)
                    profile.append(((radial[0] * r + side * tangent[0] * t,
                                     radial[1] * r + side * tangent[1] * t, z),
                                    (radial[0] * math.cos(theta) + side * tangent[0] * math.sin(theta),
                                     radial[1] * math.cos(theta) + side * tangent[1] * math.sin(theta), 0),
                                    .0015 + fillet * (theta - alpha)))
                toe, normal, _ = profile[0]
                profile.insert(0, ((toe[0] * (radius - .0015) / radius,
                                    toe[1] * (radius - .0015) / radius, z),
                                   tuple(side * x for x in tangent), 0))
                profile[1] = (profile[1][0], unit((toe[0], toe[1], 0)), profile[1][2])
                profiles.append(profile)
            for i, profile in enumerate(profiles):
                previous, following = profiles[max(0, i - 1)], profiles[min(len(profiles) - 1, i + 1)]
                dz = following[0][0][2] - previous[0][0][2]
                for j in range(2, len(profile)):
                    point, normal, arc = profile[j]
                    slope = tuple((b - a) / dz for a, b in zip(previous[j][0][:2], following[j][0][:2]))
                    profile[j] = (point, unit((normal[0], normal[1], -dot(normal[:2], slope))), arc)
            for first, last in zip(profiles, profiles[1:]):
                middle = (first[0][0][2] + last[0][0][2]) / 2
                for j in range(len(first) - 1):
                    corners = (first[j], first[j + 1], last[j + 1], last[j])
                    uvs = [coating_uv("body", arc - .005, point[2], middle)
                           for point, _, arc in corners]
                    for ids in ((0, 1, 2), (0, 2, 3)):
                        self.triangle([corners[k][0] for k in ids], [corners[k][1] for k in ids],
                                      [uvs[k] for k in ids], part)
            for z in (z0 + .018, z1 - .018):
                centre = (radial[0] * .070 + tangent[0] * half * side,
                          radial[1] * .070 + tangent[1] * half * side, z)
                self.fastener(centre, radial, tangent, side)

    def fastener(self, centre, radial, tangent, side):
        normal = tuple(side * x for x in tangent)
        radius, height = .003, .00012
        points = [(centre[0] + radial[0] * radius * math.cos(theta) + normal[0] * height,
                   centre[1] + radial[1] * radius * math.cos(theta) + normal[1] * height,
                   centre[2] + radius * math.sin(theta))
                  for theta in (i * math.pi / 2 for i in range(4))]
        left, top, right, bottom = DETAIL_PIXELS["fastener"]
        uvs = [((left + right) / 1024 + .003 * math.cos(i * math.pi / 2) * STOCK_UV_PER_METRE,
                1 - (top + bottom) / 1024 + .003 * math.sin(i * math.pi / 2) * STOCK_UV_PER_METRE)
               for i in range(4)]
        for i in range(1, 3):
            ids = (0, i, i + 1)
            self.triangle([points[j] for j in ids], [normal] * 3,
                          [uvs[j] for j in ids], "fin_fasteners")

    def nozzle_rib(self, angle):
        radial = (math.cos(angle), math.sin(angle), 0)
        tangent = (-math.sin(angle), math.cos(angle), 0)
        polygon = [(.013, .0435), (.018, .041), (.057, .032), (.057, .03375)]
        layers = [[(radial[0] * r + tangent[0] * side * .001,
                    radial[1] * r + tangent[1] * side * .001, z)
                   for z, r in polygon] for side in (-1, 1)]
        for side, layer in zip((-1, 1), layers):
            self.face(layer, tuple(side * x for x in tangent), "nozzle", "nozzle_ribs")
        for i in range(4):
            j = (i + 1) % 4
            dz, dr = polygon[j][0] - polygon[i][0], polygon[j][1] - polygon[i][1]
            self.face([layers[0][i], layers[0][j], layers[1][j], layers[1][i]],
                      (radial[0] * dz, radial[1] * dz, -dr), "nozzle", "nozzle_ribs")

    def fin(self, polygon, angle, thickness, part):
        radial = (math.cos(angle), math.sin(angle), 0)
        tangent = (-math.sin(angle), math.cos(angle), 0)
        layers = [[(radial[0] * r + tangent[0] * side * thickness / 2,
                    radial[1] * r + tangent[1] * side * thickness / 2, z)
                   for z, r in polygon] for side in (-1, 1)]
        region = "strake" if part == "strakes" else "fin"
        self.face(layers[0], tuple(-v for v in tangent), region, part)
        self.face(layers[1], tangent, region, part)
        for i in range(len(polygon)):
            j = (i + 1) % len(polygon)
            dz, dr = polygon[j][0] - polygon[i][0], polygon[j][1] - polygon[i][1]
            outward = (radial[0] * dz, radial[1] * dz, -dr)
            self.face([layers[0][i], layers[0][j], layers[1][j], layers[1][i]],
                      outward, region, part)

    def check(self, envelope):
        if len(self.indices) // 3 > TRIANGLE_BUDGET:
            raise ValueError(f"triangle budget exceeded: {len(self.indices) // 3} > {TRIANGLE_BUDGET}")
        if len(self.vertices) != len(self.normals) or len(self.vertices) != len(self.uv):
            raise ValueError("vertex attribute counts disagree")
        for values in (self.vertices, self.normals, self.uv):
            if not all(math.isfinite(x) for row in values for x in row):
                raise ValueError("nonfinite vertex attribute")
        if len(self.indices) % 3 or any(i < 0 or i >= len(self.vertices) for i in self.indices):
            raise ValueError("invalid triangle indices")
        for i in range(0, len(self.indices), 3):
            ids = self.indices[i:i + 3]
            points = [self.vertices[j] for j in ids]
            normal = cross(subtract(points[1], points[0]), subtract(points[2], points[0]))
            average = tuple(sum(self.normals[j][a] for j in ids) for a in range(3))
            if dot(normal, normal) < 1e-18 or dot(normal, average) <= 0:
                raise ValueError("degenerate or inward triangle")
            uv = [self.uv[j] for j in ids]
            area = ((uv[1][0] - uv[0][0]) * (uv[2][1] - uv[0][1]) -
                    (uv[1][1] - uv[0][1]) * (uv[2][0] - uv[0][0]))
            if abs(area) < 1e-12:
                raise ValueError(f"zero-area UV triangle {i // 3}")
        bounds = [[min(p[a] for p in self.vertices), max(p[a] for p in self.vertices)]
                  for a in range(3)]
        if any(bounds[a][0] < envelope[a][0] - 1e-6 or
               bounds[a][1] > envelope[a][1] + 1e-6 for a in range(3)):
            raise ValueError(f"mesh exceeds half-Scythe envelope: {bounds}")
        if abs(bounds[2][1] - bounds[2][0] - PROFILE["length"]) > 1e-6:
            raise ValueError("missile length changed")
        return bounds

    def weld(self, envelope):
        welded = Mesh(0)
        welded.parts = self.parts.copy()
        lookup = {}
        for index in self.indices:
            key = tuple(round(x, 9) for values in (self.vertices[index], self.normals[index], self.uv[index])
                        for x in values)
            if key not in lookup:
                lookup[key] = len(welded.vertices)
                welded.vertices.append(key[:3])
                welded.normals.append(key[3:6])
                welded.uv.append(key[6:])
            welded.indices.append(lookup[key])
        for original, reused in zip(self.indices, welded.indices):
            for name in ("vertices", "normals", "uv"):
                if tuple(round(x, 9) for x in getattr(self, name)[original]) != getattr(welded, name)[reused]:
                    raise ValueError("welding changed an expanded triangle attribute")
        if len(welded.vertices) > 65535:
            raise ValueError("welded mesh does not fit 16-bit indices")
        welded.check(envelope)
        return welded


def build(stock):
    positions = stock_positions(stock / "Mesh/AAM2_PLACEHOLDER.asset")
    envelope = [[min(p[a] for p in positions), max(p[a] for p in positions)]
                for a in range(3)]
    envelope[2] = [z / 2 for z in envelope[2]]
    centre_z = sum(envelope[2]) / 2
    mesh = Mesh(centre_z - PROFILE["length"] / 2)
    radius, length = PROFILE["diameter"] / 2, PROFILE["length"]
    shoulder = length * (1 - PROFILE["nose_fraction"])
    body = [(0, radius * .94), (.015, radius), (.48, radius), (shoulder, radius)]
    for centre in SECTION_JOINTS:
        body.extend([(centre - .002, radius), (centre - .001, radius - .0008),
                     (centre + .001, radius - .0008), (centre + .002, radius)])
    mesh.lathe(sorted(body), "body", "body")
    nose = nose_profile(PROFILE["nose_segments"], 3)
    mesh.lathe(nose, "nose", "ogive")
    mesh.lathe([(0, radius * .94), (0, .045)], "metal", "nozzle_lip")
    mesh.lathe([(0, .045), (.003, .045), (.005, .043), (.009, .043),
                (.012, .044), (.068, .031)], "nozzle", "nozzle", inward=True)
    mesh.lathe([(.068, .031), (.070, .026), (.074, .026), (.075, 0)],
               "nozzle", "nozzle", inward=True)
    for i in range(8):
        mesh.nozzle_rib(i * math.pi / 4)
    strake = [(.29, .059), (.35, PROFILE["strake_radius"]),
              (shoulder - .045, PROFILE["strake_radius"]), (shoulder + .025, .059)]
    fin = [(.032, .057), (.042, PROFILE["fin_span"] / 2),
           (.19, PROFILE["fin_span"] / 2), (.305, .059)]
    for i in range(4):
        angle = math.pi / 4 + i * math.pi / 2
        mesh.fin(strake, angle, PROFILE["strake_thickness"], "strakes")
        mesh.fin(fin, angle, PROFILE["fin_thickness"], "tail_fins")
        mesh.fin_root(strake, angle, PROFILE["strake_thickness"], .385, shoulder - .085, "strake_roots")
        mesh.fin_root(fin, angle, PROFILE["fin_thickness"], .065, .235, "tail_fin_roots")
    for i in range(4):
        mesh.shell_patch(i * math.pi / 2, .49 if i % 2 else .85, "service_hatch")
    for angle in (0, math.pi):
        mesh.shell_patch(angle, .625, "warning")
    for centre in BAND_CENTRES:
        mesh.lathe([(centre - BAND_WIDTH / 2, radius + .0002),
                    (centre + BAND_WIDTH / 2, radius + .0002)], "nose", "blue_bands")
    return mesh, mesh.check(envelope), envelope, centre_z, nose, strake, fin


def write_icon(output, nose, strake, fin):
    try:
        from PIL import Image, ImageDraw
    except ImportError as error:
        raise RuntimeError("Pillow is required to generate the matching Karambit HUD icon") from error
    scale = 4
    image = Image.new("RGBA", (512 * scale, 256 * scale))
    draw = ImageDraw.Draw(image)
    def points(profile, sign=1):
        return [((24 + z / PROFILE["length"] * 464) * scale,
                 (128 - sign * r / PROFILE["length"] * 464) * scale) for z, r in profile]
    # HUD sampling is independent of the render mesh's polygon budget.
    nose = nose_profile(22, 6)
    body = [(0, .0564), (.015, .06)] + nose
    outline = points(body) + list(reversed(points(body, -1)))
    draw.line(outline + outline[:1], fill="white", width=6, joint="curve")
    for profile in (strake, fin):
        for sign in (-1, 1):
            polygon = points(profile, sign)
            draw.line(polygon + polygon[:1], fill="white", width=6, joint="curve")
    image.resize((512, 256), Image.Resampling.LANCZOS).save(output / "karambit-icon.png")
    return True


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--output", type=Path, default=ROOT / ".local/karambit-art")
    parser.add_argument("--assets", type=Path, default=STOCK)
    args = parser.parse_args()
    material = args.assets / "Material/Weapons4_PLACEHOLDER.mat"
    material_guid = re.search(r"^guid: ([0-9a-f]{32})$",
                              material.with_suffix(".mat.meta").read_text(encoding="utf8"), re.MULTILINE)
    if material_guid is None:
        raise ValueError("Scythe material has no valid Unity GUID")
    mesh, bounds, envelope, centre, nose, strake, fin = build(args.assets)
    expanded_vertices = len(mesh.vertices)
    mesh = mesh.weld(envelope)
    flat = lambda values: [round(x, 9) for row in values for x in row]
    model = {"name": "karambit", "vertices": flat(mesh.vertices),
             "normals": flat(mesh.normals), "uv": flat(mesh.uv),
             "submeshes": [{"indices": mesh.indices[:-mesh.parts["blue_bands"] * 3]},
                           {"indices": mesh.indices[-mesh.parts["blue_bands"] * 3:]}]}
    payload = json.dumps(model, separators=(",", ":")) + "\n"
    args.output.mkdir(parents=True, exist_ok=True)
    (args.output / "karambit.json").write_text(payload, encoding="utf8", newline="\n")
    metadata = {"profile": PROFILE, "axes": "Unity X right, Y up, Z nose forward",
                "bounds": bounds, "half_scythe_envelope": envelope, "centre_z": centre,
                "material": "Weapons4", "material_guid": material_guid[1],
                "base_map": "weapons4_b_PLACEHOLDER.png", "uv_regions": UV_REGIONS,
                "uv_pixels": UV_PIXELS, "stock_uv_per_metre": STOCK_UV_PER_METRE,
                "detail_pixels": DETAIL_PIXELS, "detail_texels_per_metre": 512 * STOCK_UV_PER_METRE,
                "section_joints": {"centres_from_tail": SECTION_JOINTS, "width": .004, "depth": .0008},
                "fin_roots": {"fillet_radius": ROOT_RADIUS, "embedded_toe_depth": .0015,
                              "curve_segments": 4, "first_contact_turn_degrees": 6.5625,
                              "fastener_diameter": .006,
                              "fastener_surface_offset": .00012},
                "nozzle": {"lip_steps": 2, "internal_ribs": 8, "rib_width": .002},
                "blue_bands": {"centres_from_tail": BAND_CENTRES, "width": BAND_WIDTH,
                               "surface_offset": .0002, "submesh": 1},
                "vertices": len(mesh.vertices), "triangles": len(mesh.indices) // 3,
                "triangle_budget": TRIANGLE_BUDGET, "expanded_vertices": expanded_vertices,
                "exact_attribute_weld_roundtrip": True, "index_bits": 16,
                "parts": mesh.parts, "sha256": hashlib.sha256(payload.encode()).hexdigest()}
    metadata["icon"] = write_icon(args.output, nose, strake, fin)
    (args.output / "karambit.metadata.json").write_text(
        json.dumps(metadata, indent=2) + "\n", encoding="utf8", newline="\n")
    lines = ["# SAAM-18 Karambit; metres; +Z nose; stock Weapons4 atlas", "o karambit"]
    lines.extend("v " + " ".join(f"{x:.9f}" for x in row) for row in mesh.vertices)
    lines.extend("vt " + " ".join(f"{x:.9f}" for x in row) for row in mesh.uv)
    lines.extend("vn " + " ".join(f"{x:.9f}" for x in row) for row in mesh.normals)
    for i in range(0, len(mesh.indices), 3):
        lines.append("f " + " ".join(f"{j + 1}/{j + 1}/{j + 1}" for j in mesh.indices[i:i + 3]))
    (args.output / "karambit.obj").write_text("\n".join(lines) + "\n", encoding="utf8", newline="\n")
    print(json.dumps(metadata, indent=2))
    print(f"Exported {args.output.resolve()}")


if __name__ == "__main__":
    main()
