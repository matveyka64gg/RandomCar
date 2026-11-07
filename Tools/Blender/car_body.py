# Генератор машин v2: кузов. Запуск вместе с rc_lib.py, car_parts.py, car_designs.py, car_build.py.
#
# Кузов — лофт сечений вдоль машины. Сечение (правая половина) задаётся 8 опорными точками:
#   c0 низ по центру, c1 край днища, c2 порог, c3 самая широкая точка двери, c4 линия плеча (пояс),
#   c5 верх боковины (у купе — верх бокового стекла), c6 скругление крыши/капота, c7 центр верха.
# Опоры зависят от продольных профилей дизайна (низ, пояс, верх по центру, полуширина, «теплица»),
# заданных ключевыми точками и сглаженных монотонным кубическим сплайном (без выбросов).
# Каждая точка кольца знает, на каком отрезке сечения лежит, — по этому грани делятся на краску,
# стёкла с чёрной рамкой, крышу, капот и низ (для двухцветной окраски).

from mathutils.bvhtree import BVHTree

SEG = [2, 3, 4, 3, 2, 9, 4, 5]       # точек на отрезках c0→c1 ... c7→c8 (правая половина)
HALF = sum(SEG)
RING = 2 * HALF


# ---------- сглаженные профили ----------

def pchip(keys):
    """Монотонный кубический сплайн Эрмита (Фрич–Карлсон) по ключам (x, y), x возрастает."""
    xs = [k[0] for k in keys]
    ys = [k[1] for k in keys]
    n = len(keys)
    if n == 1:
        return lambda x: ys[0]
    h = [xs[i + 1] - xs[i] for i in range(n - 1)]
    d = [(ys[i + 1] - ys[i]) / h[i] for i in range(n - 1)]
    m = [0.0] * n
    m[0], m[-1] = d[0], d[-1]
    for i in range(1, n - 1):
        if d[i - 1] * d[i] <= 0:
            m[i] = 0.0
        else:
            w1, w2 = 2 * h[i] + h[i - 1], h[i] + 2 * h[i - 1]
            m[i] = (w1 + w2) / (w1 / d[i - 1] + w2 / d[i])

    def f(x):
        if x <= xs[0]:
            return ys[0]
        if x >= xs[-1]:
            return ys[-1]
        i = 0
        while xs[i + 1] < x:
            i += 1
        t = (x - xs[i]) / h[i]
        t2, t3 = t * t, t * t * t
        return ((2 * t3 - 3 * t2 + 1) * ys[i] + (t3 - 2 * t2 + t) * h[i] * m[i]
                + (-2 * t3 + 3 * t2) * ys[i + 1] + (t3 - t2) * h[i] * m[i + 1])
    return f


def lerp(a, b, t):
    return a + (b - a) * t


def smoothstep(a, b, x):
    if b == a:
        return 0.0 if x < a else 1.0
    t = max(0.0, min(1.0, (x - a) / (b - a)))
    return t * t * (3 - 2 * t)


