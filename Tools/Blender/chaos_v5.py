# Модели хаоса v5 («MAXIMUM CHAOS»): пружина-люк, гигантская рука, вентилятор-ураган, магнит, наковальня,
# пинбол-бампер, жвачка, масляное пятно, резиновая утка, пушка и ядро, НЛО с лучом, кувалда, воронка смерча.
# Запуск: py -3 -I Tools/Blender/blender_client.py Tools/Blender/rc_lib.py Tools/Blender/chaos_lib.py Tools/Blender/chaos_v5.py
# Координаты как в Unity: x — вправо, y — вверх, z — «вперёд» объекта.
# Физические объекты (утка, ядро) — начало координат в центре; остальные — у основания или в точке поворота.


def v5_materials(m):
    m.update(dict(
        springTop=mat("M_SpringTop", "#FF3B3B", roughness=0.35),
        lid=mat("M_Lid", "#59606B", metallic=0.5, roughness=0.45),
        gloveWhite=mat("M_GloveWhite", "#F6F3EA", roughness=0.5),
        fanBody=mat("M_FanBody", "#1FB5A8", roughness=0.45),
        magnet=mat("M_MagnetRed", "#E8262C", roughness=0.3),
        anvil=mat("M_Anvil", "#3A3F47", metallic=0.7, roughness=0.35),
        bumperCap=mat("M_BumperCap", "#FF3D5A", roughness=0.3, emission="#FF3D5A", strength=1.5),
        bumperRing=mat("M_BumperRing", "#FFB000", roughness=0.3, emission="#FFB000", strength=3.0),
        gum=mat("M_Gum", "#FF7EB6", roughness=0.3),
        gumHi=mat("M_GumHi", "#FFC3DD", roughness=0.2),
        oil=mat("M_Oil", "#121419", metallic=0.4, roughness=0.06),
        sheenA=mat("M_OilSheenA", "#6A4CFF", roughness=0.05),
        sheenB=mat("M_OilSheenB", "#19C6B0", roughness=0.05),
        duck=mat("M_DuckYellow", "#FFD21F", roughness=0.3),
        iron=mat("M_CannonIron", "#2B2E35", metallic=0.6, roughness=0.4),
        hull=mat("M_UfoHull", "#B9C3CF", metallic=0.85, roughness=0.25),
        dome=mat("M_UfoDome", "#5FE3FF", roughness=0.05),
        ufoLight=mat("M_UfoLight", "#7CFF4F", emission="#7CFF4F", strength=5.0),
        alien=mat("M_Alien", "#7CE05A", roughness=0.4),
        beam=mat("M_UfoBeam", "#7CFF4F", alpha=0.3),
        malletWood=mat("M_MalletWood", "#D9A066", roughness=0.6),
        tornado=mat("M_Tornado", "#8C8274", alpha=0.4),
    ))
    return m


def blob(name, radius_x, radius_z, height, material, seed, lobes=7, segments=48):
    """Плоская неровная клякса на земле (жвачка, масло): контур из нескольких «лепестков»."""
    rnd = random.Random(seed)
    phases = [rnd.uniform(0, math.tau) for _ in range(3)]
    amps = [rnd.uniform(0.08, 0.16), rnd.uniform(0.05, 0.1), rnd.uniform(0.03, 0.06)]
    freq = [lobes, lobes + 3, 2]
    bm = bmesh.new()
    top, bottom = [], []
    for i in range(segments):
        a = i / segments * math.tau
        k = 1.0 + sum(amp * math.sin(f * a + ph) for amp, f, ph in zip(amps, freq, phases))
        x, z = math.cos(a) * radius_x * k, math.sin(a) * radius_z * k
        top.append(bm.verts.new(C(x, height, z)))
        bottom.append(bm.verts.new(C(x * 1.03, 0.0, z * 1.03)))
    center = bm.verts.new(C(0, height * 1.15, 0))
    for i in range(segments):
        j = (i + 1) % segments
        bm.faces.new((top[i], top[j], center))
        bm.faces.new((bottom[i], bottom[j], top[j], top[i]))
    ob = obj_from_bmesh(name, bm, [material])
    smooth(ob, 60)
    return ob


