# Модели хаоса v4: шары, мины, молоты, карусель, бочки и кости, вышка-бочкопад, стены, перчатки, батуты,
# лёд с пингвином, желе, арки невесомости и уменьшателя, кольца-рывки.
# Запуск: py -3 -I Tools/Blender/blender_client.py Tools/Blender/rc_lib.py Tools/Blender/chaos_lib.py Tools/Blender/chaos_v4.py
# Координаты как в Unity: x — вправо, y — вверх, z — «вперёд» объекта.
# Физические объекты (шары, бочки, кости, кольца) — начало координат в центре; остальные — у основания.


# ---------- шары ----------

def ball_metal(m):
    R = 1.2
    ob = sphere("Ball", (0, 0, 0), R, [m["chrome"], m["rivet"]], lambda x, y, z: 1 if abs(y) < 0.12 else 0, u=40, v=20)
    parts = [ob]
    for k in range(12):
        a = k / 12 * math.tau
        parts.append(sphere("Rivet", (math.cos(a) * R * 1.0, 0, math.sin(a) * R * 1.0), 0.09, [m["chrome"]], u=8, v=6))
    return export_event("Ball_Metal", parts)


def ball_rubber(m):
    R = 1.3
    ob = sphere("Ball", (0, 0, 0), R, [m["rubA"], m["rubB"]], lambda x, y, z: 1 if int((math.atan2(z, x) + math.pi) / (math.tau / 8)) % 2 == 0 and abs(y) < R * 0.75 else 0, u=40, v=20)
    parts = [ob]
    # Мультяшная мордочка: глаза и улыбка — шар катится и «смотрит».
    for s in (-1, 1):
        parts.append(sphere("Eye", (s * 0.42, 0.35, R * 0.93), 0.26, [m["white"]], u=14, v=8, scale=(1, 1.2, 0.5)))
        parts.append(sphere("Pupil", (s * 0.42, 0.33, R * 1.03), 0.12, [m["black"]], u=10, v=6, scale=(1, 1.2, 0.5)))
    for k in range(7):
        a = math.radians(-60 + k * 20)
        parts.append(sphere("Smile", (math.sin(a) * 0.55, -0.25 - math.cos(a) * 0.18, R * 0.97), 0.07, [m["black"]], u=8, v=6))
    return export_event("Ball_Rubber", parts)


def ball_mini(m):
    R = 0.9
    ob = sphere("Ball", (0, 0, 0), R, [m["mini"], m["white"]], lambda x, y, z: 1 if abs(y) < 0.1 or abs(x) < 0.1 else 0, u=28, v=14)
    return export_event("Ball_Mini", [ob])


def ball_giant(m):
    R = 1.0
    ob = sphere("Ball", (0, 0, 0), R, [m["giantA"], m["giantB"], m["white"]],
                lambda x, y, z: 2 if abs(y) > R * 0.92 else (int((math.atan2(z, x) + math.pi) / (math.tau / 10)) % 2), u=48, v=24)
    parts = [ob]
    rnd = random.Random(3)
    for k in range(10):
        th = rnd.uniform(0, math.tau)
        ph = rnd.uniform(-0.9, 0.9)
        p = (math.cos(th) * math.cos(ph), math.sin(ph), math.sin(th) * math.cos(ph))
        parts.append(sphere("Spot", tuple(c * 0.985 for c in p), 0.09, [m["white"]], u=10, v=6))
    return export_event("Ball_Giant", parts)


# ---------- мины ----------

def mine(m):
    parts = [cylinder("Base", (0, 0.06, 0), 0.62, 0.12, 'y', m["rivet"], segments=24, bevel=0.03)]
    parts.append(cylinder("Stripe", (0, 0.13, 0), 0.6, 0.05, 'y', m["yellow"], segments=24))
    dome = sphere("Dome", (0, 0.16, 0), 0.55, [m["mine"]], u=24, v=12, scale=(1, 0.62, 1))
    parts.append(dome)
    for k in range(6):
        a = k / 6 * math.tau
        base = (math.cos(a) * 0.42, 0.32, math.sin(a) * 0.42)
        tip = (math.cos(a) * 0.72, 0.48, math.sin(a) * 0.72)
        parts.append(cone("Spike", base, tip, 0.08, m["spike"]))
    parts.append(cylinder("LampBase", (0, 0.52, 0), 0.1, 0.06, 'y', m["rivet"], segments=12))
    lamp = sphere("Lamp", (0, 0.6, 0), 0.12, [m["lamp"]], u=14, v=8)
    return export_event("Mine", parts, {"Lamp": (lamp, (0, 0.6, 0))})


