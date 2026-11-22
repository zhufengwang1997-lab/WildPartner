using UnityEngine;
using WildPartner.World;

namespace WildPartner.Creatures
{
    public enum Movement { Ground, Flying, Water }
    public enum Appear { Always, Day, Night, Rain }

    /// <summary>Static data for one species. Differences in senses, speed, habitat and timing (PRD 4.4).</summary>
    public class CreatureDef
    {
        public string id, name, description, hint, model;
        public Movement movement;
        public Appear appear;
        public float alertRadius;     // detection radius at player Noise = 1
        public float alertRate;       // alert meter per second inside the radius
        public float wanderSpeed, fleeSpeed;
        public float captureSeconds;
        public float captureRange = 3.6f;
        public float wanderRadius = 22f;
        public Player.ProceduralAnimator.Style anim;
        public float animScale = 1f;
        public Vector2[] anchors;     // habitat centres (world xz)
        public float hover;           // flyers: height above ground
        public bool startsWithPlayer;  // default pet (the cat): already a companion when a new game starts
        public Color journalColor;
        /// <summary>Several of this species in the world (试玩反馈 1123b-6/7/8): one home (world xz) per instance.
        /// Catching any one of them collects the species; the rest stay wild and can no longer be caught.
        /// null = a single creature that roams between <see cref="anchors"/>.</summary>
        public Vector2[] homes;
        public int Count => homes != null ? homes.Length : 1;
        /// <summary>Extra factor on the detection radius while the witch sneaks (rabbit: 0.5).</summary>
        public float sneakRadiusMul = 1f;
        /// <summary>How far it runs when scared; 0 = alertRadius * 1.8.</summary>
        public float fleeDistance;
        /// <summary>A capture spoiled by its alert meter first only startles it (ears up ~1 s); if the witch then
        /// keeps still it relaxes, if she moves it runs.</summary>
        public bool startleFirst;
        /// <summary>Grazers: walk about this many seconds, then stop and graze (0 = graze only at the end of a stroll).</summary>
        public float walkBurst;
        /// <summary>Stops to graze (head down) on its strolls and after running away.</summary>
        public bool grazes;
        /// <summary>Vision cone in degrees (0 = senses all round by alertRadius * Noise). With a cone, alertRadius is
        /// how far it sees (times grazeViewMul while grazing) and hearRadius * Noise how far it hears (1123c-B1).</summary>
        public float viewAngle, grazeViewMul = 1f, hearRadius;
        /// <summary>The caught one can be ridden (white deer).</summary>
        public bool rideable;

        public bool ConditionMet(float hour, bool raining) => appear switch
        {
            Appear.Day => hour >= 6f && hour < 19.5f,
            Appear.Night => hour >= 19f || hour < 5.5f,
            Appear.Rain => raining,
            _ => true,
        };

        public string ConditionText => appear switch
        {
            Appear.Day => "白天", Appear.Night => "夜晚", Appear.Rain => "雨天", _ => "全天",
        };
    }

