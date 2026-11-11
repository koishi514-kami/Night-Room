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
    /// </summary>
    [RequireComponent(typeof(NavMeshSurface))]
    public class NavMeshRuntimeBake : MonoBehaviour
    {
        private void Awake()
        {
            var surface = GetComponent<NavMeshSurface>();

            int mask = ~0;
            int playerLayer = LayerMask.NameToLayer("Player");
            int guardLayer = LayerMask.NameToLayer("Guard");
            if (playerLayer >= 0) mask &= ~(1 << playerLayer);
            if (guardLayer >= 0) mask &= ~(1 << guardLayer);

            surface.collectObjects = CollectObjects.All;
            surface.layerMask = mask;
            surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();

            Debug.Log("[Nightborn] NavMesh 已烘焙：" + name);
        }
    }
}
