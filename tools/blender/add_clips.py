"""Add extra clips to the rigged witch and re-export Witch.fbx (all other clips untouched):
  WakeUp (试玩反馈 1123c-A5), Swim (B3, breaststroke loop), Climb (B4, hand-over-hand loop), ClimbOver (B4, one-shot).

    blender -b Art/Rigged/Witch_rig.blend -P tools/blender/add_clips.py -- <out_dir> [render_dir]

WakeUp:

Opens the saved rig (all other clips untouched), keys WakeUp in the same conventions as rig_humanoid.py
(bone axes in the character's rest frame; Hips location in armature space; she faces -Y) and exports with the
same FBX settings. The clip, 5 s at 30 fps, ends standing at the origin facing -Y (Unity: the player's spot):
  0.00-0.14  lying on her back on the mattress, body along X, head towards -X (her right), breathing
  0.14-0.40  sits up, legs straight along the bed, hands pushing on the mattress
  0.40-0.62  turns on the mattress and swings her legs over the edge, knees bending down to the floor
  0.62-0.70  sits on the edge, feet on the floor
  0.70-0.92  leans forward and stands up, stepping off the bed
  0.92-1.00  settles into the idle stance
Bed (game): mattress top ~0.50 m, near edge 0.35 m behind her standing spot, centre 0.9 m behind.
"""
import bpy, math, os, sys
from mathutils import Vector, Quaternion

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = argv[0]
RENDER = argv[1] if len(argv) > 1 else None
FPS, FRAMES = 30, 150
BED_TOP, BED_EDGE, BED_MID = 0.50, 0.35, 0.90

rig = bpy.data.objects["Witch_Rig"]; body = bpy.data.objects["Witch"]
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="POSE")
PB = rig.pose.bones
REST = {b.name: b.matrix_local.to_3x3() for b in rig.data.bones}
AX = {"x": Vector((1, 0, 0)), "y": Vector((0, 1, 0)), "z": Vector((0, 0, 1))}

def pose(rot, loc):
    for pb in PB:
        pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = Quaternion(); pb.location = Vector()
    for n, rs in rot.items():
        q = Quaternion(); inv = REST[n].inverted()
        for ax, deg in rs:
            q = Quaternion((inv @ AX[ax]).normalized(), math.radians(deg)) @ q
        PB[n].rotation_quaternion = q
    for n, v in loc.items():
        PB[n].location = REST[n].inverted() @ Vector(v)

def track(pts, t):
    """Smooth (smoothstep between keys) piecewise curve through (t, value) keys; clamped ends."""
    if t <= pts[0][0]: return pts[0][1]
    if t >= pts[-1][0]: return pts[-1][1]
    for (x1, v1), (x2, v2) in zip(pts, pts[1:]):
        if x1 <= t <= x2:
            u = (t - x1) / (x2 - x1); u = u * u * (3 - 2 * u)
            return v1 + (v2 - v1) * u

# timeline keys
T_LIE, T_UP, T_SWING, T_EDGE, T_STAND, T_END = 0.14, 0.40, 0.62, 0.70, 0.92, 1.0

