using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Nightborn.Level
{
    /// <summary>
    /// 最小存档（M0 步骤 7）：单个 JSON 文件，记录每关的通关状态、最好成绩、遗物与检查点。
    ///
    /// 说明：M0 先自己写一个 60 行的 JSON 存档，保证能跑；
    /// M2 若要做加密/多档位/IL2CPP 强类型，按 THIRD_PARTY.md 换成 Save System for Unity（MIT）。
    ///
    /// 本轮加固（HANDOVER §6 那条「编辑器会话把存档冲掉」的坑就出在本文件）：
    ///  · **原子写入**：先写 `nightborn_save.json.tmp`，再用 `File.Replace` 换上去 —— 旧主档自动进 `.bak`。
    ///    旧实现 `File.WriteAllText` 直接覆盖主档，写到一半崩掉就是半截 JSON，整份进度作废；
    ///  · **损坏容错**：主档读不出来（截断 / 手改坏）就退回 `.bak`，再不行才用空档，不静默丢进度；
    ///  · **脏写抑制**：同一个检查点重复踩、同一条遗物重复记，都不再写盘（旧实现每次都整份写回）；
    ///  · **字段卫生**：重复关卡记录、重复/空 relicIds、NaN 的 bestTime、负 checkpoint 在读取时清掉；
    ///  · **index 修正**：`SetCheckpoint` / `AddRelic` 先于 `CompleteLevel` 建档时，旧实现会把 index 记成 0，
    ///    现在由调用方把真实 levelIndex 传进来；
    ///  · `IsUnlocked` 与 `IsCompleted` 语义重复，保留两个名字但只留一份实现（`IsUnlocked` 是历史名）。
    /// </summary>
    public static class SaveSystem
    {
        /// <summary>当前存档结构版本。只在**结构**变化时 +1，并在 Migrate() 里补迁移分支。</summary>
        public const int FormatVersion = 1;

        private const string FileName = "nightborn_save.json";

        [Serializable]
        public class LevelRecord
        {
            public string id;
            public int index;
            public bool completed;
            public float bestTime;
            public int checkpoint;
            public List<string> relicIds = new List<string>();
            public int relicTotal;
        }

        [Serializable]
        public class SaveData
        {
            public int version = FormatVersion;
            public string lastLevelId = "";
            public List<LevelRecord> levels = new List<LevelRecord>();
        }

        private static SaveData _data;
        private static bool _loaded;

        /// <summary>累计写盘次数（脏写频率自检用）。</summary>
        public static int SaveCount { get; private set; }

        /// <summary>最近一次写盘耗时（毫秒）。</summary>
        public static float LastSaveMs { get; private set; }

        /// <summary>最近一次读取是否走了 .bak 恢复。</summary>
        public static bool LoadedFromBackup { get; private set; }

        private static string Dir => Application.persistentDataPath;
        private static string FilePath => Path.Combine(Dir, FileName);
        private static string TempPath => FilePath + ".tmp";
        private static string BackupPath => FilePath + ".bak";

        /// <summary>存档文件绝对路径（菜单里显示，方便确认看的是哪份档）。</summary>
        public static string SavePath => FilePath;

        /// <summary>备份文件绝对路径（写盘成功后是上一次的主档）。</summary>
        public static string BackupPathForDisplay => BackupPath;

        public static SaveData Data
        {
            get
            {
                if (!_loaded) Load();
                return _data;
            }
        }

        public static void Load()
        {
            _loaded = true;
            LoadedFromBackup = false;

            _data = ReadFile(FilePath);
            if (_data == null)
            {
                // 主档不存在或坏了：退回上一次成功写入留下的 .bak（原子写入的副产物）
                _data = ReadFile(BackupPath);
                if (_data != null)
                {
                    LoadedFromBackup = true;
                    Debug.LogWarning("[Nightborn] 主存档不可用，已从 .bak 恢复（下次保存会重写主档）");
                }
            }

            if (_data == null) _data = new SaveData();

            Migrate(_data);
            Sanitize(_data);
        }

        private static SaveData ReadFile(string path)
        {
            try
            {
                if (!File.Exists(path)) return null;
                string json = File.ReadAllText(path);
                if (string.IsNullOrEmpty(json) || json.Trim().Length == 0) return null;
                return JsonUtility.FromJson<SaveData>(json);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nightborn] 存档读取失败（" + Path.GetFileName(path) + "）：" + e.Message);
                return null;
            }
        }

        /// <summary>版本迁移：只在结构变化时加分支，迁移必须幂等。</summary>
        private static void Migrate(SaveData data)
        {
            if (data.version > FormatVersion)
            {
                // 降级运行 / 手工改档：只提示，不覆盖、不清档。
                Debug.LogWarning("[Nightborn] 存档版本 " + data.version + " 高于本程序支持的 " +
                                 FormatVersion + "，按兼容模式读取（不会被写坏）");
                return;
            }

            while (data.version < FormatVersion)
            {
                switch (data.version)
                {
                    case 0:
                        // v0 → v1：M0 早期档没有 version 字段（JsonUtility 读出来是 0）
                        if (data.levels == null) data.levels = new List<LevelRecord>();
                        if (data.lastLevelId == null) data.lastLevelId = "";
                        data.version = 1;
                        break;
                    default:
                        // 未知旧版本：结构兼容，直接抬到当前版本，不丢数据
                        data.version = FormatVersion;
                        break;
                }
            }
        }

        /// <summary>读取后的字段卫生：坏字段就地修，别让它传染到运行期。</summary>
        private static void Sanitize(SaveData data)
        {
            if (data.levels == null) data.levels = new List<LevelRecord>();
            if (data.lastLevelId == null) data.lastLevelId = "";

            for (int i = data.levels.Count - 1; i >= 0; i--)
            {
                var r = data.levels[i];
                if (r == null || string.IsNullOrEmpty(r.id))
                {
                    data.levels.RemoveAt(i);
                    continue;
                }

                if (r.relicIds == null) r.relicIds = new List<string>();
                for (int k = r.relicIds.Count - 1; k >= 0; k--)
                {
                    string id = r.relicIds[k];
                    if (string.IsNullOrEmpty(id) || r.relicIds.IndexOf(id) != k) r.relicIds.RemoveAt(k);
                }

                if (float.IsNaN(r.bestTime) || float.IsInfinity(r.bestTime) || r.bestTime < 0f) r.bestTime = 0f;
                if (r.checkpoint < 0) r.checkpoint = 0;
                if (r.relicTotal < r.relicIds.Count) r.relicTotal = r.relicIds.Count;
                if (r.index < 0) r.index = 0;

                // 同 id 的重复记录合并成一条（手改档 / 旧版本迁移可能造出来）
                for (int k = 0; k < i; k++)
                {
                    var other = data.levels[k];
                    if (other == null || other.id != r.id) continue;

                    other.completed |= r.completed;
                    if (other.bestTime <= 0f || (r.bestTime > 0f && r.bestTime < other.bestTime)) other.bestTime = r.bestTime;
                    if (r.checkpoint > other.checkpoint) other.checkpoint = r.checkpoint;
                    if (r.index > other.index) other.index = r.index;
                    if (r.relicTotal > other.relicTotal) other.relicTotal = r.relicTotal;
                    for (int n = 0; n < r.relicIds.Count; n++)
                        if (!other.relicIds.Contains(r.relicIds[n])) other.relicIds.Add(r.relicIds[n]);

                    data.levels.RemoveAt(i);
                    break;
                }
            }
        }

        /// <summary>
        /// 整份写回（原子）：.tmp → File.Replace → 旧主档进 .bak。
        /// 调用频率由上层控制（检查点 / 遗物 / 通关各一次，不是每帧）。
        /// </summary>
        public static void Save()
        {
            if (!_loaded) Load();
            if (_data == null) return;

            string json;
            try
            {
                json = JsonUtility.ToJson(_data, true);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nightborn] 存档序列化失败：" + e.Message);
                return;
            }

            var watch = System.Diagnostics.Stopwatch.StartNew();
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(TempPath, json);

                if (File.Exists(FilePath))
                {
                    // 原子替换：旧主档进 .bak，.tmp 变成新主档。中途断电不会留下半截 JSON。
                    File.Replace(TempPath, FilePath, BackupPath, true);
                }
                else
                {
                    File.Move(TempPath, FilePath);
                }

                SaveCount++;
                LastSaveMs = (float)watch.Elapsed.TotalMilliseconds;
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nightborn] 存档写入失败：" + e.Message);
                FallbackWrite(json);
            }
        }

        /// <summary>File.Replace 在网络盘 / 部分 FAT 卷上会不支持：退化成「删旧 + 改名」，至少不丢档。</summary>
        private static void FallbackWrite(string json)
        {
            try
            {
                if (!File.Exists(TempPath)) File.WriteAllText(TempPath, json);
                if (File.Exists(FilePath)) File.Delete(FilePath);
                File.Move(TempPath, FilePath);
                SaveCount++;
                Debug.LogWarning("[Nightborn] 存档已用降级路径写入（本次没有 .bak 备份）");
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nightborn] 存档降级写入也失败：" + e.Message);
                TryDelete(TempPath);
            }
        }

        private static void TryDelete(string path)
        {
            try
            {
                if (File.Exists(path)) File.Delete(path);
            }
            catch
            {
                // 清理临时文件失败不能再抛：存档主流程已经结束了
            }
        }

        public static LevelRecord GetOrCreate(string levelId, int index = 0)
        {
            if (string.IsNullOrEmpty(levelId)) return null;

            var levels = Data.levels;
            for (int i = 0; i < levels.Count; i++)
            {
                var r = levels[i];
                if (r != null && r.id == levelId) return r;
            }

            var record = new LevelRecord { id = levelId, index = index < 0 ? 0 : index };
            levels.Add(record);
            return record;
        }

        /// <summary>该关是否已通关。`IsUnlocked()` 的语义其实是「这一关已完成」，这里给个直白的名字。</summary>
        public static bool IsCompleted(string levelId)
        {
            if (string.IsNullOrEmpty(levelId)) return false;

            var levels = Data.levels;
            for (int i = 0; i < levels.Count; i++)
            {
                var r = levels[i];
                if (r != null && r.completed && r.id == levelId) return true;
            }
            return false;
        }

        /// <summary>
        /// 历史名字：实际语义是「这一关是否已完成」（菜单拿它判断**上一关**打没打完）。
        /// 保留只为不改调用点，内部直接转发 <see cref="IsCompleted"/>，别再各写一份查找。
        /// </summary>
        public static bool IsUnlocked(string levelId)
        {
            return IsCompleted(levelId);
        }

        public static void SetCheckpoint(string levelId, int checkpoint, int levelIndex = -1)
        {
            if (string.IsNullOrEmpty(levelId)) return;

            var data = Data;
            var r = GetOrCreate(levelId, levelIndex);
            if (r == null) return;
            if (levelIndex >= 0) r.index = levelIndex;

            bool changed = r.checkpoint != checkpoint || data.lastLevelId != levelId;
            r.checkpoint = checkpoint;
            data.lastLevelId = levelId;

            // 同一个检查点被反复踩（复活后走回去）不再写盘
            if (!changed) return;
            Save();
        }

        public static void AddRelic(string levelId, string relicId, int levelIndex = -1)
        {
            if (string.IsNullOrEmpty(levelId) || string.IsNullOrEmpty(relicId)) return;

            var r = GetOrCreate(levelId, levelIndex);
            if (r == null) return;
            if (levelIndex >= 0) r.index = levelIndex;

            for (int i = 0; i < r.relicIds.Count; i++)
                if (r.relicIds[i] == relicId) return;   // 已记录：不重复加、不重复写盘

            r.relicIds.Add(relicId);
            if (r.relicTotal < r.relicIds.Count) r.relicTotal = r.relicIds.Count;
            Save();
        }

        public static void CompleteLevel(string levelId, int index, float time, int relics, int relicTotal)
        {
            if (string.IsNullOrEmpty(levelId)) return;

            var data = Data;
            var r = GetOrCreate(levelId, index);
            if (r == null) return;

            r.completed = true;
            if (index >= 0) r.index = index;

            int total = Mathf.Max(relicTotal, relics);
            if (total > 0) r.relicTotal = Mathf.Max(r.relicTotal, total);

            // bestTime 只在「有效且更快」时更新：<=0 视为无效成绩，别把纪录写成 0
            if (time > 0f && (r.bestTime <= 0f || time < r.bestTime)) r.bestTime = time;

            data.lastLevelId = levelId;
            Save();
            Debug.Log("[Nightborn] 存档已更新：" + FilePath +
                      "（累计写盘 " + SaveCount + " 次，本次 " + LastSaveMs.ToString("F1") + "ms）");
        }

        /// <summary>调试用：清空存档。主档 / .tmp / .bak 一起删，否则下次读取会把 .bak 当进度恢复回来。</summary>
        public static void Delete()
        {
            _data = new SaveData();
            _loaded = true;
            LoadedFromBackup = false;

            TryDelete(FilePath);
            TryDelete(TempPath);
            TryDelete(BackupPath);
        }
    }
}
