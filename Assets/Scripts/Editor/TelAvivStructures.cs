using System.Collections.Generic;
using RandomCar.Car;
using RandomCar.Track;
using UnityEngine;
using UnityEngine.Rendering;
using static RandomCar.EditorTools.BuilderKit;
using static RandomCar.EditorTools.TelAvivLayout;
using MeshBuilder = RandomCar.EditorTools.TrackMeshBuilder.MeshBuilder;

namespace RandomCar.EditorTools
{
    /// Сооружения «Тель-Авива», которые строятся по трассе в редакторе: широкая разметка на 4 полосы, тротуары,
    /// невидимые стены, парапеты и стенки эстакады/моста, опоры, вантовый мост, тоннель с парком сверху, река.
    /// Всё сохраняется мешами в Assets/Art/Generated/TelAviv.
    public static class TelAvivStructures
    {
        const string Folder = "TelAviv";
        const float Step = 2f;

        static Vector3 At(TrackPath path, float d, float lateral, float y = 0f) => RoadMeshes.At(path, d, lateral, y);

        static Mesh Named(Mesh mesh, string name)
        {
            mesh.name = name;
            return mesh;
        }

        // ---------- разметка ----------

        /// Четыре полосы: сплошные кромки, три прерывистые линии между полосами, решётка и стоп-линия у старта.
        public static Mesh Markings(TrackPath path, float[] gridDistances, float[] gridLaterals)
        {
            var b = new MeshBuilder("TA2_Markings", 1);
            const float y = 0.014f;
            float length = path.Length;
            void Strip(float d0, float d1, float lateral, float width)
            {
                for (float d = d0; d < d1; d += Step)
                {
                    float e = Mathf.Min(d + Step, d1);
                    b.QuadUpPoints(0, At(path, d, lateral - width / 2, y), At(path, d, lateral + width / 2, y), At(path, e, lateral + width / 2, y), At(path, e, lateral - width / 2, y));
                }
            }
            foreach (float side in new[] { -1f, 1f }) Strip(0f, length, side * (RoadHalf - 0.5f), 0.22f);
            foreach (float lane in new[] { -RoadHalf * 0.5f, 0f, RoadHalf * 0.5f })
                for (float d = 30f; d < length - 60f; d += 12f) Strip(d, d + 4.5f, lane, 0.18f);
            for (int s = 0; s < gridDistances.Length; s++)
            {
                float d = length - gridDistances[s];
                Strip(d + 2.3f, d + 2.5f, gridLaterals[s], 2.4f);
                foreach (float side in new[] { -1f, 1f }) Strip(d + 1.1f, d + 2.45f, gridLaterals[s] + side * 1.2f, 0.14f);
            }
            return b.Build();
        }

        /// Шахматная линия старта во всю ширину дороги.
        public static Mesh StartLine(TrackPath path)
        {
            var b = new MeshBuilder("TA2_StartLine", 2);
            const float y = 0.016f;
            int columns = 18;
            float cell = 2f * RoadHalf / columns;
            for (int row = 0; row < 2; row++)
            {
                float d0 = -1.1f + row * 1.1f;
                for (int c = 0; c < columns; c++)
                {
                    float x0 = -RoadHalf + c * cell;
                    int sub = (c + row) % 2;
                    b.QuadUpPoints(sub, At(path, d0, x0, y), At(path, d0, x0 + cell, y), At(path, d0 + 1.1f, x0 + cell, y), At(path, d0 + 1.1f, x0, y));
                }
            }
            return b.Build();
        }

        // ---------- тротуары и стены ----------

