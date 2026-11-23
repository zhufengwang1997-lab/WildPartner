using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using WildPartner.World;

namespace WildPartner.EditorTools
{
    /// <summary>
    /// The cabin interior (试玩反馈 1123b-3, layout after docs/reference/木屋内部拆解.png): one 8 × 5.5 m room with a
    /// pitched timber roof, stone fireplace on the left wall, bed and window on the right, table with benches and the
    /// journal book in the middle, wall lamps, and the door in the front wall. It is a separate room outside the map
    /// (WorldLayout.InteriorOrigin); CabinInterior fades and teleports between it and the cabin's front door.
    /// Walls and floor are cubes with small generated plank textures; furniture is the Tripo prop set.
    /// Room frame: x to the right, z away from the door, y up, origin on the floor at the room centre.
    /// </summary>
    public static partial class WorldBuilder
    {
        const float RoomW = 8f, RoomD = 5.5f, RoomH = 3f, RoofTop = 4.3f;
        const string InteriorTex = "Assets/_Game/Textures/Interior";

        static CabinInterior BuildInterior(Transform parent)
        {
            var root = new GameObject("CabinInterior").transform;
            root.SetParent(parent, false);
            root.position = WorldLayout.InteriorOrigin;
            Vector3 O = root.position;
            Vector3 P(float x, float y, float z) => O + new Vector3(x, y, z);

            var floorMat = InteriorMat("M_Int_Floor", PlankTexture("Floor", new Color(0.50f, 0.33f, 0.20f), 5, 0.22f, 7), new Vector2(RoomW / 1.2f, RoomD / 1.2f));
            var wallMat = InteriorMat("M_Int_Wall", PlankTexture("Wall", new Color(0.66f, 0.47f, 0.29f), 4, 0.16f, 11), new Vector2(RoomW / 1.5f, RoomH / 1.5f));
            var sideMat = InteriorMat("M_Int_WallSide", PlankTexture("Wall", new Color(0.66f, 0.47f, 0.29f), 4, 0.16f, 11), new Vector2(RoomD / 1.5f, RoofTop / 1.5f));
            var roofMat = InteriorMat("M_Int_Roof", PlankTexture("Roof", new Color(0.42f, 0.28f, 0.18f), 6, 0.2f, 19), new Vector2(RoomW / 1.2f, 3.1f / 1.2f));
            var beamMat = InteriorMat("M_Int_Beam", PlankTexture("Beam", new Color(0.30f, 0.19f, 0.12f), 2, 0.25f, 23), Vector2.one);
            var rugMat = InteriorMat("M_Int_Rug", RugTexture(), Vector2.one);

            // ---- shell ------------------------------------------------------------------------------------------
            Box(root, "Floor", P(0, -0.1f, 0), new Vector3(RoomW + 0.4f, 0.2f, RoomD + 0.4f), floorMat);
            Box(root, "WallBack", P(0, RoomH / 2, RoomD / 2 + 0.1f), new Vector3(RoomW + 0.4f, RoomH, 0.2f), wallMat);
            Box(root, "WallLeft", P(-RoomW / 2 - 0.1f, RoofTop / 2, 0), new Vector3(0.2f, RoofTop + 0.2f, RoomD + 0.4f), sideMat);
            Box(root, "WallRight", P(RoomW / 2 + 0.1f, RoofTop / 2, 0), new Vector3(0.2f, RoofTop + 0.2f, RoomD + 0.4f), sideMat);
            // front wall with the door opening (1.5 × 2.15 m) at x = 0
            const float doorW = 1.5f, doorH = 2.15f;
            float sideW = (RoomW + 0.4f - doorW) / 2;
            Box(root, "WallFrontL", P(-(doorW / 2 + sideW / 2), RoomH / 2, -RoomD / 2 - 0.1f), new Vector3(sideW, RoomH, 0.2f), wallMat);
            Box(root, "WallFrontR", P(doorW / 2 + sideW / 2, RoomH / 2, -RoomD / 2 - 0.1f), new Vector3(sideW, RoomH, 0.2f), wallMat);
            Box(root, "WallFrontTop", P(0, (doorH + RoomH) / 2, -RoomD / 2 - 0.1f), new Vector3(doorW, RoomH - doorH, 0.2f), wallMat);
            // pitched roof (ridge along x) doubling as the ceiling
            float run = RoomD / 2 + 0.25f, rise = RoofTop - RoomH;
            float slope = Mathf.Atan2(rise, run) * Mathf.Rad2Deg, len = Mathf.Sqrt(run * run + rise * rise) + 0.1f;
            for (int s = -1; s <= 1; s += 2)
            {
                var r = Box(root, s < 0 ? "RoofFront" : "RoofBack", P(0, RoomH + rise / 2 + 0.08f, s * run / 2), new Vector3(RoomW + 0.4f, 0.14f, len), roofMat);
                r.transform.rotation = Quaternion.Euler(s * slope, 0, 0);   // 1123e-1: high at the ridge, low over the walls (was inverted: sky showed above the front wall)
            }
            // timber frame: tie beams, ridge beam, corner posts, wall plates
            for (int i = 0; i < 4; i++)
                Box(root, "TieBeam", P(-3f + i * 2f, RoomH - 0.12f, 0), new Vector3(0.2f, 0.24f, RoomD), beamMat);
            Box(root, "RidgeBeam", P(0, RoofTop - 0.12f, 0), new Vector3(RoomW, 0.26f, 0.26f), beamMat);
            for (int s = -1; s <= 1; s += 2)
                Box(root, "WallPlate", P(0, RoomH - 0.1f, s * (RoomD / 2 - 0.1f)), new Vector3(RoomW, 0.2f, 0.2f), beamMat);
            foreach (var (x, z) in new[] { (-1, -1), (1, -1), (-1, 1), (1, 1) })
                Box(root, "Post", P(x * (RoomW / 2 - 0.12f), RoomH / 2, z * (RoomD / 2 - 0.12f)), new Vector3(0.24f, RoomH, 0.24f), beamMat);
            for (int i = 0; i < 2; i++)   // king posts from the tie beams up to the ridge
                Box(root, "KingPost", P(-1f + i * 2f, (RoomH + RoofTop) / 2, 0), new Vector3(0.16f, RoofTop - RoomH, 0.16f), beamMat);
            var rug = Box(root, "Rug", P(-0.2f, 0.006f, 0.3f), new Vector3(3.0f, 0.012f, 2.2f), rugMat);
            Object.DestroyImmediate(rug.GetComponent<Collider>());

            // ---- furniture (Tripo props, FaceYaw points a prop's front at a direction) ------------------------------
            var fireplace = Prop("Fireplace", root, P(-RoomW / 2 + 0.52f, 0, 0.5f), FaceYaw(new Vector2(1, 0)), 1.15f, true);
            var fire = new GameObject("Fire").transform;
            fire.SetParent(root, false);
            fire.position = P(-RoomW / 2 + 0.6f, 0.12f, 0.5f);
            Flames(fire);
            var flames = fire.GetComponentInChildren<ParticleSystem>();
            var fm = flames.main; fm.startSize = new ParticleSystem.MinMaxCurve(0.18f, 0.4f); fm.startSpeed = 0.9f; fm.startLifetime = 0.6f;
            var fsh = flames.shape; fsh.radius = 0.2f;
            var fireLight = PointLight(root, "FireLight", P(-RoomW / 2 + 1.1f, 0.7f, 0.5f), new Color(1f, 0.55f, 0.25f), 2.6f, 7.5f, true);

            var bed = Prop("WoodBed", root, P(RoomW / 2 - 0.85f, 0, RoomD / 2 - 1.15f), FaceYaw(new Vector2(0, -1)), 1.8f, true);
            Prop("Chest", root, P(RoomW / 2 - 0.65f, 0, -0.35f), FaceYaw(new Vector2(-1, 0)), 0.55f, true);
            Prop("Woodpile", root, P(-RoomW / 2 + 0.45f, 0, -1.35f), FaceYaw(new Vector2(1, 0)), 0.45f, true);

            var table = Prop("WoodTable", root, P(-0.2f, 0, 0.3f), 0, 1.05f, true);
            float tableTop = 0.8f * 1.05f;
            for (int s = -1; s <= 1; s += 2)
                for (int k = 0; k < 2; k++)
                    Prop("WoodBench", root, P(-0.68f + k * 0.96f, 0, 0.3f + s * 0.95f), 90, 1.3f, true);
            var book = Prop("Book", root, P(0.15f, tableTop, 0.22f), 25, 1.4f, false);

            // windows: right wall (by the bed) and back wall; a sky-coloured panel behind each shows the time of day
            var winRight = Prop("WoodWindow", root, P(RoomW / 2 - 0.12f, 1.05f, -0.9f), FaceYaw(new Vector2(-1, 0)), 1.25f, false);
            var winBack = Prop("WoodWindow", root, P(-1.6f, 1.05f, RoomD / 2 - 0.12f), FaceYaw(new Vector2(0, -1)), 1.25f, false);
            var glassMat = GlassMat();
            var glassR = Panel(root, "WindowSky", P(RoomW / 2 - 0.02f, 1.05f + 0.62f, -0.9f), Quaternion.Euler(0, 90, 0), new Vector2(1.0f, 1.1f), glassMat);
            var glassB = Panel(root, "WindowSky", P(-1.6f, 1.05f + 0.62f, RoomD / 2 - 0.02f), Quaternion.identity, new Vector2(1.0f, 1.1f), glassMat);
            var winLightR = SpotLight(root, "WindowLight", P(RoomW / 2 - 0.3f, 2.2f, -0.9f), Quaternion.LookRotation(new Vector3(-1, -0.9f, 0.1f)), 2.4f, 7f);
            var winLightB = SpotLight(root, "WindowLight", P(-1.6f, 2.2f, RoomD / 2 - 0.3f), Quaternion.LookRotation(new Vector3(0.05f, -0.9f, -1)), 1.8f, 7f);

            // wall lamps (warm, always on indoors)
            var lamps = new System.Collections.Generic.List<Light>();
            foreach (var (pos, dir) in new[] { (P(1.3f, 1.75f, RoomD / 2 - 0.02f), new Vector2(0, -1)),
                                               (P(-2.6f, 1.75f, -RoomD / 2 + 0.02f), new Vector2(0, 1)),
                                               (P(RoomW / 2 - 0.02f, 1.75f, 1.0f), new Vector2(-1, 0)) })
            {
                Prop("WallLamp", root, pos, FaceYaw(dir), 1.1f, false);
                lamps.Add(PointLight(root, "LampLight", pos + new Vector3(dir.x, 0, dir.y) * 0.3f + Vector3.up * 0.25f, new Color(1f, 0.72f, 0.42f), 1.3f, 4.8f, false));
            }
            PointLight(root, "Fill", P(0.5f, 2.6f, 0.2f), new Color(1f, 0.78f, 0.55f), 0.55f, 9f, false);

            // inside door: the Tripo door leaf on a hinge at the opening's right edge (seen from inside)
            var hinge = new GameObject("DoorHinge").transform;
            hinge.SetParent(root, false);
            hinge.position = P(doorW / 2 - 0.03f, 0, -RoomD / 2 - 0.1f);
            var leaf = Prop("WoodDoor", hinge, P(0, 0, -RoomD / 2 - 0.1f), FaceYaw(new Vector2(0, 1)), 1f, true);
            leaf.transform.localScale = new Vector3(doorW / 1.46f * 1.02f, doorH / 2.0f, 0.45f);
            var outside = Panel(root, "DoorDaylight", P(0, doorH / 2, -RoomD / 2 - 0.6f), Quaternion.identity, new Vector2(doorW + 0.6f, doorH + 0.4f), glassMat);
            DoorInfill(root, hinge, leaf, P(0, 0, -RoomD / 2), doorW, doorH, wallMat, beamMat);

            // ---- the cabin model's own door leaf (split off by tools/blender/cut_cabin_door.py) -----------------------
            Transform outLeaf = null;
            foreach (var t in parent.GetComponentsInChildren<Transform>())
                if (t.name == "Cabin_Door") { outLeaf = t; break; }
            if (outLeaf)
            {
                // a dark, faintly warm doorway behind it, so the opening door reveals an interior instead of the sky
                var cabin = outLeaf.parent;
                var dark = InteriorMat("M_Int_Doorway", null, Vector2.one);
                dark.SetColor("_BaseColor", new Color(0.16f, 0.09f, 0.05f)); dark.SetFloat("_Emission", 0.6f);
                var back = Box(cabin, "Doorway", Vector3.zero, Vector3.one, dark);
                var fwd = new Vector3(WorldLayout.CabinFacing.x, 0, WorldLayout.CabinFacing.y);
                var door = new Vector3(WorldLayout.CabinDoor.x, outLeaf.position.y, WorldLayout.CabinDoor.y);
                // deep enough that the leaf (1.36 m, opens to 70°) swings in front of it, wide and tall enough that no
                // view through the opening slips past it into the (back-face culled) inside of the cabin shell
                back.transform.SetPositionAndRotation(door - fwd * 1.45f + Vector3.up * 1.1f, Quaternion.LookRotation(fwd));
                back.transform.localScale = new Vector3(4f, 4.2f, 0.2f);   // reaches below the ground: no grass showing under it
                back.transform.SetParent(cabin, true);
                Object.DestroyImmediate(back.GetComponent<Collider>());
            }
            else Debug.LogWarning("[World] Cabin_Door not found: run tools/blender/cut_cabin_door.py on the cabin FBX");

            // ---- spots --------------------------------------------------------------------------------------------
            var ci = root.gameObject.AddComponent<CabinInterior>();
            ci.outsideLeaf = outLeaf;
            ci.insideHinge = hinge;
            var outFwd = WorldLayout.CabinFacing;
            Vector2 outSpot = WorldLayout.CabinDoor + outFwd * 2.6f;   // clear of the steps; the camera has room behind her
            ci.outsideDoor = new Vector3(WorldLayout.CabinDoor.x, TerrainH(WorldLayout.CabinDoor.x, WorldLayout.CabinDoor.y), WorldLayout.CabinDoor.y);
            ci.outsideSpot = new Vector3(outSpot.x, TerrainH(outSpot.x, outSpot.y) + 0.15f, outSpot.y);
            ci.outsideYaw = Mathf.Atan2(outFwd.x, outFwd.y) * Mathf.Rad2Deg;
            ci.insideDoor = P(0, 0, -RoomD / 2 + 0.3f);
            ci.insideSpot = P(0, 0.08f, -RoomD / 2 + 1.2f);
            ci.insideYaw = 0;
            ci.bed = bed.transform.position;
            ci.bedLie = P(RoomW / 2 - 0.85f, 0.6f, RoomD / 2 - 1.15f - 0.97f + 0.3f);   // feet end of the mattress
            ci.bedYaw = 180;   // feet towards the door, head on the pillow by the back wall
            // 1123c-A5: the WakeUp clip (tools/blender/add_clips.py) ends with her standing 0.35 m in front of the
            // bed's near edge, facing away from it; it starts with her lying on the mattress
            var bedB = new Bounds(bed.transform.position, Vector3.zero);
            foreach (var r in bed.GetComponentsInChildren<Renderer>()) bedB.Encapsulate(r.bounds);
            ci.bedStand = new Vector3(bedB.min.x - 0.35f, O.y + 0.08f, bed.transform.position.z);
            Debug.Log($"[World] bed bounds {bedB.min - O} .. {bedB.max - O} (room space); stand {ci.bedStand - O}");
            foreach (var o in AssetDatabase.LoadAllAssetsAtPath($"{AssetSetup.Rigged}/Witch.fbx"))
                if (o is AnimationClip c && c.name == "WakeUp") ci.wakeClip = c;
            if (!ci.wakeClip) Debug.LogWarning("[World] Witch.fbx has no WakeUp clip (run tools/blender/add_clips.py)");
            ci.book = book.transform.position;
            ci.lights = lamps.ToArray();
            ci.fireLight = fireLight;
            ci.windowLights = new[] { winLightR, winLightB };
            ci.windowMaterial = glassMat;
            ci.roomCenter = P(0, 1.2f, 0);
            Debug.Log("[World] cabin interior built" + (outLeaf ? "" : " (no outside door leaf)"));
            return ci;
        }

