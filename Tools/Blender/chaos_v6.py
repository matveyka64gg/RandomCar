# Шоу-события v6: VERITY (прыгающий жёлтый смайлик) и EL DRIFTO BUS (огромный дрифтующий автобус).
# Запуск: py -3 -I Tools/Blender/blender_client.py Tools/Blender/rc_lib.py Tools/Blender/chaos_lib.py Tools/Blender/chaos_v6.py
# Координаты как в Unity: x — вправо, y — вверх, z — «вперёд» объекта (лицо Верити и нос автобуса смотрят по +z).
#
# Верити — по референсу пользователя (жёлтый круг: два чёрных вертикальных овала-глаза, широкая улыбка с «ямочками»
# на концах). Шар радиусом 2.6 м, лицо — объёмные чёрные детали по поверхности. Подвижные части для анимации:
# EyeL/EyeR (моргание: масштаб по высоте вокруг центра глаза), Smile (улыбка) и MouthO (рот «О», выключен —
# включается в высоких прыжках). Начало координат — центр шара.
# Автобус — 11 м, мультяшный городской: ливрея «EL DRIFTO», окна со стёклами, двери, фары и фонари, табло,
# салон (сиденья, поручни, водитель). Колёса Wheel_FL/FR/RL/RR — отдельные объекты с центром в оси (вращаются).
# Начало координат — на земле под серединой автобуса.

VERITY_R = 2.6


def v6_materials(m):
    m.update(dict(
        verity=mat("M_VerityYellow", "#F6CB00", roughness=0.42),
        face=mat("M_VerityFace", "#0B0B0D", roughness=0.3),
        busRed=mat("M_BusRed", "#E5352A", roughness=0.35),
        busWhite=mat("M_BusWhite", "#F3F0E7", roughness=0.4),
        busYellow=mat("M_BusYellow", "#FFC21A", roughness=0.35),
        seat=mat("M_BusSeat", "#2A6FD6", roughness=0.6),
        sign=mat("M_BusSign", "#FF8A00", emission="#FF8A00", strength=3.0),
        skin=mat("M_Skin", "#F0BE93", roughness=0.6),
        glass=mat("M_CarGlass", "#0E1620", metallic=0.0, roughness=0.03, alpha=0.38),
        frame=mat("M_GlassFrit", "#08090B", roughness=0.25),
        trim=mat("M_Trim", "#26282E", roughness=0.6),
        gloss=mat("M_TrimGloss", "#0B0C0E", roughness=0.12),
        tire=mat("M_Tire", "#1A1A1D", roughness=0.88),
        rimM=mat("M_Rim", "#C9CED6", metallic=0.9, roughness=0.28),
        head=mat("M_LightHead", "#FFF6DC", emission="#FFF6DC", strength=2.0),
        tail=mat("M_LightTail", "#FF2A2A", emission="#FF1A1A", strength=2.0),
        amber=mat("M_LightAmber", "#FF9A1A", emission="#FF8A00", strength=1.0),
        interior=mat("M_Interior", "#1E1F23", roughness=0.75),
        plate=mat("M_Plate", "#F2F2EE", roughness=0.4),
    ))
    return m


def apply_transform(ob):
    bpy.context.view_layer.update()
    ob.data.transform(ob.matrix_world)
    ob.matrix_world = Matrix.Identity(4)


def ellipsoid(name, center, radii, material, normal=None, u=20, v=12):
    """Эллипсоид: radii — (полуширина, полувысота, полутолщина); normal — куда смотрит «толщина» (по поверхности шара)."""
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=u, v_segments=v, radius=1.0)
    rx, ry, rz = radii
    bmesh.ops.scale(bm, vec=Vector((rx, rz, ry)), verts=bm.verts)          # Blender: X = −x, Y = −z, Z = y
    if normal is not None:
        n = C(*normal).normalized()
        rot = Vector((0, -1, 0)).rotation_difference(n).to_matrix()        # «толщина» (−Y Blender = +z) → нормаль
        bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=rot)
    bmesh.ops.translate(bm, vec=C(*center), verts=bm.verts)
    ob = obj_from_bmesh(name, bm, [material])
    smooth(ob, 80)
    return ob


