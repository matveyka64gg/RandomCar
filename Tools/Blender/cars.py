# Генератор четырёх машин RandomCar. Запуск: blender_client.py rc_lib.py cars.py
# Все машины одного класса: колея, база и колёса в пределах ±5%, физика в Unity общая.
# Станции кузова: (z, полуширина, низ, верх) в метрах, z — вперёд.

PAINTS = {
    "Iskra": ("M_Paint_Red", "#FF4B2B"),
    "Kometa": ("M_Paint_Blue", "#2F6BFF"),
    "Pchelka": ("M_Paint_Yellow", "#FFC21A"),
    "Myata": ("M_Paint_Mint", "#2DD4A0"),
}

CARS = {
    "Iskra": dict(
        number="7", rim="M_Rim", rim_style="five", top_n=3.2,
        stations=[(-2.05, 0.78, 0.38, 0.80), (-1.95, 0.88, 0.30, 0.90), (-1.60, 0.93, 0.25, 0.94),
                  (-0.80, 0.93, 0.23, 0.93), (0.00, 0.93, 0.23, 0.91), (0.80, 0.93, 0.23, 0.88),
                  (1.50, 0.91, 0.25, 0.84), (1.90, 0.85, 0.29, 0.76), (2.05, 0.72, 0.35, 0.66)],
        cabin=dict(zr0=-1.50, zr1=-0.80, zf1=0.15, zf0=0.95, belt=0.84, roof=1.30, wb=0.84, wr=0.66),
        wheel=dict(r=0.36, w=0.27, zf=1.28, zr=-1.25, x=0.80),
        lights="wide", spoiler=True, splitter=True),
    "Kometa": dict(
        number="24", rim="M_RimDark", rim_style="ten", top_n=3.6,
        stations=[(-2.25, 0.80, 0.38, 0.82), (-2.10, 0.92, 0.29, 0.88), (-1.60, 0.97, 0.24, 0.92),
                  (-0.60, 0.96, 0.23, 0.90), (0.40, 0.95, 0.23, 0.88), (1.30, 0.95, 0.23, 0.85),
                  (1.90, 0.91, 0.26, 0.80), (2.15, 0.85, 0.30, 0.72), (2.25, 0.74, 0.35, 0.64)],
        cabin=dict(zr0=-1.85, zr1=-1.05, zf1=-0.15, zf0=0.60, belt=0.82, roof=1.24, wb=0.86, wr=0.66),
        wheel=dict(r=0.37, w=0.29, zf=1.45, zr=-1.40, x=0.83),
        lights="slim", scoop=True, stripes=True, quarter_glass=False),
    "Pchelka": dict(
        number="11", rim="M_RimWhite", rim_style="six", top_n=4.2,
        stations=[(-1.95, 0.84, 0.36, 0.92), (-1.85, 0.90, 0.30, 0.94), (-1.30, 0.92, 0.26, 0.95),
                  (0.00, 0.92, 0.26, 0.94), (1.00, 0.92, 0.26, 0.91), (1.60, 0.90, 0.28, 0.86),
                  (1.85, 0.84, 0.31, 0.78), (1.95, 0.74, 0.37, 0.70)],
        cabin=dict(zr0=-1.82, zr1=-1.66, zf1=0.25, zf0=0.98, belt=0.88, roof=1.44, wb=0.85, wr=0.73),
        wheel=dict(r=0.37, w=0.28, zf=1.25, zr=-1.25, x=0.80),
        lights="wide", lightbar=True, mudflaps=True),
    "Myata": dict(
        number="58", rim="M_Chrome", rim_style="dish", top_n=2.5,
        stations=[(-2.10, 0.70, 0.40, 0.76), (-1.95, 0.84, 0.31, 0.84), (-1.40, 0.90, 0.27, 0.88),
                  (-0.40, 0.89, 0.25, 0.86), (0.60, 0.88, 0.25, 0.85), (1.40, 0.87, 0.27, 0.82),
                  (1.90, 0.79, 0.31, 0.74), (2.10, 0.63, 0.37, 0.64)],
        cabin=dict(zr0=-1.45, zr1=-0.95, zf1=-0.20, zf0=0.45, belt=0.80, roof=1.22, wb=0.80, wr=0.64),
        wheel=dict(r=0.35, w=0.25, zf=1.30, zr=-1.30, x=0.76),
        lights="round", chrome_bumpers=True, two_tone=True),
}

