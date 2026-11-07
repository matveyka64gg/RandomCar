# Генератор машин v2: обвес и детали тюнинга под конкретный кузов.
# Каждая деталь строится по поверхности этой машины (лучи BVH, профили), поэтому прилегает к кузову без
# зазоров и пересечений. Экспорт: объект Kit_<Слот>_<Вариант> в корне машины; сборщик префабов в Unity
# раскладывает их по Kit/<Слот>/<Вариант>, в игре включается один вариант на слот.
#
# Совместимость задаётся классом машины (KIT_RULES): обвес внедорожника не строится для купе и наоборот.

KIT_RULES = {
    "sport":    {"FrontBumper": ["Sport", "Race"], "RearBumper": ["Diffuser"], "Skirts": ["Sport"], "Fenders": ["Wide"],
                 "Hood": ["Vents", "Scoop", "Carbon"], "Roof": ["Scoop"], "Mirrors": ["Aero"], "Grille": ["Mesh"],
                 "Spoiler": ["Lip", "Ducktail", "Wing", "Swan"], "Lights": []},
    "sedan":    {"FrontBumper": ["Sport", "Race"], "RearBumper": ["Diffuser"], "Skirts": ["Sport"], "Fenders": ["Wide"],
                 "Hood": ["Vents", "Carbon"], "Roof": ["Rails"], "Mirrors": ["Aero"], "Grille": ["Mesh", "Bars"],
                 "Spoiler": ["Lip", "Ducktail", "Wing"], "Lights": []},
    "compact":  {"FrontBumper": ["Sport"], "RearBumper": ["Diffuser"], "Skirts": ["Sport"], "Fenders": ["Wide", "Offroad"],
                 "Hood": ["Vents", "Scoop"], "Roof": ["Rails", "Scoop"], "Mirrors": ["Aero"], "Grille": ["Mesh"],
                 "Spoiler": ["Roof", "Rally"], "Lights": ["Rally"]},
    "rally":    {"FrontBumper": ["Sport", "Offroad"], "RearBumper": ["Diffuser", "Offroad"], "Skirts": ["Sport", "Sliders"],
                 "Fenders": ["Wide", "Offroad"], "Hood": ["Vents", "Scoop", "Carbon"], "Roof": ["Scoop", "Rack", "Lightbar"],
                 "Mirrors": ["Aero"], "Grille": ["Mesh"], "Spoiler": ["Roof", "Rally"], "Lights": ["Rally", "Pods"]},
    "luxsuv":   {"FrontBumper": ["Offroad"], "RearBumper": ["Offroad"], "Skirts": ["Sliders"], "Fenders": ["Offroad"],
                 "Hood": ["Carbon"], "Roof": ["Rack", "Expedition", "Lightbar"], "Mirrors": ["Aero"], "Grille": ["Mesh"],
                 "Spoiler": ["Roof"], "Lights": ["Pods"], "Guard": ["Bullbar"]},
    "offroad":  {"FrontBumper": ["Offroad"], "RearBumper": ["Offroad"], "Skirts": ["Sliders"], "Fenders": ["Offroad"],
                 "Hood": ["Vents", "Scoop"], "Roof": ["Expedition", "Lightbar"], "Mirrors": ["Aero"], "Grille": ["Mesh"],
                 "Spoiler": [], "Lights": ["Pods", "Rally"], "Guard": ["Bullbar"]},
    "muscle":   {"FrontBumper": ["Sport"], "RearBumper": ["Diffuser"], "Skirts": ["Sport"], "Fenders": ["Wide"],
                 "Hood": ["Scoop", "Vents", "Carbon"], "Roof": [], "Mirrors": ["Aero"], "Grille": ["Mesh", "Bars"],
                 "Spoiler": ["Lip", "Ducktail", "Wing"], "Lights": []},
    "classic":  {"FrontBumper": [], "RearBumper": [], "Skirts": [], "Fenders": ["Wide"],
                 "Hood": ["Scoop"], "Roof": ["Rails"], "Mirrors": ["Aero"], "Grille": ["Mesh"],
                 "Spoiler": ["Lip", "Ducktail"], "Lights": ["Rally"]},
    "super":    {"FrontBumper": ["Race"], "RearBumper": ["Diffuser"], "Skirts": ["Sport"], "Fenders": [],
                 "Hood": ["Vents", "Carbon"], "Roof": ["Scoop"], "Mirrors": ["Aero"], "Grille": [],
                 "Spoiler": ["Lip", "Wing", "Swan"], "Lights": []},
    "hyper":    {"FrontBumper": ["Race"], "RearBumper": ["Diffuser"], "Skirts": ["Sport"], "Fenders": [],
                 "Hood": ["Vents", "Carbon"], "Roof": [], "Mirrors": ["Aero"], "Grille": [],
                 "Spoiler": ["Wing", "Swan"], "Lights": []},
}


# ---------- вспомогательное ----------

