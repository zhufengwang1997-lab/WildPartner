"""Rig + animate the Tripo moon rabbit in Blender (headless). No Tripo rigging is used.

    blender -b -P tools/blender/rig_rabbit.py -- <in.glb> <out_dir> <name> <height_m> <max_tris> [render_dir]

1. import, join, decimate; the model stands upright on two feet at an angle -> the facing is measured from the
   tail ball (farthest point from the body axis just above the feet) and the mesh turned to face -Y
2. armature: hips, spine, chest, big rigid head, two 2-bone ears, short arms, 2-bone legs, tail
3. weights by region (head above the collar, ears, arms out to the side, legs below the belly, tail ball)
4. clips (in place): Idle (breathing, ear twitches, head tilts, tail wiggle), Walk = small hop, Run = long
   hop; both hops: anticipation squat -> push-off -> airborne (ears trail, arms up) -> landing squash, and
   the ears overshoot after the landing (follow-through)
5. export FBX + <name>_speeds.json (hop length / hop time, for the blend-tree thresholds)
"""

import bpy, sys, os, math, json
import numpy as np
from mathutils import Vector, Quaternion, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT, NAME, HEIGHT, MAX_TRIS = argv[0], argv[1], argv[2], float(argv[3]), int(argv[4])
RENDER = argv[5] if len(argv) > 5 else None
FPS = 30
TAU = 2 * math.pi

# --- import, orient, scale ---------------------------------------------------------------------
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

def verts():
    a = np.zeros(len(body.data.vertices) * 3); body.data.vertices.foreach_get("co", a); return a.reshape(-1, 3)

co = verts(); mn, mx = co.min(0), co.max(0); H0 = mx[2] - mn[2]
low = co[co[:, 2] < mn[2] + 0.05 * H0]; c0 = low[:, :2].mean(0)
body.data.transform(Matrix.Translation((-c0[0], -c0[1], -mn[2]))); body.data.transform(Matrix.Scale(1 / H0, 4))
co = verts()
m = (co[:, 2] > 0.11) & (co[:, 2] < 0.2)
ctr = co[(co[:, 2] > 0.15) & (co[:, 2] < 0.45)][:, :2].mean(0)
r = np.linalg.norm(co[m][:, :2] - ctr, axis=1); tailp = co[m][np.argmax(r)]
back = (tailp[:2] - ctr) / np.linalg.norm(tailp[:2] - ctr)
ang = math.atan2(back[1], back[0]) - math.atan2(1, 0)               # tail -> +Y, face -> -Y
body.data.transform(Matrix.Rotation(-ang, 4, "Z"))
co = verts()
print("ORIENT " + json.dumps({"turn_deg": round(math.degrees(ang), 1)}))
V = co.copy(); x, y, z = V[:, 0], V[:, 1], V[:, 2]

# joints, normalised (height 1 = ear tips), read off the oriented side / front views
JOINTS = {"Hips": (0, 0.0, 0.15), "Spine": (0, 0.0, 0.25), "Chest": (0, -0.005, 0.33), "Neck": (0, 0.0, 0.40),
          "HeadTop": (0, 0.0, 0.66), "Tail0": (0, 0.11, 0.17), "Tail1": (0, 0.24, 0.16)}
EARS = {"L": ((0.085, 0.03, 0.67), (0.11, 0.07, 0.82), (0.12, 0.10, 0.98)),
        "R": ((-0.095, 0.03, 0.67), (-0.14, 0.06, 0.82), (-0.18, 0.07, 0.97))}
ARMS = {s: ((0.11 * k, -0.02, 0.33), (0.19 * k, -0.025, 0.28), (0.27 * k, -0.03, 0.235)) for s, k in (("L", 1), ("R", -1))}
LEGS = {s: ((0.07 * k, 0.0, 0.15), (0.095 * k, 0.005, 0.045), (0.1 * k, -0.085, 0.01)) for s, k in (("L", 1), ("R", -1))}
S = HEIGHT
body.data.transform(Matrix.Scale(S, 4)); body.data.update(); body.name = NAME
def P(p): return Vector((float(p[0]) * S, float(p[1]) * S, float(p[2]) * S))
def J(k): return P(JOINTS[k])

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
bone("Root", Vector((0, 0, 0)), Vector((0, 0, 0.1 * S)))
bone("Hips", J("Hips"), J("Spine"), "Root")
bone("Spine", J("Spine"), J("Chest"), "Hips", True)
bone("Chest", J("Chest"), J("Neck"), "Spine", True)
bone("Head", J("Neck"), J("HeadTop"), "Chest", True)
for s, (a, b_, c) in EARS.items():
    bone(f"Ear1.{s}", P(a), P(b_), "Head"); bone(f"Ear2.{s}", P(b_), P(c), f"Ear1.{s}", True)
