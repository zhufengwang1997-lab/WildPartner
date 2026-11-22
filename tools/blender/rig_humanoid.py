"""Rig + animate a humanoid Tripo model in Blender (headless). No Tripo rigging is used.

    blender -b -P tools/blender/rig_humanoid.py -- <in.glb> <out_dir> <name> <height_m> <max_tris> [render_dir]

1. import, join, decimate, scale to <height_m>, origin at the feet, facing -Y (Unity +Z after export)
2. armature from a joint table measured on the mesh (normalised height 0..1, see JOINTS)
3. skin weights: distance-to-bone with region rules (Tripo meshes are thousands of open islands, so
   Blender's heat weighting is not usable); skirt = hips/thigh blend; hat & hair top = head
4. keyframed in-place loops: Idle, Walk, Run (stylised, eased, asymmetric timing)
5. export FBX (mesh + armature + all actions) and render 6-frame contact sheets per action
"""
import bpy, sys, os, math, json
import numpy as np
from mathutils import Vector, Quaternion, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT, NAME, HEIGHT, MAX_TRIS = argv[0], argv[1], argv[2], float(argv[3]), int(argv[4])
RENDER = argv[5] if len(argv) > 5 else None
FPS = 30

# --- joint table, normalised (x = character's left, y = back, z = up; 1.0 = top of hat) ---------
JOINTS = {
    "Hips":  (0, 0.010, 0.470), "Spine": (0, 0.012, 0.545), "Chest": (0, 0.012, 0.620),
    "Neck":  (0, 0.010, 0.700), "Head":  (0, 0.005, 0.752), "HeadTop": (0, 0.0, 0.900),
    "Shoulder": (0.072, 0.012, 0.684), "Elbow": (0.128, 0.018, 0.588), "Wrist": (0.178, 0.006, 0.500), "HandTip": (0.200, -0.002, 0.445),
    "HipJ": (0.030, 0.006, 0.452), "Knee": (0.030, -0.004, 0.245), "Ankle": (0.030, 0.012, 0.072), "Toe": (0.030, -0.052, 0.012),
}

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
bpy.ops.object.select_all(action="DESELECT")
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
body = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o != body: bpy.data.objects.remove(o)
tris_in = sum(len(p.vertices) - 2 for p in body.data.polygons)
if tris_in > MAX_TRIS:
    m = body.modifiers.new("dec", "DECIMATE"); m.ratio = MAX_TRIS / tris_in
    bpy.ops.object.modifier_apply(modifier=m.name)
co = np.array([v.co[:] for v in body.data.vertices])
mn, mx = co.min(0), co.max(0)
H0 = mx[2] - mn[2]
s = HEIGHT / H0
body.data.transform(Matrix.Translation((-(mn[0] + mx[0]) / 2 * 0, 0, -mn[2])))  # keep x/y as authored (joint table measured there)
body.data.transform(Matrix.Scale(s, 4))
body.data.update()
body.name = NAME

def J(k, side=1):
    x, y, z = JOINTS[k]
    return Vector((x * side * HEIGHT, y * HEIGHT, z * HEIGHT))

