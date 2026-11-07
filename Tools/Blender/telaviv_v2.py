# «Тель-Авив» v2: небоскрёбы-ориентиры, жилые башни, пилон вантового моста, опора эстакады, портал тоннеля,
# часовая башня Яффо, маяк, фонтан, круглое дерево бульвара. Стиль — мультяшный, читаемые силуэты, неон.
# Запуск: blender_client.py rc_lib.py telaviv_v2.py
# Координаты как в Unity: x — вправо, y — вверх, z — вперёд. Начало — центр основания (у башен).

import math
import random

TA_DIR = os.path.join(ART, "TelAviv")


def v2_materials():
    return dict(
        tower=mat("M_TowerGlass", "#FFFFFF", metallic=0.5, roughness=0.1),
        towerframe=mat("M_TowerFrame", "#2A3140", metallic=0.6, roughness=0.35),
        white=mat("M_Bauhaus", "#EEE9DF", roughness=0.8),
        cream=mat("M_BauhausCream", "#E6D8BE", roughness=0.8),
        concrete=mat("M_Concrete", "#D3CEC4", roughness=0.85),
        sand=mat("M_Sandstone", "#C9A878", roughness=0.9),
        sanddark=mat("M_SandstoneDark", "#A9875A", roughness=0.9),
        dark=mat("M_WindowDark", "#2B3A52", metallic=0.2, roughness=0.1),
        lit=mat("M_WindowLit", "#FFD9A0", emission="#FFC77A", strength=1.6),
        steel=mat("M_Steel", "#B8C0CC", metallic=0.6, roughness=0.4),
        trim=mat("M_Trim", "#26282E", roughness=0.6),
        red=mat("M_KerbRed", "#E0352B", roughness=0.5),
        neonpink=mat("M_Neon", "#FF4FA0", emission="#FF4FA0", strength=3.2),
        neonteal=mat("M_NeonTeal", "#24E0C8", emission="#24E0C8", strength=3.0),
        neonblue=mat("M_NeonBlue", "#3F7BFF", emission="#3F7BFF", strength=3.0),
        neonyellow=mat("M_NeonYellow", "#FFD23F", emission="#FFC21A", strength=2.8),
        lamp=mat("M_LampGlow", "#FFF3D6", emission="#FFF3D6", strength=1.5),
        clock=mat("M_ClockFace", "#FFF2C8", emission="#FFE7A0", strength=2.2),
        water=mat("M_FountainWater", "#7FE6FF", emission="#4FD8FF", strength=1.6),
        trunk=mat("M_PalmTrunk", "#7A6248", roughness=0.9),
        leaf=mat("M_FicusLeaf", "#3F8A45", roughness=0.7),
        leaf2=mat("M_FicusLeaf2", "#4FA052", roughness=0.7),
    )


def set_uv_box(ob, tile_u, tile_v):
    """UV по метрам: стеклянные фасады получают сетку окон из текстуры M_TowerGlass."""
    me = ob.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    for poly in me.polygons:
        n = poly.normal
        for li in poly.loop_indices:
            co = ob.matrix_world @ me.vertices[me.loops[li].vertex_index].co
            x, y, z = -co.x, co.z, -co.y
            if abs(n.z) > 0.7:
                u, v = x / tile_u, z / tile_u
            elif abs(n.x) > abs(n.y):
                u, v = z / tile_u, y / tile_v
            else:
                u, v = x / tile_u, y / tile_v
            uv[li].uv = (u, v)


def finish(name, parts):
    ob = join(parts[0], parts[1:]) if len(parts) > 1 else parts[0]
    ob.name = name
    ob.data.name = name
    root = empty(name + "_Root")
    set_parent(ob, root)
    export_fbx(root, os.path.join(TA_DIR, name + ".fbx"))
    tris = tri_count(ob)
    for o in [root, ob]:
        o.name = o.name + "_done"
    return name, tris


