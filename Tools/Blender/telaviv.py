# Модели карты «Тель-Авив» (ночь): пальмы, белые дома «баухаус», стеклянные башни, гостиницы, песчаник,
# набережная (фонари, спасательная вышка, зонты, шезлонги, киоск), неоновые вывески, пешеходный мост,
# ракета и перехватчик для ночного шоу. Запуск: blender_client.py rc_lib.py telaviv.py
# Координаты: x — вправо, y — вверх, z — к улице; начало — середина фасада у земли, здание уходит в -z.

import random

TA_DIR = os.path.join(ART, "TelAviv")


def lerp(a, b, t):
    return a + (b - a) * t


def ta_materials():
    return dict(
        white=mat("M_Bauhaus", "#EEE9DF", roughness=0.8),
        cream=mat("M_BauhausCream", "#E6D8BE", roughness=0.8),
        sand=mat("M_Sandstone", "#C9A878", roughness=0.9),
        sanddark=mat("M_SandstoneDark", "#A9875A", roughness=0.9),
        lit=mat("M_WindowLit", "#FFD9A0", emission="#FFC77A", strength=1.6),
        litcool=mat("M_WindowLitCool", "#CFE6FF", emission="#BFDCFF", strength=1.4),
        dark=mat("M_WindowDark", "#2B3A52", metallic=0.2, roughness=0.1),
        tower=mat("M_TowerGlass", "#FFFFFF", metallic=0.5, roughness=0.1),
        towerframe=mat("M_TowerFrame", "#2A3140", metallic=0.6, roughness=0.35),
        concrete=mat("M_Concrete", "#D3CEC4", roughness=0.85),
        trim=mat("M_Trim", "#26282E", roughness=0.6),
        steel=mat("M_Steel", "#B8C0CC", metallic=0.6, roughness=0.4),
        neonpink=mat("M_Neon", "#FF4FA0", emission="#FF4FA0", strength=3.2),
        neonteal=mat("M_NeonTeal", "#24E0C8", emission="#24E0C8", strength=3.0),
        neonblue=mat("M_NeonBlue", "#3F7BFF", emission="#3F7BFF", strength=3.0),
        neonyellow=mat("M_NeonYellow", "#FFD23F", emission="#FFC21A", strength=2.8),
        lamp=mat("M_LampGlow", "#FFF3D6", emission="#FFF3D6", strength=1.5),
        trunk=mat("M_PalmTrunk", "#7A6248", roughness=0.9),
        frond=mat("M_PalmFrond", "#3E7A3A", roughness=0.7),
        frond2=mat("M_PalmFrond2", "#4F8F3E", roughness=0.7),
        wood=mat("M_Wood", "#7A5233", roughness=0.8),
        red=mat("M_KerbRed", "#E0352B", roughness=0.5),
        canvas=mat("M_Canvas", "#F0E2C4", roughness=0.8),
        awning=[mat("M_Awning1", "#E0352B"), mat("M_Awning2", "#24A0E0"), mat("M_Awning3", "#2DAE6A")],
        rocket=mat("M_RocketBody", "#B9BCC2", metallic=0.5, roughness=0.4),
        rocketdark=mat("M_RocketDark", "#3A3D44", metallic=0.4, roughness=0.5),
        flame=mat("M_RocketFlame", "#FFB060", emission="#FFA040", strength=6.0),
        interceptor=mat("M_InterceptorBody", "#E8ECEF", metallic=0.3, roughness=0.4),
    )


def set_uv_box(ob, tile_u, tile_v):
    """UV по метрам: фасады (по нормали x или z) и крыша — текстура окон ложится сеткой этажей."""
    me = ob.data
    if not me.uv_layers:
        me.uv_layers.new(name="UVMap")
    uv = me.uv_layers.active.data
    for poly in me.polygons:
        n = poly.normal
        for li in poly.loop_indices:
            co = ob.matrix_world @ me.vertices[me.loops[li].vertex_index].co
            # Blender: X = -x, Y = -z, Z = y (машинные координаты).
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
    for o in [root, ob]:
        o.name = o.name + "_done"
    return ob