# ---------- молоты и карусель ----------

def hammer(m):
    # Начало координат — точка подвеса; рукоять вниз, боёк поперёк дороги (ось x).
    parts = [cylinder("Hinge", (0, 0, 0), 0.32, 0.9, 'z', m["steel"], segments=16)]
    parts.append(cylinder("Handle", (0, -3.6, 0), 0.2, 6.8, 'y', m["wood"], segments=12))
    parts.append(cylinder("Head", (0, -7.6, 0), 1.15, 3.2, 'x', m["head"], segments=28, bevel=0.06))
    for s in (-1, 1):
        parts.append(cylinder("Band", (s * 1.35, -7.6, 0), 1.2, 0.28, 'x', m["yellow"], segments=28))
        parts.append(cylinder("Face", (s * 1.62, -7.6, 0), 0.95, 0.06, 'x', m["white"], segments=28))
    return export_event("Hammer", parts)


def gantry(m):
    # Стойка 10 м и балка единичной длины по x (масштабируется под ширину дороги).
    p = [box("Pillar", (0, 5.0, 0), (0.7, 10.0, 0.7), m["rivet"], bevel=0.04)]
    for k in range(6):
        p.append(box("Stripe", (0, 0.6 + k * 1.6, 0), (0.74, 0.6, 0.74), m["yellow"] if k % 2 == 0 else m["black"]))
    p.append(box("Foot", (0, 0.15, 0), (1.4, 0.3, 1.4), m["rivet"], bevel=0.05))
    p.append(sphere("Lamp", (0, 10.2, 0), 0.25, [m["lampw"]], u=12, v=8))
    pillar = export_event("GantryPillar", p)
    beam_parts = [box("Beam", (0, 0, 0), (1.0, 0.8, 0.9), m["head"], bevel=0.0)]
    beam_parts.append(box("Trim", (0, -0.42, 0), (1.0, 0.08, 0.95), m["yellow"]))
    beam = export_event("GantryBeam", beam_parts)
    return pillar, beam


def spinner(m):
    parts = [cylinder("Post", (0, 0.8, 0), 0.55, 1.6, 'y', m["carnA"], segments=24, bevel=0.04)]
    for k in range(4):
        parts.append(cylinder("Ring", (0, 0.25 + k * 0.4, 0), 0.58, 0.12, 'y', m["white"], segments=24))
    parts.append(cone("Cap", (0, 1.6, 0), (0, 2.6, 0), 0.8, m["carnB"], segments=24))
    parts.append(sphere("Top", (0, 2.7, 0), 0.2, [m["yellow"]], u=12, v=8))
    parts.append(cylinder("Base", (0, 0.08, 0), 1.0, 0.16, 'y', m["rivet"], segments=24))
    base = export_event("SpinnerBase", parts)
    arms = []
    for k in range(3):
        a = k / 3 * math.tau
        d = (math.cos(a), 0, math.sin(a))
        for t in range(5):
            r0, r1 = 0.6 + t * 0.9, 0.6 + (t + 1) * 0.9
            col = m["pad"] if t % 2 == 0 else m["white"]
            arms.append(beam("Arm", (d[0] * r0, 0.75, d[2] * r0), (d[0] * r1, 0.75, d[2] * r1), 0.55, 0.7, col, bevel=0.08))
        arms.append(cylinder("Bumper", (d[0] * 5.1, 0.75, d[2] * 5.1), 0.42, 0.9, 'y', m["carnB"], segments=16, bevel=0.06))
    arms.append(cylinder("Hub", (0, 0.75, 0), 0.75, 0.9, 'y', m["yellow"], segments=24, bevel=0.05))
    arm = export_event("SpinnerArms", arms)
    return base, arm


# ---------- бочки, кости, вышка ----------

