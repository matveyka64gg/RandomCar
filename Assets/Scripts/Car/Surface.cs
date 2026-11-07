using System.Collections.Generic;
using UnityEngine;

namespace RandomCar.Car
{
    /// Метка покрытия на коллайдере. Колёса узнают покрытие по коллайдеру, в который упёрся луч подвески.
    [DisallowMultipleComponent]
    public sealed class Surface : MonoBehaviour
    {
        public SurfaceType Type;

        static readonly Dictionary<Collider, SurfaceType> cache = new Dictionary<Collider, SurfaceType>();

        /// Покрытие коллайдера; null — асфальт по умолчанию.
        public static SurfaceType Of(Collider collider)
        {
            if (collider == null) return null;
            if (cache.TryGetValue(collider, out var type)) return type;
            var surface = collider.GetComponentInParent<Surface>();
            type = surface != null ? surface.Type : null;
            cache[collider] = type;
            return type;
        }

        /// Кэш переживает Play без перезагрузки домена: чистим при старте сцены.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetCache() => cache.Clear();
    }
}