# ---------- пальмы ----------

def palm(name, m, height, lean, fronds, seed):
    rnd = random.Random(seed)
    parts = []
    # Ствол: кольца по изогнутой оси, к верху тоньше; «чешуя» — чередование радиуса.
    rings = []
    n = 14
    for k in range(n + 1):
        t = k / n
        y = height * t
        x = lean * t * t
        r = 0.22 * (1 - 0.35 * t) * (1.0 + (0.06 if k % 2 else 0.0))
        rings.append([(x + r * math.cos(2 * math.pi * j / 10), y, r * math.sin(2 * math.pi * j / 10)) for j in range(10)])
    bm = loft("Trunk", rings, lambda i, j: 0)
    trunk = obj_from_bmesh("Trunk", bm, [m["trunk"]])
    smooth(trunk, 60)
    parts.append(trunk)
    top = (lean, height, 0.0)
    for f in range(fronds):
        a = 2 * math.pi * f / fronds + rnd.uniform(-0.2, 0.2)
        length = rnd.uniform(3.2, 4.2)
        droop = rnd.uniform(0.6, 1.1)
        pts = []
        steps = 8
        for s in range(steps + 1):
            t = s / steps
            d = length * t
            yy = top[1] + 0.6 * t - droop * t * t * 1.8
            pts.append((top[0] + math.cos(a) * d, yy, top[2] + math.sin(a) * d, (1 - t) * 0.55 + 0.05))
        bm = bmesh.new()
        left, right = [], []
        for (x, y, z, w) in pts:
            px, pz = -math.sin(a) * w, math.cos(a) * w
            left.append(bm.verts.new(C(x + px, y - 0.04, z + pz)))
            right.append(bm.verts.new(C(x - px, y - 0.04, z - pz)))
        for s in range(steps):
            bm.faces.new((left[s], left[s + 1], right[s + 1], right[s]))
        ob = obj_from_bmesh("Frond", bm, [m["frond"] if f % 2 else m["frond2"]])
        parts.append(ob)
    for k in range(4):
        a = 2 * math.pi * k / 4
        parts.append(cylinder("Coconut", (top[0] + 0.18 * math.cos(a), top[1] - 0.25, 0.18 * math.sin(a)), 0.11, 0.2, 'y', m["trunk"], segments=8))
    return finish(name, parts)


# ---------- здания ----------

def window_grid(parts, m, rnd, x0, x1, z, y0, floors, floor_h, cols, w, h, lit_share, face_sign=1, cool=False):
    """Окна-рамки на фасаде (по плоскости z): светятся с вероятностью lit_share."""
    for f in range(floors):
        y = y0 + f * floor_h + floor_h * 0.5
        for c in range(cols):
            x = lerp(x0, x1, (c + 0.5) / cols)
            lit = rnd.random() < lit_share
            matw = (m["litcool"] if cool else m["lit"]) if lit else m["dark"]
            parts.append(box("Win", (x, y, z + face_sign * 0.03), (w, h, 0.06), matw))