# --- armature ----------------------------------------------------------------------------------
arm_data = bpy.data.armatures.new(NAME + "_Rig")
rig = bpy.data.objects.new(NAME + "_Rig", arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
eb = arm_data.edit_bones
def bone(name, head, tail, parent=None, connect=False):
    b = eb.new(name); b.head = head; b.tail = tail; b.roll = 0
    if parent: b.parent = eb[parent]; b.use_connect = connect
    return b
bone("Root", Vector((0, 0, 0)), Vector((0, 0, 0.15 * HEIGHT)))
bone("Hips", J("Hips"), J("Spine"), "Root")
bone("Spine", J("Spine"), J("Chest"), "Hips", True)
bone("Chest", J("Chest"), J("Neck"), "Spine", True)
bone("Neck", J("Neck"), J("Head"), "Chest", True)
bone("Head", J("Head"), J("HeadTop"), "Neck", True)
for side, sfx in ((1, "L"), (-1, "R")):
    bone(f"UpperArm.{sfx}", J("Shoulder", side), J("Elbow", side), "Chest")
    bone(f"ForeArm.{sfx}", J("Elbow", side), J("Wrist", side), f"UpperArm.{sfx}", True)
    bone(f"Hand.{sfx}", J("Wrist", side), J("HandTip", side), f"ForeArm.{sfx}", True)
    bone(f"Thigh.{sfx}", J("HipJ", side), J("Knee", side), "Hips")
    bone(f"Shin.{sfx}", J("Knee", side), J("Ankle", side), f"Thigh.{sfx}", True)
    bone(f"Foot.{sfx}", J("Ankle", side), J("Toe", side), f"Shin.{sfx}", True)
bpy.ops.object.mode_set(mode="OBJECT")
DEFORM = [b.name for b in arm_data.bones if b.name != "Root"]
arm_data.bones["Root"].use_deform = False

# --- mesh islands (Tripo meshes are many separate pieces: decide arm/skirt per piece) -------------
_par = list(range(len(body.data.vertices)))
def _f(a):
    while _par[a] != a:
        _par[a] = _par[_par[a]]; a = _par[a]
    return a
for e in body.data.edges:
    a, b = _f(e.vertices[0]), _f(e.vertices[1])
    if a != b: _par[a] = b
ISL = np.array([_f(i) for i in range(len(_par))])

# --- weights ------------------------------------------------------------------------------------
V = np.array([v.co[:] for v in body.data.vertices]) / HEIGHT     # normalised
segs = {b.name: (np.array(b.head_local) / HEIGHT, np.array(b.tail_local) / HEIGHT) for b in arm_data.bones if b.name in DEFORM}
def seg_dist(P, a, b):
    ab = b - a; t = np.clip(((P - a) @ ab) / (ab @ ab), 0, 1)
    return np.linalg.norm(P - (a + t[:, None] * ab), axis=1), t
D = {}
x, y, z = V[:, 0], V[:, 1], V[:, 2]
for n, (a, b) in segs.items():
    d, t = seg_dist(V, a, b)
    if n.startswith(("UpperArm", "ForeArm", "Hand")):
        side = 1 if n.endswith("L") else -1
        d = d * np.where(x * side < 0.085, 6.0, 1.0) * np.where(y > 0.06, 4.0, 1.0)
    if n.startswith(("Thigh", "Shin", "Foot")):
        side = 1 if n.endswith("L") else -1
        d = d * np.where(z > 0.47, 6.0, 1.0) * np.where(x * side < -0.01, 5.0, 1.0)
    if n == "Head": d = d * np.where(z > 0.745, 0.25, 1.0)
    if n in ("Neck",): d = d * np.where(z > 0.77, 5.0, 1.0)
    D[n] = d
names = list(D.keys())
Dm = np.stack([D[n] for n in names], 1)
W = 1.0 / (Dm ** 4 + 1e-12)
# keep the 3 strongest
idx = np.argsort(-W, 1)[:, 3:]
np.put_along_axis(W, idx, 0, 1)
W /= W.sum(1, keepdims=True)
# arms: only vertices inside a capsule around the arm chain (keeps hair and bodice off the arms)
armD = np.min(np.stack([D[n] for n in names if n.startswith(("UpperArm", "ForeArm", "Hand"))], 1), 1)
in_arm = np.zeros(len(V), bool)
for sfx, side in (("L", 1), ("R", -1)):
    a_names = [f"UpperArm.{sfx}", f"ForeArm.{sfx}", f"Hand.{sfx}"]
    raw = np.min(np.stack([seg_dist(V, *segs[n])[0] for n in a_names], 1), 1)
    core = ((raw < 0.045) | ((raw < 0.07) & (z < 0.56) & (x * side > 0.155))) & (x * side > 0.07) & ((y < 0.07) | (z < 0.56))
    # whole pieces: a piece is arm if most of its vertices are in the capsule
    cnt = np.bincount(ISL, minlength=len(V)); hit = np.bincount(ISL, weights=core, minlength=len(V))
    cen = np.stack([np.bincount(ISL, weights=V[:, k], minlength=len(V)) for k in range(3)], 1) / np.maximum(cnt, 1)[:, None]
    craw = np.min(np.stack([seg_dist(cen, *segs[n])[0] for n in a_names], 1), 1)
    small_near = (cnt < 1500) & (craw < 0.07) & (cen[:, 0] * side > 0.09) & (cen[:, 2] > 0.43)
    inside = ((hit[ISL] / np.maximum(cnt[ISL], 1)) > 0.5) | small_near[ISL]
    in_arm |= inside
    shoulder = inside & (z > 0.64)
    for n in a_names:
        W[~inside, names.index(n)] = 0
    i = np.where(inside)[0]
    if len(i):
        sub = np.stack([seg_dist(V[i], *segs[n])[0] for n in a_names], 1)
        w = 1.0 / (sub ** 4 + 1e-12); w /= w.sum(1, keepdims=True)
        W[i] = 0
        for k, n in enumerate(a_names): W[i, names.index(n)] = w[:, k]
        j = np.where(shoulder[i])[0]           # soft shoulder: share with the chest near the joint
        if len(j):
            t = np.clip((z[i][j] - 0.64) / 0.05, 0, 1) * np.clip((0.11 - x[i][j] * side) / 0.04, 0, 1)
            W[i[j]] *= (1 - 0.6 * t)[:, None]
            W[i[j], names.index("Chest")] += 0.6 * t
# legs below the skirt: strictly by side, then by height along the chain (rigid boots, clean knee)
def band(v, lo, hi):  # 0 below lo -> 1 above hi
    return np.clip((v - lo) / (hi - lo), 0, 1)
legR = np.minimum(np.hypot(x - 0.030, y), np.hypot(x + 0.030, y))
legzone = (z < 0.47) & ~in_arm & ((legR < 0.05) | (z < 0.11))
for sfx, side in (("L", 1), ("R", -1)):
    i = np.where(legzone & (x * side >= 0))[0]
    if not len(i): continue
    zz = z[i]
    th = band(zz, 0.225, 0.265)
    ft = 1 - band(zz, 0.055, 0.095)
    sh = (1 - th) * (1 - ft)
    W[i] = 0
    hip_share = band(zz, 0.40, 0.47)
    W[i, names.index(f"Thigh.{sfx}")] = th * (1 - hip_share)
    W[i, names.index("Hips")] = th * hip_share
    W[i, names.index(f"Shin.{sfx}")] = sh
    W[i, names.index(f"Foot.{sfx}")] = ft
# skirt: everything in the skirt band that is not leg skin or arm -> hips + thighs by side
skirt = (z > 0.10) & (z < 0.58) & ~in_arm & ~legzone
if skirt.any():
    i = np.where(skirt)[0]
    W[i] = 0
    low = np.clip((0.55 - z[i]) / 0.25, 0, 1)            # lower hem follows the legs more
    lt = np.clip(0.5 + x[i] / 0.10, 0, 1)
    hips = 1 - 0.6 * low
    W[i, names.index("Hips")] = hips * 0.85
    W[i, names.index("Spine")] = hips * 0.15
    # pieces near the centre line (front/back hem) stay with the hips instead of being torn L/R
    off = np.clip(np.abs(x[i]) / 0.06, 0.0, 1.0)
    leg = (1 - hips) * off
    W[i, names.index("Hips")] += (1 - hips) * (1 - off)
    W[i, names.index("Thigh.L")] = leg * (x[i] > 0)
    W[i, names.index("Thigh.R")] = leg * (x[i] <= 0)
dead = W.sum(1) < 1e-6        # rows whose best bones were all arm bones but sit outside the capsule
if dead.any():                 # -> nearest arm bone (raw distance), never a body bone (that tears)
    arm_names = [n for n in names if n.startswith(("UpperArm", "ForeArm", "Hand"))]
    raw_all = np.stack([seg_dist(V[dead], *segs[n])[0] for n in arm_names], 1)
    best = np.argmin(raw_all, 1)
    rows = np.where(dead)[0]
    for k, n in enumerate(arm_names): W[rows[best == k], names.index(n)] = 1.0
W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
print("WEIGHTS " + json.dumps({"verts": int(len(V)), "arm": int(in_arm.sum()), "refilled": int(dead.sum())}))
for n in names: body.vertex_groups.new(name=n)
vg = [body.vertex_groups[n] for n in names]
for vi in range(len(V)):
    for k in np.nonzero(W[vi] > 0.02)[0]:
        vg[k].add([vi], float(W[vi, k]), "REPLACE")
body.parent = rig
mod = body.modifiers.new("Armature", "ARMATURE"); mod.object = rig

# --- animation helpers ---------------------------------------------------------------------------
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="POSE")
PB = rig.pose.bones
for pb in PB: pb.rotation_mode = "QUATERNION"
REST = {b.name: b.matrix_local.to_3x3() for b in arm_data.bones}
AX = {"x": Vector((1, 0, 0)), "y": Vector((0, 1, 0)), "z": Vector((0, 0, 1))}

