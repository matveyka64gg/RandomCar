# Модели событий RandomCar. Запуск: blender_client.py rc_lib.py events.py
# Координаты машины: x — вправо, y — вверх, z — «вперёд» объекта. Начало координат у основания.

import random

EV_DIR = os.path.join(ART, "Events")


def ev_materials():
    return dict(
        worm=mat("M_Worm", "#FF8FA3", roughness=0.45),
        worm2=mat("M_Worm2", "#E2627F", roughness=0.45),
        dirt=mat("M_Dirt", "#8A5A3B", roughness=0.9),
        hole=mat("M_Hole", "#2A1A12", roughness=1.0),
        white=mat("M_Decal_White", "#F2F0E8", roughness=0.4),
        black=mat("M_Decal_Black", "#15161A", roughness=0.4),
        red=mat("M_KerbRed", "#E0352B", roughness=0.5),
        yellow=mat("M_SignYellow", "#FFD23F", roughness=0.5),
        trim=mat("M_Trim", "#26282E", roughness=0.6),
        steel=mat("M_Steel", "#B8C0CC", metallic=0.6, roughness=0.4),
        olive=mat("M_TankOlive", "#6E7D3C", roughness=0.6),
        sand=mat("M_TankSand", "#C9B27A", roughness=0.6),
        track=mat("M_TankTrack", "#33352F", roughness=0.8),
        wool=mat("M_Wool", "#F4F1EA", roughness=0.95),
        face=mat("M_SheepFace", "#2A2A2E", roughness=0.7),
        ballred=mat("M_BallRed", "#FF4B2B", roughness=0.3),
        ballyellow=mat("M_BallYellow", "#FFC21A", roughness=0.3),
        ballblue=mat("M_BallBlue", "#2F6BFF", roughness=0.3),
        ballgreen=mat("M_BallGreen", "#2DD4A0", roughness=0.3),
        banana=mat("M_Banana", "#FFD93B", roughness=0.4),
        bananadark=mat("M_BananaDark", "#7A5A1E", roughness=0.6),
        basalt=mat("M_Basalt", "#4A4E58", roughness=0.8),
        basalttop=mat("M_BasaltTop", "#7A808C", roughness=0.7),
        good=mat("M_GoodCyan", "#24E0C8", emission="#24E0C8", strength=1.5),
        goodlime=mat("M_GoodLime", "#A6F23A", emission="#A6F23A", strength=1.2),
        wood=mat("M_CrateWood", "#C98B4F", roughness=0.7),
        wooddark=mat("M_CrateWoodDark", "#8E5B2F", roughness=0.7),
        navy=mat("M_BrandDark", "#121826", roughness=0.4),
    )


def export_event(name, parts, children=None):
    """Корень-пустышка с мешем Body и именованными подвижными частями (children: имя -> (объект, точка поворота))."""
    root = empty(name)
    body = join(parts[0], parts[1:]) if len(parts) > 1 else parts[0]
    body.name = "Body"
    body.data.name = name + "_Body"
    set_parent(body, root)
    for child_name, (ob, pivot, parent_name) in (children or {}).items():
        if ob.modifiers:
            apply_modifiers(ob)
        ob.name = child_name
        ob.data.name = name + "_" + child_name
        origin_to(ob, pivot)
        set_parent(ob, bpy.data.objects[parent_name] if parent_name else root)
    export_fbx(root, os.path.join(EV_DIR, name + ".fbx"))
    tris = sum(tri_count(o) for o in all_children(root))
    # Освобождаем имена для следующих моделей.
    for ob in all_children(root):
        ob.name = ob.name + "_" + name
    return root, tris


def blob(name, center, radius, scale, material, subdiv=2, jitter=0.12, seed=0, smooth_it=True):
    rnd = random.Random(seed)
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=subdiv, radius=radius)
    for v in bm.verts:
        v.co *= 1.0 + rnd.uniform(-jitter, jitter)
    sx, sy, sz = scale
    bmesh.ops.scale(bm, vec=Vector((sx, sz, sy)), verts=bm.verts)
    bmesh.ops.translate(bm, vec=C(*center), verts=bm.verts)
    ob = obj_from_bmesh(name, bm, [material])
    if smooth_it:
        smooth(ob, 80)
    return ob