def bauhaus(name, m, w, d, floors, seed, cream=False):
    """Белый дом «баухаус»: опоры-пилоны на первом этаже, ленточные балконы со скруглёнными углами, плоская крыша."""
    rnd = random.Random(seed)
    fh = 3.1
    h = 3.4 + floors * fh
    body = m["cream"] if cream else m["white"]
    parts = [box("Mass", (0, 3.4 + floors * fh / 2, -d / 2 - 0.6), (w - 1.2, floors * fh, d - 1.2), body, bevel=0.08)]
    # Первый этаж: пилоны и тёмная стеклянная витрина в глубине.
    parts.append(box("Lobby", (0, 1.7, -d / 2 - 2.0), (w - 3.0, 3.4, d - 4.0), m["dark"]))
    for k in range(5):
        x = lerp(-w / 2 + 1.0, w / 2 - 1.0, k / 4)
        parts.append(cylinder("Pilotis", (x, 1.7, -0.9), 0.22, 3.4, 'y', body, segments=12))
    # Ленточные балконы со скруглёнными концами.
    for f in range(floors):
        y = 3.4 + f * fh + 0.05
        parts.append(box("Balcony", (0, y + 0.55, -0.2), (w - 0.6, 1.1, 1.4), body, bevel=0.35, segments=4))
        parts.append(box("BalconyShadow", (0, y + 1.6, -0.75), (w - 1.6, 1.9, 0.08), m["dark"]))
        window_grid(parts, m, rnd, -w / 2 + 1.4, w / 2 - 1.4, -0.85, 3.4 + f * fh, 1, fh, max(3, int(w / 3.2)), 1.6, 1.5, 0.45)
    # Крыша: парапет и надстройка.
    parts.append(box("Parapet", (0, h + 0.4, -d / 2 - 0.6), (w - 1.0, 0.8, d - 1.0), body, bevel=0.05))
    parts.append(box("Roofhouse", (w * 0.2, h + 1.5, -d / 2 - 1.5), (w * 0.3, 2.2, d * 0.35), body, bevel=0.1))
    # Боковые окна.
    for side in (-1, 1):
        for f in range(floors):
            for k in range(2):
                z = -2.5 - k * (d - 5.0) / 1.5
                lit = rnd.random() < 0.4
                parts.append(box("SideWin", (side * (w / 2 - 0.57), 3.4 + f * fh + fh * 0.5, z), (0.06, 1.4, 1.4), m["lit"] if lit else m["dark"]))
    return finish(name, parts)