        /// <summary>试玩反馈 1123c-A3. The Tripo door leaf is arched but the wall opening is a rectangle: fill the two
        /// corners above the arch with wall (following the leaf's real outline, measured from its vertices), and put a
        /// dark board behind the leaf (swinging with it) so cracks in the decimated mesh no longer show the bright
        /// daylight panel as white lines. `wallIn` is the inner wall face at the door centre, on the floor.</summary>
        static void DoorInfill(Transform root, Transform hinge, GameObject leaf, Vector3 wallIn, float doorW, float doorH, Material wallMat, Material backMat)
        {
            // leaf outline: the Tripo leaf is a rectangle with a semicircular top whose apex touches the opening top
            // (its vertices can't be used: the decimated arch frame is ragged). Bands get finer towards the top.
            const int Bands = 64;
            float rad = doorW / 2 - 0.005f, cy = doorH - rad;
            var lo = new float[Bands]; var hi = new float[Bands];
            for (int b = 0; b < Bands; b++)
            {
                float y = (b + 0.5f) / Bands * doorH;
                float hw = y <= cy ? doorW / 2 : Mathf.Sqrt(Mathf.Max(rad * rad - (y - cy) * (y - cy), 0f));
                lo[b] = -hw; hi[b] = hw;
            }
            float L = -doorW / 2, R = doorW / 2;
            var vIn = new System.Collections.Generic.List<Vector3>(); var tIn = new System.Collections.Generic.List<int>();
            var vBk = new System.Collections.Generic.List<Vector3>(); var tBk = new System.Collections.Generic.List<int>();
            void Quad(System.Collections.Generic.List<Vector3> vs, System.Collections.Generic.List<int> ts, float x0, float x1, float y0, float y1, float z)
            {
                if (x1 - x0 < 0.003f) return;
                int i = vs.Count;
                // 1123e-1: each side gets its own four vertices. Sharing them made RecalculateNormals sum the two
                // opposite face normals to zero -> NaN in the shader -> bloom spread it into a white glow on the door.
                for (int side = 0; side < 2; side++)
                {
                    vs.Add(new Vector3(x0, y0, z)); vs.Add(new Vector3(x1, y0, z)); vs.Add(new Vector3(x0, y1, z)); vs.Add(new Vector3(x1, y1, z));
                }
                ts.AddRange(new[] { i, i + 2, i + 1, i + 1, i + 2, i + 3, i + 4, i + 5, i + 6, i + 5, i + 7, i + 6 });
            }
            int filled = 0;
            for (int b = 0; b < Bands; b++)
            {
                float y0 = b / (float)Bands * doorH, y1 = (b + 1) / (float)Bands * doorH + 0.002f;
                float a = lo[b] + 0.015f, c = hi[b] - 0.015f;   // overlap the leaf edge a little
                if (a > L + 0.01f) { Quad(vIn, tIn, L - 0.02f, a, y0, y1, 0.012f); filled++; }
                if (c < R - 0.01f) { Quad(vIn, tIn, c, R + 0.02f, y0, y1, 0.012f); filled++; }
                Quad(vBk, tBk, lo[b] + 0.03f, hi[b] - 0.03f, y0, y1, -0.16f);   // board just outside the leaf
            }
            GameObject Make(string name, System.Collections.Generic.List<Vector3> vs, System.Collections.Generic.List<int> ts, Material m, Transform parent, Vector3 at)
            {
                var mesh = new Mesh { name = name };
                var uv = new System.Collections.Generic.List<Vector2>();
                foreach (var q in vs) uv.Add(new Vector2(q.x / 1.5f, q.y / 1.5f));
                mesh.SetVertices(vs); mesh.SetUVs(0, uv); mesh.SetTriangles(ts, 0); mesh.RecalculateNormals(); mesh.RecalculateBounds();
                AssetDatabase.CreateAsset(mesh, $"{TerrainDir}/{name}.asset");
                var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
                go.transform.SetParent(parent, true);
                go.transform.position = at;
                go.GetComponent<MeshFilter>().sharedMesh = mesh;
                var mr = go.GetComponent<MeshRenderer>(); mr.sharedMaterial = m; mr.shadowCastingMode = ShadowCastingMode.Off;
                return go;
            }
            Make("DoorArchInfill", vIn, tIn, wallMat, root, wallIn);
            Make("DoorBacking", vBk, tBk, backMat, hinge, wallIn);   // child of the hinge: swings with the leaf
            Debug.Log($"[World] door infill: {filled} strips above the arch, backing board behind the leaf");
        }