# ---------- пружина-люк ----------

def spring(m):
    base = [cylinder("Base", (0, 0.05, 0), 1.4, 0.1, 'y', m["rivet"], segments=32, bevel=0.02)]
    base.append(torus("Stripe", (0, 0.11, 0), 1.32, 0.05, 'y', m["yellow"], seg=40, ring=8))
    # Пружина единичной высоты (растягивается масштабом по y), витки — наклонные кольца.
    coil = []
    for k in range(9):
        coil.append(torus("Turn", (0, 0.06 + k * 0.11, 0), 0.75, 0.075, 'y', m["steel"], seg=28, ring=8))
    coil_ob = join(coil[0], coil[1:])
    top = [cylinder("Plate", (0, 0, 0), 1.25, 0.2, 'y', m["springTop"], segments=32, bevel=0.05)]
    top.append(torus("Rim", (0, 0.1, 0), 1.2, 0.07, 'y', m["yellow"], seg=36, ring=8))
    # Крышка-люк сверху пружины с жёлтым знаком «?».
    top.append(cylinder("Lid", (0, 0.13, 0), 1.05, 0.06, 'y', m["lid"], segments=32))
    q = text_mesh("Mark", "?", 1.5, m["yellow"], extrude=0.02)
    q.location = C(0, 0.17, 0)
    bpy.context.view_layer.update()
    apply_transform(q)
    top.append(q)
    top_ob = join(top[0], top[1:])
    return export_event("GagSpring", base, {"Coil": (coil_ob, (0, 0, 0)), "Top": (top_ob, (0, 0, 0))})


def unity_map(ob, fn):
    """Преобразовать вершины меша функцией в координатах Unity (x, y, z) -> (x, y, z)."""
    for v in ob.data.vertices:
        p = v.co
        x, y, z = fn(-p.x, p.z, -p.y)
        v.co = C(x, y, z)
    ob.data.update()


def apply_transform(ob):
    """Запечь положение и поворот объекта в меш (для текста)."""
    ob.data.transform(ob.matrix_world)
    ob.matrix_world = Matrix.Identity(4)


# ---------- гигантская рука ----------

def hand(m):
    # Мультяшная перчатка ладонью к +x (к дороге, если стоит слева). Начало — запястье у земли (точка поворота).
    w = m["gloveWhite"]
    parts = [cylinder("Cuff", (0, 0.6, 0), 1.05, 1.2, 'y', w, segments=28, bevel=0.08)]
    parts.append(torus("CuffRoll", (0, 1.25, 0), 1.0, 0.22, 'y', w, seg=32, ring=10))
    parts.append(sphere("Palm", (0, 2.6, 0), 1.35, [w], u=28, v=16, scale=(0.55, 1.15, 1.05)))
    # Пальцы веером вверх, большой — вбок.
    for k, (z, length) in enumerate([(-0.95, 1.5), (-0.32, 1.85), (0.32, 1.9), (0.95, 1.6)]):
        y0 = 3.6
        tilt = (k - 1.5) * 0.12
        y1 = y0 + length
        parts.append(cylinder("Finger", (0, (y0 + y1) / 2, z + tilt * length * 0.5), 0.33, length, 'y', w, segments=16))
        parts.append(sphere("Tip", (0, y1, z + tilt * length), 0.34, [w], u=14, v=8))
    parts.append(sphere("Thumb", (0.15, 2.4, -1.35), 0.38, [w], u=14, v=8, scale=(1, 1, 2.4)))
    # Три чёрные линии на тыльной стороне — как у мультяшной перчатки.
    for k in (-0.45, 0, 0.45):
        parts.append(box("Line", (-0.74, 2.7, k), (0.04, 1.2, 0.08), m["black"]))
    return export_event("GiantHand", parts)


# ---------- вентилятор ----------

