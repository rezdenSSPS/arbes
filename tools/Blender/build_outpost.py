# Map "Outpost" (MAP 01, see the design PDF): a compact 68 x 47 m walled compound.
#
#   Room 1 (Red spawn, west) and Room 2 (Blue spawn, east), each 10.5 x 17 m, an open MID of 28 x 19 m,
#   and six corridors: LINK W/E (6 m wide, 5 m long, straight into MID), SHORT N/S (3 m wide, one bend)
#   and LONG N/S (3.5 m wide, around the edge, entering MID on the opponent's side).
#   17 cover pieces: 10 crates, a double crate, 2 divider walls, 4 barricades. The layout is point
#   symmetric (180 degrees around the centre), so both teams get the same routes.
#
# Run:  Blender -b --python build_outpost.py -- <out.fbx> <out.blend> [render_dir]
# Coordinates: x = east, y = north (metres, centre of the arena = 0, 0).
import math
import os
import random
import sys

import bmesh
import bpy
from mathutils import Vector

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import airsoft_bpy as A  # noqa: E402

WALL_H = 3.2
WALL_T = 0.45
HALF_X, HALF_Y = 34.0, 23.5


def sym(p):
    """The point-symmetric twin of (x, y)."""
    return (-p[0], -p[1])


def sym_poly(poly):
    return [sym(p) for p in poly]


# ───────────────────────── walkable areas (Red / west half; Blue = point mirror) ─────────────────────────
def walkable_polys():
    west = []
    room = A.octagon(-29.75, -8.5, -19.25, 8.5, 1.0)              # Room 1, 10.5 x 17
    link = A.rect(-20.0, -3.0, -13.2, 3.0)                       # LINK W, 6 m wide (overlaps room and MID)
    # LONG N: up from Room 1, along the north edge, down into MID's north-east entrance
    long_v1 = A.rect(-28.6, 7.5, -25.1, 20.95)
    long_h = A.rect(-28.6, 17.45, 11.7, 20.95)
    long_v2 = A.rect(8.2, 8.6, 11.7, 20.95)
    # SHORT N: up from Room 1, east, down into MID's north entrance near the middle
    short_v1 = A.rect(-23.0, 7.5, -20.0, 14.93)
    short_h = A.rect(-23.0, 11.93, 1.0, 14.93)
    short_v2 = A.rect(-2.0, 8.6, 1.0, 14.93)
    west += [room, link, long_v1, long_h, long_v2, short_v1, short_h, short_v2]
    mid = A.octagon(-14.0, -9.5, 14.0, 9.5, 2.1)
    return west + [sym_poly(p) for p in west] + [mid]


