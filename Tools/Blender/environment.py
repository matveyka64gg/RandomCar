# Модели окружения трассы RandomCar. Запуск: blender_client.py rc_lib.py environment.py
# Координаты машины: x — вправо, y — вверх, z — «лицом» объекта (к трассе). Начало координат у основания.

import random

ENV_DIR = os.path.join(ART, "Environment")


def env_materials():
    return dict(
        wood=mat("M_Wood", "#7A5233", roughness=0.8),
        pine=mat("M_Pine", "#2F7D4A", roughness=0.8),
        pine2=mat("M_Pine2", "#3E9455", roughness=0.8),
        leaf=mat("M_Leaf", "#5DB04A", roughness=0.8),
        leaf2=mat("M_Leaf2", "#7BC24F", roughness=0.8),
        rock=mat("M_Rock", "#8C8478", roughness=0.9),
        rockdark=mat("M_RockDark", "#6E675E", roughness=0.9),
        concrete=mat("M_Concrete", "#D3CEC4", roughness=0.85),
        concretedark=mat("M_ConcreteDark", "#9A958C", roughness=0.85),
        steel=mat("M_Steel", "#B8C0CC", metallic=0.6, roughness=0.4),
        brand=mat("M_Brand", "#FF6A2A", roughness=0.5),
        branddark=mat("M_BrandDark", "#121826", roughness=0.5),
        white=mat("M_Decal_White", "#F2F0E8", roughness=0.4),
        black=mat("M_Decal_Black", "#15161A", roughness=0.4),
        glass=mat("M_Glass", "#1B2A3A", metallic=0.2, roughness=0.05),
        tire=mat("M_Tire", "#1C1C20", roughness=0.85),
        trim=mat("M_Trim", "#26282E", roughness=0.6),
        sign=mat("M_SignYellow", "#FFD23F", roughness=0.5),
        cone=mat("M_ConeOrange", "#FF6A1A", roughness=0.5),
        red=mat("M_KerbRed", "#E0352B", roughness=0.5),
        snow=mat("M_Snow", "#F4F8FB", roughness=0.6),
        mountain=mat("M_Mountain", "#7F8C99", roughness=0.95),
        hill=mat("M_Hill", "#5E9A4C", roughness=0.95),
        cloud=mat("M_Cloud", "#FFFFFF", roughness=1.0),
        lamp=mat("M_StartLamp", "#3A0A0A", emission="#FF2020", strength=0.0),
        crowd=[mat("M_Crowd1", "#FF4B2B"), mat("M_Crowd2", "#2F6BFF"), mat("M_Crowd3", "#FFC21A"),
               mat("M_Crowd4", "#2DD4A0"), mat("M_Crowd5", "#F2F0E8"), mat("M_Crowd6", "#8E5BFF")],
    )


def faceted(ob):
    for p in ob.data.polygons:
        p.use_smooth = False


def noisy_blob(name, center, radius, scale, material, subdiv=2, jitter=0.18, seed=0):
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    for v in bm.verts:
        v.co *= 1.0 + rnd.uniform(-jitter, jitter)
    sx, sy, sz = scale
    bmesh.ops.scale(bm, vec=Vector((sx, sz, sy)), verts=bm.verts)
    bmesh.ops.translate(bm, vec=C(*center), verts=bm.verts)
    ob = obj_from_bmesh(name, bm, [material])
    faceted(ob)
    return ob


def cone_mesh(name, center, r1, r2, depth, material, segments=9, rot=0.0):
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments,
                          radius1=r1, radius2=r2, depth=depth)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(rot, 3, 'Z'))
    bmesh.ops.translate(bm, vec=C(*center), verts=bm.verts)
    ob = obj_from_bmesh(name, bm, [material])
    faceted(ob)
    return ob


def finish(name, parts, path_name=None):
    ob = join(parts[0], parts[1:]) if len(parts) > 1 else parts[0]
    ob.name = name
    ob.data.name = name
    export_single(ob, os.path.join(ENV_DIR, (path_name or name) + ".fbx"))
    return ob


def export_single(ob, path):
    root = empty(ob.name + "_Root")
    set_parent(ob, root)
    export_fbx(root, path)


# ---------- растительность и камни ----------

