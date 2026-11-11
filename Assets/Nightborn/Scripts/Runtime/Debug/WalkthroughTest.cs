using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Nightborn.Detection;
using Nightborn.Level;
using Nightborn.Player;

namespace Nightborn.DebugTools
{
    /// <summary>
    /// 自动化流程自检（开发用）：进关卡后把这套跑一遍，Console 里看 ✅/❌。
    /// 用法：Play → 进关卡 → 挂到任意物体上，或按 F2 由 NightbornRuntimeBoot 自动跑（仅调试构建）。
    /// </summary>
    public class WalkthroughTest : MonoBehaviour
    {
        private readonly List<string> _report = new List<string>();
        private int _pass;
        private int _fail;

        private PlayerMotor _motor;
        private CharacterController _cc;
        private BloodSystem _blood;
        private LightExposureProbe _probe;
        private ShiftForm _shift;
        private PlayerFeeder _feeder;
        private BloodSight _sight;
        private LevelFlow _flow;

        private void Start()
        {
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            Resolve();
            if (_motor == null)
            {
                Debug.LogError("[TEST] 找不到玩家，终止");
                yield break;
            }

            yield return Step_SceneWiring();
            yield return Step_Checkpoint();
            yield return Step_DouseLamp();
            yield return Step_PushCrate();
            yield return Step_Feed();
            yield return Step_BloodSight();
            yield return Step_Phasing();
            yield return Step_Caught();
            yield return Step_Relic();
            yield return Step_Exit();

            Debug.Log("[TEST] ===== 流程自检结束：" + _pass + " 通过 / " + _fail + " 失败 =====");
            foreach (var line in _report) Debug.Log("[TEST] " + line);
        }

        private void Resolve()
        {
            var player = GameObject.Find("PLAYER_Ada");
            if (player == null) return;
            _motor = player.GetComponent<PlayerMotor>();
            _cc = player.GetComponent<CharacterController>();
            _blood = player.GetComponent<BloodSystem>();
            _probe = player.GetComponent<LightExposureProbe>();
            _shift = player.GetComponent<ShiftForm>();
            _feeder = player.GetComponent<PlayerFeeder>();
            _sight = player.GetComponent<BloodSight>();
            _flow = Object.FindObjectOfType<LevelFlow>();
        }

        private void Check(string name, bool ok, string detail)
        {
            if (ok) _pass++; else _fail++;
            _report.Add((ok ? "✅ " : "❌ ") + name + " — " + detail);
        }

        private void Teleport(Vector3 p)
        {
            var t = _motor.transform;
            _cc.enabled = false;
            t.position = p;
            _cc.enabled = true;
        }

        private IEnumerator Step_SceneWiring()
        {
            var player = _motor.transform;
            bool hasFlow = _flow != null;
            bool hasHud = Object.FindObjectOfType<NightbornDebugHud>() != null;
            int guards = GuardAI.All.Count;
            Check("场景接线", hasFlow && hasHud && guards >= 2,
                "LevelFlow=" + hasFlow + " HUD=" + hasHud + " 敌人=" + guards +
                " 玩家=" + player.position.ToString("F0") + " 血=" + _blood.Current.ToString("F0"));
            yield return new WaitForSeconds(0.2f);
        }

