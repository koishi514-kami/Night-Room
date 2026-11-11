using System.Collections.Generic;
using UnityEngine;

namespace Nightborn.Player
{
    /// <summary>
    /// P0-1「光照即地形」判定。
    ///
    /// 以 10Hz 采样玩家胸口位置的光照强度（0 起，可超过 1）：
    ///   · 环境光 + 月光 = 低权重底光，几乎不会触发灼伤；
    ///   · 点光 / 聚光   = 强度 × 距离平方衰减 × 聚光锥衰减，并做「灯 → 玩家」遮挡检测；
    ///   · 被货箱 / 建筑挡住的光不算，所以躲进阴影就是安全的。
    ///
    /// 超过阈值进入「灼伤」（带滞回 0.65 进 / 0.55 出，避免边界抖动）：
    /// 当前给的是移速压制（硬直手感），血储系统接入后在这里扣血即可。
    ///
    /// 手持提灯默认不参与灼伤计算（灯拿在手里不烧自己），但仍会被读取用于后续暴露度。
    /// </summary>
    [DisallowMultipleComponent]
    public class LightExposureProbe : MonoBehaviour
    {
        [Header("采样")]
        [Tooltip("采样间隔（秒）。10Hz 足够，调高更省性能但反应会钝。")]
        [SerializeField] private float sampleInterval = 0.1f;
        [Tooltip("相对玩家根节点的采样点偏移，默认胸口高度。")]
        [SerializeField] private Vector3 sampleOffset = new Vector3(0f, 1.1f, 0f);

        [Header("灼伤阈值")]
        [Tooltip("超过该光照值判定为灼伤。")]
        [SerializeField] private float burnEnterThreshold = 0.65f;
        [Tooltip("低于该光照值解除灼伤（滞回，避免在阈值上抖动）。")]
        [SerializeField] private float burnExitThreshold = 0.55f;

        [Header("底光权重")]
        [Tooltip("环境光亮度乘的权重。")]
        [SerializeField] private float ambientWeight = 0.5f;
        [Tooltip("方向光（月光）强度乘的权重。月光全场景都有，权重必须很低。")]
        [SerializeField] private float directionalWeight = 0.12f;

        [Header("灼伤表现")]
        [Tooltip("灼伤期间的移速倍率（硬直）。")]
        [SerializeField] private float burnSpeedMultiplier = 0.45f;
        [Tooltip("关掉则只做判定不压移速（调试用）。")]
        [SerializeField] private bool applySpeedPenalty = true;

        [Header("引用")]
        [Tooltip("玩家手持的提灯，不参与灼伤计算。留空会自动按 LANTERN_Light 名字查找。")]
        [SerializeField] private Light carriedLantern;

        [Tooltip("角色自体光。它不参与灼伤计算——自己身上的光不该烧自己。")]
        [SerializeField] private SelfGlow selfGlow;

        [Tooltip("遮挡检测的层。默认全部，玩家自身碰撞体会被自动跳过。")]
        [SerializeField] private LayerMask occluderMask = ~0;

        private readonly List<Light> _lights = new List<Light>();
        private readonly RaycastHit[] _occluders = new RaycastHit[8];
        private PlayerMotor _motor;
        private float _timer;
        private float _refreshTimer;
        private float _current;

        /// <summary>最近一次采样的光照强度（0 起，可超过 1）。</summary>
        public float LightLevel => _current;

        /// <summary>是否处于灼伤状态（带滞回）。</summary>
        public bool IsBurning { get; private set; }

        /// <summary>手持提灯当前光强，供后续暴露度系统使用。</summary>
        public float CarriedLanternIntensity =>
            carriedLantern != null && carriedLantern.enabled ? carriedLantern.intensity : 0f;

        public void SetCarriedLantern(Light lantern)
        {
            carriedLantern = lantern;
        }

        private void Awake()
        {
            _motor = GetComponent<PlayerMotor>();

            if (selfGlow == null) selfGlow = GetComponent<SelfGlow>();

            // 遮挡层收敛一下：默认的 ~0 把 Player 层和内置的 Ignore Raycast(2) 也算进遮挡了。
            // Veil（幕布）保持算遮挡——"推箱挂布遮光"这套机制就是靠它生效的。
            // 只改"仍是默认全开"的情况，避免覆盖掉在 Inspector 里手调过的遮挡层。
            if (occluderMask.value == ~0)
            {
                int playerLayer = gameObject.layer;
                occluderMask = Physics.DefaultRaycastLayers & ~(1 << playerLayer);
            }

            if (carriedLantern == null)
            {
                var go = GameObject.Find("LANTERN_Light");
                if (go != null) carriedLantern = go.GetComponent<Light>();
            }

            RefreshLights();
            Sample();
        }

        private void Update()
        {
            _timer += Time.deltaTime;
            if (_timer < sampleInterval) return;
            _timer = 0f;

            // 灯光集合偶尔刷新一次即可（场景里加灯不需要立刻响应）。
            _refreshTimer += sampleInterval;
            if (_refreshTimer >= 1f)
            {
                _refreshTimer = 0f;
                RefreshLights();
            }

            Sample();
        }