def slab(name, y0, h, w, d, angle, material, cx=0.0, cz=0.0):
    """Прямоугольный объём, повёрнутый вокруг вертикали на angle (градусы): bmesh сразу в нужном месте."""
    bm = bmesh.new()
    a = math.radians(angle)
    ca, sa = math.cos(a), math.sin(a)
    verts = []
    for yy in (y0, y0 + h):
        for (x, z) in ((-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)):
            rx, rz = x * ca - z * sa + cx, x * sa + z * ca + cz
            verts.append(bm.verts.new(C(rx, yy, rz)))
    b, t = verts[:4], verts[4:]
    bm.faces.new(list(reversed(b)))
    bm.faces.new(t)
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((b[i], b[j], t[j], t[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return obj_from_bmesh(name, bm, [material])


def ring(name, y, radius, thickness, height, material, segments=40):
    """Плоское кольцо (обод короны, «ресторанный диск»)."""
    rings = []
    for yy in (y, y + height):
        rings.append([(radius * math.cos(i / segments * math.tau), yy, radius * math.sin(i / segments * math.tau)) for i in range(segments)])
    outer = obj_from_bmesh(name, loft(name, rings, lambda i, j: 0, True), [material])
    return outer


# ---------- небоскрёбы ----------

def sky_twist(m):
    """«Закрученная» башня 160 м: 40 плит, каждая повёрнута на 2.2°, светящиеся торцы этажей."""
    parts = []
    floors, fh = 40, 4.0
    for f in range(floors):
        body = slab("Floor", f * fh, fh - 0.45, 30.0, 30.0, f * 2.2, m["tower"])
        set_uv_box(body, 4.0, 3.6)
        parts.append(body)
        if f % 4 == 3:
            parts.append(slab("Band", f * fh + fh - 0.45, 0.45, 30.6, 30.6, f * 2.2, m["neonteal"]))
        else:
            parts.append(slab("Gap", f * fh + fh - 0.45, 0.45, 29.0, 29.0, f * 2.2, m["towerframe"]))
    top = floors * fh
    parts.append(slab("Crown", top, 3.0, 26.0, 26.0, floors * 2.2, m["neonteal"]))
    parts.append(cylinder("Spire", (0, top + 14, 0), 0.4, 22, 'y', m["steel"], segments=8))
    parts.append(box("Beacon", (0, top + 25.5, 0), (0.8, 0.8, 0.8), m["red"]))
    return finish("TA_Sky_Twist", parts)


def sky_blade(m):
    """Башня-лезвие 190 м: узкая пластина со скошенной крышей и вертикальными неоновыми рёбрами."""
    parts = []
    w, d, h = 46.0, 16.0, 190.0
    bm = bmesh.new()
    pts = [(-w / 2, -d / 2), (w / 2, -d / 2), (w / 2, d / 2), (-w / 2, d / 2)]
    bottom = [bm.verts.new(C(x, 0, z)) for (x, z) in pts]
    tops = [h - 28.0, h, h, h - 28.0]
    topv = [bm.verts.new(C(x, tops[i], z)) for i, (x, z) in enumerate(pts)]
    bm.faces.new(list(reversed(bottom)))
    bm.faces.new(topv)
    for i in range(4):
        j = (i + 1) % 4
        bm.faces.new((bottom[i], bottom[j], topv[j], topv[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    body = obj_from_bmesh("Blade", bm, [m["tower"]])
    set_uv_box(body, 4.0, 3.6)
    parts.append(body)
    for k in range(7):
        x = -w / 2 + 3 + k * (w - 6) / 6
        top_here = h - 28.0 + 28.0 * (x + w / 2) / w
        for s in (-1, 1):
            parts.append(box("Rib", (x, top_here / 2, s * (d / 2 + 0.25)), (0.5, top_here, 0.5), m["neonblue"] if k % 3 == 0 else m["towerframe"]))
    parts.append(box("Lobby", (0, 6, 0), (w + 2, 12, d + 2), m["towerframe"]))
    return finish("TA_Sky_Blade", parts)


def sky_stack(m):
    """«Ступенчатая» башня 140 м: семь объёмов со сдвигом, на каждом уступе — сад и подсветка."""
    parts = []
    rnd = random.Random(4)
    y = 0.0
    for k in range(7):
        h = 20.0
        w = 36.0 - k * 2.4
        dx = rnd.uniform(-5, 5)
        dz = rnd.uniform(-4, 4)
        body = slab("Block", y, h, w, w * 0.8, 0.0, m["tower"], cx=dx, cz=dz)
        set_uv_box(body, 4.0, 3.6)
        parts.append(body)
        parts.append(slab("Ledge", y + h, 0.6, w + 0.8, w * 0.8 + 0.8, 0.0, m["neonpink"] if k % 2 == 0 else m["towerframe"], cx=dx, cz=dz))
        y += h + 0.6
    parts.append(cylinder("Mast", (0, y + 10, 0), 0.5, 20, 'y', m["steel"], segments=8))
    parts.append(box("Beacon", (0, y + 20.5, 0), (0.9, 0.9, 0.9), m["red"]))
    return finish("TA_Sky_Stack", parts)


def sky_disc(m):
    """Круглая башня 170 м с «ресторанным» кольцом под вершиной и светящимся ободом."""
    parts = []
    r, h = 16.0, 170.0
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=36, radius1=r, radius2=r * 0.86, depth=h)
    bmesh.ops.translate(bm, vec=C(0, h / 2, 0), verts=bm.verts)
    body = obj_from_bmesh("Disc", bm, [m["tower"]])
    set_uv_box(body, 4.0, 3.6)
    parts.append(body)
    parts.append(cylinder("Halo", (0, h - 22, 0), r * 1.65, 5.0, 'y', m["towerframe"], segments=40))
    parts.append(cylinder("HaloGlow", (0, h - 19.3, 0), r * 1.68, 0.8, 'y', m["neonyellow"], segments=40))
    parts.append(cylinder("HaloGlass", (0, h - 23, 0), r * 1.58, 2.4, 'y', m["lit"], segments=40))
    parts.append(cylinder("Crown", (0, h + 0.8, 0), r * 0.84, 1.6, 'y', m["neonblue"], segments=36))
    parts.append(cylinder("Spire", (0, h + 16, 0), 0.5, 30, 'y', m["steel"], segments=8))
    parts.append(box("Beacon", (0, h + 31.5, 0), (1.0, 1.0, 1.0), m["red"]))
    return finish("TA_Sky_Disc", parts)


def residential(name, m, floors, w, d, seed, cream):
    """Жилая башня (белая, с балконами-лентами и светящимися окнами): 60–90 м."""
    rnd = random.Random(seed)
    fh = 3.2
    body_mat = m["cream"] if cream else m["white"]
    parts = [box("Core", (0, floors * fh / 2 + 2, 0), (w - 1.6, floors * fh, d - 1.6), body_mat, bevel=0.1)]
    parts.append(box("Base", (0, 2.0, 0), (w + 1.0, 4.0, d + 1.0), m["towerframe"]))
    for f in range(floors):
        y = 4 + f * fh
        parts.append(box("Balcony", (0, y + 0.15, 0), (w + 0.6, 0.3, d + 0.6), body_mat, bevel=0.12))
        for s in (-1, 1):
            for c in range(int(w / 3.4)):
                x = -w / 2 + 2.0 + c * 3.4
                lit = rnd.random() < 0.42
                parts.append(box("Win", (x, y + 1.5, s * (d / 2 - 0.75)), (1.8, 1.7, 0.06), m["lit"] if lit else m["dark"]))
    top = 4 + floors * fh
    parts.append(box("Roof", (0, top + 1.0, 0), (w * 0.5, 2.0, d * 0.5), body_mat))
    parts.append(box("Sign", (0, top + 2.6, d * 0.26), (w * 0.4, 1.2, 0.2), m["neonteal"] if seed % 2 else m["neonpink"]))
    return finish(name, parts)


# ---------- мост, эстакада, тоннель ----------

def bridge_pylon(m):
    """Наклонный пилон вантового моста 58 м: два ствола, сходящиеся к вершине, светодиодные кромки.
    Стоит по центру проезжей части (между стволами — проём 26 м), x — поперёк дороги."""
    parts = []
    h = 58.0
    for s in (-1, 1):
        bm = bmesh.new()
        base_x, top_x = s * 15.0, s * 2.2
        pts = []
        for (y, x) in ((0.0, base_x), (h, top_x)):
            pts.append([(x - 1.4, y, -1.6), (x + 1.4, y, -1.6), (x + 1.4, y, 1.6), (x - 1.4, y, 1.6)])
        rings = [[C(*p) for p in pts[0]], [C(*p) for p in pts[1]]]
        vb = [bm.verts.new(v) for v in rings[0]]
        vt = [bm.verts.new(v) for v in rings[1]]
        bm.faces.new(list(reversed(vb)))
        bm.faces.new(vt)
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((vb[i], vb[j], vt[j], vt[i]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        parts.append(obj_from_bmesh("Leg", bm, [m["concrete"]]))
        # Светящаяся кромка по наружной грани ствола.
        bm2 = bmesh.new()
        e0 = bm2.verts.new(C(base_x + s * 1.45, 0.5, -0.2))
        e1 = bm2.verts.new(C(base_x + s * 1.45, 0.5, 0.2))
        e2 = bm2.verts.new(C(top_x + s * 1.45, h, 0.2))
        e3 = bm2.verts.new(C(top_x + s * 1.45, h, -0.2))
        bm2.faces.new((e0, e1, e2, e3))
        parts.append(obj_from_bmesh("Edge", bm2, [m["neonteal"]]))
    parts.append(box("Cross", (0, 30.0, 0), (20.0, 2.2, 3.0), m["concrete"]))
    parts.append(box("Head", (0, h + 2.5, 0), (6.0, 5.0, 3.4), m["concrete"]))
    parts.append(box("HeadGlow", (0, h + 5.2, 0), (6.4, 0.5, 3.8), m["neonteal"]))
    parts.append(box("Beacon", (0, h + 6.0, 0), (0.8, 0.8, 0.8), m["red"]))
    return finish("TA_BridgePylon", parts)


def highway_pier(m):
    """Опора эстакады высотой 10 м (по Y масштабируется под высоту полотна): колонна и ригель 22 м."""
    parts = [box("Column", (0, 4.6, 0), (3.0, 9.2, 2.4), m["concrete"], bevel=0.2)]
    parts.append(box("Cap", (0, 9.6, 0), (23.0, 0.8, 3.0), m["concrete"], bevel=0.1))
    parts.append(box("CapGlow", (0, 9.15, 1.52), (22.0, 0.12, 0.05), m["neonblue"]))
    parts.append(box("CapGlow", (0, 9.15, -1.52), (22.0, 0.12, 0.05), m["neonblue"]))
    return finish("TA_HighwayPier", parts)


def tunnel_portal(m):
    """Портал тоннеля: бетонная рама на 30 м проезда, светящаяся полоса и навес. Проём — по оси z."""
    parts = []
    w, h = 32.0, 9.0
    for s in (-1, 1):
        parts.append(box("Jamb", (s * (w / 2 + 1.5), h / 2, 0), (3.0, h, 2.4), m["concrete"], bevel=0.1))
    parts.append(box("Lintel", (0, h + 1.6, 0), (w + 6.0, 3.2, 2.4), m["concrete"], bevel=0.1))
    parts.append(box("Glow", (0, h + 0.2, -1.25), (w, 0.35, 0.1), m["neonyellow"]))
    parts.append(box("Sign", (0, h + 2.0, -1.3), (10.0, 1.6, 0.1), m["neonteal"]))
    parts.append(box("Planter", (0, h + 3.6, 0), (w + 6.0, 0.8, 2.4), m["sanddark"]))
    return finish("TA_TunnelPortal", parts)


# ---------- ориентиры и бульвар ----------

def clock_tower(m):
    """Часовая башня старого города: три яруса песчаника, светящиеся циферблаты на четыре стороны."""
    parts = [box("Base", (0, 4.0, 0), (7.0, 8.0, 7.0), m["sand"], bevel=0.15)]
    parts.append(box("Mid", (0, 12.0, 0), (5.6, 8.0, 5.6), m["sand"], bevel=0.12))
    parts.append(box("Top", (0, 19.0, 0), (4.6, 6.0, 4.6), m["sanddark"], bevel=0.1))
    for (dx, dz) in ((0, 1), (0, -1), (1, 0), (-1, 0)):
        rot_axis = 'z' if dz else 'x'
        parts.append(cylinder("Clock", (dx * 2.33, 19.2, dz * 2.33), 1.5, 0.1, rot_axis, m["clock"], segments=24))
    for k in range(3):
        y = 2.0 + k * 0.01
    parts.append(box("Arch", (0, 2.2, 3.52), (2.4, 4.4, 0.1), m["lit"]))
    parts.append(cylinder("Dome", (0, 23.4, 0), 2.0, 2.6, 'y', m["sanddark"], segments=16))
    parts.append(cylinder("Flag", (0, 26.6, 0), 0.08, 4.0, 'y', m["steel"], segments=6))
    return finish("TA_ClockTower", parts)


def lighthouse(m):
    """Маяк у устья реки: красно-белая башня 26 м со светящимся фонарём."""
    parts = []
    for k in range(6):
        y = k * 3.6
        parts.append(cylinder("Band", (0, y + 1.8, 0), 2.4 - k * 0.18, 3.6, 'y', m["red"] if k % 2 == 0 else m["white"], segments=20))
    parts.append(cylinder("Gallery", (0, 22.0, 0), 2.4, 0.5, 'y', m["trim"], segments=20))
    parts.append(cylinder("Lantern", (0, 23.6, 0), 1.3, 2.6, 'y', m["lamp"], segments=16))
    parts.append(cylinder("Cap", (0, 25.4, 0), 1.6, 1.0, 'y', m["red"], segments=16))
    return finish("TA_Lighthouse", parts)


def fountain(m):
    """Фонтан на площади: круглая чаша, светящаяся вода и струя."""
    parts = [cylinder("Bowl", (0, 0.4, 0), 7.0, 0.8, 'y', m["concrete"], segments=40)]
    parts.append(cylinder("Water", (0, 0.72, 0), 6.5, 0.1, 'y', m["water"], segments=40))
    parts.append(cylinder("Pillar", (0, 1.6, 0), 1.0, 2.4, 'y', m["concrete"], segments=16))
    parts.append(cylinder("Jet", (0, 4.2, 0), 0.35, 3.0, 'y', m["water"], segments=12))
    parts.append(cylinder("Cup", (0, 2.9, 0), 2.2, 0.3, 'y', m["water"], segments=24))
    return finish("TA_Fountain", parts)


def ficus(m, seed):
    """Круглое дерево бульвара (фикус): толстый ствол и шар кроны из нескольких «облаков»."""
    rnd = random.Random(seed)
    parts = [cylinder("Trunk", (0, 1.6, 0), 0.35, 3.2, 'y', m["trunk"], segments=10)]
    for k in range(5):
        a = k / 5 * math.tau
        r = 1.5 + rnd.uniform(-0.3, 0.3)
        c = (math.cos(a) * 1.3, 4.6 + rnd.uniform(-0.4, 0.5), math.sin(a) * 1.3)
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=2, radius=r)
        bmesh.ops.translate(bm, vec=C(*c), verts=bm.verts)
        ob = obj_from_bmesh("Crown", bm, [m["leaf"] if k % 2 else m["leaf2"]])
        smooth(ob, 60)
        parts.append(ob)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=1.9)
    bmesh.ops.translate(bm, vec=C(0, 5.4, 0), verts=bm.verts)
    top = obj_from_bmesh("CrownTop", bm, [m["leaf2"]])
    smooth(top, 60)
    parts.append(top)
    return finish(f"TA_Ficus_{seed}", parts)


reset_scene()
m = v2_materials()
built = [
    sky_twist(m), sky_blade(m), sky_stack(m), sky_disc(m),
    residential("TA_Resi_A", m, 22, 24.0, 18.0, 51, False),
    residential("TA_Resi_B", m, 28, 20.0, 20.0, 52, True),
    bridge_pylon(m), highway_pier(m), tunnel_portal(m),
    clock_tower(m), lighthouse(m), fountain(m), ficus(m, 0), ficus(m, 1),
]
for name, tris in built:
    print(f"{name}: {tris} tris")