class Profiles:
    """Продольные профили дизайна: функции от z."""

    def __init__(self, d):
        self.d = d
        p = d["profile"]
        self.bottom = pchip(p["bottom"])
        self.belt = pchip(p["belt"])
        self.top = pchip(p["top"])
        self.hw = pchip(p["width"])
        g = d["greenhouse"]
        self.g = g
        s = d.get("section", {})
        self.tuck = s.get("tuck", 0.84)          # ширина днища от полной
        self.bulge = s.get("bulge", 0.42)        # высота самой широкой точки (доля от низа до пояса)
        self.shoulder = s.get("shoulder", 0.975) # ширина по поясу от полной
        self.tumble = s.get("tumble", 0.0)       # завал боковин «теплицы» внутрь (доп.)
        self.roof_round = s.get("roof_round", 0.30)
        self.hood_round = s.get("hood_round", 0.22)
        self.crown = s.get("crown", 0.03)        # выпуклость крыши/капота по центру
        # Рубленый кузов (внедорожники): прямые грани и скругления заданного радиуса вместо сплайна.
        self.box = s.get("box", False)
        self.r_bottom = s.get("r_bottom", 0.06)
        self.r_shoulder = s.get("r_shoulder", 0.03)
        self.r_roof = s.get("r_roof", 0.06)

    def glasshouse(self, z):
        """0 — капот/багажник, 1 — кабина (между основанием лобового и заднего стёкол)."""
        g = self.g
        return smoothstep(g["deck"], g["roof_rear"], z) * (1 - smoothstep(g["roof_front"], g["cowl"], z))

    def top_hw(self, z):
        g = self.glasshouse(z)
        return lerp(self.d["hood_hw"] * self.hw(z), self.d["roof_hw"], g)

    def box_controls(self, z):
        """Опоры рубленого сечения и углы скруглений (для отрезков 1, 4 и 6)."""
        w = self.hw(z)
        yb = self.bottom(z)
        ysh = self.belt(z)
        yt = self.top(z)
        g = self.glasshouse(z)
        wt = self.top_hw(z)
        ws = w * self.shoulder
        rb = min(self.r_bottom, (ysh - yb) * 0.3)
        rs = min(self.r_shoulder, max(0.005, (yt - ysh) * 0.3))
        rr = min(self.r_roof, max(0.01, (yt - ysh) * 0.4), wt * 0.3)
        y3 = yb + self.bulge * (ysh - yb)
        x5 = wt - self.tumble * g * 0.04
        c = [
            (0.0, yb),
            (w - rb, yb),
            (w, yb + rb),
            (w, y3),
            (w, ysh - rs),
            (ws - (w - ws) * 0.2, ysh + rs * 0.6),
            (x5, yt - rr),
            (x5 - rr, yt),
            (0.0, yt + self.crown * 0.3),
        ]
        corners = {1: (w, yb), 4: (w, ysh), 6: (x5, yt)}
        return c, corners

    def controls(self, z):
        w = self.hw(z)
        yb = self.bottom(z)
        ysh = self.belt(z)
        yt = self.top(z)
        g = self.glasshouse(z)
        wt = self.top_hw(z)
        ws = w * self.shoulder
        H = max(0.02, yt - ysh)
        rr = lerp(self.hood_round, self.roof_round, g)
        # c5: верх боковины. У кабины — верх стекла (с завалом внутрь), у капота — кромка крыла.
        x5 = lerp(ws, wt, 0.88 + 0.12 * (1 - g)) - self.tumble * g * 0.05
        y5 = yt - H * lerp(0.35, 0.16, g)
        # Под линией плеча — почти вертикальный участок: даёт чёткую «рёберную» линию на боку.
        y3 = yb + self.bulge * (ysh - yb)
        c = [
            (0.0, yb),
            (w * self.tuck, yb),
            (w * 0.975, yb + 0.06),
            (w, y3),
            (w * 0.998, ysh - min(0.02, (ysh - y3) * 0.15)),
            (ws, ysh),
            (x5, y5),
            (wt * (1 - rr), yt - self.crown * 0.35),
            (0.0, yt),
        ]
        return c


def _catmull(p0, p1, p2, p3, t, alpha=0.5):
    """Центростремительный Catmull-Rom (без петель на острых углах)."""
    def tj(ti, a, b):
        return ti + max(1e-6, ((b[0] - a[0]) ** 2 + (b[1] - a[1]) ** 2) ** (alpha * 0.5))
    t0 = 0.0
    t1 = tj(t0, p0, p1)
    t2 = tj(t1, p1, p2)
    t3 = tj(t2, p2, p3)
    u = t1 + (t2 - t1) * t

    def L(a, b, ta, tb):
        k = (u - ta) / (tb - ta)
        return (a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k)
    A1 = L(p0, p1, t0, t1)
    A2 = L(p1, p2, t1, t2)
    A3 = L(p2, p3, t2, t3)
    B1 = L(A1, A2, t0, t2)
    B2 = L(A2, A3, t1, t3)
    return L(B1, B2, t1, t2)


def ring_points(prof, z, shrink=None):
    """Кольцо сечения: список (x, y, seg, t). Порядок: низ по центру → правая сторона вверх → верх → левая вниз."""
    if prof.box:
        return _box_ring(prof, z, shrink)
    c = prof.controls(z)
    loop = c + [(-p[0], p[1]) for p in reversed(c[1:-1])]   # 16 точек по кругу
    n = len(loop)
    half = len(c) - 1
    out = []
    # Правая половина: отрезки 0..7, левая — зеркально (отрезки 8..15 соответствуют 7..0).
    for k in range(n):
        seg = k if k < half else 2 * half - 1 - k
        count = SEG[seg]
        p0, p1, p2, p3 = loop[(k - 1) % n], loop[k], loop[(k + 1) % n], loop[(k + 2) % n]
        for s in range(count):
            t = s / count
            x, y = _catmull(p0, p1, p2, p3, t)
            tt = t if k < half else 1 - t
            out.append([x, y, seg + tt, k >= half])
    if shrink is not None:
        sx, sy, ymid = shrink
        for p in out:
            p[0] *= sx
            # Низ (бампер) остаётся прямым, скругляется только верхняя кромка.
            if p[1] > ymid:
                p[1] = ymid + (p[1] - ymid) * sy
    return out


