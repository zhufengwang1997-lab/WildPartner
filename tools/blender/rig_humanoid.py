"""Rig + animate a humanoid Tripo model in Blender (headless). No Tripo rigging is used.

    blender -b -P tools/blender/rig_humanoid.py -- <in.glb> <out_dir> <name> <height_m> <max_tris> [render_dir]

1. import, join, decimate, scale to <height_m>, origin at the feet, facing -Y (Unity +Z after export)
2. armature from a joint table measured on the mesh (normalised height 0..1, see JOINTS)
3. skin weights: distance-to-bone with region rules (Tripo meshes are thousands of open islands, so
   Blender's heat weighting is not usable); skirt = hips/thigh blend; hat & hair top = head
4. keyframed in-place loops: Idle, Walk, Run from smooth periodic gait curves (mocap-like shapes);
   pelvis height is solved per frame so the stance foot stays on the ground
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
# secondary bones (keyframed follow-through): two 3-bone hair chains from the back of the head,
# four skirt panels (front / back / left / right) hanging from the hips
HAIR_PTS = [(0.735, 0.085), (0.635, 0.115), (0.545, 0.125), (0.455, 0.115)]   # (z, y) along the chain
for side, sfx in ((1, "L"), (-1, "R")):
    prev = "Head"
    for k, nm in enumerate(("HairA", "HairB", "HairC")):
        (z0, y0), (z1, y1) = HAIR_PTS[k], HAIR_PTS[k + 1]
        bone(f"{nm}.{sfx}", Vector((0.05 * side, y0, z0)) * HEIGHT, Vector((0.05 * side, y1, z1)) * HEIGHT, prev, k > 0)
        prev = f"{nm}.{sfx}"
SKIRT = {"Skirt.F": ((0, -0.09, 0.47), (0, -0.13, 0.30)), "Skirt.B": ((0, 0.09, 0.47), (0, 0.15, 0.22)),
         "Skirt.L": ((0.11, 0, 0.47), (0.16, 0, 0.30)), "Skirt.R": ((-0.11, 0, 0.47), (-0.16, 0, 0.30))}
for n, (h, t) in SKIRT.items():
    bone(n, Vector(h) * HEIGHT, Vector(t) * HEIGHT, "Hips")
bpy.ops.object.mode_set(mode="OBJECT")
SEC = [b.name for b in arm_data.bones if b.name.startswith(("Hair", "Skirt"))]
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
SEGS_SEC = {n: segs.pop(n) for n in SEC}   # secondary bones get weights from region rules below, not by distance
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
# hair: pieces hanging behind the back (centroid behind the body, between the nape and the waist)
W = np.concatenate([W, np.zeros((len(V), len(SEC)))], 1)
names = names + SEC
cnt = np.bincount(ISL, minlength=len(V))
cen = np.stack([np.bincount(ISL, weights=V[:, k], minlength=len(V)) for k in range(3)], 1) / np.maximum(cnt, 1)[:, None]
hair_isl = (cen[:, 1] > 0.065) & (cen[:, 2] > 0.51) & (cen[:, 2] < 0.75)   # > 0.51 keeps the back peplum shards on the skirt
hair = hair_isl[ISL] & (z > 0.43) & (z < 0.76) & (y > 0.02) & ~in_arm & ~legzone
if hair.any():
    i = np.where(hair)[0]
    W[i] = 0
    share = band(0.745 - z[i], 0.0, 0.07)                 # top of the hair stays on the head
    lt = np.clip(0.5 + x[i] / 0.05, 0, 1)                 # L/R chains blend across the parting
    W[i, names.index("Head")] = 1 - share
    for sfx, wside in (("L", lt), ("R", 1 - lt)):
        ch = [f"{nm}.{sfx}" for nm in ("HairA", "HairB", "HairC")]
        dd = np.stack([seg_dist(V[i], *SEGS_SEC[n])[0] for n in ch], 1)
        ww = 1.0 / (dd ** 4 + 1e-12); ww /= ww.sum(1, keepdims=True)
        for k, n in enumerate(ch): W[i, names.index(n)] = share * wside * ww[:, k]
# skirt: everything in the skirt band that is not leg skin, arm or hair -> hips + thighs by side
skirt = (z > 0.10) & (z < 0.58) & ~in_arm & ~legzone & ~hair
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
    # part of the hips share moves to the four skirt panels (angular lobes), more towards the hem
    ang = np.arctan2(x[i], -y[i])                          # 0 front, +90 left, 180 back
    lobes = np.stack([np.maximum(0, np.cos(ang - a)) ** 2 for a in (0, np.pi, np.pi / 2, -np.pi / 2)], 1)
    lobes /= np.maximum(lobes.sum(1, keepdims=True), 1e-9)
    f = band(0.50 - z[i], 0.0, 0.14) * 0.85
    hs = W[i, names.index("Hips")] + W[i, names.index("Spine")]
    W[i, names.index("Hips")] *= (1 - f); W[i, names.index("Spine")] *= (1 - f)
    for k, n in enumerate(("Skirt.F", "Skirt.B", "Skirt.L", "Skirt.R")): W[i, names.index(n)] = hs * f * lobes[:, k]
dead = W.sum(1) < 1e-6        # rows whose best bones were all arm bones but sit outside the capsule
if dead.any():                 # -> nearest arm bone (raw distance), never a body bone (that tears)
    arm_names = [n for n in names if n.startswith(("UpperArm", "ForeArm", "Hand"))]
    raw_all = np.stack([seg_dist(V[dead], *segs[n])[0] for n in arm_names], 1)
    best = np.argmin(raw_all, 1)
    rows = np.where(dead)[0]
    for k, n in enumerate(arm_names): W[rows[best == k], names.index(n)] = 1.0
W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
print("WEIGHTS " + json.dumps({"verts": int(len(V)), "arm": int(in_arm.sum()), "hair": int(hair.sum()), "refilled": int(dead.sum())}))
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

bpy.context.scene.render.fps = FPS
FOOT_REST = {sfx: (arm_data.bones[f"Foot.{sfx}"].head_local.z, arm_data.bones[f"Foot.{sfx}"].tail_local.z) for sfx in ("L", "R")}

def ground_offset():
    """How far the lowest heel/toe is below (+) or above (-) its rest height, after the current pose."""
    bpy.context.view_layer.update()
    d = []
    for sfx in ("L", "R"):
        pb = PB[f"Foot.{sfx}"]; h0, t0 = FOOT_REST[sfx]
        d += [pb.head.z - h0, pb.tail.z - t0]
    return min(d)

def make_action(name, frames, fn, plant=True, lift=None, loop=True):
    """fn(p) -> (rot, loc) for phase p. loop=True: p in [0,1), last key = first (seamless cycle);
    loop=False: one-shot clip, p runs 0..1 inclusive. With plant=True the pelvis height is solved per
    frame so the lowest foot touches the ground (the up/down bob then comes from the legs, as in mocap),
    then low-pass filtered; lift(p) adds height on top (run flight phase)."""
    act = bpy.data.actions.new(name)
    rig.animation_data_create(); rig.animation_data.action = act
    n = frames if loop else frames + 1
    poses = [fn(f / frames) for f in range(n)]
    off = [0.0] * n
    if plant:
        for f, (r, l) in enumerate(poses):
            pose(r, l); off[f] = ground_offset()
    pw = plant if callable(plant) else (lambda q: 1.0)
    h = [-off[f] * pw(f / frames) + (lift(f / frames) if lift else 0.0) for f in range(n)]
    idx = (lambda i: i % n) if loop else (lambda i: min(max(i, 0), n - 1))
    for _ in range(2 if plant else 0):   # low-pass: no kinks where the supporting foot changes
        h = [(h[idx(f - 2)] + 2 * h[idx(f - 1)] + 3 * h[f] + 2 * h[idx(f + 1)] + h[idx(f + 2)]) / 9 for f in range(n)]
    for f in range(frames + 1):
        r, l = poses[f % n]
        l = dict(l); hz = Vector(l.get("Hips", Vector()))
        hz.z += h[f % n]
        l["Hips"] = hz
        pose(r, l); key(f)
    act.use_fake_user = True
    act.frame_range = (0, frames)
    return act

TAU = 2 * math.pi

def curve(pts, q):
    """Periodic Catmull-Rom through (phase, value) keys on [0,1) -> smooth, loopable joint curves."""
    q %= 1.0
    n = len(pts)
    for i in range(n):
        x0, _ = pts[i]; x1, _ = pts[(i + 1) % n]
        span = (x1 - x0) % 1.0 or 1.0
        t = ((q - x0) % 1.0) / span
        if t <= 1.0:
            p0, p1, p2, p3 = (pts[(i + k) % n][1] for k in (-1, 0, 1, 2))
            return 0.5 * (2 * p1 + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3)
    return pts[0][1]

# Gait curves (degrees) per leg, phase 0 = that foot's heel strike. Shapes follow captured human gait
# (as in Mixamo's in-place walk/run): hip flex +, knee flex +, ankle dorsiflex +.
WALK = dict(
    hip=[(0.0, 33), (0.15, 25), (0.35, 4), (0.52, -19), (0.62, -15), (0.75, 14), (0.88, 35)],
    knee=[(0.0, 4), (0.12, 18), (0.3, 8), (0.45, 7), (0.6, 40), (0.72, 63), (0.85, 30), (0.95, 6)],
    ankle=[(0.0, 2), (0.08, -6), (0.3, 4), (0.48, 10), (0.6, -16), (0.7, -8), (0.82, 4), (0.94, 3)],
)
RUN = dict(
    hip=[(0.0, 36), (0.12, 22), (0.3, -14), (0.4, -24), (0.55, 0), (0.72, 44), (0.88, 50)],
    knee=[(0.0, 18), (0.12, 38), (0.28, 22), (0.42, 40), (0.55, 100), (0.68, 112), (0.82, 70), (0.94, 30)],
    ankle=[(0.0, -2), (0.12, 12), (0.3, -10), (0.42, -26), (0.58, -12), (0.75, 6), (0.9, 6)],
)

def gait(p, G, arm_amp, arm_lag, elbow, elbow_amp, arm_in, lean, sway, pel_yaw, pel_roll, chest_counter, head_amp):
    rot, loc = {}, {}
    for sfx, side, ph in (("L", 1, 0.0), ("R", -1, 0.5)):
        q = (p + ph) % 1.0
        rot[f"Thigh.{sfx}"] = [("x", -curve(G["hip"], q))]
        rot[f"Shin.{sfx}"] = [("x", curve(G["knee"], q))]
        rot[f"Foot.{sfx}"] = [("x", -curve(G["ankle"], q))]
        # arms swing opposite to the same-side leg, lagging it slightly (follow-through);
        # the forearm lags the upper arm again and bends more on the forward swing
        qa = (q - arm_lag) % 1.0
        a = -math.cos(TAU * qa)                                # +1 = arm forward
        rot[f"UpperArm.{sfx}"] = [("y", arm_in * side), ("x", -arm_amp * a), ("z", -4 * side * a)]   # +y*side lowers the arm from the A-pose
        qf = (qa - 0.06) % 1.0
        f = 0.5 + 0.5 * -math.cos(TAU * qf)
        rot[f"ForeArm.{sfx}"] = [("x", -(elbow + elbow_amp * f))]
        rot[f"Hand.{sfx}"] = [("x", -8 - 6 * f), ("z", 6 * side)]
    # pelvis: yaw brings the forward leg's hip forward, roll drops the swing side, sway over the stance foot
    c2, s1 = math.cos(TAU * p), math.sin(TAU * p)
    s2 = math.sin(TAU * 2 * p)
    yaw = pel_yaw * c2
    roll = pel_roll * s1
    rot["Hips"] = [("x", lean * 0.35), ("z", yaw), ("y", roll)]
    rot["Spine"] = [("x", lean * 0.3 + 1.2 * s2), ("z", -yaw * 0.45 * chest_counter), ("y", -roll * 0.6)]
    rot["Chest"] = [("x", lean * 0.35 - 0.8 * s2), ("z", -yaw * 0.85 * chest_counter), ("y", -roll * 0.5)]
    # head stays level and looks ahead (counter-rotates the torso), with a little bob-lag nod
    rot["Neck"] = [("x", -lean * 0.45), ("z", yaw * 0.35), ("y", roll * 0.3)]
    rot["Head"] = [("x", -lean * 0.4 + head_amp * math.sin(TAU * 2 * p - 1.0)), ("z", yaw * 0.25)]
    loc["Hips"] = Vector((sway * HEIGHT * s1, 0, 0))
    return rot, loc

def idle(p):
    """Relaxed contrapposto: weight on the right leg, left knee soft, slow breathing (2 per loop),
    gentle weight drift and an occasional look around. All terms are loop harmonics -> seamless."""
    br = math.sin(TAU * 2 * p)                    # breathing
    dr = math.sin(TAU * p)                        # slow weight drift
    look = math.sin(TAU * p + 0.8)
    rot = {
        "Hips": [("y", -4.5 - 1.2 * dr), ("z", 3 + 1.0 * dr)],
        "Spine": [("x", 0.8 * br), ("y", 2.4 + 0.6 * dr)],
        "Chest": [("x", 1.4 * br), ("y", 1.6 + 0.5 * dr), ("z", -1.5 - 0.8 * dr)],
        "Neck": [("x", -0.8 * br), ("y", 0.8)],
        "Head": [("z", 7 * look), ("x", -1.5 + 2.0 * math.sin(TAU * p + 2.4)), ("y", -2 + 1.5 * look)],
        "Thigh.R": [("y", 4.5 + 1.2 * dr), ("x", -1)],
        "Shin.R": [("x", 1.5)],
        "Foot.R": [("y", -4.5)],
        "Thigh.L": [("y", 3.5 + 1.2 * dr), ("x", -7 - 1.5 * dr), ("z", 6)],
        "Shin.L": [("x", 13 + 2.5 * dr)],
        "Foot.L": [("x", -4), ("y", -3.5)],
    }
    for sfx, side in (("L", 1), ("R", -1)):
        lag = 0.4 if side > 0 else 0.0
        sway = math.sin(TAU * p + lag)
        rot[f"UpperArm.{sfx}"] = [("y", (21 + 1.2 * br) * side), ("x", 3 + 2 * sway + 0.8 * br), ("z", 4 * side)]
        rot[f"ForeArm.{sfx}"] = [("x", -16 - 3 * sway)]
        rot[f"Hand.{sfx}"] = [("x", -10 - 2 * sway), ("z", 8 * side)]
    return rot, {"Hips": Vector((-0.022 * HEIGHT - 0.004 * HEIGHT * dr, 0, 0.0012 * HEIGHT * br))}


def track(pts, t):
    """Catmull-Rom through (time, value) keys on [0,1] with clamped ends (one-shot clips)."""
    if t <= pts[0][0]: return pts[0][1]
    if t >= pts[-1][0]: return pts[-1][1]
    for i in range(len(pts) - 1):
        (x1, v1), (x2, v2) = pts[i], pts[i + 1]
        if x1 <= t <= x2:
            v0 = pts[i - 1][1] if i > 0 else v1
            v3 = pts[i + 2][1] if i + 2 < len(pts) else v2
            u = (t - x1) / (x2 - x1)
            return 0.5 * (2 * v1 + (-v0 + v2) * u + (2 * v0 - 5 * v1 + 4 * v2 - v3) * u * u + (-v0 + 3 * v1 - 3 * v2 + v3) * u ** 3)

def keyed(spec, t, loc=None):
    """spec: {bone: {axis: [(t, deg), ...]}} -> pose. Arms rotate y (raise/lower), then x (swing), then z.
    Conventions (match gait): legs x = -hipflex / +kneeflex / -dorsiflex; spine x + = lean forward;
    UpperArm y*side + = lower toward the body, x - = swing forward; ForeArm x - = bend."""
    rot = {}
    for b, ch in spec.items():
        order = ("y", "x", "z") if b.startswith("UpperArm") else ("x", "y", "z")
        rot[b] = [(a, track(ch[a], t)) for a in order if a in ch]
    return rot, {"Hips": Vector(loc(t)) if loc else Vector()}

def sides(spec_lr):
    """Expand {'Thigh': ({...L...}, {...R...})} and mirror arm y/z signs for the right side."""
    out = {}
    for b, (l, r) in spec_lr.items():
        out[b + ".L"] = l
        out[b + ".R"] = {a: [(t, -v) if (b in ("UpperArm", "Hand") and a in ("y", "z")) else (t, v) for t, v in pts]
                         for a, pts in r.items()}
    return out

JUMP_LAUNCH = 0.15   # fraction of JumpUp at which the feet leave the ground (game delays the launch to match)

def jump_up(t):
    """0.67 s: quick anticipation crouch -> explosive push-off (legs straight, toes pointed, arms swing
    up) -> feet leave the ground at JUMP_LAUNCH -> tuck towards the apex."""
    leg = lambda hip, knee, ank: {"Thigh": {"x": [(tt, -v) for tt, v in hip]}, "Shin": {"x": knee}, "Foot": {"x": [(tt, -v) for tt, v in ank]}}
    L = leg([(0, 6), (0.1, 34), (0.2, -4), (0.5, 36), (1, 46)], [(0, 10), (0.1, 62), (0.2, 2), (0.5, 64), (1, 78)], [(0, 4), (0.1, 28), (0.2, -30), (0.5, -14), (1, -10)])
    R = leg([(0, 4), (0.1, 32), (0.2, -8), (0.5, 20), (1, 30)], [(0, 8), (0.1, 60), (0.2, 4), (0.5, 42), (1, 60)], [(0, 4), (0.1, 28), (0.2, -32), (0.5, -18), (1, -12)])
    arm = {"y": [(0, 20), (0.1, 16), (0.22, -8), (0.6, -22), (1, -24)], "x": [(0, 0), (0.1, 32), (0.22, -60), (0.6, -30), (1, -22)], "z": [(0, 0), (1, 6)]}
    spec = sides({"Thigh": (L["Thigh"], R["Thigh"]), "Shin": (L["Shin"], R["Shin"]), "Foot": (L["Foot"], R["Foot"]),
                  "UpperArm": (arm, arm), "ForeArm": ({"x": [(0, -14), (0.1, -10), (0.22, -12), (1, -35)]},) * 2,
                  "Hand": ({"x": [(0, -8), (1, -14)], "z": [(0, 6), (1, 10)]},) * 2})
    spec.update({"Hips": {"x": [(0, 2), (0.1, 16), (0.2, -2), (1, 6)]}, "Spine": {"x": [(0, 1), (0.1, 12), (0.2, -5), (1, 4)]},
                 "Chest": {"x": [(0, 0), (0.1, 8), (0.2, -6), (1, 3)]}, "Neck": {"x": [(0, 0), (0.1, -10), (0.2, 3), (1, -2)]},
                 "Head": {"x": [(0, 0), (0.1, -8), (0.2, 4), (1, -2)]}})
    return keyed(spec, t)

def fall(p):
    """Air loop: tucked legs slowly cycling, arms out for balance, hair-light flutter (all loop harmonics)."""
    s, c = math.sin(TAU * p), math.cos(TAU * p)
    rot = {"Hips": [("x", 6 + 1.5 * s)], "Spine": [("x", 4 + 1.0 * s)], "Chest": [("x", 3 - 1.0 * c)],
           "Neck": [("x", -3)], "Head": [("x", -3 + 1.5 * c), ("z", 2 * s)],
           "Thigh.L": [("x", -(40 + 6 * s))], "Shin.L": [("x", 66 + 8 * s)], "Foot.L": [("x", 10)],
           "Thigh.R": [("x", -(26 - 6 * s))], "Shin.R": [("x", 50 - 8 * s)], "Foot.R": [("x", 12)]}
    for sfx, side, ph in (("L", 1, 0.0), ("R", -1, 1.3)):
        w = math.sin(TAU * p + ph)
        rot[f"UpperArm.{sfx}"] = [("y", (-24 + 4 * w) * side), ("x", -20 + 6 * w), ("z", 6 * side)]
        rot[f"ForeArm.{sfx}"] = [("x", -32 - 6 * w)]
        rot[f"Hand.{sfx}"] = [("x", -14 - 4 * w), ("z", 10 * side)]
    return rot, {"Hips": Vector()}

def land(t):
    """0.4 s: legs reach for the ground -> absorb into a crouch (pelvis drops via foot planting) -> stand."""
    leg = lambda hip, knee, ank: ({"x": [(tt, -v) for tt, v in hip]}, {"x": knee}, {"x": [(tt, -v) for tt, v in ank]})
    Lt, Ls, Lf = leg([(0, 26), (0.2, 56), (0.5, 26), (1, 2)], [(0, 40), (0.2, 104), (0.5, 46), (1, 4)], [(0, -6), (0.2, 34), (0.5, 16), (1, 2)])
    Rt, Rs, Rf = leg([(0, 16), (0.2, 52), (0.5, 22), (1, 0)], [(0, 30), (0.2, 98), (0.5, 42), (1, 3)], [(0, -8), (0.2, 34), (0.5, 16), (1, 2)])
    arm = {"y": [(0, -22), (0.22, -6), (0.6, 12), (1, 20)], "x": [(0, -24), (0.22, -34), (0.6, -8), (1, 0)], "z": [(0, 6), (1, 0)]}
    spec = sides({"Thigh": (Lt, Rt), "Shin": (Ls, Rs), "Foot": (Lf, Rf), "UpperArm": (arm, arm),
                  "ForeArm": ({"x": [(0, -32), (0.22, -40), (1, -12)]},) * 2,
                  "Hand": ({"x": [(0, -14), (1, -8)], "z": [(0, 10), (1, 6)]},) * 2})
    spec.update({"Hips": {"x": [(0, 6), (0.2, 20), (0.6, 6), (1, 1)]}, "Spine": {"x": [(0, 4), (0.2, 14), (0.6, 4), (1, 0)]},
                 "Chest": {"x": [(0, 3), (0.22, 8), (0.6, 2), (1, 0)]}, "Neck": {"x": [(0, -3), (0.22, -10), (1, 0)]},
                 "Head": {"x": [(0, -3), (0.22, -8), (1, 0)]}})
    return keyed(spec, t)

# --- secondary motion: hair and skirt follow-through ---------------------------------------------
# Hair bones: +x lifts the hair back/up off the back, y swings it sideways. Skirt: F -x / B +x lift the
# front / back panel outwards, L -y / R +y flare the sides, y on F/B and x on L/R swing the skirt around.
HAIR_W = (0.42, 0.36, 0.3)   # per-bone share; rotations accumulate down the chain (tip ~1.1x)

def add_sec(rot, hair_lift, hair_side, flare, back, front, swing, lag=0.0):
    """Each argument is a function of a per-bone delay d (in loop/phase units) -> degrees, so every
    bone further down the chain trails the one above it (overlapping action)."""
    for sfx in ("L", "R"):
        for k, nm in enumerate(("HairA", "HairB", "HairC")):
            d = lag + 0.05 * k
            rot[f"{nm}.{sfx}"] = [("x", max(0.0, hair_lift(d)) * HAIR_W[k]), ("y", hair_side(d) * HAIR_W[k])]
    d = lag + 0.04
    rot["Skirt.F"] = [("x", -max(0.0, front(d))), ("y", swing(d))]
    rot["Skirt.B"] = [("x", max(0.0, back(d))), ("y", swing(d))]
    rot["Skirt.L"] = [("y", -max(0.0, flare(d))), ("x", 0.5 * swing(d))]
    rot["Skirt.R"] = [("y", max(0.0, flare(d))), ("x", -0.5 * swing(d))]
    return rot

def loop_sec(fn, frames, hair, side, flare, back, front, swing, lag):
    """Wrap a loop clip: each secondary term is (base, amp, harmonic, phase) evaluated with lag."""
    def term(spec, p, d):
        base, amp, hz, ph = spec
        return base + amp * math.cos(TAU * (hz * (p - d) + ph))
    def g(p):
        rot, loc = fn(p)
        add_sec(rot, lambda d: term(hair, p, d), lambda d: term(side, p, d), lambda d: term(flare, p, d),
                lambda d: term(back, p, d), lambda d: term(front, p, d), lambda d: term(swing, p, d), lag)
        return rot, loc
    return g

def shot_sec(fn, hair, side, flare, back, front, swing):
    """Wrap a one-shot clip: secondary channels are keyed tracks; per-bone delay shifts the time."""
    def g(t):
        rot, loc = fn(t)
        add_sec(rot, lambda d: track(hair, t - d * 0.6), lambda d: track(side, t - d * 0.6), lambda d: track(flare, t - d * 0.6),
                lambda d: track(back, t - d * 0.6), lambda d: track(front, t - d * 0.6), lambda d: track(swing, t - d * 0.6))
        return rot, loc
    return g

walk = lambda p: gait(p, WALK, arm_amp=19, arm_lag=0.04, elbow=12, elbow_amp=14, arm_in=20,
                      lean=3, sway=0.012, pel_yaw=7, pel_roll=4, chest_counter=1.0, head_amp=1.2)
run = lambda p: gait(p, RUN, arm_amp=30, arm_lag=0.03, elbow=62, elbow_amp=26, arm_in=15,
                     lean=11, sway=0.008, pel_yaw=9, pel_roll=3, chest_counter=1.2, head_amp=2.0)
Z = (0, 0, 1, 0)
acts = {
    #                                    hair lift           hair side          skirt flare        back               front              swing          lag
    "Idle": make_action("Idle", 150, loop_sec(idle, 150, (2.0, 1.2, 2, 0), (0, 1.2, 1, 0), (1.0, 0.6, 2, 0), (1.0, 0.6, 2, 0), (0.5, 0.4, 2, 0), (0, 1.0, 1, 0.2), 0.06)),
    "Walk": make_action("Walk", 28, loop_sec(walk, 28, (6.0, 3.5, 2, 0), (0, 5.0, 1, 0.25), (3.0, 2.0, 2, 0), (4.0, 3.0, 2, 0), (2.0, 2.0, 2, 0.5), (0, 4.0, 1, 0.25), 0.1)),
    "Run":  make_action("Run", 20, loop_sec(run, 20, (24.0, 8.0, 2, 0), (0, 8.0, 1, 0.25), (9.0, 5.0, 2, 0), (14.0, 6.0, 2, 0), (6.0, 4.0, 2, 0.5), (0, 6.0, 1, 0.25), 0.12),
                        lift=lambda p: 0.025 * HEIGHT * max(0.0, math.sin(TAU * 2 * (p - 0.27))) ** 1.5),
    "JumpUp": make_action("JumpUp", 20, shot_sec(jump_up,
                          hair=[(0, 4), (0.1, 10), (0.22, 0), (0.5, 8), (1, 30)], side=[(0, 0), (1, 0)],
                          flare=[(0, 2), (0.1, 6), (0.22, 0), (0.6, 10), (1, 18)], back=[(0, 3), (0.1, 8), (0.22, 0), (0.6, 8), (1, 16)],
                          front=[(0, 2), (0.1, 0), (0.22, 2), (0.6, 6), (1, 12)], swing=[(0, 0), (1, 0)]),
                          plant=lambda p: 1.0 - min(1.0, max(0.0, (p - JUMP_LAUNCH) / 0.08)), loop=False),
    "Fall": make_action("Fall", 24, loop_sec(fall, 24, (32.0, 4.0, 1, 0), (0, 4.0, 1, 0.3), (18.0, 3.0, 1, 0.2), (16.0, 3.0, 1, 0), (12.0, 3.0, 1, 0.5), (0, 3.0, 1, 0.1), 0.08), plant=False),
    "Land": make_action("Land", 14, shot_sec(land,
                        hair=[(0, 30), (0.2, 0), (0.42, 12), (0.7, 4), (1, 3)], side=[(0, 0), (1, 0)],
                        flare=[(0, 18), (0.2, 0), (0.42, 7), (0.7, 2), (1, 1.5)], back=[(0, 16), (0.2, 1), (0.42, 8), (0.7, 3), (1, 1.5)],
                        front=[(0, 12), (0.2, 0), (0.42, 5), (0.7, 2), (1, 0.8)], swing=[(0, 0), (1, 0)]), loop=False),
}
# ground speed of each clip at 1x = speed of the planted foot sliding back (in-place loop); Unity's
# blend-tree thresholds use these so the feet do not skate (written next to the FBX as JSON)
def clip_speed(act):
    rig.animation_data.action = act; n = int(act.frame_range[1]); sc = bpy.context.scene
    ys, zs = [], []
    for f in range(n + 1):
        sc.frame_set(f); bpy.context.view_layer.update()
        hd = PB["Foot.L"].head; ys.append(hd.y); zs.append(hd.z - FOOT_REST["L"][0])
    v = sorted((ys[f + 1] - ys[f]) * FPS for f in range(n) if zs[f] < 0.015 and zs[f + 1] < 0.015)
    return round(v[len(v) // 2], 3) if v else 0.0
SPEEDS = {k: clip_speed(acts[k]) for k in ("Walk", "Run")}
SPEEDS["JumpLaunch"] = round(JUMP_LAUNCH * acts["JumpUp"].frame_range[1] / FPS, 3)   # s of crouch before take-off
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