def side_hit(surface, z, y, side, depth=3.0):
    """Точка на боку кузова (side = +1 справа, -1 слева) на высоте y в сечении z."""
    p, n = surface.hit((side * depth, y, z), (-side, 0.0, 0.0), depth * 2)
    return p, n


def top_hit(surface, x, z, height=4.0):
    p, n = surface.hit((x, height, z), (0.0, -1.0, 0.0), height * 2)
    return p, n


def tube(name, p0, p1, r, material, segments=12):
    """Труба между двумя точками (каркасы, кенгурятник, багажник)."""
    a, b = C(*p0), C(*p1)
    length = (b - a).length
    bm = bmesh.new()
    bmesh.ops.create_cone(bm, cap_ends=True, cap_tris=False, segments=segments, radius1=r, radius2=r, depth=length)
    rot = Vector((0, 0, 1)).rotation_difference((b - a).normalized()).to_matrix()
    bmesh.ops.rotate(bm, verts=bm.verts, cent=Vector(), matrix=rot)
    bmesh.ops.translate(bm, vec=(a + b) / 2, verts=bm.verts)
    ob = obj_from_bmesh(name, bm, [material])
    smooth(ob, 50)
    return ob


def blade(name, pts, material, out=0.05, thick=0.02, drop=0.0, inset=0.03):
    """Лента-лезвие по точкам (p, n) поверхности: губа, порог, сплиттер."""
    bm = bmesh.new()
    rows = []
    for (p, n) in pts:
        nn = Vector((n.x, 0.0, n.z))
        if nn.length < 1e-4:
            nn = Vector((0, 0, 1))
        nn.normalize()
        ring = []
        for (o, dy) in ((-inset, 0.0), (out, -drop), (out, -drop - thick), (-inset, -thick)):
            q = p + nn * o + Vector((0, dy, 0))
            ring.append(bm.verts.new(C(q.x, q.y, q.z)))
        rows.append(ring)
    for k in range(len(rows) - 1):
        for q in range(4):
            bm.faces.new((rows[k][q], rows[k][(q + 1) % 4], rows[k + 1][(q + 1) % 4], rows[k + 1][q]))
    if rows:
        bm.faces.new(rows[0])
        bm.faces.new(list(reversed(rows[-1])))
    ob = obj_from_bmesh(name, bm, [material])
    smooth(ob, 40)
    return ob


def contour_pts(surface, y, centre_z, angles):
    out = []
    for a in angles:
        ar = math.radians(a)
        dx, dz = math.sin(ar), math.cos(ar)
        p, n = surface.hit((dx * 4.0, y, centre_z + dz * 4.0), (-dx, 0.0, -dz), 8.0)
        if p is not None:
            out.append((p, n))
    return out


def front_y(d, prof):
    return prof.bottom(d["nose"] - 0.05)


def rear_y(d, prof):
    return prof.bottom(d["tail"] + 0.05)


# ---------- передний бампер ----------

def front_stock(d, prof, surface, m):
    parts = []
    sp = d.get("splitter")
    if sp:
        parts.append(blade("Splitter", contour_pts(surface, sp["y"], d["nose"] - 1.0, range(-30, 31, 2)), m["gloss"], sp["out"], sp["thick"]))
    b = d.get("bumper_front")
    if b:
        y0, y1, depth, half = b
        parts.append(box("Bumper", (0, (y0 + y1) / 2, d["nose"] + depth / 2 - 0.05), (2 * half, y1 - y0, depth), m[d.get("bumper_mat", "trim")], bevel=0.025, segments=3))
    return parts


def rear_stock(d, prof, surface, m):
    parts = []
    df = d.get("diffuser")
    if df:
        ybot = prof.bottom(d["tail"] + 0.05)
        dz = d["tail"] + 0.10
        outline = rounded_rect(0, ybot + 0.02 + df["h"] * 0.5, df.get("w", 1.25), df["h"], 0.03, 24)
        parts.append(patch("Diffuser", surface, outline, 'z-', m["gloss"], lift=0.004, rings=2, ref=dz))
        parts += fins("Fin", surface, ybot - 0.02, ybot + df["h"] * 0.8, dz, df["fins"], 0.12, m["gloss"])
    b = d.get("bumper_rear")
    if b:
        y0, y1, depth, half = b
        parts.append(box("Bumper", (0, (y0 + y1) / 2, d["tail"] - (depth / 2 - 0.05)), (2 * half, y1 - y0, depth), m[d.get("bumper_mat", "trim")], bevel=0.025, segments=3))
    return parts


def front_sport(d, prof, surface, m):
    y = front_y(d, prof) + 0.015
    parts = [blade("Lip", contour_pts(surface, y, d["nose"] - 1.0, range(-36, 37, 2)), m["gloss"], 0.065, 0.022, drop=0.01)]
    # Клыки-канарды по углам бампера.
    for s in (-1, 1):
        for k, dy in enumerate((0.07, 0.13)):
            p, n = surface.hit((s * 4.0, y + dy, d["nose"] - 0.18), (-s, 0.0, 0.0), 8.0)
            if p is None:
                continue
            base = (p.x, p.y, p.z)
            tip = (p.x + s * 0.07, p.y + 0.015, p.z - 0.10 + k * 0.02)
            parts.append(beam("Canard", base, tip, 0.008, 0.05 - 0.01 * k, m["gloss"], bevel=0.002))
    return parts


