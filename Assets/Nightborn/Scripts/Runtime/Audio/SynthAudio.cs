using UnityEngine;

namespace Nightborn.Audio
{
    /// <summary>
    /// 运行时合成音效（占位资源）。
    ///
    /// 工程里没有音频素材，也不希望为了占位去背版权风险，所以先全部用代码合成：
    /// 脚步（噪声脉冲）、吹灯（嘶声）、心跳（低频砰）、警觉（上滑音）、通关（双音和弦）、环境（低频 drone）、
    /// 吸血/回血（湿的低频吞咽）。
    /// M3 接入正式音效包时，把 <see cref="NightbornAudio"/> 里的 clip 引用换成 AudioClip 资源即可。
    ///
    /// 本轮（音频静态审计）改动要点：
    ///  1. `Make()` 里统一硬限幅到 ±0.95：保证样本落在 [−1, 1]，多个 PlayOneShot 叠在总线上也不会削波；
    ///  2. `Drone()` 的循环接缝改成**环绕交叉淡化**：旧实现把首尾各 0.25 s 淡到 0，
    ///     等于每 6 s 有 0.5 s 的空洞（41 Hz 的低频被振幅调制，听感是持续的"抽吸"）。
    ///     41 Hz / 62 Hz 在整数秒里正好是整数个周期，所以两条正弦首尾天然对齐，只对噪声层做环绕淡化；
    ///  3. `Caught()` 补 5 ms 起音：旧实现第一个样本就从 0 跳到 ±0.25（噪声层），是一次可听的咔哒；
    ///  4. 新增 `Feed()`（吸血 / 回血生效）。
    /// </summary>
    public static class SynthAudio
    {
        private const int SampleRate = 44100;

        public static AudioClip Footstep()
        {
            int n = (int)(SampleRate * 0.10f);
            var data = new float[n];
            var rng = new System.Random(7);
            float lp = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Exp(-18f * t);
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (white - lp) * 0.12f; // 简易低通，去掉刺耳的高频
                data[i] = lp * env * 0.55f;
            }

            return Make("SFX_Footstep", data);
        }

        public static AudioClip Douse()
        {
            int n = (int)(SampleRate * 0.45f);
            var data = new float[n];
            var rng = new System.Random(11);
            float lp = 0f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Exp(-4f * t) * (1f - Mathf.Exp(-40f * t));
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (white - lp) * 0.35f;
                data[i] = lp * env * 0.4f;
            }

