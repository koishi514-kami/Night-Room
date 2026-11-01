using UnityEngine;

namespace Nightborn.Player
{
    /// <summary>
    /// 提灯：跟随玩家做阻尼摆动（0.12s 秒级缓动），带极轻微的烛光呼吸/抖动。
    /// 灰盒阶段按用户要求关闭实时阴影（shadows = None）以省性能，
    /// 正式做「光照即地形」时在 Inspector 里改回 Soft 即可。
    /// </summary>
    public class LanternFollow : MonoBehaviour
    {
        [Header("跟随目标")]
        [SerializeField] private Transform target;
        [SerializeField] private Vector3 offset = new Vector3(0.35f, 1.25f, 0.28f);
        [SerializeField] private float smoothTime = 0.12f;

        [Header("烛光")]
        [SerializeField] private Light lanternLight;
        [SerializeField] private float baseIntensity = 1.6f;
        [SerializeField] private float flickerAmplitude = 0.12f;
        [SerializeField] private float flickerFrequency = 7.5f;

        private Vector3 _velocity;
        private float _seed;

        private void Awake()
        {
            _seed = Random.value * 100f;
            if (lanternLight == null) lanternLight = GetComponentInChildren<Light>();
            if (lanternLight != null)
            {
                lanternLight.shadows = LightShadows.None;
                lanternLight.range = 4f;
                lanternLight.type = LightType.Point;
            }
        }

        private void LateUpdate()
        {
            if (target != null)
            {
                Vector3 goal = target.position + target.rotation * offset;
                transform.position = Vector3.SmoothDamp(transform.position, goal, ref _velocity, smoothTime);
            }

            if (lanternLight != null)
            {
                float noise = Mathf.PerlinNoise(_seed + Time.time * flickerFrequency, 0f) - 0.5f;
                lanternLight.intensity = baseIntensity + noise * 2f * flickerAmplitude;
            }
        }
    }
}
