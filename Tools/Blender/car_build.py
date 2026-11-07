# Генератор машин v2/v3: сборка машины из кузова и деталей, превью и экспорт.
# Запуск: blender_client.py rc_lib.py car_body.py car_parts.py car_details.py car_designs.py car_build.py kits.py car_run.py
# (car_run.py задаёт, какие машины строить: BUILD_KEYS, EXPORT, PREVIEW_ONLY).


def outline_both(points):
    """Контур правой стороны → (правый, левый зеркальный)."""
    return points, [(-x, y) for (x, y) in reversed(points)]


def build_static(d, m):
    """Кузов со всеми неподвижными деталями. Возвращает (body, surface, prof)."""
    prof = Profiles(d)
    body, full = build_shell(d, prof, m)
    bed = d.get("bed")
    if bed:
        # Кузов пикапа: вырез открытой платформы (стенки получают тёмный материал подкрылков).
        hw = prof.hw((bed["z0"] + bed["z1"]) / 2) - bed["inset"]
        for target in (body, full):
            cutter = box("BedCut", (0, bed["floor"] + 1.5, (bed["z0"] + bed["z1"]) / 2), (2 * hw, 3.0, bed["z1"] - bed["z0"]), m["liner"])
            boolean_difference(target, [cutter])
    cut_arches(d, body, m)
    open_glass(body)
    surface = Surface(full)
    bpy.data.objects.remove(full, do_unlink=True)
    parts = []
    parts += arch_liners(d, prof, m)
    # Короба колёс в кузове пикапа (пол ниже верха колёс).
    parts += bed_tubs(d, prof, body, m)

    det = details_of(d)
    # Фары: отражатели, линзы, ходовые огни и поворотники (v3).
    h = d["head"]
    right = closed_curve(h["outline"], 4) if len(h["outline"]) < 12 else h["outline"]
    if h.get("scale_v"):
        # Выразительнее «лицо»: узкая фара выше по вертикали (вокруг своего центра).
        cv = sum(p[1] for p in right) / len(right)
        right = [(x, cv + (y - cv) * h["scale_v"]) for (x, y) in right]
    for side, outline in ((1, right), (-1, [(-x, y) for (x, y) in reversed(right)])):
        parts += headlight(surface, d, m, side, outline, h["style"], det["signal"])
    # Фонари: секции поворотника, стопа и заднего хода (v3).
    t = d["tail_lights"]
    tr = rounded_poly(t["outline"], t.get("round", 0.03)) if t.get("round") else closed_curve(t["outline"], 4)
    for side, outline in ((1, tr), (-1, [(-x, y) for (x, y) in reversed(tr)])):
        parts += taillight(surface, d, m, side, outline, det["tail_split"])
    parts += glass_patches(d, surface, m)

    # Решётки и воздухозаборники (ламели, соты, бруски, рамки) и мелкие детали кузова (v3, car_details.py).
    parts += grille(d, surface, m, det)
    parts += body_details(d, prof, surface, m)

    # Сплиттер, диффузор, бамперы и зеркала — в обвесе (kits.py: варианты «Stock»), их можно заменить.

    # Номера.
    for name, (cx, cy, w, hh), axis in (("PlateF", d["plate_front"], 'z+'), ("PlateR", d["plate_rear"], 'z-')):
        # Номер выше ламелей и рамки решётки (иначе мерцает поверх них).
        parts.append(patch(name, surface, rounded_rect(cx, cy, w, hh, 0.012, 20), axis, m["plate"], lift=0.014, rings=1))

    # Стыки дверей и ручки (тонкие тёмные линии по борту).
    for line in d.get("door_lines", []):
        pts = [(lerp(line[0][0], line[1][0], k / 10), lerp(line[0][1], line[1][1], k / 10)) for k in range(11)]
        for axis in ('x+', 'x-'):
            parts.append(strip(surface, pts, axis, 0.006, m["frame"], lift=0.002, name="Seam"))
    handles = d.get("handles") or ([d["handle"]] if "handle" in d else [])
    for hz, hy in handles:
        for axis in ('x+', 'x-'):
            parts.append(patch("Handle", surface, rounded_rect(hz, hy, 0.17, 0.028, 0.012, 16), axis, m["chrome"], lift=0.007, rings=1))


    # Выхлоп.
    e = d["exhaust"]
    zt = d["tail"] + 0.03
    xs = (e["x"], -e["x"]) if e.get("twin") else (e["x"],)
    for x in xs:
        parts.append(cylinder("Exhaust", (x, e["y"], zt), e["r"], 0.16, 'z', m["chrome"], segments=20, bevel=0.006))
        parts.append(cylinder("ExhaustIn", (x, e["y"], zt - 0.075), e["r"] * 0.8, 0.02, 'z', m["liner"], segments=20))

    # Днище: тёмная плоскость, чтобы снизу не просвечивал пустой кузов.
    wh = d["wheel"]
    parts.append(box("Underbody", (0, prof.bottom(0) + 0.015, (d["nose"] + d["tail"]) / 2), (2 * (wh["x"] - wh["w"] / 2 - 0.05), 0.01, d["nose"] - d["tail"] - 0.5), m["under"]))

    if d.get("spare_wheel"):
        zt2 = d["tail"] - 0.13
        parts.append(cylinder("Spare", (0, 1.12, zt2), wh["r"] * 0.95, wh["w"] * 0.9, 'z', m["tire"], segments=40, bevel=0.04, bevel_segments=3))
        parts.append(cylinder("SpareCover", (0, 1.12, zt2 - wh["w"] * 0.46), wh["r"] * 0.80, 0.03, 'z', m["roof"], segments=40, bevel=0.012))
    if d.get("fender_signals"):
        for s in (-1, 1):
            parts.append(rounded_box("Signal", (s * 0.80, d["profile"]["top"][-2][1] + 0.03, d["nose"] - 0.25), (0.12, 0.05, 0.08), m["amber"], 0.015))

    body = join(body, parts)
    body.name = "Body"
    triangulate_ngons(body)
    return body, surface, prof