def pine(name, height, seed, m):
    rnd = random.Random(seed)
    parts = [cylinder("Trunk", (0, height * 0.12, 0), height * 0.035, height * 0.25, 'y', m["wood"], segments=7)]
    tiers = 4
    for k in range(tiers):
        t = k / tiers
        base = height * (0.18 + 0.2 * k)
        r = height * (0.26 - 0.05 * k) * rnd.uniform(0.9, 1.1)
        depth = height * (0.36 - 0.03 * k)
        parts.append(cone_mesh("Tier", (0, base + depth / 2, 0), r, r * 0.08, depth,
                               m["pine"] if k % 2 == 0 else m["pine2"], segments=8, rot=rnd.uniform(0, 1)))
    return finish(name, parts)


def round_tree(name, height, seed, m):
    rnd = random.Random(seed)
    trunk_h = height * 0.45
    parts = [cone_mesh("Trunk", (0, trunk_h / 2, 0), height * 0.06, height * 0.035, trunk_h, m["wood"], segments=7)]
    for k in range(5):
        a = 2 * math.pi * k / 5 + rnd.uniform(-0.3, 0.3)
        d = height * 0.16 * (0 if k == 0 else 1)
        cx, cz = math.cos(a) * d, math.sin(a) * d
        cy = height * (0.68 if k else 0.78) + rnd.uniform(-0.05, 0.05) * height
        r = height * (0.27 if k == 0 else 0.2) * rnd.uniform(0.85, 1.1)
        parts.append(noisy_blob("Crown", (cx, cy, cz), r, (1, 0.85, 1), m["leaf"] if k % 2 else m["leaf2"], subdiv=2, seed=seed * 10 + k))
    return finish(name, parts)


def bush(name, size, seed, m):
    rnd = random.Random(seed)
    parts = []
    for k in range(4):
        a = 2 * math.pi * k / 4 + rnd.uniform(0, 1)
        d = size * 0.35 * (0 if k == 0 else 1)
        parts.append(noisy_blob("Bush", (math.cos(a) * d, size * 0.35, math.sin(a) * d), size * rnd.uniform(0.35, 0.5),
                                (1, 0.75, 1), m["leaf2"] if k % 2 else m["pine2"], subdiv=1, seed=seed * 7 + k))
    return finish(name, parts)


def rock(name, size, seed, m):
    parts = [noisy_blob("Rock", (0, size * 0.32, 0), size * 0.5, (1.0, 0.7, 0.85), m["rock"], subdiv=2, jitter=0.22, seed=seed)]
    rnd = random.Random(seed + 99)
    if size > 1.5:
        parts.append(noisy_blob("Rock2", (size * 0.45, size * 0.18, size * 0.15), size * 0.3, (1, 0.6, 1), m["rockdark"], subdiv=1, jitter=0.25, seed=seed + 1))
    return finish(name, parts)


# ---------- трасса: инвентарь ----------

def tire_stack(name, m, band):
    parts = []
    for k in range(3):
        y = 0.13 + k * 0.25
        parts.append(cylinder("Tire", (0, y, 0), 0.38, 0.24, 'y', m["tire"], segments=20, bevel=0.06, bevel_segments=2))
        if (k + band) % 2 == 0:
            parts.append(cylinder("Band", (0, y, 0), 0.39, 0.08, 'y', m["white"] if band else m["red"], segments=20))
        parts.append(cylinder("Hole", (0, y + 0.002, 0), 0.2, 0.245, 'y', m["black"], segments=16))
    return finish(name, parts)


def traffic_cone(name, m):
    parts = [box("Base", (0, 0.02, 0), (0.42, 0.04, 0.42), m["cone"], bevel=0.02),
             cone_mesh("Cone", (0, 0.36, 0), 0.17, 0.04, 0.64, m["cone"], segments=16),
             cone_mesh("Band", (0, 0.40, 0), 0.115, 0.095, 0.10, m["white"], segments=16)]
    for p in parts[1:]:
        smooth(p, 40)
    return finish(name, parts)


def chevron_sign(name, m):
    parts = [box("Post", (-0.6, 0.6, 0), (0.08, 1.2, 0.08), m["steel"], bevel=0.01),
             box("Post2", (0.6, 0.6, 0), (0.08, 1.2, 0.08), m["steel"], bevel=0.01),
             box("Board", (0, 1.35, 0.03), (1.9, 0.75, 0.06), m["sign"], bevel=0.02)]
    for k in range(3):
        x = -0.55 + k * 0.55
        for s in (1, -1):
            parts.append(beam("Chevron", (x - 0.12, 1.35 + s * 0.22, 0.07), (x + 0.12, 1.35, 0.07), 0.1, 0.02, m["black"], bevel=0.0))
    return finish(name, parts)