def _box_ring(prof, z, shrink):
    c, corners = prof.box_controls(z)
    loop = c + [(-p[0], p[1]) for p in reversed(c[1:-1])]
    n = len(loop)
    half = len(c) - 1
    out = []
    for k in range(n):
        seg = k if k < half else 2 * half - 1 - k
        a, b = loop[k], loop[(k + 1) % n]
        corner = corners.get(seg)
        if corner is not None and k >= half:
            corner = (-corner[0], corner[1])
        for s in range(SEG[seg]):
            t = s / SEG[seg]
            if corner is None:
                x, y = a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t
            else:
                x = (1 - t) ** 2 * a[0] + 2 * (1 - t) * t * corner[0] + t * t * b[0]
                y = (1 - t) ** 2 * a[1] + 2 * (1 - t) * t * corner[1] + t * t * b[1]
            tt = t if k < half else 1 - t
            out.append([x, y, seg + tt, k >= half])
    if shrink is not None:
        sx, sy, ymid = shrink
        for p in out:
            p[0] *= sx
            if p[1] > ymid:
                p[1] = ymid + (p[1] - ymid) * sy
    return out


def rounded_poly(points, r=0.04, per=4):
    """Многоугольник со скруглёнными углами радиуса ≈ r (окна, фонари, решётки рубленых машин)."""
    out = []
    n = len(points)
    for i in range(n):
        p0, p1, p2 = points[i - 1], points[i], points[(i + 1) % n]
        a = (p0[0] - p1[0], p0[1] - p1[1])
        b = (p2[0] - p1[0], p2[1] - p1[1])
        la, lb = math.hypot(*a) or 1, math.hypot(*b) or 1
        rr = min(r, la * 0.45, lb * 0.45)
        s0 = (p1[0] + a[0] / la * rr, p1[1] + a[1] / la * rr)
        s1 = (p1[0] + b[0] / lb * rr, p1[1] + b[1] / lb * rr)
        for k in range(per + 1):
            t = k / per
            out.append(((1 - t) ** 2 * s0[0] + 2 * (1 - t) * t * p1[0] + t * t * s1[0],
                        (1 - t) ** 2 * s0[1] + 2 * (1 - t) * t * p1[1] + t * t * s1[1]))
        # Промежуточные точки на прямой до следующего угла.
        p3 = points[(i + 2) % n]
        c = (p3[0] - p2[0], p3[1] - p2[1])
        lc = math.hypot(*c) or 1
        r2 = min(r, lb * 0.45, lc * 0.45)
        q1 = (p2[0] - b[0] / lb * r2, p2[1] - b[1] / lb * r2)
        steps = max(1, int(math.hypot(q1[0] - s1[0], q1[1] - s1[1]) / 0.04))
        for k in range(1, steps):
            t = k / steps
            out.append((s1[0] + (q1[0] - s1[0]) * t, s1[1] + (q1[1] - s1[1]) * t))
    return out


def z_stations(d):
    """Сечения вдоль машины с шагом ~3.5 см (мельче — ровнее границы стёкол и накладок)."""
    R = d.get("end_round", 0.16)
    z0, z1 = d["tail"] + R, d["nose"] - R
    n = max(8, int((z1 - z0) / 0.035))
    return [lerp(z0, z1, k / n) for k in range(n + 1)]


def end_shrink(prof, z, a, d):
    """Скругление конца: по ширине — четверть окружности, по высоте — только верхняя кромка."""
    k = math.cos(a)
    sx = d.get("end_x", 0.80) + (1 - d.get("end_x", 0.80)) * k ** 0.6
    sy = d.get("end_y", 0.55) + (1 - d.get("end_y", 0.55)) * k ** 0.6
    ymid = prof.bottom(z) + (prof.belt(z) - prof.bottom(z)) * 0.55
    return (sx, sy, ymid)


def body_loft(d, prof):
    """Оболочка кузова: кольца сечений + скругления концов + плоские торцы (кольцами к центру)."""
    R = d.get("end_round", 0.16)
    tail, nose = d["tail"], d["nose"]
    stations = []
    steps = 8
    for k in range(steps, 0, -1):
        a = k / steps * math.pi / 2
        stations.append((tail + R - R * math.sin(a), tail + R, a))
    for z in z_stations(d):
        stations.append((z, z, None))
    for k in range(1, steps + 1):
        a = k / steps * math.pi / 2
        stations.append((nose - R + R * math.sin(a), nose - R, a))

    bm = bmesh.new()
    grid = []
    attrs = []
    for z, zp, a in stations:
        shrink = end_shrink(prof, zp, a, d) if a is not None else None
        pts = ring_points(prof, zp, shrink)
        grid.append([bm.verts.new(C(x, y, z)) for x, y, seg, left in pts])
        attrs.append([(p[2], z) for p in pts])
    return bm, grid, attrs, stations