        /// Тротуары с бордюром там, где дорога на земле (не на эстакаде, не в тоннеле, не у набережной).
        public static Mesh Sidewalks(TrackPath path)
        {
            var b = new MeshBuilder("TA2_Sidewalks", 2);
            const float h = 0.15f;
            float length = path.Length;
            foreach (int side in new[] { -1, 1 })
                for (float d = 0f; d < length; d += Step)
                {
                    float e = Mathf.Min(d + Step, length), m = (d + e) * 0.5f;
                    Vector3 axis = At(path, m, 0f);
                    if (Elevated(path, m) || InTunnel(axis) || PromenadeOpen(axis, side)) continue;
                    Vector3 in0 = At(path, d, side * RoadHalf, h), out0 = At(path, d, side * SidewalkOuter, h);
                    Vector3 in1 = At(path, e, side * RoadHalf, h), out1 = At(path, e, side * SidewalkOuter, h);
                    b.QuadUpPoints(0, in0, out0, out1, in1, worldUv: 2f);
                    Vector3 face = At(path, d, 0f) - At(path, d, side, 0f);
                    b.QuadFacing(1, in0 - Vector3.up * h, in1 - Vector3.up * h, in1, in0, face);
                }
            return b.Build();
        }

        /// Невидимые стены: на земле — за тротуаром, на эстакаде и мосту — по парапету, в тоннеле — по стенам.
        /// У набережной со стороны моря стены нет: туда можно съехать.
        public static Mesh Walls(TrackPath path)
        {
            var b = new MeshBuilder("TA2_Walls", 1);
            const float h = 5f;
            float length = path.Length;
            foreach (int side in new[] { -1, 1 })
                for (float d = 0f; d < length; d += Step)
                {
                    float e = Mathf.Min(d + Step, length), m = (d + e) * 0.5f;
                    Vector3 axis = At(path, m, 0f);
                    if (PromenadeOpen(axis, side)) continue;
                    float lateral = Elevated(path, m) ? ParapetInner + 0.1f : InTunnel(axis) ? TunnelHalf - 0.3f : WallLateral;
                    Vector3 a = At(path, d, side * lateral, -1f), c = At(path, e, side * lateral, -1f);
                    Vector3 face = At(path, d, 0f) - a;
                    face.y = 0f;
                    b.QuadFacing(0, a, c, c + Vector3.up * h, a + Vector3.up * h, face);
                }
            return b.Build();
        }

        // ---------- эстакада и мост ----------

        /// Поднятое полотно: парапеты (бетон с неоновой лентой), стенки и низ плиты; на невысоких участках
        /// стенка доходит до земли (насыпь). Возвращает точки для опор (каждые 32 м, где высота > 4 м).
        public static List<(Vector3 position, Vector3 forward, float height)> ElevatedDeck(TrackPath path, GameObject parent)
        {
            var parapet = new MeshBuilder("TA2_Parapets", 2);
            var deck = new MeshBuilder("TA2_DeckSides", 1);
            var piers = new List<(Vector3, Vector3, float)>();
            float length = path.Length;
            float nextPier = 0f;
            for (float d = 0f; d < length; d += Step)
            {
                float e = Mathf.Min(d + Step, length), m = (d + e) * 0.5f;
                if (!Elevated(path, m)) continue;
                float h0 = Height(path, d), h1 = Height(path, e);
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 inward = At(path, d, 0f) - At(path, d, side, 0f);
                    inward.y = 0f;
                    // Парапет: внутренняя грань, верх, внешняя грань; неоновая лента по верху.
                    Vector3 a0 = At(path, d, side * ParapetInner), a1 = At(path, e, side * ParapetInner);
                    Vector3 o0 = At(path, d, side * DeckHalf), o1 = At(path, e, side * DeckHalf);
                    Vector3 up = Vector3.up * 1.1f;
                    parapet.QuadFacing(0, a0, a1, a1 + up, a0 + up, inward);
                    parapet.QuadFacing(0, a0 + up, a1 + up, o1 + up, o0 + up, Vector3.up);
                    parapet.QuadFacing(0, o0, o1, o1 + up, o0 + up, -inward);
                    Vector3 lift = Vector3.up * 1.12f;
                    Vector3 s0 = At(path, d, side * (ParapetInner + 0.35f)), s1 = At(path, e, side * (ParapetInner + 0.35f));
                    Vector3 t0 = At(path, d, side * (ParapetInner + 0.65f)), t1 = At(path, e, side * (ParapetInner + 0.65f));
                    parapet.QuadFacing(1, s0 + lift, s1 + lift, t1 + lift, t0 + lift, Vector3.up);
                    // Стенка плиты: на высоте — 1.4 м вниз, на насыпи — до земли.
                    float drop0 = h0 > 3.5f ? 1.4f : h0, drop1 = h1 > 3.5f ? 1.4f : h1;
                    deck.QuadFacing(0, o0 - Vector3.up * drop0, o1 - Vector3.up * drop1, o1, o0, -inward);
                }
                if (h0 > 3.5f)
                {
                    Vector3 l0 = At(path, d, -DeckHalf, -1.4f), r0 = At(path, d, DeckHalf, -1.4f);
                    Vector3 l1 = At(path, e, -DeckHalf, -1.4f), r1 = At(path, e, DeckHalf, -1.4f);
                    deck.QuadFacing(0, l0, r0, r1, l1, Vector3.down);
                }
                if (h0 > 4f && d >= nextPier)
                {
                    Vector3 p = At(path, d, 0f);
                    bool overRiver = p.z > RiverZ0 - 6f && p.z < RiverZ1 + 6f && p.x < -440f;
                    if (!overRiver) piers.Add((new Vector3(p.x, 0f, p.z), path.DirectionAt(TrackPath.Wrap(d / length)), h0));
                    nextPier = d + 32f;
                }
            }
            var parapets = Place(SaveMesh(parapet.Build(), Folder), "Parapets", parent.transform, "M_Concrete", "M_NeonBlue");
            parapets.AddComponent<MeshCollider>();
            Place(SaveMesh(deck.Build(), Folder), "DeckSides", parent.transform, "M_Concrete");
            return piers;
        }

