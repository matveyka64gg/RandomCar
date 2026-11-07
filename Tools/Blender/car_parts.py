# Генератор машин v2: детали — колёса и тормоза, фары, решётки, зеркала, салон, мелочи кузова.
# Колёса и диски строятся в осях колеса: x — наружу, y — вверх, z — вперёд, центр в начале координат.


def car_materials():
    """Материалы машины (имена совпадают с библиотекой Unity, см. ArtLibrary)."""
    paint = "#D8442E"
    return dict(
        paint=mat("M_Paint", paint, metallic=0.2, roughness=0.18),
        roof=mat("M_PaintRoof", paint, metallic=0.2, roughness=0.18),
        hood=mat("M_PaintHood", paint, metallic=0.2, roughness=0.18),
        lower=mat("M_PaintLower", paint, metallic=0.2, roughness=0.18),
        glass=mat("M_CarGlass", "#0E1620", metallic=0.0, roughness=0.03, alpha=0.38),
        frame=mat("M_GlassFrit", "#08090B", roughness=0.25),
        trim=mat("M_Trim", "#26282E", roughness=0.6),
        gloss=mat("M_TrimGloss", "#0B0C0E", roughness=0.12),
        chrome=mat("M_Chrome", "#E6EAF0", metallic=1.0, roughness=0.12),
        liner=mat("M_Liner", "#0D0E10", roughness=0.95),
        under=mat("M_Underbody", "#141518", roughness=0.9),
        housing=mat("M_LightHousing", "#2A2E35", metallic=0.9, roughness=0.15),
        head=mat("M_LightHead", "#FFF6DC", emission="#FFF6DC", strength=2.0),
        drl=mat("M_LightDrl", "#E8F4FF", emission="#E8F4FF", strength=4.0),
        lens=mat("M_LightLens", "#C8D4E0", metallic=0.8, roughness=0.05),
        tail=mat("M_LightTail", "#FF2A2A", emission="#FF1A1A", strength=2.0),
        taildark=mat("M_LightTailDark", "#5A0A0E", roughness=0.15),
        amber=mat("M_LightAmber", "#FF9A1A", emission="#FF8A00", strength=1.0),
        reverse=mat("M_LightReverse", "#F2F4F8", emission="#FFFFFF", strength=0.3),
        grille=mat("M_Grille", "#111214", roughness=0.5),
        mesh=mat("M_GrilleMesh", "#202226", metallic=0.6, roughness=0.35),
        plate=mat("M_Plate", "#F2F2EE", roughness=0.4),
        interior=mat("M_Interior", "#1E1F23", roughness=0.75),
        seat=mat("M_Seat", "#2A2B30", roughness=0.8),
        accent=mat("M_InteriorAccent", "#B3302A", roughness=0.7),
        display=mat("M_Display", "#0B1A2A", emission="#3FA0FF", strength=0.6),
        tire=mat("M_Tire", "#1A1A1D", roughness=0.88),
        rim=mat("M_Rim", "#C9CED6", metallic=0.9, roughness=0.28),
        rimdark=mat("M_RimDark", "#2E3238", metallic=0.8, roughness=0.35),
        disc=mat("M_BrakeDisc", "#6E7074", metallic=0.9, roughness=0.45),
        rimwhite=mat("M_RimWhite", "#F2F0E8", roughness=0.4),
        caliper=mat("M_Caliper", "#C8202A", metallic=0.2, roughness=0.35),
        carbon=mat("M_Carbon", "#1B1D21", metallic=0.3, roughness=0.25),
        white=mat("M_Decal_White", "#F2F0E8", roughness=0.4),
        black=mat("M_Decal_Black", "#15161A", roughness=0.4),
    )


# ---------- колесо ----------

def revolve(name, profile, segments, material, axis_x=True):
    """Тело вращения вокруг оси x колеса: профиль [(x, r)], последовательность по контуру."""
    bm = bmesh.new()
    rows = []
    for k in range(segments):
        a = 2 * math.pi * k / segments
        ca, sa = math.cos(a), math.sin(a)
        rows.append([bm.verts.new(C(x, r * sa, r * ca)) for (x, r) in profile])
    n = len(profile)
    for k in range(segments):
        k2 = (k + 1) % segments
        for i in range(n - 1):
            bm.faces.new((rows[k][i], rows[k][i + 1], rows[k2][i + 1], rows[k2][i]))
    ob = obj_from_bmesh(name, bm, [material])
    return ob


