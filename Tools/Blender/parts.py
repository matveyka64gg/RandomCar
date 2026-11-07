# Библиотека деталей гаража: диски и спойлеры. Запуск: blender_client.py rc_lib.py cars.py parts.py
# (cars.py даёт функцию rim() и материалы; его основной цикл не нужен — он защищён флагом PARTS_ONLY).
# Диски строятся в осях колеса (наружу +x) для колеса r=0.36, w=0.27 — в Unity масштабируются под колесо.
# Спойлеры строятся от точки SpoilerMount: начало координат — середина задней кромки, назад — -z.

PARTS = os.path.join(ART, "Parts")


def spoiler_lip(m):
    parts = [box("Lip", (0, 0.025, -0.05), (1.40, 0.05, 0.12), m["paint"], bevel=0.02)]
    parts.append(box("LipEdge", (0, 0.055, -0.1), (1.36, 0.02, 0.04), m["trim"], bevel=0.008))
    return join(parts[0], parts[1:])


def spoiler_duck(m):
    rings = []
    for k in range(5):
        t = k / 4
        z = -0.02 - 0.22 * t
        y = 0.02 + 0.14 * t * t
        hw = 0.78 - 0.05 * t
        rings.append([(-hw, y, z), (hw, y, z), (hw, y + 0.035, z), (-hw, y + 0.035, z)])
    bm = loft("Duck", rings, lambda i, j: 0)
    ob = obj_from_bmesh("Duck", bm, [m["paint"]])
    add_bevel(ob, 0.01, 2)
    return ob


def spoiler_gt(m):
    y = 0.28
    parts = [box("Wing", (0, y, -0.06), (1.70, 0.05, 0.32), m["trim"], bevel=0.02)]
    parts.append(box("Gurney", (0, y + 0.04, -0.21), (1.66, 0.04, 0.02), m["trim"], bevel=0.005))
    for s in (-1, 1):
        parts.append(box("WingEnd", (s * 0.86, y - 0.02, -0.06), (0.03, 0.16, 0.36), m["paint"], bevel=0.01))
        parts.append(beam("WingStrut", (s * 0.48, -0.02, 0.02), (s * 0.48, y - 0.02, -0.06), 0.05, 0.10, m["trim"]))
    return join(parts[0], parts[1:])


def build_parts():
    reset_scene()
    os.makedirs(PREVIEW, exist_ok=True)
    m = materials("M_Paint_Red", "#FF4B2B")
    made = []
    rim_styles = [("Rim_Five", "five", "rim"), ("Rim_Ten", "ten", "rimdark"), ("Rim_Six", "six", "rimwhite"),
                  ("Rim_Dish", "dish", "chrome"), ("Rim_Mesh", "mesh", "rim")]
    for i, (name, style, matkey) in enumerate(rim_styles):
        root = empty(name)
        ob = rim(name + "_Mesh", style, m[matkey], m)
        set_parent(ob, root)
        export_fbx(root, os.path.join(PARTS, name + ".fbx"))
        root.location = C(i * 0.9 - 1.8, 0.5, 0)
        made.append(root)
        for x in [root] + all_children(root):
            x.name = x.name + "_done"
    for i, (name, fn) in enumerate([("Spoiler_Lip", spoiler_lip), ("Spoiler_Duck", spoiler_duck), ("Spoiler_GT", spoiler_gt)]):
        root = empty(name)
        ob = fn(m)
        ob.name = name + "_Mesh"
        set_parent(ob, root)
        export_fbx(root, os.path.join(PARTS, name + ".fbx"))
        root.location = C(i * 2.0 - 2.0, 0.3, -1.6)
        made.append(root)
        for x in [root] + all_children(root):
            x.name = x.name + "_done"
    render_preview(os.path.join(PREVIEW, "parts.png"), (0, 0.8, 0.5), 7.0, 3.0, 20, lens=50)
    print("parts:", len(made))


build_parts()
