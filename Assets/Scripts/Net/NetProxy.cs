using System;
using RandomCar.Car;
using UnityEngine;

namespace RandomCar.Net
{
    /// Чужая машина в сетевой гонке: кинематическое тело идёт по снимкам владельца с задержкой интерполяции.
    /// Колёса, обороты, турбина и дым считаются по присланным скорости и вводу (CarPhysics.RemoteStep).
    /// Большой разрыв (возврат на трассу, рестарт) — перенос без «пролёта» через карту.
    [RequireComponent(typeof(CarPhysics))]
    public sealed class NetProxy : MonoBehaviour
    {
        const float TeleportDistance = 12f;

        readonly SnapshotBuffer buffer = new SnapshotBuffer();
        CarPhysics physics;
        Func<double> clock;
        bool placed;
        CarController controller;

        public SnapshotBuffer Buffer => buffer;

        /// Сколько кадров подряд показ шёл без свежих данных (экстраполяция): для отладки соединения.
        public int ExtrapolatedFrames { get; private set; }

        public void Init(Func<double> serverTime)
        {
            clock = serverTime;
            physics = GetComponent<CarPhysics>();
            physics.SetSimulated(false);
            buffer.Clear();
            placed = false;
        }

        public void Push(CarState state) => buffer.Push(state);

        void FixedUpdate()
        {
            if (clock == null || !buffer.HasData) return;
            double renderTime = clock() - NetProtocol.InterpolationDelay;
            if (!buffer.Sample(renderTime, out var s, out bool extrapolated)) return;
            ExtrapolatedFrames = extrapolated ? ExtrapolatedFrames + 1 : 0;

            var body = physics.Body;
            if (!placed || (body.position - s.Position).sqrMagnitude > TeleportDistance * TeleportDistance)
            {
                body.position = s.Position;
                body.rotation = s.Rotation;
                transform.SetPositionAndRotation(s.Position, s.Rotation);
                placed = true;
            }
            else
            {
                body.MovePosition(s.Position);
                body.MoveRotation(s.Rotation);
            }
            physics.RemoteStep(s.Velocity, s.Input, s.Locked, Time.fixedDeltaTime);
            // Защита чужой машины — только показ (силы к ней применяет её владелец).
            if (controller == null) controller = GetComponent<CarController>();
            if (controller != null)
            {
                controller.RemoteProtected = s.Protected;
                controller.RemoteShield = s.Shield;
            }
        }
    }
}