def tire(name, R, W, rim_r, m, style="road", segments=56):
    """Шина с профилем: боковины с «пузом» и рёбрами, плечи, протектор с продольными канавками,
    блоки на плечах (по ним видно вращение колеса). Внедорожная — шашки по всей ширине."""
    h = R - rim_r
    hw = W / 2
    side = 0.62 if style != "low" else 0.5

    def wall(sign):
        # Боковина от обода к плечу: защитный выступ у обода, ребро посередине, «пузо».
        return [(sign * (hw - 0.012), rim_r - 0.004), (sign * (hw + 0.004), rim_r + h * 0.10),
                (sign * (hw + 0.007), rim_r + h * 0.18), (sign * (hw + 0.002), rim_r + h * 0.26),
                (sign * (hw + 0.006), rim_r + h * 0.40), (sign * (hw + 0.011), rim_r + h * 0.45),
                (sign * (hw + 0.007), rim_r + h * 0.50), (sign * (hw + 0.008), rim_r + h * side),
                (sign * (hw - 0.004), R - 0.022), (sign * (hw - 0.022), R - 0.004)]
    prof = wall(-1)
    grooves = 3 if style != "offroad" else 0
    tread = [(-hw + 0.03, R)]
    for g in range(grooves):
        gx = -hw + W * (g + 1) / (grooves + 1)
        tread += [(gx - 0.008, R), (gx - 0.006, R - 0.007), (gx + 0.006, R - 0.007), (gx + 0.008, R)]
    tread.append((hw - 0.03, R))
    prof += tread
    prof += list(reversed(wall(1)))
    ob = revolve(name, prof, segments, m["tire"])
    if style == "whitewall":
        # Белая полоса на внешней боковине — классические шины.
        r0, r1 = rim_r + h * 0.22, rim_r + h * 0.62
        ring = revolve(name + "_White", [(hw + 0.009, r0), (hw + 0.009, r1), (hw + 0.015, r1), (hw + 0.015, r0)], segments, m["white"])
        ob = join(ob, [ring])
    lugs = []
    if style == "offroad":
        # Внедорожный протектор: шашки по кругу (две ступенчатые дорожки).
        n = 26
        for k in range(n):
            a = 2 * math.pi * (k + 0.5) / n
            for side_sign, off in ((-1, 0.0), (1, 0.5)):
                aa = a + off * 2 * math.pi / n
                cx = side_sign * W * 0.22
                lugs.append(_lug(cx, R, aa, W * 0.36, m["tire"]))
    else:
        # Блоки на плечах — по ним глаз видит, что колесо крутится (гладкая шина казалась неподвижной).
        n = max(28, int(2 * math.pi * R / 0.06))
        for k in range(n):
            a = 2 * math.pi * k / n
            for sgn in (-1, 1):
                lugs.append(_lug(sgn * (hw - W * 0.17), R, a + (0.5 * math.pi / n if sgn > 0 else 0.0), W * 0.12,
                                 m["tire"], length=0.026, height=0.008))
    if lugs:
        ob = join(ob, lugs)
    smooth(ob, 40)
    return ob


def _lug(cx, R, a, width, material, length=0.075, height=0.022):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=Vector((width, length, height)), verts=bm.verts)   # Blender: X — ширина, Y — вдоль окружности, Z — высота
    bmesh.ops.translate(bm, vec=Vector((0, 0, R + height * 0.5 - 0.005)), verts=bm.verts)
    bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=Matrix.Rotation(-a, 3, 'X'))
    bmesh.ops.translate(bm, vec=Vector((-cx, 0, 0)), verts=bm.verts)
    return obj_from_bmesh("Lug", bm, [material])


def brake(name, R_rim, W, m, big=False):
    """Тормоз: вентилируемый диск с колоколом (вращается) и суппорт (не вращается) — отдельно."""
    ro = R_rim * (0.86 if big else 0.80)
    ri = R_rim * 0.42
    x = -W * 0.08
    disc = revolve(name + "_Disc", [(x - 0.014, ri), (x - 0.014, ro), (x + 0.014, ro), (x + 0.014, ri)], 40, m["disc"])
    hat = cylinder(name + "_Hat", (x + 0.03, 0, 0), ri * 1.02, 0.05, 'x', m["rimdark"], segments=24)
    extra = [hat]
    if big:
        # Перфорация: три ряда отверстий (видна сквозь спицы и вращается вместе с колесом).
        for k in range(18):
            a = 2 * math.pi * k / 18
            rr = lerp(ri * 1.18, ro * 0.9, (k % 3) / 2)
            extra.append(cylinder("Drill", (x + 0.0145, rr * math.sin(a), rr * math.cos(a)), 0.0065, 0.003, 'x', m["rimdark"], segments=6))
    disc = join(disc, extra)
    # Суппорт: дуга у задней верхней части диска.
    bm = bmesh.new()
    rows = []
    a0, a1 = math.radians(95), math.radians(165) if big else math.radians(150)
    steps = 10
    rin, rout = ro * 0.74, ro * 1.06
    for k in range(steps + 1):
        a = a0 + (a1 - a0) * k / steps
        ring = []
        for (xx, rr) in ((x + 0.03, rin), (x + 0.03, rout), (x + 0.06, rout), (x + 0.06, rin)):
            ring.append(bm.verts.new(C(xx, rr * math.sin(a), rr * math.cos(a))))
        rows.append(ring)
    for k in range(steps):
        for q in range(4):
            bm.faces.new((rows[k][q], rows[k][(q + 1) % 4], rows[k + 1][(q + 1) % 4], rows[k + 1][q]))
    bm.faces.new(rows[0])
    bm.faces.new(list(reversed(rows[-1])))
    cal = obj_from_bmesh(name + "_Caliper", bm, [m["caliper"]])
    add_bevel(cal, 0.008, 2, angle=40)
    apply_modifiers(cal)
    smooth(cal, 40)
    smooth(disc, 40)
    return disc, cal


