# Генератор машин v3: детализация кузова. Запуск после car_parts.py (нужны patch/band/strip, профили кузова).
#
# Что добавляет к кузову v2 (всё — накладки по форме кузова лучами BVH, без лишних объектов):
#   швы капота и крышки багажника/двери багажника, лючок бака, молдинги окон (хром или чёрный глянец),
#   чёрный порог, хромовый молдинг борта (классика), дворники, антенна (плавник или штырь с шариком),
#   повторители поворотов, отдельные поворотники в бампере, третий стоп-сигнал, катафоты, брызговики,
#   решётки (ламели, соты, хромовые бруски) с рамкой.
# Отдельные объекты: пилот в шлеме (Driver: цвет шлема — цвет крыши, камера из салона его прячет)
# и каркас безопасности (в салоне раллийной машины и внедорожника).
#
# Характер деталей задаётся классом машины (CLASS_DETAILS), дизайн может переопределить ключи в d["details"].

CLASS_DETAILS = {
    "sport":   dict(trim="black", antenna="fin", grille="slats", signal="head", tail_split="h", third_brake=True, sill=True, repeater=True),
    "sedan":   dict(trim="chrome", antenna="fin", grille="slats", signal="head", tail_split="h", third_brake=True, sill=True, repeater=True),
    "rally":   dict(trim="black", antenna="whip", grille="mesh", signal="head", tail_split="v", third_brake=True, sill=True, repeater=True,
                    mudflaps=True, cage=True),
    "compact": dict(trim="chrome", antenna="whip", grille="bars", signal="bumper", tail_split="v", third_brake=False, sill=False, repeater=False),
    "luxsuv":  dict(trim="chrome", antenna="whip", grille="bars", signal="fender", tail_split="v", third_brake=True, sill=False, repeater=False),
    "offroad": dict(trim="black", antenna="whip", grille="bars", signal="bumper", tail_split="v", third_brake=True, sill=False, repeater=False,
                    mudflaps=True, cage=True),
    "muscle":  dict(trim="chrome", antenna="whip", grille="bars", signal="bumper", tail_split="h", third_brake=False, sill=True, repeater=False),
    "classic": dict(trim="chrome", antenna="whip", grille="bars", signal="bumper", tail_split=None, third_brake=False, sill=False, repeater=False,
                    side_chrome=True),
    "super":   dict(trim="black", antenna=None, grille="mesh", signal="blade", tail_split="h", third_brake=True, sill=True, repeater=False),
    "hyper":   dict(trim="black", antenna=None, grille="mesh", signal="blade", tail_split="h", third_brake=True, sill=True, repeater=False),
}


def details_of(d):
    """Детали машины: настройки класса + переопределения дизайна."""
    out = dict(CLASS_DETAILS[d["cls"]])
    out.update(d.get("details", {}))
    return out


# ---------- контуры ----------

def clip_poly(poly, axis, lo, hi):
    """Отсечение многоугольника полосой lo <= p[axis] <= hi (Сазерленд — Ходжмен). None — пусто."""
    def clip(pts, inside, edge):
        out = []
        n = len(pts)
        for i in range(n):
            a, b = pts[i], pts[(i + 1) % n]
            ia, ib = inside(a), inside(b)
            if ia:
                out.append(a)
            if ia != ib:
                t = (edge - a[axis]) / (b[axis] - a[axis])
                out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
        return out
    p = clip(list(poly), lambda q: q[axis] >= lo, lo)
    if len(p) < 3:
        return None
    p = clip(p, lambda q: q[axis] <= hi, hi)
    return densify(p) if len(p) >= 3 else None


def densify(poly, step=0.02):
    """Промежуточные точки на длинных сторонах: накладка повторяет кривизну кузова, а не режет её хордой."""
    out = []
    n = len(poly)
    for i in range(n):
        a, b = poly[i], poly[(i + 1) % n]
        L = math.hypot(b[0] - a[0], b[1] - a[1])
        k = max(1, int(L / step))
        for s in range(k):
            t = s / k
            out.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    return out


def extent(poly):
    us = [p[0] for p in poly]
    vs = [p[1] for p in poly]
    return min(us), max(us), min(vs), max(vs)