def barrel(m, name, body, label):
    R, H = 0.5, 1.2
    rings = []
    for k in range(9):
        y = -H / 2 + H * k / 8
        r = R * (1 + 0.12 * math.sin(math.pi * k / 8))
        rings.append([(r * math.cos(a), y, r * math.sin(a)) for a in [i / 20 * math.tau for i in range(20)]])
    ob = obj_from_bmesh("Staves", loft("Staves", rings, lambda i, j: 0), [body])
    smooth(ob, 40)
    parts = [ob]
    for y in (-0.42, 0.42, 0.0 if label else None):
        if y is None:
            continue
        r = R * (1 + 0.12 * math.sin(math.pi * (y + H / 2) / H)) + 0.02
        parts.append(cylinder("Hoop", (0, y, 0), r, 0.08 if label is None else 0.06, 'y', m["steel"] if label is None else m["yellow"], segments=20))
    if label:
        for s in (-1, 1):
            t = text_mesh("Label", label, 0.34, m["yellow"], extrude=0.01)
            t.rotation_euler = (math.radians(90), 0, math.radians(0 if s > 0 else 180))
            t.location = C(0, 0.2, s * (R * 1.12 + 0.02))
            bpy.context.view_layer.update()
            parts.append(t)
    return export_event(name, parts)


def dice(m):
    S = 1.4
    parts = [box("Cube", (0, 0, 0), (S, S, S), m["white"], bevel=0.18, segments=4)]
    h = S / 2 + 0.005
    q = S * 0.26

    def pip(p):
        parts.append(sphere("Pip", p, 0.13, [m["black"]], u=10, v=6, scale=(1, 1, 1)))
    # 1 сверху, 6 снизу, 2/5 спереди/сзади, 3/4 по бокам.
    pip((0, h, 0))
    for x in (-q, q):
        for z in (-q, 0, q):
            pip((x, -h, z))
    for p in ((-q, q), (q, -q)):
        pip((p[0], p[1], h))
    for p in ((-q, q), (q, q), (0, 0), (-q, -q), (q, -q)):
        pip((p[0], p[1], -h))
    for p in ((-q, q), (0, 0), (q, -q)):
        pip((h, p[0], p[1]))
    for p in ((-q, q), (q, q), (-q, -q), (q, -q)):
        pip((-h, p[0], p[1]))
    return export_event("Dice", parts)


def barrel_tower(m):
    parts = []
    for sx in (-1, 1):
        for sz in (-1, 1):
            parts.append(beam("Leg", (sx * 0.9, 0, sz * 0.9), (sx * 0.7, 6.0, sz * 0.7), 0.22, 0.22, m["wood"]))
    for y in (1.8, 3.8):
        for (a, b) in (((-0.8, -0.8), (0.8, -0.8)), ((0.8, -0.8), (0.8, 0.8)), ((0.8, 0.8), (-0.8, 0.8)), ((-0.8, 0.8), (-0.8, -0.8))):
            parts.append(beam("Brace", (a[0], y, a[1]), (b[0], y, b[1]), 0.12, 0.12, m["woodlight"]))
    parts.append(box("Deck", (0, 6.05, 0), (2.2, 0.16, 2.2), m["woodlight"], bevel=0.02))
    parts.append(box("Sign", (0, 7.6, -1.0), (1.8, 0.7, 0.08), m["yellow"], bevel=0.02))
    parts.append(beam("Mast", (-1.0, 6.0, -1.0), (-1.0, 8.6, -1.0), 0.1, 0.1, m["rivet"]))
    parts.append(box("Flag", (-0.6, 8.3, -1.0), (0.8, 0.5, 0.04), m["head"]))
    # Ковш: открытый ящик, ось поворота — край к дороге (+x), опрокидывается в сторону дороги.
    b = []
    b.append(box("Floor", (0, 6.3, 0), (1.9, 0.12, 1.9), m["orange"], bevel=0.02))
    for s in (-1, 1):
        b.append(box("WallZ", (0, 6.75, s * 0.92), (1.9, 0.9, 0.1), m["orange"], bevel=0.02))
    b.append(box("WallX", (-0.92, 6.75, 0), (0.1, 0.9, 1.9), m["orange"], bevel=0.02))
    b.append(box("Lip", (0.92, 6.45, 0), (0.1, 0.3, 1.9), m["yellow"], bevel=0.02))
    bin_ob = join(b[0], b[1:])
    return export_event("BarrelTower", parts, {"Bin": (bin_ob, (0.95, 6.3, 0))})


# ---------- стены, перчатки ----------

