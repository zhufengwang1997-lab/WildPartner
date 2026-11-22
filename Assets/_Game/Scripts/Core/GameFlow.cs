using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using WildPartner.Creatures;
using WildPartner.Player;
using WildPartner.World;

namespace WildPartner.Core
{
    /// <summary>
    /// Game loop (PRD 2 "complete game"): main menu -> new/continue -> explore & capture 5 creatures ->
    /// "return to the cabin" -> campfire ending -> main menu. Autosaves progress, position, time of day.
    /// </summary>
    [DefaultExecutionOrder(-40)]
    public class GameFlow : MonoBehaviour
    {
        public enum FlowState { MainMenu, Playing, Paused, Journal, Ending }
        public static GameFlow Instance { get; private set; }

        public PlayerController player;
        public EnvController environment;
        public Weather weather;
        public Transform campfire;
        public Transform menuCameraPivot;
        public float autosaveSeconds = 20f;
        public float startHour = 16.5f;   // warm late-afternoon light as the opening (PRD-补充 §3)

        public FlowState State { get; private set; } = FlowState.MainMenu;
        public SaveData Save { get; private set; } = new SaveData();
        public int CaughtCount => Save.caught.Count;
        public int Total => CreatureDefs.All.Length;
        public bool AllCaught => CaughtCount >= Total;

        public event Action<FlowState> StateChanged;
        public event Action<string> Toast;
        public event Action<string[]> Hints;

        Creature[] _creatures;
        float _availTimer, _saveTimer;
        bool _endingTriggered;
        /// <summary>Automation (benchmarks / perf runs) skips the menu and freezes gameplay.</summary>
        public static bool Automation;