def segment_u(poly, side, frac_a, frac_b, from_outer=True):
    """Часть контура по ширине: доли от внешнего края (side +1 — правая сторона машины: внешний край — max u)."""
    u0, u1, _, _ = extent(poly)
    w = u1 - u0
    outer_max = (side > 0) == from_outer
    if outer_max:
        lo, hi = u1 - w * frac_b, u1 - w * frac_a
    else:
        lo, hi = u0 + w * frac_a, u0 + w * frac_b
    return clip_poly(poly, 0, lo, hi)


def segment_v(poly, frac_a, frac_b):
    """Часть контура по высоте: доли от низа."""
    _, _, v0, v1 = extent(poly)
    h = v1 - v0
    return clip_poly(poly, 1, v0 + h * frac_a, v0 + h * frac_b)


# ---------- кузов ----------

def wheel_clear(d, z, margin=0.04):
    """Точка z не над вырезом арки (накладки по бокам строятся по целой оболочке и повисли бы над колесом)."""
    wh = d["wheel"]
    r = wh["r"] + d.get("arch_gap", 0.05) + margin
    return abs(z - wh["zf"]) > r and abs(z - wh["zr"]) > r


def side_runs(d, z0, z1, step=0.03):
    """Отрезки [z0, z1] (z0 > z1 или наоборот) без арок: список списков z."""
    lo, hi = min(z0, z1), max(z0, z1)
    n = max(2, int((hi - lo) / step))
    runs, cur = [], []
    for k in range(n + 1):
        z = lerp(lo, hi, k / n)
        if wheel_clear(d, z):
            cur.append(z)
        elif cur:
            runs.append(cur)
            cur = []
    if cur:
        runs.append(cur)
    return [r for r in runs if len(r) >= 3]


def seams(d, prof, surface, m):
    """Швы капота и крышки багажника (двери багажника у хэтчбеков и внедорожников), лючок бака."""
    parts = []
    g = d["greenhouse"]
    # Капот: замкнутая линия по верху от основания лобового до передней кромки.
    z0 = g["cowl"] + 0.035
    z1 = d.get("hood_end", d["nose"] - 0.15) - 0.02
    if z1 - z0 > 0.3:
        pts = []
        steps = 10
        for k in range(steps + 1):
            z = lerp(z0, z1, k / steps)
            pts.append((prof.top_hw(z) * 0.86 - 0.02, z))
        right = pts
        left = [(-x, z) for (x, z) in reversed(pts)]
        loop = rounded_poly(right + left, 0.05, 3)
        parts.append(band("SeamHood", surface, loop, 0.007, 'y+', m["frame"], lift=0.0025))
    if d.get("rear_glass_axis", "y+") == "y+":
        # Крышка багажника (у купе и седанов) или моторного отсека (среднемоторные).
        z0 = d["tail"] + 0.10
        z1 = g["deck"] - 0.03
        if z1 - z0 > 0.25:
            pts = []
            steps = 8
            for k in range(steps + 1):
                z = lerp(z0, z1, k / steps)
                pts.append((prof.top_hw(z) * 0.84 - 0.02, z))
            loop = rounded_poly(pts + [(-x, z) for (x, z) in reversed(pts)], 0.05, 3)
            parts.append(band("SeamTrunk", surface, loop, 0.007, 'y+', m["frame"], lift=0.0025))
    elif d.get("tailgate"):
        # Дверь багажника: контур сзади.
        hx, y0, y1 = d["tailgate"]
        loop = rounded_poly([(hx, y0), (hx, y1), (-hx, y1), (-hx, y0)], 0.05, 3)
        parts.append(band("SeamGate", surface, densify(loop, 0.03), 0.007, 'z-', m["frame"], lift=0.0025))
    # Лючок бака: круглый шов на правом заднем крыле.
    wh = d["wheel"]
    fz = wh["zr"] + wh["r"] + d.get("arch_gap", 0.05) + 0.10
    # Не на линии двери: лючок сдвигается к арке, если шов двери рядом.
    for line in d.get("door_lines", []):
        lz = (line[0][0] + line[1][0]) / 2
        if abs(lz - fz) < 0.11:
            fz = lz - 0.11 if lz - 0.11 > wh["zr"] + wh["r"] + d.get("arch_gap", 0.05) + 0.075 else lz + 0.11
    fy = prof.belt(fz) - 0.11
    if not d.get("side_intakes"):
        parts.append(band("FuelDoor", surface, ellipse(fz, fy, 0.065, 0.065, 28), 0.006, 'x+', m["frame"], lift=0.0025))
    return parts