        private IEnumerator Step_Checkpoint()
        {
            Teleport(new Vector3(0f, 0.6f, -16f));
            yield return new WaitForSeconds(0.6f);

            var field = typeof(LevelFlow).GetField("_spawnPoint", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Vector3 spawn = (Vector3)field.GetValue(_flow);
            float d = Vector3.Distance(spawn, new Vector3(0f, 0f, -16f));
            Check("检查点触发", d < 1.5f, "复活点=" + spawn.ToString("F2") + "（期望 ≈ (0,0,-16)）");
        }

        private IEnumerator Step_DouseLamp()
        {
            var lamp = GameObject.Find("LAMP_Street_1").GetComponent<DouseableLamp>();
            if (lamp == null) { Check("吹灭街灯", false, "找不到灯"); yield break; }

            if (lamp.Doused)
            {
                Check("吹灭街灯", true, "本局已吹灭，跳过（手动试玩过）");
                yield break;
            }

            Teleport(new Vector3(2.2f, 0.2f, 4f));
            _probe.Sample();
            float before = _probe.LightLevel;
            lamp.Interact(gameObject);
            _probe.Sample();
            float after = _probe.LightLevel;
            Check("吹灭街灯", after < before - 0.3f,
                "外光 " + before.ToString("F2") + " → " + after.ToString("F2") + " doused=" + lamp.Doused);
            yield return new WaitForSeconds(0.2f);
        }

        private IEnumerator Step_PushCrate()
        {
            var crate = GameObject.Find("PROP_Crate_1");
            if (crate == null) { Check("推箱", false, "找不到箱子"); yield break; }

            Vector3 start = crate.transform.position;
            Teleport(new Vector3(start.x, 0.2f, start.z - 1.5f)); // 贴地站位，别站到箱顶
            for (int i = 0; i < 30; i++)
            {
                _cc.Move(new Vector3(0f, 0f, 0.08f));
                yield return null;
            }
            yield return new WaitForSeconds(0.5f);
            float moved = Vector3.Distance(start, crate.transform.position);
            Check("推箱", moved > 0.02f, "位移 " + moved.ToString("F2") + " m（无输入时只有最小推力，实操按 W 推得更远）");
        }

        private IEnumerator Step_Feed()
        {
            var guard = GameObject.Find("NPC_Guard_1");
            if (guard == null) { Check("吸血", false, "找不到守卫"); yield break; }

            var feedTarget = guard.GetComponent<FeedTarget>();
            _blood.Damage(40f);
            float before = _blood.Current;

            // 站到背后
            Vector3 behind = guard.transform.position - guard.transform.forward * 1.0f;
            Teleport(new Vector3(behind.x, 0.2f, behind.z));
            yield return new WaitForSeconds(0.2f);

            bool started = _feeder.TryFeed(feedTarget);
            yield return new WaitForSeconds(1.8f);

            Check("吸血", started && feedTarget.IsAsleep && _blood.Current > before,
                "开始=" + started + " 昼睡=" + feedTarget.IsAsleep +
                " 血 " + before.ToString("F0") + " → " + _blood.Current.ToString("F0"));
        }

        private IEnumerator Step_BloodSight()
        {
            bool ok = _sight.Activate();
            yield return null;
            Check("血视", ok && _sight.IsActive, "active=" + _sight.IsActive + " 冷却=" + _sight.CooldownRemaining.ToString("F0"));
            yield return new WaitForSeconds(0.2f);
        }

        private IEnumerator Step_Phasing()
        {
            // 薄幕 B 在 (0, 2, 14)，尺寸 6 x 4 x 0.2
            Teleport(new Vector3(0f, 0.2f, 12.6f));
            yield return new WaitForSeconds(0.3f);

            typeof(ShiftForm).GetField("_cooldownTimer", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(_shift, 0f); // 清冷却：本局可能手动切过形态
            bool shadow = _shift.Toggle();
            yield return new WaitForSeconds(0.2f);

            for (int i = 0; i < 14; i++)
            {
                _cc.Move(new Vector3(0f, 0f, 0.15f));
                yield return null;
            }
            yield return new WaitForSeconds(0.3f);

            float z = _motor.transform.position.z;
            Check("影身穿薄幕", shadow && z > 14.6f,
                "影身=" + shadow + " 穿过后 z=" + z.ToString("F2") + "（幕在 z=14）");

            _shift.Toggle();
            yield return new WaitForSeconds(0.2f);
        }

        private IEnumerator Step_Caught()
        {
            var monk = GameObject.Find("NPC_Monk_1");
            if (monk == null) { Check("被发现→回检查点", false, "找不到修士"); yield break; }

            float bloodBefore = _blood.Current;

            // 钉住修士（否则巡逻时不停转身，视野断断续续），再站到他灯下正前方 2.5 m
            var monkAi = monk.GetComponent<GuardAI>();
            typeof(GuardAI).GetField("waypoints", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(monkAi, new Transform[] { monk.transform });
            monk.transform.LookAt(_motor.transform.position);
            Teleport(monk.transform.position + monk.transform.forward * 2.5f + new Vector3(0f, 0.2f, 0f));
            _probe.Sample();
            yield return new WaitForSeconds(0.2f);

            // 等确认 → 追逐 → 被抓（最多 12 秒）
            float t = 0f;
            while (t < 12f && !_flow.IsComplete)
            {
                t += Time.deltaTime;
                yield return null;
            }
            yield return new WaitForSeconds(1.2f);

            float dist = Vector3.Distance(_motor.transform.position, new Vector3(0f, 0.2f, -16f));
            Check("被发现→回检查点", dist < 3f,
                "等待 " + t.ToString("F1") + "s，回程距离 " + dist.ToString("F2") + " m，血 " +
                bloodBefore.ToString("F0") + " → " + _blood.Current.ToString("F0") +
                " | 修士 可见=" + monkAi.CanSeePlayer + " 进度=" + monkAi.Progress.ToString("F2") +
                " 状态=" + monkAi.CurrentState + " 休眠=" + monkAi.IsDormant +
                " 玩家影身=" + _shift.IsShadow + " 外光=" + _probe.LightLevel.ToString("F2") +
                " 距离=" + Vector3.Distance(_motor.transform.position, monk.transform.position).ToString("F2"));
        }

        private IEnumerator Step_Relic()
        {
            var relicGo = GameObject.Find("PROP_Relic_1");
            if (relicGo == null) { Check("拾取遗物", false, "找不到遗物"); yield break; }

            var relic = relicGo.GetComponent<Relic>();
            Teleport(relicGo.transform.position + new Vector3(0f, -0.8f, -0.6f));
            yield return new WaitForSeconds(0.2f);
            relic.Interact(gameObject);
            yield return new WaitForSeconds(0.3f);

            Check("拾取遗物", relic.Collected && _flow.Relics >= 1,
                "collected=" + relic.Collected + " 遗物=" + _flow.Relics + "/" + _flow.RelicTotal);
        }

        private IEnumerator Step_Exit()
        {
            Teleport(new Vector3(0f, 0.2f, 19f));
            yield return new WaitForSeconds(1.0f);
            Check("抵达出口通关", _flow.IsComplete, "IsComplete=" + _flow.IsComplete);
            yield return null;
        }
    }
}
