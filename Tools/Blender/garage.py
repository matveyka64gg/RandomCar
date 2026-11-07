# Студия гаража: пол, поворотный подиум с подсветкой, изогнутый задник (циклорама), световые панели, надпись.
# Запуск: blender_client.py rc_lib.py garage.py. Начало координат — центр подиума, машина смотрит по +z.

def build_garage():
    reset_scene()
    os.makedirs(PREVIEW, exist_ok=True)
    floor_m = mat("M_GarageFloor", "#1C2230", metallic=0.2, roughness=0.25)
    cyc_m = mat("M_Cyclorama", "#26304A", roughness=0.85)
    table_m = mat("M_Turntable", "#2E3548", metallic=0.6, roughness=0.3)
    orange = mat("M_LampOrange", "#FF6A2A", emission="#FF6A2A", strength=3.0)
    teal = mat("M_LampTeal", "#24E0C8", emission="#24E0C8", strength=2.5)
    soft = mat("M_Softbox", "#FFF6E8", emission="#FFF6E8", strength=4.0)
    steel = mat("M_Steel", "#B8C0CC", metallic=0.6, roughness=0.4)
    white = mat("M_Decal_White", "#F2F0E8", roughness=0.4)
    root = empty("GarageStudio")
    parts = []

    # Пол: большой диск с концентрическими полосами.
    parts.append(cylinder("Floor", (0, -0.05, 0), 16.0, 0.1, 'y', floor_m, segments=96))
    for r in (5.2, 8.5):
        parts.append(cylinder("FloorRing", (0, 0.002, 0), r, 0.004, 'y', mat("M_FloorLine", "#323C55", roughness=0.4), segments=96))
        parts.append(cylinder("FloorRingIn", (0, 0.003, 0), r - 0.05, 0.004, 'y', floor_m, segments=96))

    # Подиум: поворотный круг с кромкой-подсветкой.
    parts.append(cylinder("Turntable", (0, 0.06, 0), 3.4, 0.12, 'y', table_m, segments=72, bevel=0.03))
    parts.append(cylinder("TurntableGlow", (0, 0.035, 0), 3.46, 0.03, 'y', orange, segments=72))
    parts.append(cylinder("TurntableTop", (0, 0.122, 0), 3.2, 0.006, 'y', mat("M_TurntableTop", "#3A4258", metallic=0.4, roughness=0.35), segments=72))

    # Циклорама: стена по дуге позади подиума с плавным переходом в пол.
    rings = []
    arc0, arc1, steps = 0.0, 2 * math.pi, 72
    radius = 14.0
    profile = []
    for k in range(9):
        t = k / 8
        a = t * math.pi / 2
        profile.append((radius - 2.0 * (1 - math.sin(a)) - 0.0, 2.0 * (1 - math.cos(a))))
    profile.append((radius, 8.5))
    for i in range(steps + 1):
        ang = arc0 + (arc1 - arc0) * i / steps
        ring = []
        for (rr, y) in profile:
            ring.append((math.sin(ang) * rr, y, math.cos(ang) * rr))
        rings.append(ring)
    bm = bmesh.new()
    verts = [[bm.verts.new(C(*p)) for p in ring] for ring in rings]
    for i in range(len(rings) - 1):
        for j in range(len(profile) - 1):
            bm.faces.new((verts[i][j], verts[i][j + 1], verts[i + 1][j + 1], verts[i + 1][j]))
    # Нормали стены — внутрь студии: Unity рисует только лицевую сторону.
    bm.normal_update()
    for f in bm.faces:
        c = f.calc_center_median()
        inward = Vector((-c.x, -c.y, 0.0))
        if f.normal.dot(inward) < 0:
            f.normal_flip()
    cyc = obj_from_bmesh("Cyclorama", bm, [cyc_m], recalc=False)
    smooth(cyc, 60)

    # Световые полосы на циклораме: оранжевая и бирюзовая.
    for (y, m_) in ((3.2, orange), (3.5, teal)):
        for i in range(steps):
            a0 = arc0 + (arc1 - arc0) * i / steps
            a1 = arc0 + (arc1 - arc0) * (i + 1) / steps
            p0 = (math.sin(a0) * (radius - 0.05), y, math.cos(a0) * (radius - 0.05))
            p1 = (math.sin(a1) * (radius - 0.05), y, math.cos(a1) * (radius - 0.05))
            parts.append(beam("Strip", p0, p1, 0.06, 0.04, m_, bevel=0.0))

    # Софтбоксы на стойках по трём сторонам.
    for ang in (40, 140, 270):
        a = math.radians(ang)
        cx, cz = math.sin(a) * 10.5, math.cos(a) * 10.5
        parts.append(beam("Stand", (cx, 0, cz), (cx, 3.6, cz), 0.07, 0.07, steel))
        for leg in range(3):
            b = a + leg * 2 * math.pi / 3
            parts.append(beam("Leg", (cx, 0.9, cz), (cx + math.sin(b) * 0.7, 0.02, cz + math.cos(b) * 0.7), 0.04, 0.04, steel))
        box_c = (cx, 3.9, cz)
        frame = mat("M_SoftboxFrame", "#14171F", roughness=0.6)
        sb = join(box("Softbox", (0, 3.9, 0), (1.6, 1.0, 0.25), frame, bevel=0.03),
                  [box("SoftboxFace", (0, 3.9, 0.13), (1.45, 0.85, 0.02), soft)])
        origin_to(sb, (0, 3.9, 0))
        # Светящаяся сторона (+z машины) смотрит на подиум и чуть вниз.
        target = C(0, 1.0, 0) - C(*box_c)
        sb.rotation_mode = 'QUATERNION'
        sb.rotation_quaternion = target.to_track_quat('-Y', 'Z')
        sb.location = C(*box_c)
        parts.append(sb)

    # Потолочная световая рамка над подиумом.
    for (x0, z0, x1, z1) in ((-3.2, -3.2, 3.2, -3.2), (3.2, -3.2, 3.2, 3.2), (3.2, 3.2, -3.2, 3.2), (-3.2, 3.2, -3.2, -3.2)):
        parts.append(beam("TopFrame", (x0, 6.0, z0), (x1, 6.0, z1), 0.25, 0.12, mat("M_SoftboxFrame", "#14171F", roughness=0.6)))
        parts.append(beam("TopLight", (x0, 5.93, z0), (x1, 5.93, z1), 0.16, 0.02, soft))

    # Потолок: тёмный купол над студией, по краю — световое кольцо.
    parts.append(cylinder("Ceiling", (0, 9.0, 0), radius + 0.3, 0.2, 'y', mat("M_StudioCeiling", "#151A26", roughness=0.9), segments=72))
    parts.append(cylinder("CeilingGlow", (0, 8.85, 0), 6.0, 0.06, 'y', soft, segments=64))
    parts.append(cylinder("CeilingGlowCut", (0, 8.83, 0), 5.6, 0.06, 'y', mat("M_StudioCeiling", "#151A26", roughness=0.9), segments=64))

    # Надпись на заднике.
    title = text_mesh("GarageTitle", "RANDOMCAR · ГАРАЖ", 0.9, white, extrude=0.03)
    title.rotation_euler = (math.radians(90), 0, 0)   # в Unity читается правильно (в Blender зеркально)
    title.location = C(0, 5.0, -(radius - 0.35))
    parts.append(title)

    body = join(cyc, parts)
    body.name = "Studio"
    set_parent(body, root)
    turntable_anchor = empty("TurntableAnchor", (0, 0.125, 0))
    set_parent(turntable_anchor, root)
    export_fbx(root, os.path.join(ART, "Environment", "GarageStudio.fbx"))
    render_preview(os.path.join(PREVIEW, "garage.png"), (0, 1.5, 0), 11.0, 3.0, 20, lens=28)
    print("garage tris:", tri_count(body))


build_garage()