def fan(m):
    # Дует вдоль +z. Ось лопастей на высоте 4 м.
    hub_y = 4.0
    body = [box("Foot", (0, 0.25, 0), (3.0, 0.5, 2.4), m["fanBody"], bevel=0.08)]
    body.append(cylinder("Pole", (0, 2.0, -0.4), 0.35, 3.2, 'y', m["steel"], segments=16))
    body.append(cylinder("Motor", (0, hub_y, -0.9), 0.9, 1.4, 'z', m["fanBody"], segments=24, bevel=0.06))
    body.append(torus("CageFront", (0, hub_y, 0.55), 3.0, 0.08, 'z', m["steel"], seg=48, ring=8))
    body.append(torus("CageBack", (0, hub_y, -0.35), 3.0, 0.08, 'z', m["steel"], seg=48, ring=8))
    for k in range(12):
        a = k / 12 * math.tau
        body.append(beam("Spoke", (math.cos(a) * 0.4, hub_y + math.sin(a) * 0.4, 0.55), (math.cos(a) * 3.0, hub_y + math.sin(a) * 3.0, 0.55), 0.05, 0.05, m["steel"]))
        body.append(beam("Bar", (math.cos(a) * 3.0, hub_y + math.sin(a) * 3.0, -0.35), (math.cos(a) * 3.0, hub_y + math.sin(a) * 3.0, 0.55), 0.05, 0.05, m["steel"]))
    body.append(sphere("Badge", (0, hub_y, 0.6), 0.35, [m["springTop"]], u=16, v=8, scale=(1, 1, 0.4)))
    blades = [sphere("Hub", (0, hub_y, 0.1), 0.45, [m["springTop"]], u=16, v=8, scale=(1, 1, 0.7))]
    for k in range(3):
        a = k / 3 * math.tau
        cx, cy = math.cos(a) * 1.55, math.sin(a) * 1.55
        b = sphere("Blade", (0, 0, 0), 1.2, [m["yellow"]], u=16, v=8, scale=(1, 0.42, 0.08))
        # Лопасть вдоль радиуса: поворот вокруг оси вентилятора (z) в координатах Unity, затем на место.
        unity_map(b, lambda x, y, z: (x * math.cos(a) - y * math.sin(a) + cx, x * math.sin(a) + y * math.cos(a) + hub_y + cy, z + 0.1))
        blades.append(b)
    blades_ob = join(blades[0], blades[1:])
    return export_event("GiantFan", body, {"Blades": (blades_ob, (0, hub_y, 0.1))})


# ---------- магнит ----------

def magnet(m):
    # Подкова концами вниз: дуга сверху, начало координат — центр дуги.
    R, r = 1.6, 0.55
    parts = []
    bm = bmesh.new()
    seg, ring = 24, 12
    verts = []
    for i in range(seg + 1):
        a = math.pi * i / seg
        row = []
        for j in range(ring):
            b = j / ring * math.tau
            rr = R + r * math.cos(b)
            row.append(bm.verts.new(C(math.cos(a) * rr, math.sin(a) * rr, r * math.sin(b))))
        verts.append(row)
    for i in range(seg):
        for j in range(ring):
            k = (j + 1) % ring
            bm.faces.new((verts[i][j], verts[i + 1][j], verts[i + 1][k], verts[i][k]))
    arc = obj_from_bmesh("Arc", bm, [m["magnet"]])
    smooth(arc, 80)
    parts.append(arc)
    for s in (-1, 1):
        parts.append(cylinder("Leg", (s * R, -1.1, 0), r, 2.2, 'y', m["magnet"], segments=24))
        parts.append(cylinder("Tip", (s * R, -2.55, 0), r * 1.02, 0.7, 'y', m["chrome"], segments=24))
    parts.append(box("Hook", (0, R + r + 0.3, 0), (0.25, 0.6, 0.25), m["steel"]))
    parts.append(torus("Ring", (0, R + r + 0.85, 0), 0.4, 0.09, 'z', m["steel"], seg=20, ring=6))
    return export_event("Magnet", parts)


