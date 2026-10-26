using UnityEngine;

namespace RandomCar.Race
{
    /// Пять стартовых огней на арке. Загораются по одному во время отсчёта и гаснут на старт.
    public sealed class StartLights : MonoBehaviour
    {
        static readonly int EmissionColor = Shader.PropertyToID("_EmissionColor");
        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        [SerializeField] Renderer[] lamps = new Renderer[0];
        [SerializeField] Color litColor = new Color(1f, 0.12f, 0.08f);
        [SerializeField] float litIntensity = 9f;

        MaterialPropertyBlock block;
        int current = -1;

        public void Configure(Renderer[] lampRenderers) => lamps = lampRenderers;

        public void SetLit(int count)
        {
            if (count == current) return;
            current = count;
            block ??= new MaterialPropertyBlock();
            for (int i = 0; i < lamps.Length; i++)
            {
                if (lamps[i] == null) continue;
                bool on = i < count;
                block.SetColor(BaseColor, on ? litColor : new Color(0.16f, 0.03f, 0.03f));
                block.SetColor(EmissionColor, on ? litColor * litIntensity : Color.black);
                lamps[i].SetPropertyBlock(block);
            }
        }
    }
}
