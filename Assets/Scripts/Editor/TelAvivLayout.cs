using RandomCar.Track;
using UnityEngine;

namespace RandomCar.EditorTools
{
    /// Планировка «Тель-Авива» v2 (ночь), трасса ~3.6 км по часовой стрелке, ось x — на восток, z — на север:
    ///  1. Приморская дорога вдоль пляжа (x = −520) на север — длинная скоростная прямая; рядом набережная (альтернативный путь
    ///     с двумя трамплинами), затем вантовый мост над устьем реки (9 м над водой).
    ///  2. Портовый проспект на восток (z = 820): слева марина и маяк, справа парк у реки — открытый вид на город;
    ///     дорога поднимается на эстакаду.
    ///  3. Скоростная эстакада на юг (12 м) широкими дугами вдоль делового центра с небоскрёбами, второй мост над рекой.
    ///  4. Спуск в тоннель под парком.
    ///  5. Бульвар на запад: аллея фикусов, площадь с фонтаном.
    ///  6. Старый Яффо: тесные повороты на холме, часовая башня, спуск к морю на старт.
    public static class TelAvivLayout
    {
        public const float RoadHalf = 11f;
        public const float SidewalkOuter = 14.5f;
        public const float WallLateral = 14.2f;
        /// Полотно на эстакаде: от оси до внешней стороны парапета.
        public const float DeckHalf = 12.4f;
        public const float ParapetInner = 11.4f;
        public const float SeaX = -632f;
        public const float CoastX = -520f;
        public const float PromenadeInner = -531.6f;
        public const float PromenadeOuter = -548f;
        public const float PromenadeZ0 = -420f;
        public const float PromenadeZ1 = 300f;
        public const float RiverZ0 = 556f;
        public const float RiverZ1 = 644f;
        public const float RiverLevel = -1.6f;
        public const float TunnelZ0 = -270f;
        public const float TunnelZ1 = -90f;
        public const float TunnelHeight = 8.5f;
        public const float TunnelHalf = 13.2f;

        public static readonly Vector2[] Corners =
        {
            new Vector2(-520f, -470f), new Vector2(-508f, 170f), new Vector2(-520f, 820f), new Vector2(180f, 820f),
            new Vector2(215f, 450f), new Vector2(165f, 120f), new Vector2(190f, -330f), new Vector2(-190f, -330f),
            new Vector2(-235f, -420f), new Vector2(-330f, -395f), new Vector2(-400f, -470f),
        };
        public static readonly float[] Radii = { 40f, 320f, 60f, 70f, 260f, 260f, 55f, 32f, 24f, 22f, 26f };

        public enum District { Seafront, Port, Business, WhiteCity, Boulevard, Jaffa }

        public static District DistrictAt(Vector3 p)
        {
            if (p.z < -360f && p.x < -150f) return District.Jaffa;
            if (p.z < -290f) return District.Boulevard;
            if (p.x < -440f) return District.Seafront;
            if (p.z > 700f) return District.Port;
            if (p.x > 160f) return District.Business;
            return District.WhiteCity;
        }

        static float Smooth(float a, float b, float x) => Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(a, b, x));

        /// Трапеция: 0 до a, рост к b, плато до c, спад к d.
        static float Plateau(float x, float a, float b, float c, float d) => Mathf.Min(Smooth(a, b, x), 1f - Smooth(c, d, x));

        /// Высота полотна в точке плана: мост на побережье, эстакада, холм Яффо. Остальное — уровень земли.
        public static float ProfileHeight(Vector2 p)
        {
            if (p.x < -440f && p.y > 0f) return 9f * Plateau(p.y, 330f, 470f, 700f, 790f);
            if (p.y > 700f) return 12f * Smooth(-110f, 40f, p.x);
            if (p.x > 100f) return 12f * Smooth(-70f, 80f, p.y);
            if (p.y < -335f && p.x < -170f) return JaffaHill(p);
            return 0f;
        }

        public static readonly Vector2 JaffaTop = new Vector2(-322f, -408f);

        /// Холм старого Яффо: дорога и дома стоят на рельефе (не насыпь над плоской землёй).
        public static float JaffaHill(Vector2 p) => 6f * (1f - Smooth(30f, 130f, Vector2.Distance(p, JaffaTop)));

        /// В районе холма полотно не «поднято»: вокруг тот же рельеф, парапетов и опор нет.
        public static bool OnJaffaHill(Vector3 p) => p.z < -330f && p.x < -165f && Vector2.Distance(new Vector2(p.x, p.z), JaffaTop) < 140f;

        /// Скользящее среднее высоты по соседним точкам: ни изломов профиля, ни ступенек на стыках.
        public static void SmoothHeights(Vector3[] points, int window, int passes)
        {
            int n = points.Length;
            var y = new float[n];
            for (int pass = 0; pass < passes; pass++)
            {
                for (int i = 0; i < n; i++)
                {
                    float sum = 0f;
                    for (int k = -window; k <= window; k++) sum += points[((i + k) % n + n) % n].y;
                    y[i] = sum / (2 * window + 1);
                }
                for (int i = 0; i < n; i++) points[i].y = y[i];
            }
        }

        public static bool InTunnel(Vector3 p) => p.x > 100f && p.z > TunnelZ0 && p.z < TunnelZ1;

        /// Со стороны моря на приморской прямой — открытый выезд на набережную (стены нет).
        public static bool PromenadeOpen(Vector3 roadPoint, int side) =>
            side < 0 && roadPoint.x < -480f && roadPoint.z > PromenadeZ0 && roadPoint.z < PromenadeZ1;

        public static float Height(TrackPath path, float distance) => RoadMeshes.At(path, distance, 0f).y;

        /// Полотно поднято над землёй: вместо тротуара — парапет и стенка насыпи.
        public static bool Elevated(TrackPath path, float distance)
        {
            var p = RoadMeshes.At(path, distance, 0f);
            return p.y > 0.6f && !OnJaffaHill(p);
        }
    }
}
