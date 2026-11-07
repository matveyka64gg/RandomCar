# Полигон испытаний (Crash Test Arena): ангар краш-тестов, краш-стена с мишенью, гигантский манекен, сегмент ограды,
# мачта прожекторов, контейнеры, табло «TEST ZONE». Стиль RandomCar: яркие цвета, крупные формы, полосы «осторожно».
# Запуск: py -3 -I Tools/Blender/blender_client.py Tools/Blender/rc_lib.py Tools/Blender/chaos_lib.py Tools/Blender/arena.py
# Координаты как в Unity: x — вправо, y — вверх, z — «лицом» (ворота ангара, лицевая сторона стены и табло смотрят по +z).
# Начало координат каждой модели — на земле по центру основания. Надписи — латиницей (шрифт Blender без кириллицы).

ARENA_DIR = os.path.join(ART, "Arena")
os.makedirs(ARENA_DIR, exist_ok=True)


def arena_materials():
    return dict(
        concrete=mat("M_Concrete", "#D3CEC4", roughness=0.9),
        concreteDark=mat("M_ConcreteDark", "#9A958C", roughness=0.9),
        steel=mat("M_Steel", "#B8C0CC", metallic=0.6, roughness=0.4),
        trim=mat("M_Trim", "#26282E", roughness=0.6),
        yellow=mat("M_SignYellow", "#FFD23F", roughness=0.5),
        black=mat("M_Decal_Black", "#15161A", roughness=0.5),
        white=mat("M_Decal_White", "#F2F0E8", roughness=0.4),
        brand=mat("M_Brand", "#FF6A2A", roughness=0.5),
        brandDark=mat("M_BrandDark", "#121826", roughness=0.5),
        red=mat("M_KerbRed", "#E0352B", roughness=0.5),
        blue=mat("M_Paint_Blue", "#2F6BFF", roughness=0.4),
        windowLit=mat("M_WindowLit", "#FFD9A0", emission="#FFD9A0", strength=2.0),
        windowDark=mat("M_WindowDark", "#2B3A52", metallic=0.2, roughness=0.1),
        hangarWall=mat("M_HangarWall", "#6E9BD1", roughness=0.55),
        hangarRoof=mat("M_HangarRoof", "#E3EAF2", roughness=0.5),
        lamp=mat("M_Floodlamp", "#FFF6DC", emission="#FFF6DC", strength=4.0),
        tire=mat("M_Tire", "#1C1C20", roughness=0.85),
    )


def finish(name, parts):
    ob = join(parts[0], parts[1:]) if len(parts) > 1 else parts[0]
    ob.name = name
    ob.data.name = name
    root = empty(name + "_Root")
    set_parent(ob, root)
    export_fbx(root, os.path.join(ARENA_DIR, name + ".fbx"))
    tris = tri_count(ob)
    root.name = name + "_Done"
    ob.name = name + "_Mesh"
    return root, tris


def prism(name, points, z, thickness, material):
    """Плоская фигура (точки x, y — в плоскости модели), выдавленная по z на thickness с центром в z."""
    bm = bmesh.new()
    front = [bm.verts.new(C(x, y, z + thickness / 2)) for x, y in points]
    back = [bm.verts.new(C(x, y, z - thickness / 2)) for x, y in points]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    n = len(points)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([front[i], back[i], back[j], front[j]])
    return obj_from_bmesh(name, bm, [material])


def disc(name, center, radius, depth, material, segments=32):
    return cylinder(name, center, radius, depth, 'z', material, segments=segments)


def target(prefix, center, radius, parts, m, depth=0.04):
    """Мишень краш-теста: чёрно-жёлтые кольца (плоские диски, каждое чуть ближе к зрителю)."""
    x, y, z = center
    colors = [m["black"], m["yellow"], m["black"], m["yellow"]]
    for k, frac in enumerate((1.0, 0.74, 0.48, 0.22)):
        parts.append(disc(f"{prefix}Ring{k}", (x, y, z + k * depth), radius * frac, depth, colors[k]))