for s, (a, b_, c) in ARMS.items():
    bone(f"Arm.{s}", P(a), P(b_), "Chest"); bone(f"Hand.{s}", P(b_), P(c), f"Arm.{s}", True)
for s, (a, b_, c) in LEGS.items():
    bone(f"Leg.{s}", P(a), P(b_), "Hips"); bone(f"Foot.{s}", P(b_), P(c), f"Leg.{s}", True)
bone("Tail", J("Tail0"), J("Tail1"), "Hips")
bpy.ops.object.mode_set(mode="OBJECT")
arm_data.bones["Root"].use_deform = False
DEFORM = [b.name for b in arm_data.bones if b.name != "Root"]

# --- weights (normalised coordinates) -----------------------------------------------------------
segs = {b.name: (np.array(b.head_local) / S, np.array(b.tail_local) / S) for b in arm_data.bones if b.name in DEFORM}
def seg_dist(Pp, a, b):
    ab = b - a; t = np.clip(((Pp - a) @ ab) / (ab @ ab), 0, 1)
    return np.linalg.norm(Pp - (a + t[:, None] * ab), axis=1)
def band(v, lo, hi): return np.clip((v - lo) / (hi - lo), 0, 1)
names = DEFORM; NI = {n: i for i, n in enumerate(names)}
W = np.zeros((len(V), len(names)))
core = ["Hips", "Spine", "Chest"]
Dc = np.stack([seg_dist(V, *segs[n]) for n in core], 1)
Wc = 1 / (Dc ** 4 + 1e-10); Wc /= Wc.sum(1, keepdims=True)
for k, n in enumerate(core): W[:, NI[n]] = Wc[:, k]
def put(mask_w, groups):
    """mask_w: per-vertex share taken over by `groups` (list of (bone, weight array))."""
    global W
    W *= (1 - mask_w)[:, None]
    tot = sum(g for _, g in groups) + 1e-12
    for n, g in groups: W[:, NI[n]] += mask_w * g / tot
# head above the collar (rigid, incl. the leaf on it)
hz = band(z, 0.38, 0.43)
put(hz, [("Head", np.ones(len(V)))])
# ears
for s in EARS:
    d1 = seg_dist(V, *segs[f"Ear1.{s}"]); d2 = seg_dist(V, *segs[f"Ear2.{s}"])
    near = (np.minimum(d1, d2) < 0.075) & (z > 0.62) & (x * (1 if s == "L" else -1) > 0.015)
    w = near * band(z, 0.66, 0.72)
    put(w, [(f"Ear1.{s}", 1 / (d1 ** 4 + 1e-12)), (f"Ear2.{s}", 1 / (d2 ** 4 + 1e-12))])
# arms: out to the side of the chest
for s, k in (("L", 1), ("R", -1)):
    d1 = seg_dist(V, *segs[f"Arm.{s}"]); d2 = seg_dist(V, *segs[f"Hand.{s}"])
    near = (np.minimum(d1, d2) < 0.07) & (z > 0.17) & (z < 0.38)
    w = near * band(x * k, 0.105, 0.14)
    put(w, [(f"Arm.{s}", 1 / (d1 ** 4 + 1e-12)), (f"Hand.{s}", 1 / (d2 ** 4 + 1e-12))])
# legs: below the round belly, by side
for s, k in (("L", 1), ("R", -1)):
    d1 = seg_dist(V, *segs[f"Leg.{s}"]); d2 = seg_dist(V, *segs[f"Foot.{s}"])
    near = (x * k > 0.015) & (z < 0.15) & (np.minimum(d1, d2) < 0.08)
    w = near * (1 - band(z, 0.075, 0.13))
    put(w, [(f"Leg.{s}", 1 / (d1 ** 4 + 1e-12)), (f"Foot.{s}", 1 / (d2 ** 4 + 1e-12) * (z < 0.06))])
