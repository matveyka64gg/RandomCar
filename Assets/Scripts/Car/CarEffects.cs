using RandomCar.Events;
using UnityEngine;

namespace RandomCar.Car
{
    /// Нелепое управление на время (хаос v5): руль наоборот, икота, чих, балерина, «пьяный» руль.
    public enum ControlGag { None, Inverted, Hiccups, Sneeze, Ballet, Wobbly }

    /// Временные эффекты событий на машину. Пределы зависят от Хаоса (ChaosPower): в «умеренно» — честные
    /// пределы v4 (FairnessLimits), в «безумии» и «абсурде» — мультяшные. Эффекты всегда временные и не меняют
    /// характеристики машины навсегда. Физика читает отсюда множители на каждый шаг.
    public sealed class CarEffects
    {
        /// Ускорение «рывка», м/с² (при +25%).
        const float BoostAccel = 5.5f;

        float slowUntil;
        float slowCap;
        float boostUntil;
        float boostFactor = 1f;
        float slipUntil;
        float slipGrip = 0.5f;
        float gravityUntil;
        float gravityScale = 1f;
        float visualUntil;
        Vector3 visualTarget = Vector3.one;
        float tumbleUntil;
        float controlUntil;
        float controlFrom;

        public bool IsSlowed => Time.time < slowUntil;
        public bool IsBoosted => Time.time < boostUntil;
        public bool IsSlipping => Time.time < slipUntil;

        /// Свободный кувырок после сильного удара: машину не выравнивает в воздухе.
        public bool Tumbling => Time.time < tumbleUntil;

        /// Нелепое управление сейчас (None — нет).
        public ControlGag Control { get; private set; }

        /// Сколько секунд уже действует нелепое управление.
        public float ControlTime => Time.time - controlFrom;

        public bool ControlActive => Control != ControlGag.None && Time.time < controlUntil;

        /// Доля силы тяжести (1 — обычная, меньше — невесомость, минус — тянет вверх).
        public float GravityScale => Time.time < gravityUntil ? gravityScale : 1f;

        /// Масштаб вида машины (ворота-уменьшатель, сплющивание наковальней): только визуал, физика та же.
        public float VisualScale => VisualScale3.x;

        public Vector3 VisualScale3 => Time.time < visualUntil ? visualTarget : Vector3.one;

        /// Потолок скорости при замедлении, м/с (бесконечность — нет замедления).
        public float SlowCap => IsSlowed ? slowCap : float.PositiveInfinity;

        /// Множитель сцепления шин («скользко», масло).
        public float GripMultiplier => IsSlipping ? slipGrip : 1f;

        /// Дополнительная тяга рывка, м/с², и потолок скорости под рывком.
        public float BoostAcceleration => IsBoosted ? BoostAccel * (boostFactor - 1f) / (FairnessLimits.MaxBoostFactor - 1f) : 0f;
        public float BoostFactor => IsBoosted ? boostFactor : 1f;

        static ChaosPower Power => ChaosPower.Current;

        public void Clear()
        {
            slowUntil = boostUntil = slipUntil = gravityUntil = visualUntil = tumbleUntil = controlUntil = 0f;
            boostFactor = 1f;
            gravityScale = 1f;
            slipGrip = 0.5f;
            visualTarget = Vector3.one;
            slowCap = float.PositiveInfinity;
            Control = ControlGag.None;
        }

        /// Замедление: скорость падает не ниже factor от текущей (предел — ChaosPower.MinSlow), не дольше 1.5 с.
        public void Slow(float factor, float seconds, float currentSpeed)
        {
            float min = Power.MinSlow;
            factor = Mathf.Clamp(factor, min, 1f);
            seconds = Mathf.Clamp(seconds, 0f, FairnessLimits.MaxSlowSeconds);
            float cap = Mathf.Max(3f, Mathf.Abs(currentSpeed) * factor);
            slowCap = IsSlowed ? Mathf.Max(Mathf.Min(slowCap, cap), Mathf.Abs(currentSpeed) * min) : cap;
            slowUntil = Mathf.Min(Mathf.Max(slowUntil, Time.time + seconds), Time.time + FairnessLimits.MaxSlowSeconds);
        }

