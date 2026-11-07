# Модели событий v3 (перекрытие дороги, обстрел). Запуск: blender_client.py rc_lib.py events_v3.py
# Координаты как в Unity: x — вправо, y — вверх, z — «вперёд» объекта. Начало координат у основания.

import math
import random

EV_DIR = os.path.join(ART, "Events")


def v3_materials():
    return dict(
        concrete=mat("M_BarrierConcrete", "#D9D4C7", roughness=0.85),
        orange=mat("M_WorksOrange", "#FF6A2A", roughness=0.45),
        white=mat("M_Decal_White", "#F2F0E8", roughness=0.4),
        black=mat("M_Decal_Black", "#15161A", roughness=0.4),
        trim=mat("M_Trim", "#26282E", roughness=0.6),
        lamp=mat("M_WorksLamp", "#FFC21A", emission="#FFB000", strength=6.0),
        arrow=mat("M_WorksArrow", "#FFD23F", emission="#FFC21A", strength=3.0),
        rock=mat("M_MeteorRock", "#4A3A36", roughness=0.9),
        lava=mat("M_MeteorLava", "#FF5A1F", emission="#FF4A12", strength=8.0),
    )


def export_event(name, parts):
    root = empty(name)
    body = join(parts[0], parts[1:]) if len(parts) > 1 else parts[0]
    body.name = "Body"
    body.data.name = name + "_Body"
    set_parent(body, root)
    export_fbx(root, os.path.join(EV_DIR, name + ".fbx"))
    tris = sum(tri_count(o) for o in all_children(root))
    for ob in all_children(root):
        ob.name = ob.name + "_" + name
    return root, tris


def lofted(name, rings, material, cap=True):
    """Лофт по кольцам точек (кортежи в координатах Unity) с одним материалом."""
    return obj_from_bmesh(name, loft(name, rings, lambda i, j: 0, cap), [material])


def jersey_barrier(m):
    """Бетонный блок «нью-джерси» 3 м с оранжево-белыми полосами и фонарём — дорожные работы."""
    parts = []
    length = 3.0
    rings = []
    # Сечение: широкое основание, скос, узкий верх (поперёк — x, вверх — y).
    profile = [(-0.42, 0.0), (-0.42, 0.08), (-0.22, 0.28), (-0.12, 0.82), (0.12, 0.82), (0.22, 0.28), (0.42, 0.08), (0.42, 0.0)]
    for zi in (-length / 2, length / 2):
        rings.append([(x, y, zi) for x, y in profile])
    parts.append(lofted("Barrier", rings, m["concrete"]))
    for k in range(5):
        z = -1.2 + k * 0.6
        color = m["orange"] if k % 2 == 0 else m["white"]
        # Полоса охватывает верх блока чуть шире сечения — видна с обеих сторон (без поворотов: box() строит в мире).
        parts.append(box("Stripe", (0, 0.6, z), (0.33, 0.3, 0.5), color, bevel=0.01))
    parts.append(cylinder("LampPost", (0, 0.95, 1.15), 0.025, 0.26, 'Y', m["trim"], segments=8))
    parts.append(cylinder("Lamp", (0, 1.12, 1.15), 0.09, 0.12, 'Y', m["lamp"], segments=16, bevel=0.02))
    return export_event("JerseyBarrier", parts)


def traffic_cone(m):
    parts = [box("Base", (0, 0.025, 0), (0.42, 0.05, 0.42), m["black"], bevel=0.02)]
    rings = []
    for y, r in ((0.05, 0.17), (0.75, 0.035)):
        rings.append([(r * math.cos(a), y, r * math.sin(a)) for a in [i / 16 * math.tau for i in range(16)]])
    parts.append(lofted("Cone", rings, m["orange"]))
    for y0, y1 in ((0.32, 0.42), (0.52, 0.6)):
        r0 = 0.17 + (0.035 - 0.17) * (y0 - 0.05) / 0.7
        r1 = 0.17 + (0.035 - 0.17) * (y1 - 0.05) / 0.7
        band = [[((r0 + 0.006) * math.cos(a), y0, (r0 + 0.006) * math.sin(a)) for a in [i / 16 * math.tau for i in range(16)]],
                [((r1 + 0.006) * math.cos(a), y1, (r1 + 0.006) * math.sin(a)) for a in [i / 16 * math.tau for i in range(16)]]]
        parts.append(lofted("Band", band, m["white"], cap=False))
    return export_event("TrafficCone", parts)


def arrow_board(m):
    """Щит со светящейся стрелкой объезда на стойках (смотрит навстречу машинам, стрелка вправо по +x)."""
    parts = [box("Board", (0, 1.55, 0), (2.2, 1.1, 0.12), m["black"], bevel=0.03)]
    parts.append(box("Frame", (0, 1.55, 0.02), (2.32, 1.22, 0.06), m["orange"], bevel=0.02))
    for s in (-1, 1):
        parts.append(box("Leg", (s * 0.85, 0.5, 0.1), (0.08, 1.0, 0.08), m["trim"]))
        parts.append(box("Foot", (s * 0.85, 0.04, 0.1), (0.5, 0.08, 0.6), m["trim"]))
    # Стрелка из ламп: древко и наконечник на лицевой стороне (−z — к водителю).
    for k in range(6):
        parts.append(cylinder("Dot", (-0.75 + k * 0.22, 1.55, -0.07), 0.06, 0.04, 'Z', m["arrow"], segments=12))
    for k in range(3):
        for s in (-1, 1):
            parts.append(cylinder("Dot", (0.55 - k * 0.18, 1.55 + s * (0.14 + k * 0.14), -0.07), 0.06, 0.04, 'Z', m["arrow"], segments=12))
    return export_event("ArrowBoard", parts)


def meteor(m):
    """Мультяшный метеорит: тёмный камень с раскалёнными трещинами (падает в «Обстреле» на обычных картах)."""
    rnd = random.Random(7)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=0.9)
    for v in bm.verts:
        v.co *= 1.0 + rnd.uniform(-0.14, 0.14)
    bmesh.ops.translate(bm, vec=C(0, 0.9, 0), verts=bm.verts)
    rock = obj_from_bmesh("Rock", bm, [m["rock"], m["lava"]])
    # Трещины: часть граней — раскалённый материал.
    for poly in rock.data.polygons:
        n = poly.normal
        if abs(math.sin(n.x * 6.1 + n.y * 3.7) * math.cos(n.z * 5.3)) > 0.72:
            poly.material_index = 1
    smooth(rock, 50)
    glow = []
    for k in range(5):
        a = k / 5 * math.tau
        g = box("Ember", (0.95 * math.cos(a), 0.9 + 0.3 * math.sin(a * 2), 0.95 * math.sin(a)), (0.16, 0.16, 0.16), m["lava"], bevel=0.05)
        glow.append(g)
    return export_event("Meteor", [rock] + glow)


reset_scene()
m = v3_materials()
results = []
for fn in (lambda: jersey_barrier(m), lambda: traffic_cone(m), lambda: arrow_board(m), lambda: meteor(m)):
    results.append(fn())

for ob in bpy.data.objects:
    ob.hide_render = ob.type == 'MESH' and ob.name != "RC_Ground"
layout = [(-4, 0), (-1.5, 0), (1.5, 0), (5, 0)]
for (root, tris), (x, z) in zip(results, layout):
    root.location = C(x, 0, z)
    for c in all_children(root):
        c.hide_render = False
render_preview(os.path.join(PREVIEW, "events_v3.png"), (0, 0, 1.0), 12, 4, 200, lens=35)
for root, tris in results:
    print(f"{root.name}: {tris} tris")