def front_race(d, prof, surface, m):
    y = front_y(d, prof) + 0.01
    pts = contour_pts(surface, y, d["nose"] - 1.0, range(-40, 41, 2))
    parts = [blade("Splitter", pts, m["carbon"], 0.13, 0.022, drop=0.02)]
    # Торцевые пластины и опоры сплиттера.
    if pts:
        for (p, n) in (pts[0], pts[-1]):
            s = 1 if p.x > 0 else -1
            parts.append(box("Endplate", (p.x + s * 0.02, y - 0.005, p.z + 0.03), (0.012, 0.09, 0.16), m["carbon"], bevel=0.003))
        for x in (-0.35, 0.35):
            parts.append(beam("Strut", (x, y + 0.08, d["nose"] - 0.05), (x, y - 0.015, d["nose"] + 0.06), 0.012, 0.012, m["rimdark"], bevel=0.002))
    for s in (-1, 1):
        for k, dy in enumerate((0.06, 0.11, 0.16)):
            p, n = surface.hit((s * 4.0, y + dy, d["nose"] - 0.2), (-s, 0.0, 0.0), 8.0)
            if p is None:
                continue
            parts.append(beam("Canard", (p.x, p.y, p.z), (p.x + s * 0.09, p.y + 0.02, p.z - 0.12), 0.007, 0.055, m["carbon"], bevel=0.002))
    # Буксировочная петля (красная лента).
    parts.append(box("TowStrap", (0.42, y + 0.05, d["nose"] + 0.02), (0.03, 0.06, 0.05), m["caliper"], bevel=0.008))
    return parts


def front_offroad(d, prof, surface, m):
    """Силовой бампер: брус с торцами, защита картера, лебёдка, такелажные скобы."""
    y0 = front_y(d, prof) + 0.04
    half = prof.hw(d["nose"] - 0.3) * 0.98
    z = d["nose"] + 0.06
    parts = [box("SteelBar", (0, y0 + 0.13, z), (2 * half - 0.12, 0.20, 0.16), m["rimdark"], bevel=0.02)]
    for s in (-1, 1):
        parts.append(beam("BarEnd", (s * (half - 0.08), y0 + 0.13, z), (s * (half + 0.02), y0 + 0.18, z - 0.22), 0.16, 0.18, m["rimdark"], bevel=0.02))
        parts.append(box("Shackle", (s * 0.42, y0 + 0.24, z + 0.09), (0.035, 0.07, 0.05), m["caliper"], bevel=0.01))
    parts.append(cylinder("Winch", (0, y0 + 0.16, z + 0.05), 0.055, 0.42, 'x', m["trim"], segments=20, bevel=0.01))
    parts.append(box("Fairlead", (0, y0 + 0.16, z + 0.10), (0.18, 0.06, 0.03), m["chrome"], bevel=0.008))
    # Защита: наклонная пластина под носом.
    parts.append(beam("Skid", (0, y0 + 0.02, z - 0.02), (0, prof.bottom(d["nose"] - 0.6) + 0.02, d["nose"] - 0.55), 1.0, 0.012, m["rimdark"], bevel=0.004))
    return parts


# ---------- задний бампер ----------

def rear_diffuser(d, prof, surface, m):
    y = rear_y(d, prof)
    z = d["tail"] + 0.12
    parts = []
    outline = rounded_rect(0, y + 0.10, 1.35, 0.17, 0.03, 24)
    parts.append(patch("Diffuser", surface, outline, 'z-', m["carbon"], lift=0.006, rings=2, ref=z))
    for x in (-0.48, -0.24, 0.0, 0.24, 0.48):
        p, n = surface.hit((x, y + 0.08, d["tail"] - 4.0), (0, 0, 1), 8.0)
        if p is not None:
            parts.append(box("Fin", (x, y + 0.05, p.z + 0.06), (0.012, 0.16, 0.26), m["carbon"], bevel=0.003))
    p, n = surface.hit((0, y + 0.14, d["tail"] - 4.0), (0, 0, 1), 8.0)
    if p is not None:
        parts.append(box("RainLight", (0, y + 0.15, p.z - 0.012), (0.10, 0.035, 0.02), m["tail"], bevel=0.005))
    return parts


def rear_offroad(d, prof, surface, m):
    y0 = rear_y(d, prof) + 0.04
    half = prof.hw(d["tail"] + 0.3) * 0.98
    z = d["tail"] - 0.07
    parts = [box("SteelBar", (0, y0 + 0.12, z), (2 * half - 0.1, 0.18, 0.15), m["rimdark"], bevel=0.02)]
    parts.append(box("Hitch", (0, y0 + 0.07, z - 0.14), (0.07, 0.07, 0.18), m["trim"], bevel=0.008))
    parts.append(cylinder("HitchBall", (0, y0 + 0.13, z - 0.21), 0.03, 0.05, 'y', m["chrome"], segments=12))
    for s in (-1, 1):
        parts.append(box("Shackle", (s * 0.45, y0 + 0.21, z - 0.09), (0.035, 0.07, 0.05), m["caliper"], bevel=0.01))
    return parts