RIM_STYLES = ["five", "ten", "six", "split", "multi", "turbofan", "dish", "mesh", "beadlock", "steel"]


def rim(name, style, R, W, m, rim_mat=None):
    """Диск в осях колеса: обод (бочка), наружная полка, спицы по стилю, колпачок, гайки.
    R — радиус посадки шины (полка), W — ширина. Спицы вогнуты: центр утоплен внутрь."""
    rm = rim_mat or m["rim"]
    hw = W / 2
    parts = []
    # Бочка и полка.
    barrel = revolve(name, [(-hw + 0.01, R * 0.98), (-hw + 0.01, R * 0.92), (hw - 0.03, R * 0.92), (hw - 0.012, R * 0.96),
                            (hw, R * 1.0), (hw + 0.006, R * 1.0), (hw + 0.008, R * 0.97), (hw - 0.004, R * 0.90)], 48, rm)
    parts.append(barrel)
    # Полированная полка обода (хром не перекрашивается вместе с диском — контраст как у настоящих дисков).
    parts.append(revolve("Lip", [(hw - 0.006, R * 0.955), (hw + 0.010, R * 0.955), (hw + 0.012, R * 0.99), (hw - 0.002, R * 1.014)], 48, m["chrome"]))
    face_x = hw - 0.02          # плоскость спиц у обода
    hub_x = hw - 0.065 if style not in ("dish", "steel", "turbofan") else hw - 0.03   # центр утоплен
    hub_r = R * 0.22
    spokes = {"five": 5, "six": 6, "ten": 10, "split": 5, "multi": 20, "mesh": 10}.get(style, 0)

    def spoke(a, width, depth, r0, r1, x0, x1, twist=0.0, mat_=None):
        """Спица — брусок от ступицы (r0, x0) до обода (r1, x1) под углом a."""
        bm = bmesh.new()
        verts = []
        for (rr, xx, tw) in ((r0, x0, 0.0), (r1, x1, twist)):
            aa = a + tw
            ca, sa = math.cos(aa), math.sin(aa)
            # Поперечное направление спицы в плоскости колеса.
            tx, ty = -sa, ca
            for (dw, dd) in ((-width / 2, -depth / 2), (width / 2, -depth / 2), (width / 2, depth / 2), (-width / 2, depth / 2)):
                y = rr * sa + ty * dw * (0.8 if rr > r0 else 1.0)
                z = rr * ca + tx * dw * (0.8 if rr > r0 else 1.0)
                verts.append(bm.verts.new(C(xx + dd, y, z)))
        a_, b_ = verts[:4], verts[4:]
        for i in range(4):
            j = (i + 1) % 4
            bm.faces.new((a_[i], a_[j], b_[j], b_[i]))
        bm.faces.new(a_)
        bm.faces.new(list(reversed(b_)))
        ob = obj_from_bmesh("Spoke", bm, [mat_ or rm])
        add_bevel(ob, 0.006, 2, angle=30)
        return ob

    if style in ("five", "six", "ten"):
        width = {"five": 0.062, "six": 0.052, "ten": 0.030}[style] * R / 0.24
        for k in range(spokes):
            a = 2 * math.pi * k / spokes
            parts.append(spoke(a, width, 0.034, hub_r * 0.9, R * 0.93, hub_x, face_x))
    elif style == "split":
        for k in range(spokes):
            a = 2 * math.pi * k / spokes
            for off in (-0.11, 0.11):
                parts.append(spoke(a + off * 0.6, 0.026 * R / 0.24, 0.03, hub_r * 0.9, R * 0.93, hub_x, face_x, twist=off))
    elif style == "multi":
        for k in range(spokes):
            a = 2 * math.pi * k / spokes
            parts.append(spoke(a, 0.016 * R / 0.24, 0.022, hub_r * 0.95, R * 0.93, hub_x, face_x, twist=0.12))
    elif style == "mesh":
        for k in range(spokes):
            a = 2 * math.pi * k / spokes
            parts.append(spoke(a, 0.016 * R / 0.24, 0.02, hub_r, R * 0.93, hub_x, face_x, twist=0.32))
            parts.append(spoke(a, 0.016 * R / 0.24, 0.02, hub_r, R * 0.93, hub_x, face_x, twist=-0.32))
        parts.append(revolve("MeshRing", [(face_x - 0.004, R * 0.90), (face_x + 0.006, R * 0.90), (face_x + 0.006, R * 0.94), (face_x - 0.004, R * 0.94)], 40, m["chrome"]))
    elif style == "turbofan":
        parts.append(revolve("Fan", [(hub_x, hub_r), (face_x - 0.004, R * 0.92), (face_x + 0.004, R * 0.92), (hub_x + 0.008, hub_r)], 48, rm))
        for k in range(16):
            a = 2 * math.pi * k / 16
            parts.append(spoke(a, 0.03 * R / 0.24, 0.012, R * 0.42, R * 0.86, hub_x + 0.03, face_x + 0.006, twist=0.25, mat_=m["rimdark"]))
    elif style == "dish":
        parts.append(revolve("Dish", [(hub_x, hub_r), (hub_x + 0.01, R * 0.6), (face_x, R * 0.9), (face_x + 0.006, R * 0.92)], 48, rm))
        for k in range(8):
            a = 2 * math.pi * k / 8
            parts.append(cylinder("Hole", (hub_x + 0.018, R * 0.56 * math.sin(a), R * 0.56 * math.cos(a)), R * 0.11, 0.014, 'x', m["rimdark"], segments=16))
    elif style == "steel":
        parts.append(revolve("Steel", [(hub_x, hub_r), (hub_x + 0.004, R * 0.52), (hub_x - 0.012, R * 0.66), (face_x - 0.01, R * 0.9), (face_x, R * 0.93)], 48, rm))
        for k in range(6):
            a = 2 * math.pi * (k + 0.5) / 6
            parts.append(cylinder("Hole", (hub_x - 0.002, R * 0.6 * math.sin(a), R * 0.6 * math.cos(a)), R * 0.085, 0.02, 'x', m["rimdark"], segments=14))
    elif style == "beadlock":
        for k in range(8):
            a = 2 * math.pi * k / 8
            parts.append(spoke(a, 0.07 * R / 0.24, 0.03, hub_r, R * 0.86, hub_x + 0.02, face_x - 0.01, mat_=rm))
        parts.append(revolve("Beadlock", [(face_x, R * 0.86), (face_x + 0.022, R * 0.86), (face_x + 0.022, R * 1.03), (face_x, R * 1.03)], 48, m["rimdark"]))
        for k in range(24):
            a = 2 * math.pi * k / 24
            parts.append(cylinder("Bolt", (face_x + 0.026, R * 0.945 * math.sin(a), R * 0.945 * math.cos(a)), 0.007, 0.01, 'x', m["chrome"], segments=6))
    # Вентиль у обода.
    parts.append(cylinder("Valve", (face_x + 0.006, R * 0.86, 0.0), 0.007, 0.03, 'x', m["chrome"], segments=8))
    # Ступица, колпачок и гайки.
    parts.append(cylinder("Hub", (hub_x + 0.004, 0, 0), hub_r, 0.026, 'x', rm, segments=24, bevel=0.006))
    parts.append(cylinder("Cap", (hub_x + 0.018, 0, 0), hub_r * 0.45, 0.012, 'x', m["rimdark"], segments=20, bevel=0.004))
    for k in range(5):
        a = 2 * math.pi * k / 5 + 0.3
        parts.append(cylinder("Nut", (hub_x + 0.02, hub_r * 0.68 * math.sin(a), hub_r * 0.68 * math.cos(a)), 0.009, 0.016, 'x', m["chrome"], segments=6))
    ob = join(parts[0], parts[1:])
    smooth(ob, 35)
    return ob