def hazard_band(prefix, x0, x1, y0, y1, z, step, parts, m, thickness=0.06):
    """Полоса «осторожно»: наклонные чёрные полосы на жёлтом фоне между x0..x1, y0..y1 (на плоскости z)."""
    parts.append(box(prefix + "Back", ((x0 + x1) / 2, (y0 + y1) / 2, z), (x1 - x0, y1 - y0, thickness), m["yellow"]))
    h = y1 - y0
    w = step * 0.5
    x = x0
    k = 0
    while x < x1 - 0.01:
        a = max(x0, x)
        b = min(x1, x + w)
        c = min(x1, x + w + h)
        d = min(x1, x + h)
        if b > a:
            pts = [(a, y0), (b, y0), (c if c > b else b, y1), (d if d > a else a, y1)]
            parts.append(prism(f"{prefix}Stripe{k}", pts, z + thickness / 2 + 0.01, 0.02, m["black"]))
        x += step
        k += 1


def arch_points(half_width, base_y, rise, segs):
    return [(-half_width + 2 * half_width * i / segs,
             base_y + rise * math.sqrt(max(0.0, 1 - ((-half_width + 2 * half_width * i / segs) / half_width) ** 2)))
            for i in range(segs + 1)]


def arch_shell(name, half_width, base_y, rise, z0, z1, thickness, material, segs=16):
    """Арочная крыша: внешняя и внутренняя поверхности (видно снаружи и изнутри) и торцы."""
    outer = arch_points(half_width, base_y, rise, segs)
    inner = arch_points(half_width - thickness, base_y, rise - thickness, segs)
    bm = bmesh.new()
    vo0 = [bm.verts.new(C(x, y, z0)) for x, y in outer]
    vo1 = [bm.verts.new(C(x, y, z1)) for x, y in outer]
    vi0 = [bm.verts.new(C(x, y, z0)) for x, y in inner]
    vi1 = [bm.verts.new(C(x, y, z1)) for x, y in inner]
    for i in range(segs):
        bm.faces.new([vo0[i], vo0[i + 1], vo1[i + 1], vo1[i]])
        bm.faces.new([vi0[i], vi1[i], vi1[i + 1], vi0[i + 1]])
        bm.faces.new([vo0[i], vi0[i], vi0[i + 1], vo0[i + 1]])
        bm.faces.new([vo1[i], vo1[i + 1], vi1[i + 1], vi1[i]])
    ob = obj_from_bmesh(name, bm, [material])
    return ob