        /// Ванты моста: от головы пилона к кромкам полотна веером по обе стороны пилона.
        public static void BridgeCables(TrackPath path, GameObject parent, float pylonDistance, Vector3 head)
        {
            var b = new MeshBuilder("TA2_Cables", 1);
            for (int k = 1; k <= 9; k++)
                foreach (int dir in new[] { -1, 1 })
                    foreach (int side in new[] { -1, 1 })
                    {
                        float d = pylonDistance + dir * k * 11f;
                        Vector3 anchor = At(path, d, side * (DeckHalf - 0.3f), 1.2f);
                        Vector3 top = head + Vector3.up * (-k * 1.2f) + path.SideAt(TrackPath.Wrap(pylonDistance / path.Length)) * side * 1.6f;
                        Vector3 axis = (anchor - top).normalized;
                        Vector3 w1 = Vector3.Cross(axis, Vector3.up).normalized * 0.16f;
                        Vector3 w2 = Vector3.Cross(axis, w1).normalized * 0.16f;
                        b.QuadFacing(0, top - w1, top + w1, anchor + w1, anchor - w1, w2);
                        b.QuadFacing(0, top - w1, top + w1, anchor + w1, anchor - w1, -w2);
                        b.QuadFacing(0, top - w2, top + w2, anchor + w2, anchor - w2, w1);
                        b.QuadFacing(0, top - w2, top + w2, anchor + w2, anchor - w2, -w1);
                    }
            // Ванты подсвечены (тёплый белый): ночью мост читается силуэтом издалека.
            var go = Place(SaveMesh(b.Build(), Folder), "BridgeCables", parent.transform, "M_LampGlow");
            go.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        // ---------- тоннель ----------

        /// Тоннель: стены и потолок внутрь, световые ленты на потолке; сверху — парк-холм с травой.
        /// Возвращает точки входа и выхода (для порталов) и точки светильников.
        public static (float enter, float exit, List<Vector3> lamps) Tunnel(TrackPath path, GameObject parent)
        {
            var shell = new MeshBuilder("TA2_Tunnel", 3);
            var lamps = new List<Vector3>();
            float length = path.Length;
            float enter = -1f, exit = -1f, nextLamp = 0f;
            for (float d = 0f; d < length; d += Step)
            {
                float e = Mathf.Min(d + Step, length), m = (d + e) * 0.5f;
                if (!InTunnel(At(path, m, 0f))) continue;
                if (enter < 0f) enter = d;
                exit = e;
                foreach (int side in new[] { -1, 1 })
                {
                    Vector3 inward = At(path, d, 0f) - At(path, d, side, 0f);
                    inward.y = 0f;
                    Vector3 w0 = At(path, d, side * TunnelHalf, -0.2f), w1 = At(path, e, side * TunnelHalf, -0.2f);
                    shell.QuadFacing(0, w0, w1, w1 + Vector3.up * (TunnelHeight + 0.2f), w0 + Vector3.up * (TunnelHeight + 0.2f), inward);
                    // Световые ленты: вдоль потолка над каждой половиной дороги и по стенам на высоте 1 м (неон).
                    Vector3 s0 = At(path, d, side * 4.6f, TunnelHeight - 0.12f), s1 = At(path, e, side * 4.6f, TunnelHeight - 0.12f);
                    Vector3 t0 = At(path, d, side * 6.2f, TunnelHeight - 0.12f), t1 = At(path, e, side * 6.2f, TunnelHeight - 0.12f);
                    shell.QuadFacing(1, s0, s1, t1, t0, Vector3.down);
                    Vector3 g0 = At(path, d, side * (TunnelHalf - 0.06f), 0.9f), g1 = At(path, e, side * (TunnelHalf - 0.06f), 0.9f);
                    shell.QuadFacing(2, g0, g1, g1 + Vector3.up * 0.35f, g0 + Vector3.up * 0.35f, inward);
                }
                Vector3 c0 = At(path, d, -TunnelHalf, TunnelHeight), c1 = At(path, d, TunnelHalf, TunnelHeight);
                Vector3 c2 = At(path, e, TunnelHalf, TunnelHeight), c3 = At(path, e, -TunnelHalf, TunnelHeight);
                shell.QuadFacing(0, c0, c1, c2, c3, Vector3.down);
                if (d >= nextLamp)
                {
                    lamps.Add(At(path, d, 0f, TunnelHeight - 0.6f));
                    nextLamp = d + 22f;
                }
            }
            var tunnel = Place(SaveMesh(shell.Build(), Folder), "Tunnel", parent.transform, "M_TunnelTile", "M_LampGlow", "M_NeonTeal");
            tunnel.GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.TwoSided;
            return (enter, exit, lamps);
        }

        /// Парк над тоннелем: мягкий холм травы, под которым проходит дорога.
        public static Mesh ParkMound(Vector3 center, float halfX, float halfZ, float height)
        {
            var b = new MeshBuilder("TA2_ParkMound", 1);
            int nx = 24, nz = 40;
            var grid = new int[nx + 1, nz + 1];
            for (int i = 0; i <= nx; i++)
                for (int j = 0; j <= nz; j++)
                {
                    float u = i / (float)nx * 2f - 1f, v = j / (float)nz * 2f - 1f;
                    float r = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
                    float y = height * Mathf.SmoothStep(1f, 0f, Mathf.InverseLerp(0.55f, 1f, r)) - 0.2f;
                    var p = center + new Vector3(u * halfX, y, v * halfZ);
                    grid[i, j] = b.Vertex(p, new Vector2(p.x / 6f, p.z / 6f));
                }
            for (int i = 0; i < nx; i++)
                for (int j = 0; j < nz; j++)
                    b.QuadUp(0, grid[i, j], grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j]);
            return b.Build(recalcNormals: true);
        }

