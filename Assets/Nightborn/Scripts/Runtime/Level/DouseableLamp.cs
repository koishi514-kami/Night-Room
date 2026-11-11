using System;
using UnityEngine;
using Nightborn.Interaction;

namespace Nightborn.Level
{
    /// <summary>
    /// 可吹灭的街灯（M0 光操纵三件套之一：熄灯）。
    /// 按 E 吹灭 → 关掉灯光 + 熄灭灯球自发光，制造一片可以通行的黑区。
    ///
    /// 设计上**不可恢复**：`_doused` 一旦置位就永久有效（本轮不加复原入口）。
    /// 自检要能核对「灯灭了但余辉还在」，所以额外暴露 <see cref="ResidualEmission"/> / <see cref="LightsOff"/>。
    /// </summary>
    public class DouseableLamp : MonoBehaviour, IInteractable
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Light[] lights;
        [SerializeField] private Renderer[] bulbs;
        [SerializeField] private Color bulbColor = new Color(0.878f, 0.643f, 0.294f, 1f);

        private bool _doused;
        private Material[] _bulbMaterials;

        public bool CanInteract => !_doused;
        public string Prompt => "吹灭街灯";
        public bool Doused => _doused;

        /// <summary>任意一盏灯被吹灭（音频等系统订阅）。</summary>
        public static event Action<Vector3> DousedEvent;

        /// <summary>灯球当前最大的自发光分量（0 = 彻底熄灭）。用来核对「灯灭了但灯球还亮着」。</summary>
        public float ResidualEmission
        {
            get
            {
                if (_bulbMaterials == null) return 0f;
                float max = 0f;
                for (int i = 0; i < _bulbMaterials.Length; i++)
                {
                    var mat = _bulbMaterials[i];
                    if (mat == null || !mat.IsKeywordEnabled("_EMISSION")) continue;
                    Color c = mat.GetColor(EmissionColorId);
                    max = Mathf.Max(max, Mathf.Max(c.r, Mathf.Max(c.g, c.b)));
                }
                return max;
            }
        }

        /// <summary>灯源是否都关了。</summary>
        public bool LightsOff
        {
            get
            {
                if (lights == null) return true;
                for (int i = 0; i < lights.Length; i++)
                    if (lights[i] != null && lights[i].enabled) return false;
                return true;
            }
        }

        private void Awake()
        {
            if (lights == null || lights.Length == 0) lights = GetComponentsInChildren<Light>();
            if (bulbs == null || bulbs.Length == 0) bulbs = PickBulbs();

            int count = bulbs != null ? bulbs.Length : 0;
            _bulbMaterials = new Material[count];
            for (int i = 0; i < count; i++)
                _bulbMaterials[i] = bulbs[i] != null ? bulbs[i].material : null;   // 只实例化一次
        }

        /// <summary>
        /// 没在场景里接线时的兜底：优先挑「本来就在发光」的渲染器（灯球）。
        /// 旧实现是 `GetComponentsInChildren&lt;Renderer&gt;()` 全收 —— 会把灯柱一起点亮/压黑，留下一圈不该有
        /// 的灯柱余辉（ResidualEmission 会一直读到非 0）。
        /// </summary>
        private Renderer[] PickBulbs()
        {
            var all = GetComponentsInChildren<Renderer>();
            Renderer first = null;

            for (int i = 0; i < all.Length; i++)
            {
                var r = all[i];
                if (r == null) continue;
                if (first == null) first = r;

                var shared = r.sharedMaterial;
                if (shared == null || !shared.IsKeywordEnabled("_EMISSION")) continue;

                Color c = shared.GetColor(EmissionColorId);
                if (Mathf.Max(c.r, Mathf.Max(c.g, c.b)) > 0.05f) return new[] { r };
            }

            return first != null ? new[] { first } : new Renderer[0];
        }

        public void Interact(GameObject actor)
        {
            if (_doused) return;
            _doused = true;

            for (int i = 0; i < lights.Length; i++)
                if (lights[i] != null) lights[i].enabled = false;

            // 自发光改成纯黑并关掉 keyword：旧实现留了 灯色 × 0.02，HDR + Bloom 下仍是一圈可见的余辉。
            for (int i = 0; i < _bulbMaterials.Length; i++)
            {
                var mat = _bulbMaterials[i];
                if (mat == null) continue;
                mat.SetColor(EmissionColorId, Color.black);
                mat.DisableKeyword("_EMISSION");
            }

            DousedEvent?.Invoke(transform.position);
            Debug.Log("[Nightborn] 吹灭街灯：" + name + "（灯球 " + ColorUtility.ToHtmlStringRGB(bulbColor) + " → 灭）");
        }
    }
}