def hangar(m):
    W, D, WALL = 48.0, 36.0, 8.0
    hw = W / 2
    RISE = 8.0
    DOOR_W, DOOR_H = 36.0, 11.0
    parts = []
    parts.append(box("Floor", (0, 0.15, 0), (W + 1.0, 0.3, D + 1.0), m["concreteDark"]))
    # Боковые и задняя стены: голубые панели с рёбрами-стойками; окна-ленты по бокам.
    for s in (-1, 1):
        parts.append(box("SideWall", (s * (hw - 0.25), WALL / 2, 0), (0.5, WALL, D), m["hangarWall"]))
        for k in range(10):
            z = -D / 2 + 1.8 + k * (D - 3.6) / 9
            parts.append(box("Rib", (s * (hw + 0.05), WALL / 2, z), (0.25, WALL, 0.35), m["steel"]))
        parts.append(box("WindowBand", (s * (hw + 0.02), WALL - 1.6, 0), (0.1, 1.4, D - 4.0), m["windowDark"]))
    parts.append(box("BackWall", (0, WALL / 2, -D / 2 + 0.25), (W, WALL, 0.5), m["hangarWall"]))
    # Задний фронтон под аркой (закрыт), передний — над воротами.
    back_pts = [(x, y) for x, y in arch_points(hw, WALL, RISE, 16)]
    parts.append(prism("BackGable", [(hw, WALL)] + list(reversed(back_pts))[1:-1] + [(-hw, WALL)], -D / 2 + 0.25, 0.5, m["hangarWall"]))
    # Передний фронтон — часть арки выше ворот (по краям ниже DOOR_H арку закрывают простенки).
    edge = hw * math.sqrt(1 - ((DOOR_H - WALL) / RISE) ** 2)
    front_pts = [(x, y) for x, y in arch_points(hw, WALL, RISE, 32) if abs(x) < edge - 0.05]
    gable = [(-edge, DOOR_H)] + front_pts + [(edge, DOOR_H)]
    parts.append(prism("FrontGable", list(reversed(gable)), D / 2 - 0.25, 0.5, m["hangarWall"]))
    # Простенки по бокам ворот.
    for s in (-1, 1):
        cx = s * (DOOR_W / 2 + (hw - DOOR_W / 2) / 2)
        parts.append(box("FrontPier", (cx, DOOR_H / 2, D / 2 - 0.25), (hw - DOOR_W / 2, DOOR_H, 0.5), m["hangarWall"]))
    parts.append(arch_shell("Roof", hw + 0.6, WALL, RISE + 0.4, -D / 2 - 0.6, D / 2 + 0.6, 0.45, m["hangarRoof"]))
    # Рама ворот «осторожно» и вывеска.
    for s in (-1, 1):
        for k in range(8):
            col = m["yellow"] if k % 2 == 0 else m["black"]
            parts.append(box("DoorJamb", (s * (DOOR_W / 2 + 0.35), DOOR_H * (k + 0.5) / 8, D / 2 + 0.05), (0.7, DOOR_H / 8, 0.4), col))
    hazard_band("Lintel", -DOOR_W / 2 - 0.7, DOOR_W / 2 + 0.7, DOOR_H, DOOR_H + 0.9, D / 2 + 0.06, 1.6, parts, m)
    parts.append(box("SignBoard", (0, WALL + 3.2, D / 2 + 0.2), (24.0, 3.6, 0.3), m["brandDark"]))
    for text, y, size, mt in (("RANDOMCAR", WALL + 3.85, 1.7, m["brand"]), ("CRASH TEST CENTER", WALL + 2.35, 1.05, m["white"])):
        t = text_mesh("Sign_" + text.replace(" ", ""), text, size, mt, extrude=0.06)
        t.rotation_euler = (math.radians(90), 0, 0)
        t.location = C(0, y, D / 2 + 0.4)
        bpy.context.view_layer.update()
        t.data.transform(t.matrix_world)
        t.matrix_world = Matrix.Identity(4)
        parts.append(t)
    # Внутри: светильники под крышей, жёлтая разметка, подъёмник с машиной-болванкой, шкафы с инструментом.
    for x in (-14, 0, 14):
        for z in (-10, 4):
            parts.append(box("Lamp", (x, WALL + RISE - 2.2 - abs(x) * 0.12, z), (2.4, 0.25, 1.0), m["lamp"]))
    for x in (-12, 12):
        parts.append(box("FloorLine", (x, 0.31, 0), (0.25, 0.02, D - 4), m["yellow"]))
    parts.append(box("FloorLineBack", (0, 0.31, -D / 2 + 3), (24.25, 0.02, 0.25), m["yellow"]))
    for s in (-1, 1):
        parts.append(box("LiftPost", (s * 3.0 - 6, 2.0, -8), (0.45, 4.0, 0.45), m["red"]))
    parts.append(box("LiftDeck", (-6, 2.3, -8), (5.2, 0.25, 2.2), m["steel"]))
    parts.append(box("MockCar", (-6, 3.1, -8), (4.2, 1.2, 1.9), m["blue"], bevel=0.25))
    for k in range(5):
        parts.append(box("Cabinet", (14 + k * 1.3 - 2.6, 1.0, -D / 2 + 1.2), (1.2, 2.0, 0.8), m["red"] if k % 2 else m["brand"]))
    return finish("TestHangar", parts)


def crash_wall(m):
    W, H, DEP = 12.0, 3.6, 3.0
    parts = [box("Block", (0, H / 2, 0), (W, H, DEP), m["concrete"], bevel=0.12)]
    hazard_band("Band", -W / 2, W / 2, H - 0.8, H - 0.15, DEP / 2 + 0.03, 1.2, parts, m)
    target("Target", (0, 1.45, DEP / 2 + 0.05), 1.05, parts, m)
    for s in (-1, 1):
        target("SideTarget", (s * 3.9, 1.45, DEP / 2 + 0.05), 0.55, parts, m)
    # Подушка из шин перед стеной.
    for row in range(2):
        for k in range(9):
            x = -W / 2 + 0.9 + k * (W - 1.8) / 8
            parts.append(cylinder("Tire", (x, 0.25 + row * 0.42, DEP / 2 + 0.75), 0.48, 0.4, 'y', m["tire"], segments=14))
    t = text_mesh("CrashText", "CRASH", 0.8, m["black"], extrude=0.05)
    t.rotation_euler = (math.radians(90), 0, 0)
    t.location = C(0, H + 0.45, DEP / 2 - 0.3)
    bpy.context.view_layer.update()
    t.data.transform(t.matrix_world)
    t.matrix_world = Matrix.Identity(4)
    parts.append(t)
    return finish("CrashWall", parts)