        /// Рельеф холма Яффо: сетка вокруг вершины, чуть ниже дороги (полотно ложится поверх).
        public static Mesh JaffaTerrain(TrackPath path)
        {
            var b = new MeshBuilder("TA2_JaffaHill", 1);
            int n = 40;
            float size = 320f;
            var grid = new int[n + 1, n + 1];
            for (int i = 0; i <= n; i++)
                for (int j = 0; j <= n; j++)
                {
                    var p = new Vector2(JaffaTop.x - size / 2 + size * i / n, JaffaTop.y - size / 2 + size * j / n);
                    float y = JaffaHill(p) - 0.35f;
                    // Полотно сглажено и на вершине ниже рельефа: у дороги земля опускается под неё.
                    var world = new Vector3(p.x, 0f, p.y);
                    float progress = path.Nearest(world);
                    Vector3 axis = path.PointAt(progress);
                    float off = Vector2.Distance(new Vector2(axis.x, axis.z), p);
                    if (off < 40f) y = Mathf.Min(y, Mathf.Lerp(axis.y - 0.4f, y, Mathf.InverseLerp(16f, 40f, off)));
                    grid[i, j] = b.Vertex(new Vector3(p.x, Mathf.Max(-0.06f, y), p.y), new Vector2(p.x / 6f, p.y / 6f));
                }
            for (int i = 0; i < n; i++)
                for (int j = 0; j < n; j++)
                    b.QuadUp(0, grid[i, j], grid[i, j + 1], grid[i + 1, j + 1], grid[i + 1, j]);
            return b.Build(recalcNormals: true);
        }

