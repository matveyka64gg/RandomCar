# Общие помощники моделей хаоса (v4, v5): материалы, экспорт события, сфера, тор, конус.
# Склеивается после rc_lib.py: py -3 -I Tools/Blender/blender_client.py Tools/Blender/rc_lib.py Tools/Blender/chaos_lib.py Tools/Blender/<скрипт>.py

import math
import random

EV_DIR = os.path.join(ART, "Events")


def chaos_materials():
    return dict(
        chrome=mat("M_BallChrome", "#D9DEE6", metallic=0.95, roughness=0.18),
        rivet=mat("M_Trim", "#26282E", roughness=0.6),
        rubA=mat("M_BallRubberA", "#FF3D7F", roughness=0.35),
        rubB=mat("M_BallRubberB", "#FFE14D", roughness=0.35),
        white=mat("M_Decal_White", "#F2F0E8", roughness=0.4),
        black=mat("M_Decal_Black", "#15161A", roughness=0.4),
        mini=mat("M_BallMini_Red", "#FF4B2B", roughness=0.3),
        giantA=mat("M_GiantPurple", "#8B5CF6", roughness=0.35),
        giantB=mat("M_GiantOrange", "#FF8A1F", roughness=0.35),
        mine=mat("M_MineBody", "#2E3A47", metallic=0.3, roughness=0.45),
        spike=mat("M_Steel", "#B8C0CC", metallic=0.6, roughness=0.4),
        lamp=mat("M_MineLamp", "#FF2A2A", emission="#FF2A2A", strength=6.0),
        yellow=mat("M_SignYellow", "#FFD23F", roughness=0.5),
        wood=mat("M_Wood", "#7A5233", roughness=0.8),
        woodlight=mat("M_CrateWood", "#C98B4F", roughness=0.7),
        steel=mat("M_Steel", "#B8C0CC", metallic=0.6, roughness=0.4),
        tnt=mat("M_TNT", "#D7263D", roughness=0.45),
        head=mat("M_HammerHead", "#E0352B", roughness=0.4),
        orange=mat("M_WorksOrange", "#FF6A2A", roughness=0.45),
        teal=mat("M_GoodCyan", "#24E0C8", emission="#24E0C8", strength=1.5),
        blue=mat("M_TrampBlue", "#2F6BFF", roughness=0.4),
        trampmat=mat("M_TrampMat", "#1E2230", roughness=0.8),
        ice=mat("M_Ice", "#BFEFFF", roughness=0.05),
        iceDeep=mat("M_IceDeep", "#7FD3F5", roughness=0.08),
        jelly=mat("M_Jelly", "#FF5FA2", roughness=0.15),
        jellyHi=mat("M_JellyHi", "#FFB3D4", roughness=0.1),
        glow=mat("M_ArchGlow", "#B45CFF", emission="#B45CFF", strength=5.0),
        arch=mat("M_ArchBody", "#3A2E5C", metallic=0.4, roughness=0.4),
        gold=mat("M_RingGold", "#FFC21A", metallic=0.8, roughness=0.25, emission="#FFB000", strength=2.5),
        glove=mat("M_Glove", "#E81E3A", roughness=0.25),
        lampw=mat("M_WorksLamp", "#FFC21A", emission="#FFB000", strength=6.0),
        beak=mat("M_PenguinBeak", "#FF9A1F", roughness=0.5),
        pad=mat("M_Padding", "#FF4B6E", roughness=0.6),
        carnA=mat("M_CarnivalA", "#FF4BAA", roughness=0.4),
        carnB=mat("M_CarnivalB", "#2DD4F0", roughness=0.4),
    )


def export_event(name, parts, children=None):
    """Корень-пустышка с мешем Body и именованными подвижными частями (children: имя -> (объект, точка поворота))."""
    root = empty(name)
    body = join(parts[0], parts[1:]) if len(parts) > 1 else parts[0]
    body.name = "Body"
    body.data.name = name + "_Body"
    set_parent(body, root)
    for child_name, (ob, pivot) in (children or {}).items():
        if ob.modifiers:
            apply_modifiers(ob)
        ob.name = child_name
        ob.data.name = name + "_" + child_name
        origin_to(ob, pivot)
        set_parent(ob, root)
    export_fbx(root, os.path.join(EV_DIR, name + ".fbx"))
    tris = sum(tri_count(o) for o in all_children(root))
    for ob in all_children(root):
        ob.name = ob.name + "_" + name
    return root, tris


def sphere(name, center, radius, materials, mat_fn=None, u=32, v=16, scale=(1, 1, 1)):
    """Сфера в координатах Unity; mat_fn(x, y, z) по центру грани (координаты Unity от центра) -> индекс материала."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=radius)
    sx, sy, sz = scale
    bmesh.ops.scale(bm, vec=Vector((sx, sz, sy)), verts=bm.verts)
    if mat_fn:
        for f in bm.faces:
            c = f.calc_center_median()
            f.material_index = mat_fn(-c.x, c.z, -c.y)
    bmesh.ops.translate(bm, vec=C(*center), verts=bm.verts)
    ob = obj_from_bmesh(name, bm, materials)
    smooth(ob, 80)
    return ob


def torus(name, center, major, minor, axis, material, seg=40, ring=12):
    """Тор в координатах Unity: axis — ось кольца ('z' — проезжать насквозь вдоль z, 'y' — лежит)."""
    bm = bmesh.new()
    verts = []
    for i in range(seg):
        a = i / seg * math.tau
        row = []
        for j in range(ring):
            b = j / ring * math.tau
            r = major + minor * math.cos(b)
            p = (r * math.cos(a), r * math.sin(a), minor * math.sin(b))   # кольцо в плоскости XY, ось Z
            if axis == 'y':
                p = (p[0], p[2], p[1])
            row.append(bm.verts.new(C(center[0] + p[0], center[1] + p[1], center[2] + p[2])))
        verts.append(row)
    for i in range(seg):
        for j in range(ring):
            a, b = verts[i][j], verts[(i + 1) % seg][j]
            c, d = verts[(i + 1) % seg][(j + 1) % ring], verts[i][(j + 1) % ring]
            bm.faces.new((a, b, c, d))
    ob = obj_from_bmesh(name, bm, [material])
    smooth(ob, 80)
    return ob


def cone(name, base, tip, radius, material, segments=12):
    a, b = Vector(base), Vector(tip)
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=True, segments=segments, radius1=radius, radius2=0.0, depth=(b - a).length)
    d = C(*tip) - C(*base)
    rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix()
    bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=rot)
    bmesh.ops.translate(bm, vec=(C(*base) + C(*tip)) / 2, verts=bm.verts)
    return obj_from_bmesh(name, bm, [material])
