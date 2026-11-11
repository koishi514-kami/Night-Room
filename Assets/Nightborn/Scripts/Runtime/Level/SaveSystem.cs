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
    /// </summary>
    public static class SaveSystem
    {
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
            public int version = 1;
            public string lastLevelId = "";
            public List<LevelRecord> levels = new List<LevelRecord>();
        }

        private static SaveData _data;
        private static bool _loaded;

        private static string FilePath => Path.Combine(Application.persistentDataPath, "nightborn_save.json");

        /// <summary>存档文件绝对路径（菜单里显示，方便确认看的是哪份档）。</summary>
        public static string SavePath => FilePath;

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
            try
            {
                if (File.Exists(FilePath))
                {
                    string json = File.ReadAllText(FilePath);
                    _data = JsonUtility.FromJson<SaveData>(json);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nightborn] 存档读取失败，使用空存档：" + e.Message);
            }
            if (_data == null) _data = new SaveData();
            if (_data.levels == null) _data.levels = new List<LevelRecord>();
        }

        public static void Save()
        {
            if (!_loaded) Load();
            try
            {
                File.WriteAllText(FilePath, JsonUtility.ToJson(_data, true));
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nightborn] 存档写入失败：" + e.Message);
            }
        }

        public static LevelRecord GetOrCreate(string levelId, int index = 0)
        {
            var record = Data.levels.Find(r => r.id == levelId);
            if (record != null) return record;

            record = new LevelRecord { id = levelId, index = index };
            Data.levels.Add(record);
            return record;
        }

        public static bool IsUnlocked(string levelId)
        {
            var record = Data.levels.Find(r => r.id == levelId && r.completed);
            return record != null;
        }

        /// <summary>该关是否已通关。`IsUnlocked()` 的语义其实是「上一关已完成」，这里给个直白的名字。</summary>
        public static bool IsCompleted(string levelId)
        {
            return !string.IsNullOrEmpty(levelId) && Data.levels.Find(r => r.id == levelId && r.completed) != null;
        }

        public static void SetCheckpoint(string levelId, int checkpoint)
        {
            var r = GetOrCreate(levelId);
            r.checkpoint = checkpoint;
            Data.lastLevelId = levelId;
            Save();
        }

        public static void AddRelic(string levelId, string relicId)
        {
            var r = GetOrCreate(levelId);
            if (!r.relicIds.Contains(relicId)) r.relicIds.Add(relicId);
            Save();
        }

        public static void CompleteLevel(string levelId, int index, float time, int relics, int relicTotal)
        {
            var r = GetOrCreate(levelId, index);
            r.completed = true;
            r.relicTotal = relicTotal;
            r.bestTime = r.bestTime <= 0f ? time : Mathf.Min(r.bestTime, time);
            Data.lastLevelId = levelId;
            Save();
            Debug.Log("[Nightborn] 存档已更新：" + FilePath);
        }

        /// <summary>调试用：清空存档。</summary>
        public static void Delete()
        {
            _data = new SaveData();
            _loaded = true;
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Nightborn] 存档删除失败：" + e.Message);
            }
        }
    }
}
