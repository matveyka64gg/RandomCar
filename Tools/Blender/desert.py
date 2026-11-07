# Модели карты «Пустыня»: скалы, столовые горы, стены каньона, кактусы, заправка-закусочная, водонапорная башня.
# Запуск: blender_client.py rc_lib.py desert.py. Координаты машины: x — вправо, y — вверх, z — «лицом» к трассе.

import random

DESERT_DIR = os.path.join(ART, "Desert")


def desert_materials():
    return dict(
        rock=mat("M_SandRock", "#C9875A", roughness=0.95),
        rockdark=mat("M_SandRockDark", "#9E6544", roughness=0.95),
        rocklight=mat("M_SandRockLight", "#E3B183", roughness=0.95),
        cactus=mat("M_Cactus", "#4F8F4A", roughness=0.8),
        wood=mat("M_Wood", "#7A5233", roughness=0.8),
        canvas=mat("M_Canvas", "#F0E2C4", roughness=0.8),
        steel=mat("M_Steel", "#B8C0CC", metallic=0.6, roughness=0.4),
        trim=mat("M_Trim", "#26282E", roughness=0.6),
        white=mat("M_Decal_White", "#F2F0E8", roughness=0.4),
        brand=mat("M_Brand", "#FF6A2A", roughness=0.5),
        branddark=mat("M_BrandDark", "#121826", roughness=0.5),
        teal=mat("M_PlasterTeal", "#5FB3A8", roughness=0.85),
        plaster=mat("M_Plaster", "#E8D9B5", roughness=0.85),
        shop=mat("M_WindowShop", "#FFE3B0", emission="#FFD08A", strength=2.2),
        dark=mat("M_WindowDark", "#2B3A52", metallic=0.2, roughness=0.1),
        neon=mat("M_Neon", "#FF4FA0", emission="#FF4FA0", strength=3.2),
        red=mat("M_KerbRed", "#E0352B", roughness=0.5),
        sign=mat("M_SignYellow", "#FFD23F", roughness=0.5),
        tire=mat("M_Tire", "#1C1C20", roughness=0.85),
    )


def finish(name, parts):
    ob = join(parts[0], parts[1:]) if len(parts) > 1 else parts[0]
    ob.name = name
    ob.data.name = name
    root = empty(name + "_Root")
    set_parent(ob, root)
    export_fbx(root, os.path.join(DESERT_DIR, name + ".fbx"))
    return ob


def faceted(ob):
    for p in ob.data.polygons:
        p.use_smooth = False


def strata_rock(name, radius, height, seed, m, sides=9, taper=0.82, layers=5, jitter=0.18):
    """Скала слоями: каждый слой — многоугольник со случайным контуром, цвета чередуются (полосы песчаника)."""
    rnd = random.Random(seed)
    parts = []
    layer_mats = [m["rock"], m["rocklight"], m["rock"], m["rockdark"], m["rocklight"]]
    y = 0.0
    r = radius
    for i in range(layers):
        h = height / layers * rnd.uniform(0.8, 1.2)
        rings = []
        r_top = r * rnd.uniform(taper - 0.06, taper + 0.04)
        for (yy, rr) in ((y, r), (y + h, r_top)):
            ring = []
            for k in range(sides):
                a = 2 * math.pi * k / sides
                jr = rr * (1 + rnd.uniform(-jitter, jitter))
                ring.append((math.cos(a) * jr, yy, math.sin(a) * jr))
            rings.append(ring)
        bm = loft(f"Layer{i}", rings, lambda a, b: 0)
        ob = obj_from_bmesh(f"Layer{i}", bm, [layer_mats[i % len(layer_mats)]])
        faceted(ob)
        parts.append(ob)
        y += h
        r = r_top * rnd.uniform(0.98, 1.05)
    return parts


def mesa(name, seed, m):
    parts = strata_rock(name, 38, 46, seed, m, sides=11, taper=0.93, layers=6, jitter=0.12)
    return finish(name, parts)


def spire(name, seed, m):
    parts = strata_rock(name, 7, 30, seed, m, sides=7, taper=0.72, layers=5, jitter=0.2)
    return finish(name, parts)