def guardrail_post(name, m):
    return finish(name, [box("Post", (0, 0.45, 0), (0.1, 0.9, 0.12), m["steel"], bevel=0.01)])


def flag_pole(name, m, color):
    parts = [cylinder("Pole", (0, 3.5, 0), 0.05, 7.0, 'y', m["steel"], segments=10),
             cylinder("Top", (0, 7.05, 0), 0.08, 0.1, 'y', m["steel"], segments=10)]
    bm = bmesh.new()
    rows = []
    for i in range(9):
        t = i / 8
        x = 0.05 + t * 1.8
        z = 0.12 * math.sin(t * math.pi * 1.6)
        rows.append((bm.verts.new(C(x, 6.9, z)), bm.verts.new(C(x, 5.8, z))))
    for i in range(8):
        bm.faces.new((rows[i][0], rows[i + 1][0], rows[i + 1][1], rows[i][1]))
    flag = obj_from_bmesh("Flag", bm, [color], recalc=False)
    sol = flag.modifiers.new("Thick", 'SOLIDIFY')
    sol.thickness = 0.02
    apply_modifiers(flag)
    parts.append(flag)
    return finish(name, parts)


def ad_board(name, m, text):
    parts = [box("Panel", (0, 0.75, 0), (5.0, 1.1, 0.12), m["branddark"], bevel=0.03),
             box("Stripe", (0, 0.32, 0.065), (5.0, 0.16, 0.02), m["brand"]),
             box("LegL", (-2.2, 0.2, -0.1), (0.1, 0.4, 0.3), m["steel"]),
             box("LegR", (2.2, 0.2, -0.1), (0.1, 0.4, 0.3), m["steel"])]
    t = text_mesh("Text", text, 0.62, m["white"], extrude=0.01)
    t.rotation_euler = (math.radians(90), 0, 0)
    t.location = C(0, 0.8, 0.07)
    parts.append(t)
    return finish(name, parts)


# ---------- сооружения ----------

def grandstand(name, m, seed):
    rnd = random.Random(seed)
    W, tiers, step_h, step_d = 16.0, 6, 0.55, 1.15
    parts = []
    depth_total = tiers * step_d
    parts.append(box("Base", (0, 0.4, -depth_total / 2), (W, 0.8, depth_total + 0.6), m["concretedark"], bevel=0.05))
    for k in range(tiers):
        y = 0.8 + (k + 1) * step_h
        z = -k * step_d - step_d / 2
        parts.append(box("Step", (0, y / 2, z), (W, y, step_d), m["concrete"], bevel=0.03))
        seat_mat = m["brand"] if k % 2 == 0 else m["steel"]
        parts.append(box("Seat", (0, y + 0.18, z - 0.15), (W - 0.4, 0.12, 0.45), seat_mat, bevel=0.02))
        x = -W / 2 + 0.6
        while x < W / 2 - 0.5:
            if rnd.random() < 0.82:
                col = rnd.choice(m["crowd"])
                h = rnd.uniform(0.5, 0.62)
                parts.append(box("Body", (x, y + 0.25 + h / 2, z - 0.2), (0.42, h, 0.3), col, bevel=0.06, segments=1))
                parts.append(box("Head", (x, y + 0.42 + h, z - 0.2), (0.24, 0.26, 0.24), m["concretedark"] if rnd.random() < 0.3 else mat("M_Skin", "#E8B48A"), bevel=0.06, segments=1))
            x += rnd.uniform(0.55, 0.75)
    top = 0.8 + tiers * step_h
    back_z = -depth_total - 0.2
    parts.append(box("BackWall", (0, (top + 2.6) / 2, back_z), (W, top + 2.6, 0.4), m["concretedark"], bevel=0.05))
    roof_y = top + 3.4
    parts.append(box("Roof", (0, roof_y, -depth_total / 2 + 0.6), (W + 0.8, 0.25, depth_total + 2.2), m["white"], bevel=0.06))
    parts.append(box("RoofEdge", (0, roof_y - 0.1, 1.7), (W + 0.8, 0.55, 0.15), m["brand"], bevel=0.03))
    for x in (-W / 2 + 0.3, 0, W / 2 - 0.3):
        parts.append(beam("Column", (x, top, back_z + 0.3), (x, roof_y, back_z + 0.3), 0.3, 0.3, m["steel"]))
        parts.append(beam("Strut", (x, top + 1.0, back_z + 0.3), (x, roof_y - 0.15, 1.2), 0.18, 0.18, m["steel"]))
    parts.append(box("Rail", (0, 1.9, 0.45), (W, 0.06, 0.06), m["steel"]))
    for k in range(9):
        parts.append(box("RailPost", (-W / 2 + 0.2 + k * (W - 0.4) / 8, 1.35, 0.45), (0.06, 1.1, 0.06), m["steel"]))
    return finish(name, parts)