# ---------- пороги и арки ----------

def side_points(d, surface, y, side):
    wh = d["wheel"]
    z0 = wh["zr"] + wh["r"] + 0.10
    z1 = wh["zf"] - wh["r"] - 0.10
    pts = []
    steps = 20
    for k in range(steps + 1):
        z = lerp(z0, z1, k / steps)
        p, n = side_hit(surface, z, y, side)
        if p is not None:
            pts.append((p, n))
    return pts


def skirts_sport(d, prof, surface, m):
    parts = []
    y = prof.bottom(0.0) + 0.035
    for s in (-1, 1):
        parts.append(blade("Skirt", side_points(d, surface, y, s), m["gloss"], 0.035, 0.03, drop=0.0))
    return parts


def skirts_sliders(d, prof, surface, m):
    parts = []
    y = prof.bottom(0.0) + 0.02
    wh = d["wheel"]
    z0 = wh["zr"] + wh["r"] + 0.12
    z1 = wh["zf"] - wh["r"] - 0.12
    for s in (-1, 1):
        p, n = side_hit(surface, 0.0, y + 0.05, s)
        x = (p.x if p is not None else s * prof.hw(0)) + s * 0.06
        parts.append(tube("Slider", (x, y, z0), (x, y, z1), 0.032, m["rimdark"]))
        for z in (z0 + 0.1, (z0 + z1) / 2, z1 - 0.1):
            parts.append(tube("SliderMount", (x - s * 0.08, y + 0.02, z), (x, y, z), 0.022, m["rimdark"]))
    return parts


def flares(d, prof, surface, m, width, out, material, rivets):
    """Расширители арок: дуга вокруг выреза, по поверхности борта, с выступом наружу."""
    wh = d["wheel"]
    parts = []
    R = wh["r"] + d.get("arch_gap", 0.05)
    for zc in (wh["zf"], wh["zr"]):
        for s in (-1, 1):
            bm = bmesh.new()
            rows = []
            steps = 26
            for k in range(steps + 1):
                a = math.radians(-8 + 196 * k / steps)
                ring = []
                taper = max(0.0, math.sin(math.pi * k / steps)) ** 0.5
                for (rr, o) in ((R - 0.005, out * taper), (R + width, 0.004), (R + width, -0.01), (R - 0.005, -0.01)):
                    y = wh["r"] + rr * math.sin(a)
                    z = zc + rr * math.cos(a)
                    p, n = side_hit(surface, z, max(y, prof.bottom(z) + 0.02), s)
                    if p is None:
                        x = s * prof.hw(z)
                    else:
                        x = p.x
                    ring.append(bm.verts.new(C(x + s * (o + 0.004), y, z)))
                rows.append(ring)
            for k in range(steps):
                for q in range(4):
                    bm.faces.new((rows[k][q], rows[k][(q + 1) % 4], rows[k + 1][(q + 1) % 4], rows[k + 1][q]))
            ob = obj_from_bmesh("Flare", bm, [material])
            smooth(ob, 40)
            parts.append(ob)
            if rivets:
                for k in range(1, 8):
                    a = math.radians(-8 + 196 * k / 8)
                    y = wh["r"] + (R + width * 0.6) * math.sin(a)
                    z = zc + (R + width * 0.6) * math.cos(a)
                    p, n = side_hit(surface, z, y, s)
                    if p is not None:
                        parts.append(cylinder("Rivet", (p.x + s * 0.01, y, z), 0.008, 0.012, 'x', m["chrome"], segments=8))
    return parts


def fenders_wide(d, prof, surface, m):
    return flares(d, prof, surface, m, 0.075, 0.055, m["paint"], True)


def fenders_offroad(d, prof, surface, m):
    return flares(d, prof, surface, m, 0.11, 0.07, m["trim"], True)


# ---------- капот ----------

def hood_area(d, prof):
    g = d["greenhouse"]
    z0 = g["cowl"] + 0.10
    z1 = d.get("hood_end", d["nose"] - 0.2) - 0.08
    return z0, z1


def hood_vents(d, prof, surface, m):
    z0, z1 = hood_area(d, prof)
    zc = lerp(z0, z1, 0.45)
    parts = []
    for s in (-1, 1):
        x = s * prof.top_hw(zc) * 0.42
        outline = rounded_rect(x, zc, 0.22, 0.32, 0.03, 24)
        parts.append(patch("Vent", surface, outline, 'y+', m["grille"], lift=0.004, rings=2))
        for k in range(5):
            zz = zc - 0.12 + k * 0.06
            parts.append(patch("Louver", surface, rounded_rect(x, zz, 0.20, 0.014, 0.006, 12), 'y+', m["gloss"], lift=0.009, rings=1))
    return parts