TUMBLEHOME = 0.07


def materials(paint_name, paint_hex):
    return dict(
        paint=mat(paint_name, paint_hex, metallic=0.1, roughness=0.22),
        glass=mat("M_Glass", "#1B2A3A", metallic=0.2, roughness=0.05),
        tire=mat("M_Tire", "#1C1C20", roughness=0.85),
        rim=mat("M_Rim", "#C9CED6", metallic=0.9, roughness=0.3),
        rimdark=mat("M_RimDark", "#2E3238", metallic=0.8, roughness=0.35),
        rimwhite=mat("M_RimWhite", "#F2F0E8", roughness=0.4),
        chrome=mat("M_Chrome", "#E6EAF0", metallic=1.0, roughness=0.12),
        trim=mat("M_Trim", "#26282E", roughness=0.6),
        head=mat("M_LightHead", "#FFF6DC", emission="#FFF6DC", strength=2.0),
        tail=mat("M_LightTail", "#FF2A2A", emission="#FF1A1A", strength=2.0),
        white=mat("M_Decal_White", "#F2F0E8", roughness=0.4),
        black=mat("M_Decal_Black", "#15161A", roughness=0.4),
    )


def station_at(stations, z):
    if z <= stations[0][0]:
        return stations[0][1:]
    for a, b in zip(stations, stations[1:]):
        if a[0] <= z <= b[0]:
            t = (z - a[0]) / (b[0] - a[0])
            t = t * t * (3 - 2 * t)
            return tuple(a[k] + (b[k] - a[k]) * t for k in (1, 2, 3))
    return stations[-1][1:]


def section_point(hw, yb, yt, t, top_n):
    yc, hh = (yb + yt) / 2, (yt - yb) / 2
    c, s = math.cos(t), math.sin(t)
    n = top_n if s > 0 else 6.0
    x = hw * math.copysign(abs(c) ** (2 / n), c)
    y = yc + hh * math.copysign(abs(s) ** (2 / n), s)
    x *= 1.0 - TUMBLEHOME * max(0.0, s)
    return x, y


def side_x(spec, z, y):
    """Полуширина кузова на высоте y в сечении z (точка на боковине)."""
    hw, yb, yt = station_at(spec["stations"], z)
    yc, hh = (yb + yt) / 2, (yt - yb) / 2
    v = max(-0.999, min(0.999, (y - yc) / hh))
    n = spec["top_n"] if v > 0 else 6.0
    s = math.copysign(abs(v) ** (n / 2), v)
    c = math.sqrt(max(0.0, 1 - s * s))
    return hw * c ** (2 / n) * (1.0 - TUMBLEHOME * max(0.0, s))


def body_shell(spec, m):
    st = spec["stations"]
    z0, z1 = st[0][0], st[-1][0]
    count, ring_n = 48, 32
    rings = []
    for i in range(count):
        z = z0 + (z1 - z0) * i / (count - 1)
        hw, yb, yt = station_at(st, z)
        ring = []
        for j in range(ring_n):
            x, y = section_point(hw, yb, yt, 2 * math.pi * j / ring_n, spec["top_n"])
            ring.append((x, y, z))
        rings.append(ring)
    bm = loft("Body", rings, lambda i, j: 0)
    ob = obj_from_bmesh("Body", bm, [m["paint"]])
    sub = ob.modifiers.new("Sub", 'SUBSURF')
    sub.levels = 1
    sub.render_levels = 1
    apply_modifiers(ob)

    # Колёсные арки режем только с боков, чтобы не прорезать капот.
    wh = spec["wheel"]
    cutters = []
    for z in (wh["zf"], wh["zr"]):
        for s in (-1, 1):
            cutters.append(cylinder("ArchCut", (s * 1.0, wh["r"], z), wh["r"] + 0.075, 0.76, 'x', m["trim"], segments=48))
    boolean_difference(ob, cutters)
    smooth(ob, 40)
    return ob


