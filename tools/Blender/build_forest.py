# Map "Forest": an 80 x 80 m woodland airsoft field (the same playable size as Town).
#
#   Red camp in the north clearing, Blue camp in the south clearing (tents, sandbag walls, crates).
#   A dry creek winds east-west through the middle (a footbridge in the centre), two log cabins stand
#   either side of it (west cabin north of the creek, east cabin south of it). Rolling ground, ~130 trees
#   with colliding trunks, bushes for concealment, rocks and fallen logs for cover. Everything is point
#   symmetric around the centre, so both teams get the same routes.
#
# Run:  Blender -b --python build_forest.py -- <out.fbx> <out.blend> [render_dir]
# Coordinates: x = east, y = north (metres, centre = 0, 0); playable area -40..40 on both axes.
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import airsoft_bpy as A  # noqa: E402

HALF = 40.0          # playable half size
OUTER = 56.0         # terrain half size (the band outside the play area is scenery)
CAMP = (0.0, 32.0)   # Red camp (Blue = point mirror)
CABIN = (-14.0, 8.0)  # west cabin (east cabin = point mirror)
CABIN_YAW = 8.0


def sym(p):
    return (-p[0], -p[1])


def smoothstep(e0, e1, x):
    t = max(0.0, min(1.0, (x - e0) / (e1 - e0)))
    return t * t * (3 - 2 * t)


# ───────────────────────── terrain height ─────────────────────────
def _raw(x, y):
    return (0.55 * math.sin(x * 0.105 + 0.7) * math.cos(y * 0.085 - 0.3)
            + 0.30 * math.sin(x * 0.21 + y * 0.16 + 1.1)
            + 0.22 * math.cos(x * 0.05 - y * 0.29 + 0.4)
            + 0.12 * math.sin(x * 0.47 - y * 0.41))


def creek_y(x):
    return 4.0 * math.sin(x * math.pi / 40.0)


def creek_dist(x, y):
    slope = 4.0 * math.pi / 40.0 * math.cos(x * math.pi / 40.0)
    return abs(y - creek_y(x)) / math.sqrt(1 + slope * slope)


FLAT = []   # (cx, cy, radius, falloff)


def base_height(x, y):
    h = 0.5 * (_raw(x, y) + _raw(-x, -y))          # point symmetric
    # berm and hills outside the play area
    out = max(abs(x), abs(y)) - (HALF - 1.0)
    if out > 0:
        h += 0.10 * out ** 1.6
    return h


def height(x, y):
    h = base_height(x, y)
    for cx, cy, r, f in FLAT:
        d = math.hypot(x - cx, y - cy)
        w = 1.0 - smoothstep(r, r + f, d)
        if w > 0:
            h = h * (1 - w) + base_height(cx, cy) * w
    # dry creek bed: 0.9 m deep, ~3 m wide at the bottom (only inside the play area)
    if abs(x) < HALF + 6:
        d = creek_dist(x, y)
        h -= 0.9 * (1.0 - smoothstep(1.4, 3.6, d))
    return h


def init_flats():
    FLAT.clear()
    for p in (CAMP, sym(CAMP)):
        FLAT.append((p[0], p[1], 8.5, 4.0))
    for p in (CABIN, sym(CABIN)):
        FLAT.append((p[0], p[1], 5.0, 3.0))


# ───────────────────────── ground ─────────────────────────
def build_ground():
    grass = A.mat("ForestGround", (0.27, 0.33, 0.17), 1.0)
    path = A.mat("DirtPath", (0.40, 0.33, 0.23), 1.0)
    creek = A.mat("CreekSandGravel", (0.52, 0.47, 0.38), 1.0)
    bm = bmesh.new()
    step = 1.0
    n = int(2 * OUTER / step)
    verts = []
    for j in range(n + 1):
        row = []
        y = -OUTER + j * step
        for i in range(n + 1):
            x = -OUTER + i * step
            row.append(bm.verts.new((x, y, height(x, y))))
        verts.append(row)
    me_mat = []
    for j in range(n):
        for i in range(n):
            f = bm.faces.new((verts[j][i], verts[j][i + 1], verts[j + 1][i + 1], verts[j + 1][i]))
            cx = -OUTER + (i + 0.5) * step
            cy = -OUTER + (j + 0.5) * step
            if abs(cx) < HALF + 4 and creek_dist(cx, cy) < 2.6:
                f.material_index = 2
            elif on_path(cx, cy):
                f.material_index = 1
    ob = A.obj_from_bmesh("Ground_Forest", bm, grass, A.collection("Ground"))
    ob.data.materials.append(path)
    ob.data.materials.append(creek)
    return ob