# ---------- фары и решётки ----------

def headlight(surface, d, m, side, outline, style, signal="head"):
    """Фара: корпус-накладка по форме кузова, хромированный отражатель, линзы с ободками, полоса ходовых огней,
    янтарная секция поворотника (signal "head" — у внешнего края, "blade" — у внутреннего).
    Луч идёт спереди и чуть снаружи (wrap) — фары на скруглённых углах не растягиваются."""
    parts = []
    h = d["head"]
    sl = (-side * h.get("wrap", 0.35), h.get("tilt", 0.0), 0.0)
    ref = d["nose"] - h.get("depth", 0.15)

    def P(name, ol, material, lift, rings):
        return patch(name, surface, ol, 'z+', material, lift=lift, rings=rings, slant=sl, ref=ref)

    parts.append(P("Head", outline, m["housing"], 0.006, 4))
    parts.append(band("HeadRim", surface, outline, 0.010, 'z+', m["gloss"], lift=0.0075, slant=sl, ref=ref))
    us = [p[0] for p in outline]
    vs = [p[1] for p in outline]
    wu, hv = max(us) - min(us), max(vs) - min(vs)
    cu = sum(us) / len(us)
    cv = sum(vs) / len(vs)
    inner = poly_offset_true(outline, 0.014)
    # Поворотник — отдельная янтарная секция; линзы сдвигаются от неё.
    amber = None
    if signal in ("head", "blade"):
        amber = segment_u(inner, side, 0.0, 0.2 if signal == "head" else 0.17, from_outer=(signal == "head"))
    shift = 0.0
    if amber is not None and signal == "head":
        shift = -side * wu * 0.10
    if style in ("projector", "slim", "wide"):
        # Хромированный отражатель под линзами: фара блестит и днём, а не выглядит тёмной дырой.
        parts.append(P("Reflector", inner, m["chrome"], 0.0068, 3))
        drl = []
        u0, u1 = min(us) + wu * 0.08, max(us) - wu * 0.08
        steps = 12
        for k in range(steps + 1):
            u = lerp(u0, u1, k / steps)
            drl.append((u, _top_v(outline, u) - hv * 0.12))
        for k in range(steps, -1, -1):
            u = lerp(u0, u1, k / steps)
            drl.append((u, _top_v(outline, u) - hv * 0.12 - max(0.012, hv * 0.12)))
        if amber is not None and signal == "head":
            drl = segment_u(drl, side, 0.21, 1.0) or drl
        parts.append(P("Drl", drl, m["drl"], 0.0095, 1))
        n = 2 if wu > 0.32 else 1
        for k in range(n):
            u = cu + shift + (k - (n - 1) / 2) * wu * 0.30 * side
            rr = min(0.05, hv * 0.30)
            parts.append(P("LensRing", ellipse(u, cv - hv * 0.10, rr * 1.4, rr * 1.4, 20), m["housing"], 0.0090, 2))
            parts.append(P("LensChrome", ellipse(u, cv - hv * 0.10, rr * 1.22, rr * 1.22, 20), m["chrome"], 0.0105, 2))
            parts.append(P("Lens", ellipse(u, cv - hv * 0.10, rr, rr, 20), m["head"], 0.0125, 2))
    elif style == "blade":
        # Фара-лезвие суперкара: светящаяся окантовка по контуру и одна узкая линза.
        parts.append(P("Reflector", inner, m["chrome"], 0.0068, 3))
        parts.append(band("Drl", surface, outline, 0.012, 'z+', m["drl"], lift=0.009, slant=sl, ref=ref))
        parts.append(P("Lens", ellipse(cu - side * wu * 0.06, cv, wu * 0.18, hv * 0.24, 16), m["head"], 0.011, 2))
    elif style == "round":
        parts.append(P("Reflector", ellipse(cu, cv, wu * 0.44, hv * 0.44, 32), m["chrome"], 0.009, 3))
        parts.append(P("LensRing", ellipse(cu, cv, wu * 0.27, hv * 0.27, 28), m["housing"], 0.0105, 2))
        parts.append(P("Lens", ellipse(cu, cv, wu * 0.22, hv * 0.22, 28), m["head"], 0.013, 2))
        parts.append(band("Drl", surface, ellipse(cu, cv, wu * 0.47, hv * 0.47, 40), 0.014, 'z+', m["drl"], lift=0.011, slant=sl, ref=ref))
    if amber is not None:
        parts.append(P("Signal", amber, m["amber"], 0.0098, 2))
    return parts