def dummy(m):
    """Гигантский манекен краш-теста на постаменте: машет рукой, на голове и груди — мишени."""
    parts = [box("Pedestal", (0, 0.6, 0), (6.0, 1.2, 6.0), m["concreteDark"], bevel=0.15)]
    hazard_band("PedBand", -3.0, 3.0, 0.15, 0.55, 3.03, 0.8, parts, m)
    y0 = 1.2
    yel, blk = m["yellow"], m["black"]
    for s in (-1, 1):
        parts.append(cylinder("Leg", (s * 0.9, y0 + 1.6, 0), 0.55, 3.2, 'y', yel, segments=16, bevel=0.1))
        parts.append(torus("Knee", (s * 0.9, y0 + 1.7, 0), 0.6, 0.12, 'y', blk, seg=20, ring=8))
        parts.append(box("Foot", (s * 0.9, y0 + 0.2, 0.35), (1.0, 0.45, 1.6), blk, bevel=0.15))
    parts.append(box("Hips", (0, y0 + 3.4, 0), (2.6, 0.9, 1.5), blk, bevel=0.3))
    parts.append(box("Torso", (0, y0 + 5.0, 0), (3.0, 2.6, 1.7), yel, bevel=0.45, segments=3))
    target("Chest", (0, y0 + 5.2, 0.9), 0.55, parts, m)
    parts.append(cylinder("Neck", (0, y0 + 6.6, 0), 0.32, 0.6, 'y', blk, segments=14))
    parts.append(sphere("Head", (0, y0 + 7.55, 0), 1.0, [yel], u=24, v=14))
    for s in (-1, 1):
        target("HeadMark", (s * 0.42, y0 + 7.75, 0.86), 0.24, parts, m, depth=0.03)
    parts.append(box("Mouth", (0, y0 + 7.2, 0.95), (0.6, 0.08, 0.06), blk))
    # Левая рука опущена, правая поднята и машет.
    parts.append(cylinder("ArmL", (-1.85, y0 + 4.9, 0), 0.38, 2.6, 'y', yel, segments=14, bevel=0.08))
    parts.append(sphere("HandL", (-1.85, y0 + 3.5, 0), 0.45, [blk], u=14, v=8))
    arm = cylinder("ArmR", (0, 0, 0), 0.38, 2.6, 'y', yel, segments=14, bevel=0.08)
    arm.data.transform(Matrix.Rotation(math.radians(-35), 4, 'Y'))
    arm.data.transform(Matrix.Translation(C(2.3, y0 + 7.0, 0)))
    parts.append(arm)
    parts.append(sphere("HandR", (2.95, y0 + 8.1, 0), 0.45, [blk], u=14, v=8))
    return finish("CrashDummy", parts)


def arena_wall(m):
    """Сегмент ограды полигона: 12 м бетона, красно-белый верх, стойка."""
    L, H, T = 12.0, 3.2, 0.5
    parts = [box("Wall", (0, H / 2, 0), (L, H, T), m["concrete"])]
    for k in range(6):
        parts.append(box("Cap", (-L / 2 + 1.0 + k * 2.0, H + 0.15, 0), (2.0, 0.3, T + 0.1), m["red"] if k % 2 == 0 else m["white"]))
    parts.append(box("Post", (L / 2 - 0.25, H / 2 + 0.2, 0), (0.5, H + 0.4, T + 0.3), m["concreteDark"]))
    parts.append(box("Stripe", (0, 0.9, T / 2 + 0.01), (L - 0.6, 0.35, 0.02), m["brand"]))
    parts.append(box("StripeBack", (0, 0.9, -T / 2 - 0.01), (L - 0.6, 0.35, 0.02), m["brand"]))
    return finish("ArenaWall", parts)


