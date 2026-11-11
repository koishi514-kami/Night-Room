using Unity.AI.Navigation;
using UnityEngine.AI;
using UnityEngine;

namespace Nightborn.Level
{
    /// <summary>
    /// 运行时烘焙 NavMesh（M2）。
    ///
    /// 场景是 NightbornBootstrap 用代码生成的，NavMesh 数据没法可靠地预存成子资源，
    /// 所以改成进入场景时烘焙一次：这个 40×40 的箱庭开销只有几十毫秒。
    /// 排除 Player / Guard 层，避免把角色自己烘成障碍。
    ///
    /// 频率与开销（本轮补的观测）：每个关卡场景挂了 1 个本组件，**每次场景加载烘一次**，在 Awake 里
    /// 同步执行 —— 也就是阻塞开局第一帧。耗时打到 Console（`NavMesh 已烘焙：… 耗时 x.x ms`），
    /// 主 agent 可以直接读数判断要不要挪到下一帧。
    /// </summary>
    // 2026-11-12：把烘焙提到所有 Awake 之前。否则 NavMeshAgent 的 OnEnable 有可能早于烘焙，
    // 会在 Console 刷一条 "Failed to create agent because there is no valid NavMesh"，
    // 敌人要等 GuardAI 的 EnsureOnNavMesh 重试才贴回网格（能用但脆）。
    [DefaultExecutionOrder(-1000)]
    [RequireComponent(typeof(NavMeshSurface))]
    public class NavMeshRuntimeBake : MonoBehaviour
    {
        [Tooltip("场景里已经有可用 NavMesh 时跳过烘焙。默认关：上一次场景的 NavMesh 数据若没被清掉，" +
                 "开着它会让新关跳过烘焙，敌人原地不动。确认过清理行为再开。")]
        [SerializeField] private bool skipIfAlreadyBaked = false;

        /// <summary>最近一次烘焙耗时（毫秒）。</summary>
        public static float LastBakeMs { get; private set; }

        /// <summary>本次会话累计烘焙次数。</summary>
        public static int BakeCount { get; private set; }

        private void Awake()
        {
            var surface = GetComponent<NavMeshSurface>();
            if (surface == null) return;

            if (skipIfAlreadyBaked && HasNavMesh(surface))
            {
                Debug.Log("[Nightborn] NavMesh 已存在，跳过烘焙：" + name);
                return;
            }

            int mask = ~0;
            int playerLayer = LayerMask.NameToLayer("Player");
            int guardLayer = LayerMask.NameToLayer("Guard");
            if (playerLayer >= 0) mask &= ~(1 << playerLayer);
            if (guardLayer >= 0) mask &= ~(1 << guardLayer);

            surface.collectObjects = CollectObjects.All;
            surface.layerMask = mask;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;

            var watch = System.Diagnostics.Stopwatch.StartNew();
            surface.BuildNavMesh();
            watch.Stop();

            LastBakeMs = (float)watch.Elapsed.TotalMilliseconds;
            BakeCount++;
            Debug.Log("[Nightborn] NavMesh 已烘焙：" + name + "  耗时 " + LastBakeMs.ToString("F1") +
                      " ms（本次会话第 " + BakeCount + " 次）");
        }

        /// <summary>场景里是否已有可用的 NavMesh：在玩家出生点附近采一次样即可判定。</summary>
        private static bool HasNavMesh(NavMeshSurface surface)
        {
            if (surface.navMeshData != null) return true;
            return NavMesh.SamplePosition(new Vector3(0f, 0.2f, -17f), out _, 2f, NavMesh.AllAreas);
        }
    }
}