def hood_scoop(d, prof, surface, m):
    z0, z1 = hood_area(d, prof)
    zc = lerp(z0, z1, 0.40)
    p, n = top_hit(surface, 0.0, zc)
    y = p.y if p is not None else prof.top(zc)
    w = min(0.62, prof.top_hw(zc) * 0.75)
    parts = [pod("Scoop", (0, y + 0.015, zc), 0.62, w, 0.16, m["hood"], nose=0.25, rings=12)]
    # Тёмный зев воздухозаборника спереди.
    parts.append(box("ScoopMouth", (0, y + 0.03, zc + 0.30), (w * 0.8, 0.05, 0.02), m["grille"], bevel=0.008))
    return parts


def hood_carbon(d, prof, surface, m):
    z0, z1 = hood_area(d, prof)
    pts = []
    steps = 8
    for k in range(steps + 1):
        z = lerp(z0, z1, k / steps)
        pts.append((prof.top_hw(z) * 0.86, z))
    outline = pts + [(-x, z) for (x, z) in reversed(pts)]
    return [patch("CarbonHood", surface, rounded_poly(outline, 0.05), 'y+', m["carbon"], lift=0.004, rings=4)]


# ---------- крыша ----------

def roof_points(d, prof):
    g = d["greenhouse"]
    z0 = g["roof_rear"] + 0.08
    z1 = g["roof_front"] - 0.05
    return z0, z1


def roof_rails(d, prof, surface, m):
    z0, z1 = roof_points(d, prof)
    parts = []
    for s in (-1, 1):
        x = s * d["roof_hw"] * 0.78
        p0, _ = top_hit(surface, x, z0)
        p1, _ = top_hit(surface, x, z1)
        if p0 is None or p1 is None:
            continue
        parts.append(tube("Rail", (x, p0.y + 0.055, z0), (x, p1.y + 0.055, z1), 0.016, m["chrome"]))
        for zz, pp in ((z0, p0), (z1, p1)):
            parts.append(box("RailFoot", (x, pp.y + 0.025, zz), (0.04, 0.05, 0.08), m["trim"], bevel=0.01))
    return parts


def roof_rack(d, prof, surface, m):
    parts = roof_rails(d, prof, surface, m)
    z0, z1 = roof_points(d, prof)
    p, _ = top_hit(surface, 0.0, (z0 + z1) / 2)
    y = (p.y if p is not None else prof.top(0)) + 0.075
    hw = d["roof_hw"] * 0.78
    for k in range(3):
        z = lerp(z0 + 0.1, z1 - 0.1, k / 2)
        parts.append(box("CrossBar", (0, y, z), (2 * hw + 0.06, 0.025, 0.05), m["trim"], bevel=0.008))
    return parts


def roof_expedition(d, prof, surface, m):
    """Экспедиционный багажник: корзина с бортами, канистры, ящик и запаска."""
    parts = roof_rack(d, prof, surface, m)
    z0, z1 = roof_points(d, prof)
    p, _ = top_hit(surface, 0.0, (z0 + z1) / 2)
    y = (p.y if p is not None else prof.top(0)) + 0.10
    hw = d["roof_hw"] * 0.80
    zs, ze = z0 + 0.02, z1 - 0.02
    for s in (-1, 1):
        parts.append(tube("BasketSide", (s * hw, y + 0.10, zs), (s * hw, y + 0.10, ze), 0.016, m["rimdark"]))
        for k in range(6):
            z = lerp(zs, ze, k / 5)
            parts.append(tube("BasketPost", (s * hw, y, z), (s * hw, y + 0.10, z), 0.012, m["rimdark"]))
    for z in (zs, ze):
        parts.append(tube("BasketEnd", (-hw, y + 0.10, z), (hw, y + 0.10, z), 0.016, m["rimdark"]))
    parts.append(box("Floor", (0, y, (zs + ze) / 2), (2 * hw, 0.02, ze - zs), m["rimdark"], bevel=0.005))
    # Груз: две канистры, ящик, запасное колесо.
    olive = mat("M_Cargo", "#5E6B3A", roughness=0.6)
    for k, x in enumerate((-hw + 0.16, -hw + 0.36)):
        parts.append(box("Jerrycan", (x, y + 0.13, zs + 0.2), (0.16, 0.24, 0.32), olive, bevel=0.02))
    parts.append(box("Crate", (hw - 0.30, y + 0.11, zs + 0.28), (0.42, 0.20, 0.42), m["trim"], bevel=0.015))
    wr = min(0.36, d["wheel"]["r"])
    parts.append(cylinder("RoofSpare", (0.0, y + 0.06, ze - wr - 0.02), wr, 0.10, 'y', m["tire"], segments=32, bevel=0.03))
    return parts