# ---------- классификация граней ----------

def point_in_poly(z, y, poly):
    inside = False
    n = len(poly)
    j = n - 1
    for i in range(n):
        zi, yi = poly[i]
        zj, yj = poly[j]
        if (yi > y) != (yj > y) and z < (zj - zi) * (y - yi) / (yj - yi + 1e-12) + zi:
            inside = not inside
        j = i
    return inside


def dist_to_poly(z, y, poly):
    best = 1e9
    n = len(poly)
    for i in range(n):
        az, ay = poly[i]
        bz, by = poly[(i + 1) % n]
        dz, dy = bz - az, by - ay
        L = dz * dz + dy * dy
        t = max(0.0, min(1.0, ((z - az) * dz + (y - ay) * dy) / L)) if L > 0 else 0.0
        px, py = az + dz * t, ay + dy * t
        best = min(best, math.hypot(z - px, y - py))
    return best


def inside_by(z, y, poly, margin):
    """Точка внутри контура и не ближе margin к краю."""
    return point_in_poly(z, y, poly) and dist_to_poly(z, y, poly) >= margin


MAT_ORDER = ["paint", "roof", "hood", "lower", "glass", "frame", "trim"]


def glass_areas(d, prof):
    """Все стёкла машины как контуры в плоскости вида: боковые (x±, координаты z,y), лобовое и заднее
    (сверху: x,z; у вертикального заднего стекла — сзади: x,y). Края контуров сглажены."""
    g = d["greenhouse"]
    areas = []
    for poly in d["side_windows"]:
        sm = rounded_poly(poly, d.get("window_round", 0.05))
        for side, axis in ((1, 'x+'), (-1, 'x-')):
            areas.append(dict(axis=axis, outline=sm, side=side, key=lambda v: (v[2], v[1]), trim=d.get("window_trim", 0.03)))

    def pillar_outline(z0, z1, inset, corner):
        lo, hi = min(z0, z1), max(z0, z1)
        steps = 10
        right = [(prof.top_hw(lerp(lo, hi, k / steps)) - inset, lerp(lo, hi, k / steps)) for k in range(steps + 1)]
        right = [(x, z) for (x, z) in right]
        left = [(-x, z) for (x, z) in reversed(right)]
        pts = right + left
        return closed_curve(pts[::2], 4)

    ws = pillar_outline(g["cowl"] - 0.04, g["roof_front"] + 0.01, d.get("a_pillar", 0.10), 0.05)
    areas.append(dict(axis='y+', outline=ws, side=0, key=lambda v: (v[0], v[2]), trim=0.012))
    if d.get("rear_glass_axis", "y+") == "y+":
        rg = pillar_outline(g["deck"] + 0.01, g["roof_rear"] - 0.01, d.get("c_pillar", 0.12), 0.05)
        areas.append(dict(axis='y+', outline=rg, side=0, key=lambda v: (v[0], v[2]), trim=0.012))
    else:
        rg = rounded_poly(d["rear_glass"], d.get("window_round", 0.05))
        areas.append(dict(axis='z-', outline=rg, side=0, key=lambda v: (v[0], v[1]), trim=0.02))
    for a in areas:
        a["outer"] = poly_offset_true(a["outline"], -a["trim"])
    return areas


def _faces_view(axis, side, n):
    """Грань смотрит в сторону вида (нормаль n в координатах машины)."""
    if axis == 'x+':
        return n[0] > 0.3
    if axis == 'x-':
        return n[0] < -0.3
    if axis == 'y+':
        return n[1] > 0.2
    if axis == 'z-':
        return n[2] < -0.3
    return n[2] > 0.3


def glass_class(d, verts_car, normal):
    """Проём (грань удаляется) — если грань задевает прозрачную часть стекла (глубже полосы фритты);
    чёрная рамка — если грань целиком внутри контура. Так все ступеньки сетки оказываются под фриттой."""
    frit = d.get("frit", 0.06)
    for a in d["glass_areas_list"]:
        if not _faces_view(a["axis"], a["side"], normal):
            continue
        pts = [a["key"](v) for v in verts_car]
        # Всё, что целиком внутри контура с окантовкой, закрыто полосой окантовки и фритты.
        if not all(point_in_poly(u, v, a["outer"]) for (u, v) in pts):
            continue
        if any(inside_by(u, v, a["outline"], frit * 0.95) for (u, v) in pts):
            return "glass"
        return "frame"
    return None


