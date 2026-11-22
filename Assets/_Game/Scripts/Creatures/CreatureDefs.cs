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
                hint = "白天在中央草甸的石阵附近蹦跳。很胆小——按住 Ctrl 潜行慢慢靠近。",
                movement = Movement.Ground, appear = Appear.Day,
                alertRadius = 12f, alertRate = 0.45f, wanderSpeed = 1.8f, fleeSpeed = 9f, captureSeconds = 2.0f,
                anim = Player.ProceduralAnimator.Style.Hopper, animScale = 0.8f,
                anchors = new[] { WorldLayout.StoneCircle + new Vector2(18, -10), new Vector2(430, 500), new Vector2(270, 610) },
                journalColor = new Color(0.92f, 0.88f, 1f),
            },
            new CreatureDef
            {
                id = "white_deer", name = "白色神鹿", model = "WhiteDeer",
                description = "秋色林里的白鹿，传说是森林的守护者。视觉和听觉都非常敏锐。",
                hint = "在北边的秋色林里。警觉性极高，远远就会发现你——一路潜行，别跑。",
                movement = Movement.Ground, appear = Appear.Always,
                alertRadius = 20f, alertRate = 0.4f, wanderSpeed = 1.4f, fleeSpeed = 10f, captureSeconds = 2.6f,
                captureRange = 4.2f,
                anim = Player.ProceduralAnimator.Style.Quadruped, animScale = 2f,
                anchors = new[] { new Vector2(560, 800), new Vector2(470, 830), new Vector2(650, 790) },
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
                anchors = new[] { new Vector2(812, 708), new Vector2(690, 790), new Vector2(905, 730) },
                journalColor = new Color(0.7f, 0.75f, 1f),
            },
            new CreatureDef
            {
                id = "ink_koi", name = "赤墨锦鲤", model = "Koi",
                description = "墨黑底色、赤红花纹的锦鲤，尾鳍像水墨一样飘散。平时藏在深水里。",
                hint = "下雨的时候才会浮到木桥附近的河面上。站在岸边或桥上等它。",
                movement = Movement.Water, appear = Appear.Rain,
                alertRadius = 8f, alertRate = 0.35f, wanderSpeed = 1.2f, fleeSpeed = 4f, captureSeconds = 2.2f,
                captureRange = 7f, wanderRadius = 14f,
                anim = Player.ProceduralAnimator.Style.Swimmer, animScale = 0.8f,
                anchors = new[] { new Vector2(746, 640), new Vector2(745, 560), new Vector2(712, 712) },
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