def triangulate_ngons(ob):
    """Многоугольники больше четырёх вершин — в треугольники (Unity отбрасывает самопересекающиеся n-угольники)."""
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4], quad_method='BEAUTY', ngon_method='BEAUTY')
    bm.to_mesh(ob.data)
    bm.free()


TIRE_RIM_RATIO = {"Sport": 1.08, "Offroad": 0.94}


def build_wheels(d, m, root, key):
    """Колёса: шина (по ней физика берёт радиус), заводской диск, тормоз; варианты шин — соседи колеса."""
    wh = d["wheel"]
    out = []
    for label, xs, z in (("FL", -1, wh["zf"]), ("FR", 1, wh["zf"]), ("RL", -1, wh["zr"]), ("RR", 1, wh["zr"])):
        tob = tire("Wheel_" + label, wh["r"], wh["w"], wh["rim_r"], m, wh.get("tire", "road"))
        rob = rim("Rim_" + label, wh["rim"], wh["rim_r"], wh["w"], m)
        disc, cal = brake("Brake_" + label, wh["rim_r"] * (0.95 if wh.get("brake_big") else 1.0), wh["w"], m, wh.get("brake_big", False))
        disc.name = "Disc_" + label
        cal.name = "Caliper_" + label
        variants = []
        for style, ratio in TIRE_RIM_RATIO.items():
            tv = tire(f"Tire_{style}_{label}", wh["r"], wh["w"] * (1.04 if style == "Sport" else 1.0), wh["rim_r"] * ratio, m,
                      "low" if style == "Sport" else "offroad")
            variants.append(tv)
        for ob in [tob, rob, disc, cal] + variants:
            ob.data.name = ob.name + "_" + key
            if xs < 0:
                ob.rotation_euler = (0, 0, math.pi)
            ob.location = C(xs * wh["x"], wh["r"], z)
            set_parent(ob, root)
        out += [tob, rob, disc, cal]
    return out