def floodlight(m):
    H = 22.0
    parts = [cylinder("Base", (0, 0.4, 0), 0.9, 0.8, 'y', m["concreteDark"], segments=12)]
    pole = bmesh.new()
    bmesh.ops.create_cone(pole, cap_ends=True, cap_tris=False, segments=10, radius1=0.45, radius2=0.22, depth=H)
    bmesh.ops.translate(pole, vec=C(0, H / 2 + 0.8, 0), verts=pole.verts)
    parts.append(obj_from_bmesh("Pole", pole, [m["steel"]]))
    parts.append(box("Frame", (0, H + 1.4, 0.3), (4.6, 2.4, 0.4), m["trim"]))
    for i in range(3):
        for j in range(2):
            parts.append(box("Lamp", (-1.5 + i * 1.5, H + 0.85 + j * 1.1, 0.55), (1.2, 0.85, 0.15), m["lamp"]))
    parts.append(box("Ladder", (0.5, H / 2 + 0.8, -0.35), (0.35, H, 0.08), m["yellow"]))
    return finish("Floodlight", parts)


def container(name, color, m):
    L, H, W = 6.0, 2.6, 2.4
    parts = [box("Box", (0, H / 2, 0), (W, H, L), color)]
    for k in range(13):
        z = -L / 2 + 0.25 + k * (L - 0.5) / 12
        for s in (-1, 1):
            parts.append(box("Rib", (s * (W / 2 + 0.03), H / 2, z), (0.06, H - 0.3, 0.16), color))
    for y in (0.08, H - 0.08):
        for s in (-1, 1):
            parts.append(box("Rail", (s * (W / 2 - 0.05), y, 0), (0.2, 0.16, L), m["trim"]))
    parts.append(box("Doors", (0, H / 2, L / 2 + 0.03), (W - 0.2, H - 0.3, 0.06), m["trim"]))
    return finish(name, parts)


def zone_sign(m):
    """Табло «TEST ZONE» на двух ногах: жёлто-чёрное, видно из машины издалека."""
    parts = []
    for s in (-1, 1):
        parts.append(box("Leg", (s * 2.2, 1.6, 0), (0.25, 3.2, 0.25), m["steel"]))
    parts.append(box("Board", (0, 3.9, 0), (5.6, 1.9, 0.2), m["black"]))
    hazard_band("Band", -2.8, 2.8, 2.95, 3.25, 0.11, 0.7, parts, m, thickness=0.04)
    for zs, rot in ((0.13, 0), (-0.13, 180)):
        t = text_mesh("ZoneText", "TEST ZONE", 0.95, m["yellow"], extrude=0.03)
        t.rotation_euler = (math.radians(90), 0, math.radians(rot))
        t.location = C(0, 4.05, zs)
        bpy.context.view_layer.update()
        t.data.transform(t.matrix_world)
        t.matrix_world = Matrix.Identity(4)
        parts.append(t)
    return finish("ZoneSign", parts)


reset_scene()
m = arena_materials()
results = [hangar(m), crash_wall(m), dummy(m), arena_wall(m), floodlight(m),
           container("Container_Orange", m["brand"], m), container("Container_Blue", m["blue"], m), zone_sign(m)]
for ob in bpy.data.objects:
    ob.hide_render = ob.type == 'MESH' and ob.name != "RC_Ground"
layout = [(0, 0), (40, 0), (-30, 10), (25, 22), (-50, 25), (60, 18), (68, 18), (40, 28)]
for (root, tris), (x, z) in zip(results, layout):
    root.location = C(x, 0, z)
    for c in all_children(root):
        c.hide_render = False
# Цели — в координатах Blender (Z вверх); C(x, y, z) Unity → Blender (−x, −z, y).
render_preview(os.path.join(PREVIEW, "arena_hangar.png"), (0, 0, 8), 70, 18, 0, lens=35)
render_preview(os.path.join(PREVIEW, "arena_props.png"), (-35, -15, 4), 45, 12, 20, lens=35)
render_preview(os.path.join(PREVIEW, "arena_dummy.png"), (30, -10, 6), 22, 5, -15, lens=40)
for (root, tris) in results:
    print(f"{root.name}: {tris} tris")