def glass_tower(name, m, w, d, h, shape, seed, crown):
    """Стеклянная башня: фасад с текстурой окон (свет этажей — в текстуре), рёбра, светящаяся корона."""
    rnd = random.Random(seed)
    parts = []
    if shape == "round":
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=True, segments=28, radius1=w / 2, radius2=w / 2 * 0.92, depth=h)
        bmesh.ops.translate(bm, vec=C(0, h / 2, -d / 2), verts=bm.verts)
        body = obj_from_bmesh("Tower", bm, [m["tower"]])
    elif shape == "prism":
        bm = bmesh.new()
        pts = [(-w / 2, 0.0), (w / 2, 0.0), (0.0, -d)]
        bottom = [bm.verts.new(C(x, 0, z)) for (x, z) in pts]
        topv = [bm.verts.new(C(x * 0.85, h, z * 0.85 - d * 0.05)) for (x, z) in pts]
        bm.faces.new(bottom)
        bm.faces.new(list(reversed(topv)))
        for i in range(3):
            j = (i + 1) % 3
            bm.faces.new((bottom[i], bottom[j], topv[j], topv[i]))
        body = obj_from_bmesh("Tower", bm, [m["tower"]])
    else:
        body = box("Tower", (0, h / 2, -d / 2), (w, h, d), m["tower"])
    set_uv_box(body, 4.0, 3.6)
    parts.append(body)
    # Вертикальные рёбра и горизонтальные пояса — масштаб и «дорогой» вид.
    if shape == "box":
        for k in range(6):
            x = lerp(-w / 2, w / 2, k / 5)
            parts.append(box("Fin", (x, h / 2, 0.15), (0.35, h, 0.3), m["towerframe"]))
        for k in range(int(h // 18)):
            parts.append(box("Belt", (0, (k + 1) * 18, -d / 2), (w + 0.4, 0.6, d + 0.4), m["towerframe"]))
    # Корона: светодиодный обод и шпиль.
    crown_mat = m[crown]
    if shape == "round":
        parts.append(cylinder("Crown", (0, h + 0.6, -d / 2), w / 2 * 0.95, 1.2, 'y', crown_mat, segments=28))
    else:
        parts.append(box("Crown", (0, h + 0.6, -d / 2), (w * 0.9, 1.2, d * 0.6), crown_mat))
    parts.append(cylinder("Spire", (0, h + 6, -d / 2), 0.3, 12, 'y', m["steel"], segments=8))
    parts.append(box("Beacon", (0, h + 12.2, -d / 2), (0.6, 0.6, 0.6), m["red"]))
    return finish(name, parts)


def hotel(name, m, w, d, h, seed):
    """Гостиница на набережной: высокая пластина с балконами и светящимися номерами."""
    rnd = random.Random(seed)
    parts = [box("Slab", (0, h / 2, -d / 2), (w, h, d), m["white"])]
    floors = int((h - 4) // 3.2)
    for f in range(floors):
        y = 4 + f * 3.2
        parts.append(box("Balcony", (0, y, 0.4), (w - 0.4, 0.25, 0.8), m["concrete"]))
        window_grid(parts, m, rnd, -w / 2 + 1.0, w / 2 - 1.0, 0.0, y, 1, 3.2, max(4, int(w / 3.5)), 2.2, 2.0, 0.55)
    parts.append(box("Sign", (0, h + 1.5, -0.6), (w * 0.5, 2.0, 0.4), m["neonblue"]))
    return finish(name, parts)


def sandstone(name, m, w, d, floors, seed):
    """Дом из песчаника (старый Яффо): арки, тёплый свет, плоская крыша с парапетом."""
    rnd = random.Random(seed)
    fh = 3.6
    h = floors * fh
    parts = [box("Mass", (0, h / 2, -d / 2), (w, h, d), m["sand"], bevel=0.1)]
    arches = max(2, int(w / 3.2))
    for k in range(arches):
        x = lerp(-w / 2 + 1.6, w / 2 - 1.6, (k + 0.5) / arches) if arches > 1 else 0
        parts.append(box("ArchDoor", (x, 1.4, 0.02), (1.6, 2.8, 0.1), m["lit"] if rnd.random() < 0.6 else m["dark"]))
        parts.append(cylinder("ArchTop", (x, 2.8, 0.03), 0.8, 0.1, 'z', m["lit"] if rnd.random() < 0.5 else m["dark"], segments=12))
    for f in range(1, floors):
        window_grid(parts, m, rnd, -w / 2 + 1.0, w / 2 - 1.0, 0.0, f * fh, 1, fh, arches, 1.0, 1.6, 0.5)
    parts.append(box("Parapet", (0, h + 0.35, -d / 2), (w + 0.2, 0.7, d + 0.2), m["sanddark"]))
    return finish(name, parts)


# ---------- набережная ----------

def promenade_lamp(m):
    parts = [cylinder("Pole", (0, 3.0, 0), 0.08, 6.0, 'y', m["trim"], segments=10)]
    for s in (-1, 1):
        parts.append(beam("Arm", (0, 5.8, 0), (s * 0.9, 6.1, 0), 0.06, 0.06, m["trim"]))
        parts.append(box("Head", (s * 1.0, 6.0, 0), (0.5, 0.18, 0.3), m["trim"], bevel=0.04))
        parts.append(box("Glow", (s * 1.0, 5.9, 0), (0.42, 0.04, 0.24), m["lamp"]))
    return finish("TA_PromenadeLamp", parts)


def lifeguard(m):
    parts = []
    for x in (-1.0, 1.0):
        for z in (-1.0, 1.0):
            parts.append(beam("Leg", (x, 0, z), (x * 0.8, 2.6, z * 0.8), 0.14, 0.14, m["wood"]))
    parts.append(box("Cabin", (0, 3.6, 0), (2.4, 2.0, 2.2), m["white"], bevel=0.05))
    parts.append(box("Stripe", (0, 3.0, 1.12), (2.4, 0.4, 0.05), m["red"]))
    parts.append(box("Window", (0, 3.9, 1.12), (1.8, 0.8, 0.06), m["lit"]))
    parts.append(box("Roof", (0, 4.75, 0), (2.8, 0.25, 2.6), m["red"], bevel=0.05))
    parts.append(beam("Ramp", (0, 0, 3.2), (0, 2.6, 1.1), 0.9, 0.08, m["wood"]))
    return finish("TA_Lifeguard", parts)


def umbrella(m, i):
    parts = [cylinder("Pole", (0, 1.2, 0), 0.04, 2.4, 'y', m["steel"], segments=8)]
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, segments=16, radius1=1.4, radius2=0.05, depth=0.5)
    bmesh.ops.translate(bm, vec=C(0, 2.35, 0), verts=bm.verts)
    parts.append(obj_from_bmesh("Canopy", bm, [m["awning"][i % 3]]))
    parts.append(box("Lounger", (0.9, 0.25, 0.6), (0.6, 0.12, 1.8), m["white"], bevel=0.03))
    return finish(f"TA_Umbrella_{i}", parts)


def kiosk(m, i, neon):
    parts = [box("Body", (0, 1.5, -2.0), (5.0, 3.0, 4.0), m["white"], bevel=0.05)]
    parts.append(box("Counter", (0, 1.0, 0.1), (4.4, 1.1, 0.4), m["wood"]))
    parts.append(box("Opening", (0, 2.0, 0.0), (4.4, 1.2, 0.1), m["lit"]))
    parts.append(box("Awning", (0, 3.1, 0.6), (5.4, 0.12, 1.6), m["awning"][i % 3], bevel=0.02))
    parts.append(box("Neon", (0, 3.7, 0.05), (3.0, 0.5, 0.12), m[neon]))
    for x in (-1.6, 0.0, 1.6):
        parts.append(cylinder("Stool", (x, 0.38, 0.8), 0.18, 0.76, 'y', m["steel"], segments=10))
    return finish(f"TA_Kiosk_{i}", parts)


def neon_sign(m, i):
    """Неоновая вывеска: тёмная рама и светящиеся трубки (абстрактные полосы и круги)."""
    colors = ["neonpink", "neonteal", "neonblue", "neonyellow"]
    c1, c2 = m[colors[i % 4]], m[colors[(i + 1) % 4]]
    parts = [box("Frame", (0, 0, 0), (4.0, 1.6, 0.15), m["trim"], bevel=0.03)]
    for k in range(3):
        parts.append(box("Tube", (-1.2 + k * 1.2, 0.2, 0.1), (0.9, 0.08, 0.06), c1))
    parts.append(cylinder("Ring", (1.4, -0.3, 0.1), 0.35, 0.05, 'z', c2, segments=20))
    parts.append(box("Under", (-0.4, -0.45, 0.1), (2.4, 0.07, 0.06), c2))
    return finish(f"TA_Neon_{i}", parts)


def footbridge(m, span):
    """Пешеходный мост над трассой: пролёт с подсветкой, лестничные опоры."""
    h = 6.2
    parts = [box("Deck", (0, h, 0), (span, 0.5, 3.2), m["concrete"], bevel=0.05)]
    for s in (-1, 1):
        parts.append(box("Rail", (0, h + 0.75, s * 1.55), (span, 1.0, 0.08), m["dark"]))
        parts.append(box("Led", (0, h - 0.27, s * 1.62), (span, 0.08, 0.06), m["neonteal"]))
        parts.append(box("Pier", (s * (span / 2 - 0.6), h / 2, 0), (1.0, h, 2.6), m["concrete"]))
    parts.append(box("Banner", (0, h + 1.9, 1.62), (span * 0.4, 1.4, 0.1), m["neonpink"]))
    return finish("TA_Footbridge", parts)


def rocket(m):
    """Ракета для шоу (вымышленная, без опознавательных знаков): корпус, оперение, сопло с пламенем. Ось — +z."""
    parts = [cylinder("Body", (0, 0, 0), 0.18, 2.4, 'z', m["rocket"], segments=12)]
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, segments=12, radius1=0.18, radius2=0.01, depth=0.7)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(math.radians(90), 3, 'X'))
    bmesh.ops.translate(bm, vec=C(0, 0, 1.55), verts=bm.verts)
    parts.append(obj_from_bmesh("Nose", bm, [m["rocketdark"]]))
    for k in range(4):
        a = math.pi / 2 * k
        parts.append(box("Fin", (0.28 * math.cos(a), 0.28 * math.sin(a), -1.0), (0.04 if k % 2 == 0 else 0.3, 0.3 if k % 2 == 0 else 0.04, 0.5), m["rocketdark"]))
    parts.append(cylinder("Flame", (0, 0, -1.45), 0.14, 0.5, 'z', m["flame"], segments=10))
    return finish("TA_Rocket", parts)


