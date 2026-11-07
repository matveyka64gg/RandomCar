# Модели бонусов: ящик «?», самонаводящаяся ракета, ракета на лидера, пулемёт на крышу, пузырь щита.
# Запуск: py -3 -I Tools/Blender/blender_client.py Tools/Blender/rc_lib.py Tools/Blender/chaos_lib.py Tools/Blender/powerups_v1.py
# Координаты как в Unity: x — вправо, y — вверх, z — «вперёд». Ящик, ракеты и пузырь — начало в центре; пулемёт — у основания.


def pu_materials(m):
    m.update(dict(
        boxA=mat("M_ItemBoxA", "#FF4BAA", roughness=0.3, emission="#FF4BAA", strength=0.8),
        boxB=mat("M_ItemBoxB", "#2DD4F0", roughness=0.3, emission="#2DD4F0", strength=0.8),
        boxC=mat("M_ItemBoxC", "#FFC21A", roughness=0.3, emission="#FFC21A", strength=0.8),
        mark=mat("M_ItemMark", "#FFFFFF", roughness=0.3, emission="#FFFFFF", strength=2.0),
        rocketRed=mat("M_SpringTop", "#FF3B3B", roughness=0.35),
        leaderBlue=mat("M_LeaderBlue", "#2F6BFF", roughness=0.3, metallic=0.2),
        iron=mat("M_CannonIron", "#2B2E35", metallic=0.6, roughness=0.4),
        olive=mat("M_AmmoOlive", "#6B7A3A", roughness=0.6),
        shield=mat("M_ShieldGlow", "#40FFE0", alpha=0.3),
    ))
    return m


def item_box(m):
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.3)
    ob = obj_from_bmesh("BoxShell", bm, [m["boxA"], m["boxB"], m["boxC"]])
    add_bevel(ob, 0.16, 3)
    apply_modifiers(ob)
    # Цвет грани по её направлению: бока x — розовые, z — бирюзовые, верх и низ — жёлтые.
    for p in ob.data.polygons:
        n = p.normal
        ax, ay, az = abs(n.x), abs(n.y), abs(n.z)
        p.material_index = 0 if ax >= ay and ax >= az else (2 if az >= ay else 1)
    parts = [ob]
    # «?» на четырёх боках и сверху.
    for k, (rx, rz, pos) in enumerate([(90, 0, (0, 0, -0.67)), (90, 180, (0, 0, 0.67)), (90, 90, (0.67, 0, 0)), (90, -90, (-0.67, 0, 0))]):
        q = text_mesh("Mark", "?", 0.9, m["mark"], extrude=0.03)
        q.rotation_euler = (math.radians(rx), 0, math.radians(rz))
        q.location = C(*pos)
        bpy.context.view_layer.update()
        apply_transform(q)
        parts.append(q)
    top = text_mesh("Mark", "?", 0.9, m["mark"], extrude=0.03)
    top.location = C(0, 0.67, 0)
    bpy.context.view_layer.update()
    apply_transform(top)
    parts.append(top)
    return export_event("ItemBox", parts)


def apply_transform(ob):
    ob.data.transform(ob.matrix_world)
    ob.matrix_world = Matrix.Identity(4)


def homing_rocket(m):
    # Смотрит вдоль +z, начало — центр. Злые мультяшные глаза на носу.
    parts = [cylinder("Body", (0, 0, 0), 0.24, 1.3, 'z', m["rocketRed"], segments=20, bevel=0.03)]
    parts.append(cone("Nose", (0, 0, 0.65), (0, 0, 1.15), 0.24, m["white"], segments=20))
    parts.append(cylinder("Band", (0, 0, 0.3), 0.25, 0.12, 'z', m["white"], segments=20))
    parts.append(cylinder("Nozzle", (0, 0, -0.75), 0.17, 0.25, 'z', m["steel"], segments=16))
    for k in range(4):
        a = k / 4 * math.tau + math.pi / 4
        parts.append(box("Fin", (math.cos(a) * 0.36, math.sin(a) * 0.36, -0.45), (0.04 if abs(math.cos(a)) < 0.5 else 0.3, 0.3 if abs(math.cos(a)) < 0.5 else 0.04, 0.4), m["yellow"]))
    for s in (-1, 1):
        parts.append(sphere("Eye", (s * 0.11, 0.12, 0.62), 0.09, [m["white"]], u=10, v=6))
        parts.append(sphere("Pupil", (s * 0.11, 0.12, 0.69), 0.045, [m["black"]], u=8, v=4))
        parts.append(beam("Brow", (s * 0.2, 0.24, 0.6), (s * 0.03, 0.19, 0.66), 0.04, 0.03, m["black"]))
    return export_event("HomingRocket", parts)