# ---------- наковальня ----------

def anvil(m):
    a = m["anvil"]
    parts = [box("Foot", (0, 0.2, 0), (1.5, 0.4, 2.0), a, bevel=0.05)]
    parts.append(box("Waist", (0, 0.75, 0), (0.85, 0.7, 1.3), a, bevel=0.05))
    parts.append(box("Face", (0, 1.35, -0.15), (1.25, 0.5, 2.3), a, bevel=0.06))
    parts.append(cone("Horn", (0, 1.38, 1.0), (0, 1.3, 2.3), 0.42, a, segments=16))
    parts.append(box("Shine", (0, 1.605, -0.2), (1.0, 0.01, 1.6), m["chrome"]))
    for s in (-1, 1):
        t = text_mesh("Weight", "1 Т", 0.55, m["white"], extrude=0.01)
        t.rotation_euler = (math.radians(90), 0, math.radians(-90 * s))
        t.location = C(s * 0.64, 1.35, -0.15)
        bpy.context.view_layer.update()
        apply_transform(t)
        parts.append(t)
    return export_event("Anvil", parts)


# ---------- пинбол-бампер ----------

def bumper(m):
    parts = [cylinder("Base", (0, 0.15, 0), 1.45, 0.3, 'y', m["rivet"], segments=32, bevel=0.04)]
    parts.append(cylinder("Body", (0, 0.75, 0), 1.0, 0.9, 'y', m["white"], segments=32))
    parts.append(sphere("Cap", (0, 1.2, 0), 1.1, [m["bumperCap"]], u=32, v=12, scale=(1, 0.45, 1)))
    parts.append(sphere("Star", (0, 1.7, 0), 0.25, [m["yellow"]], u=12, v=6))
    ring = torus("Ring", (0, 0.75, 0), 1.25, 0.16, 'y', m["bumperRing"], seg=40, ring=10)
    return export_event("PinballBumper", parts, {"Ring": (ring, (0, 0.75, 0))})


# ---------- жвачка и масло ----------

def gum(m):
    parts = [blob("Gum", 1.9, 1.6, 0.12, m["gum"], 11)]
    parts.append(blob("GumHi", 0.6, 0.45, 0.14, m["gumHi"], 12, lobes=4, segments=24))
    parts[-1].location = C(-0.5, 0, -0.4)
    bpy.context.view_layer.update()
    apply_transform(parts[-1])
    bubble = sphere("Bubble", (0.4, 0.75, 0.3), 0.7, [m["gum"], m["gumHi"]], lambda x, y, z: 1 if y > 0.45 and x < 0 else 0, u=24, v=12)
    return export_event("GumPuddle", parts, {"Bubble": (bubble, (0.4, 0.1, 0.3))})


def oil(m):
    parts = [blob("Oil", 2.6, 1.9, 0.05, m["oil"], 21, lobes=6)]
    rnd = random.Random(22)
    for k in range(5):
        sheen = blob("Sheen", rnd.uniform(0.3, 0.6), rnd.uniform(0.2, 0.4), 0.056, m["sheenA"] if k % 2 == 0 else m["sheenB"], 30 + k, lobes=4, segments=20)
        sheen.location = C(rnd.uniform(-1.4, 1.4), 0, rnd.uniform(-1.0, 1.0))
        bpy.context.view_layer.update()
        apply_transform(sheen)
        parts.append(sheen)
    return export_event("OilSlick", parts)


# ---------- утка, пушка и ядро ----------

