"""Add the one-shot "WakeUp" clip to the rigged witch (试玩反馈 1123c-A5) and re-export Witch.fbx.

    blender -b Art/Rigged/Witch_rig.blend -P tools/blender/add_wakeup.py -- <out_dir> [render_dir]

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

if "WakeUp" in bpy.data.actions: bpy.data.actions.remove(bpy.data.actions["WakeUp"])
act = bpy.data.actions.new("WakeUp")
rig.animation_data_create(); rig.animation_data.action = act
for f in range(FRAMES + 1):
    r, l = wake(f / FRAMES)
    pose(r, l)
    for pb in PB:
        pb.keyframe_insert("rotation_quaternion", frame=f); pb.keyframe_insert("location", frame=f)
act.use_fake_user = True
act.frame_range = (0, FRAMES)
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
    print("WAKEUP rendered")