def worm(m):
    parts = []
    # Тело-«гармошка»: сегменты по S-образной кривой.
    for k in range(7):
        t = k / 6
        y = 0.15 + t * 1.65
        x = 0.18 * math.sin(t * 3.4)
        z = 0.12 * math.sin(t * 2.1 + 0.5)
        r = 0.36 - 0.1 * t
        parts.append(blob("Seg", (x, y, z), r, (1, 0.8, 1), m["worm"] if k % 2 == 0 else m["worm2"], subdiv=2, jitter=0.0, seed=k))
    head = (0.18 * math.sin(3.4), 1.85, 0.12 * math.sin(2.6))
    parts.append(blob("Head", head, 0.3, (1, 1, 1), m["worm"], jitter=0.0))
    for s in (-1, 1):
        parts.append(blob("Eye", (head[0] + s * 0.12, head[1] + 0.1, head[2] + 0.24), 0.1, (1, 1, 1), m["white"], jitter=0.0))
        parts.append(blob("Pupil", (head[0] + s * 0.12, head[1] + 0.1, head[2] + 0.33), 0.045, (1, 1, 1), m["black"], subdiv=1, jitter=0.0))
    parts.append(box("Mouth", (head[0], head[1] - 0.1, head[2] + 0.27), (0.14, 0.03, 0.03), m["black"], bevel=0.01))
    return export_event("Worm", parts)


def dirt_mound(m):
    parts = [cylinder("Hole", (0, 0.02, 0), 0.55, 0.04, 'y', m["hole"], segments=20)]
    rnd = random.Random(5)
    for k in range(9):
        a = 2 * math.pi * k / 9 + rnd.uniform(-0.2, 0.2)
        r = 0.75 + rnd.uniform(-0.1, 0.15)
        parts.append(blob("Clod", (math.cos(a) * r, 0.1, math.sin(a) * r), rnd.uniform(0.2, 0.32), (1, 0.6, 1), m["dirt"], subdiv=1, jitter=0.25, seed=k, smooth_it=False))
    return export_event("DirtMound", parts)


def rocket(m):
    # Нос смотрит вниз (-y), начало координат в кончике носа: ракета падает отвесно.
    L, R = 3.0, 0.32
    parts = [cylinder("Hull", (0, 0.6 + L / 2, 0), R, L, 'y', m["white"], segments=24, bevel=0.03)]
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=24, radius1=R, radius2=0.02, depth=0.9)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(math.pi, 3, 'X'))
    bmesh.ops.translate(bm, vec=C(0, 0.45, 0), verts=bm.verts)
    nose = obj_from_bmesh("Nose", bm, [m["red"]])
    smooth(nose, 50)
    parts.append(nose)
    parts.append(cylinder("Band", (0, 1.2, 0), R + 0.01, 0.22, 'y', m["black"], segments=24))
    parts.append(cylinder("Band2", (0, 1.45, 0), R + 0.01, 0.12, 'y', m["yellow"], segments=24))
    for k in range(4):
        a = math.pi / 2 * k + math.pi / 4
        dx, dz = math.cos(a), math.sin(a)
        parts.append(beam("Fin", (dx * R, 3.2, dz * R), (dx * (R + 0.45), 3.65, dz * (R + 0.45)), 0.06, 0.55, m["red"], bevel=0.01))
    parts.append(cylinder("Nozzle", (0, 3.75, 0), R * 0.7, 0.3, 'y', m["trim"], segments=20, bevel=0.02))
    return export_event("Rocket", parts)