def pose(rot, loc=None):
    """rot: {bone: [(world_axis, degrees), ...]} applied in order; loc: {bone: world vector (m)}"""
    for pb in PB:
        pb.rotation_quaternion = Quaternion(); pb.location = Vector()
    for n, rs in rot.items():
        q = Quaternion()
        inv = REST[n].inverted()
        for ax, deg in rs:
            a = AX[ax] if isinstance(ax, str) else Vector(ax)
            q = Quaternion((inv @ a).normalized(), math.radians(deg)) @ q
        PB[n].rotation_quaternion = q
    for n, v in (loc or {}).items():
        PB[n].location = REST[n].inverted() @ Vector(v)

def key(frame):
    for pb in PB:
        pb.keyframe_insert("rotation_quaternion", frame=frame)
        pb.keyframe_insert("location", frame=frame)

def make_action(name, frames, fn):
    act = bpy.data.actions.new(name)
    rig.animation_data_create(); rig.animation_data.action = act
    for f in range(frames + 1):
        p = (f % frames) / frames
        pose(*fn(p)); key(f)
    act.use_fake_user = True
    act.frame_range = (0, frames)
    return act

def ease(x):  # smooth 0..1..0 bump on [0,1]
    return 0.5 - 0.5 * math.cos(2 * math.pi * x)