def _top_v(outline, u):
    """Верхняя граница контура над точкой u (линейно по ближайшим точкам)."""
    best = None
    n = len(outline)
    for i in range(n):
        a, b = outline[i], outline[(i + 1) % n]
        if (a[0] - u) * (b[0] - u) <= 0 and a[0] != b[0]:
            t = (u - a[0]) / (b[0] - a[0])
            v = a[1] + (b[1] - a[1]) * t
            if best is None or v > best:
                best = v
    return best if best is not None else max(p[1] for p in outline)


def _ring_outline(cu, cv, a0, b0, a1, b1, n=32):
    outer = ellipse(cu, cv, a0, b0, n)
    inner = list(reversed(ellipse(cu, cv, a1, b1, n)))
    return outer + [outer[0]] + inner + [inner[-1]]


def taillight(surface, d, m, side, outline, split="h"):
    """Фонарь: тёмно-красный корпус, секции под общим стеклом — поворотник (янтарь), стоп/габарит (красный, светится
    сильнее при торможении — CarLights), задний ход (белый). split "h" — секции по ширине (узкие фонари),
    "v" — по высоте (вертикальные фонари внедорожников), None — круглый фонарь с белой линзой в центре."""
    t = d["tail_lights"]
    sl = (-side * t.get("wrap", 0.3), 0.0, 0.0)
    ref = d["tail"] + t.get("depth", 0.12)

    def P(name, ol, material, lift, rings=2):
        return patch(name, surface, ol, 'z-', material, lift=lift, rings=rings, slant=sl, ref=ref)

    parts = [P("Tail", outline, m["taildark"], 0.006, 3)]
    inner = poly_offset_true(outline, 0.014)
    if split == "h":
        segs = [(segment_u(inner, side, 0.0, 0.17), m["amber"]),
                (segment_u(inner, side, 0.19, 0.80), m["tail"]),
                (segment_u(inner, side, 0.82, 1.0), m["reverse"])]
    elif split == "v":
        segs = [(segment_v(inner, 0.76, 1.0), m["amber"]),
                (segment_v(inner, 0.30, 0.74), m["tail"]),
                (segment_v(inner, 0.0, 0.28), m["reverse"])]
    else:
        segs = [(inner, m["tail"])]
    for poly, mt in segs:
        if poly is not None:
            parts.append(P("TailGlow", poly, mt, 0.009))
    if split is None:
        us = [p[0] for p in inner]
        vs = [p[1] for p in inner]
        cu, cv = sum(us) / len(us), sum(vs) / len(vs)
        r = (max(us) - min(us)) * 0.2
        parts.append(P("TailReverse", ellipse(cu, cv, r, r, 20), m["reverse"], 0.0115))
        parts.append(band("TailChrome", surface, ellipse(cu, cv, r * 1.35, r * 1.35, 24), r * 0.3, 'z-', m["chrome"], lift=0.011, slant=sl, ref=ref))
    parts.append(band("TailRim", surface, outline, 0.008, 'z-', m["gloss"], lift=0.0075, slant=sl, ref=ref))
    return parts