def wake(t):
    br = math.sin(t * FRAMES / FPS * 2 * math.pi / 4.0)          # slow breathing
    lean = track([(0, -90), (T_LIE, -90), (T_UP - 0.04, 6), (T_UP, 0), (T_EDGE, 2), (T_EDGE + 0.06, 30), (T_STAND - 0.06, 8), (T_STAND, 0)], t)
    yaw = track([(0, 90), (T_UP, 90), (T_SWING, 0)], t)
    hipflex = track([(0, 0), (T_LIE, 0), (T_UP, 90), (T_EDGE, 86), (T_EDGE + 0.06, 100), (T_STAND - 0.04, 6), (T_STAND, 0)], t)
    # legs: lying straight (thigh 0 in the body frame), long-sit 90, on the edge 86 + knees bent down
    thigh = -(hipflex + lean) if t < T_UP else -track([(T_UP, 90), (T_EDGE, 86), (T_EDGE + 0.06, 70), (T_STAND - 0.04, 6), (T_STAND, 0)], t) - lean * 0.0
    if t >= T_UP:
        thigh = -track([(T_UP, 90), (T_EDGE, 84), (T_EDGE + 0.06, 72), (T_STAND - 0.05, 8), (T_STAND, 0)], t) + 0 * lean
    knee = track([(0, 2), (T_UP, 4), (T_UP + 0.05, 30), (T_SWING, 86), (T_EDGE, 88), (T_EDGE + 0.06, 92), (T_STAND - 0.05, 10), (T_STAND, 2)], t)
    ankle = track([(0, -20), (T_UP, -10), (T_SWING, 6), (T_EDGE + 0.06, 18), (T_STAND, 0)], t)
    spine = track([(0, 0), (T_LIE, 0), (T_UP - 0.08, 18), (T_UP, 4), (T_EDGE + 0.06, 16), (T_STAND, 0)], t)
    head = track([(0, -6), (T_LIE, -6), (T_UP - 0.08, 10), (T_UP, -4), (T_SWING, 0), (T_EDGE + 0.04, -10), (T_STAND, 0)], t)
    headturn = track([(0, 0), (T_LIE * 0.6, 0), (T_LIE, 22), (T_UP - 0.1, 10), (T_UP, -6), (T_SWING, 0)], t)
    # arms: by her sides lying, pushing on the mattress while sitting up / standing, idle at the end
    push = max(track([(T_LIE, 0), (T_LIE + 0.08, 1), (T_UP, 0.3), (T_SWING, 0)], t), track([(T_EDGE - 0.04, 0), (T_EDGE + 0.04, 1), (T_STAND - 0.04, 0)], t))
    rot = {
        "Hips": [("x", lean), ("z", yaw)],
        "Spine": [("x", spine * 0.6 + 0.6 * br)], "Chest": [("x", spine * 0.4 + 1.0 * br)],
        "Neck": [("x", head * 0.4)], "Head": [("x", head * 0.6), ("z", headturn)],
    }
    for sfx, side in (("L", 1), ("R", -1)):
        rot[f"Thigh.{sfx}"] = [("x", thigh), ("y", 3 * side), ("z", 4 * side)]
        rot[f"Shin.{sfx}"] = [("x", knee)]
        rot[f"Foot.{sfx}"] = [("x", -ankle)]
        rot[f"UpperArm.{sfx}"] = [("y", (21 - 6 * push) * side), ("x", 3 + 34 * push), ("z", (4 + 8 * push) * side)]
        rot[f"ForeArm.{sfx}"] = [("x", -16 + 10 * push)]
        rot[f"Hand.{sfx}"] = [("x", -10 - 40 * push), ("z", 8 * side)]
    # pelvis path (armature space): on the bed's centre line -> its edge -> up and forward to the standing spot
    y = track([(0, BED_MID), (T_LIE, BED_MID), (T_UP, BED_MID - 0.05), (T_SWING, BED_EDGE + 0.12), (T_EDGE, BED_EDGE + 0.1), (T_EDGE + 0.08, BED_EDGE - 0.02), (T_STAND, 0)], t)
    x = track([(0, 0.0), (T_UP, 0.05), (T_SWING, 0.0)], t)
    z = track([(0, BED_TOP + 0.13), (T_LIE, BED_TOP + 0.13), (T_UP, BED_TOP + 0.11), (T_EDGE, BED_TOP + 0.11), (T_EDGE + 0.06, BED_TOP + 0.13), (T_STAND - 0.04, 0.79), (T_STAND, 0.775)], t)
    z += 0.004 * br * (1 if t < T_UP else 0)
    rot["Hips"] = rot["Hips"]
    return rot, {"Hips": Vector((x, y, z - 0.775))}

TAU = 2 * math.pi

