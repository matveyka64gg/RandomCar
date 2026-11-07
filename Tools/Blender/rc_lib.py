# Общие помощники для генерации моделей RandomCar в Blender.
# Склеивается с конкретным скриптом в blender_client.py и выполняется внутри Blender.
#
# Система координат «машины» (как в Unity): x — вправо, y — вверх, z — вперёд.
# В Blender модель смотрит на -Y (стандарт для экспорта FBX в Unity с осями -Z/Y):
#   Blender X = -x, Blender Y = -z, Blender Z = y.
# Это отражение, поэтому после построения мешей нормали пересчитываются наружу.

import math
import os

import bmesh
import bpy
from mathutils import Matrix, Vector

# Корень проекта — на два уровня выше этого файла (Tools/Blender/rc_lib.py).
PROJECT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
ART = os.path.join(PROJECT, "Assets", "Art", "Models")
PREVIEW = os.path.join(os.environ.get("TEMP") or os.environ.get("TMPDIR") or "/tmp", "randomcar", "blender")


def C(x, y, z):
    """Точка из координат машины в координаты Blender."""
    return Vector((-x, -z, y))


def hex_color(h, alpha=1.0):
    """sRGB hex -> линейный цвет Blender."""
    h = h.lstrip("#")
    rgb = [int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)]
    lin = [c / 12.92 if c <= 0.04045 else ((c + 0.055) / 1.055) ** 2.4 for c in rgb]
    return (lin[0], lin[1], lin[2], alpha)


# ---------- сцена ----------

def reset_scene():
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj, do_unlink=True)
    for coll in (bpy.data.meshes, bpy.data.curves, bpy.data.cameras, bpy.data.lights):
        for block in list(coll):
            if block.users == 0:
                coll.remove(block)
    bpy.context.scene.unit_settings.system = 'METRIC'
    bpy.context.scene.unit_settings.scale_length = 1.0


# ---------- материалы ----------

def mat(name, color, metallic=0.0, roughness=0.5, emission=None, strength=0.0, alpha=1.0):
    m = bpy.data.materials.get(name) or bpy.data.materials.new(name)
    m.use_nodes = True
    bsdf = m.node_tree.nodes.get("Principled BSDF")
    bsdf.inputs["Base Color"].default_value = hex_color(color)
    bsdf.inputs["Metallic"].default_value = metallic
    bsdf.inputs["Roughness"].default_value = roughness
    if emission:
        bsdf.inputs["Emission Color"].default_value = hex_color(emission)
        bsdf.inputs["Emission Strength"].default_value = strength
    bsdf.inputs["Alpha"].default_value = alpha
    if alpha < 1.0 and hasattr(m, "surface_render_method"):
        m.surface_render_method = 'BLENDED'
    m.diffuse_color = hex_color(color, alpha)
    return m


# ---------- меши ----------

def obj_from_bmesh(name, bm, materials, recalc=True):
    if recalc:
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    me = bpy.data.meshes.new(name)
    bm.to_mesh(me)
    bm.free()
    for m in materials:
        me.materials.append(m)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def box(name, center, size, material, bevel=0.0, segments=2):
    """Ящик в координатах машины: size = (ширина x, высота y, длина z)."""
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    sx, sy, sz = size
    bmesh.ops.scale(bm, vec=Vector((sx, sz, sy)), verts=bm.verts)
    bmesh.ops.translate(bm, vec=C(*center), verts=bm.verts)
    ob = obj_from_bmesh(name, bm, [material])
    if bevel > 0:
        add_bevel(ob, bevel, segments)
    return ob


def cylinder(name, center, radius, depth, axis, material, segments=32, bevel=0.0, bevel_segments=2):
    """Цилиндр в координатах машины. axis: 'x' (вбок), 'y' (вверх), 'z' (вперёд)."""
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments,
                          radius1=radius, radius2=radius, depth=depth)
    # create_cone строит цилиндр вдоль оси Z Blender (= вверх машины).
    if axis == 'x':
        bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(math.radians(90), 3, 'Y'))
    elif axis == 'z':
        bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(math.radians(90), 3, 'X'))
    bmesh.ops.translate(bm, vec=C(*center), verts=bm.verts)
    ob = obj_from_bmesh(name, bm, [material])
    if bevel > 0:
        add_bevel(ob, bevel, bevel_segments)
    return ob