TAU = 2 * math.pi
def legs(p, swing, knee_swing, knee_load, foot_amp, arm_swing, elbow, arm_in, lean, bob, twist):
    rot, loc = {}, {}
    for sfx, side, ph in (("L", 1, 0.0), ("R", -1, 0.5)):
        q = (p + ph) % 1.0
        s = swing * math.cos(TAU * q)                        # + = leg forward
        # knee: big flex while the leg swings forward (q 0.5..1), small load flex just after contact
        k = knee_swing * max(0.0, math.sin(TAU * (q - 0.5))) ** 1.3 + knee_load * max(0.0, math.sin(TAU * q * 2)) * (q < 0.5)
        ft = foot_amp * (math.cos(TAU * q) * 0.6 - 0.8 * max(0, math.sin(TAU * (q - 0.25))) * (q < 0.6))
        rot[f"Thigh.{sfx}"] = [("x", -s)]
        rot[f"Shin.{sfx}"] = [("x", k)]
        rot[f"Foot.{sfx}"] = [("x", -ft - k * 0.25 + s * 0.2)]
        a = -s / swing * arm_swing if swing else 0
        rot[f"UpperArm.{sfx}"] = [("y", -arm_in * side), ("x", a)]
        rot[f"ForeArm.{sfx}"] = [("x", -(elbow + max(0, -a) * 0.4))]
        rot[f"Hand.{sfx}"] = [("x", -6)]
    hip_yaw = twist * math.cos(TAU * p)
    rot["Hips"] = [("z", hip_yaw), ("y", 2.5 * math.sin(TAU * p)), ("x", lean * 0.4)]
    rot["Spine"] = [("z", -hip_yaw * 0.5), ("x", lean * 0.3)]
    rot["Chest"] = [("z", -hip_yaw * 0.7), ("x", lean * 0.3)]
    rot["Neck"] = [("z", hip_yaw * 0.4), ("x", -lean * 0.4)]
    rot["Head"] = [("x", -lean * 0.3 + 1.5 * math.cos(TAU * 2 * p))]
    loc["Hips"] = Vector((0.006 * HEIGHT * math.sin(TAU * p), 0, -bob * HEIGHT * (0.5 + 0.5 * math.cos(TAU * 2 * p))))
    return rot, loc