def tank(m):
    parts = []
    parts.append(box("Hull", (0, 0.95, 0), (2.6, 0.7, 4.4), m["olive"], bevel=0.12))
    parts.append(box("HullTop", (0, 1.38, -0.2), (2.1, 0.2, 3.2), m["olive"], bevel=0.08))
    parts.append(box("Stripe", (0, 1.31, 0.9), (2.62, 0.08, 0.5), m["sand"], bevel=0.02))
    for s in (-1, 1):
        parts.append(box("Track", (s * 1.35, 0.5, 0), (0.6, 0.9, 4.6), m["track"], bevel=0.25))
        for k in range(5):
            parts.append(cylinder("Wheel", (s * 1.67, 0.45, -1.6 + k * 0.8), 0.32, 0.08, 'x', m["olive"], segments=16, bevel=0.02))
        parts.append(box("Skirt", (s * 1.36, 1.0, 0), (0.66, 0.12, 4.5), m["olive"], bevel=0.04))
    # Башня и ствол — отдельные объекты с точками поворота.
    turret_parts = [cylinder("TurretBase", (0, 1.75, -0.2), 1.0, 0.55, 'y', m["olive"], segments=24, bevel=0.1),
                    box("TurretTop", (0, 2.05, -0.3), (1.4, 0.3, 1.5), m["olive"], bevel=0.12),
                    cylinder("Hatch", (0.3, 2.25, -0.5), 0.3, 0.1, 'y', m["sand"], segments=16, bevel=0.02),
                    box("TurretStripe", (0, 1.75, 0.7), (1.2, 0.12, 0.2), m["sand"], bevel=0.02)]
    turret = join(turret_parts[0], turret_parts[1:])
    barrel_parts = [cylinder("Barrel", (0, 1.85, 1.9), 0.12, 2.4, 'z', m["olive"], segments=16),
                    cylinder("Muzzle", (0, 1.85, 3.15), 0.18, 0.35, 'z', m["track"], segments=16, bevel=0.03),
                    cylinder("Mantlet", (0, 1.85, 0.75), 0.25, 0.4, 'z', m["olive"], segments=16, bevel=0.04)]
    barrel = join(barrel_parts[0], barrel_parts[1:])
    return export_event("Tank", parts, {
        "Turret": (turret, (0, 1.75, -0.2), None),
        "Barrel": (barrel, (0, 1.85, 0.6), "Turret"),
    })


def sheep(m, seed):
    rnd = random.Random(seed)
    parts = []
    for k in range(7):
        parts.append(blob("Wool", (rnd.uniform(-0.25, 0.25), 0.85 + rnd.uniform(-0.1, 0.12), rnd.uniform(-0.45, 0.45)),
                          rnd.uniform(0.3, 0.42), (1, 0.9, 1), m["wool"], subdiv=1, jitter=0.15, seed=seed * 10 + k, smooth_it=False))
    parts.append(box("Head", (0, 1.05, 0.72), (0.34, 0.38, 0.42), m["face"], bevel=0.12))
    parts.append(blob("Tuft", (0, 1.27, 0.66), 0.16, (1.2, 0.8, 1), m["wool"], subdiv=1, seed=seed + 50, smooth_it=False))
    for s in (-1, 1):
        parts.append(box("Ear", (s * 0.24, 1.15, 0.65), (0.18, 0.07, 0.1), m["face"], bevel=0.02))
        parts.append(blob("Eye", (s * 0.11, 1.1, 0.93), 0.05, (1, 1, 1), m["white"], subdiv=1, jitter=0.0))
        for z in (-0.35, 0.35):
            parts.append(cylinder("Leg", (s * 0.18, 0.3, z), 0.06, 0.6, 'y', m["face"], segments=8))
    return export_event("Sheep_" + "AB"[seed % 2], parts)


def beach_ball(m):
    R = 1.5
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=36, v_segments=18, radius=R)
    colors = [m["ballred"], m["white"], m["ballyellow"], m["white"], m["ballblue"], m["white"]]
    for f in bm.faces:
        c = f.calc_center_median()
        if abs(c.z) > R * 0.93:
            f.material_index = 1
            continue
        a = (math.atan2(c.y, c.x) + math.pi) / (2 * math.pi)
        f.material_index = int(a * 6) % 6
    bmesh.ops.translate(bm, vec=C(0, R, 0), verts=bm.verts)
    ob = obj_from_bmesh("Ball", bm, colors)
    smooth(ob, 80)
    cap = cylinder("Cap", (0, 2 * R - 0.02, 0), 0.25, 0.06, 'y', m["ballgreen"], segments=20)
    return export_event("BeachBall", [ob, cap])


def banana(m):
    parts = []
    for k in range(4):
        a = math.pi / 2 * k + 0.3
        bm = bmesh.new()
        rows = []
        for i in range(9):
            t = i / 8
            r = 0.05 + t * 0.55
            x, z = math.cos(a) * r, math.sin(a) * r
            y = 0.06 + 0.12 * math.sin(t * math.pi) * (1 - t * 0.5)
            w = 0.09 * math.sin(math.pi * (0.15 + 0.85 * t)) + 0.02
            px, pz = -math.sin(a) * w, math.cos(a) * w
            rows.append([bm.verts.new(C(x + px, y, z + pz)), bm.verts.new(C(x - px, y, z - pz))])
        for i in range(8):
            f = bm.faces.new((rows[i][0], rows[i + 1][0], rows[i + 1][1], rows[i][1]))
            f.material_index = 1 if i == 7 else 0
        petal = obj_from_bmesh("Petal", bm, [m["banana"], m["bananadark"]], recalc=False)
        sol = petal.modifiers.new("Thick", 'SOLIDIFY')
        sol.thickness = 0.03
        apply_modifiers(petal)
        smooth(petal, 60)
        parts.append(petal)
    parts.append(cylinder("Stem", (0, 0.12, 0), 0.06, 0.2, 'y', m["bananadark"], segments=10, bevel=0.02))
    return export_event("BananaPeel", parts)


