using System;
using UnityEngine;
using Nightborn.Detection;
using Nightborn.Level;
using Nightborn.Player;
using Nightborn.Settings;

namespace Nightborn.Audio
{
    /// <summary>
    /// 音频总控（M2）：环境 drone + 事件音效 + 脚步 + 低血心跳。
    ///
    /// 音源全部是运行时合成的占位 clip（见 <see cref="SynthAudio"/>），
    /// 正式音效包接入时只需把这里的字段换成 AudioClip 资源引用。
    ///
    /// 本轮（音频静态审计）改动要点：
    ///  1. **增益曾经乘了两遍**：`_sfx.volume = sfxVolume` 且 `PlayOneShot(clip, sfxVolume × k)`，
    ///     实际是 sfxVolume² —— 脚步只剩 0.28×0.28 = 0.078（约 −22 dB，几乎听不见），
    ///     事件音效统一低 ~3 dB。现在一次性音源固定 `volume = 1`，增益只走 PlayOneShot 的第二个参数。
    ///  2. 被抓音效会同时被「血储归零」和「LevelFlow.CaughtEvent」触发 → 同一帧两记 `_caught`
    ///     叠在一起（+6 dB、相位打架）。加了 0.2 s 合并窗口。
    ///  3. 补上原来没有声音的四个事件：吸血/回血生效、化形硬直（被强光灼回）、血视开启、压光药剂。
    ///     没加新素材：分别复用已有的 Caught/Heartbeat/Douse/Alert（见 Cues 区）。
    ///  4. 失焦/暂停时把总线静音（原来切出窗口后环境 drone 会一直在后台响）。
    ///  5. 一次性音源不再 `loop = true`；删掉 `OnLampDoused` 里给脚步音源设 position 的无效代码
    ///     （所有音源 spatialBlend = 0，位置对它没有任何作用）。
    ///  6. 所有事件回调改成具名字段 + `OnDestroy` 退订（原来用匿名 lambda，退订不了）。
    ///
    /// 2026-11-12「设置菜单」那一轮（接线）：三个固定增益（ambientVolume / sfxVolume / footstepVolume）
    /// 一个都没动，只是在**每一个出声点**再乘一次 `NightbornSettings.Master` ——
    /// `Master × Sfx`（事件音效/心跳）、`Master × Ambient`（drone）、`Master × Footstep`（脚步）。
    /// 在设置里一拉立刻生效（订阅 OnVolumeChanged），`SetAudible(false)` 走 mute / 音量 0，仍压过设置。
    /// 没有 Audio Mixer，也没有改任何合成逻辑，只多了一次乘法。
    /// </summary>
    [DisallowMultipleComponent]
    public class NightbornAudio : MonoBehaviour
    {
        [Header("音量")]
        [Range(0f, 1f)] [SerializeField] private float ambientVolume = 0.35f;
        [Range(0f, 1f)] [SerializeField] private float sfxVolume = 0.7f;
        [Range(0f, 1f)] [SerializeField] private float footstepVolume = 0.28f;

        [Header("脚步节奏")]
        [SerializeField] private float walkStepInterval = 0.52f;
        [SerializeField] private float sneakStepInterval = 1.05f;
        [SerializeField] private float minSpeedForStep = 0.4f;

        [Header("合并窗口")]
        [Tooltip("同一记被抓音效在这段时间内只响一次（血储归零与 CaughtEvent 会同时触发）。")]
        [SerializeField] private float caughtMergeWindow = 0.2f;
        [Tooltip("「可疑 → 围猎」两级告警的最小间隔。")]
        [SerializeField] private float alertMergeWindow = 0.35f;

        private AudioSource _ambient;
        private AudioSource _sfx;
        private AudioSource _step;
        private AudioSource _heart;

        private AudioClip _footstep;
        private AudioClip _douse;
        private AudioClip _heartbeat;
        private AudioClip _alert;
        private AudioClip _caught;
        private AudioClip _complete;
        private AudioClip _feed;

        private PlayerMotor _motor;
        private BloodSystem _blood;
        private DetectionMeter _detection;
        private ShiftForm _shift;
        private BloodSight _sight;
        private DimPotion _potion;
        private PlayerFeeder _feeder;
        private LevelFlow _flow;