def leader_rocket(m):
    parts = [cylinder("Body", (0, 0, 0), 0.34, 1.7, 'z', m["leaderBlue"], segments=24, bevel=0.04)]
    parts.append(cone("Nose", (0, 0, 0.85), (0, 0, 1.55), 0.34, m["gold"], segments=24))
    parts.append(cylinder("Nozzle", (0, 0, -0.95), 0.24, 0.3, 'z', m["steel"], segments=16))
    # Крылья и корона шипов — «ракета-гроза лидеров».
    for s in (-1, 1):
        parts.append(box("Wing", (s * 0.75, 0, -0.1), (1.1, 0.06, 0.7), m["gold"], bevel=0.02))
        parts.append(box("Tail", (s * 0.35, 0.3, -0.75), (0.5, 0.05, 0.35), m["gold"]))
    for k in range(8):
        a = k / 8 * math.tau
        parts.append(cone("Spike", (math.cos(a) * 0.33, math.sin(a) * 0.33, 0.45), (math.cos(a) * 0.6, math.sin(a) * 0.6, 0.45), 0.08, m["white"], segments=8))
    return export_event("LeaderRocket", parts)


def machine_gun(m):
    base = [box("Plate", (0, 0.04, 0), (0.8, 0.08, 0.7), m["rivet"], bevel=0.02)]
    base.append(cylinder("Swivel", (0, 0.14, 0), 0.18, 0.14, 'y', m["steel"], segments=16))
    base.append(box("Body", (0, 0.32, 0.05), (0.42, 0.26, 0.6), m["iron"], bevel=0.03))
    base.append(box("Ammo", (0.33, 0.28, -0.05), (0.22, 0.2, 0.36), m["olive"], bevel=0.02))
    barrels = []
    for s in (-1, 1):
        barrels.append(cylinder("Barrel", (s * 0.12, 0.34, 0.75), 0.06, 0.9, 'z', m["iron"], segments=12))
        barrels.append(cylinder("Muzzle", (s * 0.12, 0.34, 1.18), 0.085, 0.1, 'z', m["steel"], segments=12))
        barrels.append(cylinder("Cooler", (s * 0.12, 0.34, 0.55), 0.09, 0.3, 'z', m["steel"], segments=12))
    barrel_ob = join(barrels[0], barrels[1:])
    return export_event("MachineGun", base, {"Barrels": (barrel_ob, (0, 0.34, 0.3))})


def shield_bubble(m):
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=3, radius=0.5)
    ob = obj_from_bmesh("Bubble", bm, [m["shield"]])
    return export_event("ShieldBubble", [ob])


reset_scene()
m = pu_materials(chaos_materials())
results = [fn(m) for fn in (item_box, homing_rocket, leader_rocket, machine_gun, shield_bubble)]
for ob in bpy.data.objects:
    ob.hide_render = ob.type == 'MESH' and ob.name != "RC_Ground"
for idx, (root, tris) in enumerate(results):
    root.location = C(idx * 2.6 - 5, 1.2, 0)
    for c in all_children(root):
        c.hide_render = False
render_preview(os.path.join(PREVIEW, "powerups_v1.png"), (0, 1.2, 0), 9, 3, 200, lens=40)
for root, tris in results:
    print(f"{root.name}: {tris} tris")
