"""Rig + animate the Tripo cat in Blender (headless). No Tripo rigging is used.

    blender -b -P tools/blender/rig_cat.py -- <in.glb> <out_dir> <name> <height_m> <max_tris> [render_dir]

1. import, join, decimate; the model stands at an angle, so the forward axis is measured from the four
   paws (k-means on the lowest vertices) and the mesh is turned to face -Y (Unity +Z after export)
2. armature from joints measured on the mesh: spine, neck, head, two ears, four 3-bone legs, 4-bone tail
3. skin weights by region (legs below the belly by quadrant, tail behind the hips, head + ears, body by
   distance to the spine) — Tripo meshes are many open pieces, so Blender's heat weighting is not usable
4. keyframed clips (in place): Idle, Walk, Run, SitDown -> SitLick (loop), LieDown -> BellyUp (loop), Rub
   locomotion keeps the planted paw on the ground; the lying/sitting clips are lifted so the lowest point of
   the deformed mesh touches the ground
5. export FBX + <name>_speeds.json (ground speed of Walk/Run, for foot-skate-free blend thresholds)
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
body.data.transform(Matrix.Translation((0, 0, -mn[2]))); body.data.transform(Matrix.Scale(1 / H0, 4))   # height 1
co = verts()

def kmeans(P, k, it=60):
    rng = np.random.default_rng(1); c = P[rng.choice(len(P), k, replace=False)]
    for _ in range(it):
        l = np.argmin(((P[:, None] - c[None]) ** 2).sum(2), 1)
        c = np.array([P[l == j].mean(0) if (l == j).any() else c[j] for j in range(k)])
    return c

feet = kmeans(co[co[:, 2] < 0.06][:, :2], 4)
headc = co[(co[:, 2] > 0.55) & (co[:, 2] < 0.8)][:, :2].mean(0)
order = np.argsort(np.linalg.norm(feet - headc, axis=1))
front, back = feet[order[:2]].mean(0), feet[order[2:]].mean(0)
fwd = (front - back) / np.linalg.norm(front - back)
ang = math.atan2(fwd[1], fwd[0]) - math.atan2(-1, 0)          # turn fwd onto -Y
body.data.transform(Matrix.Rotation(-ang, 4, "Z"))
co = verts()
feet = kmeans(co[co[:, 2] < 0.06][:, :2], 4)
c4 = feet.mean(0)
body.data.transform(Matrix.Translation((-c4[0], -c4[1], 0)))
co = verts(); feet -= c4
print("ORIENT " + json.dumps({"turn_deg": round(math.degrees(ang), 1), "feet": np.round(feet, 3).tolist()}))

fr = feet[feet[:, 1] < 0]; bk = feet[feet[:, 1] >= 0]
yF, yB = float(fr[:, 1].mean()), float(bk[:, 1].mean())
xF, xB = float(np.abs(fr[:, 0]).mean()), float(np.abs(bk[:, 0]).mean())
V = co.copy()                                                     # normalised (height 1) for measuring/weights
x, y, z = V[:, 0], V[:, 1], V[:, 2]

# tail: everything behind the hind legs above the hocks -> centroid curve by height
tail_m = (y > yB + 0.09) & (z > 0.3)
tz = z[tail_m]; tpts = []
for k in range(6):
    lo = tz.min() + (tz.max() - tz.min()) * k / 6; hi = lo + (tz.max() - tz.min()) / 6
    s = V[tail_m & (z >= lo) & (z <= hi + 1e-6)]
    if len(s): tpts.append(s.mean(0))
tip = V[tail_m][np.argmax(tz)]
tpts = [np.array([0.0, yB + 0.1, 0.40])] + [p for p in tpts[1:]] + [tip]
# resample the tail polyline into 5 points (4 bones) by length
seg = [np.linalg.norm(tpts[i + 1] - tpts[i]) for i in range(len(tpts) - 1)]; L = sum(seg)
TP = []
for k in range(5):
    d = L * k / 4; acc = 0
    for i, sl in enumerate(seg):
        if acc + sl >= d - 1e-9 or i == len(seg) - 1:
            t = 0 if sl < 1e-9 else (d - acc) / sl; TP.append(tpts[i] + (tpts[i + 1] - tpts[i]) * min(t, 1)); break
        acc += sl
# ears: highest pieces in front of the tail, split left/right
ear_m = (z > 0.78) & (y < yB) & ~tail_m
EARS = {}
for sfx, side in (("L", 1), ("R", -1)):
    s = V[ear_m & (x * side > 0.03)]
    if len(s) > 10:
        base = s[s[:, 2] < s[:, 2].min() + 0.03].mean(0); tipe = s[np.argmax(s[:, 2])]
        EARS[sfx] = (base, tipe)
print("MEASURE " + json.dumps({"yF": round(yF, 3), "yB": round(yB, 3), "xF": round(xF, 3), "xB": round(xB, 3),
                               "tail": np.round(np.array(TP), 3).tolist(), "ears": list(EARS)}))

SPINE_Z = 0.42
JOINTS = {
    "Hips": (0, yB + 0.02, SPINE_Z), "Spine": (0, (yF + yB) / 2, SPINE_Z + 0.01), "Chest": (0, yF + 0.05, SPINE_Z + 0.02),
    "Neck": (0, yF + 0.01, 0.51), "Head": (0, yF - 0.01, 0.57), "HeadTop": (0, yF - 0.03, 0.84),
    "Shoulder": (xF, yF + 0.01, 0.37), "Elbow": (xF, yF + 0.03, 0.21), "Wrist": (xF, yF + 0.0, 0.065), "FToe": (xF, yF - 0.06, 0.01),
    "HipJ": (xB, yB, 0.36), "Knee": (xB, yB - 0.035, 0.22), "Hock": (xB, yB + 0.025, 0.085), "BToe": (xB, yB - 0.05, 0.01),
}
S = HEIGHT
body.data.transform(Matrix.Scale(S, 4)); body.data.update(); body.name = NAME
def J(k, side=1):
    a, b, c = JOINTS[k]; return Vector((a * side * S, b * S, c * S))
def P(p): return Vector((float(p[0]) * S, float(p[1]) * S, float(p[2]) * S))

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
bone("Root", Vector((0, 0, 0)), Vector((0, 0, 0.2 * S)))
bone("Hips", J("Hips"), J("Spine"), "Root")
bone("Spine", J("Spine"), J("Chest"), "Hips", True)
bone("Chest", J("Chest"), J("Neck"), "Spine", True)
bone("Neck", J("Neck"), J("Head"), "Chest", True)
bone("Head", J("Head"), J("HeadTop"), "Neck", True)
for sfx, (b0, t0) in EARS.items():
    bone(f"Ear.{sfx}", P(b0), P(t0), "Head")
for side, sfx in ((1, "L"), (-1, "R")):
    bone(f"FrontUpper.{sfx}", J("Shoulder", side), J("Elbow", side), "Chest")
    bone(f"FrontLower.{sfx}", J("Elbow", side), J("Wrist", side), f"FrontUpper.{sfx}", True)
    bone(f"FrontPaw.{sfx}", J("Wrist", side), J("FToe", side), f"FrontLower.{sfx}", True)
    bone(f"BackThigh.{sfx}", J("HipJ", side), J("Knee", side), "Hips")
    bone(f"BackShin.{sfx}", J("Knee", side), J("Hock", side), f"BackThigh.{sfx}", True)
    bone(f"BackPaw.{sfx}", J("Hock", side), J("BToe", side), f"BackShin.{sfx}", True)
prev = "Hips"
for k in range(4):
    bone(f"Tail{k + 1}", P(TP[k]), P(TP[k + 1]), prev, k > 0); prev = f"Tail{k + 1}"
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
# body: inverse distance to the spine/neck chain
core = ["Hips", "Spine", "Chest", "Neck", "Head"]
Dc = np.stack([seg_dist(V, *segs[n]) for n in core], 1)
Wc = 1 / (Dc ** 4 + 1e-10); Wc /= Wc.sum(1, keepdims=True)
for k, n in enumerate(core): W[:, NI[n]] = Wc[:, k]
# head: big round head -> rigid on Head above the neck line, blended through the neck
headzone = (y < yF + 0.17) & ~tail_m
hz = band(z, 0.47, 0.56) * headzone
W *= (1 - hz)[:, None]; W[:, NI["Head"]] += hz
# ears
for sfx in EARS:
    d = seg_dist(V, *segs[f"Ear.{sfx}"])
    e = (d < 0.06) & (z > 0.74) & headzone
    w = band(z, 0.76, 0.80) * e
    W *= (1 - w)[:, None]; W[:, NI[f"Ear.{sfx}"]] += w
# legs: below the belly, by quadrant; along the chain by height
legzone = (z < 0.33) & ~tail_m
for sfx, side in (("L", 1), ("R", -1)):
    for pre, ys, chain, cuts in (("F", y < (yF + yB) / 2, ("FrontUpper", "FrontLower", "FrontPaw"), (0.20, 0.24, 0.06, 0.09)),
                                 ("B", y >= (yF + yB) / 2, ("BackThigh", "BackShin", "BackPaw"), (0.20, 0.24, 0.07, 0.10))):
        fx, fy = (xF, yF) if pre == "F" else (xB, yB)
        near = (np.hypot(x - fx * side, y - fy) < 0.095) | (z < 0.1)      # only the leg itself, not the belly fur
        i = np.where(legzone & ys & (x * side >= 0) & near)[0]
        if not len(i): continue
        zz = z[i]
        up = band(zz, cuts[0], cuts[1]); paw = 1 - band(zz, cuts[2], cuts[3]); mid = (1 - up) * (1 - paw)
        body_share = band(zz, 0.27, 0.33)                       # top of the leg blends into the body
        keep = W[i] * body_share[:, None]
        W[i] = keep
        W[i, NI[f"{chain[0]}.{sfx}"]] += up * (1 - body_share)
        W[i, NI[f"{chain[1]}.{sfx}"]] += mid * (1 - body_share)
        W[i, NI[f"{chain[2]}.{sfx}"]] += paw * (1 - body_share)
# tail
i = np.where(tail_m)[0]
tn = [f"Tail{k}" for k in range(1, 5)]
Dt = np.stack([seg_dist(V[i], *segs[n]) for n in tn], 1)
Wt = 1 / (Dt ** 4 + 1e-10); Wt /= Wt.sum(1, keepdims=True)
root_share = 1 - band(y[i], yB + 0.09, yB + 0.16)               # where the tail meets the rump
W[i] *= root_share[:, None]
for k, n in enumerate(tn): W[i, NI[n]] += Wt[:, k] * (1 - root_share)
W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
for n in names: body.vertex_groups.new(name=n)
vg = [body.vertex_groups[n] for n in names]
for vi in range(len(V)):
    row = W[vi]; top = np.argsort(-row)[:4]
    for k in top:
        if row[k] > 0.02: vg[k].add([vi], float(row[k]), "REPLACE")
body.parent = rig
mod = body.modifiers.new("Armature", "ARMATURE"); mod.object = rig

# --- animation helpers ---------------------------------------------------------------------------
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
PAWS = [f"{p}.{s}" for p in ("FrontPaw", "BackPaw") for s in ("L", "R")]
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

def tail_wave(p, amp, freq, lag, base_x=0.0, curl=0.0):
    """Overlapping S-curve: each segment trails the one before it."""
    r = {}
    for k in range(4):
        w = (0.5 + 0.35 * k)
        r[f"Tail{k + 1}"] = [("x", base_x * (1 if k == 0 else 0.4) + curl * k / 3), ("z", amp * w * math.sin(TAU * (freq * p - lag * k)))]
    return r

# ----------------------------------------------------------------------------------------- Idle
def idle(p):
    br = math.sin(TAU * 3 * p)                                     # three breaths per loop
    look = curve([(0.0, 0), (0.18, 0), (0.3, 16), (0.48, 16), (0.58, -8), (0.78, -8), (0.9, 0)], p)
    tilt = curve([(0.0, 0), (0.3, 0), (0.36, 7), (0.48, 6), (0.55, 0)], p)
    ear = max(0.0, 1 - abs(p - 0.62) / 0.025) * 28 + max(0.0, 1 - abs(p - 0.66) / 0.02) * 16   # quick double flick
    r = {"Spine": [("x", -0.8 * br)], "Chest": [("x", 0.6 * br)],
         "Neck": [("z", look * 0.35)], "Head": [("z", look * 0.65), ("y", tilt), ("x", 1.2 * br)],
         "Ear.L": [("y", ear)], "Ear.R": [("y", -0.3 * ear)]}
    r.update(tail_wave(p, 9, 1, 0.08, base_x=0, curl=-4))
    return r, {}

# --------------------------------------------------------------------------------------- gaits
# per-leg phase 0 = touch-down; stance until ~0.6. Angles about x: negative swings the leg forward.
WALK = dict(fu=[(0.0, -16), (0.58, 15), (0.72, 2), (0.88, -20)], fl=[(0.0, 0), (0.5, 0), (0.64, 32), (0.78, 48), (0.92, 6)],
            fp=[(0.0, 0), (0.55, -14), (0.68, 28), (0.84, 8)],
            bt=[(0.0, -18), (0.58, 17), (0.75, 4), (0.9, -22)], bs=[(0.0, 2), (0.52, -6), (0.66, 30), (0.8, 30), (0.94, 6)],
            bp=[(0.0, 0), (0.55, 18), (0.7, -22), (0.86, -6)])
RUN = dict(fu=[(0.0, -24), (0.45, 20), (0.62, 6), (0.82, -28)], fl=[(0.0, 0), (0.4, 0), (0.55, 38), (0.72, 52), (0.9, 6)],
           fp=[(0.0, 0), (0.42, -16), (0.58, 28), (0.8, 8)],
           bt=[(0.0, -24), (0.45, 22), (0.62, 8), (0.84, -28)], bs=[(0.0, 4), (0.42, -8), (0.58, 32), (0.76, 34), (0.92, 6)],
           bp=[(0.0, 0), (0.42, 20), (0.62, -24), (0.84, -6)])

def legs(p, G, ph):
    r = {}
    for sfx, (pf, pb) in ph.items():
        qf, qb = p - pf, p - pb
        r[f"FrontUpper.{sfx}"] = [("x", curve(G["fu"], qf))]
        r[f"FrontLower.{sfx}"] = [("x", curve(G["fl"], qf))]
        r[f"FrontPaw.{sfx}"] = [("x", curve(G["fp"], qf))]
        r[f"BackThigh.{sfx}"] = [("x", curve(G["bt"], qb))]
        r[f"BackShin.{sfx}"] = [("x", curve(G["bs"], qb))]
        r[f"BackPaw.{sfx}"] = [("x", curve(G["bp"], qb))]
    return r

def walk(p):
    # lateral-sequence walk: BL 0, FL 0.25, BR 0.5, FR 0.75 (what cats do)
    r = legs(p, WALK, {"L": (0.25, 0.0), "R": (0.75, 0.5)})
    sw = math.sin(TAU * p)
    r = merge(r, {"Hips": [("y", 3 * sw), ("z", 2.5 * sw)], "Chest": [("y", -3 * sw), ("z", -3 * sw)],
                  "Neck": [("x", 1.5 * math.sin(TAU * 2 * p + 1))], "Head": [("z", 2 * sw), ("x", -1.5 * math.sin(TAU * 2 * p + 1))]})
    r.update(tail_wave(p, 8, 1, 0.1, base_x=-4, curl=-6))
    return r, {}

def run(p):
    # bound: hind pair then front pair, small L/R split; the spine flexes and extends
    r = legs(p, RUN, {"L": (0.42, 0.0), "R": (0.47, 0.05)})
    flex = math.sin(TAU * (p - 0.15))
    r = merge(r, {"Hips": [("x", 7 * flex)], "Spine": [("x", -5 * flex)], "Chest": [("x", -6 * flex)],
                  "Neck": [("x", 10 - 4 * flex)], "Head": [("x", -6 + 6 * flex)]})
    r.update(tail_wave(p, 6, 1, 0.12, base_x=-10, curl=0))   # tail streams out behind (-x tips an upright bone back)
    return r, {}

# -------------------------------------------------------------------------------------- sitting
SIT = {"Hips": [("x", -30)], "BackThigh.L": [("x", -42), ("y", -6)], "BackThigh.R": [("x", -42), ("y", 6)],
       "BackShin.L": [("x", 68)], "BackShin.R": [("x", 68)], "BackPaw.L": [("x", -26)], "BackPaw.R": [("x", -26)],
       "FrontUpper.L": [("x", 27)], "FrontUpper.R": [("x", 27)], "FrontLower.L": [("x", 2)], "FrontLower.R": [("x", 2)],
       "FrontPaw.L": [("x", 2)], "FrontPaw.R": [("x", 2)],
       "Neck": [("x", 12)], "Head": [("x", 14)],
       "Tail1": [("x", 24), ("z", 10)], "Tail2": [("x", -6), ("z", 20)], "Tail3": [("z", 24)], "Tail4": [("z", 18)]}   # Tail1 +x cancels the hips pitch
def sit_down(t):
    a = track([(0, 0), (0.15, -0.08), (0.75, 1.06), (0.88, 0.98), (1, 1.0)], t)   # anticipation, overshoot, settle
    r = lerp_pose({}, SIT, max(a, 0))
    if a < 0: r = merge(r, {"Hips": [("x", 40 * a)], "Head": [("x", -20 * a)]})  # a small lean forward first
    return r, {}

def sit_lick(p):
    # paw comes up (0.1-0.25), three quick licks, a pause, two slower ones, paw goes down (0.85-1.0)
    raise_ = curve([(0.0, 0), (0.08, 0), (0.22, 1), (0.82, 1), (0.95, 0)], p)
    lick = 0.0
    for c, w in ((0.30, 0.035), (0.37, 0.035), (0.44, 0.035), (0.62, 0.05), (0.73, 0.05)):
        lick += max(0.0, 1 - abs(p - c) / w)
    r = lerp_pose(SIT, {}, 0)
    r = merge(r, {"FrontUpper.L": [("x", -72 * raise_), ("z", 10 * raise_)], "FrontLower.L": [("x", 70 * raise_)],
                  "FrontPaw.L": [("x", 40 * raise_)],
                  "Chest": [("y", 6 * raise_)],
                  "Neck": [("x", 4 * raise_), ("z", 10 * raise_)],
                  "Head": [("x", (12 + 7 * lick) * raise_), ("z", 14 * raise_), ("y", -8 * raise_)],
                  "Ear.L": [("y", 6 * lick)], "Ear.R": [("y", -6 * lick)]})
    r = merge(r, {"Tail3": [("z", 10 * math.sin(TAU * p))], "Tail4": [("z", 16 * math.sin(TAU * p - 0.6))]})
    return r, {}

# -------------------------------------------------------------------------------- lying on its back
CROUCH = {"BackThigh.L": [("x", -50)], "BackThigh.R": [("x", -50)], "BackShin.L": [("x", 85)], "BackShin.R": [("x", 85)],
          "BackPaw.L": [("x", -35)], "BackPaw.R": [("x", -35)],
          "FrontUpper.L": [("x", -40)], "FrontUpper.R": [("x", -40)], "FrontLower.L": [("x", 75)], "FrontLower.R": [("x", 75)],
          "FrontPaw.L": [("x", -30)], "FrontPaw.R": [("x", -30)], "Head": [("x", -10)]}
BELLY = {"Hips": [("y", 105)], "Spine": [("y", 8)], "Chest": [("y", 6)], "Neck": [("y", -55), ("x", -8)], "Head": [("y", -75), ("z", 12)],
         "FrontUpper.L": [("x", -55), ("z", 15)], "FrontUpper.R": [("x", -55), ("z", -15)],
         "FrontLower.L": [("x", 95)], "FrontLower.R": [("x", 95)], "FrontPaw.L": [("x", 40)], "FrontPaw.R": [("x", 40)],
         "BackThigh.L": [("x", -45), ("y", -20)], "BackThigh.R": [("x", -45), ("y", 20)],
         "BackShin.L": [("x", 60)], "BackShin.R": [("x", 60)], "BackPaw.L": [("x", 25)], "BackPaw.R": [("x", 25)],
         "Tail1": [("z", 40)], "Tail2": [("z", 30)], "Tail3": [("z", 20)]}
def lie_down(t):
    c = track([(0, 0), (0.35, 1), (0.55, 1), (1, 0)], t)              # crouch, then the legs come up as it rolls
    roll = track([(0, 0), (0.4, 0), (0.88, 1.05), (1, 1)], t)
    r = lerp_pose(lerp_pose({}, CROUCH, c), BELLY, roll)
    return r, {}

def belly_up(p):
    wig = math.sin(TAU * p); bat = curve([(0, 0), (0.4, 0), (0.47, 1), (0.53, 0.2), (0.58, 1), (0.66, 0)], p)
    r = lerp_pose({}, BELLY, 1)
    r = merge(r, {"Hips": [("y", 7 * wig)], "Chest": [("y", -5 * wig)], "Head": [("y", 6 * math.sin(TAU * p + 1)), ("x", 4 * bat)],
                  "FrontUpper.L": [("x", -25 * bat)], "FrontLower.L": [("x", -30 * bat)],
                  "FrontUpper.R": [("x", 8 * math.sin(TAU * 2 * p))], "BackThigh.L": [("x", 6 * wig)], "BackThigh.R": [("x", -6 * wig)],
                  "Tail2": [("z", 18 * math.sin(TAU * 2 * p))], "Tail3": [("z", 25 * math.sin(TAU * 2 * p - 0.7))],
                  "Tail4": [("z", 30 * math.sin(TAU * 2 * p - 1.4))]})
    return r, {}

# ---------------------------------------------------------------------------------- rubbing the witch
def rub(p):
    s = math.sin(TAU * p)                                             # one slow side-to-side rub per loop
    push = 0.5 + 0.5 * math.sin(TAU * 2 * p - 1)
    r = {"Hips": [("y", 5 * s)], "Spine": [("y", 4 * s), ("z", -4 * s)], "Chest": [("y", 6 * s), ("z", -6 * s), ("x", -4 * push)],
         "Neck": [("y", 14 * s), ("z", 10 * s), ("x", -6)], "Head": [("y", 24 * s), ("z", 16 * s), ("x", -10 - 6 * push)],
         "Ear.L": [("y", -10 - 8 * push)], "Ear.R": [("y", 10 + 8 * push)],
         "FrontUpper.L": [("x", -4 * push)], "FrontUpper.R": [("x", -4 * push)]}
    q = math.sin(TAU * 6 * p)                                          # happy tail: upright, tip quivering
    r.update({"Tail1": [("x", -10)], "Tail2": [("x", -14)], "Tail3": [("z", 6 * q)], "Tail4": [("z", 12 * q), ("x", 20)]})
    return r, {}

acts = {
    "Idle": make_action("Idle", 150, idle),
    "Walk": make_action("Walk", 22, walk),
    "Run": make_action("Run", 12, run, lift=lambda p: 0.05 * S * max(0.0, math.sin(TAU * (p - 0.55))) ** 1.5),
    "SitDown": make_action("SitDown", 22, sit_down, ground="mesh", loop=False),
    "SitLick": make_action("SitLick", 110, sit_lick, ground="mesh"),
    "LieDown": make_action("LieDown", 40, lie_down, ground="mesh", loop=False),
    "BellyUp": make_action("BellyUp", 96, belly_up, ground="mesh"),
    "Rub": make_action("Rub", 54, rub),
}

def clip_speed(act):
    rig.animation_data.action = act; n = int(act.frame_range[1]); sc = bpy.context.scene
    ys, zs = [], []
    for f in range(n + 1):
        sc.frame_set(f); bpy.context.view_layer.update()
        hd = PB["FrontPaw.L"].head; ys.append(hd.y); zs.append(hd.z - PAW_REST["FrontPaw.L"][0])
    v = sorted((ys[f + 1] - ys[f]) * FPS for f in range(n) if zs[f] < 0.01 * S * 3 and zs[f + 1] < 0.01 * S * 3)
    return round(v[len(v) // 2], 3) if v else 0.0
SPEEDS = {k: clip_speed(acts[k]) for k in ("Walk", "Run")}
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
                f = round(i * n / 6) if an not in ("SitDown", "LieDown") else round(i * n / 5)
                sc.frame_set(f)
                p = os.path.join(RENDER, f"{an}_{view}_{i}_f{f:03d}.png")
                sc.render.filepath = p
                bpy.ops.render.render(write_still=True)
                files.append(p)
    print("RENDERS " + json.dumps(len(files)))