# tail ball
dt = seg_dist(V, *segs["Tail"])
w = ((dt < 0.08) & (y > 0.1) & (z > 0.07) & (z < 0.27)) * band(y, 0.1, 0.13)
put(w, [("Tail", np.ones(len(V)))])
W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
for n in names: body.vertex_groups.new(name=n)
vg = [body.vertex_groups[n] for n in names]
for vi in range(len(V)):
    row = W[vi]; top = np.argsort(-row)[:4]
    for k in top:
        if row[k] > 0.02: vg[k].add([vi], float(row[k]), "REPLACE")
body.parent = rig
mod = body.modifiers.new("Armature", "ARMATURE"); mod.object = rig

# --- animation helpers --------------------------------------------------------------------------- ---------------------------------------------------------------------------
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="POSE")
PB = rig.pose.bones
for pb in PB: pb.rotation_mode = "QUATERNION"
REST = {b.name: b.matrix_local.to_3x3() for b in arm_data.bones}
AX = {"x": Vector((1, 0, 0)), "y": Vector((0, 1, 0)), "z": Vector((0, 0, 1))}
HAS = set(PB.keys())

def pose(rot, loc=None):
    """rot: {bone: [(axis, degrees), ...]} about rest-frame world axes (x side, y back, z up)."""
    for pb in PB: pb.rotation_quaternion = Quaternion(); pb.location = Vector()
    for n, rs in rot.items():
        if n not in HAS: continue
        q = Quaternion(); inv = REST[n].inverted()
        for ax, deg in rs:
            q = Quaternion((inv @ AX[ax]).normalized(), math.radians(deg)) @ q
        PB[n].rotation_quaternion = q
    for n, v in (loc or {}).items():
        PB[n].location = REST[n].inverted() @ Vector(v)

def key(frame):
    for pb in PB:
        pb.keyframe_insert("rotation_quaternion", frame=frame); pb.keyframe_insert("location", frame=frame)

bpy.context.scene.render.fps = FPS
PAWS = ["Foot.L", "Foot.R"]
PAW_REST = {n: (arm_data.bones[n].head_local.z, arm_data.bones[n].tail_local.z) for n in PAWS}

def paw_offset():
    bpy.context.view_layer.update()
    return min(min(PB[n].head.z - PAW_REST[n][0], PB[n].tail.z - PAW_REST[n][1]) for n in PAWS)

def mesh_offset():
    """Lowest point of the deformed mesh (0 = touching the ground)."""
    bpy.context.view_layer.update()
    dg = bpy.context.evaluated_depsgraph_get(); ev = body.evaluated_get(dg); me = ev.to_mesh()
    a = np.zeros(len(me.vertices) * 3); me.vertices.foreach_get("co", a); ev.to_mesh_clear()
    return float(a.reshape(-1, 3)[:, 2].min()) - 0.003 * S

def make_action(name, frames, fn, ground="paws", lift=None, loop=True, smooth=2):
    act = bpy.data.actions.new(name)
    rig.animation_data_create(); rig.animation_data.action = act
    n = frames if loop else frames + 1
    poses = [fn(f / frames) for f in range(n)]
    off = [0.0] * n
    if ground:
        for f, (r, l) in enumerate(poses):
            pose(r, l); off[f] = paw_offset() if ground == "paws" else mesh_offset()
    h = [-off[f] + (lift(f / frames) if lift else 0.0) for f in range(n)]
    idx = (lambda i: i % n) if loop else (lambda i: min(max(i, 0), n - 1))
    for _ in range(smooth if ground else 0):
        h = [(h[idx(f - 2)] + 2 * h[idx(f - 1)] + 3 * h[f] + 2 * h[idx(f + 1)] + h[idx(f + 2)]) / 9 for f in range(n)]
    for f in range(frames + 1):
        r, l = poses[f % n]
        l = dict(l); hv = Vector(l.get("Hips", Vector())); hv.z += h[f % n]; l["Hips"] = hv
        pose(r, l); key(f)
    act.use_fake_user = True; act.frame_range = (0, frames)
    return act

