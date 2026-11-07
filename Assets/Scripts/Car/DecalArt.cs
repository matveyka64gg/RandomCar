using System;
using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Car
{
    /// Описание встроенной наклейки для гаража.
    public sealed class DecalInfo
    {
        public string Id;
        public string Name;
        public string Group;
        /// Рекомендуемая сторона и размер при добавлении.
        public DecalSide Side;
        public float Size;
        public DecalInfo(string id, string name, string group, DecalSide side, float size)
        {
            Id = id;
            Name = name;
            Group = group;
            Side = side;
            Size = size;
        }
    }

    /// Библиотека встроенных наклеек: рисуется кодом (поля расстояний со сглаживанием), без файлов и чужих ассетов.
    /// Белые части перекрашиваются цветом слоя, чёрные остаются чёрными (номера, обводки).
    public static class DecalArt
    {
        public static readonly DecalInfo[] All =
        {
            new DecalInfo("number:roundel", "Номер в круге", "Номера", DecalSide.Left, 0.16f),
            new DecalInfo("number:plate", "Номер на плашке", "Номера", DecalSide.Left, 0.16f),
            new DecalInfo("number:bold", "Крупные цифры", "Номера", DecalSide.Left, 0.18f),
            new DecalInfo("number:outline", "Цифры с обводкой", "Номера", DecalSide.Hood, 0.45f),
            new DecalInfo("stripe.double", "Двойная полоса", "Полосы", DecalSide.Hood, 0.55f),
            new DecalInfo("stripe.single", "Широкая полоса", "Полосы", DecalSide.Hood, 0.5f),
            new DecalInfo("stripe.pin", "Тонкие линии", "Полосы", DecalSide.Left, 0.9f),
            new DecalInfo("stripe.slash", "Боковой росчерк", "Полосы", DecalSide.Left, 0.7f),
            new DecalInfo("stripe.hockey", "Клюшка", "Полосы", DecalSide.Left, 0.8f),
            new DecalInfo("stripe.fade", "Полосы скорости", "Полосы", DecalSide.Left, 0.6f),
            new DecalInfo("stripe.checker", "Шахматный флаг", "Полосы", DecalSide.Left, 0.6f),
            new DecalInfo("stripe.zigzag", "Зигзаг", "Полосы", DecalSide.Left, 0.8f),
            new DecalInfo("geo.chevrons", "Шевроны", "Геометрия", DecalSide.Hood, 0.5f),
            new DecalInfo("geo.triangles", "Треугольники", "Геометрия", DecalSide.Left, 0.5f),
            new DecalInfo("geo.hex", "Соты", "Геометрия", DecalSide.Left, 0.45f),
            new DecalInfo("geo.dots", "Полутон", "Геометрия", DecalSide.Left, 0.55f),
            new DecalInfo("geo.pixels", "Пиксели", "Геометрия", DecalSide.Left, 0.45f),
            new DecalInfo("geo.circles", "Круги", "Геометрия", DecalSide.Hood, 0.4f),
            new DecalInfo("geo.diamonds", "Ромбы", "Геометрия", DecalSide.Left, 0.5f),
            new DecalInfo("geo.waves", "Волны", "Геометрия", DecalSide.Left, 0.7f),
            new DecalInfo("geo.hazard", "Диагональные полосы", "Геометрия", DecalSide.Front, 0.5f),
            new DecalInfo("abs.splash", "Брызги", "Узоры", DecalSide.Left, 0.5f),
            new DecalInfo("abs.flames", "Языки пламени", "Узоры", DecalSide.Left, 0.6f),
            new DecalInfo("abs.camo", "Камуфляж", "Узоры", DecalSide.Left, 0.9f),
            new DecalInfo("abs.speed", "Скоростные линии", "Узоры", DecalSide.Left, 0.7f),
            new DecalInfo("abs.shards", "Осколки", "Узоры", DecalSide.Left, 0.55f),
            new DecalInfo("abs.swoosh", "Волна-росчерк", "Узоры", DecalSide.Left, 0.85f),
            new DecalInfo("emb.star", "Звезда", "Эмблемы", DecalSide.Hood, 0.22f),
            new DecalInfo("emb.bolt", "Молния", "Эмблемы", DecalSide.Left, 0.25f),
            new DecalInfo("emb.wings", "Крылья", "Эмблемы", DecalSide.Hood, 0.35f),
            new DecalInfo("emb.crown", "Корона", "Эмблемы", DecalSide.Hood, 0.22f),
            new DecalInfo("emb.sun", "Солнце", "Эмблемы", DecalSide.Hood, 0.3f),
            new DecalInfo("emb.mountain", "Горы", "Эмблемы", DecalSide.Left, 0.25f),
            new DecalInfo("emb.heart", "Сердце", "Эмблемы", DecalSide.Left, 0.14f),
            new DecalInfo("emb.target", "Мишень", "Эмблемы", DecalSide.Left, 0.16f),
            new DecalInfo("emb.shield", "Щит", "Эмблемы", DecalSide.Hood, 0.22f),
            new DecalInfo("emb.rc", "Монограмма RC", "Эмблемы", DecalSide.Left, 0.2f),
            new DecalInfo("emb.paw", "Лапа", "Эмблемы", DecalSide.Left, 0.14f),
            new DecalInfo("emb.arrow", "Стрела", "Эмблемы", DecalSide.Left, 0.3f),
            new DecalInfo("emb.flame", "Огонёк", "Эмблемы", DecalSide.Left, 0.14f),
        };

        static readonly Dictionary<string, Texture2D> cache = new Dictionary<string, Texture2D>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => cache.Clear();

        public static DecalInfo Info(string id) => Array.Find(All, d => d.Id == id);

        /// Текстура встроенной наклейки (number: — с номером). Неизвестный id — null.
        public static Texture2D Get(string id, int number = 7)
        {
            string key = id.StartsWith("number:", StringComparison.Ordinal) ? $"{id}#{number}" : id;
            if (cache.TryGetValue(key, out var tex) && tex != null) return tex;
            tex = Render(id, Mathf.Clamp(number, 0, 99));
            if (tex != null) cache[key] = tex;
            return tex;
        }

        // ---------- растеризация ----------

        /// Пиксель наклейки: покрытие (0..1) и яркость (1 — белое, перекрашивается; 0 — чёрное).
        delegate void Shader(float x, float y, out float alpha, out float white);

        static Texture2D Render(string id, int number)
        {
            int w = 256, h = 256;
            Shader shade = null;
            switch (id)
            {
                case "number:roundel":
                    shade = (float x, float y, out float a, out float wh) =>
                    {
                        float disc = Circle(x, y, 0.5f, 0.5f, 0.47f);
                        float digits = Digits(number, x, y, 0.5f, 0.5f, 0.5f, 0.12f);
                        a = Cover(disc, 1f / 256);
                        wh = 1f - Cover(digits, 1f / 256);
                    };
                    break;
                case "number:plate":
                    shade = (float x, float y, out float a, out float wh) =>
                    {
                        float plate = RoundBox(x, y, 0.5f, 0.5f, 0.44f, 0.44f, 0.09f);
                        float border = Mathf.Abs(RoundBox(x, y, 0.5f, 0.5f, 0.40f, 0.40f, 0.07f)) - 0.012f;
                        float digits = Digits(number, x, y, 0.5f, 0.5f, 0.56f, 0.12f);
                        a = Cover(plate, 1f / 256);
                        wh = 1f - Mathf.Max(Cover(digits, 1f / 256), Cover(border, 1f / 256));
                    };
                    break;
                case "number:bold":
                    shade = (float x, float y, out float a, out float wh) =>
                    {
                        a = Cover(Digits(number, x, y, 0.5f, 0.5f, 0.82f, 0.15f), 1f / 256);
                        wh = 1f;
                    };
                    break;
                case "number:outline":
                    shade = (float x, float y, out float a, out float wh) =>
                    {
                        float dd = Digits(number, x, y, 0.5f, 0.5f, 0.78f, 0.12f);
                        a = Cover(dd - 0.03f, 1f / 256);
                        wh = Cover(dd, 1f / 256);
                    };
                    break;
                default:
                    shade = Design(id, ref w, ref h);
                    break;
            }
            if (shade == null) return null;
            var pixels = new Color32[w * h];
            float aspect = (float)w / h;
            for (int py = 0; py < h; py++)
            {
                float y = (py + 0.5f) / h;
                for (int px = 0; px < w; px++)
                {
                    float x = (px + 0.5f) / w * aspect;
                    shade(x, y, out float a, out float white);
                    byte v = (byte)(Mathf.Clamp01(white) * 255f);
                    pixels[py * w + px] = new Color32(v, v, v, (byte)(Mathf.Clamp01(a) * 255f));
                }
            }
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, true) { name = "Decal_" + id, wrapMode = TextureWrapMode.Clamp, anisoLevel = 4 };
            tex.SetPixels32(pixels);
            tex.Apply(true, true);
            return tex;
        }

        static float Cover(float d, float aa) => Mathf.Clamp01(0.5f - d / (aa * 1.5f));

        static Shader Solid(Func<float, float, float> sdf, float aa)
        {
            return (float x, float y, out float a, out float wh) =>
            {
                a = Cover(sdf(x, y), aa);
                wh = 1f;
            };
        }

        /// Узоры и эмблемы. Размер текстуры (w, h) задаёт пропорцию наклейки.
        static Shader Design(string id, ref int w, ref int h)
        {
            float aa = 1f / 256;
            switch (id)
            {
                case "stripe.double":
                    w = 128; h = 512;
                    return Solid((x, y) => Mathf.Min(Mathf.Abs(x - 0.07f) - 0.05f, Mathf.Abs(x - 0.18f) - 0.05f), aa);
                case "stripe.single":
                    w = 128; h = 512;
                    return Solid((x, y) => Mathf.Abs(x - 0.125f) - 0.09f, aa);
                case "stripe.pin":
                    w = 512; h = 64;
                    return Solid((x, y) => Mathf.Min(Mathf.Abs(y - 0.3f) - 0.08f, Mathf.Abs(y - 0.7f) - 0.08f), aa * 2);
                case "stripe.slash":
                    w = 512; h = 128;
                    return Solid((x, y) => Polygon(x, y, new[] { new Vector2(0.1f, 0.2f), new Vector2(3.7f, 0.2f), new Vector2(3.95f, 0.8f), new Vector2(0.4f, 0.8f) }), aa * 2);
                case "stripe.hockey":
                    w = 512; h = 256;
                    return Solid((x, y) => Mathf.Min(Box(x, y, 0.95f, 0.30f, 0.9f, 0.1f), Box(x, y, 1.78f, 0.55f, 0.07f, 0.35f)), aa);
                case "stripe.fade":
                    w = 512; h = 128;
                    return Solid((x, y) =>
                    {
                        float best = 1f;
                        for (int i = 0; i < 6; i++)
                        {
                            float cx = 0.25f + i * 0.62f;
                            float half = 0.26f * (1f - i * 0.14f);
                            best = Mathf.Min(best, Polygon(x, y, new[] { new Vector2(cx - half, 0.15f), new Vector2(cx + half, 0.15f), new Vector2(cx + half + 0.15f, 0.85f), new Vector2(cx - half + 0.15f, 0.85f) }));
                        }
                        return best;
                    }, aa * 2);
                case "stripe.checker":
                    w = 512; h = 128;
                    return (float x, float y, out float a, out float wh) =>
                    {
                        int cx = Mathf.FloorToInt(x / 0.25f), cy = Mathf.FloorToInt(y / 0.25f);
                        a = 1f;
                        wh = (cx + cy) % 2 == 0 ? 1f : 0f;
                    };
                case "stripe.zigzag":
                    w = 512; h = 128;
                    return Solid((x, y) =>
                    {
                        float t = Mathf.Repeat(x, 0.5f) / 0.5f;
                        float zig = 0.5f + (t < 0.5f ? t * 2f - 0.5f : 1.5f - t * 2f) * 0.6f;
                        return Mathf.Abs(y - zig) - 0.12f;
                    }, aa * 2);
                case "geo.chevrons":
                    w = 256; h = 512;
                    return Solid((x, y) =>
                    {
                        float yy = Mathf.Repeat(y * 2f + Mathf.Abs(x - 0.25f) * 1.6f, 0.5f);
                        return Mathf.Max(Mathf.Abs(yy - 0.25f) - 0.09f, Mathf.Abs(x - 0.25f) - 0.22f);
                    }, aa);
                case "geo.triangles":
                    return Solid((x, y) =>
                    {
                        float best = 1f;
                        var rnd = new System.Random(4);
                        for (int i = 0; i < 9; i++)
                        {
                            var c = new Vector2((float)rnd.NextDouble() * 0.8f + 0.1f, (float)rnd.NextDouble() * 0.8f + 0.1f);
                            float s = 0.05f + (float)rnd.NextDouble() * 0.1f;
                            float ang = (float)rnd.NextDouble() * 6.28f;
                            best = Mathf.Min(best, Polygon(x, y, Ngon(c, s, 3, ang)));
                        }
                        return best;
                    }, aa);
                case "geo.hex":
                    return Solid((x, y) =>
                    {
                        float s = 0.11f;
                        Vector2 p = new Vector2(x, y) / s;
                        Vector2 r = new Vector2(1f, 1.732f);
                        Vector2 hgrid = r * 0.5f;
                        Vector2 a1 = Mod(p, r) - hgrid;
                        Vector2 b1 = Mod(p - hgrid, r) - hgrid;
                        Vector2 g = a1.sqrMagnitude < b1.sqrMagnitude ? a1 : b1;
                        float hexd = Mathf.Max(Mathf.Abs(g.x) * 0.866f + Mathf.Abs(g.y) * 0.5f, Mathf.Abs(g.y)) - 0.42f;
                        float fade = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f)) - 0.48f;
                        return Mathf.Max(Mathf.Abs(hexd * s) - 0.008f, fade);
                    }, aa);
                case "geo.dots":
                    w = 512; h = 256;
                    return Solid((x, y) =>
                    {
                        float cell = 0.08f;
                        float cx = (Mathf.Floor(x / cell) + 0.5f) * cell, cy = (Mathf.Floor(y / cell) + 0.5f) * cell;
                        float r = cell * 0.48f * Mathf.Clamp01(1.1f - cx / 2.0f);
                        return Vector2.Distance(new Vector2(x, y), new Vector2(cx, cy)) - r;
                    }, aa);
                case "geo.pixels":
                    w = 512; h = 256;
                    return (float x, float y, out float a, out float wh) =>
                    {
                        int cx = Mathf.FloorToInt(x / 0.1f), cy = Mathf.FloorToInt(y / 0.1f);
                        float chance = Mathf.Clamp01(1.2f - x / 1.6f);
                        float hsh = Hash(cx, cy);
                        float inner = Mathf.Max(Mathf.Abs(Mathf.Repeat(x, 0.1f) - 0.05f), Mathf.Abs(Mathf.Repeat(y, 0.1f) - 0.05f)) - 0.04f;
                        a = hsh < chance ? Cover(inner, 1f / 256) : 0f;
                        wh = 1f;
                    };
                case "geo.circles":
                    return Solid((x, y) =>
                    {
                        float r = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f));
                        return Mathf.Max(Mathf.Abs(Mathf.Repeat(r, 0.1f) - 0.05f) - 0.022f, r - 0.47f);
                    }, aa);
                case "geo.diamonds":
                    w = 512; h = 128;
                    return Solid((x, y) =>
                    {
                        float cx = (Mathf.Floor(x / 0.36f) + 0.5f) * 0.36f;
                        float s = 0.38f - 0.05f * cx;
                        return (Mathf.Abs(x - cx) + Mathf.Abs(y - 0.5f)) * 0.7f - s * 0.5f;
                    }, aa * 2);
                case "geo.waves":
                    w = 512; h = 128;
                    return Solid((x, y) => Mathf.Min(Mathf.Abs(y - 0.35f - 0.12f * Mathf.Sin(x * 9f)) - 0.06f, Mathf.Abs(y - 0.68f - 0.12f * Mathf.Sin(x * 9f + 1.6f)) - 0.05f), aa * 2);
                case "geo.hazard":
                    w = 512; h = 128;
                    return (float x, float y, out float a, out float wh) =>
                    {
                        a = 1f;
                        wh = Mathf.Repeat(x + y, 0.4f) < 0.2f ? 1f : 0f;
                    };
                case "abs.splash":
                    return Solid((x, y) =>
                    {
                        float best = 1f;
                        var rnd = new System.Random(11);
                        for (int i = 0; i < 26; i++)
                        {
                            float ang = (float)rnd.NextDouble() * 6.28f;
                            float dist = (float)Math.Pow(rnd.NextDouble(), 0.7) * 0.42f;
                            float r = 0.012f + (float)rnd.NextDouble() * 0.05f * (1f - dist * 1.6f);
                            best = SmoothMin(best, Circle(x, y, 0.5f + Mathf.Cos(ang) * dist, 0.5f + Mathf.Sin(ang) * dist, Mathf.Max(0.008f, r)), 0.03f);
                        }
                        return SmoothMin(best, Circle(x, y, 0.5f, 0.5f, 0.16f), 0.06f);
                    }, aa);
                case "abs.flames":
                    w = 512; h = 256;
                    return Solid((x, y) =>
                    {
                        float best = 1f;
                        for (int i = 0; i < 5; i++)
                        {
                            float yc = 0.18f + i * 0.16f;
                            float len = 1.3f + 0.45f * Mathf.Sin(i * 2.3f);
                            float t = Mathf.Clamp01(x / len);
                            float thick = 0.075f * (1f - t) + 0.006f;
                            float centre = yc + 0.07f * Mathf.Sin(x * 4f + i) * t;
                            best = SmoothMin(best, Mathf.Max(Mathf.Abs(y - centre) - thick, x - len), 0.04f);
                        }
                        return Mathf.Max(best, 0.04f - x);
                    }, aa);
                case "abs.camo":
                    w = 512; h = 256;
                    return (float x, float y, out float a, out float wh) =>
                    {
                        float n = Mathf.PerlinNoise(x * 3.1f + 7f, y * 3.1f + 3f) + 0.5f * Mathf.PerlinNoise(x * 7f, y * 7f);
                        float m = Mathf.PerlinNoise(x * 2.3f + 21f, y * 2.3f + 9f);
                        a = Cover(0.78f - n, 0.01f) + Cover(0.38f - m, 0.01f);
                        wh = Cover(0.38f - m, 0.01f) > 0.5f ? 0f : 1f;
                        a = Mathf.Clamp01(a);
                    };
                case "abs.speed":
                    w = 512; h = 128;
                    return Solid((x, y) =>
                    {
                        float best = 1f;
                        for (int i = 0; i < 7; i++)
                        {
                            float yc = 0.1f + i * 0.13f;
                            float start = 0.2f + Hash(i, 3) * 1.6f;
                            float len = 0.8f + Hash(i, 5) * 1.6f;
                            float thick = 0.02f + Hash(i, 7) * 0.025f;
                            float t = Mathf.Clamp01((x - start) / len);
                            best = Mathf.Min(best, Mathf.Max(Mathf.Abs(y - yc) - thick * (1f - t), Mathf.Max(start - x, x - start - len)));
                        }
                        return best;
                    }, aa * 2);
                case "abs.shards":
                    return Solid((x, y) =>
                    {
                        float best = 1f;
                        var rnd = new System.Random(23);
                        for (int i = 0; i < 14; i++)
                        {
                            var c = new Vector2(0.15f + (float)rnd.NextDouble() * 0.7f, 0.15f + (float)rnd.NextDouble() * 0.7f);
                            var p = new Vector2[3];
                            for (int k = 0; k < 3; k++) p[k] = c + new Vector2((float)rnd.NextDouble() - 0.5f, (float)rnd.NextDouble() - 0.5f) * 0.22f;
                            best = Mathf.Min(best, Polygon(x, y, p));
                        }
                        return best;
                    }, aa);
                case "abs.swoosh":
                    w = 512; h = 192;
                    return Solid((x, y) =>
                    {
                        float t = x / 2.66f;
                        float c = 0.25f + 0.5f * t * t;
                        float thick = 0.03f + 0.11f * Mathf.Sin(Mathf.Clamp01(t) * Mathf.PI);
                        return Mathf.Max(Mathf.Abs(y - c) - thick, Mathf.Max(0.05f - x, x - 2.6f));
                    }, aa * 2);
                case "emb.star":
                    return Solid((x, y) => Polygon(x, y, Star(new Vector2(0.5f, 0.5f), 0.47f, 0.2f, 5, Mathf.PI / 2f)), aa);
                case "emb.bolt":
                    return Solid((x, y) => Polygon(x, y, new[] { new Vector2(0.58f, 0.97f), new Vector2(0.22f, 0.45f), new Vector2(0.46f, 0.45f), new Vector2(0.36f, 0.03f), new Vector2(0.80f, 0.6f), new Vector2(0.54f, 0.6f) }), aa);
                case "emb.wings":
                    w = 512; h = 256;
                    return Solid((x, y) =>
                    {
                        float best = Circle(x, y, 1f, 0.55f, 0.12f);
                        for (int s = -1; s <= 1; s += 2)
                            for (int k = 0; k < 4; k++)
                            {
                                float len = 0.78f - k * 0.14f;
                                float yy = 0.72f - k * 0.13f;
                                var a = new Vector2(1f + s * 0.1f, yy);
                                var b = new Vector2(1f + s * (0.1f + len), yy + 0.12f - k * 0.02f);
                                best = Mathf.Min(best, Segment(x, y, a, b) - (0.05f - k * 0.008f));
                            }
                        return best;
                    }, aa);
                case "emb.crown":
                    return Solid((x, y) => Mathf.Min(Polygon(x, y, new[] { new Vector2(0.12f, 0.22f), new Vector2(0.88f, 0.22f), new Vector2(0.95f, 0.78f), new Vector2(0.72f, 0.5f), new Vector2(0.5f, 0.86f), new Vector2(0.28f, 0.5f), new Vector2(0.05f, 0.78f) }),
                        Box(x, y, 0.5f, 0.14f, 0.38f, 0.05f)), aa);
                case "emb.sun":
                    return Solid((x, y) =>
                    {
                        float r = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f));
                        float ang = Mathf.Atan2(y - 0.5f, x - 0.5f);
                        float rays = r - (0.36f + 0.09f * Mathf.Max(0f, Mathf.Cos(ang * 12f)));
                        return Mathf.Min(r - 0.22f, Mathf.Max(rays, 0.27f - r));
                    }, aa);
                case "emb.mountain":
                    w = 256; h = 192;
                    return Solid((x, y) => Mathf.Min(Polygon(x, y, new[] { new Vector2(0.05f, 0.08f), new Vector2(0.55f, 0.92f), new Vector2(1.05f, 0.08f) }),
                        Polygon(x, y, new[] { new Vector2(0.6f, 0.08f), new Vector2(0.95f, 0.62f), new Vector2(1.3f, 0.08f) })), aa);
                case "emb.heart":
                    return Solid((x, y) =>
                    {
                        float hx = Mathf.Abs(x - 0.5f) * 2.1f, hy = (y - 0.25f) * 2.1f;
                        float d = Mathf.Sqrt(hx * hx + Mathf.Pow(hy - Mathf.Sqrt(hx) * 0.55f, 2f)) - 0.9f;
                        return d * 0.3f;
                    }, aa);
                case "emb.target":
                    return Solid((x, y) =>
                    {
                        float r = Vector2.Distance(new Vector2(x, y), new Vector2(0.5f, 0.5f));
                        return Mathf.Min(Mathf.Abs(r - 0.4f) - 0.05f, Mathf.Min(Mathf.Abs(r - 0.22f) - 0.05f, r - 0.07f));
                    }, aa);
                case "emb.shield":
                    return Solid((x, y) =>
                    {
                        float outer = Polygon(x, y, new[] { new Vector2(0.12f, 0.92f), new Vector2(0.88f, 0.92f), new Vector2(0.86f, 0.45f), new Vector2(0.5f, 0.05f), new Vector2(0.14f, 0.45f) });
                        float inner = Polygon(x, y, new[] { new Vector2(0.2f, 0.84f), new Vector2(0.8f, 0.84f), new Vector2(0.78f, 0.47f), new Vector2(0.5f, 0.15f), new Vector2(0.22f, 0.47f) });
                        float star = Polygon(x, y, Star(new Vector2(0.5f, 0.52f), 0.2f, 0.09f, 5, Mathf.PI / 2f));
                        return Mathf.Min(Mathf.Max(outer, -inner), star);
                    }, aa);
                case "emb.rc":
                    w = 256; h = 192;
                    return Solid((x, y) =>
                    {
                        float k = 0.09f;
                        // R: стойка, дуга, ножка; C: дуга.
                        float r = Mathf.Min(Segment(x, y, new Vector2(0.18f, 0.12f), new Vector2(0.18f, 0.88f)),
                                  Mathf.Min(Arc(x, y, new Vector2(0.36f, 0.69f), 0.19f, -90f, 90f),
                                  Mathf.Min(Segment(x, y, new Vector2(0.18f, 0.88f), new Vector2(0.36f, 0.88f)),
                                  Mathf.Min(Segment(x, y, new Vector2(0.18f, 0.5f), new Vector2(0.36f, 0.5f)),
                                  Segment(x, y, new Vector2(0.34f, 0.5f), new Vector2(0.56f, 0.12f))))));
                        float c = Arc(x, y, new Vector2(0.98f, 0.5f), 0.36f, 50f, 310f);
                        return Mathf.Min(r, c) - k;
                    }, aa);
                case "emb.paw":
                    return Solid((x, y) =>
                    {
                        float pad = Ellipse(x, y, 0.5f, 0.36f, 0.22f, 0.18f);
                        float t1 = Ellipse(x, y, 0.22f, 0.62f, 0.08f, 0.11f), t2 = Ellipse(x, y, 0.4f, 0.78f, 0.08f, 0.11f);
                        float t3 = Ellipse(x, y, 0.6f, 0.78f, 0.08f, 0.11f), t4 = Ellipse(x, y, 0.78f, 0.62f, 0.08f, 0.11f);
                        return Mathf.Min(pad, Mathf.Min(Mathf.Min(t1, t2), Mathf.Min(t3, t4)));
                    }, aa);
                case "emb.arrow":
                    w = 512; h = 192;
                    return Solid((x, y) => Polygon(x, y, new[] { new Vector2(0.08f, 0.38f), new Vector2(1.9f, 0.38f), new Vector2(1.9f, 0.16f), new Vector2(2.58f, 0.5f), new Vector2(1.9f, 0.84f), new Vector2(1.9f, 0.62f), new Vector2(0.08f, 0.62f) }), aa * 2);
                case "emb.flame":
                    return Solid((x, y) =>
                    {
                        float t = Mathf.Clamp01(y);
                        float centre = 0.5f + 0.08f * Mathf.Sin(t * 7f) * t;
                        float width = 0.3f * Mathf.Sin(Mathf.Pow(1f - t, 0.7f) * Mathf.PI * 0.9f) + 0.005f;
                        return Mathf.Max(Mathf.Abs(x - centre) - width, Mathf.Max(0.05f - y, y - 0.97f));
                    }, aa);
            }
            return null;
        }

        // ---------- примитивы ----------

        static float Circle(float x, float y, float cx, float cy, float r) => Mathf.Sqrt((x - cx) * (x - cx) + (y - cy) * (y - cy)) - r;

        static float Ellipse(float x, float y, float cx, float cy, float rx, float ry)
        {
            float dx = (x - cx) / rx, dy = (y - cy) / ry;
            return (Mathf.Sqrt(dx * dx + dy * dy) - 1f) * Mathf.Min(rx, ry);
        }

        static float Box(float x, float y, float cx, float cy, float hx, float hy)
        {
            float dx = Mathf.Abs(x - cx) - hx, dy = Mathf.Abs(y - cy) - hy;
            return new Vector2(Mathf.Max(dx, 0f), Mathf.Max(dy, 0f)).magnitude + Mathf.Min(Mathf.Max(dx, dy), 0f);
        }

        static float RoundBox(float x, float y, float cx, float cy, float hx, float hy, float r) => Box(x, y, cx, cy, hx - r, hy - r) - r;

        static float Segment(float x, float y, Vector2 a, Vector2 b)
        {
            var p = new Vector2(x, y);
            var ab = b - a;
            float t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(1e-6f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        /// Дуга окружности (центр c, радиус r, от a0 до a1 градусов против часовой) — расстояние до линии.
        static float Arc(float x, float y, Vector2 c, float r, float a0, float a1)
        {
            float best = float.MaxValue;
            int steps = 24;
            Vector2 prev = c + new Vector2(Mathf.Cos(a0 * Mathf.Deg2Rad), Mathf.Sin(a0 * Mathf.Deg2Rad)) * r;
            for (int i = 1; i <= steps; i++)
            {
                float a = Mathf.Lerp(a0, a1, (float)i / steps) * Mathf.Deg2Rad;
                Vector2 cur = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
                best = Mathf.Min(best, Segment(x, y, prev, cur));
                prev = cur;
            }
            return best;
        }

        /// Знаковое расстояние до многоугольника (минус внутри).
        static float Polygon(float x, float y, Vector2[] v)
        {
            var p = new Vector2(x, y);
            float d = (p - v[0]).sqrMagnitude;
            float s = 1f;
            for (int i = 0, j = v.Length - 1; i < v.Length; j = i, i++)
            {
                Vector2 e = v[j] - v[i];
                Vector2 w = p - v[i];
                Vector2 b = w - e * Mathf.Clamp01(Vector2.Dot(w, e) / Mathf.Max(1e-8f, e.sqrMagnitude));
                d = Mathf.Min(d, b.sqrMagnitude);
                bool c1 = p.y >= v[i].y, c2 = p.y < v[j].y, c3 = e.x * w.y > e.y * w.x;
                if ((c1 && c2 && c3) || (!c1 && !c2 && !c3)) s = -s;
            }
            return s * Mathf.Sqrt(d);
        }

        static Vector2[] Ngon(Vector2 c, float r, int n, float start)
        {
            var v = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                float a = start + i * Mathf.PI * 2f / n;
                v[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * r;
            }
            return v;
        }

        static Vector2[] Star(Vector2 c, float r1, float r2, int n, float start)
        {
            var v = new Vector2[n * 2];
            for (int i = 0; i < n * 2; i++)
            {
                float a = start + i * Mathf.PI / n;
                v[i] = c + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * (i % 2 == 0 ? r1 : r2);
            }
            return v;
        }

        static float SmoothMin(float a, float b, float k)
        {
            float h = Mathf.Clamp01(0.5f + 0.5f * (b - a) / k);
            return Mathf.Lerp(b, a, h) - k * h * (1f - h);
        }

        static Vector2 Mod(Vector2 a, Vector2 m) => new Vector2(a.x - m.x * Mathf.Floor(a.x / m.x), a.y - m.y * Mathf.Floor(a.y / m.y));

        static float Hash(int a, int b)
        {
            uint h = (uint)(a * 374761393 + b * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            return (h & 0xFFFFFF) / (float)0x1000000;
        }

        // ---------- цифры (штриховой спортивный шрифт) ----------

        /// Расстояние до числа (одна или две цифры) с центром (cx, cy) и высотой size, толщина штриха stroke.
        static float Digits(int number, float x, float y, float cx, float cy, float size, float stroke)
        {
            string s = number.ToString();
            float digitW = size * 0.56f;
            float gap = size * 0.12f;
            float total = s.Length * digitW + (s.Length - 1) * gap;
            float left = cx - total * 0.5f;
            float best = float.MaxValue;
            for (int i = 0; i < s.Length; i++)
            {
                float ox = left + i * (digitW + gap);
                float u = (x - ox) / digitW;
                float v = (y - (cy - size * 0.5f)) / size;
                // Расстояние в единицах высоты: x масштабируется шириной цифры.
                best = Mathf.Min(best, Digit(s[i] - '0', u * 0.56f, v) * size);
            }
            return best - stroke * size * 0.5f;
        }

        /// Цифра в поле шириной 0.56 и высотой 1 (низ — 0).
        static float Digit(int d, float x, float y)
        {
            const float l = 0.06f, r = 0.5f, b = 0.06f, t = 0.94f;
            float cx = (l + r) * 0.5f;
            float rx = (r - l) * 0.5f;
            switch (d)
            {
                case 0:
                    return Mathf.Abs(Box(x, y, cx, 0.5f, rx - 0.17f, 0.27f) - 0.17f);
                case 1:
                    return Mathf.Min(Segment(x, y, new Vector2(0.32f, b), new Vector2(0.32f, t)), Segment(x, y, new Vector2(0.32f, t), new Vector2(0.14f, 0.78f)));
                case 2:
                    return Mathf.Min(ArcE(x, y, new Vector2(cx, 0.72f), rx, 0.22f, -35f, 180f),
                        Mathf.Min(Segment(x, y, new Vector2(cx + rx * 0.82f, 0.59f), new Vector2(l, b)), Segment(x, y, new Vector2(l, b), new Vector2(r, b))));
                case 3:
                    return Mathf.Min(ArcE(x, y, new Vector2(cx, 0.73f), rx, 0.21f, -90f, 160f), ArcE(x, y, new Vector2(cx, 0.28f), rx, 0.22f, -160f, 90f));
                case 4:
                    return Mathf.Min(Segment(x, y, new Vector2(0.4f, b), new Vector2(0.4f, t)),
                        Mathf.Min(Segment(x, y, new Vector2(0.4f, t), new Vector2(l, 0.32f)), Segment(x, y, new Vector2(l, 0.32f), new Vector2(r, 0.32f))));
                case 5:
                    return Mathf.Min(Segment(x, y, new Vector2(r, t), new Vector2(l + 0.02f, t)),
                        Mathf.Min(Segment(x, y, new Vector2(l + 0.02f, t), new Vector2(l, 0.55f)), ArcE(x, y, new Vector2(cx, 0.32f), rx, 0.26f, -150f, 120f)));
                case 6:
                    return Mathf.Min(Mathf.Abs(Ellipse(x, y, cx, 0.3f, rx, 0.24f)), ArcE(x, y, new Vector2(cx + 0.02f, 0.52f), rx, 0.42f, 60f, 180f));
                case 7:
                    return Mathf.Min(Segment(x, y, new Vector2(l, t), new Vector2(r, t)), Segment(x, y, new Vector2(r, t), new Vector2(0.18f, b)));
                case 8:
                    return Mathf.Min(Mathf.Abs(Ellipse(x, y, cx, 0.73f, rx * 0.86f, 0.21f)), Mathf.Abs(Ellipse(x, y, cx, 0.28f, rx, 0.23f)));
                default:
                    return Mathf.Min(Mathf.Abs(Ellipse(x, y, cx, 0.7f, rx, 0.24f)), ArcE(x, y, new Vector2(cx - 0.02f, 0.48f), rx, 0.42f, -120f, 0f));
            }
        }

        /// Дуга эллипса — для цифр.
        static float ArcE(float x, float y, Vector2 c, float rx, float ry, float a0, float a1)
        {
            float best = float.MaxValue;
            int steps = 28;
            Vector2 prev = c + new Vector2(Mathf.Cos(a0 * Mathf.Deg2Rad) * rx, Mathf.Sin(a0 * Mathf.Deg2Rad) * ry);
            for (int i = 1; i <= steps; i++)
            {
                float a = Mathf.Lerp(a0, a1, (float)i / steps) * Mathf.Deg2Rad;
                Vector2 cur = c + new Vector2(Mathf.Cos(a) * rx, Mathf.Sin(a) * ry);
                best = Mathf.Min(best, Segment(x, y, prev, cur));
                prev = cur;
            }
            return best;
        }
    }
}