def idle(p):
    br = math.sin(TAU * p)                       # breathing
    sw = math.sin(TAU * p + 0.6)                 # slow weight shift
    rot = {
        "Hips": [("y", 1.2 * sw)], "Spine": [("x", 1.0 * br), ("y", -0.6 * sw)], "Chest": [("x", 1.6 * br)],
        "Neck": [("x", -1.0 * br)], "Head": [("y", 2.0 * math.sin(TAU * p + 1.4)), ("z", 3.0 * math.sin(TAU * p * 1 + 2.2))],
        "Thigh.L": [("y", -1.2 * sw)], "Thigh.R": [("y", -1.2 * sw)], "Shin.L": [("x", 2 + 2 * max(0, sw))], "Shin.R": [("x", 2 + 2 * max(0, -sw))],
    }
    for sfx, side in (("L", 1), ("R", -1)):
        rot[f"UpperArm.{sfx}"] = [("y", -6 * side - 1.5 * side * br), ("x", 2 * br)]
        rot[f"ForeArm.{sfx}"] = [("x", -12 - 2 * br)]
        rot[f"Hand.{sfx}"] = [("x", -8), ("z", 4 * side)]
    return rot, {"Hips": Vector((0.004 * HEIGHT * sw, 0, -0.002 * HEIGHT * (1 + br)))}

acts = {
    "Idle": make_action("Idle", 72, idle),
    "Walk": make_action("Walk", 24, lambda p: legs(p, swing=27, knee_swing=48, knee_load=10, foot_amp=12, arm_swing=18,
                                                    elbow=14, arm_in=10, lean=4, bob=0.012, twist=6)),
    "Run":  make_action("Run", 16, lambda p: legs(p, swing=36, knee_swing=80, knee_load=18, foot_amp=14, arm_swing=28,
                                                   elbow=55, arm_in=16, lean=14, bob=0.03, twist=9)),
}
rig.animation_data.action = acts["Idle"]
bpy.ops.object.mode_set(mode="OBJECT")

# --- export ---------------------------------------------------------------------------------------
os.makedirs(OUT, exist_ok=True)
for o in (rig, body): o.select_set(True)
fbx = os.path.join(OUT, NAME + ".fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", path_mode="STRIP", embed_textures=False,
                         mesh_smooth_type="FACE", add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
_src = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Art", "Rigged")  # source stays outside Assets/
os.makedirs(_src, exist_ok=True)
bpy.ops.wm.save_as_mainfile(filepath=os.path.join(_src, NAME + "_rig.blend"))
print("RIG " + json.dumps({"fbx": fbx, "tris": sum(len(p.vertices) - 2 for p in body.data.polygons),
                           "bones": len(DEFORM), "actions": {k: int(a.frame_range[1]) for k, a in acts.items()}}))

# --- contact sheets ---------------------------------------------------------------------------------
if RENDER:
    os.makedirs(RENDER, exist_ok=True)
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "TEXTURE"
    sc.display.shading.show_cavity = False
    sc.render.resolution_x, sc.render.resolution_y = 360, 480
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.86, 0.87, 0.9)
    cd = bpy.data.cameras.new("cam"); cd.type = "ORTHO"; cd.ortho_scale = HEIGHT * 1.25
    cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam
    # ground line
    bpy.ops.mesh.primitive_plane_add(size=HEIGHT * 3, location=(0, 0, 0))
    files = []
    for view, d in (("q", Vector((0.75, -1, 0.18))), ("side", Vector((1, 0, 0.05)))):
        d.normalize()
        cam.location = Vector((0, 0, HEIGHT * 0.5)) + d * HEIGHT * 4
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        for an, act in acts.items():
            rig.animation_data.action = act
            n = int(act.frame_range[1])
            for i in range(6):
                f = round(i * n / 6)
                sc.frame_set(f)
                p = os.path.join(RENDER, f"{an}_{view}_{i}_f{f:02d}.png")
                sc.render.filepath = p
                bpy.ops.render.render(write_still=True)
                files.append(p)
    print("RENDERS " + json.dumps(files))