def classify(d, prof, seg, verts_car, normal):
    """Материал грани: стекло (вырезается), чёрная рамка под фриттой, крыша, капот, низ, краска."""
    g = d["greenhouse"]
    x = sum(v[0] for v in verts_car) / 4
    y = sum(v[1] for v in verts_car) / 4
    z = sum(v[2] for v in verts_car) / 4
    gh = prof.glasshouse(z)
    if seg >= 4.6:
        c = glass_class(d, verts_car, normal)
        if c:
            return c
    if seg >= 6.0:
        if gh > 0.85 and y > prof.top(z) - 0.12:
            return "roof"
        if z > g["cowl"] and d.get("hood_end", 99) > z and y > prof.belt(z) - 0.02:
            return "hood"
    if seg < 2.6:
        return "lower"
    return "paint"


def build_shell(d, prof, m):
    d["glass_areas_list"] = glass_areas(d, prof)
    bm, grid, attrs, stations = body_loft(d, prof)
    mats = [m[k] for k in MAT_ORDER]
    n = RING
    glass_faces = []
    for i in range(len(grid) - 1):
        zc = stations[i][0]
        for j in range(n):
            k = (j + 1) % n
            quad = (grid[i][j], grid[i][k], grid[i + 1][k], grid[i + 1][j])
            f = bm.faces.new(quad)
            seg = (attrs[i][j][0] + attrs[i][k][0] + attrs[i + 1][k][0] + attrs[i + 1][j][0]) / 4
            if abs(attrs[i][j][0] - attrs[i][k][0]) > 3:
                seg = attrs[i][j][0]
            # Из Blender обратно в координаты машины: x = -X, y = Z, z = -Y.
            vc = [(-v.co.x, v.co.z, -v.co.y) for v in quad]
            a = Vector(vc[1]) - Vector(vc[0])
            b = Vector(vc[3]) - Vector(vc[0])
            nrm = a.cross(b)
            if nrm.length > 0:
                nrm.normalize()
            ctr = sum((Vector(v) for v in vc), Vector()) / 4
            axis_pt = Vector((0.0, (prof.bottom(ctr.z) + prof.top(ctr.z)) * 0.5, ctr.z))
            if nrm.dot(ctr - axis_pt) < 0:
                nrm = -nrm
            ends = stations[i][2] is not None or stations[i + 1][2] is not None
            name = classify(d, prof, seg, vc, nrm) if not ends else ("lower" if seg < 2.6 else "paint")
            f.material_index = MAT_ORDER.index(name)
            if name == "glass":
                glass_faces.append(f)
    # Торцы: плоские диски из колец к центру (ровная заливка без длинных треугольников).
    for row in (grid[0], grid[-1]):
        cx = sum(v.co.x for v in row) / len(row)
        cy = sum(v.co.y for v in row) / len(row)
        cz = sum(v.co.z for v in row) / len(row)
        centre = Vector((cx, cy, cz))
        prev = row
        for sc in (0.66, 0.33):
            cur = [bm.verts.new(centre + (v.co - centre) * sc) for v in row]
            for j in range(len(row)):
                k = (j + 1) % len(row)
                f = bm.faces.new((prev[j], prev[k], cur[k], cur[j]))
                f.material_index = MAT_ORDER.index("lower")
            prev = cur
        c = bm.verts.new(centre)
        for j in range(len(row)):
            k = (j + 1) % len(row)
            f = bm.faces.new((prev[j], prev[k], c))
            f.material_index = MAT_ORDER.index("lower")
    # Целая оболочка — для лучей накладок. Проёмы под стёклами вырезаются после арок
    # (булевы операции требуют замкнутой оболочки).
    full = obj_from_bmesh("BodySurface", bm.copy(), mats)
    ob = obj_from_bmesh("Body", bm, mats)
    smooth(ob, d.get("crease", 38))
    return ob, full


def open_glass(body):
    """Удаляет грани со слотом стекла: в оболочке появляются проёмы под накладками стёкол."""
    idx = MAT_ORDER.index("glass")
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.delete(bm, geom=[f for f in bm.faces if f.material_index == idx], context='FACES')
    bm.to_mesh(body.data)
    bm.free()


# ---------- арки ----------