def curve(pts, q):
    """Periodic Catmull-Rom through (phase, value) keys on [0,1)."""
    q %= 1.0; n = len(pts)
    for i in range(n):
        x0, _ = pts[i]; x1, _ = pts[(i + 1) % n]
        span = (x1 - x0) % 1.0 or 1.0
        t = ((q - x0) % 1.0) / span
        if t <= 1.0:
            p0, p1, p2, p3 = (pts[(i + k) % n][1] for k in (-1, 0, 1, 2))
            return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)
    return pts[0][1]

def track(pts, t):
    """Non-periodic smooth keys on [0,1] (one-shot clips)."""
    t = min(max(t, 0.0), 1.0)
    for i in range(len(pts) - 1):
        (t0, v0), (t1, v1) = pts[i], pts[i + 1]
        if t <= t1:
            u = (t - t0) / max(t1 - t0, 1e-9); u = u * u * (3 - 2 * u)
            return v0 + (v1 - v0) * u
    return pts[-1][1]

def merge(*ds):
    out = {}
    for d in ds:
        for k, v in d.items(): out.setdefault(k, []).extend(v)
    return out

def lerp_pose(a, b, t):
    """Blend two rot dicts (per-axis angle lerp; fine for these small, same-axis poses)."""
    out = {}
    for n in set(a) | set(b):
        ax = {}
        for k, v in a.get(n, []): ax[k] = ax.get(k, 0) + v * (1 - t)
        for k, v in b.get(n, []): ax[k] = ax.get(k, 0) + v * t
        out[n] = [(k, v) for k, v in ax.items()]
    return out

def flick(p, at, w=0.02):
    return max(0.0, 1 - abs(((p - at + 0.5) % 1.0) - 0.5) / w)

def ears(lx, rx, ly=0.0, ry=0.0, tip=0.6):
    """Ear pose: x tips the ear back (+) / forward (-), y swings it sideways; the tip bone follows by `tip`."""
    return {"Ear1.L": [("x", lx), ("y", ly)], "Ear2.L": [("x", lx * tip)], "Ear1.R": [("x", rx), ("y", ry)], "Ear2.R": [("x", rx * tip)]}

# ----------------------------------------------------------------------------------------- Idle
def idle(p):
    br = math.sin(TAU * 4 * p)
    tilt = curve([(0.0, 0), (0.3, 0), (0.36, 12), (0.52, 12), (0.6, 0)], p)
    look = curve([(0.0, 0), (0.62, 0), (0.68, -14), (0.82, -14), (0.9, 0)], p)
    twL = flick(p, 0.2) * 22 + flick(p, 0.235, 0.015) * 14; twR = flick(p, 0.75) * 20
    sniff = sum(flick(p, c, 0.012) for c in (0.08, 0.11, 0.14, 0.88, 0.91))
    r = {"Spine": [("x", -1.5 * br)], "Chest": [("x", 1.2 * br)],
         "Head": [("y", tilt), ("z", look), ("x", 3 * sniff - 1.0 * br)],
         "Arm.L": [("x", 2 * br)], "Arm.R": [("x", 2 * br)], "Hand.L": [("x", -4 * sniff)], "Hand.R": [("x", -4 * sniff)],
         "Tail": [("z", 18 * (flick(p, 0.45, 0.03) - flick(p, 0.5, 0.03)))]}
    r.update(ears(-4 + twL * 0.3 + 2 * br, -4 + twR * 0.3 + 2 * br, ly=twL, ry=-twR))
    return r, {}