def build_compound():
    floor_m = A.mat("ConcreteFloor", (0.46, 0.45, 0.42), 0.9)
    wall_m = A.mat("ConcreteTWall", (0.62, 0.60, 0.55), 0.9)
    coll = A.collection("Compound")

    parts = [A.prism("Part%d" % i, poly, 0.0, WALL_H, wall_m, coll) for i, poly in enumerate(walkable_polys())]
    vol = parts[0]
    A.boolean_union(vol, parts[1:])

    # Split the walkable volume: bottom faces = floor, side faces = wall surfaces (top removed).
    bm = bmesh.new()
    bm.from_mesh(vol.data)
    bm.normal_update()
    floor_bm, wall_bm = bmesh.new(), bmesh.new()

    def copy_face(dst, f, flip):
        vs = [dst.verts.new(v.co) for v in f.verts]
        if flip:
            vs.reverse()
        dst.faces.new(vs)

    for f in bm.faces:
        n = f.normal
        if n.z < -0.9:
            copy_face(floor_bm, f, True)          # face up
        elif abs(n.z) < 0.1:
            copy_face(wall_bm, f, True)           # face into the walkable area (towards the players)
    bm.free()
    bpy.data.objects.remove(vol, do_unlink=True)

    for b in (floor_bm, wall_bm):
        bmesh.ops.remove_doubles(b, verts=b.verts, dist=0.001)
        b.normal_update()

    seams = bmesh.new()
    for f in wall_bm.faces:
        # T-wall seams every 1.6 m along each wall face (visual), slightly proud of the wall
        zs = [v.co.z for v in f.verts]
        if max(zs) - min(zs) < 1.0:
            continue
        low = sorted(f.verts, key=lambda v: v.co.z)[:2]
        a, b = Vector((low[0].co.x, low[0].co.y)), Vector((low[1].co.x, low[1].co.y))
        d = b - a
        L = d.length
        if L < 1.2:
            continue
        u = d / L
        n = Vector((f.normal.x, f.normal.y)).normalized()
        k = 1.6
        while k < L - 0.4:
            p = a + u * k + n * 0.02
            _seam(seams, p, u, n)
            k += 1.6

    floor = A.obj_from_bmesh("CompoundFloor", floor_bm, floor_m, coll)
    walls = A.obj_from_bmesh("CompoundWalls", wall_bm, wall_m, coll)
    md = walls.modifiers.new("Solid", "SOLIDIFY")
    md.thickness = WALL_T
    md.offset = -1.0                 # grow away from the players, into the solid blocks
    md.use_even_offset = True
    md.use_rim = True
    A.apply_modifiers(walls)
    A.obj_from_bmesh("VIS_WallSeams", seams, A.mat("ConcreteSeam", (0.36, 0.35, 0.33)), coll)
    return floor, walls


def _seam(bm, p, u, n):
    w, depth, h = 0.07, 0.03, WALL_H - 0.05
    c = [(-w / 2, 0), (w / 2, 0), (w / 2, depth), (-w / 2, depth)]
    bottom, top = [], []
    for sx, sy in c:
        q = p + u * sx + n * sy
        bottom.append(bm.verts.new((q.x, q.y, 0.0)))
        top.append(bm.verts.new((q.x, q.y, h)))
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
    bm.faces.new(top)


# ───────────────────────── cover (17 pieces, point symmetric) ─────────────────────────
def build_cover():
    crate_m = A.mat("WoodCrate", (0.55, 0.40, 0.24))
    crate_dark = A.mat("WoodCrateDark", (0.42, 0.30, 0.18))
    concrete = A.mat("ConcreteDivider", (0.58, 0.58, 0.55))
    bags = A.mat("SandBags", (0.66, 0.58, 0.42), 0.95)
    coll = A.collection("Cover")

    crates = [(-8.16, 20.04, 10), (2.89, 18.33, -6), (-14.18, 12.85, 4), (-5.06, 6.95, 12), (-21.17, -0.96, -8)]
    i = 0
    for x, y, yaw in crates:
        for p, sgn in (((x, y), 1), (sym((x, y)), -1)):
            i += 1
            A.box("Crate%02d" % i, (p[0], p[1], 0), (1.5, 1.5, 1.1), crate_m, yaw * sgn, coll, 0.03)
            A.box("VIS_CrateBand%02d" % i, (p[0], p[1], 0.48), (1.53, 1.53, 0.12), crate_dark, yaw * sgn, coll)

    # double crate in the middle of MID: two stacked crates each (high cover)
    for k, (x, y) in enumerate(((-1.8, 2.18), (1.8, -2.18))):
        yaw = 6 if k == 0 else -6
        A.box("CrateBig%d" % k, (x, y, 0), (3.4, 3.4, 1.3), crate_m, yaw, coll, 0.04)
        A.box("CrateTop%d" % k, (x + 0.15, y - 0.1, 1.3), (2.9, 2.9, 1.2), crate_dark, yaw + 9, coll, 0.04)

    # divider walls (concrete, 2.2 m)
    for k, (a, b) in enumerate((((-10.45, 5.86), (-10.45, -2.6)),)):
        A.segment_box("DividerWall%d" % k, a, b, 0.4, 2.2, concrete, 0, coll, 0.02)
        A.segment_box("DividerWall%d" % (k + 1), sym(a), sym(b), 0.4, 2.2, concrete, 0, coll, 0.02)

    # barricades (sandbags, 1.1 m), in MID and in each room
    for k, (a, b) in enumerate((((3.1, 5.77), (9.87, 7.61)), ((-27.7, -3.1), (-22.4, -6.1)))):
        A.segment_box("Barricade%d" % (2 * k), a, b, 0.65, 1.1, bags, 0, coll, 0.12)
        A.segment_box("Barricade%d" % (2 * k + 1), sym(a), sym(b), 0.65, 1.1, bags, 0, coll, 0.12)