def window_trim(d, prof, surface, m, det):
    """Молдинг вдоль низа боковых окон (хром у седанов, классики и люкса; чёрный глянец у спортивных)."""
    parts = []
    mat_ = m["chrome"] if det["trim"] == "chrome" else m["gloss"]
    for poly in d["side_windows"]:
        ymin = min(p[1] for p in poly)
        low = [p for p in poly if p[1] < ymin + 0.03]
        za, zb = min(p[0] for p in low), max(p[0] for p in low)
        if zb - za < 0.15:
            continue
        y = ymin - d.get("window_trim", 0.03) - 0.012
        pts = [(lerp(zb + 0.02, za - 0.02, k / 12), y) for k in range(13)]
        for axis in ('x+', 'x-'):
            parts.append(strip(surface, pts, axis, 0.018, mat_, lift=0.003, name="BeltTrim"))
    return parts


def side_lines(d, prof, surface, m, det):
    """Чёрный порог между арками; у классики — хромовый молдинг вдоль всего борта."""
    parts = []
    wh = d["wheel"]
    if det.get("sill"):
        za = wh["zr"] + wh["r"] + d.get("arch_gap", 0.05) + 0.06
        zb = wh["zf"] - wh["r"] - d.get("arch_gap", 0.05) - 0.06
        if zb - za > 0.4:
            y = prof.bottom((za + zb) / 2) + 0.075
            pts = [(lerp(zb, za, k / 16), y) for k in range(17)]
            for axis in ('x+', 'x-'):
                parts.append(strip(surface, pts, axis, 0.06, m["gloss"], lift=0.003, name="Sill"))
    if det.get("side_chrome"):
        for run in side_runs(d, d["nose"] - 0.25, d["tail"] + 0.25):
            pts = [(z, prof.belt(z) - 0.13) for z in reversed(run)]
            for axis in ('x+', 'x-'):
                parts.append(strip(surface, pts, axis, 0.016, m["chrome"], lift=0.003, name="SideChrome"))
    return parts


def wipers(d, prof, surface, m):
    """Два дворника у основания лобового стекла (лежат на стекле)."""
    parts = []
    g = d["greenhouse"]
    zc = g["cowl"] - 0.06
    hw = prof.top_hw(zc)
    rise = prof.top(g["roof_front"]) - prof.top(g["cowl"])
    slope = rise / max(0.05, g["cowl"] - g["roof_front"])
    k = 1.0 / max(1.0, slope) ** 2
    for (xa, xb, dz) in ((-0.62, 0.02, -0.07 * k), (0.04, 0.60, -0.06 * k)):
        pa, _ = top_hit(surface, xa * hw, zc)
        pb, _ = top_hit(surface, xb * hw, zc + dz)
        if pa is None or pb is None:
            continue
        a = (pa.x, pa.y + 0.016, pa.z)
        b = (pb.x, pb.y + 0.016, pb.z)
        parts.append(beam("Wiper", a, b, 0.012, 0.016, m["gloss"], bevel=0.004))
        parts.append(cylinder("WiperPivot", (pa.x, pa.y + 0.008, pa.z), 0.018, 0.02, 'y', m["gloss"], segments=10))
    return parts