def strip(surface, points, axis, width, material, lift=0.005, name="Strip"):
    """Тонкая лента по поверхности вдоль ломаной (линии стыков, молдинги, щётки)."""
    left, right = [], []
    n = len(points)
    for i in range(n):
        a = points[max(0, i - 1)]
        b = points[min(n - 1, i + 1)]
        du, dv = b[0] - a[0], b[1] - a[1]
        L = math.hypot(du, dv) or 1.0
        nu, nv = -dv / L, du / L
        left.append((points[i][0] + nu * width / 2, points[i][1] + nv * width / 2))
        right.append((points[i][0] - nu * width / 2, points[i][1] - nv * width / 2))
    outline = left + list(reversed(right))
    return patch(name, surface, outline, axis, material, lift=lift, rings=1)


# ---------- салон ----------

def rounded_box(name, center, size, material, bevel=0.03, segments=3):
    return box(name, center, size, material, bevel=bevel, segments=segments)


def seat(name, x, z, floor, m, sport=True, mat_=None):
    """Сиденье: подушка, спинка с наклоном, подголовник, боковая поддержка."""
    sm = mat_ or m["seat"]
    parts = [rounded_box(name, (x, floor + 0.20, z), (0.48, 0.11, 0.48), sm, 0.035)]
    back = rounded_box(name + "_Back", (x, floor + 0.52, z - 0.26), (0.46, 0.58, 0.10), sm, 0.035)
    _tilt(back, (x, floor + 0.24, z - 0.26), -14)
    parts.append(back)
    head = rounded_box(name + "_Head", (x, floor + 0.88, z - 0.33), (0.26, 0.16, 0.08), sm, 0.03)
    _tilt(head, (x, floor + 0.24, z - 0.26), -14)
    parts.append(head)
    if sport:
        for s in (-1, 1):
            bol = rounded_box(name + "_Bolster", (x + s * 0.22, floor + 0.50, z - 0.22), (0.07, 0.48, 0.14), m["accent"], 0.03)
            _tilt(bol, (x, floor + 0.24, z - 0.26), -14)
            parts.append(bol)
    return join(parts[0], parts[1:])


def _tilt(ob, pivot_car, deg):
    """Наклон вокруг поперечной оси через точку pivot (спинка назад — отрицательный угол)."""
    apply_modifiers(ob) if ob.modifiers else None
    p = C(*pivot_car)
    R = Matrix.Translation(p) @ Matrix.Rotation(math.radians(-deg), 4, 'X') @ Matrix.Translation(-p)
    ob.data.transform(R)


def steering_wheel(name, m, radius=0.185):
    """Руль в собственных осях: ось колонки — z (к водителю — минус z), центр в начале координат."""
    parts = [revolve(name, _torus_profile(radius, 0.018), 40, m["interior"])]
    # Revolve строит вокруг оси x; руль нужен вокруг z: поворачиваем.
    for k in range(3):
        a = math.radians(90 + 120 * k)
        parts.append(beam("WheelSpoke", (0, 0, 0), (0.0, radius * math.sin(a) * 0.95, radius * math.cos(a) * 0.95), 0.03, 0.016, m["interior"], bevel=0.006))
    parts.append(cylinder("WheelHub", (0.0, 0, 0), 0.055, 0.05, 'x', m["interior"], segments=20, bevel=0.01))
    ob = join(parts[0], parts[1:])
    smooth(ob, 40)
    # Ось колеса x → ось колонки z.
    ob.data.transform(Matrix.Rotation(math.radians(-90), 4, 'Z'))
    return ob