# ----------------------------------------------------------------------------------------- hops
def hop_pose(p, big):
    """p 0..1: 0-0.18 squat (anticipation), 0.18-0.28 push-off, 0.28-0.78 airborne, 0.78-0.9 landing squash,
    0.9-1 recover. Ears lag the body (drag back on the way up, flop forward after landing)."""
    k = 1.6 if big else 1.0
    squat = curve([(0.0, 0.45), (0.12, 1.0), (0.24, 0.1), (0.3, -0.25), (0.55, 0.15), (0.78, 0.35), (0.86, 1.0), (0.95, 0.6)], p)
    sq = max(squat, 0); ext = max(-squat, 0)
    lean = curve([(0.0, 6), (0.14, 14), (0.26, -6), (0.5, -2), (0.76, 8), (0.88, 12), (0.96, 8)], p) * k
    earx = curve([(0.0, -5), (0.2, -10), (0.32, 18), (0.55, 20), (0.78, 6), (0.88, -22), (0.96, -9)], p) * (1.15 if big else 1.0)
    arm = curve([(0.0, 10), (0.16, 25), (0.3, -45), (0.6, -30), (0.82, 20), (0.92, 15)], p) * (1.3 if big else 1.0)
    r = {"Hips": [("x", lean * 0.4)], "Spine": [("x", lean * 0.35)], "Chest": [("x", lean * 0.25)],
         "Head": [("x", -lean * 0.6 + 6 * sq)],
         "Leg.L": [("x", -40 * sq + 25 * ext)], "Leg.R": [("x", -40 * sq + 25 * ext)],
         "Foot.L": [("x", 40 * sq - 30 * ext)], "Foot.R": [("x", 40 * sq - 30 * ext)],
         "Arm.L": [("x", arm), ("y", -0.3 * arm)], "Arm.R": [("x", arm), ("y", 0.3 * arm)],
         "Hand.L": [("x", 0.4 * arm)], "Hand.R": [("x", 0.4 * arm)],
         "Tail": [("x", -20 * ext + 10 * sq)]}
    r.update(ears(earx, earx * 0.9 + 3, ly=4 * math.sin(TAU * p), ry=-4 * math.sin(TAU * p), tip=0.5))
    return r, {}

def hop_lift(big):
    h = (0.32 if big else 0.18) * S
    return lambda p: h * math.sin(math.pi * min(max((p - 0.27) / 0.52, 0.0), 1.0)) ** 0.9

HOP = {"Walk": (14, 0.34), "Run": (13, 0.78)}     # frames per hop, hop length (m)
acts = {
    "Idle": make_action("Idle", 150, idle),
    "Walk": make_action("Walk", HOP["Walk"][0], lambda p: hop_pose(p, False), lift=hop_lift(False)),
    "Run": make_action("Run", HOP["Run"][0], lambda p: hop_pose(p, True), lift=hop_lift(True)),
}
SPEEDS = {k: round(L / (f / FPS), 3) for k, (f, L) in HOP.items()}
print("SPEEDS " + json.dumps(SPEEDS))
rig.animation_data.action = acts["Idle"]
bpy.ops.object.mode_set(mode="OBJECT")

# --- export ---------------------------------------------------------------------------------------
os.makedirs(OUT, exist_ok=True)
for o in (rig, body): o.select_set(True)
fbx = os.path.join(OUT, NAME + ".fbx")
with open(os.path.join(OUT, NAME + "_speeds.json"), "w") as fh: json.dump(SPEEDS, fh)
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"ARMATURE", "MESH"},
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", path_mode="STRIP", embed_textures=False,
                         mesh_smooth_type="FACE", add_leaf_bones=False, primary_bone_axis="Y", secondary_bone_axis="X",
                         bake_anim=True, bake_anim_use_all_actions=True, bake_anim_use_nla_strips=False,
                         bake_anim_force_startend_keying=True, bake_anim_simplify_factor=0.0)
_src = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "..", "Art", "Rigged")
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
    sc.render.resolution_x, sc.render.resolution_y = 360, 300
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.86, 0.87, 0.9)
    cd = bpy.data.cameras.new("cam"); cd.type = "ORTHO"; cd.ortho_scale = HEIGHT * 1.7
    cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam
    bpy.ops.mesh.primitive_plane_add(size=HEIGHT * 4, location=(0, 0, 0))
    files = []
    for view, d in (("q", Vector((0.8, -1, 0.3))), ("side", Vector((1, 0, 0.05)))):
        d.normalize()
        cam.location = Vector((0, 0, HEIGHT * 0.45)) + d * HEIGHT * 5
        cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
        for an, act in acts.items():
            rig.animation_data.action = act
            n = int(act.frame_range[1])
            for i in range(6):
                f = round(i * n / 6) if True else round(i * n / 5)
                sc.frame_set(f)
                p = os.path.join(RENDER, f"{an}_{view}_{i}_f{f:03d}.png")
                sc.render.filepath = p
                bpy.ops.render.render(write_still=True)
                files.append(p)
    print("RENDERS " + json.dumps(len(files)))