    public static class CreatureDefs
    {
        public static readonly CreatureDef[] All =
        {
            new CreatureDef
            {
                id = "moon_rabbit", name = "月影兔", model = "MoonRabbit",
                description = "额头有月牙印记的小兔子，耳朵末端带着淡紫色。胆子很小，一有动静就蹦得飞快。",
                hint = "白天在山谷各处的草地上，两三只一群（木屋坡下就有一群）。胆小——按住 Ctrl 潜行慢慢靠近；它竖起耳朵时站着别动。",
                movement = Movement.Ground, appear = Appear.Day,
                // 试玩反馈 1123b-6: notice distance 12 -> 7.2 m (-40%), halved again while sneaking; hold 2.0 -> 1.4 s
                alertRadius = 7.2f, alertRate = 0.45f, wanderSpeed = 1.8f, fleeSpeed = 7f, captureSeconds = 1.4f,
                sneakRadiusMul = 0.5f, fleeDistance = 6f, startleFirst = true, grazes = true, wanderRadius = 5f,
                anim = Player.ProceduralAnimator.Style.Hopper, animScale = 0.8f,
                anchors = new[] { new Vector2(218, 122), new Vector2(192, 143), new Vector2(178, 112) },
                // 1123c-B5: twelve in five groups of 2-3 across the meadows (tools: HabitatPicker)
                homes = new[] { new Vector2(206.1f, 128.7f), new Vector2(208.5f, 129.8f), new Vector2(204.3f, 124.9f),
                                new Vector2(155.5f, 129.0f), new Vector2(154.8f, 126.3f),
                                new Vector2(246.3f, 103.2f), new Vector2(249.8f, 105.6f), new Vector2(246.7f, 105.8f),
                                new Vector2(165.8f, 181.4f), new Vector2(163.8f, 182.2f),
                                new Vector2(113.1f, 242.1f), new Vector2(107.2f, 239.3f) },
                journalColor = new Color(0.92f, 0.88f, 1f),
            },
            new CreatureDef
            {
                id = "white_deer", name = "白色神鹿", model = "WhiteDeer",
                description = "秋色林里的白鹿，传说是森林的守护者。视觉和听觉都非常敏锐。",
                hint = "北边的林子里散着三只（西北深林、北边、东北秋色林各一只），走几步就停下吃草。眼睛只看前方（低头吃草时看得更近），耳朵听得见脚步——从背后或侧后方潜行靠近；它抬头时站着别动。收服的那只可以骑（靠近按 E）。",
                movement = Movement.Ground, appear = Appear.Always,
                // 1123c-B1: sees a 120° cone 20 m ahead (7 m while grazing), hears 9 m x noise (run 14 m, walk 9 m,
                // sneak 4 m): from behind, sneaking, she can walk up to the 4.2 m capture range unheard
                alertRadius = 20f, alertRate = 0.4f, wanderSpeed = 1.4f, fleeSpeed = 10f, captureSeconds = 2.6f,
                viewAngle = 120f, grazeViewMul = 0.35f, hearRadius = 9f,
                captureRange = 4.2f, wanderRadius = 10f, walkBurst = 3f, grazes = true, rideable = true, fleeDistance = 22f,
                // 试玩反馈 1123b-8: three in the woods north of the stone circle, two together and one apart
                homes = new[] { new Vector2(157.8f, 302.5f), new Vector2(73.6f, 209.7f), new Vector2(214.0f, 268.1f) },   // 1123c-B5: spread over the northern woods
                anim = Player.ProceduralAnimator.Style.Quadruped, animScale = 2f,
                anchors = new[] { new Vector2(560, 800) * WorldLayout.K, new Vector2(470, 830) * WorldLayout.K, new Vector2(650, 790) * WorldLayout.K },
                journalColor = new Color(0.98f, 0.96f, 0.9f),
            },
            new CreatureDef
            {
                id = "moon_butterfly", name = "月影蝶", model = "MoonButterfly",
                description = "只在夜里出现的蝴蝶，翅膀像月光一样微微发亮。飞得不快，但很难预测方向。",
                hint = "只在夜晚出现在湖畔，翅膀会发光。白天去了也找不到。",
                movement = Movement.Flying, appear = Appear.Night,
                alertRadius = 6f, alertRate = 0.5f, wanderSpeed = 1.5f, fleeSpeed = 4.5f, captureSeconds = 1.8f,
                anim = Player.ProceduralAnimator.Style.Flyer, animScale = 0.6f, hover = 1.6f, wanderRadius = 16f,
                anchors = new[] { new Vector2(812, 708) * WorldLayout.K, new Vector2(690, 790) * WorldLayout.K, new Vector2(905, 730) * WorldLayout.K },
                journalColor = new Color(0.7f, 0.75f, 1f),
            },
            new CreatureDef
            {
                id = "ink_koi", name = "赤墨锦鲤", model = "Koi",
                description = "墨黑底色、赤红花纹的锦鲤，尾鳍像水墨一样飘散。平时藏在深水里。",
                hint = "下雨的时候才会浮上来：瀑布下的上游、木桥下、池塘边的下游、大湖西南岸的浅水里各一条。站在岸边或桥上等它。",
                movement = Movement.Water, appear = Appear.Rain,
                alertRadius = 8f, alertRate = 0.35f, wanderSpeed = 1.2f, fleeSpeed = 4f, captureSeconds = 2.2f,
                captureRange = 7f, wanderRadius = 6f,
                anim = Player.ProceduralAnimator.Style.Swimmer, animScale = 0.8f,
                // 试玩反馈 1123b-7: three koi - under the bridge, at the river bend, in the lake shallows
                homes = new[] { new Vector2(142.9f, 261.9f), new Vector2(258.8f, 235.5f), new Vector2(242.2f, 159.2f), new Vector2(270.0f, 254.0f) },   // 1123c-B5: upper / middle (bridge) / lower river + lake
                anchors = new[] { new Vector2(746, 640) * WorldLayout.K, new Vector2(745, 560) * WorldLayout.K, new Vector2(712, 712) * WorldLayout.K },
                journalColor = new Color(1f, 0.6f, 0.6f),
            },
            new CreatureDef
            {
                id = "cat", name = "猫", model = "Cat", startsWithPlayer = true,
                description = "女巫的老伙伴，毛蓬蓬的小猫。总是跟在你脚边，偶尔停下来舔舔爪子。",
                hint = "开局就在你身边。",
                movement = Movement.Ground, appear = Appear.Always,
                alertRadius = 1f, alertRate = 0f, wanderSpeed = 1.2f, fleeSpeed = 6f, captureSeconds = 1f,
                captureRange = 2f, wanderRadius = 3f,
                anim = Player.ProceduralAnimator.Style.Quadruped, animScale = 0.45f,
                anchors = new[] { WorldLayout.PlayerStart + new Vector2(1.5f, 1f) },
                journalColor = new Color(0.86f, 0.84f, 0.9f),
            },
        };

        public static CreatureDef Get(string id) => System.Array.Find(All, d => d.id == id);
    }
}
