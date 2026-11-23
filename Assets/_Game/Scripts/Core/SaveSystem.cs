using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace WildPartner.Core
{
    [Serializable]
    public class CaughtEntry
    {
        public string id;
        public float playTime;     // seconds of play when caught
        public float gameHour;     // in-game time of day when caught
        public Vector3 position;   // where it was caught (shown on the big map)
    }

    [Serializable]
    public class SaveData
    {
        /// <summary>2 = the 360 m map (2026-11-23). Saves from the old 600 m map hold coordinates that no longer
        /// fit, so they are discarded and the player starts a new game.</summary>
        public const int Current = 2;
        public int version = Current;
        public List<CaughtEntry> caught = new List<CaughtEntry>();
        public Vector3 playerPosition;
        public float playerYaw;
        public float timeOfDay = 9f;     // hours 0..24
        public float playTime;
        public bool introShown;
        public bool finished;
        public bool indoors;             // saved inside the cabin (试玩反馈 1123b-3)
        public string currentPet = "cat"; // the pet at her side (试玩反馈 1123c-B2); missing in older saves -> cat
        public bool moonHintShown;       // 1123d-3: the first clear night by the lake gave its hint
        public int rainyNightsInRow;     // 1123d-3: sleeping into a rainy night twice in a row -> the next one is clear
        public bool HasCaught(string id) => caught.Exists(c => c.id == id);
    }

    [Serializable]
    public class SettingsData
    {
        public int version = 1;
        public string tier = "";             // "" = auto (QualityTiers decides)
        public string upscaler = "";         // stage 2: "" = auto
        public string upscalerQuality = "";  // stage 2
        public bool grd = true;              // unused (old placeholder; settings files saved before 11-23 have it true)
        public bool grdEnabled;              // stage 4: GPU Resident Drawer, off by default (Toon objects vanish under GRD on Metal, see DECISIONS)
        public bool gpuOcclusion = true;     // stage 4: GPU occlusion culling (needs GRD)
        public bool debugOverlay;
        public float mouseSensitivity = 1f;
        public float masterVolume = 0.8f;
        public float musicVolume = 1f;      // 1123e-4: per-category volumes
        public float sfxVolume = 1f;
        public float ambienceVolume = 1f;
        public string antiAliasing = "";   // 超分阶段: Off / TAA / STP
        public bool invertY;
    }

    /// <summary>
    /// JSON files in persistentDataPath: save.json (progress, autosaved) and settings.json. Writes go to a
    /// temp file first and are then moved over the old one so a crash never leaves a half-written save.
    /// </summary>
    public static class SaveSystem
    {
        /// <summary>The smoke test keeps its own save folder so a game the user has open is never touched.</summary>
        public static string Dir => System.Array.IndexOf(System.Environment.GetCommandLineArgs(), "-smoketest") >= 0
            ? System.IO.Path.Combine(Application.persistentDataPath, "smoke_save") : Application.persistentDataPath;
        static string SavePath => Path.Combine(Dir, "save.json");
        static string SettingsPath => Path.Combine(Dir, "settings.json");

        static SettingsData _settings;
        public static SettingsData Settings => _settings ??= LoadSettings();

        public static bool HasSave => LoadGame() != null;

        public static SaveData LoadGame()
        {
            try
            {
                if (!File.Exists(SavePath)) return null;
                var d = JsonUtility.FromJson<SaveData>(File.ReadAllText(SavePath));
                if (d == null || d.version < SaveData.Current)
                {
                    Debug.LogWarning($"[Save] save version {(d == null ? 0 : d.version)} is from the old 600 m map: discarded, start a new game");
                    File.Delete(SavePath);
                    return null;
                }
                return d;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[Save] cannot read save: {e.Message}");
                return null;
            }
        }

        public static void SaveGame(SaveData d) => WriteAtomic(SavePath, JsonUtility.ToJson(d, true));

        public static void DeleteGame()
        {
            if (File.Exists(SavePath)) File.Delete(SavePath);
        }

        public static SettingsData LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsPath)) return JsonUtility.FromJson<SettingsData>(File.ReadAllText(SettingsPath)) ?? new SettingsData();
            }
            catch (Exception e) { Debug.LogWarning($"[Save] cannot read settings: {e.Message}"); }
            return new SettingsData();
        }

        public static void SaveSettings() => WriteAtomic(SettingsPath, JsonUtility.ToJson(Settings, true));

        static void WriteAtomic(string path, string json)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path));
                string tmp = path + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e) { Debug.LogError($"[Save] write failed {path}: {e.Message}"); }
        }
    }
}