def slide_wall(m):
    # Сегмент 4 м по x, высота 1.3: мягкая стена с шевронами и лампами.
    parts = [box("Core", (0, 0.65, 0), (4.0, 1.3, 0.8), m["pad"], bevel=0.12, segments=3)]
    for k in range(4):
        x = -1.5 + k
        parts.append(box("Chevron", (x, 0.65, -0.42), (0.45, 0.9, 0.06), m["white"] if k % 2 == 0 else m["yellow"]))
        parts.append(box("ChevronB", (x, 0.65, 0.42), (0.45, 0.9, 0.06), m["white"] if k % 2 == 0 else m["yellow"]))
    for x in (-1.6, 0, 1.6):
        parts.append(cylinder("LampBase", (x, 1.36, 0), 0.12, 0.08, 'y', m["rivet"], segments=10))
        parts.append(sphere("Lamp", (x, 1.48, 0), 0.14, [m["lampw"]], u=10, v=6))
    return export_event("SlideWall", parts)


def kicker(m):
    parts = [box("Post", (0, 0.6, 0), (0.3, 1.2, 0.3), m["rivet"])]
    parts.append(box("Box", (0, 1.45, 0), (1.3, 1.0, 1.3), m["yellow"], bevel=0.06))
    for k in range(3):
        parts.append(box("Stripe", (0, 1.15 + k * 0.3, -0.66), (1.32, 0.12, 0.02), m["black"]))
    parts.append(box("Hole", (0.66, 1.45, 0), (0.04, 0.7, 0.7), m["black"]))
    parts.append(sphere("Lamp", (0, 2.05, 0), 0.16, [m["lamp"]], u=12, v=8))
    box_root = export_event("KickerBox", parts)
    # Пружина единичной длины по +x (растягивается масштабом).
    sp = []
    for k in range(9):
        sp.append(cylinder("Coil", (0.06 + k * 0.11, 0, 0), 0.24, 0.035, 'x', m["steel"], segments=16))
    sp.append(cylinder("Rod", (0.5, 0, 0), 0.06, 1.0, 'x', m["steel"], segments=8))
    spring = export_event("KickerSpring", sp)
    g = [sphere("Fist", (0.45, 0, 0), 0.5, [m["glove"]], u=20, v=12, scale=(1.1, 0.95, 0.9))]
    g.append(sphere("Thumb", (0.4, 0.35, 0.3), 0.2, [m["glove"]], u=12, v=8, scale=(1.4, 0.8, 0.8)))
    g.append(cylinder("Cuff", (0.0, 0, 0), 0.4, 0.3, 'x', m["white"], segments=18, bevel=0.04))
    g.append(box("Lace", (0.1, 0.33, 0), (0.25, 0.06, 0.1), m["white"]))
    glove = export_event("KickerGlove", g)
    return box_root, spring, glove


# ---------- батут, лёд, желе ----------

def trampoline(m):
    parts = [torus("Frame", (0, 0.55, 0), 1.65, 0.16, 'y', m["blue"], seg=40, ring=10)]
    for k in range(6):
        a = k / 6 * math.tau + 0.3
        parts.append(beam("Leg", (math.cos(a) * 1.6, 0.0, math.sin(a) * 1.6), (math.cos(a) * 1.62, 0.5, math.sin(a) * 1.62), 0.12, 0.12, m["rivet"]))
    for k in range(16):
        a = k / 16 * math.tau
        parts.append(beam("Spring", (math.cos(a) * 1.25, 0.52, math.sin(a) * 1.25), (math.cos(a) * 1.55, 0.55, math.sin(a) * 1.55), 0.05, 0.05, m["steel"]))
    pad = []
    pad.append(cylinder("Mat", (0, 0.52, 0), 1.25, 0.06, 'y', m["trampmat"], segments=32))
    pad.append(torus("Ring", (0, 0.56, 0), 0.7, 0.05, 'y', m["teal"], seg=28, ring=6))
    mat_ob = join(pad[0], pad[1:])
    return export_event("Trampoline", parts, {"Mat": (mat_ob, (0, 0.52, 0))})


def ice_tile(m):
    parts = [box("Slab", (0, 0.03, 0), (1.0, 0.06, 1.0), m["ice"], bevel=0.02)]
    rnd = random.Random(5)
    for k in range(5):
        x, z = rnd.uniform(-0.38, 0.38), rnd.uniform(-0.38, 0.38)
        parts.append(box("Shine", (x, 0.061, z), (rnd.uniform(0.05, 0.12), 0.004, rnd.uniform(0.18, 0.35)), m["white"]))
    parts.append(box("Deep", (0.15, 0.062, -0.2), (0.3, 0.003, 0.22), m["iceDeep"]))
    return export_event("IceTile", parts)