# trails: camp -> each cabin -> bridge (and the mirror)
PATHS = []


def init_paths():
    PATHS.clear()
    west = [(CAMP[0] - 2, CAMP[1] - 6), (-8, 18), CABIN, (-6, 3), (0, 0)]
    east = [(CAMP[0] + 3, CAMP[1] - 6), (10, 20), (16, 10), (15, 2)]
    for line in (west, east):
        PATHS.append(line)
        PATHS.append([sym(p) for p in line])


def _seg_dist(p, a, b):
    ax, ay = a
    bx, by = b
    dx, dy = bx - ax, by - ay
    t = max(0.0, min(1.0, ((p[0] - ax) * dx + (p[1] - ay) * dy) / (dx * dx + dy * dy)))
    return math.hypot(p[0] - (ax + t * dx), p[1] - (ay + t * dy))


def on_path(x, y, width=0.9):
    for line in PATHS:
        for a, b in zip(line, line[1:]):
            if _seg_dist((x, y), a, b) < width:
                return True
    return False


# ───────────────────────── bmesh shape helpers (into a shared mesh) ─────────────────────────
def add_cyl(bm, base, r0, r1, h, segs=8, tilt=None):
    m = Matrix.Translation(Vector(base) + Vector((0, 0, h / 2)))
    if tilt is not None:
        m = Matrix.Translation(Vector(base)) @ tilt @ Matrix.Translation((0, 0, h / 2))
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segs, radius1=r0, radius2=r1, depth=h, matrix=m)


def add_blob(bm, center, radii, rnd, subdiv=1, jitter=0.18):
    m = Matrix.Translation(Vector(center)) @ Matrix.Diagonal(Vector((radii[0], radii[1], radii[2], 1.0)))
    res = bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=1.0, matrix=m)
    for v in res["verts"]:
        v.co += Vector((rnd.uniform(-1, 1), rnd.uniform(-1, 1), rnd.uniform(-1, 1))) * jitter * min(radii)