        void Awake()
        {
            Instance = this;
            Application.targetFrameRate = -1;
            QualitySettings.vSyncCount = 1;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Start()
        {
            _creatures = FindObjectsByType<Creature>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            for (int i = 0; i < _creatures.Length; i++) _creatures[i].Init(0, true);
            CaptureSystem.Captured += OnCaptured;
            CaptureSystem.Interrupted += c => Toast?.Invoke($"{c.Def.name}受惊逃走了！试试按住 Ctrl 潜行靠近。");
            if (!Automation) SetState(FlowState.MainMenu);
        }

        void SetState(FlowState s)
        {
            State = s;
            bool playing = s == FlowState.Playing;
            Time.timeScale = s == FlowState.Paused || s == FlowState.Journal ? 0f : 1f;
            GameInput.Blocked = !playing;
            Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !playing;
            if (player) player.enabled = s != FlowState.MainMenu;
            StateChanged?.Invoke(s);
        }

        // ---------------------------------------------------------------- menu actions
        public void NewGame()
        {
            SaveSystem.DeleteGame();
            Save = new SaveData { timeOfDay = startHour };
            _endingTriggered = false;
            foreach (var c in _creatures) c.Init(0, true);
            var start = WorldLayout.PlayerStart;
            var startPos = new Vector3(start.x, Creature.GroundHeight(start.x, start.y) + 0.2f, start.y);
            player.Teleport(startPos, WorldLayout.PlayerStartYaw);
            foreach (var c in _creatures)   // the default pet (cat) is with the witch from the start
                if (c.Def.startsWithPlayer)
                {
                    Save.caught.Add(new CaughtEntry { id = c.defId, playTime = 0, gameHour = startHour });
                    c.RestoreAsCompanion(Save.caught.Count - 1, startPos);
                }
            environment.timeOfDay = startHour;
            weather.Set(false, true);
            SetState(FlowState.Playing);
            Hints?.Invoke(new[]
            {
                "WASD 移动，鼠标转动视角，Shift 奔跑，空格跳跃",
                "按住 Ctrl 潜行：动物不容易发现你",
                "靠近动物后按住 E 捕捉；Tab 打开图鉴查看线索",
                $"猫已经在你身边了。再找到另外 {Total - CaughtCount} 种伙伴，然后回到木屋前的篝火",
            });
            SaveNow();
            Debug.Log("[Flow] new game");
        }

        public void Continue()
        {
            var d = SaveSystem.LoadGame();
            if (d == null) { NewGame(); return; }
            Save = d;
            _endingTriggered = d.finished;   // a finished save keeps exploring instead of replaying the ending
            environment.timeOfDay = d.timeOfDay;
            player.Teleport(d.playerPosition, d.playerYaw);
            foreach (var c in _creatures) c.Init(0, true);
            int slot = 0;
            foreach (var e in d.caught)
            {
                var c = _creatures.FirstOrDefault(x => x.defId == e.id);
                if (c) c.RestoreAsCompanion(slot++, d.playerPosition);
            }
            SetState(FlowState.Playing);
            Toast?.Invoke($"欢迎回来！已收集 {CaughtCount}/{Total}");
            Debug.Log($"[Flow] continue: {CaughtCount} caught, t={d.timeOfDay:0.0}");
        }

        public void Pause() { if (State == FlowState.Playing) { SaveNow(); SetState(FlowState.Paused); } }
        public void Resume() { if (State == FlowState.Paused || State == FlowState.Journal) SetState(FlowState.Playing); }
        public void OpenJournal() { if (State == FlowState.Playing) SetState(FlowState.Journal); }
        public void BackToMenu() { if (State != FlowState.Ending) SaveNow(); SetState(FlowState.MainMenu); }
        public void Quit()
        {
            if (State == FlowState.Playing || State == FlowState.Paused) SaveNow();
            Application.Quit();
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#endif
        }

        // ---------------------------------------------------------------- loop
        void Update()
        {
            if (State == FlowState.Playing || State == FlowState.Journal || State == FlowState.Paused)
            {
                if (GameInput.PausePressed)
                {
                    if (State == FlowState.Playing) Pause(); else Resume();
                }
                else if (GameInput.JournalPressed)
                {
                    if (State == FlowState.Playing) OpenJournal(); else if (State == FlowState.Journal) Resume();
                }
            }

            if (State != FlowState.Playing) return;
            Save.playTime += Time.deltaTime;

            _availTimer -= Time.deltaTime;
            if (_availTimer <= 0)
            {
                _availTimer = 1f;
                foreach (var c in _creatures)
                    c.UpdateAvailability(environment.timeOfDay, weather.IsRaining, player.transform.position);
            }

            _saveTimer += Time.deltaTime;
            if (_saveTimer > autosaveSeconds) SaveNow();

            if (AllCaught && !_endingTriggered && campfire &&
                Vector3.Distance(player.transform.position, campfire.position) < 5f)
                TriggerEnding();
        }

        void OnCaptured(Creature c)
        {
            if (Save.HasCaught(c.defId)) return;
            Save.caught.Add(new CaughtEntry { id = c.defId, playTime = Save.playTime, gameHour = environment.timeOfDay });
            c.BecomeCompanion(Save.caught.Count - 1);
            Toast?.Invoke($"捕捉成功：{c.Def.name}！（{CaughtCount}/{Total}）");
            if (AllCaught) Toast?.Invoke("伙伴们都到齐了！回到悬崖木屋前的篝火吧。");
            SaveNow();
        }

        public string Objective =>
            AllCaught ? "回到悬崖木屋前的篝火（东南高地）" : $"收集伙伴 {CaughtCount}/{Total}（Tab 查看图鉴线索）";

        public void SaveNow()
        {
            _saveTimer = 0;
            if (!player || Automation) return;
            Save.playerPosition = player.transform.position;
            Save.playerYaw = player.transform.eulerAngles.y;
            Save.timeOfDay = environment.timeOfDay;
            SaveSystem.SaveGame(Save);
        }

        void TriggerEnding()
        {
            _endingTriggered = true;
            Save.finished = true;
            SaveNow();
            // companions gather around the fire
            int i = 0;
            var comps = _creatures.Where(c => c.Current == Creature.State.Companion).ToList();
            foreach (var c in comps)
            {
                float a = (i++ / (float)Mathf.Max(comps.Count, 1)) * Mathf.PI * 2f;
                var p = campfire.position + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * 3f;
                p.y = Creature.GroundHeight(p.x, p.z) + (c.Def.movement == Movement.Ground ? 0 : 1.2f);
                var agent = c.GetComponent<UnityEngine.AI.NavMeshAgent>();
                if (agent && agent.enabled) agent.Warp(p); else c.transform.position = p;
                c.transform.rotation = Quaternion.LookRotation(campfire.position - p);
            }
            SetState(FlowState.Ending);
            Debug.Log($"[Flow] ending reached in {Save.playTime:0}s");
        }

        public static string FormatTime(float seconds)
        {
            int m = Mathf.FloorToInt(seconds / 60), s = Mathf.FloorToInt(seconds % 60);
            return $"{m} 分 {s:00} 秒";
        }

        void OnApplicationQuit() { if (State == FlowState.Playing || State == FlowState.Paused) SaveNow(); }
    }
}