def cut_arches(d, body, m):
    """Вырезы арок только по бортам (пол между колёсами не прорезается)."""
    wh = d["wheel"]
    cutters = []
    for z in (wh["zf"], wh["zr"]):
        r = wh["r"] + d.get("arch_gap", 0.05)
        for s in (-1, 1):
            xc = s * (wh["x"] + 0.45)
            cutters.append(cylinder("ArchCut", (xc, wh["r"], z), r, 0.9 + 2 * 0.05, 'x', m["liner"], segments=64))
            cutters.append(box("ArchCutLow", (xc, wh["r"] * 0.5 - 0.2, z), (1.0, wh["r"] + 0.4, 2 * r), m["liner"]))
    boolean_difference(body, cutters)


def arch_liners(d, prof, m):
    """Подкрылки: тёмная полутруба внутри арки — сквозь вырез не видно пустоту кузова."""
    wh = d["wheel"]
    parts = []
    for zc in (wh["zf"], wh["zr"]):
        R = wh["r"] + d.get("arch_gap", 0.05) + 0.003
        for s in (-1, 1):
            x_in = wh["x"] - 0.45 + 0.004 if wh["x"] - 0.45 > 0 else 0.05
            x_in = max(x_in, wh["x"] - wh["w"] * 0.5 - 0.10)
            x_out = prof.hw(zc) + 0.02
            bm = bmesh.new()
            rows = []
            steps = 32
            for k in range(steps + 1):
                a = math.radians(-14 + 208 * k / steps)
                yy = wh["r"] + R * math.sin(a)
                zz = zc + R * math.cos(a)
                rows.append([bm.verts.new(C(s * x_in, yy, zz)), bm.verts.new(C(s * x_out, yy, zz))])
            for k in range(steps):
                bm.faces.new((rows[k][0], rows[k][1], rows[k + 1][1], rows[k + 1][0]))
            # Торцевая стенка со стороны мотора: полукруг на внутреннем краю.
            cap = [r[0] for r in rows]
            centre_v = bm.verts.new(C(s * x_in, wh["r"], zc))
            for k in range(steps):
                bm.faces.new((cap[k], cap[k + 1], centre_v))
            ob = obj_from_bmesh("Liner", bm, [m["liner"]], recalc=False)
            me = ob.data
            centre = C(s * (x_in + x_out) / 2, wh["r"], zc)
            for poly in me.polygons:
                if poly.normal.dot(centre - poly.center) < 0:
                    poly.flip()
            parts.append(ob)
    return parts


# ---------- поверхность и накладки ----------

class Surface:
    """Поиск точек на кузове лучами (BVH): для фар, решёток, стёкол, обвеса."""

    def __init__(self, ob):
        dg = bpy.context.evaluated_depsgraph_get()
        self.tree = BVHTree.FromObject(ob, dg)

    def hit(self, origin_car, direction_car, max_dist=6.0):
        o = C(*origin_car)
        dvec = C(*direction_car)
        loc, normal, idx, dist = self.tree.ray_cast(o, dvec.normalized(), max_dist)
        if loc is None:
            return None, None
        return Vector((-loc.x, loc.z, -loc.y)), Vector((-normal.x, normal.z, -normal.y))


AXES = {
    'z+': (0.0, 0.0, -1.0),
    'z-': (0.0, 0.0, 1.0),
    'x+': (-1.0, 0.0, 0.0),
    'x-': (1.0, 0.0, 0.0),
    'y+': (0.0, -1.0, 0.0),
}


def ray_origin(axis, u, v, ref):
    """Точка контура (u, v) в опорной плоскости вида. ref — положение плоскости (z носа/кормы, x борта, y крыши)."""
    if axis == 'z+':
        return Vector((u, v, 4.0 if ref is None else ref))
    if axis == 'z-':
        return Vector((u, v, -4.0 if ref is None else ref))
    if axis == 'x+':
        return Vector((4.0 if ref is None else ref, v, u))
    if axis == 'x-':
        return Vector((-4.0 if ref is None else -ref, v, u))
    return Vector((u, 4.0 if ref is None else ref, v))


def _ray_dir(axis, slant):
    d = Vector(AXES[axis]) + Vector(slant)
    if axis == 'x-':
        d = Vector(AXES[axis]) + Vector((-slant[0], slant[1], slant[2]))
    d.normalize()
    return d


def _orient(ob, dvec):
    me = ob.data
    if not len(me.polygons):
        return
    view = -C(*dvec)
    avg = Vector()
    for p in me.polygons:
        avg += p.normal
    if avg.dot(view) < 0:
        for p in me.polygons:
            p.flip()