# ---------- VERITY ----------

FLAT_Z = 0.93      # шар чуть сплюснут спереди назад: объёмный, но лицо читается как круг на референсе


def surface(x, y):
    """Точка на передней поверхности шара и нормаль (координаты от центра)."""
    R = VERITY_R
    k = max(0.0, R * R - x * x - y * y)
    z = FLAT_Z * math.sqrt(k)
    # Нормаль эллипсоида (x/R², y/R², z/(c²)).
    c = FLAT_Z * R
    n = Vector((x / (R * R), y / (R * R), z / (c * c))).normalized()
    return (x, y, z), (n.x, n.y, n.z)


def tube_on_face(name, points, radius, material, ring=10, embed=0.35):
    """Трубка вдоль ломаной по поверхности шара (улыбка): сечение — окружность, наполовину утоплена."""
    bm = bmesh.new()
    rows = []
    n = len(points)
    for i, (px, py) in enumerate(points):
        p, nrm = surface(px, py)
        a = points[max(0, i - 1)]
        b = points[min(n - 1, i + 1)]
        pa, _ = surface(*a)
        pb, _ = surface(*b)
        tangent = (Vector(pb) - Vector(pa)).normalized()
        normal = Vector(nrm)
        side = tangent.cross(normal).normalized()
        centre = Vector(p) - normal * radius * embed
        row = []
        for j in range(ring):
            t = j / ring * math.tau
            q = centre + (normal * math.cos(t) + side * math.sin(t)) * radius
            row.append(bm.verts.new(C(q.x, q.y, q.z)))
        rows.append(row)
    for i in range(n - 1):
        for j in range(ring):
            k = (j + 1) % ring
            bm.faces.new((rows[i][j], rows[i][k], rows[i + 1][k], rows[i + 1][j]))
    ob = obj_from_bmesh(name, bm, [material])
    smooth(ob, 80)
    return ob


def verity(m):
    R = VERITY_R
    body = [sphere("Ball", (0, 0, 0), R, [m["verity"]], u=56, v=28, scale=(1, 1, FLAT_Z))]
    # Глаза: вертикальные овалы (по референсу: центры ±0.204R, +0.145R; полуширина 0.071R, полувысота 0.123R).
    eyes = {}
    for label, s in (("EyeL", -1), ("EyeR", 1)):
        x, y = s * 0.204 * R, 0.145 * R
        p, n = surface(x, y)
        eye = ellipsoid(label, p, (0.08 * R, 0.138 * R, 0.035 * R), m["face"], normal=n, u=24, v=14)
        eyes[label] = (eye, p)
    # Улыбка: дуга окружности через концы (±0.677R, −0.117R) и низ (0, −0.465R); толщина ~0.07R; «ямочки» на концах.
    c, r = 0.3675 * R, 0.8325 * R
    pts = []
    a0, a1 = math.radians(-144.4), math.radians(-35.6)
    for k in range(41):
        a = a0 + (a1 - a0) * k / 40
        pts.append((r * math.cos(a), c + r * math.sin(a)))
    smile = [tube_on_face("Smile", pts, 0.05 * R, m["face"], ring=12, embed=0.45)]
    for (ex, ey), a in ((pts[0], a0), (pts[-1], a1)):
        p, n = surface(ex, ey)
        # «Ямочка»: короткий овал поперёк дуги, слегка наклонённый к центру лица.
        tilt = Vector((math.cos(a), math.sin(a), 0.0))      # радиальное направление дуги — поперёк линии
        cap = ellipsoid("Dimple", p, (0.045 * R, 0.085 * R, 0.035 * R), m["face"], normal=n, u=16, v=10)
        # Повернуть «высоту» овала вдоль радиального направления (поперёк улыбки).
        ang = math.atan2(tilt.y, tilt.x) - math.pi / 2
        pivot = C(*p)
        nb = C(*n).normalized()
        cap.data.transform(Matrix.Translation(pivot) @ Matrix.Rotation(ang, 4, nb) @ Matrix.Translation(-pivot))
        smile.append(cap)
    smile_ob = join(smile[0], smile[1:])
    # Рот «О» (удивление в мега-прыжке): тёмный овал там, где середина улыбки.
    p, n = surface(0.0, -0.33 * R)
    mouth = ellipsoid("MouthO", p, (0.15 * R, 0.19 * R, 0.04 * R), m["face"], normal=n, u=24, v=14)
    children = {
        "EyeL": (eyes["EyeL"][0], eyes["EyeL"][1]),
        "EyeR": (eyes["EyeR"][0], eyes["EyeR"][1]),
        "Smile": (smile_ob, surface(0.0, -0.3 * R)[0]),
        "MouthO": (mouth, p),
    }
    return export_event("Verity", body, children)