        // ---------- земля, река, море ----------

        /// Суша полосами (без реки), берега реки, вода реки и моря; коллайдеры суши — ящиками.
        public static void GroundAndWater(GameObject parent, Dictionary<SurfaceKind, SurfaceType> surfaces)
        {
            var ground = new MeshBuilder("TA2_Ground", 1);
            const float far = 3200f;
            ground.QuadUpPoints(0, new Vector3(SeaX + 4f, -0.05f, -far), new Vector3(far, -0.05f, -far), new Vector3(far, -0.05f, RiverZ0), new Vector3(SeaX + 4f, -0.05f, RiverZ0), worldUv: 20f);
            ground.QuadUpPoints(0, new Vector3(SeaX + 4f, -0.05f, RiverZ1), new Vector3(far, -0.05f, RiverZ1), new Vector3(far, -0.05f, far), new Vector3(SeaX + 4f, -0.05f, far), worldUv: 20f);
            Place(SaveMesh(ground.Build(recalcNormals: true), Folder), "Ground", parent.transform, "M_CityGroundNight");
            foreach (var (z0, z1) in new[] { (-far, RiverZ0), (RiverZ1, far) })
            {
                var col = new GameObject("GroundCollider").AddComponent<BoxCollider>();
                col.transform.SetParent(parent.transform, false);
                col.center = new Vector3((SeaX + far) * 0.5f, -0.55f, (z0 + z1) * 0.5f);
                col.size = new Vector3(far - SeaX, 1f, z1 - z0);
                col.gameObject.AddComponent<Surface>().Type = surfaces[SurfaceKind.Asphalt];
            }
            // Берега: бетонные стенки от земли к воде, по обе стороны.
            var banks = new MeshBuilder("TA2_RiverBanks", 1);
            foreach (var (z, facing) in new[] { (RiverZ0, Vector3.forward), (RiverZ1, Vector3.back) })
                banks.QuadFacing(0, new Vector3(SeaX, RiverLevel - 0.5f, z), new Vector3(far, RiverLevel - 0.5f, z), new Vector3(far, 0f, z), new Vector3(SeaX, 0f, z), facing);
            Place(SaveMesh(banks.Build(), Folder), "RiverBanks", parent.transform, "M_Concrete");
            var water = new MeshBuilder("TA2_River", 1);
            water.QuadUpPoints(0, new Vector3(SeaX - 40f, RiverLevel, RiverZ0), new Vector3(far, RiverLevel, RiverZ0), new Vector3(far, RiverLevel, RiverZ1), new Vector3(SeaX - 40f, RiverLevel, RiverZ1), worldUv: 30f);
            Place(SaveMesh(water.Build(recalcNormals: true), Folder), "River", parent.transform, "M_Sea").GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
            var sea = new MeshBuilder("TA2_Sea", 1);
            sea.QuadUpPoints(0, new Vector3(SeaX, -0.45f, -far), new Vector3(-6000f, -0.45f, -far), new Vector3(-6000f, -0.45f, far), new Vector3(SeaX, -0.45f, far), worldUv: 40f);
            Place(SaveMesh(sea.Build(recalcNormals: true), Folder), "Sea", parent.transform, "M_Sea").GetComponent<Renderer>().shadowCastingMode = ShadowCastingMode.Off;
        }

