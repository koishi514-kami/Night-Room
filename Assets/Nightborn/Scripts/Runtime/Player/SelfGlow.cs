using System;
using UnityEngine;

namespace Nightborn.Player
{
    /// <summary>
    /// 自体光（2026-11-11 设计变更：取消提灯，光长在角色身上）。
    ///
    /// · 固定 3 m 柔光，不可调（设计决策 #8）；
    /// · 投软阴影，玩家自己会在地面/墙面留下影子；
    /// · 不参与"灼伤"计算——由 <see cref="LightExposureProbe"/> 显式排除，否则站哪都在烧自己；
    /// · 影身（ShiftForm）调用 <see cref="SetGlowing"/>(false) 关灯。
    /// </summary>
    [DisallowMultipleComponent]
    public class SelfGlow : MonoBehaviour
    {
        [Header("光源")]
        [Tooltip("角色的自发光点光源，留空会自动在子节点里找。")]
        [SerializeField] private Light glowLight;
        [SerializeField] private float radius = 3f;
        [SerializeField] private float intensity = 1.6f;
        [SerializeField] private Color color = new Color(0.878f, 0.643f, 0.294f, 1f);

        [Header("阴影")]
        [SerializeField] private bool castShadows = true;
        [SerializeField] private int shadowResolution = 512;
        [SerializeField, Range(0f, 1f)] private float shadowStrength = 0.8f;

        [Header("呼吸")]
        [Tooltip("光强轻微起伏，幅度按比例。0 = 完全静止。")]
        [SerializeField] private float breathAmplitude = 0.05f;
        [SerializeField] private float breathFrequency = 0.35f;

        private float _seed;
        private bool _glowing = true;

        public Light GlowLight => glowLight;
        public bool IsGlowing => _glowing;
        public float Radius => radius;

        /// <summary>开关自体光（影身进入/退出时触发）。</summary>
        public event Action<bool> GlowChanged;

        private void Awake()
        {
            _seed = UnityEngine.Random.value * 100f;
            if (glowLight == null) glowLight = GetComponentInChildren<Light>();
            if (glowLight == null)
                Debug.LogError("[Nightborn] SelfGlow 找不到 Light，请检查 PLAYER_Glow 子节点。");
            Apply();
        }

        private void Update()
        {
            if (glowLight == null || !_glowing) return;
            float noise = Mathf.PerlinNoise(_seed + Time.time * breathFrequency, 0f) - 0.5f;
            glowLight.intensity = intensity * (1f + noise * 2f * breathAmplitude);
        }

        /// <summary>关掉 / 打开自体光。影身用它实现"熄光"，光身再打开。</summary>
        public void SetGlowing(bool on)
        {
            if (_glowing == on) return;
            _glowing = on;
            Apply();
            GlowChanged?.Invoke(on);
            Debug.Log("[Nightborn] 自体光 " + (on ? "开启" : "熄灭"));
        }

        private void Apply()
        {
            if (glowLight == null) return;

            glowLight.type = LightType.Point;
            glowLight.range = radius;
            glowLight.intensity = intensity;
            glowLight.color = color;
            glowLight.shadows = castShadows ? LightShadows.Soft : LightShadows.None;
            glowLight.shadowStrength = shadowStrength;
            glowLight.shadowCustomResolution = castShadows ? Mathf.Clamp(shadowResolution, 64, 2048) : 0;
            glowLight.enabled = _glowing;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (glowLight == null) glowLight = GetComponentInChildren<Light>();
            if (glowLight != null) Apply();
        }
#endif
    }
}