# ───────────────────────── floor markings (visual) ─────────────────────────
def build_markings():
    coll = A.collection("Markings")
    red = A.mat("PaintRed", (0.78, 0.22, 0.18))
    blue = A.mat("PaintBlue", (0.20, 0.42, 0.80))
    white = A.mat("PaintWhite", (0.88, 0.88, 0.84))
    yellow = A.mat("PaintYellow", (0.92, 0.72, 0.14))
    red_sp = [(-27.8, 6.19), (-20.8, 6.19), (-27.8, -7.11), (-20.8, -7.11)]
    for i, p in enumerate(red_sp):
        A.cylinder("VIS_SpawnRed%d" % i, (p[0], p[1], 0.004), 0.75, 0.01, red, 16)
        q = sym(p)
        A.cylinder("VIS_SpawnBlue%d" % i, (q[0], q[1], 0.004), 0.75, 0.01, blue, 16)
    # team stripe across each room entrance to LINK
    A.box("VIS_RoomLineRed", (-19.5, 0, 0.003), (0.25, 6.0, 0.01), red, 0, coll)
    A.box("VIS_RoomLineBlue", (19.5, 0, 0.003), (0.25, 6.0, 0.01), blue, 0, coll)
    # MID: centre circle + entrance hatching (the orange dashes of the plan)
    A.cylinder("VIS_MidRing", (0, 0, 0.003), 6.5, 0.008, white, 32)
    A.cylinder("VIS_MidRingIn", (0, 0, 0.006), 6.3, 0.008, A.mat("ConcreteFloorPaint", (0.46, 0.45, 0.42)), 32)
    for a, b in (((-13.9, -3.0), (-13.9, 3.0)), ((-2.0, 9.45), (1.0, 9.45)), ((8.2, 9.45), (11.7, 9.45))):
        for p0, p1 in ((a, b), (sym(a), sym(b))):
            A.segment_box("VIS_Entry", p0, p1, 0.18, 0.01, yellow, 0.003, coll)


