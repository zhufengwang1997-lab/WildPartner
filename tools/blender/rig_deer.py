"""Rig + animate the Tripo white deer in Blender (headless). No Tripo rigging is used.

    blender -b -P tools/blender/rig_deer.py -- <in.glb> <out_dir> <name> <height_m> <max_tris> [render_dir]

1. import, join, decimate; the model stands at an angle, so the forward axis is measured from the four
   paws (k-means on the lowest vertices) and the mesh is turned to face -Y (Unity +Z after export)
2. armature (same scheme as rig_cat.py): spine, two neck bones, head (antlers ride on it), ears, four 4-bone
   legs (upper / fore / cannon / hoof), 2-bone tail. The model stands with its hind legs staggered, so every
   clip adds a per-leg correction that brings the hooves back under the hips
3. skin weights by region: legs by distance to their own chain (the thigh / upper arm blending into the
   body), head + antlers rigid, ears, tail, body by distance to the spine
4. keyframed clips (in place): Idle (look around, ear flicks, tail flicks), Walk, Run (bounding gallop),
   GrazeDown (one shot) -> Graze (loop: nibbling, chewing, ear and tail flicks)
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
headc = co[(co[:, 2] > 0.6) & (co[:, 2] < 0.78)][:, :2].mean(0)
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

FOOT = {}
for f in feet:
    FOOT[("F" if f[1] < 0 else "B") + ("L" if f[0] > 0 else "R")] = (float(abs(f[0])), float(f[1]))
assert len(FOOT) == 4, FOOT
yF = (FOOT["FL"][1] + FOOT["FR"][1]) / 2; yB = (FOOT["BL"][1] + FOOT["BR"][1]) / 2
V = co.copy()                                                     # normalised (height 1) for measuring/weights
x, y, z = V[:, 0], V[:, 1], V[:, 2]


print("MEASURE " + json.dumps({"feet": {k: [round(a, 3) for a in v] for k, v in FOOT.items()}}))

# joints in normalised coordinates (height 1 incl. antlers), read off the oriented side / front views
JOINTS = {
    "Hips": (0, 0.15, 0.47), "Spine": (0, 0.0, 0.47), "Chest": (0, -0.15, 0.48),
    "Neck1": (0, -0.19, 0.52), "Neck2": (0, -0.235, 0.61), "Head": (0, -0.275, 0.70), "HeadTop": (0, -0.29, 0.80),
    "Tail0": (0, 0.205, 0.49), "Tail1": (0, 0.245, 0.42), "Tail2": (0, 0.27, 0.33),
}
def leg_joints(pre, side):
    fx, fy = FOOT[pre + side]
    if pre == "F":
        return [(fx * 0.75, -0.165, 0.45), (fx, -0.165, 0.33), (fx, fy + 0.005, 0.15), (fx, fy, 0.045), (fx, fy - 0.04, 0.0)]
    return [(fx * 0.75, 0.15, 0.44), (fx, 0.11, 0.30), (fx, fy + 0.04, 0.18), (fx, fy, 0.045), (fx, fy - 0.04, 0.0)]
EAR = {s: ((0.045 * k, -0.27, 0.735), (0.125 * k, -0.255, 0.72)) for s, k in (("L", 1), ("R", -1))}
LEGB = {"F": ("FrontUpper", "FrontFore", "FrontCannon", "FrontHoof"), "B": ("BackThigh", "BackShin", "BackCannon", "BackHoof")}

S = HEIGHT
body.data.transform(Matrix.Scale(S, 4)); body.data.update(); body.name = NAME
def P(p, side=1): return Vector((float(p[0]) * side * S, float(p[1]) * S, float(p[2]) * S))
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
bone("Root", Vector((0, 0, 0)), Vector((0, 0, 0.2 * S)))
bone("Hips", J("Hips"), J("Spine"), "Root")
bone("Spine", J("Spine"), J("Chest"), "Hips", True)
bone("Chest", J("Chest"), J("Neck1"), "Spine", True)
bone("Neck1", J("Neck1"), J("Neck2"), "Chest", True)
bone("Neck2", J("Neck2"), J("Head"), "Neck1", True)
bone("Head", J("Head"), J("HeadTop"), "Neck2", True)
for sfx, (b0, t0) in EAR.items():
    bone(f"Ear.{sfx}", P(b0), P(t0), "Head")
for sfx in ("L", "R"):
    for pre, parent in (("F", "Chest"), ("B", "Hips")):
        js = leg_joints(pre, sfx); side = 1 if sfx == "L" else -1
        prev = parent
        for k, bn in enumerate(LEGB[pre]):
            bone(f"{bn}.{sfx}", P(js[k], side), P(js[k + 1], side), prev, k > 0); prev = f"{bn}.{sfx}"
bone("Tail1", J("Tail0"), J("Tail1"), "Hips")
bone("Tail2", J("Tail1"), J("Tail2"), "Tail1", True)
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
x, y, z = V[:, 0], V[:, 1], V[:, 2]
core = ["Hips", "Spine", "Chest", "Neck1", "Neck2", "Head"]
Dc = np.stack([seg_dist(V, *segs[n]) for n in core], 1)
Wc = 1 / (Dc ** 4 + 1e-10); Wc /= Wc.sum(1, keepdims=True)
for k, n in enumerate(core): W[:, NI[n]] = Wc[:, k]
# head + antlers: rigid on Head
hz = np.maximum(band(z, 0.63, 0.69) * (y < -0.235), (z > 0.765).astype(float))
W *= (1 - hz)[:, None]; W[:, NI["Head"]] += hz
# ears
for sfx in EAR:
    d = seg_dist(V, *segs[f"Ear.{sfx}"])
    w = ((d < 0.035) & (np.abs(x) > 0.055) & (z < 0.765) & (y < -0.2)).astype(float) * band(np.abs(x), 0.055, 0.075)
    W *= (1 - w)[:, None]; W[:, NI[f"Ear.{sfx}"]] += w
# legs: each vertex near a leg chain goes to that chain (nearest bone), blending into the body up top
chains = {}
for sfx in ("L", "R"):
    for pre in ("F", "B"):
        bn = [f"{b}.{sfx}" for b in LEGB[pre]]
        chains[pre + sfx] = (bn, np.stack([seg_dist(V, *segs[n]) for n in bn], 1))
dmin = np.stack([c[1].min(1) for c in chains.values()], 1)
which = np.argmin(dmin, 1); keys = list(chains)
for ci, kk in enumerate(keys):
    bn, D = chains[kk]; side = 1 if kk[1] == "L" else -1
    sel = (which == ci) & (dmin[:, ci] < 0.075) & (x * side > 0.015) & (z < 0.47)
    i = np.where(sel)[0]
    if not len(i): continue
    Wl = 1 / (D[i] ** 4 + 1e-12); Wl /= Wl.sum(1, keepdims=True)
    body_share = band(z[i], 0.32, 0.45) * np.where(np.abs(x[i]) < 0.05, 1.0, 0.75)
    body_share = np.maximum(body_share, band(z[i], 0.36, 0.40) * (np.abs(x[i]) < 0.045))   # belly / chest stays on the body
    W[i] *= body_share[:, None]
    for k, n in enumerate(bn): W[i, NI[n]] += Wl[:, k] * (1 - body_share)
# tail
d1 = seg_dist(V, *segs["Tail1"]); d2 = seg_dist(V, *segs["Tail2"])
tail_m = (np.minimum(d1, d2) < 0.05) & (y > 0.2) & (z > 0.29) & (np.abs(x) < 0.06)
i = np.where(tail_m)[0]
Wt = np.stack([1 / (d1[i] ** 4 + 1e-12), 1 / (d2[i] ** 4 + 1e-12)], 1); Wt /= Wt.sum(1, keepdims=True)
share = band(y[i], 0.2, 0.225)
W[i] *= (1 - share)[:, None]; W[i, NI["Tail1"]] += Wt[:, 0] * share; W[i, NI["Tail2"]] += Wt[:, 1] * share
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
PAWS = [f"{p}.{s}" for p in ("FrontHoof", "BackHoof") for s in ("L", "R")]
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
    """Quick 0 -> 1 -> 0 pulse centred on phase `at`."""
    return max(0.0, 1 - abs(((p - at + 0.5) % 1.0) - 0.5) / w)

# hooves back under the hips: the model stands with one hind leg forward, one back
NEUTRAL = {}
for pre in ("F", "B"):
    for sfx in ("L", "R"):
        fy = FOOT[pre + sfx][1]; fy0 = (FOOT[pre + "L"][1] + FOOT[pre + "R"][1]) / 2
        js = leg_joints(pre, sfx); h = js[0][2]
        NEUTRAL[f"{LEGB[pre][0]}.{sfx}"] = [("x", -math.degrees(math.atan2(fy - fy0, h)))]
        NEUTRAL[f"{LEGB[pre][2]}.{sfx}"] = [("x", math.degrees(math.atan2(fy - fy0, h)) * 0.6)]
def withn(r): return merge(NEUTRAL, r)

# ----------------------------------------------------------------------------------------- Idle
def idle(p):
    br = math.sin(TAU * 4 * p)                                          # four breaths per loop
    look = curve([(0.0, 0), (0.15, 0), (0.24, 22), (0.42, 22), (0.5, -10), (0.66, -10), (0.76, 0)], p)
    lift = curve([(0.0, 0), (0.2, 0), (0.26, -6), (0.4, -6), (0.48, 0)], p)   # head comes up while looking
    eL = flick(p, 0.55) * 30 + flick(p, 0.585, 0.015) * 18; eR = flick(p, 0.86) * 26
    tail = flick(p, 0.32, 0.03) + flick(p, 0.36, 0.03) + flick(p, 0.9, 0.03)
    shift = math.sin(TAU * p)
    r = {"Spine": [("x", -0.6 * br)], "Chest": [("x", 0.5 * br), ("y", 1.2 * shift)], "Hips": [("y", -1.2 * shift)],
         "Neck1": [("z", look * 0.3), ("x", lift * 0.5)], "Neck2": [("z", look * 0.3), ("x", lift * 0.5)],
         "Head": [("z", look * 0.4), ("x", 0.8 * br + flick(p, 0.62, 0.05) * 6)],
         "Ear.L": [("y", eL)], "Ear.R": [("y", -eR)],
         "Tail1": [("x", -35 * tail)], "Tail2": [("x", -20 * tail)]}
    return withn(r), {}

# --------------------------------------------------------------------------------------- gaits
# per-leg phase 0 = touch-down. +x moves a leg bone's tip backward.
WALK = dict(fu=[(0.0, -19), (0.6, 18), (0.74, 6), (0.9, -23)], ff=[(0.0, 0), (0.55, 4), (0.68, -18), (0.82, -14), (0.94, 0)],
            fc=[(0.0, 0), (0.52, 4), (0.66, 70), (0.8, 40), (0.93, 0)], fh=[(0.0, 0), (0.55, 18), (0.68, 40), (0.84, 4)],
            bt=[(0.0, -18), (0.6, 20), (0.76, 4), (0.9, -22)], bs=[(0.0, 0), (0.55, -4), (0.68, 22), (0.8, 18), (0.94, 0)],
            bc=[(0.0, 0), (0.55, 6), (0.68, -38), (0.82, -24), (0.94, 0)], bh=[(0.0, 0), (0.55, 20), (0.68, 30), (0.86, 4)])
RUN = dict(fu=[(0.0, -26), (0.36, 26), (0.55, 10), (0.8, -36)], ff=[(0.0, 0), (0.32, 6), (0.48, -26), (0.66, -24), (0.86, 4)],
           fc=[(0.0, 0), (0.3, 10), (0.46, 95), (0.62, 70), (0.82, 0)], fh=[(0.0, 0), (0.32, 24), (0.46, 50), (0.75, 6)],
           bt=[(0.0, -32), (0.36, 30), (0.55, 10), (0.82, -40)], bs=[(0.0, 2), (0.32, -8), (0.48, 26), (0.66, 22), (0.86, 2)],
           bc=[(0.0, 0), (0.32, 10), (0.48, -50), (0.68, -30), (0.88, 0)], bh=[(0.0, 0), (0.32, 26), (0.48, 36), (0.8, 6)])

def legs(p, G, ph):
    r = {}
    for sfx, (pf, pb) in ph.items():
        qf, qb = p - pf, p - pb
        for bn, k in zip(LEGB["F"], ("fu", "ff", "fc", "fh")): r[f"{bn}.{sfx}"] = [("x", curve(G[k], qf))]
        for bn, k in zip(LEGB["B"], ("bt", "bs", "bc", "bh")): r[f"{bn}.{sfx}"] = [("x", curve(G[k], qb))]
    return r

def walk(p):
    # lateral-sequence walk: BL 0, FL 0.25, BR 0.5, FR 0.75; the head nods twice per stride
    r = legs(p, WALK, {"L": (0.25, 0.0), "R": (0.75, 0.5)})
    sw = math.sin(TAU * p); nod = math.sin(TAU * 2 * p + 0.8)
    r = merge(r, {"Hips": [("y", 2.5 * sw), ("z", 2 * sw)], "Chest": [("y", -2.5 * sw), ("z", -2 * sw)],
                  "Neck1": [("x", 2.5 * nod)], "Neck2": [("x", 1.5 * nod)], "Head": [("x", -3 * nod), ("z", 1.5 * sw)],
                  "Ear.L": [("y", 4 * nod)], "Ear.R": [("y", -4 * nod)], "Tail1": [("z", 6 * sw)], "Tail2": [("z", 8 * math.sin(TAU * p - 0.8))]})
    return withn(r), {}

def run(p):
    # bounding gallop: hind pair pushes (0.0/0.05), front pair lands half a stride later; spine flexes,
    # neck stretches forward, tail up (white flag)
    r = legs(p, RUN, {"L": (0.5, 0.0), "R": (0.56, 0.06)})
    flex = math.sin(TAU * (p - 0.2))
    r = merge(r, {"Hips": [("x", 8 * flex)], "Spine": [("x", -5 * flex)], "Chest": [("x", -7 * flex)],
                  "Neck1": [("x", 22 - 5 * flex)], "Neck2": [("x", 10 - 4 * flex)], "Head": [("x", -14 + 7 * flex)],
                  "Ear.L": [("x", 25)], "Ear.R": [("x", 25)],
                  "Tail1": [("x", -60)], "Tail2": [("x", -25 + 6 * flex)]})
    return withn(r), {}

# -------------------------------------------------------------------------------------- grazing
GRAZE = {"Spine": [("x", 5)], "Chest": [("x", 9)], "Neck1": [("x", 78)], "Neck2": [("x", 22)], "Head": [("x", -18)],
         "FrontUpper.L": [("x", -22), ("y", -6)], "FrontUpper.R": [("x", 10), ("y", 6)], "FrontFore.L": [("x", 10)], "FrontCannon.L": [("x", 6)],
         "FrontFore.R": [("x", -6)], "FrontCannon.R": [("x", 8)],
         "Ear.L": [("x", -12), ("y", 10)], "Ear.R": [("x", -12), ("y", -10)]}
def graze_down(t):
    a = track([(0, 0), (0.12, -0.06), (0.72, 1.04), (0.86, 0.98), (1, 1.0)], t)   # glance up first, overshoot, settle
    r = lerp_pose({}, GRAZE, max(a, 0))
    if a < 0: r = merge(r, {"Neck2": [("x", 60 * a)], "Head": [("x", 40 * a)]})
    return withn(r), {}

def graze(p):
    nib = sum(flick(p, c, 0.025) for c in (0.08, 0.14, 0.2, 0.44, 0.5, 0.75, 0.81, 0.87))   # tugs at the grass
    chew = math.sin(TAU * 24 * p) * (0.5 + 0.5 * math.sin(TAU * p))
    up = curve([(0.0, 0), (0.25, 0), (0.3, 1), (0.38, 1), (0.43, 0)], p)                # quick check around, back down
    eL = flick(p, 0.6) * 25; eR = flick(p, 0.33) * 30 + flick(p, 0.95) * 20
    tail = flick(p, 0.66, 0.03) + flick(p, 0.7, 0.03)
    r = lerp_pose({}, GRAZE, 1)
    r = merge(r, {"Neck1": [("x", -14 * up), ("z", 4 * math.sin(TAU * 2 * p))], "Neck2": [("x", -10 * up)],
                  "Head": [("x", 6 * nib - 8 * up + 1.2 * chew), ("z", 10 * up)],
                  "Ear.L": [("y", eL)], "Ear.R": [("y", -eR)], "Tail1": [("x", -35 * tail)], "Tail2": [("x", -20 * tail)],
                  "Chest": [("y", 1.5 * math.sin(TAU * p))]})
    return withn(r), {}

acts = {
    "Idle": make_action("Idle", 180, idle),
    "Walk": make_action("Walk", 28, walk),
    "Run": make_action("Run", 16, run, lift=lambda p: 0.06 * S * max(0.0, math.sin(TAU * (p - 0.2))) ** 1.5),
    "GrazeDown": make_action("GrazeDown", 30, graze_down, loop=False),
    "Graze": make_action("Graze", 150, graze),
}

def clip_speed(act):
    rig.animation_data.action = act; n = int(act.frame_range[1]); sc = bpy.context.scene
    ys, zs = [], []
    for f in range(n + 1):
        sc.frame_set(f); bpy.context.view_layer.update()
        hd = PB["FrontHoof.L"].head; ys.append(hd.y); zs.append(hd.z - PAW_REST["FrontHoof.L"][0])
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
                f = round(i * n / 6) if an not in ("GrazeDown",) else round(i * n / 5)
                sc.frame_set(f)
                p = os.path.join(RENDER, f"{an}_{view}_{i}_f{f:03d}.png")
                sc.render.filepath = p
                bpy.ops.render.render(write_still=True)
                files.append(p)
    print("RENDERS " + json.dumps(len(files)))