        private float _stepTimer;
        private float _heartTimer;
        private bool _wasFeeding;
        private bool _wasStaggered;
        private bool _wasSightOn;
        private bool _wasPotionOn;
        private float _lastBlood = -1f;
        private float _healSuppressUntil = -99f;
        private float _lastCaughtAt = -99f;
        private float _lastAlertAt = -99f;
        private bool _audible = true;

        // 具名回调：匿名 lambda 退订不了（旧实现在 OnDestroy 里退订不到，只能靠同场景销毁兜底）
        private Action _onDied;
        private Action _onCaught;
        private Action _onComplete;
        private Action<DetectionMeter.DetectionState> _onStateChanged;
        private Action<bool> _onFormChanged;
        private Action _onVolumeSettingsChanged;

        private void Awake()
        {
            _footstep = SynthAudio.Footstep();
            _douse = SynthAudio.Douse();
            _heartbeat = SynthAudio.Heartbeat();
            _alert = SynthAudio.Alert();
            _caught = SynthAudio.Caught();
            _complete = SynthAudio.Complete();
            _feed = SynthAudio.Feed();

            _ambient = MakeSource("AUDIO_Ambient", true, ambientVolume * NightbornSettings.Master);
            _ambient.clip = SynthAudio.Drone();
            _ambient.Play();

            // 一次性音源：volume 固定 1，实际增益全部走 PlayOneShot 的 volumeScale（避免乘两遍）
            _sfx = MakeSource("AUDIO_Sfx", false, 1f);
            _step = MakeSource("AUDIO_Step", false, 1f);
            _heart = MakeSource("AUDIO_Heart", false, 1f);
        }

        private void Start()
        {
            _motor = GetComponent<PlayerMotor>();
            _blood = GetComponent<BloodSystem>();
            _detection = GetComponent<DetectionMeter>();
            _shift = GetComponent<ShiftForm>();
            _sight = GetComponent<BloodSight>();
            _potion = GetComponent<DimPotion>();
            _feeder = GetComponent<PlayerFeeder>();
            _flow = FindObjectOfType<LevelFlow>();

            if (_blood != null)
            {
                _lastBlood = _blood.Current;
                _onDied = () => PlayCaught(0.9f);
                _blood.Died += _onDied;
            }

            if (_detection != null)
            {
                _onStateChanged = OnDetectionStateChanged;
                _detection.StateChanged += _onStateChanged;
            }

            if (_shift != null)
            {
                _onFormChanged = OnFormChanged;
                _shift.FormChanged += _onFormChanged;
            }

            if (_flow != null)
            {
                _onCaught = () =>
                {
                    // 回检查点会一次性把血补回来，别把那一次当成「回血音效」
                    _healSuppressUntil = Time.unscaledTime + 1f;
                    PlayCaught(0.85f);
                };
                _onComplete = () => Play(_complete, 0.8f);
                _flow.CaughtEvent += _onCaught;
                _flow.LevelCompleted += _onComplete;
            }

            DouseableLamp.DousedEvent += OnLampDoused;
            Relic.CollectedAny += OnRelicCollected;

            // 设置里改主音量 / 环境音量：drone 当场变响，不用重进场景
            _onVolumeSettingsChanged = OnVolumeSettingsChanged;
            NightbornSettings.OnVolumeChanged -= _onVolumeSettingsChanged;
            NightbornSettings.OnVolumeChanged += _onVolumeSettingsChanged;

            SetAudible(true);
        }

        private void OnDestroy()
        {
            DouseableLamp.DousedEvent -= OnLampDoused;
            Relic.CollectedAny -= OnRelicCollected;

            if (_blood != null && _onDied != null) _blood.Died -= _onDied;
            if (_detection != null && _onStateChanged != null) _detection.StateChanged -= _onStateChanged;
            if (_shift != null && _onFormChanged != null) _shift.FormChanged -= _onFormChanged;
            if (_flow != null)
            {
                if (_onCaught != null) _flow.CaughtEvent -= _onCaught;
                if (_onComplete != null) _flow.LevelCompleted -= _onComplete;
            }

            if (_onVolumeSettingsChanged != null) NightbornSettings.OnVolumeChanged -= _onVolumeSettingsChanged;
        }

