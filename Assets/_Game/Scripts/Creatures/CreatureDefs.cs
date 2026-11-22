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
                hint = "白天在木屋坡下的草地上，两三只一群。胆小——按住 Ctrl 潜行慢慢靠近；它竖起耳朵时站着别动。",
                movement = Movement.Ground, appear = Appear.Day,
                // 试玩反馈 1123b-6: notice distance 12 -> 7.2 m (-40%), halved again while sneaking; hold 2.0 -> 1.4 s
                alertRadius = 7.2f, alertRate = 0.45f, wanderSpeed = 1.8f, fleeSpeed = 7f, captureSeconds = 1.4f,
                sneakRadiusMul = 0.5f, fleeDistance = 6f, startleFirst = true, grazes = true, wanderRadius = 5f,
                anim = Player.ProceduralAnimator.Style.Hopper, animScale = 0.8f,
                anchors = new[] { new Vector2(218, 122), new Vector2(192, 143), new Vector2(178, 112) },
                // six in three pairs on the meadow below the cabin ramp, south-west of the pond
                homes = new[] { new Vector2(217, 121), new Vector2(219.5f, 123.5f),
                                new Vector2(192, 143), new Vector2(194.5f, 141),
                                new Vector2(178, 112), new Vector2(180.5f, 114.5f) },
                journalColor = new Color(0.92f, 0.88f, 1f),
            },
            new CreatureDef
            {
                id = "white_deer", name = "白色神鹿", model = "WhiteDeer",
                description = "秋色林里的白鹿，传说是森林的守护者。视觉和听觉都非常敏锐。",
                hint = "石阵北边的林子里有三只，走几步就停下吃草。警觉性极高，远远就会发现你——一路潜行，别跑。收服的那只可以骑（靠近按 E）。",
                movement = Movement.Ground, appear = Appear.Always,
                alertRadius = 20f, alertRate = 0.4f, wanderSpeed = 1.4f, fleeSpeed = 10f, captureSeconds = 2.6f,
                captureRange = 4.2f, wanderRadius = 10f, walkBurst = 3f, grazes = true, rideable = true, fleeDistance = 22f,
                // 试玩反馈 1123b-8: three in the woods north of the stone circle, two together and one apart
                homes = new[] { new Vector2(106f, 219f), new Vector2(109f, 221.5f), new Vector2(131f, 231f) },
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
                hint = "下雨的时候才会浮上来：一条在木桥下，一条在南边的河湾，一条在大湖南岸的浅水里。站在岸边或桥上等它。",
                movement = Movement.Water, appear = Appear.Rain,
                alertRadius = 8f, alertRate = 0.35f, wanderSpeed = 1.2f, fleeSpeed = 4f, captureSeconds = 2.2f,
                captureRange = 7f, wanderRadius = 6f,
                anim = Player.ProceduralAnimator.Style.Swimmer, animScale = 0.8f,
                // 试玩反馈 1123b-7: three koi - under the bridge, at the river bend, in the lake shallows
                homes = new[] { new Vector2(263f, 233.4f), new Vector2(253.8f, 164.5f), new Vector2(300f, 262f) },
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