            return Make("SFX_Douse", data);
        }

        public static AudioClip Heartbeat()
        {
            int n = (int)(SampleRate * 0.22f);
            var data = new float[n];

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-14f * t);
                float s = Mathf.Sin(2f * Mathf.PI * 52f * t) * 0.8f + Mathf.Sin(2f * Mathf.PI * 104f * t) * 0.2f;
                data[i] = s * env * 0.6f;
            }

            return Make("SFX_Heartbeat", data);
        }

        /// <summary>吸血 / 回血生效：低频吞咽 + 一点点噪声，「吸到了」这件事要有反馈。</summary>
        public static AudioClip Feed()
        {
            int n = (int)(SampleRate * 0.26f);
            var data = new float[n];
            var rng = new System.Random(31);
            float lp = 0f;
            float attack = SampleRate * 0.012f;

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float k = i / (float)n;
                float env = Mathf.Exp(-9f * k) * (1f - Mathf.Exp(-70f * k));
                float body = Mathf.Sin(2f * Mathf.PI * 78f * t) * 0.7f
                             + Mathf.Sin(2f * Mathf.PI * 117f * t) * 0.25f;
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (white - lp) * 0.25f;
                float a = Mathf.Clamp01(i / Mathf.Max(1f, attack)); // 5~12 ms 起音，避免咔哒
                data[i] = (body + lp * 0.35f) * env * 0.55f * a;
            }

            return Make("SFX_Feed", data);
        }

        public static AudioClip Alert()
        {
            int n = (int)(SampleRate * 0.25f);
            var data = new float[n];

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float freq = Mathf.Lerp(620f, 1180f, t);
                float env = Mathf.Sin(Mathf.PI * t) * 0.7f;
                data[i] = Mathf.Sin(2f * Mathf.PI * freq * (i / (float)SampleRate)) * env * 0.35f;
            }

            return Make("SFX_Alert", data);
        }

        public static AudioClip Caught()
        {
            int n = (int)(SampleRate * 0.7f);
            var data = new float[n];
            var rng = new System.Random(3);
            float attack = SampleRate * 0.005f; // 5 ms：噪声层从 0 起，别在第一样本上跳一下

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)n;
                float env = Mathf.Exp(-4f * t);
                float low = Mathf.Sin(2f * Mathf.PI * 46f * (i / (float)SampleRate));
                float noise = (float)(rng.NextDouble() * 2.0 - 1.0) * 0.25f;
                float a = Mathf.Clamp01(i / Mathf.Max(1f, attack));
                data[i] = (low * 0.7f + noise) * env * 0.5f * a;
            }

            return Make("SFX_Caught", data);
        }

        public static AudioClip Complete()
        {
            int n = (int)(SampleRate * 1.4f);
            var data = new float[n];

            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float env = Mathf.Exp(-2.2f * t);
                float a = Mathf.Sin(2f * Mathf.PI * 523f * t);
                float b = Mathf.Sin(2f * Mathf.PI * 784f * t) * 0.7f;
                data[i] = (a + b) * env * 0.22f;
            }

            return Make("SFX_Complete", data);
        }

        /// <summary>
        /// 环境 drone（循环）。秒数取整：41 Hz × 整数秒 = 整数个周期，62 Hz 同理，
        /// 于是两条正弦在接缝处相位天然对齐，只需要给噪声层做环绕交叉淡化。
        /// </summary>
        public static AudioClip Drone(float seconds = 6f)
        {
            seconds = Mathf.Max(1f, Mathf.Round(seconds));
            int n = (int)(SampleRate * seconds);
            int fade = (int)(SampleRate * 0.35f);
            if (fade * 2 > n) fade = Mathf.Max(1, n / 4);

            // 1) 噪声层：多算 fade 个样本，然后把尾巴折回头部做等功率交叉淡化
            var noise = new float[n + fade];
            var rng = new System.Random(23);
            float lp = 0f;
            for (int i = 0; i < n + fade; i++)
            {
                float white = (float)(rng.NextDouble() * 2.0 - 1.0);
                lp += (white - lp) * 0.01f;
                noise[i] = lp * 2.5f;
            }

            for (int i = 0; i < fade; i++)
            {
                float w = (i / (float)fade) * Mathf.PI * 0.5f;
                noise[i] = noise[i] * Mathf.Sin(w) + noise[n + i] * Mathf.Cos(w);
            }

            // 2) 全曲：周期正弦 + 已环绕的噪声。首尾连续，循环点上既没有接缝也没有空洞
            var data = new float[n];
            for (int i = 0; i < n; i++)
            {
                float t = i / (float)SampleRate;
                float baseTone = Mathf.Sin(2f * Mathf.PI * 41f * t) * 0.5f + Mathf.Sin(2f * Mathf.PI * 62f * t) * 0.25f;
                data[i] = (baseTone + noise[i]) * 0.10f;
            }

            return Make("SFX_Drone", data);
        }

        private static AudioClip Make(string name, float[] data)
        {
            // 硬限幅：保证样本值一定落在 [−1, 1]（多路 PlayOneShot 叠加时也不会把总线推爆）
            for (int i = 0; i < data.Length; i++)
            {
                float v = data[i];
                if (v > 0.95f) data[i] = 0.95f;
                else if (v < -0.95f) data[i] = -0.95f;
            }

            var clip = AudioClip.Create(name, data.Length, 1, SampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