def swim(p):
    """Breaststroke, 1.2 s loop, body prone just under the surface (the game holds her feet ~1 m under the water
    line). Phases: glide (arms forward, legs together) -> pull (arms sweep out and back) -> recover (hands under the
    chest, knees drawn up) -> kick (legs whip round and together while the arms shoot forward)."""
    pull = track([(0, 0), (0.15, 0), (0.45, 1), (0.6, 0.7), (0.75, 0), (1, 0)], p)       # arms out/back
    tuck = track([(0, 0), (0.4, 0), (0.6, 1), (0.72, 0.6), (0.85, 0), (1, 0)], p)       # hands to chest
    kick = track([(0, 0.1), (0.45, 0.2), (0.62, 1), (0.8, 0.15), (1, 0.1)], p)          # knees up and out
    bob = math.sin(TAU * p - 1.0)
    rot = {"Hips": [("x", 74 + 4 * bob)], "Spine": [("x", -6 - 4 * pull)], "Chest": [("x", -8 - 6 * pull)],
           "Neck": [("x", -28 - 6 * pull)], "Head": [("x", -34 - 6 * pull)]}
    for sfx, side in (("L", 1), ("R", -1)):
        rot[f"UpperArm.{sfx}"] = [("y", (-150 + 70 * pull + 40 * tuck) * side), ("x", -20 + 30 * tuck), ("z", (10 * pull) * side)]
        rot[f"ForeArm.{sfx}"] = [("x", -8 - 70 * tuck - 20 * pull)]
        rot[f"Hand.{sfx}"] = [("x", -10 + 20 * pull), ("z", 10 * side)]
        rot[f"Thigh.{sfx}"] = [("x", -(8 + 55 * kick)), ("z", 22 * kick * side)]
        rot[f"Shin.{sfx}"] = [("x", 10 + 95 * kick)]
        rot[f"Foot.{sfx}"] = [("x", 40 - 50 * kick), ("z", -20 * kick * side)]
    return rot, {"Hips": Vector((0, 0.05, 0.12 + 0.03 * bob))}

def climb(p):
    """Hand-over-hand climbing, 1.4 s loop (two moves): facing the wall (-Y), one hand reaches high while the other
    pulls down to the shoulder, the opposite knee steps up. The game moves her up the wall; the clip moves the limbs."""
    poses = {}
    rot = {"Hips": [("x", 8)], "Spine": [("x", 4)], "Chest": [("x", 2)], "Neck": [("x", -10)], "Head": [("x", -14 + 4 * math.sin(TAU * 2 * p))]}
    for sfx, side, ph in (("L", 1, 0.0), ("R", -1, 0.5)):
        reach = 0.5 + 0.5 * math.cos(TAU * (p - ph))        # 1 = hand high
        step = 0.5 + 0.5 * math.cos(TAU * (p - ph - 0.5))   # opposite knee up
        rot[f"UpperArm.{sfx}"] = [("y", (-125 - 45 * reach) * side), ("x", 24 + 8 * reach), ("z", 6 * side)]
        rot[f"ForeArm.{sfx}"] = [("x", -10 - 95 * (1 - reach))]
        rot[f"Hand.{sfx}"] = [("x", -25), ("z", 8 * side)]
        rot[f"Thigh.{sfx}"] = [("x", -(22 + 48 * step)), ("z", 10 * side)]
        rot[f"Shin.{sfx}"] = [("x", 30 + 62 * step)]
        rot[f"Foot.{sfx}"] = [("x", -10 - 10 * step)]
    hips_up = 0.04 * math.sin(TAU * 2 * p)
    return rot, {"Hips": Vector((0, 0.16, hips_up - 0.08))}

def climb_over(t):
    """0.9 s: from hanging at the ledge (root already on the top, body 1 m lower and 0.45 m back) -> elbows lock,
    chest over the edge, a knee up onto it -> standing."""
    up = track([(0, 0), (0.35, 0.45), (0.65, 0.85), (1, 1)], t)
    lean = track([(0, 10), (0.3, 30), (0.55, 45), (0.8, 15), (1, 0)], t)
    knee = track([(0, 0.2), (0.45, 0.3), (0.65, 1), (0.85, 0.4), (1, 0)], t)
    arm = track([(0, 1), (0.35, 0.6), (0.6, 0.15), (1, 0)], t)
    rot = {"Hips": [("x", lean * 0.6)], "Spine": [("x", lean * 0.3)], "Chest": [("x", lean * 0.2)], "Neck": [("x", -8 * (1 - t))], "Head": [("x", -10 * (1 - t))]}
    for sfx, side, lag in (("L", 1, 0.0), ("R", -1, 0.08)):
        k = track([(0, 0.2), (0.45 + lag, 0.3), (0.65 + lag, 1), (0.88, 0.3), (1, 0)], t)
        rot[f"UpperArm.{sfx}"] = [("y", (21 - 130 * arm) * side), ("x", -60 * arm), ("z", 4 * side)]
        rot[f"ForeArm.{sfx}"] = [("x", -16 - 30 * arm)]
        rot[f"Hand.{sfx}"] = [("x", -10 + 50 * arm), ("z", 8 * side)]
        rot[f"Thigh.{sfx}"] = [("x", -(10 + 85 * k))]
        rot[f"Shin.{sfx}"] = [("x", 10 + 100 * k)]
        rot[f"Foot.{sfx}"] = [("x", -10 * k)]
    return rot, {"Hips": Vector((0, 0.45 * (1 - up), -1.0 * (1 - up)))}