def start_gantry(m):
    root = empty("StartGantry")
    span, h = 19.6, 7.2
    parts = []
    for s in (-1, 1):
        parts.append(box("Pillar", (s * span / 2, h / 2, 0), (0.7, h, 0.7), m["branddark"], bevel=0.05))
        parts.append(box("PillarBand", (s * span / 2, 1.2, 0), (0.74, 0.3, 0.74), m["brand"], bevel=0.02))
        parts.append(box("Foot", (s * span / 2, 0.15, 0), (1.2, 0.3, 1.2), m["concretedark"], bevel=0.04))
    parts.append(box("Beam", (0, h - 0.6, 0), (span + 0.7, 1.2, 1.0), m["branddark"], bevel=0.05))
    for k in range(10):
        x = -span / 2 + 0.6 + k * (span - 1.2) / 9
        parts.append(box("Truss", (x, h - 0.6, 0.52), (0.08, 1.1, 0.04), m["steel"]))
    for s in (1, -1):
        parts.append(box("Banner", (0, h - 0.6, s * 0.53), (12.0, 0.9, 0.04), m["brand"], bevel=0.01))
        t = text_mesh("Logo", "RANDOMCAR", 0.78, m["white"], extrude=0.01)
        t.rotation_euler = (math.radians(90), 0, math.radians(0 if s > 0 else 180))
        t.location = C(0, h - 0.62, s * 0.57)
        parts.append(t)
    # Шахматная полоса под балкой
    for k in range(24):
        for row in range(2):
            col = m["white"] if (k + row) % 2 == 0 else m["black"]
            parts.append(box("Checker", (-span / 2 + 0.4 + k * (span - 0.8) / 23, h - 1.33 - row * 0.14, 0), ((span - 0.8) / 23, 0.14, 1.02), col))
    # Панель стартовых огней: смотрит на -z (на машины перед линией)
    parts.append(box("LightPanel", (0, h - 2.05, -0.1), (4.2, 0.9, 0.35), m["black"], bevel=0.05))
    gantry = join(parts[0], parts[1:])
    gantry.name = "Structure"
    gantry.data.name = "StartGantry_Structure"
    set_parent(gantry, root)
    for k in range(5):
        x = -1.6 + k * 0.8
        housing = cylinder("Housing", (x, h - 2.05, -0.3), 0.3, 0.12, 'z', m["trim"], segments=24, bevel=0.02)
        lamp = cylinder("StartLamp_%d" % (k + 1), (x, h - 2.05, -0.37), 0.24, 0.04, 'z', m["lamp"], segments=24)
        lamp.name = "StartLamp_%d" % (k + 1)
        lamp.data.name = lamp.name
        housing = join(housing, [])
        housing.name = "LampHousing_%d" % (k + 1)
        set_parent(housing, root)
        set_parent(lamp, root)
    export_fbx(root, os.path.join(ENV_DIR, "StartGantry.fbx"))
    return root


def pit_building(m):
    L, D, H = 42.0, 9.0, 7.0
    parts = [box("Main", (0, H / 2, 0), (L, H, D), m["concrete"], bevel=0.08),
             box("Roof", (0, H + 0.2, 0.6), (L + 1.0, 0.4, D + 1.6), m["white"], bevel=0.05),
             box("Fascia", (0, H - 0.1, D / 2 + 1.3), (L + 1.0, 0.7, 0.2), m["brand"], bevel=0.03)]
    doors = 8
    for k in range(doors):
        x = -L / 2 + 2.6 + k * (L - 5.2) / (doors - 1)
        parts.append(box("Door", (x, 1.75, D / 2 + 0.02), (3.8, 3.5, 0.1), m["steel"], bevel=0.02))
        for j in range(6):
            parts.append(box("DoorRib", (x, 0.35 + j * 0.55, D / 2 + 0.08), (3.7, 0.04, 0.04), m["concretedark"]))
        parts.append(box("Number", (x, 3.85, D / 2 + 0.05), (1.4, 0.4, 0.06), m["branddark"], bevel=0.01))
    parts.append(box("Windows", (0, 5.3, D / 2 + 0.03), (L - 2.0, 1.3, 0.08), m["glass"]))
    for k in range(14):
        parts.append(box("Mullion", (-L / 2 + 1.0 + k * (L - 2.0) / 13, 5.3, D / 2 + 0.09), (0.12, 1.3, 0.06), m["concretedark"]))
    t = text_mesh("Sign", "RANDOMCAR  PIT", 0.9, m["white"], extrude=0.02)
    t.rotation_euler = (math.radians(90), 0, 0)
    t.location = C(0, H + 1.05, D / 2 + 0.3)
    parts.append(box("SignBoard", (0, H + 1.05, D / 2 + 0.2), (10.0, 1.2, 0.15), m["branddark"], bevel=0.03))
    parts.append(t)
    return finish("PitBuilding", parts)