def _torus_profile(R, r, n=10):
    return [(r * math.cos(2 * math.pi * k / n), R + r * math.sin(2 * math.pi * k / n)) for k in range(n + 1)]


def cabin_liner(d, prof, m):
    """Обшивка салона изнутри: дверные карты, потолок, пол. Видна сквозь стёкла, закрывает «пустую» оболочку."""
    g = d["greenhouse"]
    z0 = g["deck"] + 0.05
    z1 = g["cowl"] - 0.05
    rows = []
    bm = bmesh.new()
    z = z0
    steps = max(4, int((z1 - z0) / 0.08))
    for k in range(steps + 1):
        z = lerp(z0, z1, k / steps)
        pts = ring_points(prof, z)
        yc = (prof.bottom(z) + prof.top(z)) * 0.5
        row = []
        for x, y, seg, left in pts:
            row.append(bm.verts.new(C(x * 0.94, yc + (y - yc) * 0.95, z)))
        rows.append(row)
    n = len(rows[0])
    for k in range(steps):
        for i in range(n):
            j = (i + 1) % n
            bm.faces.new((rows[k][i], rows[k + 1][i], rows[k + 1][j], rows[k][j]))
    bm.faces.new(rows[0])
    bm.faces.new(list(reversed(rows[-1])))
    ob = obj_from_bmesh("CabinLiner", bm, [m["interior"]], recalc=False)
    # Нормали — внутрь салона.
    me = ob.data
    centre = C(0, (prof.bottom((z0 + z1) / 2) + prof.top((z0 + z1) / 2)) / 2, (z0 + z1) / 2)
    flip = 0
    for p in me.polygons:
        if p.normal.dot(centre - p.center) < 0:
            flip += 1
    if flip > len(me.polygons) / 2:
        for p in me.polygons:
            p.flip()
    return ob


def interior(d, prof, m):
    """Салон: сиденья, торпедо с приборами и экраном, тоннель, рычаг. Руль и обшивка (CabinLiner) — отдельные
    объекты: обшивку камера из салона прячет, иначе она закрыла бы весь вид."""
    it = d["interior"]
    floor = it["floor"]
    parts = []
    sx = it["seat_x"]
    zs = it["seat_z"]
    sport = it.get("sport_seats", True)
    parts.append(seat("SeatL", -sx, zs, floor, m, sport))
    parts.append(seat("SeatR", sx, zs, floor, m, sport))
    if it.get("rear_seats", False):
        zr = it["rear_z"]
        parts.append(rounded_box("RearSeat", (0, floor + 0.22, zr), (2 * sx + 0.45, 0.12, 0.46), m["seat"], 0.04))
        back = rounded_box("RearBack", (0, floor + 0.52, zr - 0.24), (2 * sx + 0.45, 0.52, 0.10), m["seat"], 0.04)
        _tilt(back, (0, floor + 0.26, zr - 0.24), -18)
        parts.append(back)
    # Торпедо: широкий скруглённый брус под лобовым, козырёк приборов и центральный экран.
    g = d["greenhouse"]
    zd = it["dash_z"]
    dy = it["dash_y"]
    half = prof.hw(zd) * 0.88
    parts.append(rounded_box("Dash", (0, dy, zd), (2 * half, 0.20, 0.36), m["interior"], 0.06))
    parts.append(rounded_box("DashTop", (0, dy + 0.11, zd + 0.06), (2 * half - 0.1, 0.04, 0.30), m["gloss"], 0.02))
    parts.append(rounded_box("Binnacle", (-sx, dy + 0.13, zd - 0.12), (0.34, 0.09, 0.14), m["interior"], 0.035))
    parts.append(rounded_box("Cluster", (-sx, dy + 0.12, zd - 0.19), (0.28, 0.07, 0.01), m["display"], 0.004))
    parts.append(rounded_box("Screen", (0, dy + 0.15, zd - 0.12), (0.24, 0.13, 0.015), m["display"], 0.006))
    parts.append(rounded_box("Console", (0, floor + 0.20, zs + 0.10), (0.20, 0.20, 0.70), m["interior"], 0.04))
    parts.append(cylinder("Shifter", (0, floor + 0.36, zs + 0.18), 0.018, 0.14, 'y', m["chrome"], segments=12))
    parts.append(rounded_box("Floor", (0, floor + 0.02, (g["deck"] + g["cowl"]) * 0.5), (2 * prof.hw((g["deck"] + g["cowl"]) * 0.5) * 0.9, 0.04, abs(g["cowl"] - g["deck"]) * 0.9), m["interior"], 0.02))
    ob = join(parts[0], parts[1:])
    ob.name = "Interior"
    return ob