def arch_lips(spec, m):
    """Тёмные накладки по кромке арок."""
    wh = spec["wheel"]
    R = wh["r"] + 0.075
    parts = []
    for zc in (wh["zf"], wh["zr"]):
        for s in (-1, 1):
            bm = bmesh.new()
            rows = []
            steps = 24
            for k in range(steps + 1):
                a = math.radians(6 + 168 * k / steps)
                taper = 0.35 + 0.65 * math.sin(math.pi * k / steps)
                ring = []
                for dr, dx in ((0.0, 0.012), (0.055 * taper, 0.012), (0.055 * taper, -0.04), (0.0, -0.04)):
                    rr = R + dr
                    zz = zc + rr * math.cos(a)
                    yy = wh["r"] + rr * math.sin(a)
                    _, _, top = station_at(spec["stations"], zz)
                    yy = min(yy, top - 0.03)
                    xs = side_x(spec, zz, max(yy, 0.3))   # накладка повторяет поверхность в каждой вершине
                    ring.append(bm.verts.new(C(s * (xs + dx), yy, zz)))
                rows.append(ring)
            for k in range(steps):
                for q in range(4):
                    bm.faces.new((rows[k][q], rows[k][(q + 1) % 4], rows[k + 1][(q + 1) % 4], rows[k + 1][q]))
            bm.faces.new(rows[0])
            bm.faces.new(rows[-1])
            ob = obj_from_bmesh("ArchLip", bm, [m["trim"]])
            smooth(ob, 50)
            parts.append(ob)
    return parts


def glass_panel(name, corners, inset, lift, material, bottom_extra=0.0):
    """Стеклянная вставка: многоугольник на грани кабины с отступом от краёв."""
    pts = [Vector(p) for p in corners]
    centroid = sum(pts, Vector()) / len(pts)
    shrunk = []
    for p in pts:
        d = centroid - p
        q = p + d.normalized() * inset * 1.35
        shrunk.append(q)
    if bottom_extra:
        low = min(p.y for p in shrunk)
        shrunk = [Vector((p.x, p.y + bottom_extra, p.z)) if abs(p.y - low) < 0.02 else p for p in shrunk]
    a, b, c = shrunk[0], shrunk[1], shrunk[2]
    normal = (b - a).cross(c - a).normalized()
    if normal.dot(centroid - Vector((0, 1.0, 0))) < 0:
        normal = -normal
    bm = bmesh.new()
    front = [bm.verts.new(C(*(p + normal * lift))) for p in shrunk]
    back = [bm.verts.new(C(*(p - normal * 0.01))) for p in shrunk]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    n = len(front)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((front[i], front[j], back[j], back[i]))
    return obj_from_bmesh(name, bm, [material])


