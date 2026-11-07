# Превью обвеса: каждый вариант на машине отдельно (остальные слоты — заводские). Запуск после car_run.py
# с EXPORT = False (машина остаётся в сцене). Кадры: %TEMP%/randomcar/blender/v2/kits/<Машина>_<Слот>_<Вариант>.png

def kit_objects(root):
    return [o for o in all_children(root) if o.name.startswith("Kit_")]


def show_variant(root, slot, variant):
    for o in kit_objects(root):
        parts = o.name.split("_")
        s, v = parts[1], parts[2]
        if s == slot:
            o.hide_render = v != variant
        else:
            o.hide_render = v != "Stock"


def preview_kits(key):
    root = bpy.data.objects.get("Car_" + key)
    for o in [root] + all_children(root):
        o.hide_render = False
    base = os.path.join(PREVIEW, "v2", "kits")
    os.makedirs(base, exist_ok=True)
    scene = studio()
    scene.render.resolution_x = 640
    scene.render.resolution_y = 400
    done = []
    for o in kit_objects(root):
        parts = o.name.split("_")
        slot, variant = parts[1], parts[2]
        if variant == "Stock":
            continue
        show_variant(root, slot, variant)
        rear = slot in ("RearBumper", "Spoiler", "Roof")
        yaw = 150 if rear else 35
        shot(os.path.join(base, f"{key}_{slot}_{variant}.png"), (0, 0.65, 0.0), 6.6, 1.6, yaw, lens=45)
        done.append(f"{slot}_{variant}")
    show_variant(root, "", "")
    scene.render.resolution_x = 1280
    scene.render.resolution_y = 720
    print(key, len(done), done)


for key in BUILD_KEYS:
    preview_kits(key)