# ───────────────────────── surroundings (visual, outside the walls) ─────────────────────────
def build_surroundings():
    coll = A.collection("Surroundings")
    dirt = A.mat("DirtGround", (0.42, 0.36, 0.27), 1.0)
    metal = A.mat("MetalPost", (0.30, 0.31, 0.30), 0.5, 0.6)
    wood = A.mat("WoodTower", (0.45, 0.33, 0.21))
    roof = A.mat("MetalRoof", (0.33, 0.37, 0.30), 0.6, 0.4)
    canvas = A.mat("ClothTent", (0.40, 0.44, 0.30))
    cont = A.mat("MetalContainer", (0.30, 0.40, 0.30), 0.6, 0.4)

    # ground under everything (also the solid blocks between the corridors)
    A.box("DirtGroundPlane", (0, 0, -0.3), (160, 140, 0.28), dirt, 0, coll)

    # perimeter fence along the chamfered boundary
    c = 3.0
    ring = [(-HALF_X + c, -HALF_Y), (HALF_X - c, -HALF_Y), (HALF_X, -HALF_Y + c), (HALF_X, HALF_Y - c),
            (HALF_X - c, HALF_Y), (-HALF_X + c, HALF_Y), (-HALF_X, HALF_Y - c), (-HALF_X, -HALF_Y + c)]
    fence = bmesh.new()
    for i in range(len(ring)):
        a, b = Vector(ring[i]), Vector(ring[(i + 1) % len(ring)])
        L = (b - a).length
        n = max(1, int(L / 3.0))
        for k in range(n + 1):
            p = a + (b - a) * (k / n)
            _post(fence, p, 0.07, 2.4)
        for z in (0.6, 1.4, 2.2):   # wires
            _wire(fence, a, b, z)
    A.obj_from_bmesh("VIS_FenceMetal", fence, metal, coll)

    # watchtowers at the four chamfered corners
    for x, y in ((-31.5, 21.0), (31.5, 21.0), (31.5, -21.0), (-31.5, -21.0)):
        t = bmesh.new()
        for dx, dy in ((-1.1, -1.1), (1.1, -1.1), (1.1, 1.1), (-1.1, 1.1)):
            _post(t, Vector((x + dx, y + dy)), 0.12, 4.2)
        A.obj_from_bmesh("VIS_TowerLegs", t, wood, coll)
        A.box("VIS_TowerDeck", (x, y, 4.0), (2.8, 2.8, 0.2), wood, 0, coll)
        for k, (dx, dy, sx, sy) in enumerate(((0, -1.35, 2.8, 0.1), (0, 1.35, 2.8, 0.1), (-1.35, 0, 0.1, 2.8), (1.35, 0, 0.1, 2.8))):
            A.box("VIS_TowerRail", (x + dx, y + dy, 4.2), (sx, sy, 1.0), wood, 0, coll)
        A.box("MMH_TowerRoof", (x, y, 6.1), (3.3, 3.3, 0.15), roof, 0, coll)
        t2 = bmesh.new()
        for dx, dy in ((-1.25, -1.25), (1.25, -1.25), (1.25, 1.25), (-1.25, 1.25)):
            _post(t2, Vector((x + dx, y + dy)), 0.06, 6.1, 4.2)
        A.obj_from_bmesh("VIS_TowerPosts", t2, wood, coll)

    # radio mast in the block between SHORT N and LONG N, and its twin
    for p in ((4.5, 16.2), sym((4.5, 16.2))):
        A.cylinder("VIS_RadioMast", (p[0], p[1], 0), 0.12, 11.0, metal, 6, 0.05)
        A.box("VIS_MastBox", (p[0], p[1], 0), (1.0, 0.8, 1.2), cont, 0, coll)

    # containers and tents in the large empty corners (north-east and its twin)
    for base, yaw in (((22.0, 15.5), 0), (sym((22.0, 15.5)), 180)):
        bx, by = base
        A.box("VIS_Container", (bx - 3, by + 1.5, 0), (6.0, 2.4, 2.6), cont, yaw + 3, coll)
        A.box("VIS_Container", (bx + 4, by - 2.5, 0), (6.0, 2.4, 2.6), A.mat("MetalContainerTan", (0.55, 0.47, 0.33), 0.6, 0.4), yaw + 90, coll)
        _tent(coll, (bx - 2.5, by - 3.5), canvas, yaw)
        _tent(coll, (bx + 6.0, by + 4.0), canvas, yaw + 90)
    A.empty("Note_Outpost", (0, 0, 0))


def _post(bm, p, r, h, z0=0.0):
    bottom, top = [], []
    for k in range(4):
        a = math.radians(45 + 90 * k)
        q = (p.x + math.cos(a) * r, p.y + math.sin(a) * r)
        bottom.append(bm.verts.new((q[0], q[1], z0)))
        top.append(bm.verts.new((q[0], q[1], h)))
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
    bm.faces.new(top)