def duck(m):
    # Начало координат — центр тела (физический объект, коллайдер — сфера 1.3 м). Смотрит вдоль +z.
    d = m["duck"]
    parts = [sphere("Body", (0, -0.25, -0.1), 1.25, [d], u=28, v=16, scale=(1.0, 0.8, 1.25))]
    parts.append(sphere("Tail", (0, 0.25, -1.35), 0.45, [d], u=14, v=8, scale=(1, 0.9, 1.1)))
    parts.append(sphere("Head", (0, 0.75, 0.75), 0.72, [d], u=24, v=14))
    parts.append(sphere("Beak", (0, 0.6, 1.45), 0.4, [m["beak"]], u=16, v=8, scale=(1.1, 0.42, 1.0)))
    for s in (-1, 1):
        parts.append(sphere("Eye", (s * 0.3, 0.98, 1.27), 0.17, [m["white"]], u=12, v=8, scale=(1, 1.15, 0.6)))
        parts.append(sphere("Pupil", (s * 0.31, 0.98, 1.36), 0.08, [m["black"]], u=8, v=6, scale=(1, 1.2, 0.6)))
        parts.append(sphere("Wing", (s * 1.05, -0.2, -0.15), 0.45, [d], u=12, v=8, scale=(0.35, 0.8, 1.4)))
    return export_event("RubberDuck", parts)


def cannon(m):
    # Ствол смотрит вдоль +z; лафет у земли. Ствол — подвижная часть (поворот вокруг цапф).
    body = [box("Carriage", (0, 0.75, -0.3), (1.3, 0.7, 2.6), m["wood"], bevel=0.06)]
    for s in (-1, 1):
        body.append(cylinder("Wheel", (s * 0.85, 0.75, 0.3), 0.75, 0.22, 'x', m["woodlight"], segments=24, bevel=0.03))
        body.append(cylinder("Hub", (s * 0.98, 0.75, 0.3), 0.18, 0.12, 'x', m["iron"], segments=12))
        for k in range(6):
            a = k / 6 * math.tau
            body.append(beam("Spoke", (s * 0.85, 0.75, 0.3), (s * 0.85, 0.75 + math.sin(a) * 0.65, 0.3 + math.cos(a) * 0.65), 0.08, 0.08, m["wood"]))
    body.append(box("Trail", (0, 0.3, -1.9), (0.5, 0.25, 1.4), m["wood"]))
    barrel = [cylinder("Tube", (0, 1.45, 0.6), 0.55, 3.6, 'z', m["iron"], segments=28, bevel=0.03)]
    barrel.append(cylinder("Muzzle", (0, 1.45, 2.4), 0.68, 0.3, 'z', m["iron"], segments=28, bevel=0.05))
    barrel.append(cylinder("Breech", (0, 1.45, -1.2), 0.62, 0.3, 'z', m["iron"], segments=28))
    barrel.append(sphere("Knob", (0, 1.45, -1.5), 0.3, [m["iron"]], u=14, v=8))
    for z in (-0.4, 0.8, 1.9):
        barrel.append(cylinder("Band", (0, 1.45, z), 0.6, 0.14, 'z', m["gold"], segments=28))
    barrel.append(cylinder("Trunnion", (0, 1.45, 0.3), 0.15, 1.5, 'x', m["iron"], segments=12))
    barrel_ob = join(barrel[0], barrel[1:])
    root = export_event("Cannon", body, {"Barrel": (barrel_ob, (0, 1.45, 0.3))})
    ball = [sphere("Ball", (0, 0, 0), 0.9, [m["iron"], m["chrome"]], lambda x, y, z: 1 if y > 0.6 and z > 0.2 and x < 0 else 0, u=28, v=14)]
    ball_root = export_event("Cannonball", ball)
    return root, ball_root


# ---------- НЛО ----------