def antenna(d, prof, surface, m, kind):
    parts = []
    g = d["greenhouse"]
    if kind == "fin":
        z = g["roof_rear"] + 0.16
        p, _ = top_hit(surface, 0.0, z)
        if p is not None:
            parts.append(pod("Antenna", (0.0, p.y + 0.022, p.z), 0.17, 0.05, 0.075, m["gloss"], nose=0.75, segments=14, rings=8))
    elif kind == "whip":
        # Штырь на правом заднем крыле с мультяшным шариком на конце (у пикапа заднее крыло — борт кузова,
        # поэтому штырь на переднем крыле у лобового стекла).
        if d.get("bed"):
            z = g["cowl"] + 0.22
            x = prof.hw(z) - 0.12
        else:
            z = d["tail"] + 0.32
            x = prof.hw(z) - 0.16
        p, _ = top_hit(surface, x, z)
        if p is not None:
            base = (p.x, p.y, p.z)
            tip = (p.x + 0.02, p.y + 0.46, p.z - 0.12)
            parts.append(cylinder("AntennaBase", (p.x, p.y + 0.012, p.z), 0.016, 0.024, 'y', m["gloss"], segments=10))
            parts.append(beam("AntennaWhip", base, tip, 0.006, 0.006, m["chrome"], bevel=0.0))
            bm = bmesh.new()
            bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=0.017)
            bmesh.ops.translate(bm, vec=C(*tip), verts=bm.verts)
            ball = obj_from_bmesh("AntennaBall", bm, [m["amber"]])
            smooth(ball, 80)
            parts.append(ball)
    return parts


def signals(d, prof, surface, m, det):
    """Повторители на крыльях и поворотники в бампере (у машин с круглыми/прямоугольными фарами без янтарной секции)."""
    parts = []
    wh = d["wheel"]
    if det.get("repeater"):
        z = wh["zf"] - wh["r"] - d.get("arch_gap", 0.05) - 0.14
        y = prof.belt(z) - 0.07
        for axis in ('x+', 'x-'):
            parts.append(patch("Repeater", surface, rounded_rect(z, y, 0.085, 0.026, 0.012, 16), axis, m["amber"], lift=0.004, rings=1))
    if det.get("signal") == "bumper":
        h = d["head"]["outline"]
        u0, u1, v0, v1 = extent(h)
        cu = (u0 + u1) / 2
        cv = v0 - 0.075
        if cv - prof.bottom(d["nose"] - 0.1) > 0.08:
            for s in (1, -1):
                parts.append(patch("SignalF", surface, rounded_rect(s * cu, cv, 0.12, 0.045, 0.02, 20), 'z+', m["amber"], lift=0.006, rings=1))
                parts.append(band("SignalFRim", surface, rounded_rect(s * cu, cv, 0.12, 0.045, 0.02, 20), 0.008, 'z+', m["chrome"], lift=0.0075))
    return parts


def third_brake(d, prof, surface, m):
    """Третий стоп-сигнал над задним стеклом (у пикапа — на задней стенке кабины)."""
    g = d["greenhouse"]
    if d.get("rear_glass_axis", "y+") == "y+":
        # Над наклонным задним стеклом — проекция сверху (спереди-сзади полоса растянулась бы на пологом стекле).
        z = g["roof_rear"] - 0.035
        outline = rounded_rect(0.0, z, 0.26, 0.03, 0.01, 20)
        return [patch("BrakeLight3", surface, outline, 'y+', m["tail"], lift=0.006, rings=1)]
    else:
        rg = d.get("rear_glass")
        y = (max(p[1] for p in rg) + 0.035) if rg else prof.top(d["tail"]) - 0.05
        ref = d.get("rear_glass_ref")
    outline = rounded_rect(0.0, y, 0.26, 0.02, 0.009, 20)
    return [patch("BrakeLight3", surface, outline, 'z-', m["tail"], lift=0.006, rings=1, ref=ref)]


def reflectors(d, prof, surface, m):
    """Красные катафоты по краям заднего бампера."""
    parts = []
    t = d["tail_lights"]["outline"]
    u0, u1, v0, v1 = extent(t)
    y = prof.bottom(d["tail"] + 0.05) + 0.12
    if v0 - y < 0.1:
        return parts
    x = min(u1, prof.hw(d["tail"] + 0.1) - 0.12)
    for s in (1, -1):
        parts.append(patch("Reflector", surface, rounded_rect(s * (x - 0.05), y, 0.10, 0.03, 0.012, 16), 'z-', m["taildark"], lift=0.005, rings=1))
    return parts


