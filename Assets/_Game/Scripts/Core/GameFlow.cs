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
        public enum FlowState { MainMenu, Playing, Paused, Journal, Ending, Map }
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
        public void ShowToast(string text) => Toast?.Invoke(text);
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
            CaptureSystem.Startled += c => Toast?.Invoke($"{c.Def.name}竖起了耳朵——站着别动，等它放松下来再试。");
            Pets = gameObject.AddComponent<PetSwitcher>();
            gameObject.AddComponent<FishingSystem>();   // 1123d-1: the koi is caught by fishing
            FishingSystem.Lost += why => Toast?.Invoke(why);
            FishingSystem.Bite += () => Toast?.Invoke("浮标沉下去了——快按 E 提竿！");
            FishingSystem.Caught += koi => { if (!koi) Toast?.Invoke("钓到一条小银鱼，随手放回了水里。再试一次吧！"); };
            PetSwitcher.Switched += d => Toast?.Invoke($"现在陪着你的是{d.name}：{PetSwitcher.Ability(d.id)}");
            Player.PlayerController.EdgeReached += () => Toast?.Invoke("山太陡了，林子也太密——山谷外面去不了，回去吧。");
            Player.PlayerController.Recovered += () => Toast?.Invoke("好像迷路了……回到了刚才走过的地方。");
            Player.PlayerController.TowerTopReached += () => Toast?.Invoke("登上瞭望塔顶！整个山谷尽收眼底——" + TowerView());
            Player.PlayerController.UndertowHit += () => Toast?.Invoke("水流太急，也许需要水中的伙伴帮忙。");
            // 11-24: no main-menu flyover at launch (it clipped through terrain); the game opens with the witch
            // waking in her bed. The main menu is still reachable from pause / the ending, over the game view.
            if (!Automation) StartCoroutine(OpenInBed());
        }

        System.Collections.IEnumerator OpenInBed()
        {
            yield return null;   // let the cabin, creatures and UI finish their own Start first
            if (!Automation) NewGame();
        }

        void SetState(FlowState s)
        {
            State = s;
            bool playing = s == FlowState.Playing;
            Time.timeScale = s == FlowState.Paused || s == FlowState.Journal || s == FlowState.Map ? 0f : 1f;
            GameInput.Blocked = !playing;
            Cursor.lockState = playing ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !playing;
            if (player) player.enabled = s != FlowState.MainMenu;
            StateChanged?.Invoke(s);
        }

        // ---------------------------------------------------------------- menu actions
        /// <summary>Current-pet rotation (1123c-B2).</summary>
        public PetSwitcher Pets { get; private set; }

        public void NewGame()
        {
            SaveSystem.DeleteGame();
            Save = new SaveData { timeOfDay = startHour };
            _endingTriggered = false;
            foreach (var c in _creatures) c.Init(0, true);
            // 试玩反馈 1123b-3: the witch wakes up in her bed inside the cabin
            var cabin = CabinInterior.Instance;
            var start = WorldLayout.PlayerStart;
            var startPos = cabin ? cabin.bedStand : new Vector3(start.x, Creature.GroundHeight(start.x, start.y) + 0.2f, start.y);
            if (cabin) CabinInterior.SetInside(true);
            player.Teleport(startPos, cabin ? cabin.bedYaw + 90 : WorldLayout.PlayerStartYaw);
            foreach (var c in _creatures)   // the default pet (cat) is with the witch from the start
                if (c.Def.startsWithPlayer)
                {
                    Save.caught.Add(new CaughtEntry { id = c.defId, playTime = 0, gameHour = startHour, position = c.transform.position });
                    c.RestoreAsCompanion(Save.caught.Count - 1, startPos);
                }
            environment.timeOfDay = startHour;
            weather.Set(false, true);
            SetState(FlowState.Playing);
            Pets.Restore("cat");
            if (cabin) cabin.WakeUp();
            Hints?.Invoke(new[]
            {
                "你在木屋里醒来。走到门口按 E 开门；床可以睡觉存档，桌上的书是图鉴",
                "WASD 移动，鼠标转动视角，Shift 奔跑，空格跳跃；F1 切换全屏窗口 / 窗口",
                "按住 Ctrl 潜行：动物不容易发现你",
                "靠近动物后按住 E 捕捉；Tab 打开图鉴查看线索",
                "左上角是小地图；按 M 打开大地图",
                "收集到的伙伴平时不出来：按 J 换一只陪在身边，只有身边那只的能力有效",
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
            CabinInterior.SetInside(d.indoors && CabinInterior.Instance);
            player.Teleport(d.playerPosition, d.playerYaw);
            foreach (var c in _creatures) c.Init(0, true);
            int slot = 0;
            foreach (var e in d.caught)
            {
                var c = _creatures.FirstOrDefault(x => x.defId == e.id);
                if (c) c.RestoreAsCompanion(slot++, d.playerPosition);
            }
            SetState(FlowState.Playing);
            Pets.Restore(d.currentPet);
            Toast?.Invoke($"欢迎回来！已收集 {CaughtCount}/{Total}");
            Debug.Log($"[Flow] continue: {CaughtCount} caught, t={d.timeOfDay:0.0}");
        }

        public void Pause() { if (State == FlowState.Playing) { SaveNow(); SetState(FlowState.Paused); } }
        public void Resume() { if (State == FlowState.Paused || State == FlowState.Journal || State == FlowState.Map) SetState(FlowState.Playing); }
        public void OpenMap() { if (State == FlowState.Playing) SetState(FlowState.Map); }
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
            if (State == FlowState.Playing || State == FlowState.Journal || State == FlowState.Paused || State == FlowState.Map)
            {
                if (GameInput.PausePressed && State == FlowState.Playing && FishingSystem.Active) { }   // Esc puts the rod away instead
                else if (GameInput.PausePressed)
                {
                    if (State == FlowState.Playing) Pause(); else Resume();
                }
                else if (GameInput.JournalPressed)
                {
                    if (State == FlowState.Playing) OpenJournal(); else if (State == FlowState.Journal) Resume();
                }
                else if (GameInput.MapPressed)
                {
                    if (State == FlowState.Playing) OpenMap(); else if (State == FlowState.Map) Resume();
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
            Save.caught.Add(new CaughtEntry { id = c.defId, playTime = Save.playTime, gameHour = environment.timeOfDay, position = c.transform.position });
            c.BecomeCompanion(Save.caught.Count - 1);
            Pets.Stow(c);   // 1123c-B2: it joins the collection; J brings it out
            Toast?.Invoke($"捕捉成功：{c.Def.name}！（{CaughtCount}/{Total}）" + (c.Def.Count > 1 ? $" 其余的{c.Def.name}会继续留在野外。" : ""));
            Toast?.Invoke($"按 J 换{c.Def.name}陪在身边：{PetSwitcher.Ability(c.defId)}");
            if (AllCaught) Toast?.Invoke("伙伴们都到齐了！回到悬崖木屋前的篝火吧。");
            SaveNow();
        }

        /// <summary>1123c-B4 reward on top of the watchtower: where the pets still to find live.</summary>
        string TowerView()
        {
            var left = CreatureDefs.All.Where(d => !Save.HasCaught(d.id)).Select(d => $"{d.name}：{d.hint.Split('。')[0]}").ToArray();
            return left.Length == 0 ? "伙伴们都已经在你身边了。" : "远远望见 " + string.Join("；", left) + "。";
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
            Save.indoors = EnvController.Indoors;
            SaveSystem.SaveGame(Save);
        }

        void TriggerEnding()
        {
            _endingTriggered = true;
            Save.finished = true;
            SaveNow();
            Pets.ShowAll();   // 1123c-B2: every collected pet comes out for the ending
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
