# Модели карты «Город»: здания нескольких типов и уличные объекты. Запуск: blender_client.py rc_lib.py city.py
# Координаты машины: x — вправо, y — вверх, z — к улице. Начало координат — середина фасада у земли,
# здание уходит назад (в -z). Вечерний город: часть окон и витрины светятся.

import random

CITY_DIR = os.path.join(ART, "City")


def city_materials():
    return dict(
        brick=mat("M_Brick", "#B5523B", roughness=0.9),
        plaster=mat("M_Plaster", "#E8D9B5", roughness=0.85),
        teal=mat("M_PlasterTeal", "#5FB3A8", roughness=0.85),
        mustard=mat("M_PlasterMustard", "#E0A93B", roughness=0.85),
        lilac=mat("M_PlasterLilac", "#B39DDB", roughness=0.85),
        roof=mat("M_Roof", "#5A4A44", roughness=0.8),
        lit=mat("M_WindowLit", "#FFD9A0", emission="#FFC77A", strength=1.6),
        dark=mat("M_WindowDark", "#2B3A52", metallic=0.2, roughness=0.1),
        shop=mat("M_WindowShop", "#FFE3B0", emission="#FFD08A", strength=2.2),
        glassblue=mat("M_GlassBlue", "#3F7FA8", metallic=0.6, roughness=0.05),
        trim=mat("M_Trim", "#26282E", roughness=0.6),
        concrete=mat("M_Concrete", "#D3CEC4", roughness=0.85),
        concretedark=mat("M_ConcreteDark", "#9A958C", roughness=0.85),
        steel=mat("M_Steel", "#B8C0CC", metallic=0.6, roughness=0.4),
        awning=[mat("M_Awning1", "#E0352B"), mat("M_Awning2", "#24A0E0"), mat("M_Awning3", "#2DAE6A")],
        white=mat("M_Decal_White", "#F2F0E8", roughness=0.4),
        neon=mat("M_Neon", "#FF4FA0", emission="#FF4FA0", strength=3.2),
        brand=mat("M_Brand", "#FF6A2A", roughness=0.5),
        branddark=mat("M_BrandDark", "#121826", roughness=0.5),
        lamp=mat("M_LampGlow", "#FFF3D6", emission="#FFF3D6", strength=1.5),
        red=mat("M_TrafficRed", "#3A0A0A", emission="#FF2A1A", strength=2.5),
        green=mat("M_TrafficGreen", "#0A3A1A", emission="#2AFF7A", strength=2.5),
        off=mat("M_TrafficOff", "#2A2A1A", roughness=0.4),
        leaf=mat("M_Leaf", "#5DB04A", roughness=0.8),
        leaf2=mat("M_Leaf2", "#7BC24F", roughness=0.8),
        wood=mat("M_Wood", "#7A5233", roughness=0.8),
        kerbred=mat("M_KerbRed", "#E0352B", roughness=0.5),
        glass=mat("M_Glass", "#1B2A3A", metallic=0.2, roughness=0.05),
    )


def finish(name, parts):
    ob = join(parts[0], parts[1:]) if len(parts) > 1 else parts[0]
    ob.name = name
    ob.data.name = name
    root = empty(name + "_Root")
    set_parent(ob, root)
    export_fbx(root, os.path.join(CITY_DIR, name + ".fbx"))
    return ob


def windows_on_face(parts, m, rnd, face, w, d, h0, floors, floor_h, cols, win_w, win_h, lit_share=0.45, frame=True):
    """Окна на грани: face — 'front' (z=0), 'left' (x=-w/2), 'right' (x=+w/2)."""
    span = w if face == "front" else d
    for f in range(floors):
        y = h0 + f * floor_h + floor_h * 0.55
        for c in range(cols):
            t = (c + 0.5) / cols
            pos = -span / 2 + t * span
            lit = rnd.random() < lit_share
            glass = m["lit"] if lit else m["dark"]
            if face == "front":
                center, size, fsize = (pos, y, 0.03), (win_w, win_h, 0.06), (win_w + 0.18, win_h + 0.18, 0.04)
                fcenter = (pos, y, 0.01)
            else:
                sx = -w / 2 - 0.03 if face == "left" else w / 2 + 0.03
                z = -d / 2 + pos
                center, size, fsize = (sx, y, z), (0.06, win_h, win_w), (0.04, win_h + 0.18, win_w + 0.18)
                fcenter = (sx * 0.999, y, z)
            if frame:
                parts.append(box("Frame", fcenter, fsize, m["trim"]))
            parts.append(box("Win", center, size, glass))