def control_tower(m):
    parts = [box("Shaft", (0, 6.0, 0), (3.2, 12.0, 3.2), m["concrete"], bevel=0.06),
             box("Band", (0, 11.6, 0), (3.4, 0.4, 3.4), m["brand"], bevel=0.03),
             box("Cab", (0, 13.4, 0), (5.6, 3.0, 5.6), m["glass"], bevel=0.12),
             box("CabFloor", (0, 11.95, 0), (6.0, 0.3, 6.0), m["concretedark"], bevel=0.05),
             box("CabRoof", (0, 15.05, 0), (6.4, 0.35, 6.4), m["white"], bevel=0.08),
             cylinder("Antenna", (1.8, 16.6, 1.8), 0.05, 3.0, 'y', m["steel"], segments=8),
             cylinder("Dish", (-1.6, 15.5, -1.6), 0.5, 0.15, 'y', m["steel"], segments=16)]
    for x in (-2.75, 2.75):
        for z in (-2.75, 2.75):
            parts.append(box("Frame", (x, 13.4, z), (0.15, 3.0, 0.15), m["branddark"]))
    return finish("ControlTower", parts)


def light_pole(m):
    parts = [cylinder("Pole", (0, 5.0, 0), 0.12, 10.0, 'y', m["steel"], segments=10),
             box("Arm", (0, 9.8, 0.9), (0.15, 0.15, 1.9), m["steel"]),
             box("Lamp", (0, 9.65, 1.7), (0.7, 0.2, 0.45), m["branddark"], bevel=0.04),
             box("LampGlass", (0, 9.53, 1.7), (0.6, 0.04, 0.38), mat("M_LampGlow", "#FFF3D6", emission="#FFF3D6", strength=1.0))]
    return finish("LightPole", parts)


# ---------- дальний план ----------

def ring_landscape(name, r_in, r_mid, r_out, h_min, h_max, seed, material, snow=None, snow_h=0.0, steps=220, sharp=1.6):
    """Кольцо гор или холмов вокруг трассы: острые гребни, неровная линия вершин, снег на верхних гранях."""
    rnd = random.Random(seed)
    phase = [rnd.uniform(0, 6.28) for _ in range(5)]
    peaks, ridge = [], []
    for i in range(steps):
        a = 2 * math.pi * i / steps
        n = (math.sin(a * 3 + phase[0]) * 0.30 + math.sin(a * 7 + phase[1]) * 0.22 +
             math.sin(a * 17 + phase[2]) * 0.18 + rnd.uniform(-0.25, 0.25))
        n = max(0.0, min(1.0, 0.5 + n * 0.7))
        # Чередование пиков и седловин даёт треугольный силуэт вместо плато.
        n *= 1.0 if i % 2 == 0 else rnd.uniform(0.55, 0.8)
        peaks.append(h_min + (h_max - h_min) * n ** sharp)
        ridge.append(r_mid + rnd.uniform(-0.12, 0.12) * (r_out - r_in))
    K = 9
    bm = bmesh.new()
    grid = []
    for i in range(steps):
        a = 2 * math.pi * i / steps
        row = []
        for k in range(K):
            t = k / (K - 1)
            r = r_in + (r_out - r_in) * t
            rp = ridge[i]
            if r <= rp:
                prof = ((r - r_in) / (rp - r_in)) ** 1.25
            else:
                prof = 1.0 - 0.55 * ((r - rp) / (r_out - rp))
            jitter = rnd.uniform(-0.08, 0.08) if 0 < k < K - 1 else 0.0
            y = peaks[i] * max(0.0, prof) * (1 + jitter)
            rr = r * (1 + (rnd.uniform(-0.015, 0.015) if 0 < k < K - 1 else 0))
            row.append(bm.verts.new(C(math.cos(a) * rr, y, math.sin(a) * rr)))
        grid.append(row)
    mats = [material] + ([snow] if snow else [])
    for i in range(steps):
        j = (i + 1) % steps
        for k in range(K - 1):
            for tri in ((grid[i][k], grid[j][k], grid[j][k + 1]), (grid[i][k], grid[j][k + 1], grid[i][k + 1])):
                f = bm.faces.new(tri)
                if snow:
                    low = min(v.co.z for v in f.verts)
                    f.material_index = 1 if low > snow_h else 0
    ob = obj_from_bmesh(name, bm, mats)
    faceted(ob)
    return finish(name, [ob])