def build_car(key, export=False):
    d = DESIGNS[key]
    m = car_materials()
    root = empty("Car_" + key)
    body, surface, prof = build_static(d, m)
    set_parent(body, root)
    inner = interior(d, prof, m)
    if details_of(d).get("cage"):
        inner = join(inner, cage(d, prof, m))
        inner.name = "Interior"
    triangulate_ngons(inner)
    set_parent(inner, root)
    # Пилот (v3): отдельный объект, камера из салона прячет его вместе с обшивкой.
    pilot = driver(d, prof, m)
    triangulate_ngons(pilot)
    set_parent(pilot, root)
    liner = cabin_liner(d, prof, m)
    liner.name = "CabinLiner"
    triangulate_ngons(liner)
    set_parent(liner, root)
    sw = steering_wheel("SteeringWheel", m)
    st = d["steering"]
    sw.matrix_world = Matrix.Translation(C(*st["pos"])) @ Matrix.Rotation(math.radians(st["tilt"]), 4, 'X')
    set_parent(sw, root)
    build_wheels(d, m, root, key)
    build_kits(d, prof, surface, m, root)
    # В превью — только заводской вид (в FBX экспортируются все варианты).
    for ob in all_children(root):
        if ob.name.startswith("Tire_") or (ob.name.startswith("Kit_") and not ob.name.endswith("_Stock")):
            ob.hide_render = True
    wh = d["wheel"]
    mount_y = prof.top(d["spoiler_mount"][2])
    mount = empty("SpoilerMount", (0, mount_y, d["spoiler_mount"][2]))
    set_parent(mount, root)
    roof = empty("RoofMount", (0, prof.top(d["roof_mount_z"]), d["roof_mount_z"]))
    set_parent(roof, root)
    # Метки зон для наклеек сверху: капот, крыша, багажник (z в координатах машины).
    g = d["greenhouse"]
    for name, z in (("Mark_HoodStart", g["cowl"] + 0.05), ("Mark_HoodEnd", d.get("hood_end", d["nose"] - 0.15)),
                    ("Mark_RoofStart", g["roof_rear"]), ("Mark_RoofEnd", g["roof_front"]),
                    ("Mark_TrunkStart", d["tail"] + 0.05), ("Mark_TrunkEnd", g["deck"])):
        mk = empty(name, (0, prof.top(z), z))
        set_parent(mk, root)
    return root


def studio(scene_name="RC_Studio"):
    """Нейтральная студия для оценки машин: серый пол, три источника, мягкое небо."""
    scene = bpy.context.scene
    world = bpy.data.worlds.get("RC_StudioWorld") or bpy.data.worlds.new("RC_StudioWorld")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = hex_color("#9AA6B4")
    bg.inputs["Strength"].default_value = 0.55
    scene.world = world
    try:
        scene.render.engine = "BLENDER_EEVEE"
    except TypeError:
        scene.render.engine = "BLENDER_EEVEE_NEXT"
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    if hasattr(scene, "eevee"):
        scene.eevee.taa_render_samples = 48
    scene.view_settings.view_transform = 'AgX'
    for name, kind, energy, loc, rot, size in (
            ("RC_Key", 'AREA', 900, (4.5, -3.5, 4.0), (50, 0, 50), 4.0),
            ("RC_Fill", 'AREA', 300, (-5.0, -2.0, 3.0), (60, 0, -60), 5.0),
            ("RC_Back", 'AREA', 600, (0.0, 5.5, 3.5), (-55, 0, 180), 4.0)):
        ob = bpy.data.objects.get(name)
        if ob is None:
            light = bpy.data.lights.new(name, kind)
            ob = bpy.data.objects.new(name, light)
            scene.collection.objects.link(ob)
        ob.data.energy = energy
        ob.data.size = size
        ob.location = loc
        ob.rotation_euler = tuple(math.radians(a) for a in rot)
    ground = bpy.data.objects.get("RC_StudioFloor")
    if ground is None:
        bm = bmesh.new()
        bmesh.ops.create_grid(bm, x_segments=1, y_segments=1, size=40)
        ground = obj_from_bmesh("RC_StudioFloor", bm, [mat("RC_StudioFloorMat", "#5C6068", roughness=0.55)], recalc=False)
    for nm in ("RC_Sun", "RC_Ground"):
        ob = bpy.data.objects.get(nm)
        if ob:
            ob.hide_render = True
    return scene