        private void OnApplicationFocus(bool focused)
        {
            SetAudible(focused);
        }

        private void OnApplicationPause(bool paused)
        {
            SetAudible(!paused);
        }

        /// <summary>失焦 / 暂停时静音自己的音源（不动 `AudioListener.volume`，那是全局的）。</summary>
        private void SetAudible(bool audible)
        {
            _audible = audible;
            // 环境音量 = 固定增益 × 设置里的主音量；失焦静音（音量 0 + mute）永远压过设置
            if (_ambient != null) _ambient.volume = audible ? ambientVolume * NightbornSettings.Master : 0f;
            if (_sfx != null) _sfx.mute = !audible;
            if (_step != null) _step.mute = !audible;
            if (_heart != null) _heart.mute = !audible;
        }

        /// <summary>设置里拖主音量 / 环境音量：drone 立刻跟着变（一次性音效是每次 PlayOneShot 现算，不用管）。</summary>
        private void OnVolumeSettingsChanged()
        {
            if (_ambient != null) _ambient.volume = _audible ? ambientVolume * NightbornSettings.Master : 0f;
        }

        private AudioSource MakeSource(string name, bool loop, float volume)
        {
            var go = new GameObject(name);
            go.transform.SetParent(transform, false);
            var src = go.AddComponent<AudioSource>();
            src.playOnAwake = false;
            src.loop = loop;
            src.volume = volume;
            src.spatialBlend = 0f; // 占位阶段统一 2D，避免听感位置错乱
            return src;
        }

        /// <summary>
        /// 事件音效。`sfxVolume` 只在这里乘一次（旧实现还额外乘了一次音源的 volume，等于平方）。
        /// </summary>
        private void Play(AudioClip clip, float gain)
        {
            if (clip == null || _sfx == null || !_audible) return;
            _sfx.PlayOneShot(clip, Mathf.Clamp01(sfxVolume * NightbornSettings.Master * gain));
        }

        private void PlayOn(AudioSource source, AudioClip clip, float gain)
        {
            if (clip == null || source == null || !_audible) return;
            // master 只在这里乘一次：调用方传进来的 gain 已经含了各自的固定增益
            source.PlayOneShot(clip, Mathf.Clamp01(gain * NightbornSettings.Master));
        }

        /// <summary>被抓：血储归零（Died）与 LevelFlow.CaughtEvent 会同时来，合并成一记。</summary>
        private void PlayCaught(float gain)
        {
            if (Time.unscaledTime - _lastCaughtAt < caughtMergeWindow) return;
            _lastCaughtAt = Time.unscaledTime;
            Play(_caught, gain);
        }

        private void OnLampDoused(Vector3 position)
        {
            // 所有音源都是 2D（spatialBlend = 0），位置没有意义，不再给脚步音源设 position
            Play(_douse, 0.9f);
        }

        private void OnRelicCollected(Vector3 position)
        {
            Play(_complete, 0.35f);
        }

        private void OnFormChanged(bool shadow)
        {
            Play(_douse, shadow ? 0.55f : 0.35f);
        }

        private void OnDetectionStateChanged(DetectionMeter.DetectionState state)
        {
            if (state == DetectionMeter.DetectionState.Calm) return;
            if (Time.unscaledTime - _lastAlertAt < alertMergeWindow) return;
            _lastAlertAt = Time.unscaledTime;
            Play(_alert, state == DetectionMeter.DetectionState.Hunted ? 1f : 0.6f);
        }

        private void Update()
        {
            if (_motor == null) return;
            UpdateFootsteps();
            UpdateHeartbeat();
            UpdateFeeding();
            UpdateCues();
        }