def pod(name, centre, length, width, height, material, nose=0.6, segments=20, rings=10):
    """Обтекаемый корпус (зеркало, фара-люстра): лофт эллипсов вдоль z, нос скруглён, корма срезана."""
    cx, cy, cz = centre
    bm = bmesh.new()
    rows = []
    for k in range(rings + 1):
        t = k / rings                       # 0 — корма (плоская), 1 — нос
        z = cz - length / 2 + length * t
        s = math.sqrt(max(0.0, 1 - (max(0.0, t - (1 - nose)) / nose) ** 2)) if t > 1 - nose else 1.0
        s = max(s, 0.08)
        row = []
        for j in range(segments):
            a = 2 * math.pi * j / segments
            row.append(bm.verts.new(C(cx + width / 2 * s * math.cos(a), cy + height / 2 * s * math.sin(a), z)))
        rows.append(row)
    for k in range(rings):
        for j in range(segments):
            j2 = (j + 1) % segments
            bm.faces.new((rows[k][j], rows[k][j2], rows[k + 1][j2], rows[k + 1][j]))
    bm.faces.new(list(reversed(rows[0])))
    bm.faces.new(rows[-1])
    ob = obj_from_bmesh(name, bm, [material])
    smooth(ob, 50)
    return ob


def mirror(name, base, m, side, style="stock"):
    """Зеркало: обтекаемый корпус на ножке у основания стойки; стекло — на плоской задней стенке."""
    x, y, z = base
    ox = x + side * (0.12 if style == "stock" else 0.15)
    parts = []
    if style == "stock":
        parts.append(pod(name, (ox, y + 0.07, z), 0.12, 0.21, 0.12, m["roof"]))
        parts.append(pod(name + "_Glass", (ox, y + 0.07, z - 0.061), 0.004, 0.18, 0.095, m["chrome"], nose=0.01, rings=1))
        parts.append(beam(name + "_Arm", (x - side * 0.015, y + 0.005, z + 0.01), (ox - side * 0.06, y + 0.05, z), 0.06, 0.035, m["roof"], bevel=0.012))
    else:
        parts.append(pod(name, (ox, y + 0.09, z - 0.02), 0.14, 0.17, 0.085, m["carbon"]))
        parts.append(pod(name + "_Glass", (ox, y + 0.09, z - 0.091), 0.004, 0.15, 0.065, m["chrome"], nose=0.01, rings=1))
        for dz in (-0.03, 0.03):
            parts.append(beam(name + "_Arm", (x - side * 0.015, y, z + dz), (ox - side * 0.05, y + 0.08, z - 0.01 + dz * 0.5), 0.014, 0.03, m["carbon"], bevel=0.004))
    ob = join(parts[0], parts[1:])
    smooth(ob, 45)
    return ob


# ---------- обвес по контуру кузова ----------

def contour(surface, y, centre_z, angles, radius=4.0):
    """Контур кузова на высоте y в плане: лучи из окружности к центру (0, y, centre_z)."""
    pts = []
    for a in angles:
        ar = math.radians(a)
        dx, dz = math.sin(ar), math.cos(ar)
        origin = (dx * radius, y, centre_z + dz * radius)
        p, n = surface.hit(origin, (-dx, 0.0, -dz), radius * 2)
        if p is not None:
            pts.append((p.x, p.z, n.x, n.z))
    return pts


def lip(name, surface, y, centre_z, angles, out, thick, material, drop=0.0):
    """Губа/сплиттер/диффузор: лента по контуру кузова на высоте y, выступ out наружу, толщина thick."""
    pts = contour(surface, y, centre_z, angles)
    bm = bmesh.new()
    rows = []
    for (x, z, nx, nz) in pts:
        L = math.hypot(nx, nz) or 1.0
        nx, nz = nx / L, nz / L
        ring = []
        for (o, dy) in ((-0.03, 0.0), (out, -drop), (out, -drop - thick), (-0.03, -thick)):
            ring.append(bm.verts.new(C(x + nx * o, y + dy, z + nz * o)))
        rows.append(ring)
    for k in range(len(rows) - 1):
        for q in range(4):
            bm.faces.new((rows[k][q], rows[k][(q + 1) % 4], rows[k + 1][(q + 1) % 4], rows[k + 1][q]))
    bm.faces.new(rows[0])
    bm.faces.new(list(reversed(rows[-1])))
    ob = obj_from_bmesh(name, bm, [material])
    smooth(ob, 40)
    return ob


def fins(name, surface, y0, y1, z_ref, xs, depth, material):
    """Вертикальные рёбра диффузора под задним бампером."""
    parts = []
    for x in xs:
        p, n = surface.hit((x, (y0 + y1) / 2, z_ref - 4.0), (0, 0, 1), 8.0)
        if p is None:
            continue
        parts.append(box(name, (x, (y0 + y1) / 2, p.z + depth * 0.5 - 0.02), (0.012, y1 - y0, depth), material, bevel=0.003))
    return parts
