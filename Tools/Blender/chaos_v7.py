# ГИПЕРГАМИЯ (шоу-событие v7): огромная «карточка» с картинкой пользователя, парящая над дорогой.
# Запуск: py -3 -I Tools/Blender/blender_client.py Tools/Blender/rc_lib.py Tools/Blender/chaos_lib.py Tools/Blender/chaos_v7.py
# Координаты как в Unity: x — вправо, y — вверх, z — «лицо» карточки. Начало координат — низ карточки по центру.
#
# Модель — только рама: золотая рамка, розовая подложка, сердечки по верхним углам, звёздочки. Саму картинку
# (Assets/Art/Textures/Events/hypergamy.jpg, 828×979) сборщик префаба кладёт двумя плоскостями поверх подложки
# с обеих сторон (PictureFront/PictureBack) — так она читается без зеркала с любой стороны. Окно под картинку —
# 3.4 × 4.02 м (пропорции картинки), от y = 0.22 до 4.24, плоскость подложки ±0.08 по z.

PIC_W, PIC_H = 3.4, 4.02
BAR = 0.22
DEPTH = 0.26


def hyper_materials(m):
    m.update(dict(
        hFrame=mat("M_HypergamyFrame", "#FFC83D", metallic=0.55, roughness=0.3),
        hBack=mat("M_HypergamyBack", "#FF4F9A", roughness=0.45),
        hHeart=mat("M_HypergamyHeart", "#FF2D55", roughness=0.3),
        hStar=mat("M_HypergamyStar", "#FFF1A8", emission="#FFE45C", strength=1.5),
    ))
    return m


def extruded(name, points, center, thickness, material):
    """Плоская фигура (точки x, y в плоскости карточки), выдавленная по z на thickness, центр — center (Unity)."""
    bm = bmesh.new()
    cx, cy, cz = center
    front = [bm.verts.new(C(cx + x, cy + y, cz + thickness / 2)) for x, y in points]
    back = [bm.verts.new(C(cx + x, cy + y, cz - thickness / 2)) for x, y in points]
    bm.faces.new(front)
    bm.faces.new(list(reversed(back)))
    n = len(points)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new([front[i], back[i], back[j], front[j]])
    ob = obj_from_bmesh(name, bm, [material])
    return ob


def heart_points(size, count=40):
    pts = []
    for k in range(count):
        t = k / count * math.tau
        x = 16 * math.sin(t) ** 3
        y = 13 * math.cos(t) - 5 * math.cos(2 * t) - 2 * math.cos(3 * t) - math.cos(4 * t)
        pts.append((x / 17 * size, (y + 2) / 17 * size))
    return pts


def star_points(outer, inner, rays=4):
    pts = []
    for k in range(rays * 2):
        a = math.pi / 2 + k * math.pi / rays
        r = outer if k % 2 == 0 else inner
        pts.append((math.cos(a) * r, math.sin(a) * r))
    return pts


def card(m):
    outer_w = PIC_W + 2 * BAR
    outer_h = PIC_H + 2 * BAR
    parts = []
    # Рама из четырёх скруглённых брусьев и подложка между картинками.
    parts.append(box("FrameBottom", (0, BAR / 2, 0), (outer_w, BAR, DEPTH), m["hFrame"], bevel=0.06, segments=3))
    parts.append(box("FrameTop", (0, outer_h - BAR / 2, 0), (outer_w, BAR, DEPTH), m["hFrame"], bevel=0.06, segments=3))
    for s in (-1, 1):
        parts.append(box("FrameSide", (s * (outer_w - BAR) / 2, outer_h / 2, 0), (BAR, outer_h, DEPTH), m["hFrame"], bevel=0.06, segments=3))
    parts.append(box("Plate", (0, outer_h / 2, 0), (PIC_W + 0.04, PIC_H + 0.04, 0.16), m["hBack"]))
    # Сердечки по верхним углам и маленькое — посередине низа, с обеих сторон рамы.
    for s in (-1, 1):
        parts.append(extruded("Heart", heart_points(0.48), (s * outer_w / 2, outer_h - 0.12, 0), 0.16, m["hHeart"]))
    for zs in (-1, 1):
        parts.append(extruded("HeartSmall", heart_points(0.22), (0, 0.02, zs * (DEPTH / 2 + 0.02)), 0.05, m["hHeart"]))
    # Звёздочки-блёстки у верхних углов (светятся: видно ночью).
    for s, dy, size in ((-1, 0.55, 0.32), (1, 0.75, 0.24), (-1, -0.9, 0.2)):
        parts.append(extruded("Star", star_points(size, size * 0.32), (s * (outer_w / 2 + 0.38), outer_h + dy - 0.6, 0), 0.06, m["hStar"]))
    return export_event("HypergamyCard", parts)


reset_scene()
m = hyper_materials(chaos_materials())
root, tris = card(m)
for ob in bpy.data.objects:
    ob.hide_render = ob.type == 'MESH' and ob.name != "RC_Ground"
for c in all_children(root):
    c.hide_render = False
root.location = C(0, 0.6, 0)
# Цели — в координатах Blender (Z вверх).
render_preview(os.path.join(PREVIEW, "v7_hypergamy_front.png"), (0, 0, 2.8), 11, 1.5, 0, lens=40)
render_preview(os.path.join(PREVIEW, "v7_hypergamy_side.png"), (0, 0, 2.8), 11, 2.5, 60, lens=40)
print(f"HypergamyCard: {tris} tris")
