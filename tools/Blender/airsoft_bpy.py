# Shared helpers for the map scripts (build_outpost.py, build_forest.py).
#
# Conventions (both scripts):
#   - metres; Blender X = east, Blender Y = north, Blender Z = up. The FBX export below turns that into
#     Unity X = east, Unity Z = north, Unity Y = up (the Unity builders check it with the Probe_* empties).
#   - object names tell the Unity builder what to do with them:
#       "VIS_..."   visual only, no collider
#       "MMH_..."   visual only and hidden on the minimap / map preview (roofs, tree canopies)
#       anything else gets a MeshCollider
#   - material names drive the footstep / BB impact sounds (Surfaces.cs): "Wood", "Crate", "Sand",
#     "Bag", "Dirt", "Ground", "Grass", "Metal", "Trunk", "Leaves", ... (no match = concrete)
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector

_mats = {}


def reset():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    _mats.clear()


def args():
    """Command line arguments after '--'."""
    return sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []


def mat(name, rgb, rough=0.85, metal=0.0):
    if name in _mats:
        return _mats[name]
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    col = (rgb[0], rgb[1], rgb[2], 1.0)
    if bsdf:
        bsdf.inputs["Base Color"].default_value = col
        bsdf.inputs["Roughness"].default_value = rough
        bsdf.inputs["Metallic"].default_value = metal
    m.diffuse_color = col
    _mats[name] = m
    return m


def collection(name):
    c = bpy.data.collections.get(name)
    if c is None:
        c = bpy.data.collections.new(name)
        bpy.context.scene.collection.children.link(c)
    return c


def obj_from_bmesh(name, bm, material, coll=None):
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    if material is not None:
        me.materials.append(material)
    ob = bpy.data.objects.new(name, me)
    (coll or bpy.context.scene.collection).objects.link(ob)
    return ob


