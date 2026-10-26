using UnityEngine;

namespace RandomCar.Core
{
    /// Камера главного меню: медленный облёт стартовой решётки с машинами.
    public sealed class MenuCamera : MonoBehaviour
    {
        [SerializeField] Vector3 focus;
        [SerializeField] float radius = 15f;
        [SerializeField] float height = 4.2f;
        [SerializeField] float speed = 7f;
        [SerializeField] float startAngle = 210f;

        float angle;

        public void Configure(Vector3 focusPoint, float startYaw)
        {
            focus = focusPoint;
            startAngle = startYaw;
        }

        void OnEnable() => angle = startAngle;

        void LateUpdate()
        {
            angle += speed * Time.unscaledDeltaTime;
            float a = angle * Mathf.Deg2Rad;
            float bob = Mathf.Sin(Time.unscaledTime * 0.35f) * 0.6f;
            transform.position = focus + new Vector3(Mathf.Cos(a) * radius, height + bob, Mathf.Sin(a) * radius);
            transform.LookAt(focus + Vector3.up * 0.9f);
            var cam = GetComponent<Camera>();
            if (cam != null) cam.fieldOfView = 48f;
        }
    }
}