def cabin(spec, m):
    c = spec["cabin"]
    prof = [(c["zr0"], c["belt"], c["wb"]), (c["zr1"], c["roof"], c["wr"]),
            (c["zf1"], c["roof"], c["wr"]), (c["zf0"], c["belt"], c["wb"])]
    bm = bmesh.new()
    L = [bm.verts.new(C(-w, y, z)) for z, y, w in prof]
    R = [bm.verts.new(C(w, y, z)) for z, y, w in prof]
    roof_index = 1 if spec.get("two_tone") else 0
    faces = [
        ((L[0], L[1], L[2], L[3]), 0), ((R[3], R[2], R[1], R[0]), 0),
        ((L[1], R[1], R[2], L[2]), roof_index), ((L[2], R[2], R[3], L[3]), 0),
        ((L[0], R[0], R[1], L[1]), 0), ((L[3], R[3], R[0], L[0]), 0),
    ]
    for verts, idx in faces:
        bm.faces.new(verts).material_index = idx
    ob = obj_from_bmesh("Cabin", bm, [m["paint"], m["white"]])
    add_bevel(ob, 0.07, 3, angle=20)
    apply_modifiers(ob)
    smooth(ob, 30)
    parts = [ob]

    def P(side, z, y, w):
        return (side * w, y, z)

    zb = (c["zr1"] + c["zf1"]) / 2
    # Ширина боковины кабины на высоте y меняется линейно от пояса к крыше.
    def w_at(y):
        t = (y - c["belt"]) / (c["roof"] - c["belt"])
        return c["wb"] + (c["wr"] - c["wb"]) * t

    def side_corners(side, za_bottom, za_top, zb_top, zb_bottom):
        return [P(side, za_bottom, c["belt"], c["wb"]), P(side, za_top, c["roof"], c["wr"]),
                P(side, zb_top, c["roof"], c["wr"]), P(side, zb_bottom, c["belt"], c["wb"])]

    # Где пояс на линии zb: точка на нижней кромке боковины.
    t_bottom = (zb - c["zr0"]) / (c["zf0"] - c["zr0"])
    for side in (-1, 1):
        rear = side_corners(side, c["zr0"], c["zr1"], zb, zb)
        front = side_corners(side, zb, zb, c["zf1"], c["zf0"])
        if spec.get("quarter_glass", True):
            parts.append(glass_panel("SideGlassR", rear, 0.09, 0.004, m["glass"], bottom_extra=0.07))
        parts.append(glass_panel("SideGlassF", front, 0.08, 0.004, m["glass"], bottom_extra=0.07))
    wind = [P(-1, c["zf0"], c["belt"], c["wb"]), P(1, c["zf0"], c["belt"], c["wb"]),
            P(1, c["zf1"], c["roof"], c["wr"]), P(-1, c["zf1"], c["roof"], c["wr"])]
    back = [P(-1, c["zr0"], c["belt"], c["wb"]), P(-1, c["zr1"], c["roof"], c["wr"]),
            P(1, c["zr1"], c["roof"], c["wr"]), P(1, c["zr0"], c["belt"], c["wb"])]
    parts.append(glass_panel("Windshield", wind, 0.08, 0.004, m["glass"], bottom_extra=0.06))
    parts.append(glass_panel("RearGlass", back, 0.08, 0.004, m["glass"], bottom_extra=0.06))
    return parts


