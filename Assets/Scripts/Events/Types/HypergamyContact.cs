using UnityEngine;

namespace RandomCar.Events
{
    /// Триггер карточки ГИПЕРГАМИИ: касание машины передаётся событию (решает событие: только своя машина игрока).
    public sealed class HypergamyContact : MonoBehaviour
    {
        [SerializeField] HypergamyEvent owner;

        public void Configure(HypergamyEvent ev) => owner = ev;

        void OnTriggerEnter(Collider other)
        {
            if (owner != null) owner.OnTouch(other);
        }

        // Машина могла въехать в триггер, пока он был выключен (карточка ещё вырастала) — касание засчитывается и так.
        void OnTriggerStay(Collider other)
        {
            if (owner != null) owner.OnTouch(other);
        }
    }
}
