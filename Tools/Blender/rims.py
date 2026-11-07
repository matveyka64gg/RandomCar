# Общие диски гаража v2: Assets/Art/Models/Parts/Rim_<Стиль>.fbx. Строятся в осях колеса (наружу +x)
# под радиус посадки шины 0.24 м и ширину 0.24 м; в Unity подгоняются масштабом под колесо машины.
# Запуск: blender_client.py rc_lib.py car_body.py car_parts.py rims.py

PARTS = os.path.join(ART, "Parts")
RIM_EXPORT = ["Five", "Ten", "Six", "Split", "Multi", "Turbofan", "Dish", "Mesh", "Beadlock", "Steel"]


def build_rims():
    reset_scene()
    m = car_materials()
    made = []
    for i, style in enumerate(RIM_EXPORT):
        root = empty("Rim_" + style)
        ob = rim("Rim_" + style + "_Mesh", style.lower(), 0.24, 0.24, m)
        triangulate_ngons(ob)
        set_parent(ob, root)
        export_fbx(root, os.path.join(PARTS, f"Rim_{style}.fbx"))
        root.location = C((i % 5) * 0.62 - 1.24, 0.3 + (i // 5) * 0.62, 0)
        made.append(root)
        for x in [root] + all_children(root):
            x.name = x.name + "_done"
    shot(os.path.join(PREVIEW, "v2", "rims.png"), (0, 0.6, 0), 4.0, 0.0, 90, lens=50)
    print("rims:", len(made))


def triangulate_ngons(ob):
    bm = bmesh.new()
    bm.from_mesh(ob.data)
    bmesh.ops.triangulate(bm, faces=[f for f in bm.faces if len(f.verts) > 4])
    bm.to_mesh(ob.data)
    bm.free()


build_rims()