def patch(name, surface, outline, axis, material, lift=0.004, rings=4, slant=(0.0, 0.0, 0.0), ref=None, uv_box=None):
    """Накладка по форме кузова: контур outline (u, v) в опорной плоскости вида axis (на глубине ref)
    проецируется на кузов лучом (наклон slant — для фар на углах и стёкол с завалом).
    Кольца от края к центру дают ровную сетку. uv_box=(u0, v0, u1, v1) — UV из координат контура."""
    dvec = _ray_dir(axis, slant)
    cu = sum(p[0] for p in outline) / len(outline)
    cv = sum(p[1] for p in outline) / len(outline)
    bm = bmesh.new()
    uvl = bm.loops.layers.uv.new("UVMap") if uv_box else None
    vuv = {}

    def cast(u, v):
        origin = ray_origin(axis, u, v, ref) - dvec * 2.5
        p, n = surface.hit(tuple(origin), tuple(dvec), 12.0)
        if p is None:
            return None
        vert = bm.verts.new(C(*(p + n * lift)))
        if uv_box:
            vuv[vert] = ((u - uv_box[0]) / (uv_box[2] - uv_box[0]), (v - uv_box[1]) / (uv_box[3] - uv_box[1]))
        return vert

    layers = []
    for r in range(rings + 1):
        s = 1.0 - r / rings
        layers.append([cast(cu + (u - cu) * s, cv + (v - cv) * s) for (u, v) in outline])
    centre = cast(cu, cv)
    n = len(outline)
    for r in range(rings):
        for i in range(n):
            j = (i + 1) % n
            q = (layers[r][i], layers[r][j], layers[r + 1][j], layers[r + 1][i])
            if None in q or len(set(q)) < 4:
                continue
            bm.faces.new(q)
    if centre is not None:
        last = layers[-1]
        for i in range(n):
            j = (i + 1) % n
            if last[i] is not None and last[j] is not None and last[i] != last[j]:
                bm.faces.new((last[i], last[j], centre))
    if uvl is not None:
        for f in bm.faces:
            for loop in f.loops:
                loop[uvl].uv = vuv.get(loop.vert, (0.5, 0.5))
    ob = obj_from_bmesh(name, bm, [material])
    _orient(ob, dvec)
    smooth(ob, 60)
    return ob


def band(name, surface, outline, width, axis, material, lift=0.004, slant=(0.0, 0.0, 0.0), ref=None):
    """Полоса вдоль контура внутрь на ширину width (рамка стекла, ободок фары)."""
    inner = poly_offset_true(outline, width)
    dvec = _ray_dir(axis, slant)
    bm = bmesh.new()
    rows = []
    for ring in (outline, inner):
        row = []
        for (u, v) in ring:
            origin = ray_origin(axis, u, v, ref) - dvec * 2.5
            p, n = surface.hit(tuple(origin), tuple(dvec), 12.0)
            row.append(bm.verts.new(C(*(p + n * lift))) if p is not None else None)
        rows.append(row)
    n = len(outline)
    for i in range(n):
        j = (i + 1) % n
        q = (rows[0][i], rows[0][j], rows[1][j], rows[1][i])
        if None in q:
            continue
        bm.faces.new(q)
    ob = obj_from_bmesh(name, bm, [material])
    _orient(ob, dvec)
    smooth(ob, 60)
    return ob


def poly_offset_true(poly, d):
    """Честный сдвиг замкнутого контура внутрь на d (по биссектрисам, с ограничением на острых углах)."""
    n = len(poly)
    area = sum(poly[i][0] * poly[(i + 1) % n][1] - poly[(i + 1) % n][0] * poly[i][1] for i in range(n))
    sign = 1.0 if area > 0 else -1.0     # обход против часовой — внутрь слева
    out = []
    for i in range(n):
        a, b, c = poly[i - 1], poly[i], poly[(i + 1) % n]
        e1 = (b[0] - a[0], b[1] - a[1])
        e2 = (c[0] - b[0], c[1] - b[1])
        l1 = math.hypot(*e1) or 1.0
        l2 = math.hypot(*e2) or 1.0
        n1 = (-e1[1] / l1 * sign, e1[0] / l1 * sign)
        n2 = (-e2[1] / l2 * sign, e2[0] / l2 * sign)
        nb = (n1[0] + n2[0], n1[1] + n2[1])
        lb = math.hypot(*nb) or 1.0
        nb = (nb[0] / lb, nb[1] / lb)
        cosh = max(0.6, nb[0] * n1[0] + nb[1] * n1[1])
        out.append((b[0] + nb[0] * d / cosh, b[1] + nb[1] * d / cosh))
    return out