def beam(name, p0, p1, thickness, depth, material, bevel=0.01):
    """Брусок между двумя точками машины: сечение thickness x depth."""
    a, b = C(*p0), C(*p1)
    length = (b - a).length
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector((thickness, depth, length)), verts=bm.verts)
    rot = Vector((0, 0, 1)).rotation_difference((b - a).normalized()).to_matrix()
    bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=rot)
    bmesh.ops.translate(bm, vec=(a + b) / 2, verts=bm.verts)
    ob = obj_from_bmesh(name, bm, [material])
    if bevel > 0:
        add_bevel(ob, bevel, 2)
    return ob


def loft(name, rings, material_fn, cap=True):
    """Лофт по кольцам точек (координаты машины). material_fn(i, j) -> индекс материала грани."""
    bm = bmesh.new()
    verts = [[bm.verts.new(C(*p)) for p in ring] for ring in rings]
    n = len(rings[0])
    for i in range(len(rings) - 1):
        for j in range(n):
            k = (j + 1) % n
            f = bm.faces.new((verts[i][j], verts[i][k], verts[i + 1][k], verts[i + 1][j]))
            f.material_index = material_fn(i, j)
    if cap:
        for ring_verts in (verts[0], verts[-1]):
            f = bm.faces.new(ring_verts)
            f.material_index = material_fn(-1, 0)
    return bm


def add_bevel(ob, width, segments=2, angle=35.0):
    mod = ob.modifiers.new("Bevel", 'BEVEL')
    mod.width = width
    mod.segments = segments
    mod.limit_method = 'ANGLE'
    mod.angle_limit = math.radians(angle)
    mod.harden_normals = True
    return mod


def apply_modifiers(ob):
    dg = bpy.context.evaluated_depsgraph_get()
    ev = ob.evaluated_get(dg)
    me = bpy.data.meshes.new_from_object(ev, preserve_all_data_layers=True, depsgraph=dg)
    old = ob.data
    ob.modifiers.clear()
    ob.data = me
    bpy.data.meshes.remove(old)


def smooth(ob, angle=35.0):
    me = ob.data
    for p in me.polygons:
        p.use_smooth = True
    if hasattr(me, "set_sharp_from_angle"):
        me.set_sharp_from_angle(angle=math.radians(angle))


def boolean_difference(ob, cutters):
    for c in cutters:
        mod = ob.modifiers.new("Cut_" + c.name, 'BOOLEAN')
        mod.operation = 'DIFFERENCE'
        mod.object = c
        if hasattr(mod, "material_mode"):
            mod.material_mode = 'TRANSFER'   # стенки выреза получают материал резака
    apply_modifiers(ob)
    for c in cutters:
        bpy.data.objects.remove(c, do_unlink=True)


def join(target, others):
    """Склеивает объекты в target (материалы объединяются по слотам)."""
    for ob in [target] + others:
        if ob.modifiers:
            apply_modifiers(ob)
    with bpy.context.temp_override(active_object=target, object=target,
                                   selected_objects=[target] + others,
                                   selected_editable_objects=[target] + others):
        bpy.ops.object.join()
    return target


def text_mesh(name, body, size, material, extrude=0.004):
    curve = bpy.data.curves.new(name, 'FONT')
    curve.body = body
    curve.size = size
    curve.extrude = extrude
    curve.align_x = 'CENTER'
    curve.align_y = 'CENTER'
    tmp = bpy.data.objects.new(name + "_tmp", curve)
    bpy.context.scene.collection.objects.link(tmp)
    dg = bpy.context.evaluated_depsgraph_get()
    me = bpy.data.meshes.new_from_object(tmp.evaluated_get(dg), depsgraph=dg)
    bpy.data.objects.remove(tmp, do_unlink=True)
    bpy.data.curves.remove(curve)
    me.materials.clear()
    me.materials.append(material)
    ob = bpy.data.objects.new(name, me)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def set_parent(child, parent):
    # matrix_world обновляется только после пересчёта слоя, иначе новые location/rotation теряются.
    bpy.context.view_layer.update()
    m = child.matrix_world.copy()
    child.parent = parent
    child.matrix_world = m