def mudflaps(d, prof, m):
    parts = []
    wh = d["wheel"]
    gap = d.get("arch_gap", 0.05)
    for zc in (wh["zf"], wh["zr"]):
        z = zc - wh["r"] - gap - 0.03
        top = prof.bottom(z) + 0.04
        bottom = 0.12
        if top - bottom < 0.12:
            continue
        for s in (-1, 1):
            parts.append(box("Mudflap", (s * wh["x"], (top + bottom) / 2, z), (wh["w"] * 0.95, top - bottom, 0.014), m["trim"], bevel=0.004))
    return parts


def grille(d, surface, m, det):
    """Решётки: основа (тёмная или соты), ламели или хромовые бруски, рамка. Боковые заборники — соты."""
    parts = []
    g = d["grille"]

    def trap(q, r=0.03):
        return trapezoid(q[0][0], q[1][0], q[0][1], q[3][0], q[2][0], q[2][1], r)
    honey = (0.0, 0.0, 0.22, 0.22)   # UV в метрах / 0.22: соты ~3 см
    main = trap(g["main"])
    style = det.get("grille", "slats")
    if style == "slats":
        parts.append(patch("Grille", surface, main, 'z+', m["grille"], lift=0.004, rings=3))
        x0, x1, y0, y1 = extent(main)
        n = max(3, int((y1 - y0) / 0.034))
        for k in range(n):
            y = lerp(y0, y1, (k + 0.5) / n)
            inner = clip_poly(main, 1, y - 0.006, y + 0.006)
            if inner:
                parts.append(patch("GrilleSlat", surface, inner, 'z+', m["gloss"], lift=0.008, rings=1))
    else:
        parts.append(patch("Grille", surface, main, 'z+', m["mesh"], lift=0.004, rings=3, uv_box=honey))
    frame = m["chrome"] if det["trim"] == "chrome" else m["gloss"]
    parts.append(band("GrilleFrame", surface, main, 0.014, 'z+', frame, lift=0.009))
    if "upper" in g:
        up = trap(g["upper"], 0.015)
        parts.append(patch("GrilleUpper", surface, up, 'z+', m["mesh"], lift=0.004, rings=2, uv_box=honey))
        parts.append(band("GrilleUpperFrame", surface, up, 0.008, 'z+', m["gloss"], lift=0.0075))
    if "side" in g:
        sr = trap(g["side"], 0.02)
        for ol in (sr, [(-x, y) for (x, y) in reversed(sr)]):
            parts.append(patch("Intake", surface, ol, 'z+', m["mesh"], lift=0.004, rings=2, uv_box=honey))
            parts.append(band("IntakeFrame", surface, ol, 0.008, 'z+', m["gloss"], lift=0.0075))
    if g.get("bars"):
        x0, x1, y0, y1 = extent([p for p in g["main"]])
        for k in range(g["bars"]):
            y = lerp(y0, y1, (k + 0.5) / g["bars"])
            parts.append(patch("GrilleBar", surface, rounded_rect(0, y, x1 - x0 - 0.04, 0.022, 0.01, 16), 'z+', m["chrome"], lift=0.012, rings=1))
    for outline in d.get("side_intakes", []):
        sm = rounded_poly(outline, 0.04)
        for axis in ('x+', 'x-'):
            parts.append(patch("SideIntake", surface, sm, axis, m["mesh"], lift=0.004, rings=2, uv_box=honey))
            parts.append(band("SideIntakeFrame", surface, sm, 0.01, axis, m["gloss"], lift=0.006))
    return parts


def body_details(d, prof, surface, m):
    """Все детали v3 на кузове (склеиваются с кузовом в один объект Body)."""
    det = details_of(d)
    parts = []
    parts += seams(d, prof, surface, m)
    parts += window_trim(d, prof, surface, m, det)
    parts += side_lines(d, prof, surface, m, det)
    parts += wipers(d, prof, surface, m)
    parts += antenna(d, prof, surface, m, det.get("antenna"))
    parts += signals(d, prof, surface, m, det)
    if det.get("third_brake"):
        parts += third_brake(d, prof, surface, m)
    parts += reflectors(d, prof, surface, m)
    if det.get("mudflaps"):
        parts += mudflaps(d, prof, m)
    return [p for p in parts if p is not None]


# ---------- салон: каркас и пилот ----------