def interceptor(m):
    parts = [cylinder("Body", (0, 0, 0), 0.08, 1.6, 'z', m["interceptor"], segments=10)]
    for k in range(4):
        a = math.pi / 2 * k
        parts.append(box("Fin", (0.12 * math.cos(a), 0.12 * math.sin(a), -0.6), (0.03 if k % 2 == 0 else 0.16, 0.16 if k % 2 == 0 else 0.03, 0.3), m["rocketdark"]))
    parts.append(cylinder("Flame", (0, 0, -0.95), 0.07, 0.3, 'z', m["flame"], segments=8))
    return finish("TA_Interceptor", parts)


def build_telaviv():
    reset_scene()
    m = ta_materials()
    made = []
    made.append(palm("TA_Palm_A", m, 9.5, 0.8, 11, 1))
    made.append(palm("TA_Palm_B", m, 12.0, -1.3, 12, 2))
    made.append(palm("TA_Palm_C", m, 7.0, 0.3, 10, 3))
    made.append(bauhaus("TA_Bauhaus_A", m, 18.0, 13.0, 3, 11))
    made.append(bauhaus("TA_Bauhaus_B", m, 22.0, 14.0, 4, 12, cream=True))
    made.append(bauhaus("TA_Bauhaus_C", m, 15.0, 12.0, 4, 13))
    made.append(glass_tower("TA_Tower_A", m, 30.0, 30.0, 120.0, "box", 21, "neonteal"))
    made.append(glass_tower("TA_Tower_B", m, 34.0, 34.0, 150.0, "round", 22, "neonblue"))
    made.append(glass_tower("TA_Tower_C", m, 40.0, 30.0, 95.0, "prism", 23, "neonpink"))
    made.append(glass_tower("TA_Tower_D", m, 24.0, 24.0, 70.0, "box", 24, "neonyellow"))
    made.append(hotel("TA_Hotel_A", m, 36.0, 16.0, 52.0, 31))
    made.append(hotel("TA_Hotel_B", m, 28.0, 14.0, 38.0, 32))
    made.append(sandstone("TA_Sandstone_A", m, 12.0, 10.0, 2, 41))
    made.append(sandstone("TA_Sandstone_B", m, 16.0, 11.0, 3, 42))
    made.append(promenade_lamp(m))
    made.append(lifeguard(m))
    for i in range(3):
        made.append(umbrella(m, i))
    for i, neon in enumerate(["neonpink", "neonteal", "neonyellow"]):
        made.append(kiosk(m, i, neon))
    for i in range(4):
        made.append(neon_sign(m, i))
    made.append(footbridge(m, 22.0))
    made.append(rocket(m))
    made.append(interceptor(m))
    print("telaviv models:", len(made))


build_telaviv()