        private void UpdateFootsteps()
        {
            // 死亡 / 被抓冻结时 motor 会被 disable，此时不该继续踩脚步（旧实现照踩）
            if (!_motor.enabled)
            {
                _stepTimer = 0f;
                return;
            }

            float speed = _motor.CurrentSpeed;
            if (speed < minSpeedForStep)
            {
                _stepTimer = 0f;
                return;
            }

            float interval = _motor.IsSneaking ? sneakStepInterval : walkStepInterval;
            _stepTimer -= Time.deltaTime;
            if (_stepTimer > 0f) return;

            _stepTimer = interval;
            _step.pitch = UnityEngine.Random.Range(0.9f, 1.1f);
            PlayOn(_step, _footstep, footstepVolume * (_motor.IsSneaking ? 0.5f : 1f));
        }

        private void UpdateHeartbeat()
        {
            if (_blood == null) return;

            float health = _blood.Normalized;
            if (health >= 0.35f)
            {
                _heartTimer = 0f;
                return;
            }

            float t = 1f - health / 0.35f;
            float interval = Mathf.Lerp(0.95f, 0.42f, t);
            _heartTimer -= Time.deltaTime;
            if (_heartTimer > 0f) return;

            _heartTimer = interval;
            PlayOn(_heart, _heartbeat, sfxVolume * Mathf.Lerp(0.25f, 0.6f, t));
        }

        /// <summary>
        /// 吸血：开始一记低频，真正回血时再给一记「吞咽」。
        /// 回血用**血量跳变**判定（>5 一次性回复），所以吸血、血瓶都会响，
        /// 而 Cancel / 被抓（只回检查点、不回血）不会误响；回检查点的那次大补血用 `_healSuppressUntil` 屏蔽。
        /// </summary>
        private void UpdateFeeding()
        {
            if (_feeder != null)
            {
                bool feeding = _feeder.IsFeeding;
                if (feeding != _wasFeeding)
                {
                    _wasFeeding = feeding;
                    if (feeding) PlayOn(_heart, _heartbeat, 0.5f * sfxVolume);
                }
            }

            if (_blood != null)
            {
                if (_lastBlood >= 0f)
                {
                    float delta = _blood.Current - _lastBlood;
                    if (delta > 5f && Time.unscaledTime > _healSuppressUntil)
                        Play(_feed, 0.9f);
                }
                _lastBlood = _blood.Current;
            }
        }

        /// <summary>补齐原本没有声音的四个状态变化（都用已有 clip，不新增素材）。</summary>
        private void UpdateCues()
        {
            // 化形被强光灼回 → 硬直
            if (_shift != null)
            {
                bool staggered = _shift.IsStaggered;
                if (staggered && !_wasStaggered) Play(_douse, 0.5f);
                _wasStaggered = staggered;
            }

            // 血视开启
            if (_sight != null)
            {
                bool on = _sight.IsActive;
                if (on && !_wasSightOn) Play(_alert, 0.45f);
                _wasSightOn = on;
            }

            // 压光药剂生效
            if (_potion != null)
            {
                bool on = _potion.IsActive;
                if (on && !_wasPotionOn) Play(_douse, 0.4f);
                _wasPotionOn = on;
            }
        }

        // ------------------------------------------------------------------
        // 音量设置（HANDOVER §5 第 3 条）：2026-11-12 已接线，实际音量 = 固定增益 × NightbornSettings.Master。
        // 下面 1–3 是「以后要真上 Audio Mixer」的可选路线，本轮没做（没新增资产，也没改合成逻辑）：
        //  1. 新建 `Assets/Nightborn/Audio/Master.mixer`，加 Master / Music / SFX / Ambience 四个 Group；
        //  2. 给 `_ambient` 输出到 Ambience，`_sfx` / `_step` / `_heart` 输出到 SFX
        //     （在 `MakeSource` 里 `src.outputAudioMixerGroup = group;`，签名加一个 Group 参数即可）；
        //  3. 设置界面里写 `mixer.SetFloat("SFXVolume", Mathf.Log10(v) * 20f)`，存档里只存 0–1。
        // 现在这套字段（ambientVolume / sfxVolume / footstepVolume）仍是「代码里的固定增益」，
        // 玩家在设置面板里改的是 master / ambient / sfx 三个总线值，两者相乘才是最终音量；
        // 失焦静音走 mute 与音量 0，永远压过设置，也不会和将来的 Mixer 冲突。
        // ------------------------------------------------------------------
    }
}