def cage(d, prof, m):
    """Каркас безопасности: дуга за сиденьями, распорки к стойкам, диагональ (виден сквозь стёкла)."""
    it = d["interior"]
    g = d["greenhouse"]
    floor = it["floor"]
    zb = it["seat_z"] - 0.42
    za = g["cowl"] - 0.25
    r = 0.022
    parts = []

    def roof_y(z):
        return prof.top(z) - 0.11

    def side_x(z, y):
        return min(prof.top_hw(z), prof.hw(z)) * 0.80

    yb = roof_y(zb)
    xb = side_x(zb, yb)
    ya = roof_y(za)
    xa = side_x(za, ya)
    for s in (-1, 1):
        parts.append(tube("Cage", (s * xb, floor + 0.05, zb), (s * xb, yb, zb), r, m["interior"]))
        parts.append(tube("Cage", (s * xb, yb, zb), (s * xa, ya, za), r, m["interior"]))
        parts.append(tube("Cage", (s * xa, ya, za), (s * xa * 1.02, floor + 0.15, za + 0.25), r, m["interior"]))
    parts.append(tube("Cage", (-xb, yb, zb), (xb, yb, zb), r, m["interior"]))
    parts.append(tube("Cage", (-xb, floor + 0.12, zb), (xb, yb - 0.04, zb), r, m["interior"]))
    return parts


def driver(d, prof, m):
    """Пилот: комбинезон, руки на руле, шлем цвета крыши с тёмным визором. Камера из салона его прячет."""
    it = d["interior"]
    st = d["steering"]
    floor = it["floor"]
    x = -it["seat_x"]
    zs = it["seat_z"]
    R = 0.13
    zh = zs - 0.17
    # Голова под крышей: у низких суперкаров пилот «сползает» в сиденье.
    cy = min(floor + 0.93, prof.top(zh) - 0.075 - R)
    shoulder = cy - R - 0.07
    parts = []
    torso = rounded_box("Driver", (x, (floor + 0.24 + shoulder) / 2, zs - 0.13), (0.40, shoulder - floor - 0.24, 0.22), m["white"], 0.07)
    _tilt(torso, (x, floor + 0.24, zs - 0.22), -10)
    parts.append(torso)
    parts.append(cylinder("DriverNeck", (x, shoulder + 0.03, zh + 0.01), 0.055, 0.08, 'y', m["white"], segments=12))
    # Руки: плечо → локоть → кисть на руле (на «без четверти три»).
    sx, sy, sz = st["pos"]
    for s in (-1, 1):
        sh = (x + s * 0.17, shoulder - 0.03, zs - 0.10)
        hand = (sx + s * 0.16, sy - 0.02, sz - 0.04)
        elbow = ((sh[0] + hand[0]) / 2 + s * 0.06, min(sh[1], hand[1]) - 0.08, (sh[2] + hand[2]) / 2)
        parts.append(beam("DriverArm", sh, elbow, 0.085, 0.085, m["white"], bevel=0.03))
        parts.append(beam("DriverArm", elbow, hand, 0.075, 0.075, m["white"], bevel=0.028))
        bm = bmesh.new()
        bmesh.ops.create_uvsphere(bm, u_segments=10, v_segments=6, radius=0.045)
        bmesh.ops.translate(bm, vec=C(*hand), verts=bm.verts)
        glove = obj_from_bmesh("DriverGlove", bm, [m["accent"]])
        smooth(glove, 80)
        parts.append(glove)
    # Шлем: сфера, визор (тёмный глянец, выпуклый) и полоса.
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=20, v_segments=12, radius=R)
    bmesh.ops.scale(bm, vec=Vector((1.0, 1.12, 1.0)), verts=bm.verts)     # чуть вытянут вперёд-назад
    bmesh.ops.translate(bm, vec=C(x, cy, zh), verts=bm.verts)
    helmet = obj_from_bmesh("DriverHelmet", bm, [m["roof"]])
    smooth(helmet, 80)
    parts.append(helmet)
    visor = pod("DriverVisor", (x, cy + 0.005, zh + R * 1.12 - 0.02), 0.07, R * 1.55, R * 0.62, m["gloss"], nose=0.9, segments=18, rings=6)
    parts.append(visor)
    # Полоса по центру шлема спереди назад (кольцо вокруг поперечной оси, вытянуто как шлем).
    stripe = revolve("DriverStripe", [(-0.014, R * 1.004), (0.014, R * 1.004), (0.014, R * 1.022), (-0.014, R * 1.022)], 28, m["black"])
    stripe.data.transform(Matrix.Diagonal((1.0, 1.12, 1.0, 1.0)))
    stripe.data.transform(Matrix.Translation(C(x, cy, zh)))
    parts.append(stripe)
    ob = join(parts[0], parts[1:])
    ob.name = "Driver"
    smooth(ob, 50)
    return ob


