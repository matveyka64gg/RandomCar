using UnityEngine;

namespace RandomCar.Events
{
    /// Пересылает столкновения тела автобуса его событию (коллайдеры — на теле, логика — в ElDriftoEvent).
    public sealed class DriftBusContact : MonoBehaviour
    {
        [SerializeField] ElDriftoEvent owner;

        public void Configure(ElDriftoEvent ev) => owner = ev;

        void OnCollisionEnter(Collision collision)
        {
            if (owner != null) owner.OnBusContact(collision, true);
        }

        void OnCollisionStay(Collision collision)
        {
            if (owner != null) owner.OnBusContact(collision, false);
        }
    }
}