def roof_details(parts, m, rnd, w, d, h):
    parts.append(box("Parapet", (0, h + 0.3, -d / 2), (w + 0.2, 0.6, d + 0.2), m["concretedark"]))
    parts.append(box("RoofTop", (0, h + 0.55, -d / 2), (w - 0.4, 0.1, d - 0.4), m["roof"]))
    for i in range(rnd.randint(1, 3)):
        x = rnd.uniform(-w / 2 + 1.5, w / 2 - 1.5)
        z = rnd.uniform(-d + 1.5, -1.5)
        parts.append(box("AC", (x, h + 1.1, z), (1.4, 0.9, 1.0), m["steel"], bevel=0.04))


def shop(name, w, d, wall, sign_text, awning, seed, m):
    rnd = random.Random(seed)
    h = 7.6
    parts = [box("Walls", (0, h / 2, -d / 2), (w, h, d), wall, bevel=0.05)]
    # Витрина первого этажа и дверь.
    parts.append(box("ShopFrame", (0, 1.7, 0.02), (w - 1.0, 3.0, 0.08), m["trim"]))
    parts.append(box("ShopGlass", (0, 1.75, 0.07), (w - 1.5, 2.6, 0.05), m["shop"]))
    for k in range(1, 4):
        x = -w / 2 + 0.5 + k * (w - 1.0) / 4
        parts.append(box("Mullion", (x, 1.75, 0.1), (0.1, 2.6, 0.05), m["trim"]))
    # Маркиза: наклонная полосатая плита.
    for s in range(6):
        aw = box("Awning", (0, 0, 0), ((w - 1.0) / 6 - 0.02, 0.06, 1.5), awning if s % 2 == 0 else m["white"])
        # Поворот вокруг собственного центра, затем на место (box() строит вершины в мировых координатах).
        aw.rotation_euler = (math.radians(18), 0, 0)
        aw.location = C((s - 2.5) * (w - 1.0) / 6, 3.55, 0.75)
        parts.append(aw)
    # Вывеска.
    parts.append(box("SignBoard", (0, 4.6, 0.1), (w * 0.6, 0.8, 0.12), m["branddark"], bevel=0.03))
    t = text_mesh("SignText", sign_text, 0.55, m["neon"], extrude=0.02)
    t.rotation_euler = (math.radians(90), 0, 0)
    t.location = C(0, 4.6, 0.2)
    parts.append(t)
    windows_on_face(parts, m, rnd, "front", w, d, 5.0, 1, 2.6, max(2, int(w / 3)), 1.3, 1.5, 0.6)
    windows_on_face(parts, m, rnd, "left", w, d, 3.6, 2, 2.6, max(2, int(d / 3.5)), 1.2, 1.4)
    windows_on_face(parts, m, rnd, "right", w, d, 3.6, 2, 2.6, max(2, int(d / 3.5)), 1.2, 1.4)
    roof_details(parts, m, rnd, w, d, h)
    return finish(name, parts)


def apartment(name, w, d, floors, wall, seed, m):
    rnd = random.Random(seed)
    fh = 3.0
    h = 1.2 + floors * fh
    parts = [box("Walls", (0, h / 2, -d / 2), (w, h, d), wall, bevel=0.05)]
    parts.append(box("Base", (0, 0.6, 0.02), (w + 0.1, 1.2, 0.1), m["concretedark"]))
    parts.append(box("Door", (0, 1.25, 0.06), (1.6, 2.4, 0.1), m["trim"]))
    parts.append(box("DoorLight", (0, 2.7, 0.12), (0.5, 0.15, 0.15), m["lamp"]))
    cols = max(3, int(w / 2.8))
    windows_on_face(parts, m, rnd, "front", w, d, 1.2, floors, fh, cols, 1.2, 1.6)
    windows_on_face(parts, m, rnd, "left", w, d, 1.2, floors, fh, max(2, int(d / 3.2)), 1.1, 1.6)
    windows_on_face(parts, m, rnd, "right", w, d, 1.2, floors, fh, max(2, int(d / 3.2)), 1.1, 1.6)
    # Балконы на части этажей.
    for f in range(1, floors):
        if rnd.random() < 0.55:
            for c in (0, cols - 1):
                x = -w / 2 + (c + 0.5) * w / cols
                y = 1.2 + f * fh
                parts.append(box("Balcony", (x, y + 0.05, 0.55), (1.8, 0.12, 1.1), m["concrete"]))
                parts.append(box("Rail", (x, y + 0.55, 1.08), (1.8, 0.9, 0.05), m["steel"]))
    # Карниз между этажами.
    parts.append(box("Cornice", (0, 1.2 + fh * 2, 0.08), (w + 0.1, 0.2, 0.16), m["concrete"]))
    roof_details(parts, m, rnd, w, d, h)
    return finish(name, parts)