# ───────────────────────── trees, bushes, rocks, logs ─────────────────────────
def build_vegetation():
    rnd = random.Random(20261111)
    coll = A.collection("Forest")
    trunks, needles, leaves, bushes, rocks, logs = (bmesh.new() for _ in range(6))
    far_trunks, far_needles, far_leaves = bmesh.new(), bmesh.new(), bmesh.new()

    keep_clear = [(CAMP, 11.0), (sym(CAMP), 11.0), (CABIN, 6.5), (sym(CABIN), 6.5), ((0, 0), 4.5)]
    keep_clear += [(p, 2.2) for p in SPAWN_FFA + [sym(p) for p in SPAWN_FFA]]
    keep_clear += [(p, 2.5) for p in POINTS.values()]

    def clear_ok(p, min_d):
        if creek_dist(*p) < 2.4 or on_path(p[0], p[1], 1.6):
            return False
        for c, r in keep_clear:
            if math.hypot(p[0] - c[0], p[1] - c[1]) < r:
                return False
        for q in placed:
            if math.hypot(p[0] - q[0], p[1] - q[1]) < min_d:
                return False
        return True

    placed = []
    # trees in the play area: half of them sampled in the north half, mirrored to the south
    tries = 0
    while len(placed) < 132 and tries < 20000:
        tries += 1
        p = (rnd.uniform(-HALF + 1.5, HALF - 1.5), rnd.uniform(-1.0, HALF - 1.5))
        if not clear_ok(p, 4.6) or math.hypot(p[0] - sym(p)[0], p[1] - sym(p)[1]) < 4.6:
            continue
        placed += [p, sym(p)]
    for k, p in enumerate(placed):
        kind = (k // 2) % 3   # the mirrored pair share a kind
        tree(p, kind, rnd, trunks, needles, leaves)

    # scenery band outside the play area (no colliders: invisible walls keep players in)
    for _ in range(420):
        x, y = rnd.uniform(-OUTER + 1, OUTER - 1), rnd.uniform(-OUTER + 1, OUTER - 1)
        if max(abs(x), abs(y)) < HALF + 1.5:
            continue
        tree((x, y), rnd.randrange(3), rnd, far_trunks, far_needles, far_leaves, scale=rnd.uniform(1.0, 1.35))

    # bushes (concealment only), rocks and fallen logs (cover), mirrored pairs
    def scatter(count, min_d, fn):
        made, t = 0, 0
        while made < count and t < 6000:
            t += 1
            p = (rnd.uniform(-HALF + 2, HALF - 2), rnd.uniform(-HALF + 2, HALF - 2))
            if not clear_ok(p, min_d):
                continue
            seed = rnd.random()
            fn(p, seed, 1)
            fn(sym(p), seed, -1)
            placed.extend([p, sym(p)])
            made += 1

    def bush(p, seed, s):
        r = random.Random(int(seed * 1e6))
        z = height(*p)
        for _ in range(r.randint(2, 4)):
            off = (r.uniform(-0.7, 0.7), r.uniform(-0.7, 0.7))
            rad = r.uniform(0.6, 1.0)
            add_blob(bushes, (p[0] + off[0], p[1] + off[1], z + rad * 0.55), (rad, rad, rad * 0.8), r)

    def rock(p, seed, s):
        r = random.Random(int(seed * 1e6))
        z = height(*p)
        sx, sy, sz = r.uniform(0.9, 1.6), r.uniform(0.7, 1.3), r.uniform(0.7, 1.2)
        add_blob(rocks, (p[0], p[1], z + sz * 0.45), (sx, sy, sz), r, 1, 0.25)

    def log(p, seed, s):
        r = random.Random(int(seed * 1e6))
        L = r.uniform(4.0, 6.5)
        rad = r.uniform(0.32, 0.45)
        yaw = r.uniform(0, math.pi) + (math.pi if s < 0 else 0)
        z = min(height(p[0] + math.cos(yaw) * L / 2, p[1] + math.sin(yaw) * L / 2),
                height(p[0] - math.cos(yaw) * L / 2, p[1] - math.sin(yaw) * L / 2), height(*p))
        tilt = Matrix.Rotation(yaw, 4, "Z") @ Matrix.Rotation(math.pi / 2, 4, "Y")
        m = Matrix.Translation((p[0], p[1], z + rad * 0.8)) @ tilt
        bmesh.ops.create_cone(logs, cap_ends=True, cap_tris=False, segments=9, radius1=rad, radius2=rad * 0.85,
                              depth=L, matrix=m)

    scatter(46, 2.5, bush)
    scatter(12, 3.5, rock)
    scatter(9, 4.0, log)

    # rocks along the creek bed (cover while crossing)
    for x in (-30, -21, -9, 6, 17, 27):
        p = (x + 0.7, creek_y(x) + 0.4)
        rock(p, (x + 50) / 130.0, 1)
        rock(sym(p), (x + 50) / 130.0, -1)

    A.obj_from_bmesh("TreeTrunks", trunks, A.mat("TreeTrunkBark", (0.30, 0.22, 0.15)), coll)
    A.obj_from_bmesh("MMH_PineNeedles", needles, A.mat("PineLeaves", (0.13, 0.25, 0.13)), coll)
    A.obj_from_bmesh("MMH_TreeLeaves", leaves, A.mat("TreeLeaves", (0.24, 0.38, 0.15)), coll)
    A.obj_from_bmesh("VIS_Bushes", bushes, A.mat("BushLeaves", (0.22, 0.36, 0.14)), coll)
    A.obj_from_bmesh("Rocks", rocks, A.mat("RockGranite", (0.47, 0.47, 0.45)), coll)
    A.obj_from_bmesh("FallenLogs", logs, A.mat("LogWood", (0.36, 0.27, 0.18)), coll)
    far = A.collection("Scenery")
    A.obj_from_bmesh("VIS_FarTrunks", far_trunks, A.mat("TreeTrunkBark", (0.30, 0.22, 0.15)), far)
    A.obj_from_bmesh("MMH_FarNeedles", far_needles, A.mat("PineLeaves", (0.13, 0.25, 0.13)), far)
    A.obj_from_bmesh("MMH_FarLeaves", far_leaves, A.mat("TreeLeaves", (0.24, 0.38, 0.15)), far)


def tree(p, kind, rnd, trunks, needles, leaves, scale=1.0):
    z = height(*p) - 0.2
    if kind < 2:   # pine: tall trunk, stacked cones from ~2.4 m up
        h = rnd.uniform(9.0, 13.0) * scale
        r = rnd.uniform(0.22, 0.32) * scale
        add_cyl(trunks, (p[0], p[1], z), r, r * 0.45, h, 7)
        base = rnd.uniform(2.4, 3.2)
        tiers = 4
        for t in range(tiers):
            f = t / tiers
            cz = z + base + (h - base) * f * 0.92
            cr = (2.3 - 1.6 * f) * scale * rnd.uniform(0.9, 1.1)
            ch = (h - base) / tiers * 1.55
            add_cyl(needles, (p[0], p[1], cz), cr, 0.05, ch, 8)
    else:          # broadleaf: trunk + a few blobs
        h = rnd.uniform(5.5, 7.5) * scale
        r = rnd.uniform(0.25, 0.36) * scale
        add_cyl(trunks, (p[0], p[1], z), r, r * 0.6, h, 7)
        for _ in range(3):
            off = Vector((rnd.uniform(-1.2, 1.2), rnd.uniform(-1.2, 1.2), rnd.uniform(-0.4, 0.8)))
            rad = rnd.uniform(1.7, 2.4) * scale
            add_blob(leaves, Vector((p[0], p[1], z + h)) + off, (rad, rad, rad * 0.8), rnd)


# ───────────────────────── cabins ─────────────────────────
def local(c, yaw_deg, x, y):
    a = math.radians(yaw_deg)
    return (c[0] + x * math.cos(a) - y * math.sin(a), c[1] + x * math.sin(a) + y * math.cos(a))


def wall_with_gaps(name, c, yaw, a, b, z0, h, t, gaps, m, coll):
    """Wall from local a to b (2D), with gaps [(at, width, bottom, top)] measured from a."""
    ax, ay = a
    bx, by = b
    L = math.hypot(bx - ax, by - ay)
    ux, uy = (bx - ax) / L, (by - ay) / L

    def pt(s):
        return local(c, yaw, ax + ux * s, ay + uy * s)

    cuts = sorted(gaps)
    s = 0.0
    for at, w, g0, g1 in cuts:
        s0, s1 = at - w / 2, at + w / 2
        if s0 > s + 0.01:
            A.segment_box(name, pt(s), pt(s0), t, h, m, z0, coll)
        if g0 > 0.01:
            A.segment_box(name, pt(s0), pt(s1), t, g0, m, z0, coll)
        if g1 < h - 0.01:
            A.segment_box(name, pt(s0), pt(s1), t, h - g1, m, z0 + g1, coll)
        s = s1
    if L > s + 0.01:
        A.segment_box(name, pt(s), pt(L), t, h, m, z0, coll)


def build_cabin(c, yaw, tag):
    coll = A.collection("Cabins")
    logs = A.mat("LogWallWood", (0.46, 0.33, 0.21))
    floor_m = A.mat("FloorPlankWood", (0.40, 0.30, 0.20))
    roof = A.mat("RoofShingles", (0.25, 0.20, 0.17))
    frame = A.mat("FrameWood", (0.30, 0.22, 0.15))
    W, D, H, T = 7.0, 5.0, 2.8, 0.22
    g = base_height(*c)
    fz = g + 0.18
    # floor slab (sinks into the slope a little)
    A.box("CabinFloorWood" + tag, (c[0], c[1], g - 0.5), (W, D, fz - g + 0.5), floor_m, yaw, coll)
    hx, hy = W / 2 - T / 2, D / 2 - T / 2
    door_s, door_n = 2.2, 4.8          # doorways along the south / north wall, from its west end
    # a step under each doorway
    for x, sgn in ((-hx + door_s, -1), (-hx + door_n, 1)):
        p = local(c, yaw, x, sgn * (D / 2 + 0.5))
        A.box("CabinStepWood" + tag, (p[0], p[1], g - 0.4), (1.9, 1.0, 0.49), floor_m, yaw, coll)
    # long walls: a doorway each (offset so they don't line up) + a window
    wall_with_gaps("CabinWallWood" + tag, c, yaw, (-hx, -hy), (hx, -hy), fz, H, T,
                   [(door_s, 1.7, 0.0, 2.3), (5.4, 1.1, 1.0, 1.9)], logs, coll)
    wall_with_gaps("CabinWallWood" + tag, c, yaw, (-hx, hy), (hx, hy), fz, H, T,
                   [(1.6, 1.1, 1.0, 1.9), (door_n, 1.7, 0.0, 2.3)], logs, coll)
    # short walls: one window each
    wall_with_gaps("CabinWallWood" + tag, c, yaw, (-hx, -hy + T / 2), (-hx, hy - T / 2), fz, H, T,
                   [(2.3, 1.1, 1.0, 1.9)], logs, coll)
    wall_with_gaps("CabinWallWood" + tag, c, yaw, (hx, -hy + T / 2), (hx, hy - T / 2), fz, H, T,
                   [(2.3, 1.1, 1.0, 1.9)], logs, coll)
    # gable roof (hidden on the minimap)
    bm = bmesh.new()
    ov = 0.5
    pts = [(-W / 2 - ov, -D / 2 - ov, 0.0), (W / 2 + ov, -D / 2 - ov, 0.0), (W / 2 + ov, 0, 1.5), (-W / 2 - ov, 0, 1.5),
           (-W / 2 - ov, D / 2 + ov, 0.0), (W / 2 + ov, D / 2 + ov, 0.0)]
    v = []
    for x, y, z in pts:
        q = local(c, yaw, x, y)
        v.append(bm.verts.new((q[0], q[1], fz + H + z)))
    bm.faces.new((v[0], v[1], v[2], v[3]))
    bm.faces.new((v[3], v[2], v[5], v[4]))
    # gables
    for sx in (-1, 1):
        q0 = local(c, yaw, sx * W / 2, -D / 2)
        q1 = local(c, yaw, sx * W / 2, D / 2)
        q2 = local(c, yaw, sx * W / 2, 0)
        a = [bm.verts.new((q0[0], q0[1], fz + H)), bm.verts.new((q1[0], q1[1], fz + H)), bm.verts.new((q2[0], q2[1], fz + H + 1.5))]
        bm.faces.new(a)
    A.obj_from_bmesh("MMH_CabinRoof" + tag, bm, roof, coll)
    # inside: a table and some crates (cover)
    p = local(c, yaw, -1.6, 0.6)
    A.box("CabinTableWood" + tag, (p[0], p[1], fz), (1.6, 0.9, 0.78), frame, yaw + 4, coll)
    p = local(c, yaw, 2.2, -0.9)
    A.box("CrateCabin" + tag, (p[0], p[1], fz), (1.1, 1.1, 0.9), A.mat("WoodCrate", (0.55, 0.40, 0.24)), yaw - 12, coll, 0.03)
    # woodpile against the outside of the west/east short wall
    q = local(c, yaw, -W / 2 - 0.7, 0.0)
    A.box("WoodpileLog" + tag, (q[0], q[1], g), (1.0, 3.0, 1.2), A.mat("LogWood", (0.36, 0.27, 0.18)), yaw, coll, 0.05)


# ───────────────────────── camps ─────────────────────────
def build_camp(c, team, facing):
    """facing = direction of the forest (+1 north, -1 south) from the camp centre; local +y points at it."""
    coll = A.collection("Camps")
    bags = A.mat("SandBags", (0.62, 0.55, 0.40), 0.95)
    tent = A.mat("ClothTentOlive", (0.33, 0.37, 0.24))
    crate = A.mat("WoodCrate", (0.55, 0.40, 0.24))
    colour = A.mat("FlagRed", (0.80, 0.18, 0.15)) if team == 1 else A.mat("FlagBlue", (0.18, 0.38, 0.82))
    metal = A.mat("MetalPole", (0.35, 0.35, 0.35), 0.5, 0.6)
    s = facing
    g = base_height(*c)

    def P(x, y):
        return (c[0] + x * s, c[1] + y * s)   # local frame flipped for the south camp (point mirror)

    # sandbag line towards the forest, with two gaps
    for a, b in (((-9.0, 4.5), (-3.2, 6.0)), ((-1.2, 6.2), (3.6, 6.2)), ((5.6, 5.8), (9.5, 4.0))):
        A.segment_box("SandBagWall", P(*a), P(*b), 0.7, 1.1, bags, g - 0.1, coll, 0.1)
    # three tents at the back
    for x, yaw in ((-5.5, 0), (0.0, 0), (5.5, 0)):
        p = P(x, -4.0)
        _tent(coll, p, tent, yaw + (180 if s < 0 else 0), g)
    # crates and a flag
    for x, y in ((-7.5, 0.5), (7.0, -0.5), (2.6, 1.8)):
        p = P(x, y)
        A.box("CrateCamp", (p[0], p[1], g), (1.3, 1.3, 1.0), crate, 8 * s, coll, 0.03)
    p = P(-2.6, 1.6)
    A.box("CrateCamp", (p[0], p[1], g), (1.3, 1.3, 1.0), crate, -6 * s, coll, 0.03)
    A.box("CrateCamp", (p[0] + 0.1, p[1], g + 1.0), (1.1, 1.1, 0.9), crate, 10 * s, coll, 0.03)
    fp = P(0.0, -0.8)
    A.cylinder("VIS_FlagPole", (fp[0], fp[1], g), 0.06, 6.0, metal, 6, None, coll)
    A.box("VIS_Flag", (fp[0] + 0.75, fp[1], g + 4.9), (1.5, 0.04, 0.95), colour, 0, coll)
    # campfire ring (visual)
    cf = P(0.0, 2.6)
    for k in range(8):
        a = k * math.pi / 4
        A.box("VIS_FireStone", (cf[0] + math.cos(a) * 0.7, cf[1] + math.sin(a) * 0.7, g - 0.05), (0.3, 0.25, 0.25),
              A.mat("RockGranite", (0.47, 0.47, 0.45)), math.degrees(a), coll)


def _tent(coll, p, m, yaw, g):
    bm = bmesh.new()
    w, l, h = 3.0, 4.0, 2.2
    pts = [(-w / 2, -l / 2, 0), (w / 2, -l / 2, 0), (w / 2, l / 2, 0), (-w / 2, l / 2, 0), (0, -l / 2, h), (0, l / 2, h)]
    v = [bm.verts.new(q) for q in pts]
    bm.faces.new((v[0], v[1], v[4]))
    bm.faces.new((v[2], v[3], v[5]))
    bm.faces.new((v[1], v[2], v[5], v[4]))
    bm.faces.new((v[3], v[0], v[4], v[5]))
    bm.faces.new((v[3], v[2], v[1], v[0]))
    ob = A.obj_from_bmesh("TentCloth", bm, m, coll)
    ob.location = (p[0], p[1], g - 0.05)
    ob.rotation_euler = (0, 0, math.radians(yaw + 90))


# ───────────────────────── footbridge over the creek ─────────────────────────
def build_bridge():
    coll = A.collection("Bridge")
    wood = A.mat("BridgePlankWood", (0.45, 0.34, 0.22))
    slope = 4.0 * math.pi / 40.0
    ang = math.degrees(math.atan(slope)) + 90.0   # across the creek
    a = math.radians(ang)
    L = 7.0
    p0 = (-math.cos(a) * L / 2, -math.sin(a) * L / 2)
    p1 = (math.cos(a) * L / 2, math.sin(a) * L / 2)
    z = max(height(*p0), height(*p1)) - 0.05
    A.segment_box("BridgeDeckWood", p0, p1, 1.8, 0.18, wood, z - 0.18, coll)
    for sx in (-1, 1):
        off = (-math.sin(a) * 0.85 * sx, math.cos(a) * 0.85 * sx)
        A.segment_box("VIS_BridgeRail", (p0[0] + off[0], p0[1] + off[1]), (p1[0] + off[0], p1[1] + off[1]),
                      0.08, 0.08, wood, z + 0.9, coll)
        for t in (0.0, 0.5, 1.0):
            q = (p0[0] + (p1[0] - p0[0]) * t + off[0], p0[1] + (p1[1] - p0[1]) * t + off[1])
            A.box("BridgePostWood", (q[0], q[1], height(*q) - 0.3), (0.12, 0.12, z + 0.98 - height(*q) + 0.3), wood, ang, coll)


# ───────────────────────── gameplay points ─────────────────────────
SPAWN_RED = [(-5.5, 29.0), (-1.4, 28.4), (1.4, 28.4), (5.5, 29.0)]
SPAWN_FFA = [(-34.0, 20.0), (-28.0, -6.0), (-20.0, 30.0), (-6.0, 14.0)]
ENEMIES = [(-24.0, 10.0), (-4.0, 20.0), (-30.0, -14.0)]
POINTS = {
    "GP_RedBase": (0.0, 31.0), "GP_BlueBase": (0.0, -31.0),
    "GP_CP_A": (-14.0, 8.0), "GP_CP_B": (0.0, 0.0), "GP_CP_C": (14.0, -8.0),
    "GP_Bomb_A": (-14.5, 8.3), "GP_Bomb_B": (14.5, -8.3),
    "GP_VipStart": (5.0, -29.0), "GP_VipExtract": (-35.0, 35.0),
}


def gameplay():
    pts = dict(POINTS)
    for i, p in enumerate(SPAWN_RED):
        pts["GP_Spawn_R_%d" % i] = p
        pts["GP_Spawn_B_%d" % i] = sym(p)
    for i, p in enumerate(SPAWN_FFA):
        pts["GP_Spawn_F_%d" % (2 * i)] = p
        pts["GP_Spawn_F_%d" % (2 * i + 1)] = sym(p)
    for i, p in enumerate(ENEMIES):
        pts["GP_Enemy_%d" % (2 * i)] = p
        pts["GP_Enemy_%d" % (2 * i + 1)] = sym(p)
    A.gameplay_points(pts, height)


def main():
    out = A.args()
    fbx = out[0] if len(out) > 0 else os.path.abspath("Forest.fbx")
    blend = out[1] if len(out) > 1 else os.path.abspath("forest.blend")
    renders = out[2] if len(out) > 2 else None
    A.reset()
    init_flats()
    init_paths()
    build_ground()
    build_cabin(CABIN, CABIN_YAW, "W")
    build_cabin(sym(CABIN), CABIN_YAW + 180, "E")
    build_camp(CAMP, 1, -1)          # Red: the forest is to the south
    build_camp(sym(CAMP), 2, 1)      # Blue: point mirror
    build_bridge()
    build_vegetation()
    gameplay()
    A.add_probes()
    A.finish(fbx, blend)
    if renders:
        A.render_views(renders, HALF, HALF, "forest", eye_views=(
            ((0.0, 26.0, 2.0), (0.0, 0.0, 0.5)),            # from the Red camp towards the centre
            ((-20.0, 4.0, 1.7), (-14.0, 8.0, 1.5)),         # approaching the west cabin
            ((-10.0, 1.0, 1.7), (10.0, 1.0, 0.5)),          # along the creek
            ((0.0, -60.0, 30.0), (0.0, 0.0, 0.0)),          # overview from the south
        ))


main()