def boulder(name, size, seed, m):
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=1, radius=size)
    for v in bm.verts:
        v.co *= 1.0 + rnd.uniform(-0.25, 0.25)
        v.co.z = max(v.co.z * 0.75, -size * 0.2)
    bmesh.ops.translate(bm, vec=Vector((0, 0, size * 0.45)), verts=bm.verts)
    ob = obj_from_bmesh(name, bm, [m["rock"] if seed % 2 == 0 else m["rockdark"]])
    faceted(ob)
    return finish(name, [ob])


def canyon_wall(name, seed, m):
    """Участок стены каньона 24 м: зубчатый слоистый массив, лицевая сторона к трассе (+z)."""
    rnd = random.Random(seed)
    parts = []
    x = -12.0
    while x < 12.0:
        w = rnd.uniform(3.5, 6.5)
        h = rnd.uniform(14, 24)
        d = rnd.uniform(6, 10)
        layers = 4
        y = 0.0
        for i in range(layers):
            lh = h / layers
            shrink = i * rnd.uniform(0.2, 0.6)
            b = box("Block", (x + w / 2, y + lh / 2, -d / 2 - shrink), (w + rnd.uniform(-0.3, 0.6), lh, d), [m["rock"], m["rocklight"], m["rockdark"], m["rock"]][i], bevel=0.0)
            parts.append(b)
            y += lh
        x += w * rnd.uniform(0.75, 0.95)
    return finish(name, parts)


def saguaro(name, height, seed, m):
    rnd = random.Random(seed)
    parts = [cylinder("Trunk", (0, height / 2, 0), 0.32, height, 'y', m["cactus"], segments=10)]
    parts.append(cylinder("Top", (0, height, 0), 0.32, 0.3, 'y', m["cactus"], segments=10, bevel=0.12))
    for s in (-1, 1):
        if rnd.random() < 0.85:
            y0 = height * rnd.uniform(0.35, 0.55)
            reach = rnd.uniform(0.8, 1.2)
            up = height * rnd.uniform(0.25, 0.4)
            parts.append(cylinder("ArmOut", (s * reach / 2, y0, 0), 0.22, reach, 'x', m["cactus"], segments=8))
            parts.append(cylinder("ArmUp", (s * reach, y0 + up / 2, 0), 0.22, up, 'y', m["cactus"], segments=8, bevel=0.08))
    for p in parts:
        faceted(p)
    return finish(name, parts)


def dead_tree(name, seed, m):
    rnd = random.Random(seed)
    parts = [beam("Trunk", (0, 0, 0), (0.2, 3.2, 0.1), 0.25, 0.25, m["wood"])]
    for i in range(4):
        y = rnd.uniform(1.6, 3.0)
        a = rnd.uniform(0, 2 * math.pi)
        parts.append(beam("Branch", (0.1, y, 0.05), (math.cos(a) * 1.4, y + rnd.uniform(0.6, 1.2), math.sin(a) * 1.4), 0.1, 0.1, m["wood"]))
    return finish(name, parts)


def diner(m):
    """Заправка-закусочная «RANDOM 66»: павильон, навес над колонками, вывеска на столбе."""
    parts = [box("Hall", (0, 2.2, -8), (16, 4.4, 9), m["plaster"], bevel=0.06)]
    parts.append(box("Stripe", (0, 3.6, -3.45), (16.1, 0.6, 0.12), m["teal"]))
    parts.append(box("Window", (-2.5, 1.8, -3.45), (9, 2.2, 0.1), m["shop"]))
    parts.append(box("Door", (4.5, 1.3, -3.45), (1.6, 2.6, 0.1), m["dark"]))
    parts.append(box("Roof", (0, 4.55, -8), (16.6, 0.3, 9.6), m["branddark"]))
    # Навес над колонками.
    parts.append(box("Canopy", (0, 5.0, 3.5), (12, 0.5, 7), m["white"], bevel=0.05))
    parts.append(box("CanopyBand", (0, 4.8, 3.5), (12.1, 0.3, 7.1), m["brand"]))
    for x in (-4.5, 4.5):
        parts.append(box("Column", (x, 2.4, 3.5), (0.4, 4.8, 0.4), m["steel"]))
    for x in (-2.0, 2.0):
        parts.append(box("Pump", (x, 0.8, 3.5), (0.8, 1.6, 0.6), m["red"], bevel=0.05))
        parts.append(box("PumpScreen", (x, 1.2, 3.82), (0.5, 0.35, 0.04), m["dark"]))
    # Вывеска на высоком столбе.
    parts.append(box("SignPole", (9.5, 5, 2), (0.4, 10, 0.4), m["steel"]))
    parts.append(box("SignBoard", (9.5, 10.5, 2), (5.5, 2.6, 0.4), m["branddark"], bevel=0.08))
    t = text_mesh("SignText", "RANDOM 66", 0.85, m["neon"], extrude=0.05)
    t.rotation_euler = (math.radians(90), 0, 0)
    t.location = C(9.5, 10.5, 2.25)
    parts.append(t)
    return finish("Diner", parts)