def lights_and_trim(spec, m):
    st = spec["stations"]
    zf, zr = st[-1][0], st[0][0]
    parts = []

    hz = zf - 0.14
    hw, yb, yt = station_at(st, hz)
    hy = yb + 0.62 * (yt - yb)
    for s in (-1, 1):
        x = s * 0.60 * hw
        if spec["lights"] == "round":
            parts.append(cylinder("Head", (x, hy, hz + 0.08), 0.13, 0.12, 'z', m["head"], segments=28, bevel=0.025))
            parts.append(cylinder("HeadRing", (x, hy, hz + 0.05), 0.155, 0.08, 'z', m["chrome"], segments=28, bevel=0.015))
        elif spec["lights"] == "slim":
            parts.append(box("HeadHousing", (x, hy + 0.03, hz + 0.04), (0.50, 0.12, 0.14), m["trim"], bevel=0.04))
            parts.append(box("Head", (x, hy + 0.03, hz + 0.08), (0.44, 0.07, 0.12), m["head"], bevel=0.03))
        else:
            parts.append(box("HeadHousing", (x, hy, hz + 0.04), (0.42, 0.15, 0.14), m["trim"], bevel=0.05))
            parts.append(box("Head", (x - 0.04 * s, hy + 0.005, hz + 0.08), (0.24, 0.10, 0.12), m["head"], bevel=0.04))
            parts.append(box("HeadInner", (x + 0.11 * s, hy + 0.005, hz + 0.075), (0.10, 0.08, 0.12), m["chrome"], bevel=0.03))

    tz = zr + 0.12
    hw, yb, yt = station_at(st, tz)
    ty = yb + 0.66 * (yt - yb)
    for s in (-1, 1):
        if spec["lights"] == "round":
            parts.append(cylinder("Tail", (s * 0.58 * hw, ty, tz - 0.08), 0.10, 0.10, 'z', m["tail"], segments=20, bevel=0.015))
        else:
            parts.append(box("Tail", (s * 0.60 * hw, ty, tz - 0.08), (0.40, 0.12, 0.12), m["tail"], bevel=0.03))
    if spec["lights"] == "slim":
        parts.append(box("TailBar", (0, ty, tz - 0.09), (0.9 * hw, 0.04, 0.08), m["tail"], bevel=0.015))

    hw, yb, yt = station_at(st, zf - 0.05)
    gy = yb + 0.32 * (yt - yb)
    parts.append(box("Grille", (0, gy, zf - 0.02), (0.9 * hw, 0.13, 0.12), m["trim"], bevel=0.03))
    for i in range(3):
        parts.append(box("GrilleSlat", (0, gy - 0.04 + i * 0.04, zf + 0.035), (0.85 * hw, 0.012, 0.03), m["chrome"]))
    bumper_mat = m["chrome"] if spec.get("chrome_bumpers") else m["trim"]
    bumper_h = 0.08 if spec.get("chrome_bumpers") else 0.12
    hwf, ybf, _ = station_at(st, zf - 0.12)
    hwr, ybr, _ = station_at(st, zr + 0.12)
    parts.append(box("BumperF", (0, ybf + 0.05, zf - 0.10), (1.88 * hwf, bumper_h, 0.24), bumper_mat, bevel=0.05))
    parts.append(box("BumperR", (0, ybr + 0.05, zr + 0.10), (1.88 * hwr, bumper_h, 0.24), bumper_mat, bevel=0.05))
    parts.append(box("Plate", (0, ybr + 0.25, zr - 0.005), (0.42, 0.11, 0.03), m["white"], bevel=0.01))
    parts.append(box("PlateF", (0, ybf + 0.16, zf + 0.015), (0.40, 0.10, 0.03), m["white"], bevel=0.01))
    if spec.get("splitter"):
        parts.append(box("Splitter", (0, ybf - 0.01, zf - 0.04), (1.6 * hwf, 0.025, 0.14), m["trim"], bevel=0.008))

    wh = spec["wheel"]
    mid = (wh["zf"] + wh["zr"]) / 2
    sill_len = (wh["zf"] - wh["zr"]) - 2 * (wh["r"] + 0.16)
    c = spec["cabin"]
    hw0, yb0, yt0 = station_at(st, mid)
    for s in (-1, 1):
        parts.append(box("Sill", (s * (side_x(spec, mid, yb0 + 0.07) - 0.015), yb0 + 0.07, mid), (0.08, 0.10, sill_len), m["trim"], bevel=0.025))
        hy2 = c["belt"] - 0.10
        zh = c["zr0"] * 0.35 + c["zf0"] * 0.65 - 0.55
        parts.append(box("Handle", (s * (side_x(spec, zh, hy2) + 0.008), hy2, zh), (0.03, 0.035, 0.16), m["chrome"], bevel=0.01))

        mz = c["zf0"] - 0.22
        my = c["belt"] + 0.10
        mx = s * (c["wb"] + 0.16)
        parts.append(box("Mirror", (mx, my, mz), (0.18, 0.11, 0.20), m["paint"], bevel=0.035))
        parts.append(box("MirrorGlass", (mx, my, mz - 0.105), (0.15, 0.08, 0.012), m["chrome"]))
        parts.append(beam("MirrorArm", (s * (c["wb"] - 0.02), c["belt"] + 0.02, mz + 0.05), (mx - s * 0.06, my - 0.02, mz), 0.04, 0.03, m["trim"]))

        zn = (wh["zf"] + wh["zr"]) / 2 + 0.10
        hwn, ybn, ytn = station_at(st, zn)
        yn = ybn + 0.45 * (ytn - ybn)
        xn = side_x(spec, zn, yn)
        parts.append(cylinder("NumberDisc", (s * (xn + 0.004), yn, zn), 0.23, 0.012, 'x', m["white"], segments=32))
        num = text_mesh("Number", spec["number"], 0.30, m["black"], extrude=0.004)
        num.rotation_euler = (math.radians(90), 0, math.radians(-90 if s > 0 else 90))
        num.location = C(s * (xn + 0.014), yn - 0.01, zn)
        parts.append(num)

    for s in (-1, 1):
        parts.append(cylinder("Exhaust", (s * 0.42, ybr + 0.07, zr - 0.04), 0.045, 0.20, 'z', m["chrome"], segments=16))
    return parts