def ufo(m):
    parts = [sphere("Saucer", (0, 0, 0), 3.4, [m["hull"]], u=40, v=16, scale=(1, 0.22, 1))]
    parts.append(torus("Rim", (0, 0, 0), 3.35, 0.16, 'y', m["steel"], seg=48, ring=8))
    parts.append(sphere("Belly", (0, -0.55, 0), 1.3, [m["steel"]], u=24, v=10, scale=(1, 0.35, 1)))
    for k in range(10):
        a = k / 10 * math.tau
        parts.append(sphere("Light", (math.cos(a) * 3.0, 0.05, math.sin(a) * 3.0), 0.22, [m["ufoLight"]], u=10, v=6))
    parts.append(sphere("Dome", (0, 0.55, 0), 1.45, [m["dome"]], u=28, v=12, scale=(1, 0.75, 1)))
    # Зелёный пилот выглядывает из купола: голова, глаза, антенны.
    parts.append(sphere("Alien", (0, 1.45, 0.2), 0.62, [m["alien"]], u=20, v=12, scale=(1, 1.1, 1)))
    for s in (-1, 1):
        parts.append(sphere("AlienEye", (s * 0.24, 1.55, 0.72), 0.17, [m["black"]], u=10, v=8, scale=(1, 1.5, 0.6)))
        parts.append(beam("Antenna", (s * 0.2, 1.95, 0.15), (s * 0.45, 2.55, 0.1), 0.05, 0.05, m["alien"]))
        parts.append(sphere("AntennaTip", (s * 0.45, 2.6, 0.1), 0.12, [m["ufoLight"]], u=8, v=6))
    root = export_event("Ufo", parts)
    # Луч: конус от днища вниз на 16 м (прозрачный, светится).
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=False, cap_tris=False, segments=28, radius1=3.2, radius2=1.1, depth=16.0)
    bmesh.ops.translate(bm, vec=C(0, -8.0, 0), verts=bm.verts)
    beam_ob = obj_from_bmesh("Beam", bm, [m["beam"]])
    beam_root = export_event("UfoBeam", [beam_ob])
    return root, beam_root


# ---------- кувалда ----------

def mallet(m):
    # Начало координат — конец рукояти (ось поворота вдоль z). Рукоять вверх, боёк поперёк (вдоль z).
    parts = [cylinder("Handle", (0, 4.0, 0), 0.32, 8.0, 'y', m["wood"], segments=16, bevel=0.03)]
    parts.append(cylinder("Grip", (0, 1.0, 0), 0.38, 1.6, 'y', m["springTop"], segments=16))
    parts.append(cylinder("Head", (0, 8.9, 0), 1.7, 4.4, 'z', m["malletWood"], segments=32, bevel=0.1))
    for s in (-1, 1):
        parts.append(cylinder("Band", (0, 8.9, s * 1.6), 1.76, 0.4, 'z', m["head"], segments=32))
        parts.append(cylinder("Face", (0, 8.9, s * 2.21), 1.45, 0.04, 'z', m["malletWood"], segments=32))
    return export_event("GiantMallet", parts)


# ---------- смерч ----------

def tornado(m):
    # Закрученная воронка: узкая внизу, широкая вверху, 22 м высотой. Прозрачная (сам вихрь — частицы).
    rings = []
    n = 24
    for i in range(14):
        t = i / 13
        y = t * 22.0
        r = 0.9 + 6.0 * t ** 1.6
        twist = t * 2.4
        wob = 0.6 * math.sin(t * 5.0)
        rings.append([(math.cos(j / n * math.tau + twist) * r + wob, y, math.sin(j / n * math.tau + twist) * r) for j in range(n)])
    bm = loft("Funnel", rings, lambda i, j: 0, cap=False)
    ob = obj_from_bmesh("Funnel", bm, [m["tornado"]], recalc=False)
    smooth(ob, 80)
    return export_event("TornadoFunnel", [ob])


reset_scene()
m = v5_materials(chaos_materials())
results = []
for fn in (spring, hand, fan, magnet, anvil, bumper, gum, oil, duck, mallet, tornado):
    results.append(fn(m))
results += list(cannon(m))
results += list(ufo(m))

for ob in bpy.data.objects:
    ob.hide_render = ob.type == 'MESH' and ob.name != "RC_Ground"
cols = 5
for idx, (root, tris) in enumerate(results):
    x = (idx % cols) * 9.0 - 18
    z = (idx // cols) * 10.0
    root.location = C(x, 0, z)
    for c in all_children(root):
        c.hide_render = False
render_preview(os.path.join(PREVIEW, "chaos_v5.png"), (0, 4, 10), 55, 26, 200, lens=35)
for root, tris in results:
    print(f"{root.name}: {tris} tris")