def roof_lightbar(d, prof, surface, m):
    g = d["greenhouse"]
    z = g["roof_front"] - 0.10
    p, _ = top_hit(surface, 0.0, z)
    y = (p.y if p is not None else prof.top(z)) + 0.08
    hw = d["roof_hw"] * 0.86
    parts = [box("LightBar", (0, y, z + 0.06), (2 * hw, 0.08, 0.08), m["trim"], bevel=0.015)]
    n = 6
    for k in range(n):
        x = lerp(-hw + 0.12, hw - 0.12, k / (n - 1))
        parts.append(box("LedCell", (x, y, z + 0.105), (2 * hw / n - 0.04, 0.05, 0.01), m["drl"], bevel=0.004))
    for s in (-1, 1):
        pp, _ = top_hit(surface, s * hw * 0.9, z)
        if pp is not None:
            parts.append(beam("BarStand", (s * hw * 0.9, pp.y, z), (s * hw * 0.9, y - 0.03, z + 0.05), 0.03, 0.03, m["trim"], bevel=0.006))
    return parts


def roof_scoop(d, prof, surface, m):
    g = d["greenhouse"]
    z = lerp(g["roof_rear"], g["roof_front"], 0.75)
    p, _ = top_hit(surface, 0.0, z)
    y = p.y if p is not None else prof.top(z)
    return [pod("RoofScoop", (0, y + 0.02, z), 0.34, 0.30, 0.10, m["roof"], nose=0.35),
            box("ScoopMouth", (0, y + 0.035, z + 0.17), (0.24, 0.04, 0.012), m["grille"], bevel=0.006)]


# ---------- защита, свет, зеркала, решётка ----------

def guard_bullbar(d, prof, surface, m):
    y0 = front_y(d, prof) + 0.12
    z = d["nose"] + 0.16
    y1 = y0 + 0.62
    parts = []
    for s in (-1, 1):
        x = s * 0.36
        parts.append(tube("BarPost", (x, y0, z), (x, y1, z - 0.04), 0.03, m["rimdark"]))
        parts.append(tube("BarBend", (x, y1, z - 0.04), (s * 0.12, y1 + 0.05, z - 0.06), 0.03, m["rimdark"]))
        parts.append(tube("BarMount", (x, y0 + 0.05, z), (x, y0 + 0.05, d["nose"] - 0.05), 0.025, m["rimdark"]))
    parts.append(tube("BarTop", (-0.12, y1 + 0.05, z - 0.06), (0.12, y1 + 0.05, z - 0.06), 0.03, m["rimdark"]))
    parts.append(tube("BarMid", (-0.36, y0 + 0.30, z - 0.02), (0.36, y0 + 0.30, z - 0.02), 0.024, m["rimdark"]))
    return parts


def lamp(name, centre, r, m, depth=0.07):
    x, y, z = centre
    body = cylinder(name, (x, y, z - depth / 2), r, depth, 'z', m["trim"], segments=24, bevel=0.01)
    lens = cylinder(name + "_Lens", (x, y, z + 0.002), r * 0.86, 0.01, 'z', m["head"], segments=24)
    ring = cylinder(name + "_Ring", (x, y, z), r, 0.014, 'z', m["chrome"], segments=24)
    return [body, lens, ring]


def lights_rally(d, prof, surface, m):
    """Четыре круглые фары на кронштейне перед капотом."""
    z = d["nose"] + 0.04
    p, _ = top_hit(surface, 0.0, d["nose"] - 0.15)
    y = (p.y if p is not None else prof.top(d["nose"])) - 0.02
    parts = [tube("LampBar", (-0.55, y - 0.06, z - 0.03), (0.55, y - 0.06, z - 0.03), 0.014, m["trim"])]
    for x in (-0.45, -0.15, 0.15, 0.45):
        parts += lamp("RallyLamp", (x, y, z + 0.04), 0.075, m)
    return parts


def lights_pods(d, prof, surface, m):
    y = front_y(d, prof) + 0.28
    z = d["nose"] + 0.08
    parts = []
    for s in (-1, 1):
        parts += lamp("PodLamp", (s * 0.62, y, z), 0.06, m, depth=0.09)
    return parts


def mirrors_stock(d, prof, surface, m):
    mx, my, mz = d["mirror"]
    return [mirror("Mirror", (s * mx, my, mz), m, s, "stock") for s in (-1, 1)]


def mirrors_aero(d, prof, surface, m):
    mx, my, mz = d["mirror"]
    return [mirror("Mirror", (s * mx, my, mz), m, s, "aero") for s in (-1, 1)]


def grille_mesh(d, prof, surface, m):
    g = d.get("grille", {})
    if "main" not in g:
        return []
    q = g["main"]
    outline = trapezoid(q[0][0], q[1][0], q[0][1], q[3][0], q[2][0], q[2][1], 0.03)
    x0 = min(p[0] for p in q)
    x1 = max(p[0] for p in q)
    y0 = min(p[1] for p in q)
    y1 = max(p[1] for p in q)
    return [patch("GrilleMesh", surface, poly_offset_true(outline, 0.012), 'z+', m["mesh"], lift=0.007, rings=3, uv_box=(x0, y0, x0 + (x1 - x0), y0 + (x1 - x0)))]