        // ------------------------------------------------------------------------------------------------ helpers
        static GameObject Prop(string prefab, Transform parent, Vector3 pos, float yaw, float scale, bool collider)
        {
            var p = AssetDatabase.LoadAssetAtPath<GameObject>($"{AssetSetup.EnvPrefabs}/{prefab}.prefab");
            if (!p) { Debug.LogWarning($"[World] missing prefab {prefab}"); return new GameObject(prefab); }
            var go = (GameObject)PrefabUtility.InstantiatePrefab(p, parent);
            go.transform.SetPositionAndRotation(pos, Quaternion.Euler(0, yaw, 0));
            go.transform.localScale = Vector3.one * scale;
            foreach (var c in go.GetComponentsInChildren<Collider>()) Object.DestroyImmediate(c);
            if (collider)
            {
                var r = go.GetComponentInChildren<MeshFilter>();
                if (r)
                {
                    var b = r.sharedMesh.bounds;
                    var bc = r.gameObject.AddComponent<BoxCollider>();
                    bc.center = b.center; bc.size = Vector3.Scale(b.size, new Vector3(0.95f, 1f, 0.95f));
                    if (prefab == "WoodBed") { bc.size = new Vector3(bc.size.x, b.size.y * 0.5f, bc.size.z); bc.center = new Vector3(b.center.x, b.min.y + b.size.y * 0.25f, b.center.z); }
                }
                GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic);
            }
            foreach (var mr in go.GetComponentsInChildren<MeshRenderer>()) mr.shadowCastingMode = ShadowCastingMode.Off;
            DoubleSided(go);
            return go;
        }