# ---------- EL DRIFTO BUS ----------

BUS = dict(L=11.0, W=2.7, H=3.3, floor=1.42, belt=1.55, win_top=2.78, zf=3.2, zr=-2.95, wr=0.56, wx=1.06)


def bus(m):
    L, W, H = BUS["L"], BUS["W"], BUS["H"]
    hw = W / 2
    zf, zr, wr, wx = BUS["zf"], BUS["zr"], BUS["wr"], BUS["wx"]
    belt, win_top, floor = BUS["belt"], BUS["win_top"], BUS["floor"]
    zfront, zback = L / 2, -L / 2
    parts = []
    # Нижний кузов (до линии окон) — красный, со скруглёнными рёбрами; арки колёс вырезаны с бортов.
    lower = box("Lower", (0, (0.34 + belt) / 2, 0), (W, belt - 0.34, L), m["busRed"], bevel=0.16, segments=3)
    apply_modifiers(lower)
    cutters = []
    for z in (zf, zr):
        for s in (-1, 1):
            cutters.append(cylinder("ArchCut", (s * (hw + 0.05), wr, z), wr + 0.12, 0.62, 'x', m["trim"], segments=40))
    boolean_difference(lower, cutters)
    parts.append(lower)
    # Днище (видно, когда автобус лежит на боку или на крыше): тёмный поддон между колёсами, лонжероны,
    # бак, глушитель с трубой, «пузо» заднего моста.
    parts.append(box("Underbody", (0, 0.325, -0.1), (1.42, 0.03, L - 0.9), m["trim"], bevel=0.01))
    for s in (-1, 1):
        parts.append(box("Rail", (s * 0.52, 0.27, -0.1), (0.16, 0.1, L - 1.3), m["gloss"], bevel=0.02))
    parts.append(box("FuelTank", (-0.25, 0.22, 0.9), (0.42, 0.2, 1.3), m["trim"], bevel=0.05))
    parts.append(cylinder("Muffler", (0.26, 0.21, -1.6), 0.13, 0.9, 'z', m["rimM"], segments=16, bevel=0.03))
    parts.append(cylinder("ExhaustPipe", (0.26, 0.21, -3.6), 0.05, 2.9, 'z', m["gloss"], segments=10))
    parts.append(sphere("Diff", (0, 0.33, zr), 0.2, [m["gloss"]], u=16, v=10))
    parts.append(cylinder("Axle", (0, 0.28, zr), 0.06, 1.3, 'x', m["gloss"], segments=10))
    parts.append(cylinder("Axle", (0, 0.28, zf), 0.05, 1.5, 'x', m["gloss"], segments=10))
    # Пол салона (виден в окна) и юбка-порог.
    parts.append(box("Floor", (0, belt + 0.005, -0.1), (W - 0.2, 0.02, L - 1.2), m["interior"]))
    # Порог-юбка — участками между арками (не перечёркивает колёса).
    gap = wr + 0.14
    for s in (-1, 1):
        for a, b in ((zback + 0.3, zr - gap), (zr + gap, zf - gap), (zf + gap, zfront - 0.3)):
            if b - a > 0.2:
                parts.append(box("Skirt", (s * (hw + 0.005), 0.42, (a + b) / 2), (0.03, 0.16, b - a), m["trim"]))
    # Жёлтая полоса и надпись EL DRIFTO по бортам.
    for s in (-1, 1):
        parts.append(box("Stripe", (s * (hw + 0.012), belt - 0.16, 0), (0.02, 0.12, L - 0.5), m["busYellow"]))
    # Левый борт (без дверей) — большая надпись между арками; правый — на белом поясе крыши (там двери).
    for s, y, z, size, mt in ((-1, 0.92, 0.3, 0.62, m["busYellow"]), (1, 3.05, -0.4, 0.4, m["busRed"])):
        t = text_mesh("Logo", "EL DRIFTO", size, mt, extrude=0.03)
        t.rotation_euler = (math.radians(90), 0, math.radians(-90 * s))
        t.location = C(s * (hw + 0.035), y, z)
        bpy.context.view_layer.update()
        apply_transform(t)
        parts.append(t)
    # Верх: стойки между окнами (белые), крыша, рамки окон (чёрные), стёкла.
    roof_y0 = win_top
    parts.append(box("Roof", (0, (roof_y0 + H) / 2, 0), (W, H - roof_y0, L), m["busWhite"], bevel=0.2, segments=3))
    parts.append(box("RoofHatch", (0, H + 0.05, -1.0), (1.0, 0.1, 1.2), m["busWhite"], bevel=0.04))
    parts.append(box("RoofAC", (0, H + 0.12, 2.0), (1.8, 0.24, 2.4), m["busWhite"], bevel=0.08))
    win_h = win_top - belt
    # Окна по бортам: участки между стойками; у правого борта — двери (передняя и средняя).
    zs = [zback + 0.55]
    while zs[-1] < zfront - 1.9:
        zs.append(zs[-1] + 1.18)
    for s in (-1, 1):
        for i, z in enumerate(zs):
            parts.append(box("Pillar", (s * (hw - 0.04), (belt + win_top) / 2, z), (0.1, win_h, 0.14), m["busWhite"]))
            if i < len(zs) - 1:
                zc = (z + zs[i + 1]) / 2
                parts.append(box("Glass", (s * (hw - 0.03), (belt + win_top) / 2, zc), (0.02, win_h - 0.04, 1.04), m["glass"]))
        parts.append(box("WinTop", (s * (hw + 0.005), win_top - 0.04, (zs[0] + zs[-1]) / 2), (0.03, 0.08, zs[-1] - zs[0] + 0.14), m["frame"]))
        parts.append(box("WinLow", (s * (hw + 0.005), belt + 0.04, (zs[0] + zs[-1]) / 2), (0.03, 0.08, zs[-1] - zs[0] + 0.14), m["frame"]))
        # Последнее окно до носа — к лобовому.
        parts.append(box("Glass", (s * (hw - 0.03), (belt + win_top) / 2, (zs[-1] + zfront - 0.35) / 2), (0.02, win_h - 0.04, zfront - 0.35 - zs[-1] - 0.1), m["glass"]))
    # Двери справа: тёмная рама, два стеклянных створки, жёлтые поручни внутри видны сквозь стекло.
    for zc in (zfront - 0.82, 0.25):
        # Рама створок по контуру, тёмная нижняя часть (ступени), выше — стекло (видны поручни салона).
        y0, y1 = 0.42, win_top
        for (cy, sy, dz, sz) in ((y1, 0.08, 0.0, 1.25), (y0, 0.08, 0.0, 1.25), ((y0 + y1) / 2, y1 - y0, -0.6, 0.07),
                                 ((y0 + y1) / 2, y1 - y0, 0.6, 0.07), ((y0 + y1) / 2, y1 - y0, 0.0, 0.06)):
            parts.append(box("DoorFrame", (hw + 0.025, cy, zc + dz), (0.05, sy, sz), m["frame"]))
        parts.append(box("DoorLow", (hw + 0.01, (y0 + belt) / 2, zc), (0.03, belt - y0, 1.2), m["trim"]))
        for dz in (-0.3, 0.3):
            parts.append(box("DoorGlass", (hw + 0.03, (belt + y1) / 2, zc + dz), (0.02, y1 - belt - 0.06, 0.52), m["glass"]))
        parts.append(box("DoorStep", (hw - 0.1, 0.42, zc), (0.25, 0.08, 1.15), m["trim"]))
    # Нос: лобовое стекло, маска с фарами, бампер, табло EL DRIFTO, зеркала-«уши».
    parts.append(box("Windshield", (0, (1.0 + win_top) / 2 + 0.05, zfront - 0.03), (W - 0.24, win_top - 1.0, 0.03), m["glass"]))
    # Рамка лобового — по контуру (сквозь стекло виден салон и водитель).
    ws_y0, ws_y1 = 1.05, win_top + 0.05
    for (cx, cy, sx, sy) in ((0, ws_y1, W - 0.06, 0.08), (0, ws_y0, W - 0.06, 0.08), (-(hw - 0.06), (ws_y0 + ws_y1) / 2, 0.1, ws_y1 - ws_y0),
                             (hw - 0.06, (ws_y0 + ws_y1) / 2, 0.1, ws_y1 - ws_y0), (0, (ws_y0 + ws_y1) / 2, 0.07, ws_y1 - ws_y0)):
        parts.append(box("WsFrame", (cx, cy, zfront - 0.01), (sx, sy, 0.05), m["frame"]))
    parts.append(box("Mask", (0, 0.68, zfront + 0.02), (W - 0.3, 0.62, 0.05), m["busWhite"], bevel=0.04))
    parts.append(box("Grille", (0, 0.68, zfront + 0.05), (1.1, 0.34, 0.03), m["gloss"]))
    for gy in (0.58, 0.68, 0.78):
        parts.append(box("GrilleBar", (0, gy, zfront + 0.07), (1.06, 0.03, 0.02), m["busWhite"]))
    for s in (-1, 1):
        for dx in (0.0, 0.3):
            x = s * (0.92 - dx)
            parts.append(cylinder("HeadRim", (x, 0.72, zfront + 0.05), 0.15, 0.05, 'z', m["gloss"], segments=24))
            parts.append(cylinder("Head", (x, 0.72, zfront + 0.075), 0.12, 0.02, 'z', m["head"], segments=24))
        parts.append(box("Signal", (s * 1.12, 0.48, zfront + 0.05), (0.18, 0.08, 0.04), m["amber"]))
    parts.append(box("BumperF", (0, 0.36, zfront + 0.12), (W + 0.06, 0.24, 0.26), m["trim"], bevel=0.06))
    parts.append(box("PlateF", (0, 0.36, zfront + 0.26), (0.52, 0.11, 0.02), m["plate"]))
    parts.append(box("SignBox", (0, win_top + 0.2, zfront - 0.02), (W - 0.4, 0.34, 0.05), m["gloss"]))
    parts.append(box("SignLed", (0, win_top + 0.2, zfront + 0.005), (W - 0.55, 0.24, 0.02), m["sign"]))
    sign = text_mesh("SignText", "EL DRIFTO", 0.2, m["face"], extrude=0.01)
    sign.rotation_euler = (math.radians(90), 0, 0)
    sign.location = C(0, win_top + 0.2, zfront + 0.025)
    bpy.context.view_layer.update()
    apply_transform(sign)
    parts.append(sign)
    for s in (-1, 1):
        parts.append(beam("MirrorArm", (s * (hw - 0.1), win_top + 0.25, zfront - 0.05), (s * (hw + 0.25), win_top - 0.1, zfront + 0.45), 0.05, 0.05, m["gloss"], bevel=0.01))
        parts.append(box("Mirror", (s * (hw + 0.25), win_top - 0.42, zfront + 0.45), (0.12, 0.62, 0.3), m["gloss"], bevel=0.04))
    # Корма: заднее стекло, моторная решётка, фонари, бампер, выхлоп.
    parts.append(box("RearGlass", (0, (2.0 + win_top) / 2, zback - 0.02), (W - 0.5, win_top - 2.0, 0.03), m["glass"]))
    parts.append(box("EngineGrille", (0, 1.0, zback - 0.02), (1.6, 0.6, 0.03), m["gloss"]))
    for gy in (0.8, 0.92, 1.04, 1.16):
        parts.append(box("EngineBar", (0, gy, zback - 0.04), (1.56, 0.03, 0.02), m["busWhite"]))
    for s in (-1, 1):
        parts.append(box("TailRed", (s * 1.12, 1.15, zback - 0.03), (0.2, 0.42, 0.04), m["tail"]))
        parts.append(box("TailAmber", (s * 1.12, 0.84, zback - 0.03), (0.2, 0.14, 0.04), m["amber"]))
    parts.append(box("BumperR", (0, 0.36, zback - 0.1), (W + 0.06, 0.24, 0.22), m["trim"], bevel=0.06))
    parts.append(box("PlateR", (0, 0.62, zback - 0.03), (0.52, 0.11, 0.02), m["plate"]))
    parts.append(cylinder("Exhaust", (-0.9, 0.28, zback - 0.15), 0.08, 0.3, 'z', m["gloss"], segments=12))
    # Внутренняя стенка арок — от выреза (материал резака M_Trim): колесо видно целиком.
    # Салон: сиденья парами по бортам, поручни, водитель.
    for zc in [z for z in [zback + 1.3 + k * 0.95 for k in range(9)] if z < zfront - 2.1]:
        for s in (-1, 1):
            if s > 0 and abs(zc - 0.25) < 0.8:
                continue                        # у средней двери — накопительная площадка
            x = s * (hw - 0.62)
            parts.append(box("Seat", (x, floor + 0.32, zc), (0.95, 0.12, 0.48), m["seat"], bevel=0.03))
            parts.append(box("SeatBack", (x, floor + 0.68, zc - 0.22), (0.95, 0.62, 0.1), m["seat"], bevel=0.03))
    for s in (-1, 1):
        parts.append(cylinder("Rail", (s * (hw - 0.4), win_top - 0.15, -0.2), 0.03, L - 2.0, 'z', m["busYellow"], segments=8))
    for zc in (-3.5, -1.2, 0.25, 1.6):
        parts.append(cylinder("Pole", (0.25, (floor + win_top) / 2, zc), 0.035, win_top - floor, 'y', m["busYellow"], segments=8))
    # Водитель: кресло, руль, фигура в кепке и тёмных очках, с усами.
    dx, dz = -0.65, zfront - 1.15
    parts.append(box("DriverSeat", (dx, floor + 0.35, dz - 0.2), (0.6, 0.14, 0.55), m["trim"], bevel=0.03))
    parts.append(box("DriverBack", (dx, floor + 0.78, dz - 0.48), (0.6, 0.75, 0.12), m["trim"], bevel=0.03))
    parts.append(box("Dash", (0, floor + 0.42, zfront - 0.4), (W - 0.4, 0.22, 0.45), m["interior"], bevel=0.08))
    parts.append(torus("BusWheel", (dx, floor + 0.85, dz + 0.42), 0.27, 0.035, 'y', m["gloss"], seg=28, ring=8))
    parts.append(box("Torso", (dx, floor + 0.85, dz - 0.25), (0.5, 0.62, 0.32), m["seat"], bevel=0.12, segments=3))
    parts.append(sphere("DriverHead", (dx, floor + 1.38, dz - 0.2), 0.2, [m["skin"]], u=20, v=12))
    parts.append(cylinder("Cap", (dx, floor + 1.53, dz - 0.21), 0.21, 0.1, 'y', m["seat"], segments=20, bevel=0.03))
    parts.append(box("CapVisor", (dx, floor + 1.5, dz - 0.0), (0.3, 0.03, 0.18), m["gloss"]))
    parts.append(box("Shades", (dx, floor + 1.4, dz - 0.02), (0.32, 0.07, 0.04), m["gloss"]))
    parts.append(ellipsoid("Mustache", (dx, floor + 1.28, dz - 0.01), (0.13, 0.035, 0.04), m["face"], u=14, v=8))
    for s in (-1, 1):
        parts.append(beam("Arm", (dx + s * 0.25, floor + 1.05, dz - 0.2), (dx + s * 0.22, floor + 0.86, dz + 0.38), 0.11, 0.11, m["seat"], bevel=0.04))
    # Колёса: шина, диск с колпаком и гайками; задние — двойные (шире). Центр — в оси колеса.
    wheels = {}
    for label, s, z, dual in (("Wheel_FL", -1, zf, False), ("Wheel_FR", 1, zf, False), ("Wheel_RL", -1, zr, True), ("Wheel_RR", 1, zr, True)):
        x = s * wx
        width = 0.62 if dual else 0.36
        w = [cylinder(label, (x, wr, z), wr, width, 'x', m["tire"], segments=36, bevel=0.06, bevel_segments=3)]
        w.append(cylinder("Disc", (x + s * (width / 2 + 0.005), wr, z), wr * 0.62, 0.03, 'x', m["rimM"], segments=28))
        w.append(cylinder("Cap", (x + s * (width / 2 + 0.03), wr, z), wr * 0.24, 0.05, 'x', m["busRed"], segments=20, bevel=0.015))
        for k in range(8):
            a = k / 8 * math.tau
            w.append(cylinder("Nut", (x + s * (width / 2 + 0.02), wr + math.sin(a) * wr * 0.4, z + math.cos(a) * wr * 0.4), 0.03, 0.04, 'x', m["busWhite"], segments=6))
        # Протектор: шашки «ёлочкой» по середине шины (не торчат за скруглённые плечи) — по ним видно вращение.
        for k in range(18):
            a = k / 18 * math.tau
            shift = (width * 0.1) if k % 2 else -(width * 0.1)
            lug = box("Lug", (0, 0, 0), (width * 0.55, 0.03, 0.11), m["tire"])
            lug.data.transform(Matrix.Rotation(a, 4, 'X'))
            lug.data.transform(Matrix.Translation(C(x + shift, wr + math.sin(a) * (wr - 0.004), z + math.cos(a) * (wr - 0.004))))
            w.append(lug)
        wheels[label] = (join(w[0], w[1:]), (x, wr, z))
    return export_event("ElDriftoBus", parts, wheels)