def grille_bars(d, prof, surface, m):
    g = d.get("grille", {})
    if "main" not in g:
        return []
    q = g["main"]
    x0 = min(p[0] for p in q) + 0.03
    x1 = max(p[0] for p in q) - 0.03
    y0 = min(p[1] for p in q)
    y1 = max(p[1] for p in q)
    parts = []
    n = 4
    for k in range(n):
        y = lerp(y0, y1, (k + 0.5) / n)
        parts.append(patch("Bar", surface, rounded_rect((x0 + x1) / 2, y, x1 - x0, 0.022, 0.01, 16), 'z+', m["chrome"], lift=0.012, rings=1))
    return parts


# ---------- спойлеры (по ширине и высоте этой машины) ----------

def trunk_edge(d, prof, surface):
    """Задняя кромка крышки багажника: z, высота и полуширина."""
    z = d["spoiler_mount"][2]
    p, _ = top_hit(surface, 0.0, z)
    y = p.y if p is not None else prof.top(z)
    return z, y, prof.top_hw(z)


def spoiler_lip(d, prof, surface, m):
    z, y, hw = trunk_edge(d, prof, surface)
    pts = []
    steps = 16
    for k in range(steps + 1):
        x = lerp(-hw * 0.9, hw * 0.9, k / steps)
        p, n = top_hit(surface, x, z)
        if p is not None:
            pts.append((p, Vector((0, 0, -1))))
    bm_obj = blade("LipSpoiler", pts, m["paint"], out=0.05, thick=0.018, drop=-0.03, inset=0.02)
    return [bm_obj]


def spoiler_ducktail(d, prof, surface, m):
    z, y, hw = trunk_edge(d, prof, surface)
    rings = []
    for k in range(6):
        t = k / 5
        zz = z + 0.05 - 0.22 * t
        yy = y + 0.01 + 0.10 * t * t
        w = hw * (0.94 - 0.04 * t)
        rings.append([(-w, yy, zz), (w, yy, zz), (w, yy - 0.03, zz), (-w, yy - 0.03, zz)])
    bm = loft("Ducktail", rings, lambda i, j: 0)
    ob = obj_from_bmesh("Ducktail", bm, [m["paint"]])
    add_bevel(ob, 0.008, 2)
    apply_modifiers(ob)
    smooth(ob, 40)
    return [ob]


def wing(name, centre, span, chord, m, material, endplate=0.14):
    """Профилированное крыло: верх выпуклый, низ плоский, задняя кромка с гурни."""
    x0, y0, z0 = centre
    prof_pts = [(0.0, 0.0), (0.08, 0.018), (0.35, 0.03), (0.75, 0.02), (1.0, 0.004), (1.0, -0.004), (0.6, -0.008), (0.1, -0.006)]
    rings = []
    for xs in (-span / 2, span / 2):
        ring = []
        for (u, v) in prof_pts:
            # Передняя кромка — вперёд (+z), угол атаки ~8°.
            zz = z0 + chord * (0.5 - u)
            yy = y0 + chord * v + (u - 0.5) * chord * 0.14
            ring.append((x0 + xs, yy, zz))
        rings.append(ring)
    bm = loft(name, rings, lambda i, j: 0)
    ob = obj_from_bmesh(name, bm, [material])
    smooth(ob, 30)
    parts = [ob]
    for s in (-1, 1):
        parts.append(box(name + "_End", (x0 + s * span / 2, y0 + 0.01, z0), (0.012, endplate, chord * 1.15), material, bevel=0.004))
    parts.append(box(name + "_Gurney", (x0, y0 + chord * 0.07 + 0.012, z0 - chord * 0.5), (span - 0.02, 0.022, 0.008), material, bevel=0.002))
    return parts


def spoiler_wing(d, prof, surface, m):
    z, y, hw = trunk_edge(d, prof, surface)
    span = hw * 2 * 0.98
    h = 0.24
    parts = wing("Wing", (0, y + h, z - 0.04), span, 0.26, m, m["carbon"])
    for s in (-1, 1):
        x = s * hw * 0.55
        p, _ = top_hit(surface, x, z + 0.10)
        yb = p.y if p is not None else y
        parts.append(beam("WingStrut", (x, yb, z + 0.10), (x, y + h - 0.01, z - 0.02), 0.024, 0.10, m["carbon"], bevel=0.005))
    return parts


