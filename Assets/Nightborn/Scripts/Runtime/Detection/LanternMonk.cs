using UnityEngine;

namespace Nightborn.Detection
{
    /// <summary>
    /// 执灯修士（M2 第二种敌人）：自带一盏**吹不灭**的提灯。
    ///
    /// 与普通守卫的差别（HANDOVER §12 决策 15）：
    ///  · 身上挂点光，会把周围照成高危区（玩家踩进来就会灼伤）；
    ///  · 每 8 秒做一次「举灯巡照」：2.2 秒内光照半径 5 m → 8.5 m、强度 1.4 → 2.4；
    ///  · 被吸血后进入休眠，灯随之熄灭。
    ///
    /// 灯灭不掉，只能绕、遮（推箱）、或者在巡照间隙穿过去。
    /// </summary>
    [DisallowMultipleComponent]
    public class LanternMonk : MonoBehaviour
    {
        [Header("灯")]
        [SerializeField] private Light lantern;
        [SerializeField] private float baseRadius = 5f;
        [SerializeField] private float baseIntensity = 1.4f;

        [Header("举灯巡照")]
        [SerializeField] private float sweepInterval = 8f;
        [SerializeField] private float sweepDuration = 2.2f;
        [SerializeField] private float sweepRadius = 8.5f;
        [SerializeField] private float sweepIntensity = 2.4f;

        [Header("呼吸")]
        [SerializeField] private float breathAmplitude = 0.06f;
        [SerializeField] private float breathFrequency = 0.6f;

        [Header("醒灯")]
        [Tooltip("睡醒 / 开局时灯从 0 渐亮到满功率的时间（秒）。")]
        [SerializeField] private float wakeBlendTime = 0.6f;

        [Header("引用")]
        [SerializeField] private GuardAI guard;

        private float _timer;
        private float _seed;
        private float _wakeBlend;   // 0→1：醒来先渐亮，避免一睁眼就把贴着站的人点着

        public Light Lantern => lantern;
        public bool IsSweeping { get; private set; }

        private void Awake()
        {
            _seed = Random.value * 100f;
            if (guard == null) guard = GetComponent<GuardAI>();
        }

        private void Update()
        {
            if (lantern == null) return;

            // 被吸血睡着 → 灯灭
            if (guard != null && guard.IsDormant)
            {
                lantern.enabled = false;
                IsSweeping = false;
                _wakeBlend = 0f;   // 醒来要从 0 重新渐亮，否则「睡醒一闪」白改
                return;
            }

            lantern.enabled = true;

            float dt = Time.deltaTime;

            // 醒来 / 开局先渐亮：原来睡醒那一帧直接满功率，站在修士旁边的玩家
            // 会在毫无预兆的情况下瞬间进入灼伤（LightExposureProbe 只看光强，不看来源）。
            _wakeBlend = Mathf.MoveTowards(_wakeBlend, 1f, dt / Mathf.Max(0.05f, wakeBlendTime));

            _timer += dt;

            float cycle = _timer % Mathf.Max(0.5f, sweepInterval);
            float pulse = cycle < sweepDuration ? Mathf.Sin(cycle / sweepDuration * Mathf.PI) : 0f;
            IsSweeping = pulse > 0.5f && _wakeBlend >= 0.999f;

            float noise = Mathf.PerlinNoise(_seed + Time.time * breathFrequency, 0f) - 0.5f;
            float wobble = 1f + noise * 2f * breathAmplitude;

            lantern.range = Mathf.Lerp(baseRadius, sweepRadius, pulse) * _wakeBlend;
            lantern.intensity = Mathf.Lerp(baseIntensity, sweepIntensity, pulse) * wobble * _wakeBlend;
        }

    }
}