def empty(name, location=(0, 0, 0)):
    ob = bpy.data.objects.new(name, None)
    ob.empty_display_size = 0.5
    ob.location = C(*location)
    bpy.context.scene.collection.objects.link(ob)
    return ob


def origin_to(ob, point_car):
    """Переносит начало координат объекта в точку машины, геометрия остаётся на месте."""
    target = C(*point_car)
    delta = target - ob.matrix_world.translation
    ob.data.transform(Matrix.Translation(-delta))
    ob.matrix_world.translation = target


def all_children(root):
    out = []
    stack = list(root.children)
    while stack:
        c = stack.pop()
        out.append(c)
        stack.extend(c.children)
    return out


def tri_count(ob):
    return sum(len(p.vertices) - 2 for p in ob.data.polygons) if ob.type == 'MESH' else 0


# ---------- экспорт и превью ----------

def export_fbx(root, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    for ob in bpy.context.scene.objects:
        ob.select_set(False)
    objs = [root] + all_children(root)
    for ob in objs:
        ob.select_set(True)
    with bpy.context.temp_override(active_object=root, selected_objects=objs):
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types={'EMPTY', 'MESH'},
            apply_unit_scale=True, apply_scale_options='FBX_SCALE_ALL', global_scale=1.0,
            axis_forward='-Z', axis_up='Y', bake_space_transform=True,
            use_mesh_modifiers=True, mesh_smooth_type='OFF', use_tspace=False,
            add_leaf_bones=False, bake_anim=False, path_mode='AUTO', embed_textures=False)
    for ob in objs:
        ob.select_set(False)


def setup_preview_world():
    scene = bpy.context.scene
    world = bpy.data.worlds.get("RC_World") or bpy.data.worlds.new("RC_World")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = hex_color("#BFE3F7")
    bg.inputs["Strength"].default_value = 0.9
    scene.world = world
    for engine in ("BLENDER_EEVEE", "BLENDER_EEVEE_NEXT", "BLENDER_WORKBENCH"):
        try:
            scene.render.engine = engine
            break
        except TypeError:
            continue
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    scene.render.film_transparent = False
    if hasattr(scene, "eevee") and hasattr(scene.eevee, "taa_render_samples"):
        scene.eevee.taa_render_samples = 32
    scene.view_settings.view_transform = 'AgX' if 'AgX' in [i.identifier for i in scene.view_settings.bl_rna.properties['view_transform'].enum_items] else 'Filmic'

    sun = bpy.data.objects.get("RC_Sun")
    if sun is None:
        light = bpy.data.lights.new("RC_Sun", 'SUN')
        light.energy = 4.0
        light.color = (1.0, 0.93, 0.82)
        sun = bpy.data.objects.new("RC_Sun", light)
        scene.collection.objects.link(sun)
    sun.rotation_euler = (math.radians(50), math.radians(10), math.radians(-35))

    ground = bpy.data.objects.get("RC_Ground")
    if ground is None:
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=60)
        ground = obj_from_bmesh("RC_Ground", bm, [mat("RC_PreviewGround", "#7FA866", roughness=0.9)], recalc=False)
    return scene


def render_preview(path, target, distance, height, yaw_deg, lens=50):
    scene = setup_preview_world()
    cam = bpy.data.objects.get("RC_Cam")
    if cam is None:
        cam = bpy.data.objects.new("RC_Cam", bpy.data.cameras.new("RC_Cam"))
        scene.collection.objects.link(cam)
    cam.data.lens = lens
    t = Vector(target)
    yaw = math.radians(yaw_deg)
    cam.location = t + Vector((math.sin(yaw) * distance, -math.cos(yaw) * distance, height))
    direction = t - cam.location
    cam.rotation_euler = direction.to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    os.makedirs(os.path.dirname(path), exist_ok=True)
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def cleanup_preview():
    for name in ("RC_Cam", "RC_Sun", "RC_Ground"):
        ob = bpy.data.objects.get(name)
        if ob:
            bpy.data.objects.remove(ob, do_unlink=True)