def spoiler_swan(d, prof, surface, m):
    """Крыло на «лебединых шеях»: стойки огибают крыло сверху."""
    z, y, hw = trunk_edge(d, prof, surface)
    span = hw * 2 * 1.02
    h = 0.30
    parts = wing("Wing", (0, y + h, z - 0.10), span, 0.30, m, m["carbon"], endplate=0.18)
    for s in (-1, 1):
        x = s * hw * 0.42
        p, _ = top_hit(surface, x, z + 0.12)
        yb = p.y if p is not None else y
        parts.append(beam("Neck", (x, yb, z + 0.12), (x, y + h + 0.07, z - 0.02), 0.018, 0.06, m["rimdark"], bevel=0.004))
        parts.append(beam("NeckTop", (x, y + h + 0.07, z - 0.02), (x, y + h + 0.03, z - 0.14), 0.018, 0.05, m["rimdark"], bevel=0.004))
    return parts


def spoiler_roof(d, prof, surface, m):
    """Козырёк над задним стеклом (хэтчбеки и внедорожники)."""
    g = d["greenhouse"]
    z = g["roof_rear"] + 0.02
    p, _ = top_hit(surface, 0.0, z)
    y = p.y if p is not None else prof.top(z)
    hw = d["roof_hw"] * 0.98
    rings = []
    for k in range(5):
        t = k / 4
        zz = z + 0.06 - 0.26 * t
        yy = y - 0.005 - 0.04 * t
        w = hw * (1.0 - 0.06 * t)
        rings.append([(-w, yy, zz), (w, yy, zz), (w, yy - 0.025 - 0.02 * (1 - t), zz), (-w, yy - 0.025 - 0.02 * (1 - t), zz)])
    bm = loft("RoofSpoiler", rings, lambda i, j: 0)
    ob = obj_from_bmesh("RoofSpoiler", bm, [m["roof"]])
    add_bevel(ob, 0.008, 2)
    apply_modifiers(ob)
    smooth(ob, 40)
    return [ob]


def spoiler_rally(d, prof, surface, m):
    """Раллийное крыло на крыше: большой козырёк с боковыми пластинами."""
    g = d["greenhouse"]
    z = g["roof_rear"] - 0.02
    p, _ = top_hit(surface, 0.0, z + 0.05)
    y = p.y if p is not None else prof.top(z)
    hw = d["roof_hw"] * 1.0
    parts = wing("RallyWing", (0, y + 0.05, z - 0.12), hw * 2, 0.30, m, m["roof"], endplate=0.16)
    for s in (-1, 1):
        parts.append(box("WingPlate", (s * hw * 0.98, y + 0.01, z - 0.06), (0.012, 0.10, 0.34), m["roof"], bevel=0.004))
    return parts


KIT_BUILDERS = {
    ("FrontBumper", "Stock"): front_stock, ("FrontBumper", "Sport"): front_sport, ("RearBumper", "Stock"): rear_stock,
    ("FrontBumper", "Race"): front_race, ("FrontBumper", "Offroad"): front_offroad,
    ("RearBumper", "Diffuser"): rear_diffuser, ("RearBumper", "Offroad"): rear_offroad,
    ("Skirts", "Sport"): skirts_sport, ("Skirts", "Sliders"): skirts_sliders,
    ("Fenders", "Wide"): fenders_wide, ("Fenders", "Offroad"): fenders_offroad,
    ("Hood", "Vents"): hood_vents, ("Hood", "Scoop"): hood_scoop, ("Hood", "Carbon"): hood_carbon,
    ("Roof", "Rails"): roof_rails, ("Roof", "Rack"): roof_rack, ("Roof", "Expedition"): roof_expedition,
    ("Roof", "Lightbar"): roof_lightbar, ("Roof", "Scoop"): roof_scoop,
    ("Guard", "Bullbar"): guard_bullbar, ("Lights", "Rally"): lights_rally, ("Lights", "Pods"): lights_pods,
    ("Mirrors", "Stock"): mirrors_stock, ("Mirrors", "Aero"): mirrors_aero,
    ("Grille", "Mesh"): grille_mesh, ("Grille", "Bars"): grille_bars,
    ("Spoiler", "Lip"): spoiler_lip, ("Spoiler", "Ducktail"): spoiler_ducktail, ("Spoiler", "Wing"): spoiler_wing,
    ("Spoiler", "Swan"): spoiler_swan, ("Spoiler", "Roof"): spoiler_roof, ("Spoiler", "Rally"): spoiler_rally,
}


def build_kits(d, prof, surface, m, root):
    """Все совместимые варианты обвеса этой машины → объекты Kit_<Слот>_<Вариант> в корне."""
    rules = KIT_RULES[d["cls"]]
    plan = [("FrontBumper", "Stock"), ("RearBumper", "Stock"), ("Mirrors", "Stock")]
    for slot, variants in rules.items():
        for v in variants:
            plan.append((slot, v))
    made = []
    for slot, variant in plan:
        fn = KIT_BUILDERS[(slot, variant)]
        parts = [p for p in fn(d, prof, surface, m) if p is not None]
        if not parts:
            continue
        ob = join(parts[0], parts[1:]) if len(parts) > 1 else parts[0]
        triangulate_ngons(ob)
        ob.name = f"Kit_{slot}_{variant}"
        ob.data.name = ob.name + "_" + d["key"]
        set_parent(ob, root)
        made.append(ob.name)
    return made