def extras(spec, m):
    st = spec["stations"]
    zf, zr = st[-1][0], st[0][0]
    c = spec["cabin"]
    parts = []
    # Спойлер больше не часть кузова: детали из библиотеки (parts.py) ставятся на точку SpoilerMount.
    if spec.get("scoop"):
        z = 1.05
        hw, yb, yt = station_at(st, z)
        parts.append(box("Scoop", (0, yt + 0.02, z), (0.52, 0.10, 0.60), m["paint"], bevel=0.04))
        parts.append(box("ScoopMouth", (0, yt + 0.035, z + 0.30), (0.40, 0.05, 0.02), m["trim"]))
        for k in range(3):
            y = 0.55 + k * 0.05
            for s in (-1, 1):
                parts.append(box("SideVent", (s * (side_x(spec, 0.95, y) + 0.004), y, 0.95), (0.02, 0.02, 0.32), m["trim"]))
    if spec.get("stripes"):
        for s in (-1, 1):
            parts.append(box("Stripe", (s * 0.11, c["roof"] + 0.002, (c["zr1"] + c["zf1"]) / 2), (0.12, 0.01, c["zf1"] - c["zr1"] - 0.05), m["white"]))
    if spec.get("lightbar"):
        y = c["roof"] + 0.10
        z = c["zf1"] - 0.15
        parts.append(box("LightBar", (0, y, z), (1.10, 0.08, 0.10), m["trim"], bevel=0.02))
        for k in range(4):
            parts.append(cylinder("BarLamp", (-0.39 + k * 0.26, y + 0.01, z + 0.07), 0.075, 0.05, 'z', m["head"], segments=20))
        for s in (-1, 1):
            parts.append(beam("BarStand", (s * 0.45, c["roof"] - 0.02, z), (s * 0.45, y - 0.03, z), 0.04, 0.04, m["trim"]))
            parts.append(beam("RackRail", (s * 0.50, c["roof"] + 0.05, c["zr1"] + 0.15), (s * 0.50, c["roof"] + 0.05, c["zf1"] - 0.35), 0.035, 0.035, m["chrome"]))
            parts.append(beam("RackFoot", (s * 0.50, c["roof"] - 0.02, c["zr1"] + 0.2), (s * 0.50, c["roof"] + 0.05, c["zr1"] + 0.2), 0.03, 0.03, m["trim"]))
    if spec.get("mudflaps"):
        wh = spec["wheel"]
        for s in (-1, 1):
            for zw in (wh["zf"], wh["zr"]):
                parts.append(box("MudFlap", (s * wh["x"], 0.24, zw - wh["r"] - 0.13), (0.26, 0.30, 0.02), m["trim"], bevel=0.005))
    return parts