        /// Набережная (брусчатка, параллельный путь), пляж до воды и стены кармана (у моря и по краям).
        public static void Promenade(GameObject parent, GameObject props, Dictionary<SurfaceKind, SurfaceType> surfaces)
        {
            var b = new MeshBuilder("TA2_Promenade", 1);
            for (float z = PromenadeZ0 - 40f; z < PromenadeZ1 + 20f; z += 40f)
                b.QuadUpPoints(0, new Vector3(PromenadeInner, 0.02f, z), new Vector3(PromenadeOuter, 0.02f, z), new Vector3(PromenadeOuter, 0.02f, z + 40f), new Vector3(PromenadeInner, 0.02f, z + 40f), worldUv: 3f);
            var promenade = Place(SaveMesh(b.Build(recalcNormals: true), Folder), "Promenade", parent.transform, "M_Cobble");
            promenade.AddComponent<MeshCollider>();
            promenade.AddComponent<Surface>().Type = surfaces[SurfaceKind.Cobble];
            var s = new MeshBuilder("TA2_Beach", 1);
            for (float z = -560f; z < RiverZ0; z += 40f)
            {
                float z1 = Mathf.Min(z + 40f, RiverZ0);
                s.QuadUpPoints(0, new Vector3(PromenadeOuter, 0f, z), new Vector3(SeaX - 10f, -0.9f, z), new Vector3(SeaX - 10f, -0.9f, z1), new Vector3(PromenadeOuter, 0f, z1), worldUv: 6f);
            }
            var beach = Place(SaveMesh(s.Build(recalcNormals: true), Folder), "Beach", parent.transform, "M_Sand");
            beach.AddComponent<MeshCollider>();
            beach.AddComponent<Surface>().Type = surfaces[SurfaceKind.Sand];
            // Карман набережной: кромка воды и поперечные стены у концов (машину ведёт обратно на дорогу).
            var wall = new MeshBuilder("TA2_ShoreWalls", 1);
            float zA = PromenadeZ0 - 30f, zB = PromenadeZ1 + 26f;
            for (float z = zA; z < zB; z += 40f)
                wall.QuadFacing(0, new Vector3(SeaX + 2f, -2f, z), new Vector3(SeaX + 2f, -2f, Mathf.Min(z + 40f, zB)), new Vector3(SeaX + 2f, 4f, Mathf.Min(z + 40f, zB)), new Vector3(SeaX + 2f, 4f, z), Vector3.right);
            wall.QuadFacing(0, new Vector3(SeaX + 2f, -2f, zB), new Vector3(CoastX - WallLateral, -2f, zB + 30f), new Vector3(CoastX - WallLateral, 4f, zB + 30f), new Vector3(SeaX + 2f, 4f, zB), Vector3.back);
            wall.QuadFacing(0, new Vector3(CoastX - WallLateral, -2f, zA - 30f), new Vector3(SeaX + 2f, -2f, zA), new Vector3(SeaX + 2f, 4f, zA), new Vector3(CoastX - WallLateral, 4f, zA - 30f), Vector3.forward);
            var walls = new GameObject("ShoreWalls");
            walls.transform.SetParent(parent.transform, false);
            walls.AddComponent<MeshCollider>().sharedMesh = SaveMesh(wall.Build(), Folder);
            // Трамплины спасателей — прыжок для тех, кто выбрал набережную.
            foreach (float z in new[] { -160f, 120f })
            {
                var r = new MeshBuilder($"TA2_Ramp_{z:F0}", 1);
                float x0 = -544f, x1 = -535f, len = 10f, h = 1.3f;
                Vector3 a0 = new Vector3(x0, 0.02f, z - len / 2), a1 = new Vector3(x1, 0.02f, z - len / 2);
                Vector3 c0 = new Vector3(x0, h, z + len / 2), c1 = new Vector3(x1, h, z + len / 2);
                Vector3 d0 = new Vector3(x0, 0.02f, z + len / 2), d1 = new Vector3(x1, 0.02f, z + len / 2);
                r.QuadUpPoints(0, a0, a1, c1, c0);
                r.QuadFacing(0, d0, d1, c1, c0, Vector3.forward);
                r.QuadFacing(0, a0, d0, c0, c0, Vector3.left);
                r.QuadFacing(0, d1, a1, c1, c1, Vector3.right);
                var ramp = Place(SaveMesh(r.Build(recalcNormals: true), Folder), $"Ramp_{z:F0}", props.transform, "M_Wood");
                ramp.AddComponent<MeshCollider>();
                ramp.AddComponent<Surface>().Type = surfaces[SurfaceKind.Asphalt];
            }
        }
    }
}