def rounded_rect(cu, cv, w, h, r, n=40, skew=0.0, taper=0.0):
    """Контур скруглённого прямоугольника (u, v); skew — сдвиг верха по u, taper — сужение к верху."""
    pts = []
    corners = [(cu + w / 2 - r, cv + h / 2 - r, 0), (cu - w / 2 + r, cv + h / 2 - r, 90),
               (cu - w / 2 + r, cv - h / 2 + r, 180), (cu + w / 2 - r, cv - h / 2 + r, 270)]
    per = max(2, n // 4)
    for (x, y, a0) in corners:
        for k in range(per):
            a = math.radians(a0 + 90 * k / (per - 1))
            px = x + r * math.cos(a)
            py = y + r * math.sin(a)
            t = (py - (cv - h / 2)) / max(1e-6, h)
            px += skew * t
            px = cu + (px - cu) * (1 - taper * t)
            pts.append((px, py))
    return pts


def trapezoid(x0b, x1b, yb, x0t, x1t, yt, r=0.02, per=5):
    """Трапеция (низ x0b..x1b на yb, верх x0t..x1t на yt) со скруглёнными углами — для решёток и заборников."""
    corners = [(x1b, yb), (x1t, yt), (x0t, yt), (x0b, yb)]
    out = []
    n = len(corners)
    for i in range(n):
        p0, p1, p2 = corners[i - 1], corners[i], corners[(i + 1) % n]
        a = (p0[0] - p1[0], p0[1] - p1[1])
        b = (p2[0] - p1[0], p2[1] - p1[1])
        la, lb = math.hypot(*a) or 1, math.hypot(*b) or 1
        s0 = (p1[0] + a[0] / la * r, p1[1] + a[1] / la * r)
        s1 = (p1[0] + b[0] / lb * r, p1[1] + b[1] / lb * r)
        for k in range(per + 1):
            t = k / per
            # Квадратичная кривая Безье через угол — скругление.
            x = (1 - t) ** 2 * s0[0] + 2 * (1 - t) * t * p1[0] + t * t * s1[0]
            y = (1 - t) ** 2 * s0[1] + 2 * (1 - t) * t * p1[1] + t * t * s1[1]
            out.append((x, y))
        # Прямой участок до следующего угла — промежуточные точки для ровной проекции.
        q0 = s1
        p3 = corners[(i + 2) % n]
        c = (p3[0] - p2[0], p3[1] - p2[1])
        q1 = (p2[0] - b[0] / lb * r, p2[1] - b[1] / lb * r)
        steps = max(1, int(math.hypot(q1[0] - q0[0], q1[1] - q0[1]) / 0.04))
        for k in range(1, steps):
            t = k / steps
            out.append((q0[0] + (q1[0] - q0[0]) * t, q0[1] + (q1[1] - q0[1]) * t))
    return out


def ellipse(cu, cv, a, b, n=32):
    return [(cu + a * math.cos(2 * math.pi * k / n), cv + b * math.sin(2 * math.pi * k / n)) for k in range(n)]


def poly_offset(poly, d):
    """Сдвиг контура к центру (d > 0) — масштабом от центра (для выпуклых контуров)."""
    cu = sum(p[0] for p in poly) / len(poly)
    cv = sum(p[1] for p in poly) / len(poly)
    out = []
    for (u, v) in poly:
        du, dv = u - cu, v - cv
        L = math.hypot(du, dv) or 1.0
        k = max(0.0, (L - d) / L)
        out.append((cu + du * k, cv + dv * k))
    return out


def closed_curve(points, per=6):
    """Сглаженный замкнутый контур по опорным точкам (Catmull-Rom)."""
    n = len(points)
    out = []
    for i in range(n):
        p0, p1, p2, p3 = points[(i - 1) % n], points[i], points[(i + 1) % n], points[(i + 2) % n]
        for s in range(per):
            out.append(_catmull(p0, p1, p2, p3, s / per))
    return out


def glass_patches(d, surface, m):
    """Стёкла поверх проёмов: прозрачная накладка и непрозрачная фритта по краю (прячет ступеньки сетки)."""
    parts = []
    frit = d.get("frit", 0.06)
    for a in d["glass_areas_list"]:
        parts.append(patch("Glass", surface, a["outline"], a["axis"], m["glass"], lift=0.002, rings=3))
        # Окантовка (снаружи контура) и фритта (внутри) одной полосой: закрывают ступеньки сетки.
        parts.append(band("Frit", surface, a["outer"], frit + a["trim"], a["axis"], m["frame"], lift=0.0035))
    return parts