        private void RefreshLights()
        {
            _lights.Clear();
#if UNITY_2023_1_OR_NEWER
            var found = Object.FindObjectsByType<Light>(FindObjectsSortMode.None);
#else
            var found = Object.FindObjectsOfType<Light>();
#endif
            foreach (var l in found)
            {
                if (l != null && l.isActiveAndEnabled) _lights.Add(l);
            }
        }

        /// <summary>立即采样一次（切场景 / 传送后可手动调用）。</summary>
        public void Sample()
        {
            Vector3 point = transform.position + sampleOffset;

            // 多光源按 HANDOVER §4 的模型取「最亮的那个」，不是求和。
            // 求和会把两盏灯的交界处（实际仍是半影区）判成灼伤：街区两盏 2.2 强度的灯
            // 各贡献 0.55，和起来 1.1 > 0.65 就直接烧，躲进灯之间的暗区也无效。
            float strongest = 0f;
            for (int i = 0; i < _lights.Count; i++)
            {
                var l = _lights[i];
                if (l == null || l == carriedLantern) continue;
                if (selfGlow != null && l == selfGlow.GlowLight) continue;
                strongest = Mathf.Max(strongest, Evaluate(l, point));
            }

            float level = AmbientBaseline() + strongest;

            _current = level;

            bool burning = level >= (IsBurning ? burnExitThreshold : burnEnterThreshold);
            if (burning != IsBurning)
            {
                IsBurning = burning;
                Debug.Log(string.Format("[Nightborn] 光照 {0:F2} → {1}", level, burning ? "灼伤 (burning)" : "安全 (safe)"));
            }

            if (_motor != null)
                _motor.SetSpeedMultiplier("burn", applySpeedPenalty && IsBurning ? burnSpeedMultiplier : 1f);
        }

        private float AmbientBaseline()
        {
            Color amb = RenderSettings.ambientLight;
            float luminance = 0.2126f * amb.r + 0.7152f * amb.g + 0.0722f * amb.b;
            float baseline = luminance * ambientWeight;
            return Mathf.Clamp(baseline, 0f, 0.35f);
        }

        private float Evaluate(Light l, Vector3 point)
        {
            // 灯被吹灭后立刻失效，不等下一次 1 秒的灯光缓存刷新
            if (l == null || !l.enabled || !l.isActiveAndEnabled) return 0f;

            if (l.type == LightType.Directional)
                return l.intensity * directionalWeight;

            if (l.type != LightType.Point && l.type != LightType.Spot)
                return 0f;

            Vector3 toPoint = point - l.transform.position;
            float dist = toPoint.magnitude;
            if (dist > l.range) return 0f;

            Vector3 dir = dist > 0.0001f ? toPoint / dist : Vector3.up;

            // 距离衰减：到 range 边界归零，按平方衰减（比 Unity 自带曲线更"硬"，手感更清楚）。
            float atten = Mathf.Clamp01(1f - dist / l.range);
            atten *= atten;

            if (l.type == LightType.Spot)
            {
                float cosHalf = Mathf.Cos(l.spotAngle * 0.5f * Mathf.Deg2Rad);
                float cosAngle = Vector3.Dot(-dir, l.transform.forward);
                atten *= Mathf.Clamp01((cosAngle - cosHalf) / Mathf.Max(0.0001f, 1f - cosHalf));
            }

            if (atten <= 0f) return 0f;

            // 遮挡：从玩家胸口朝灯反打一条射线，少走 0.2m 以免打到灯泡自身的碰撞体。
            // 反打（而不是从灯打向玩家）更稳：起点永远不会卡在灯座/灯泡的几何体内部。
            float rayLength = Mathf.Max(0f, dist - 0.2f);
            if (rayLength > 0f)
            {
                int hits = Physics.RaycastNonAlloc(point, -dir, _occluders, rayLength, occluderMask, QueryTriggerInteraction.Ignore);
                for (int i = 0; i < hits; i++)
                {
                    var h = _occluders[i];
                    if (h.collider == null) continue;
                    // 打到玩家自己（CharacterController）不算遮挡。
                    if (h.collider.transform.root == transform.root) continue;
                    // 打到灯具自身的挂点（灯柱、灯泡）也不算遮挡——否则站在灯下永远读不到光。
                    if (h.collider.transform.root == l.transform.root) continue;
                    return 0f;
                }
            }

            return l.intensity * atten;
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 point = Application.isPlaying ? transform.position + sampleOffset : transform.position + sampleOffset;
            Gizmos.color = IsBurning ? new Color(1f, 0.25f, 0.2f, 0.9f) : new Color(0.4f, 0.9f, 0.6f, 0.7f);
            Gizmos.DrawWireSphere(point, 0.35f);

            for (int i = 0; i < _lights.Count; i++)
            {
                var l = _lights[i];
                if (l == null || l == carriedLantern || l.type == LightType.Directional) continue;
                if (Vector3.Distance(l.transform.position, point) > l.range) continue;
                Gizmos.DrawLine(l.transform.position, point);
            }
        }
    }
}