def water_tower(m):
    parts = []
    for (x, z) in ((-2, -2), (2, -2), (-2, 2), (2, 2)):
        parts.append(beam("Leg", (x, 0, z), (x * 0.6, 12, z * 0.6), 0.3, 0.3, m["steel"]))
    parts.append(cylinder("Tank", (0, 14.5, 0), 3.2, 5.0, 'y', m["teal"], segments=20))
    parts.append(cone_like_roof(m))
    t = text_mesh("TowerText", "RANDOM", 1.0, m["white"], extrude=0.03)
    t.rotation_euler = (math.radians(90), 0, 0)
    t.location = C(0, 14.5, 3.25)
    parts.append(t)
    return finish("WaterTower", parts)


def cone_like_roof(m):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=20, radius1=3.4, radius2=0.2, depth=1.6)
    bmesh.ops.translate(bm, vec=C(0, 17.8, 0), verts=bm.verts)
    return obj_from_bmesh("TankRoof", bm, [m["brand"]])


def road_sign(name, text, m):
    parts = [box("Post", (0, 1.2, 0), (0.15, 2.4, 0.15), m["wood"])]
    parts.append(box("Board", (0, 2.3, 0.1), (2.4, 0.8, 0.08), m["sign"], bevel=0.02))
    t = text_mesh("Text", text, 0.32, m["branddark"], extrude=0.01)
    t.rotation_euler = (math.radians(90), 0, 0)
    t.location = C(0, 2.3, 0.15)
    parts.append(t)
    return finish(name, parts)


def tire_barrier(m):
    parts = []
    for k in range(5):
        for h in range(2):
            parts.append(cylinder("Tire", (-1.6 + k * 0.8, 0.13 + h * 0.26, 0), 0.38, 0.26, 'y', m["tire"] if (k + h) % 2 else m["red"], segments=14))
    return finish("TireBarrier", parts)


def build_desert():
    reset_scene()
    os.makedirs(PREVIEW, exist_ok=True)
    m = desert_materials()
    made = [mesa("Mesa_A", 1, m), mesa("Mesa_B", 2, m), spire("Spire_A", 3, m), spire("Spire_B", 4, m),
            boulder("Boulder_A", 1.4, 5, m), boulder("Boulder_B", 2.6, 6, m), boulder("Boulder_C", 4.5, 7, m),
            canyon_wall("CanyonWall_A", 8, m), canyon_wall("CanyonWall_B", 9, m),
            saguaro("Saguaro_A", 5.5, 10, m), saguaro("Saguaro_B", 7.0, 11, m), dead_tree("DeadTree", 12, m),
            diner(m), water_tower(m), road_sign("RoadSign_A", "КАНЬОН  →", m), road_sign("RoadSign_B", "ОСТОРОЖНО: ПЕСОК", m),
            tire_barrier(m)]
    layout = {"Mesa_A": (-120, 0, -150), "Spire_A": (-40, 0, -60), "Spire_B": (-20, 0, -50), "Boulder_A": (-6, 0, 4), "Boulder_B": (-2, 0, 6),
              "Boulder_C": (6, 0, 0), "CanyonWall_A": (-30, 0, -20), "Saguaro_A": (2, 0, 10), "Saguaro_B": (-4, 0, 12), "DeadTree": (8, 0, 10),
              "Diner": (25, 0, -10), "WaterTower": (45, 0, -25), "RoadSign_A": (0, 0, 16), "TireBarrier": (4, 0, 16)}
    for ob in made:
        root = ob.parent
        if ob.name in layout:
            root.location = C(*layout[ob.name])
        else:
            root.location = C(400, 0, 400)
    render_preview(os.path.join(PREVIEW, "desert.png"), (C(5, 4, 0).x, C(5, 4, 0).y, 4), 55, 12, 15, lens=28)
    for ob in made:
        print(f"{ob.name}: {tri_count(ob)} tris")


build_desert()