def bake(name, frames, fn, loop):
    if name in bpy.data.actions: bpy.data.actions.remove(bpy.data.actions[name])
    act = bpy.data.actions.new(name)
    rig.animation_data_create(); rig.animation_data.action = act
    for f in range(frames + 1):
        r, l = fn((f % frames) / frames if loop else f / frames)
        pose(r, l)
        for pb in PB:
            pb.keyframe_insert("rotation_quaternion", frame=f); pb.keyframe_insert("location", frame=f)
    act.use_fake_user = True
    act.frame_range = (0, frames)
    return act

act = bake("WakeUp", FRAMES, wake, False)
extra = {"Swim": bake("Swim", 36, swim, True), "Climb": bake("Climb", 42, climb, True), "ClimbOver": bake("ClimbOver", 27, climb_over, False)}
rig.animation_data.action = bpy.data.actions["Idle"]
bpy.ops.object.mode_set(mode="OBJECT")

os.makedirs(OUT, exist_ok=True)
for o in bpy.context.scene.objects: o.select_set(False)
for o in (rig, body): o.select_set(True)
bpy.context.view_layer.objects.active = rig
bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, "Witch.fbx"), use_selection=True, object_types={"ARMATURE", "MESH"},
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", path_mode="STRIP", embed_textures=False,
                         mesh_smooth_type="FACE", add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
bpy.ops.wm.save_mainfile()
print("WAKEUP exported", [a.name for a in bpy.data.actions])

if RENDER:
    os.makedirs(RENDER, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "TEXTURE"
    sc.render.resolution_x, sc.render.resolution_y = 400, 360
    sc.world = bpy.data.worlds.new("w") if not sc.world else sc.world
    sc.world.color = (0.86, 0.87, 0.9)
    # stand-in bed: mattress box behind her
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0.0, (BED_EDGE + 1.45) / 2, BED_TOP / 2))
    bed = bpy.context.active_object; bed.scale = (2.0, 1.45 - BED_EDGE, BED_TOP)
    bpy.ops.mesh.primitive_plane_add(size=6, location=(0, 0, 0))
    cd = bpy.data.cameras.new("cam"); cd.type = "ORTHO"; cd.ortho_scale = 2.6
    cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam
    rig.animation_data.action = act
    for view, d in (("front", Vector((0.5, -1, 0.35))), ("side", Vector((1, 0.15, 0.25)))):
        d.normalize()
        cam.location = Vector((0, 0.5, 0.75)) + d * 6
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        for i, t in enumerate((0.0, 0.25, 0.4, 0.52, 0.66, 0.78, 0.86, 1.0)):
            sc.frame_set(round(t * FRAMES))
            sc.render.filepath = os.path.join(RENDER, f"wake_{view}_{i}.png")
            bpy.ops.render.render(write_still=True)
    for an, a2 in extra.items():
        rig.animation_data.action = a2
        bed.hide_render = True
        n = int(a2.frame_range[1])
        for view, d in (("front", Vector((0.5, -1, 0.35))), ("side", Vector((1, 0.15, 0.25)))):
            d.normalize()
            cam.location = Vector((0, 0, 0.8)) + d * 6
            cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
            for i in range(6):
                sc.frame_set(round(i * n / 6))
                sc.render.filepath = os.path.join(RENDER, f"{an}_{view}_{i}.png")
                bpy.ops.render.render(write_still=True)
    print("WAKEUP rendered")