def basalt(m):
    parts = []
    rnd = random.Random(12)
    layout = [(-1.8, 0.0), (-0.9, 0.3), (0.0, -0.1), (0.9, 0.25), (1.8, -0.05), (-1.35, -0.7), (0.45, -0.75), (1.35, 0.8)]
    for k, (x, z) in enumerate(layout):
        h = rnd.uniform(2.4, 3.8)
        r = rnd.uniform(0.42, 0.58)
        col = cylinder("Column", (x, h / 2, z), r, h, 'y', m["basalt"], segments=6, bevel=0.03)
        col.rotation_euler = (0, 0, rnd.uniform(0, 1))
        parts.append(col)
        parts.append(cylinder("Top", (x, h + 0.02, z), r * 0.92, 0.05, 'y', m["basalttop"], segments=6))
    for p in parts:
        for poly in p.data.polygons:
            poly.use_smooth = False
    return export_event("BasaltColumns", parts)


def windsock(m):
    parts = [cylinder("Pole", (0, 2.2, 0), 0.06, 4.4, 'y', m["steel"], segments=10),
             cylinder("Ring", (0.05, 4.2, 0), 0.32, 0.05, 'x', m["steel"], segments=16)]
    for k in range(5):
        x0 = 0.1 + k * 0.42
        r0 = 0.32 - k * 0.045
        r1 = r0 - 0.045
        bm = bmesh.new()
        bmesh.ops.create_cone(bm, cap_ends=False, cap_tris=False, segments=16, radius1=r0, radius2=r1, depth=0.42)
        bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(math.radians(90), 3, 'Y'))
        bmesh.ops.translate(bm, vec=C(x0 + 0.21, 4.2 - k * 0.03, 0), verts=bm.verts)
        seg = obj_from_bmesh("Sock", bm, [m["red"] if k % 2 == 0 else m["white"]], recalc=False)
        sol = seg.modifiers.new("Thick", 'SOLIDIFY')
        sol.thickness = 0.02
        apply_modifiers(seg)
        smooth(seg, 60)
        parts.append(seg)
    return export_event("Windsock", parts)



def flat_chevron(name, center, width, depth, thickness, material, tilt=0.0):
    """Плоский шеврон «^», указывает на +z. tilt — наклон по продольной оси (для трамплина)."""
    cx, cy, cz = center
    w, d, t = width / 2, depth / 2, thickness
    outline = [(-w, -d), (0, d - t), (w, -d), (w, -d + t), (0, d), (-w, -d + t)]
    bm = bmesh.new()
    vs = [bm.verts.new(C(cx + x, cy + z * math.tan(tilt), cz + z)) for x, z in outline]
    bm.faces.new(vs)
    bmesh.ops.triangulate(bm, faces=bm.faces)
    ob = obj_from_bmesh(name, bm, [material], recalc=False)
    for poly in ob.data.polygons:
        if poly.normal.z < 0:
            poly.flip()
    sol = ob.modifiers.new("Thick", 'SOLIDIFY')
    sol.thickness = 0.025
    sol.offset = 1.0
    apply_modifiers(ob)
    return ob


def ramp(m):
    W, H, L = 4.2, 1.3, 6.0
    bm = bmesh.new()
    pts = [(-W / 2, 0, -L / 2), (W / 2, 0, -L / 2), (W / 2, 0, L / 2), (-W / 2, 0, L / 2),
           (-W / 2, H, L / 2), (W / 2, H, L / 2)]
    v = [bm.verts.new(C(*p)) for p in pts]
    for f in ((v[0], v[1], v[2], v[3]), (v[0], v[3], v[4]), (v[1], v[5], v[2]), (v[3], v[2], v[5], v[4]), (v[0], v[4], v[5], v[1])):
        bm.faces.new(f)
    body = obj_from_bmesh("Wedge", bm, [m["navy"]])
    add_bevel(body, 0.06, 2)
    apply_modifiers(body)
    parts = [body]
    slope = math.atan2(H, L)
    for k in range(4):
        z = -L / 2 + 1.0 + k * 1.35
        y = (z + L / 2) / L * H + 0.035
        parts.append(flat_chevron("Chevron", (0, y, z), 2.6, 0.9, 0.32, m["good"], tilt=slope))
    for s in (-1, 1):
        parts.append(box("Rail", (s * (W / 2 + 0.05), 0.0, 0), (0.12, 0.3, L), m["white"], bevel=0.03))
    return export_event("Ramp", parts)


