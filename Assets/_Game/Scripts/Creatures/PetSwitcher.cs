using System.Linq;
using UnityEngine;
using WildPartner.Core;
using WildPartner.Player;
using WildPartner.World;

namespace WildPartner.Creatures
{
    /// <summary>
    /// 试玩反馈 1123c-B2: only the current pet walks with her (the cat at the start); the other collected pets are
    /// kept out of sight. J cycles to the next collected one with a little magic puff where the old one vanishes and
    /// the new one appears. Only the current pet's ability works:
    ///   cat - leads to uncollected creatures; moon rabbit - higher jump; white deer - ride (E);
    ///   ink koi - walk on water; moon butterfly - circles her with a purple trail and lights the night.
    /// Riding, or standing on water with the koi, she is first put down safely (off the deer / onto the shore).
    /// </summary>
    public class PetSwitcher : MonoBehaviour
    {
        public static PetSwitcher Instance { get; private set; }
        /// <summary>Order of J cycling.</summary>
        public static readonly string[] Order = { "cat", "moon_rabbit", "white_deer", "ink_koi", "moon_butterfly" };
        /// <summary>The pet at her side now (id).</summary>
        public static string Active { get; private set; } = "cat";
        public static bool Is(string id) => Active == id;
        public static event System.Action<CreatureDef> Switched;

        public static string Ability(string id) => id switch
        {
            "cat" => "领路：附近有没收集的宠物时，猫会看向那边叫一声",
            "moon_rabbit" => "跳得更高",
            "white_deer" => "靠近按 E 骑乘，再按 E 下来；骑着跑得更快",
            "ink_koi" => "踏水而行：可以走在河面和湖面上",
            "moon_butterfly" => "环绕飞行，紫色尾焰，夜里照亮四周",
            _ => "",
        };

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public static Creature Pet(string id) => Companion(id);
        static Creature Companion(string id) =>
            FindObjectsByType<Creature>(FindObjectsSortMode.None).FirstOrDefault(c => c.defId == id && c.Current == Creature.State.Companion);

        void Update()
        {
            var flow = GameFlow.Instance;
            if (flow == null || flow.State != GameFlow.FlowState.Playing || CabinInterior.Busy) return;
            if (GameInput.SwitchPetPressed) Next();
        }

        /// <summary>Make the save's pet current (new game / continue) without effects.</summary>
        public void Restore(string id)
        {
            var flow = GameFlow.Instance;
            if (string.IsNullOrEmpty(id) || flow == null || !flow.Save.HasCaught(id) || Companion(id) == null) id = "cat";
            Active = id;
            Apply(false);
        }

        /// <summary>Just caught (not the current pet): it vanishes in a puff into the collection.</summary>
        public void Stow(Creature c)
        {
            if (c.defId == Active) return;
            Puff(c.transform.position + Vector3.up * 0.4f);
            c.SetPetShown(false);
        }

        /// <summary>The ending: everyone comes out round the campfire.</summary>
        public void ShowAll()
        {
            foreach (var c in FindObjectsByType<Creature>(FindObjectsSortMode.None))
                if (c.Current == Creature.State.Companion && !c.PetShown) { c.SetPetShown(true); Puff(c.transform.position + Vector3.up * 0.4f); }
        }

        public void Next()
        {
            var flow = GameFlow.Instance;
            if (flow == null) return;
            int i = System.Array.IndexOf(Order, Active);
            for (int k = 1; k <= Order.Length; k++)
            {
                string id = Order[(i + k) % Order.Length];
                if (flow.Save.HasCaught(id) && Companion(id) != null) { SetActive(id); return; }
            }
        }

        /// <summary>Switch to `id` (must be collected).</summary>
        public void SetActive(string id)
        {
            var player = PlayerController.Instance;
            if (player == null || id == Active) { Switched?.Invoke(CreatureDefs.Get(id)); return; }
            // never leave her in mid-air or in the water: off the deer first, off the water onto the shore
            var deer = Companion("white_deer");
            if (player.Riding && deer) deer.Dismount(player);
            if (player.WaterWalking) player.StepAshore();
            var old = Companion(Active);
            if (old) Puff(old.transform.position + Vector3.up * 0.4f);
            Active = id;
            Apply(true);
            var now = Companion(id);
            if (now) Puff(now.transform.position + Vector3.up * 0.4f);
            if (GameFlow.Instance) GameFlow.Instance.Save.currentPet = id;
            Debug.Log($"[Pet] current pet -> {id}");
            Switched?.Invoke(CreatureDefs.Get(id));
        }

        /// <summary>Show the current companion beside her, hide the rest.</summary>
        void Apply(bool place)
        {
            var player = PlayerController.Instance;
            foreach (var c in FindObjectsByType<Creature>(FindObjectsSortMode.None))
            {
                if (c.Current != Creature.State.Companion) continue;
                bool on = c.defId == Active;
                c.SetPetShown(on);
                if (on && place && player)
                {
                    var side = player.transform.right * 1.2f - player.transform.forward * 0.6f;
                    c.WarpNear(player.transform.position + side, player.transform.eulerAngles.y);
                }
            }
        }

        public void Puff(Vector3 at)
        {
            var go = new GameObject("PetPuff", typeof(ParticleSystem));
            go.transform.position = at;
            var ps = go.GetComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.duration = 0.5f; main.loop = false; main.startLifetime = new ParticleSystem.MinMaxCurve(0.5f, 0.9f);
            main.startSpeed = new ParticleSystem.MinMaxCurve(0.6f, 2.2f); main.startSize = new ParticleSystem.MinMaxCurve(0.06f, 0.2f);
            main.startColor = new ParticleSystem.MinMaxGradient(new Color(0.8f, 0.6f, 1f), new Color(1f, 0.92f, 0.6f));
            main.gravityModifier = -0.15f; main.simulationSpace = ParticleSystemSimulationSpace.World; main.stopAction = ParticleSystemStopAction.Destroy;
            var em = ps.emission; em.rateOverTime = 0; em.SetBursts(new[] { new ParticleSystem.Burst(0, 40) });
            var sh = ps.shape; sh.shapeType = ParticleSystemShapeType.Sphere; sh.radius = 0.4f;
            var col = ps.colorOverLifetime; col.enabled = true;
            var g = new Gradient();
            g.SetKeys(new[] { new GradientColorKey(Color.white, 0), new GradientColorKey(Color.white, 1) },
                      new[] { new GradientAlphaKey(1, 0), new GradientAlphaKey(0.8f, 0.5f), new GradientAlphaKey(0, 1) });
            col.color = g;
            go.GetComponent<ParticleSystemRenderer>().sharedMaterial = FxMat.Additive;
            ps.Play();
        }
    }
}