# ---------- кузов пикапа ----------

def bed_tubs(d, prof, body, m):
    """Короба задних колёс в кузове пикапа: пол кузова ниже верха колёс, без коробов шина торчала бы сквозь пол.
    Грани пола над колесом удаляются (короб закрыт сам), короб — тёмный, как подкрылок."""
    bed = d.get("bed")
    if not bed:
        return []
    wh = d["wheel"]
    gap = d.get("arch_gap", 0.05)
    R = wh["r"] + gap
    zr = wh["zr"]
    if not (bed["z0"] < zr < bed["z1"]):
        return []
    floor = bed["floor"]
    top = wh["r"] + R + 0.04
    if top <= floor + 0.02:
        return []
    x_out = prof.hw(zr) - bed["inset"] + 0.01
    # Внутренняя стенка короба — чуть дальше торца подкрылка (как в arch_liners), иначе его край торчит «плавником».
    x_liner = max(wh["x"] - 0.45 + 0.004, wh["x"] - wh["w"] * 0.5 - 0.10)
    x_in = x_liner - 0.025
    half_z = R + 0.05
    # Пол кузова после булевой операции — крупные треугольники на всю ширину: режем их плоскостями по контуру
    # коробов и удаляем куски внутри (иначе колесо «упирается» в пол, которого не видно).
    bm = bmesh.new()
    bm.from_mesh(body.data)

    def floor_faces():
        out = []
        for f in bm.faces:
            c = f.calc_center_median()
            if f.normal.z > 0.9 and abs(c.z - floor) < 0.03:
                out.append(f)
        return out
    planes = []
    for sx in (-1, 1):
        for xx in (x_in + 0.045, x_out):
            planes.append((C(sx * xx, floor, zr), Vector((1.0, 0.0, 0.0))))
    for zz in (zr - half_z + 0.045, zr + half_z - 0.045):
        planes.append((C(0.0, floor, zz), Vector((0.0, 1.0, 0.0))))
    for co, no in planes:
        faces = floor_faces()
        edges = list({e for f in faces for e in f.edges})
        verts = list({v for f in faces for v in f.verts})
        bmesh.ops.bisect_plane(bm, geom=faces + edges + verts, plane_co=co, plane_no=no)
    kill = []
    for f in floor_faces():
        c = f.calc_center_median()
        x, z = -c.x, -c.y
        # Чуть меньше короба (у него скруглённые углы): пол под углами остаётся, щелей нет.
        if x_in + 0.045 < abs(x) < x_out + 0.005 and abs(z - zr) < half_z - 0.045:
            kill.append(f)
    if kill:
        bmesh.ops.delete(bm, geom=kill, context='FACES')
    bm.to_mesh(body.data)
    bm.free()
    parts = []
    for s in (-1, 1):
        xc = s * (x_in + x_out) * 0.5
        tub = box("BedTub", (xc, (floor - 0.01 + top) * 0.5, zr), (x_out - x_in, top - floor + 0.01, 2 * half_z), m["liner"], bevel=0.04, segments=3)
        apply_modifiers(tub)
        # Дно короба не нужно (под полом его не видно), а без него колесо свободно ходит внутри короба.
        tb = bmesh.new()
        tb.from_mesh(tub.data)
        bottom = [f for f in tb.faces if f.normal.z < -0.5 and f.calc_center_median().z < floor + 0.03]
        bmesh.ops.delete(tb, geom=bottom, context='FACES')
        tb.to_mesh(tub.data)
        tb.free()
        parts.append(tub)
    return parts