def office(name, w, d, floors, seed, m):
    rnd = random.Random(seed)
    fh = 3.4
    h = floors * fh
    parts = [box("Core", (0, h / 2, -d / 2), (w - 0.6, h, d - 0.6), m["concretedark"])]
    # Стеклянные ленты фасада и межэтажные пояса.
    for f in range(floors):
        y = f * fh + fh * 0.55
        lit = rnd.random() < 0.5
        g = m["lit"] if lit else m["glassblue"]
        parts.append(box("GlassBand", (0, y, -d / 2), (w - 0.4, fh * 0.62, d - 0.4), g))
        parts.append(box("Slab", (0, f * fh + 0.1, -d / 2), (w, 0.35, d), m["concrete"]))
    parts.append(box("Crown", (0, h + 0.6, -d / 2), (w, 1.2, d), m["concrete"]))
    # Неоновая вывеска на крыше.
    t = text_mesh("Logo", "RANDOM", 2.2, m["neon"], extrude=0.1)
    t.rotation_euler = (math.radians(90), 0, 0)
    t.location = C(0, h + 2.6, 0.0)
    parts.append(t)
    parts.append(box("LogoFrame", (0, h + 2.6, -0.2), (w * 0.85, 2.6, 0.3), m["branddark"]))
    # Вход: козырёк.
    parts.append(box("Canopy", (0, 3.2, 1.2), (w * 0.5, 0.25, 2.4), m["brand"], bevel=0.05))
    return finish(name, parts)


def oldhouse(name, w, d, wall, seed, m):
    rnd = random.Random(seed)
    floors, fh = 3, 3.3
    h = floors * fh
    parts = [box("Walls", (0, h / 2, -d / 2), (w, h, d), wall, bevel=0.04)]
    for f in range(floors + 1):
        parts.append(box("Cornice", (0, f * fh, 0.1), (w + 0.2, 0.22, 0.2), m["white"]))
    cols = max(3, int(w / 2.6))
    windows_on_face(parts, m, rnd, "front", w, d, 0.0, floors, fh, cols, 1.0, 1.9, 0.5)
    windows_on_face(parts, m, rnd, "left", w, d, 0.0, floors, fh, max(2, int(d / 3.0)), 0.9, 1.8)
    windows_on_face(parts, m, rnd, "right", w, d, 0.0, floors, fh, max(2, int(d / 3.0)), 0.9, 1.8)
    # Двускатная крыша.
    rings = []
    for z in (0.3, -d - 0.3):
        rings.append([(-w / 2 - 0.3, h, z), (w / 2 + 0.3, h, z), (0, h + w * 0.28, z)])
    bm = loft("Roof", rings, lambda i, j: 0)
    parts.append(obj_from_bmesh("Roof", bm, [m["roof"]]))
    parts.append(box("Chimney", (w * 0.25, h + w * 0.25, -d * 0.4), (0.8, 2.0, 0.8), m["brick"]))
    return finish(name, parts)


def street_lamp(m):
    parts = [cylinder("Pole", (0, 3.0, 0), 0.08, 6.0, 'y', m["trim"], segments=12)]
    parts.append(cylinder("Base", (0, 0.3, 0), 0.16, 0.6, 'y', m["trim"], segments=12))
    parts.append(beam("Arm", (0, 5.8, 0), (0, 6.1, 1.4), 0.07, 0.07, m["trim"]))
    parts.append(box("Head", (0, 6.0, 1.5), (0.45, 0.18, 0.7), m["trim"], bevel=0.03))
    parts.append(box("Bulb", (0, 5.89, 1.5), (0.35, 0.05, 0.55), m["lamp"]))
    return finish("StreetLamp", parts)


def traffic_light(m):
    parts = [cylinder("Pole", (0, 2.6, 0), 0.09, 5.2, 'y', m["trim"], segments=12)]
    parts.append(beam("Arm", (0, 5.0, 0), (0, 5.0, 3.0), 0.1, 0.1, m["trim"]))
    parts.append(box("Box", (0, 4.4, 3.0), (0.45, 1.3, 0.35), m["trim"], bevel=0.04))
    for i, matk in enumerate(("red", "off", "green")):
        parts.append(cylinder("Light", (0, 4.8 - i * 0.4, 3.2), 0.13, 0.05, 'z', m[matk], segments=16))
    return finish("TrafficLight", parts)


def bench(m):
    parts = [box("Seat", (0, 0.45, 0), (1.8, 0.08, 0.45), m["wood"], bevel=0.02)]
    parts.append(box("Back", (0, 0.8, -0.2), (1.8, 0.4, 0.06), m["wood"], bevel=0.02))
    for x in (-0.75, 0.75):
        parts.append(box("Leg", (x, 0.25, -0.05), (0.08, 0.5, 0.45), m["trim"]))
    return finish("Bench", parts)


