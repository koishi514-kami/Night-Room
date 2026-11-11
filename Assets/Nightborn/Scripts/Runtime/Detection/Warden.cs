using UnityEngine;
using Nightborn.Level;

namespace Nightborn.Detection
{
    /// <summary>
    /// 钟楼看守（M3 第四种敌人）：重甲、慢、看得远，**吸不动**。
    ///
    /// 设计依据 HANDOVER §12 决策 15/17：
    ///  · 视野极宽（Bootstrap 配 90° / 14 m），几乎无法从正面绕过；
    ///  · 移速慢（巡逻 1.0 / 追击 1.6），但**搜索时间 60 秒**，一旦惊动会长时间盘踞；
    ///  · 重甲：`FeedTarget` 被关掉 —— 吸不了，只能绕、遮光、或者等它走开。
    /// </summary>
    [DisallowMultipleComponent]
    public class Warden : MonoBehaviour
    {
        [SerializeField] private FeedTarget feedTarget;

        private void Awake()
        {
            if (feedTarget == null) feedTarget = GetComponent<FeedTarget>();
            LockFeed();
        }

        private void OnEnable()
        {
            // 运行时被重新启用（场景重载 / 其它系统开关）也要把「重甲」钉回去：
            // FeedTarget.feedable 是个 [SerializeField]，SetFeedable 之外没人保证它不被翻回来。
            LockFeed();
        }

        private void LockFeed()
        {
            if (feedTarget == null)
            {
                Debug.LogWarning("[Nightborn] 钟楼看守身上没有 FeedTarget，重甲逻辑无处生效：" + name);
                return;
            }

            bool wasFeedable = feedTarget.Feedable;
            feedTarget.SetFeedable(false);
            if (wasFeedable) Debug.Log("[Nightborn] 钟楼看守：重甲，无法吸血");
        }


    }
}
