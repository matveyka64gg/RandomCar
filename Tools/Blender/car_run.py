# Какие машины строить в этом запуске (меняется под задачу).
BUILD_KEYS = ["Iskra", "Bastion", "Kometa", "Pchelka", "Myata", "Grom", "Sapsan", "Bizon", "Taiga", "Lastochka"]
EXPORT = True

reset_scene()
report = []
for key in BUILD_KEYS:
    root = build_car(key)
    tris = sum(tri_count(o) for o in all_children(root))
    report.append(f"{key}: {tris} tris, objects {len(all_children(root))}")
    preview_car(key, root)
    if EXPORT:
        export_car(key, root)
    # Следующая машина строится в пустой сцене (кроме студии).
    for ob in [root] + all_children(root):
        ob.hide_render = True
print("\n".join(report))