def bonus_crate(m):
    S = 1.2
    parts = [box("Crate", (0, S / 2, 0), (S, S, S), m["wood"], bevel=0.05)]
    for s in (-1, 1):
        for axis in ('x', 'z'):
            if axis == 'x':
                parts.append(box("Plank", (s * (S / 2 + 0.01), S / 2, 0), (0.04, 0.16, S * 0.95), m["wooddark"]))
            else:
                parts.append(box("Plank", (0, S / 2, s * (S / 2 + 0.01)), (S * 0.95, 0.16, 0.04), m["wooddark"]))
    for x in (-1, 1):
        for y in (0, 1):
            for z in (-1, 1):
                parts.append(box("Corner", (x * S / 2, y * S, z * S / 2), (0.2, 0.2, 0.2), m["steel"], bevel=0.03))
    # Молния на каждой стороне
    bolt = [(-0.05, 0.30), (0.10, 0.30), (0.02, 0.05), (0.12, 0.05), (-0.06, -0.30), (0.0, -0.02), (-0.10, -0.02)]
    for face in range(4):
        rot = Matrix.Rotation(math.pi / 2 * face, 3, 'Z')
        bm = bmesh.new()
        vs = [bm.verts.new(rot @ C(x, S / 2 + y, S / 2 + 0.04)) for x, y in bolt]
        bm.faces.new(vs)
        bmesh.ops.triangulate(bm, faces=bm.faces, quad_method='BEAUTY', ngon_method='EAR_CLIP')
        ob = obj_from_bmesh("Bolt", bm, [m["goodlime"]], recalc=False)
        sol = ob.modifiers.new("Thick", 'SOLIDIFY')
        sol.thickness = 0.02
        apply_modifiers(ob)
        parts.append(ob)
    return export_event("BonusCrate", parts)


def boost_pad(m):
    parts = [box("Pad", (0, 0.03, 0), (3.6, 0.06, 6.0), m["navy"], bevel=0.03)]
    for k in range(3):
        z = -1.8 + k * 1.8
        parts.append(flat_chevron("Chevron", (0, 0.065, z), 2.6, 1.1, 0.38, m["good"]))
    for s in (-1, 1):
        parts.append(box("Edge", (s * 1.8, 0.05, 0), (0.12, 0.1, 6.0), m["good"], bevel=0.02))
    return export_event("BoostPad", parts)


reset_scene()
m = ev_materials()
results = []
for fn in (lambda: worm(m), lambda: dirt_mound(m), lambda: rocket(m), lambda: tank(m), lambda: sheep(m, 0),
           lambda: sheep(m, 1), lambda: beach_ball(m), lambda: banana(m), lambda: basalt(m), lambda: windsock(m),
           lambda: ramp(m), lambda: bonus_crate(m), lambda: boost_pad(m)):
    root, tris = fn()
    results.append((root, tris))

# Превью: всё в ряд
for ob in bpy.data.objects:
    ob.hide_render = ob.type == 'MESH' and ob.name != "RC_Ground"
layout = [(-13, 0), (-13, 2.5), (-9, -1), (-4, 1), (1, 3), (3, 3), (6, 1), (8.5, -2), (11, 4), (15, 1), (-8, 7), (-2, 8), (4, 8)]
for (root, tris), (x, z) in zip(results, layout):
    root.location = C(x, 0, z)
    for c in all_children(root):
        c.hide_render = False
bpy.data.objects["Rocket"].location = C(-9, 0.5, -1)
render_preview(os.path.join(PREVIEW, "events_lineup.png"), (C(0, 1, 3).x, C(0, 1, 3).y, 1.5), 30, 9, 18, lens=35)
for root, tris in results:
    print(f"{root.name}: {tris} tris")