reset_scene()
m = v6_materials(chaos_materials())
results = [verity(m), bus(m)]
for ob in bpy.data.objects:
    ob.hide_render = ob.type == 'MESH' and ob.name != "RC_Ground"
for idx, (root, tris) in enumerate(results):
    root.location = C(-5 + idx * 10, 0 if idx else VERITY_R, 0)
    for c in all_children(root):
        c.hide_render = False
mo = bpy.data.objects.get("MouthO_Verity")
if mo:
    mo.hide_render = True
# Цели — в координатах Blender (Z вверх): Верити в (5, 0, 2.6), автобус в (−5, 0, 0), нос автобуса — к −Y.
render_preview(os.path.join(PREVIEW, "v6_verity.png"), (5, 0, 2.6), 10, 0.6, 0, lens=40)
render_preview(os.path.join(PREVIEW, "v6_verity_side.png"), (5, 0, 2.6), 10, 2.0, 55, lens=40)
render_preview(os.path.join(PREVIEW, "v6_bus_front.png"), (-5, 0, 1.5), 14, 2.5, -35, lens=40)
render_preview(os.path.join(PREVIEW, "v6_bus_rear.png"), (-5, 0, 1.5), 14, 3.5, 215, lens=40)
render_preview(os.path.join(PREVIEW, "v6_bus_door.png"), (-5, 0, 1.5), 13, 1.5, 70, lens=40)
# Днище: автобус на боку (как в кювете) — только для превью, после экспорта.
bus_root = results[1][0]
bus_root.rotation_euler = (0, math.radians(-100), 0)
bus_root.location = (-5 + 1.5, 0, 1.4)
render_preview(os.path.join(PREVIEW, "v6_bus_under.png"), (-5, 0, 1.2), 13, 2.0, 120, lens=40)
for root, tris in results:
    print(f"{root.name}: {tris} tris")