def cloud(name, seed, m):
    rnd = random.Random(seed)
    parts = []
    for k in range(7):
        parts.append(noisy_blob("Puff", (rnd.uniform(-9, 9), rnd.uniform(0, 3), rnd.uniform(-4, 4)), rnd.uniform(3.5, 6),
                                (1.3, 0.65, 1.0), m["cloud"], subdiv=2, jitter=0.1, seed=seed * 13 + k))
    for p in parts:
        smooth(p, 60)
    return finish(name, parts)


reset_scene()
m = env_materials()
made = []
made += [pine("Pine_A", 8.0, 1, m), pine("Pine_B", 11.0, 2, m), pine("Pine_C", 14.0, 3, m)]
made += [round_tree("Tree_A", 7.0, 4, m), round_tree("Tree_B", 9.5, 5, m)]
made += [bush("Bush_A", 1.6, 6, m), bush("Bush_B", 2.2, 7, m)]
made += [rock("Rock_A", 1.2, 8, m), rock("Rock_B", 2.4, 9, m), rock("Rock_C", 4.5, 10, m)]
made += [tire_stack("TireStack_Red", m, 0), tire_stack("TireStack_White", m, 1), traffic_cone("Cone", m),
         chevron_sign("ChevronSign", m), guardrail_post("GuardrailPost", m),
         flag_pole("FlagPole_Orange", m, m["brand"]), flag_pole("FlagPole_Blue", m, m["crowd"][1]),
         ad_board("AdBoard_A", m, "RANDOMCAR"), ad_board("AdBoard_B", m, "ISKRA MOTORS")]
made += [grandstand("Grandstand", m, 11), pit_building(m), control_tower(m), light_pole(m)]
start_gantry(m)
made += [ring_landscape("Hills", 335, 450, 600, 10, 48, 21, m["hill"], steps=160, sharp=1.2),
         ring_landscape("Mountains", 680, 940, 1300, 90, 320, 22, m["mountain"], snow=m["snow"], snow_h=190)]
made += [cloud("Cloud_A", 31, m), cloud("Cloud_B", 32, m), cloud("Cloud_C", 33, m)]

# Превью: окружение в сборе
for ob in list(bpy.data.objects):
    if ob.type == 'MESH' and ob.name not in ("RC_Ground",):
        ob.hide_render = True
layout = {"Pine_A": (-14, 0, 6), "Pine_B": (-11, 0, 9), "Pine_C": (-16, 0, 11), "Tree_A": (-7, 0, 9), "Tree_B": (-4, 0, 12),
          "Bush_A": (-9, 0, 4), "Bush_B": (-5, 0, 5), "Rock_A": (-2, 0, 3), "Rock_B": (1, 0, 5), "Rock_C": (5, 0, 9),
          "TireStack_Red": (-1, 0, 0), "TireStack_White": (0, 0, 0), "Cone": (1.5, 0, -0.5), "ChevronSign": (3.5, 0, 0),
          "FlagPole_Orange": (6, 0, 1), "AdBoard_A": (9, 0, 1), "Grandstand": (14, 0, 6)}
for ob in bpy.data.objects:
    root_name = ob.name.replace("_Root", "")
    if ob.name.endswith("_Root") and root_name in layout:
        ob.location = C(*layout[root_name])
        for c in all_children(ob):
            c.hide_render = False
render_preview(os.path.join(PREVIEW, "env_props.png"), (C(0, 2, 6).x, C(0, 2, 6).y, 2.5), 32, 9, 20, lens=35)
print("exported:", len(made) + 1, "models")
for ob in made:
    print(f"{ob.name}: {tri_count(ob)} tris")