def _wire(bm, a, b, z):
    d = (b - a).normalized()
    n = Vector((-d.y, d.x)) * 0.015
    vs = [bm.verts.new((a.x - n.x, a.y - n.y, z)), bm.verts.new((b.x - n.x, b.y - n.y, z)),
          bm.verts.new((b.x + n.x, b.y + n.y, z + 0.03)), bm.verts.new((a.x + n.x, a.y + n.y, z + 0.03))]
    bm.faces.new(vs)


def _tent(coll, p, m, yaw):
    bm = bmesh.new()
    w, l, h = 2.4, 3.6, 2.0
    pts = [(-w / 2, -l / 2, 0), (w / 2, -l / 2, 0), (w / 2, l / 2, 0), (-w / 2, l / 2, 0), (0, -l / 2, h), (0, l / 2, h)]
    v = [bm.verts.new(q) for q in pts]
    bm.faces.new((v[0], v[1], v[4]))
    bm.faces.new((v[2], v[3], v[5]))
    bm.faces.new((v[1], v[2], v[5], v[4]))
    bm.faces.new((v[3], v[0], v[4], v[5]))
    ob = A.obj_from_bmesh("VIS_Tent", bm, m, coll)
    ob.location = (p[0], p[1], 0)
    ob.rotation_euler = (0, 0, math.radians(yaw))


# ───────────────────────── gameplay points (read by the Unity builder) ─────────────────────────
def gameplay():
    red = [(-27.8, 6.2), (-20.8, 6.2), (-27.8, -7.1), (-20.8, -7.1)]
    ffa = [(-27.07, 0.17), (-12.09, 6.9), (-14.06, 19.16), (-6.07, 13.43)]
    enemies = [(-6.0, -3.0), (-6.0, 19.2), (-10.0, 13.4)]
    pts = {"GP_RedBase": (-25.5, 2.5), "GP_BlueBase": sym((-25.5, 2.5)),
           "GP_CP_A": (-12.0, 13.4), "GP_CP_B": (0.0, 0.0), "GP_CP_C": sym((-12.0, 13.4)),
           "GP_Bomb_A": (-11.5, -6.0), "GP_Bomb_B": sym((-11.5, -6.0)),
           "GP_VipStart": (24.5, -5.0), "GP_VipExtract": (-26.8, 19.2)}
    for i, p in enumerate(red):
        pts["GP_Spawn_R_%d" % i] = p
        pts["GP_Spawn_B_%d" % i] = sym(p)
    for i, p in enumerate(ffa):
        pts["GP_Spawn_F_%d" % (2 * i)] = p
        pts["GP_Spawn_F_%d" % (2 * i + 1)] = sym(p)
    for i, p in enumerate(enemies):
        pts["GP_Enemy_%d" % (2 * i)] = p
        pts["GP_Enemy_%d" % (2 * i + 1)] = sym(p)
    A.gameplay_points(pts, lambda x, y: 0.0)


def main():
    out = A.args()
    fbx = out[0] if len(out) > 0 else os.path.abspath("Outpost.fbx")
    blend = out[1] if len(out) > 1 else os.path.abspath("outpost.blend")
    renders = out[2] if len(out) > 2 else None
    random.seed(20261021)
    A.reset()
    build_compound()
    build_cover()
    build_markings()
    build_surroundings()
    gameplay()
    A.add_probes()
    A.finish(fbx, blend)
    if renders:
        A.render_views(renders, HALF_X, HALF_Y, "outpost", eye_views=(
            ((-16.5, 0.0, 1.7), (0.0, 0.0, 1.2)),          # from LINK W into MID
            ((-24.5, 7.0, 1.7), (-24.5, -6.0, 1.0)),       # inside Room 1
            ((-26.8, 18.8, 1.7), (5.0, 19.2, 1.2)),        # down LONG N
            ((0.0, -30.0, 22.0), (0.0, 0.0, 0.0)),         # overview from the south
        ))


main()