def wheel(name, spec, m):
    """Колесо: шина и внутренняя тёмная чаша. Сам диск — отдельный объект (rim), его можно заменить в гараже."""
    wh = spec["wheel"]
    r, w = wh["r"], wh["w"]
    out = w / 2
    parts = [cylinder(name, (0, 0, 0), r, w, 'x', m["tire"], segments=40, bevel=0.05, bevel_segments=3)]
    # Боковина с выпуклой надписью-кольцом и протектор: два тонких кольца, читаются на крупном плане.
    parts.append(cylinder("SidewallRing", (out - 0.004, 0, 0), r * 0.9, 0.012, 'x', m["trim"], segments=40))
    parts.append(cylinder("RimBack", (out - 0.05, 0, 0), r * 0.70, 0.02, 'x', m["trim"], segments=32))
    ob = join(parts[0], parts[1:])
    smooth(ob, 35)
    return ob


def rim(name, style, rim_mat, m, r=0.36, w=0.27):
    """Диск в осях колеса (наружу +x). Используется и для машин, и для библиотеки деталей."""
    out = w / 2
    parts = []
    if style == "dish":
        parts.append(cylinder(name, (out - 0.02, 0, 0), r * 0.66, 0.04, 'x', rim_mat, segments=32, bevel=0.012))
        parts.append(cylinder("DishRing", (out - 0.005, 0, 0), r * 0.40, 0.03, 'x', m["trim"], segments=24))
        for k in range(8):
            a = 2 * math.pi * k / 8
            parts.append(cylinder("DishHole", (out + 0.001, r * 0.53 * math.sin(a), r * 0.53 * math.cos(a)), 0.03, 0.012, 'x', m["trim"], segments=12))
        parts.append(cylinder("Hub", (out + 0.005, 0, 0), r * 0.17, 0.04, 'x', rim_mat, segments=16, bevel=0.01))
    elif style == "mesh":
        parts.append(cylinder(name, (out - 0.025, 0, 0), r * 0.70, 0.035, 'x', rim_mat, segments=32))
        parts.append(cylinder("RimHole", (out - 0.006, 0, 0), r * 0.62, 0.03, 'x', m["trim"], segments=32))
        for k in range(12):
            a = 2 * math.pi * k / 12
            b = a + math.pi / 6
            parts.append(beam("Mesh", (out - 0.01, 0.07 * math.sin(a), 0.07 * math.cos(a)), (out - 0.01, r * 0.64 * math.sin(b), r * 0.64 * math.cos(b)), 0.025, 0.03, rim_mat, bevel=0.005))
            parts.append(beam("Mesh", (out - 0.01, 0.07 * math.sin(b), 0.07 * math.cos(b)), (out - 0.01, r * 0.64 * math.sin(a), r * 0.64 * math.cos(a)), 0.025, 0.03, rim_mat, bevel=0.005))
        parts.append(cylinder("Hub", (out + 0.005, 0, 0), r * 0.15, 0.04, 'x', m["chrome"], segments=16, bevel=0.01))
    else:
        spokes = {"five": 5, "six": 6, "ten": 10}[style]
        thick = {"five": 0.075, "six": 0.07, "ten": 0.04}[style]
        parts.append(cylinder(name, (out - 0.025, 0, 0), r * 0.70, 0.035, 'x', rim_mat, segments=32))
        parts.append(cylinder("RimHole", (out - 0.006, 0, 0), r * 0.62, 0.03, 'x', m["trim"], segments=32))
        for k in range(spokes):
            a = 2 * math.pi * k / spokes
            p0 = (out - 0.01, 0.06 * math.sin(a), 0.06 * math.cos(a))
            p1 = (out - 0.01, r * 0.64 * math.sin(a), r * 0.64 * math.cos(a))
            parts.append(beam("Spoke", p0, p1, thick, 0.035, rim_mat, bevel=0.008))
        parts.append(cylinder("Hub", (out + 0.005, 0, 0), r * 0.17, 0.04, 'x', m["chrome"], segments=16, bevel=0.01))
        # Гайки на ступице
        for k in range(5):
            a = 2 * math.pi * k / 5
            parts.append(cylinder("Nut", (out + 0.02, 0.035 * math.sin(a), 0.035 * math.cos(a)), 0.012, 0.02, 'x', m["chrome"], segments=8))
    ob = join(parts[0], parts[1:])
    smooth(ob, 35)
    return ob