def planter(m, seed):
    rnd = random.Random(seed)
    parts = [box("Pot", (0, 0.35, 0), (1.4, 0.7, 1.4), m["concrete"], bevel=0.05)]
    parts.append(cylinder("Trunk", (0, 1.6, 0), 0.1, 2.0, 'y', m["wood"], segments=8))
    for i in range(3):
        bm = bmesh.new()
        bmesh.ops.create_icosphere(bm, subdivisions=1, radius=1.0 - i * 0.15)
        bmesh.ops.translate(bm, vec=C(rnd.uniform(-0.3, 0.3), 2.9 + i * 0.4, rnd.uniform(-0.3, 0.3)), verts=bm.verts)
        parts.append(obj_from_bmesh("Crown", bm, [m["leaf"] if i % 2 == 0 else m["leaf2"]]))
    return finish("Planter", parts)


def barrier(m):
    parts = [box("Block", (0, 0.45, 0), (3.0, 0.9, 0.6), m["concrete"], bevel=0.05)]
    for k in range(4):
        parts.append(box("Stripe", (-1.1 + k * 0.75, 0.6, 0.31), (0.35, 0.3, 0.02), m["kerbred"]))
    return finish("CityBarrier", parts)


def bus_stop(m):
    parts = [box("Roof", (0, 2.6, -0.6), (4.0, 0.12, 1.6), m["trim"], bevel=0.03)]
    for x in (-1.9, 1.9):
        parts.append(box("Post", (x, 1.3, -1.3), (0.1, 2.6, 0.1), m["steel"]))
    parts.append(box("BackGlass", (0, 1.4, -1.35), (3.8, 2.0, 0.04), m["glass"]))
    parts.append(box("Ad", (1.5, 1.3, -1.3), (0.9, 1.6, 0.08), m["shop"]))
    parts.append(box("Seat", (-0.4, 0.5, -1.1), (2.4, 0.08, 0.4), m["wood"]))
    return finish("BusStop", parts)


def billboard(m, text):
    parts = [box("PoleL", (-2.0, 3.0, 0), (0.25, 6.0, 0.25), m["steel"]), box("PoleR", (2.0, 3.0, 0), (0.25, 6.0, 0.25), m["steel"])]
    parts.append(box("Board", (0, 6.6, 0.1), (7.0, 3.0, 0.3), m["branddark"], bevel=0.05))
    parts.append(box("Face", (0, 6.6, 0.27), (6.6, 2.6, 0.04), m["brand"]))
    t = text_mesh("Text", text, 1.1, m["white"], extrude=0.03)
    t.rotation_euler = (math.radians(90), 0, 0)
    t.location = C(0, 6.6, 0.3)
    parts.append(t)
    return finish("CityBillboard_" + str(len(text)), parts)


def build_city():
    reset_scene()
    os.makedirs(PREVIEW, exist_ok=True)
    m = city_materials()
    made = []
    made.append(shop("Bld_Shop_A", 12, 10, m["plaster"], "КАФЕ", m["awning"][0], 1, m))
    made.append(shop("Bld_Shop_B", 10, 10, m["teal"], "ЦВЕТЫ", m["awning"][2], 2, m))
    made.append(shop("Bld_Shop_C", 14, 10, m["mustard"], "ПЕКАРНЯ", m["awning"][1], 3, m))
    made.append(apartment("Bld_Apt_A", 14, 12, 5, m["brick"], 4, m))
    made.append(apartment("Bld_Apt_B", 16, 12, 6, m["lilac"], 5, m))
    made.append(apartment("Bld_Apt_C", 12, 12, 4, m["teal"], 6, m))
    made.append(office("Bld_Office_A", 18, 18, 9, 7, m))
    made.append(office("Bld_Office_B", 14, 14, 12, 8, m))
    made.append(oldhouse("Bld_Old_A", 12, 11, m["plaster"], 9, m))
    made.append(oldhouse("Bld_Old_B", 10, 10, m["mustard"], 10, m))
    made += [street_lamp(m), traffic_light(m), bench(m), planter(m, 11), barrier(m), bus_stop(m), billboard(m, "RANDOMCAR")]

    # Превью: улица из домов.
    x = -40.0
    for ob in made:
        root = ob.parent
        root.location = C(x, 0, 0)
        x += 15.0 if ob.name.startswith("Bld") else 5.0
    render_preview(os.path.join(PREVIEW, "city.png"), (C(10, 6, 0).x, C(10, 6, 0).y, 6), 70, 12, 10, lens=30)
    for ob in made:
        print(f"{ob.name}: {tri_count(ob)} tris")


build_city()