def box(name, center, size, material, yaw_deg=0.0, coll=None, bevel=0.0):
    """Axis box: center (x, y, z) is the middle of the bottom face; size (sx, sy, sz)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    for v in bm.verts:
        v.co = Vector((v.co.x * size[0], v.co.y * size[1], (v.co.z + 0.5) * size[2]))
    ob = obj_from_bmesh(name, bm, material, coll)
    ob.location = Vector(center)
    ob.rotation_euler = (0.0, 0.0, math.radians(yaw_deg))
    if bevel > 0:
        md = ob.modifiers.new("Bevel", "BEVEL")
        md.width = bevel
        md.segments = 1
    return ob


def segment_box(name, a, b, width, height, material, z0=0.0, coll=None, bevel=0.0):
    """A wall-like box from 2D point a to b (x, y) on the ground at z0."""
    ax, ay = a
    bx, by = b
    dx, dy = bx - ax, by - ay
    length = math.hypot(dx, dy)
    yaw = math.degrees(math.atan2(dy, dx))
    return box(name, ((ax + bx) / 2, (ay + by) / 2, z0), (length, width, height), material, yaw, coll, bevel)


def cylinder(name, base, radius, height, material, verts=8, radius_top=None, coll=None):
    bm = bmesh.new()
    rt = radius if radius_top is None else radius_top
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=verts,
                          radius1=radius, radius2=rt, depth=height)
    for v in bm.verts:
        v.co.z += height / 2
    ob = obj_from_bmesh(name, bm, material, coll)
    ob.location = Vector(base)
    return ob


def prism(name, poly, z0, z1, material, coll=None):
    """Vertical prism over a 2D polygon (list of (x, y), counter-clockwise)."""
    bm = bmesh.new()
    bottom = [bm.verts.new((x, y, z0)) for x, y in poly]
    top = [bm.verts.new((x, y, z1)) for x, y in poly]
    bm.faces.new(list(reversed(bottom)))
    bm.faces.new(top)
    n = len(poly)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
    bm.normal_update()
    return obj_from_bmesh(name, bm, material, coll)


def octagon(x0, y0, x1, y1, c):
    """Rectangle x0..x1, y0..y1 with 45 degree corner cuts of size c (counter-clockwise)."""
    return [(x0 + c, y0), (x1 - c, y0), (x1, y0 + c), (x1, y1 - c),
            (x1 - c, y1), (x0 + c, y1), (x0, y1 - c), (x0, y0 + c)]


def rect(x0, y0, x1, y1):
    return [(x0, y0), (x1, y0), (x1, y1), (x0, y1)]


def boolean_union(target, others):
    for o in others:
        md = target.modifiers.new("U_" + o.name, "BOOLEAN")
        md.operation = "UNION"
        md.solver = "EXACT"
        md.object = o
    apply_modifiers(target)
    for o in others:
        bpy.data.objects.remove(o, do_unlink=True)


def apply_modifiers(ob):
    bpy.context.view_layer.objects.active = ob
    for o in bpy.context.selected_objects:
        o.select_set(False)
    ob.select_set(True)
    for md in list(ob.modifiers):
        bpy.ops.object.modifier_apply(modifier=md.name)


def empty(name, pos, coll=None):
    e = bpy.data.objects.new(name, None)
    e.location = Vector(pos)
    (coll or bpy.context.scene.collection).objects.link(e)
    return e


def add_probes():
    """Unity checks the axis conversion with these (east = +X, north = +Z)."""
    c = collection("Probes")
    empty("Probe_Origin", (0, 0, 0), c)
    empty("Probe_East", (10, 0, 0), c)
    empty("Probe_North", (0, 10, 0), c)
    empty("Probe_Up", (0, 0, 10), c)


def gameplay_points(points, height):
    """Empties "GP_..." the Unity builder turns into spawns and mode points (it drops them onto the ground).
    points: {name: (x, y)}, height(x, y): ground height there."""
    c = collection("Gameplay")
    for name, (x, y) in points.items():
        empty(name, (x, y, height(x, y)), c)


def finish(fbx_path, blend_path):
    for ob in bpy.data.objects:
        if ob.type == "MESH" and ob.modifiers:
            apply_modifiers(ob)
    os.makedirs(os.path.dirname(fbx_path), exist_ok=True)
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)
    bpy.ops.wm.save_as_mainfile(filepath=blend_path)
    bpy.ops.export_scene.fbx(
        filepath=fbx_path,
        use_selection=False,
        object_types={"MESH", "EMPTY"},
        apply_unit_scale=True,
        apply_scale_options="FBX_SCALE_ALL",
        axis_forward="-Z",
        axis_up="Y",
        bake_space_transform=True,
        use_mesh_modifiers=True,
        mesh_smooth_type="FACE",
        add_leaf_bones=False,
        use_custom_props=False,
    )
    print("[airsoft] exported", fbx_path)


def render_views(out_dir, half_x, half_y, prefix, eye_views=()):
    """Workbench renders for checking the map: top-down ortho + a few perspective views."""
    os.makedirs(out_dir, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"
    sc.display.shading.color_type = "MATERIAL"
    sc.display.shading.show_shadows = True
    sc.display.shading.show_cavity = True
    sc.render.resolution_percentage = 100
    cam_data = bpy.data.cameras.new("PreviewCam")
    cam = bpy.data.objects.new("PreviewCam", cam_data)
    sc.collection.objects.link(cam)
    sc.camera = cam

    hidden = [o for o in bpy.data.objects if o.name.startswith("MMH_")]

    # top-down
    for o in hidden:
        o.hide_render = True
    cam_data.type = "ORTHO"
    cam_data.ortho_scale = 2 * max(half_x, half_y) + 4
    cam_data.clip_end = 500
    cam.location = (0, 0, 120)
    cam.rotation_euler = (0, 0, 0)
    aspect = half_x / max(half_y, 1e-3)
    sc.render.resolution_x = 1400
    sc.render.resolution_y = int(1400 / max(aspect, 1.0)) if aspect >= 1 else 1400
    sc.render.filepath = os.path.join(out_dir, prefix + "_top.png")
    bpy.ops.render.render(write_still=True)
    for o in hidden:
        o.hide_render = False

    cam_data.type = "PERSP"
    cam_data.lens = 24
    sc.render.resolution_x, sc.render.resolution_y = 1280, 720
    for i, (loc, target) in enumerate(eye_views):
        cam.location = Vector(loc)
        d = Vector(target) - Vector(loc)
        cam.rotation_euler = d.to_track_quat("-Z", "Y").to_euler()
        sc.render.filepath = os.path.join(out_dir, "%s_view%d.png" % (prefix, i))
        bpy.ops.render.render(write_still=True)
    bpy.data.objects.remove(cam, do_unlink=True)