def shot(path, target_car, dist, height, yaw, lens=45):
    scene = studio()
    cam = bpy.data.objects.get("RC_Cam")
    if cam is None:
        cam = bpy.data.objects.new("RC_Cam", bpy.data.cameras.new("RC_Cam"))
        scene.collection.objects.link(cam)
    cam.data.lens = lens
    t = C(*target_car)
    a = math.radians(yaw)
    # yaw в координатах машины: 0 — спереди, 90 — справа.
    off = C(math.sin(a) * dist, height, math.cos(a) * dist)
    cam.location = t + off
    cam.rotation_euler = (t - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.camera = cam
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)


def preview_car(key, root):
    base = os.path.join(PREVIEW, "v2")
    os.makedirs(base, exist_ok=True)
    shot(os.path.join(base, f"{key}_front34.png"), (0, 0.6, 0.1), 6.2, 1.1, 35)
    shot(os.path.join(base, f"{key}_rear34.png"), (0, 0.6, -0.1), 6.2, 1.4, 145)
    shot(os.path.join(base, f"{key}_side.png"), (0, 0.65, 0.0), 7.5, 0.6, 90, lens=50)
    shot(os.path.join(base, f"{key}_wheel.png"), (0.8, 0.35, DESIGNS[key]["wheel"]["zf"]), 1.6, 0.25, 75, lens=50)
    shot(os.path.join(base, f"{key}_cabin.png"), (0, 0.95, 0.0), 2.6, 0.55, 55, lens=35)


def preview_quick(key, root, tag="v3"):
    base = os.path.join(PREVIEW, tag)
    os.makedirs(base, exist_ok=True)
    scene = studio()
    if hasattr(scene, "eevee"):
        scene.eevee.taa_render_samples = 24
    shot(os.path.join(base, f"{key}_front34.png"), (0, 0.6, 0.1), 6.2, 1.1, 35)
    shot(os.path.join(base, f"{key}_rear34.png"), (0, 0.6, -0.1), 6.2, 1.4, 145)
    shot(os.path.join(base, f"{key}_frontlow.png"), (0, 0.6, 1.4), 3.6, 0.35, 20, lens=40)
    shot(os.path.join(base, f"{key}_wheel.png"), (0.8, 0.35, DESIGNS[key]["wheel"]["zf"]), 1.6, 0.25, 75, lens=50)
    shot(os.path.join(base, f"{key}_cabin.png"), (0, 0.95, 0.0), 2.6, 0.55, 55, lens=35)
    # Салон без стекла (материал стекла временно полностью прозрачный) и без обшивки: пилот, руль, каркас.
    glass = bpy.data.materials.get("M_CarGlass")
    bsdf = glass.node_tree.nodes.get("Principled BSDF") if glass else None
    alpha = bsdf.inputs["Alpha"].default_value if bsdf else 1.0
    if bsdf:
        bsdf.inputs["Alpha"].default_value = 0.0
    liner = [o for o in all_children(root) if o.name.startswith("CabinLiner")]
    for o in liner:
        o.hide_render = True
    shot(os.path.join(base, f"{key}_driver.png"), (-0.3, 0.95, 0.2), 2.4, 0.45, 15, lens=35)
    for o in liner:
        o.hide_render = False
    if bsdf:
        bsdf.inputs["Alpha"].default_value = alpha


def export_car(key, root):
    """FBX в Assets/Art/Models/Cars; после экспорта имена деталей получают суффикс машины (имена в Blender общие)."""
    export_fbx(root, os.path.join(ART, "Cars", f"Car_{key}.fbx"))
    for ob in all_children(root):
        ob.name = ob.name + "_" + key
    root.name = root.name + "_done"
