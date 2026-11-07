# Пробный запуск генератора: одна или несколько машин без экспорта, быстрые превью (Tools/Blender/car_try.py).
TRY_KEYS = ["Iskra", "Kometa", "Pchelka", "Myata", "Bastion", "Bizon", "Taiga", "Lastochka", "Grom", "Sapsan"]
reset_scene()
report = []
for key in TRY_KEYS:
    root = build_car(key)
    tris = sum(tri_count(o) for o in all_children(root) if not o.hide_render)
    report.append(f"{key}: {tris} tris visible, objects {len(all_children(root))}")
    preview_quick(key, root)
    # Как после экспорта: имена деталей с суффиксом машины (иначе следующая получит .001).
    for ob in all_children(root):
        ob.name = ob.name + "_" + key
    root.name = root.name + "_done"
    for ob in [root] + all_children(root):
        ob.hide_render = True
print("\n".join(report))