def penguin(m):
    parts = [sphere("Body", (0, 0.55, 0), 0.38, [m["black"]], u=20, v=12, scale=(1, 1.45, 0.95))]
    parts.append(sphere("Belly", (0, 0.5, -0.17), 0.3, [m["white"]], u=18, v=10, scale=(0.95, 1.35, 0.7)))
    parts.append(sphere("Head", (0, 1.08, 0), 0.26, [m["black"]], u=16, v=10))
    for s in (-1, 1):
        parts.append(sphere("Eye", (s * 0.09, 1.13, -0.21), 0.06, [m["white"]], u=8, v=6))
        parts.append(sphere("Pupil", (s * 0.09, 1.13, -0.255), 0.03, [m["black"]], u=6, v=4))
        parts.append(sphere("Wing", (s * 0.38, 0.6, 0), 0.12, [m["black"]], u=10, v=6, scale=(0.5, 2.4, 1)))
        parts.append(sphere("Foot", (s * 0.13, 0.03, -0.12), 0.1, [m["beak"]], u=10, v=6, scale=(1, 0.35, 1.6)))
    parts.append(cone("Beak", (0, 1.05, -0.22), (0, 1.03, -0.42), 0.07, m["beak"]))
    return export_event("Penguin", parts)


def jelly_block(m):
    parts = [box("Jelly", (0, 0.25, 0), (1.0, 0.5, 1.0), m["jelly"], bevel=0.16, segments=4)]
    parts.append(box("Hi", (-0.2, 0.505, -0.2), (0.25, 0.01, 0.12), m["jellyHi"]))
    return export_event("JellyBlock", parts)


# ---------- арки и кольца ----------

def arch(m):
    p = [cylinder("Post", (0, 3.5, 0), 0.35, 7.0, 'y', m["arch"], segments=16, bevel=0.03)]
    for k in range(5):
        p.append(torus("Glow", (0, 1.0 + k * 1.3, 0), 0.5, 0.07, 'y', m["glow"], seg=24, ring=6))
    p.append(sphere("Orb", (0, 7.3, 0), 0.45, [m["glow"]], u=16, v=10))
    p.append(cylinder("Foot", (0, 0.1, 0), 0.7, 0.2, 'y', m["rivet"], segments=16))
    post = export_event("ArchPost", p)
    b = [cylinder("Tube", (0, 0, 0), 0.22, 1.0, 'x', m["glow"], segments=12)]
    b.append(cylinder("Shell", (0, 0, 0), 0.3, 1.0, 'x', m["arch"], segments=12))
    beam_root = export_event("ArchBeam", [b[1], b[0]])
    return post, beam_root


def boost_ring(m):
    parts = [torus("Ring", (0, 0, 0), 2.0, 0.2, 'z', m["gold"], seg=48, ring=12)]
    for k in range(8):
        a = k / 8 * math.tau
        parts.append(sphere("Gem", (math.cos(a) * 2.0, math.sin(a) * 2.0, 0), 0.26, [m["teal"]], u=10, v=6))
    return export_event("BoostRing", parts)


reset_scene()
m = chaos_materials()
results = []
for fn in (ball_metal, ball_rubber, ball_mini, ball_giant, mine, hammer, dice, barrel_tower, slide_wall,
           trampoline, ice_tile, penguin, jelly_block, boost_ring):
    results.append(fn(m))
results += list(gantry(m))
results += list(spinner(m))
results.append(barrel(m, "Barrel", m["woodlight"], None))
results.append(barrel(m, "BarrelTNT", m["tnt"], "БАХ"))
results += list(kicker(m))
results += list(arch(m))

for ob in bpy.data.objects:
    ob.hide_render = ob.type == 'MESH' and ob.name != "RC_Ground"
cols = 7
for idx, (root, tris) in enumerate(results):
    x = (idx % cols) * 4.0 - 12
    z = (idx // cols) * 5.0
    root.location = C(x, 0, z)
    for c in all_children(root):
        c.hide_render = False
render_preview(os.path.join(PREVIEW, "chaos_v4.png"), (0, 1.5, 6), 30, 14, 200, lens=35)
for root, tris in results:
    print(f"{root.name}: {tris} tris")