RIM_MATS = {"M_Rim": "rim", "M_RimDark": "rimdark", "M_RimWhite": "rimwhite", "M_Chrome": "chrome"}


def build_car(key):
    spec = CARS[key]
    m = materials(*PAINTS[key])
    root = empty("Car_" + key)
    body = body_shell(spec, m)
    others = cabin(spec, m) + arch_lips(spec, m) + lights_and_trim(spec, m) + extras(spec, m)
    body = join(body, others)
    body.name = "Body"
    body.data.name = "Body_" + key
    set_parent(body, root)

    wh = spec["wheel"]
    rim_mat = m[RIM_MATS[spec["rim"]]]
    for label, x_sign, z in (("FL", -1, wh["zf"]), ("FR", 1, wh["zf"]), ("RL", -1, wh["zr"]), ("RR", 1, wh["zr"])):
        wob = wheel("Wheel_" + label, spec, m)
        wob.name = "Wheel_" + label
        wob.data.name = "Wheel_" + key + "_" + label
        rob = rim("Rim_" + label, spec["rim_style"], rim_mat, m, wh["r"], wh["w"])
        rob.name = "Rim_" + label
        rob.data.name = "Rim_" + key + "_" + label
        # Диск — сосед колеса с тем же трансформом (вложенные объекты экспорт FBX портит);
        # внутрь колеса его переносит сборщик префабов в Unity.
        for ob in (wob, rob):
            if x_sign < 0:
                ob.rotation_euler = (0, 0, math.pi)
            ob.location = C(x_sign * wh["x"], wh["r"], z)
            set_parent(ob, root)

    # Точка крепления спойлера: середина задней кромки крыши багажника.
    st = spec["stations"]
    zr = st[0][0]
    hw, yb, yt = station_at(st, zr + 0.25)
    mount = empty("SpoilerMount", (0, yt, zr + 0.25))
    set_parent(mount, root)
    return root


def build_cars():
    reset_scene()
    os.makedirs(PREVIEW, exist_ok=True)
    roots = {}
    report = []
    for key in CARS:
        # Остальные машины прячем, чтобы крупные планы были чистыми.
        for other in roots.values():
            for ob in [other] + all_children(other):
                ob.hide_render = True
        root = build_car(key)
        roots[key] = root
        tris = sum(tri_count(o) for o in all_children(root))
        report.append(f"{key}: {tris} tris")
        render_preview(os.path.join(PREVIEW, f"car_{key}_front.png"), (0, 0, 0.6), 7.5, 1.7, 35, lens=50)
        render_preview(os.path.join(PREVIEW, f"car_{key}_rear.png"), (0, 0, 0.6), 7.5, 2.2, 215, lens=50)
        export_fbx(root, os.path.join(ART, "Cars", f"Car_{key}.fbx"))
        # Имена в Blender глобальные: освобождаем Body/Wheel_* для следующей машины.
        for ob in all_children(root):
            ob.name = ob.name + "_" + key

    for idx, (key, root) in enumerate(roots.items()):
        root.location.x = (idx - 1.5) * 2.6
        for ob in [root] + all_children(root):
            ob.hide_render = False
    render_preview(os.path.join(PREVIEW, "cars_lineup.png"), (0, 0, 0.5), 14, 4.5, 20, lens=40)
    for root in roots.values():
        root.location.x = 0

    print(chr(10).join(report))


if not globals().get("PARTS_ONLY"):
    build_cars()
