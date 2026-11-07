using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Events
{
    /// Минное поле: мины выпрыгивают из асфальта рядами, в каждом ряду открыт проезд (слалом, диагональ или «клин»).
    /// Мины мигают и пищат (чаще, когда машина рядом). Среди настоящих бывают ложные (конфетти и «пшик»),
    /// прыгающие (отскакивают в сторону при подъезде), «призраки» (пропадают и появляются) и с задержкой.
    /// Взрыв подбрасывает машину и закручивает в занос, ударная волна поджигает соседние мины (цепочка)
    /// и отбрасывает шары и бочки. Держится, пока игрок не проедет.
    public sealed class MinefieldEvent : RaceEvent
    {
        public const int MaxMines = 10;

        MineField field;
        TelegraphMarker marker;
        int used;
        float activeStart;

        void Awake()
        {
            field = new MineField(this, (code, index, value) =>
            {
                Signal(code, index, value);
                return true;
            });
            for (int i = 0; i < MaxMines; i++) field.Bind(Child("Mine_" + i));
            marker = Child("Marker").GetComponent<TelegraphMarker>();
        }

        protected override void OnTelegraph()
        {
            float length = Context.Track.Length;
            int pattern = Mathf.FloorToInt(RandomRange(0f, 2.999f));
            int rows = RoadHalf > 8f ? 4 : 3;
            var layout = MineField.Layout(RoadHalf, rows, ZoneLength / (rows + 0.5f), pattern, RandomRange, MaxMines);
            used = layout.Count;
            for (int i = 0; i < used; i++)
            {
                float progress = Progress + layout[i].x / length;
                var h = At(progress, layout[i].y);
                // Виды мин: в начале гонки почти все настоящие, к финалу — больше сюрпризов.
                float roll = RandomRange(0f, 1f);
                var type = roll < 0.58f ? MineType.Real : roll < 0.72f ? MineType.Fake : roll < 0.84f ? MineType.Hopper : roll < 0.93f ? MineType.Ghost : MineType.Delayed;
                field.Place(i, Ground(h.Position) + Vector3.up * 0.02f, h.Right, h.Forward, progress, layout[i].y, type, RandomRange(0f, 1f));
            }
            for (int i = used; i < MaxMines; i++) field.Hide(i);
            var c = At(Progress, 0f);
            Center = c.Position;
            Radius = MaxRadius;
            marker.Show(c.Position, c.Forward, RoadHalf * 0.9f, false, false, ZoneLength * 0.5f / (RoadHalf * 0.9f));
        }

        protected override void OnTelegraphUpdate(float progress, float time)
        {
            marker.Pulse(progress);
            field.Rise(progress, used);
        }

        protected override void OnActivate()
        {
            marker.Hide();
            field.Arm(used);
            activeStart = Time.time;
        }

        protected override void OnActiveUpdate(float progress, float time) =>
            field.Update(Time.time - activeStart, used, Racers, Authority);

        protected override void OnSignal(int code, int index, Vector3 value)
        {
            if (code == MineField.SignalTrigger) field.Trigger(index);
        }

        protected override void OnEndUpdate(float progress)
        {
            // Оставшиеся мины уходят в асфальт.
            field.Rise(1f - progress, used);
        }

        protected override void Cleanup()
        {
            marker.Hide();
            field.Clear();
        }

        public override void CollectHazards(List<HazardInfo> list)
        {
            if (Stage != EventStage.Telegraph && Stage != EventStage.Active) return;
            field.CollectHazards(list, used, StartedAt);
        }

        public override void CollectBlocks(List<RoadBlock> list)
        {
            if (Stage == EventStage.Active) field.CollectBlocks(list, used);
        }
    }
}