        /// <summary>1123c-A3: the decimated Tripo props are full of hairline cracks (thousands of open edges, see
        /// DECISIONS). Drawing back faces too means a crack shows the inside of the same wood, not the room behind.</summary>
        static void DoubleSided(GameObject go)
        {
            if (!go) return;
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                foreach (var m in r.sharedMaterials)
                    if (m && m.HasProperty("_Cull") && m.shader.name == "WildPartner/Toon" && m.GetFloat("_Cull") != 0f) { m.SetFloat("_Cull", 0f); EditorUtility.SetDirty(m); }
        }

        static GameObject Box(Transform parent, string name, Vector3 pos, Vector3 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.localScale = size;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;   // indoors only point lights without shadows
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.NavigationStatic | StaticEditorFlags.BatchingStatic);
            return go;
        }

        static Renderer Panel(Transform parent, string name, Vector3 pos, Quaternion rot, Vector2 size, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            go.transform.localScale = new Vector3(size.x, size.y, 1);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat; mr.shadowCastingMode = ShadowCastingMode.Off;
            return mr;
        }

        static Light PointLight(Transform parent, string name, Vector3 pos, Color c, float intensity, float range, bool flicker)
        {
            var go = new GameObject(name, typeof(Light));
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            var l = go.GetComponent<Light>();
            l.type = LightType.Point; l.color = c; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            if (flicker) { var nl = go.AddComponent<NightLight>(); nl.indoor = true; nl.flicker = true; }
            return l;
        }

        static Light SpotLight(Transform parent, string name, Vector3 pos, Quaternion rot, float intensity, float range)
        {
            var go = new GameObject(name, typeof(Light));
            go.transform.SetParent(parent, false);
            go.transform.SetPositionAndRotation(pos, rot);
            var l = go.GetComponent<Light>();
            l.type = LightType.Spot; l.spotAngle = 70; l.innerSpotAngle = 30; l.intensity = intensity; l.range = range; l.shadows = LightShadows.None;
            return l;
        }

        static Material InteriorMat(string name, Texture tex, Vector2 tiling)
        {
            string path = $"{AssetSetup.MatDir}/{name}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("WildPartner/Toon")); AssetDatabase.CreateAsset(m, path); }
            m.SetTexture("_BaseMap", tex ? tex : Texture2D.whiteTexture);
            m.SetTextureScale("_BaseMap", tiling);
            m.SetColor("_BaseColor", Color.white);
            m.SetColor("_ShadowColor", new Color(0.62f, 0.48f, 0.42f));   // warm shade indoors, not the outdoor blue
            m.SetFloat("_RampSteps", 3);
            m.SetFloat("_Emission", 0f);
            EditorUtility.SetDirty(m);
            return m;
        }

        static Material GlassMat()
        {
            string path = $"{AssetSetup.MatDir}/M_Int_WindowSky.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (!m) { m = new Material(Shader.Find("Universal Render Pipeline/Unlit")); AssetDatabase.CreateAsset(m, path); }
            m.SetColor("_BaseColor", new Color(0.8f, 0.9f, 1f));
            EditorUtility.SetDirty(m);
            return m;
        }

        /// <summary>Planks with seams, grain streaks and per-plank tone; saved once as a PNG asset.</summary>
        static Texture2D PlankTexture(string name, Color baseCol, int planks, float variation, int seed)
        {
            string path = $"{InteriorTex}/T_{name}.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing) return existing;
            const int N = 256;
            var rng = new System.Random(seed);
            var px = new Color[N * N];
            int ph = N / planks;
            var seamX = new int[planks];
            var tone = new float[planks * 2];
            for (int i = 0; i < planks; i++) seamX[i] = rng.Next(N);
            for (int i = 0; i < tone.Length; i++) tone[i] = 1f + ((float)rng.NextDouble() - 0.5f) * 2 * variation;
            for (int y = 0; y < N; y++)
            {
                int pl = Mathf.Min(y / ph, planks - 1); int py = y - pl * ph;
                for (int x = 0; x < N; x++)
                {
                    int seg = x < seamX[pl] ? 0 : 1;
                    float t = tone[pl * 2 + seg];
                    float grain = Mathf.PerlinNoise(x * 0.02f + pl * 13.1f, py * 0.35f + seg * 5f) * 0.25f + Mathf.PerlinNoise(x * 0.15f, y * 0.9f) * 0.08f;
                    float edge = (py < 2 || py >= ph - 2 || Mathf.Abs(x - seamX[pl]) < 2) ? 0.45f : 1f;
                    var c = baseCol * t * (0.88f + grain) * edge;
                    c.a = 1; px[y * N + x] = c;
                }
            }
            return SaveTexture(path, px, N);
        }

        static Texture2D RugTexture()
        {
            string path = $"{InteriorTex}/T_Rug.png";
            var existing = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (existing) return existing;
            const int N = 256;
            var px = new Color[N * N];
            var red = new Color(0.62f, 0.2f, 0.16f); var cream = new Color(0.88f, 0.76f, 0.55f); var dark = new Color(0.32f, 0.12f, 0.12f);
            for (int y = 0; y < N; y++)
                for (int x = 0; x < N; x++)
                {
                    float u = x / (float)N - 0.5f, v = y / (float)N - 0.5f;
                    float e = Mathf.Max(Mathf.Abs(u), Mathf.Abs(v));
                    Color c = e > 0.46f ? dark : e > 0.42f ? cream : e > 0.38f ? red : (Mathf.Abs(Mathf.Abs(u) + Mathf.Abs(v) - 0.22f) < 0.025f ? cream : red);
                    c *= 0.92f + Mathf.PerlinNoise(x * 0.3f, y * 0.3f) * 0.12f;
                    c.a = 1; px[y * N + x] = c;
                }
            return SaveTexture(path, px, N);
        }

        static Texture2D SaveTexture(string path, Color[] px, int n)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            var tex = new Texture2D(n, n, TextureFormat.RGB24, true);
            tex.SetPixels(px); tex.Apply();
            File.WriteAllBytes(path, tex.EncodeToPNG());
            AssetDatabase.ImportAsset(path);
            var imp = (TextureImporter)AssetImporter.GetAtPath(path);
            imp.wrapMode = TextureWrapMode.Repeat; imp.sRGBTexture = true; imp.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
    }
}