        /// Рывок: ровно 2 с, не больше ChaosPower.MaxBoost к ограничителю скорости.
        public void Boost(float factor)
        {
            factor = Mathf.Clamp(factor, 1f, Power.MaxBoost);
            boostFactor = IsBoosted ? Mathf.Max(boostFactor, factor) : factor;
            boostUntil = Time.time + FairnessLimits.BoostSeconds;
        }

        /// Турбо (бонус): разгон сильнее обычного рывка и не зависит от пределов Хаоса.
        public void Turbo(float factor, float seconds)
        {
            boostFactor = Mathf.Max(IsBoosted ? boostFactor : 1f, factor);
            boostUntil = Mathf.Max(boostUntil, Time.time + seconds);
        }

        /// Невесомость, пока машина в зоне: зона обновляет эффект каждый кадр (держится 0.4 с после выезда).
        public void LowGravity(float scale) => Gravity(scale, 0.4f);

        /// Сила тяжести на время: невесомость или «гравитационный сбой» (минус — машину тянет вверх).
        public void Gravity(float scale, float seconds)
        {
            gravityScale = Mathf.Clamp(scale, Mathf.Min(Power.MinGravity, 1f), 1f);
            gravityUntil = Mathf.Max(gravityUntil, Time.time + Mathf.Clamp(seconds, 0f, 4f));
        }

        /// Вид машины меньше (или больше) на время, не дольше MaxVisualScaleSeconds. Только визуал.
        public void ScaleVisual(float scale, float seconds) => ScaleVisual(Vector3.one * Mathf.Clamp(scale, 0.5f, 1.4f), seconds);

        /// Неравномерный масштаб (сплющило наковальней, вытянуло жвачкой). Только визуал.
        public void ScaleVisual(Vector3 scale, float seconds)
        {
            visualTarget = new Vector3(Mathf.Clamp(scale.x, 0.3f, 1.8f), Mathf.Clamp(scale.y, 0.25f, 1.8f), Mathf.Clamp(scale.z, 0.3f, 1.8f));
            visualUntil = Time.time + Mathf.Clamp(seconds, 0f, FairnessLimits.MaxVisualScaleSeconds);
        }

        /// Скользко: сцепление падает (grip — доля, масло — сильнее), не дольше 1.5 с. Занос получается из физики шин.
        public void Slip(float seconds, float grip = 0.5f)
        {
            seconds = Mathf.Clamp(seconds, 0f, FairnessLimits.MaxSlipSeconds);
            slipGrip = IsSlipping ? Mathf.Min(slipGrip, Mathf.Clamp(grip, 0.1f, 1f)) : Mathf.Clamp(grip, 0.1f, 1f);
            slipUntil = Mathf.Min(Mathf.Max(slipUntil, Time.time + seconds), Time.time + FairnessLimits.MaxSlipSeconds);
        }

        /// Кувырок после удара: на это время машину не выравнивает в воздухе.
        public void Tumble(float seconds)
        {
            if (seconds <= 0f) return;
            tumbleUntil = Mathf.Max(tumbleUntil, Time.time + seconds);
        }

        /// Нелепое управление на время (не дольше 4 с).
        public void SetControl(ControlGag gag, float seconds)
        {
            Control = gag;
            controlFrom = Time.time;
            controlUntil = Time.time + Mathf.Clamp(seconds, 0f, 4f);
        }

        /// Управление водителя после нелепого эффекта (игрок и ИИ — одинаково).
        public CarInput Filter(CarInput input)
        {
            if (!ControlActive)
            {
                Control = ControlGag.None;
                return input;
            }
            float t = ControlTime;
            switch (Control)
            {
                case ControlGag.Inverted:
                    input.Steer = -input.Steer;
                    break;
                case ControlGag.Wobbly:
                    input.Steer = Mathf.Clamp(input.Steer * 0.6f + Mathf.Sin(t * 7.5f) * 0.75f, -1f, 1f);
                    break;
                case ControlGag.Ballet:
                    input.Steer = 1f;
                    input.Handbrake = true;
                    input.Throttle = 1f;
                    break;
            }
            return input;
        }
    }
}
