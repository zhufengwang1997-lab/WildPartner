"""Rig + animate the two small creatures in Blender (headless). No Tripo rigging is used.

    blender -b -P tools/blender/rig_small.py -- <butterfly|koi> <in.glb> <out_dir> <name> <height_m> <max_tris> [render_dir]

butterfly (月影蝶, upright little fairy with the wings spread behind it, facing -Y already):
  body, head (antennae ride on it), one bone per wing hinged on a vertical axis at the shoulders.
  Idle = hover (slow beats, body bobbing against the beat), Walk = cruise, Run = fast flutter leaning forward.
koi (赤墨锦鲤, lying diagonally): turned along its long axis (head = the thicker end) to face -Y; a 5-bone
  spine from snout to tail tip with tent weights along the body (fins follow the nearest bone).
  Idle = slow drift (small wave), Walk = swim, Run = dart (fast, large tail beats). The wave travels from
  head to tail, growing towards the tail.
Ground speeds are design values (written to <name>_speeds.json) since these clips are not foot-planted.
"""

import bpy, sys, os, math, json
import numpy as np
from mathutils import Vector, Quaternion, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
MODE, SRC, OUT, NAME, HEIGHT, MAX_TRIS = argv[0], argv[1], argv[2], argv[3], float(argv[4]), int(argv[5])
RENDER = argv[6] if len(argv) > 6 else None
ROOTB = "Body" if MODE == "butterfly" else "Spine0"   # bone that carries the clip's height offset
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
c0 = (mn + mx) / 2
body.data.transform(Matrix.Translation((-c0[0], -c0[1], -mn[2]))); body.data.transform(Matrix.Scale(1 / H0, 4))
co = verts()
if MODE == "koi":
    xy = co[:, :2] - co[:, :2].mean(0)
    ev, evec = np.linalg.eigh(np.cov(xy.T)); ax = evec[:, np.argmax(ev)]
    t = xy @ ax
    # head = the end whose slice is fuller (the tail is thin fins): compare vertex spread near each end
    def fullness(sel): s = co[sel]; return np.ptp(s[:, 2]) * len(s) ** 0 if len(s) else 0
    lo, hi = np.quantile(t, 0.2), np.quantile(t, 0.8)
    a_lo = co[t < lo]; a_hi = co[t > hi]
    perp = np.array([-ax[1], ax[0]])
    def thick(a): return np.std((a[:, :2] - co[:, :2].mean(0)) @ perp) + np.std(a[:, 2])
    head_dir = -ax if thick(a_lo) > thick(a_hi) else ax                 # unit vector towards the head
    ang = math.atan2(head_dir[1], head_dir[0]) - math.atan2(-1, 0)
    body.data.transform(Matrix.Rotation(-ang, 4, "Z"))
    co = verts(); mn, mx = co.min(0), co.max(0)
    body.data.transform(Matrix.Translation((-(mn[0] + mx[0]) / 2, -(mn[1] + mx[1]) / 2, 0)))
    co = verts()
    print("ORIENT " + json.dumps({"turn_deg": round(math.degrees(ang), 1)}))
V = co.copy(); x, y, z = V[:, 0], V[:, 1], V[:, 2]
mn, mx = V.min(0), V.max(0)
print("EXTENT " + json.dumps(np.round(np.stack([mn, mx]), 3).tolist()))
S = HEIGHT
body.data.transform(Matrix.Scale(S, 4)); body.data.update(); body.name = NAME
def P(p): return Vector((float(p[0]) * S, float(p[1]) * S, float(p[2]) * S))

arm_data = bpy.data.armatures.new(NAME + "_Rig")
rig = bpy.data.objects.new(NAME + "_Rig", arm_data)
bpy.context.scene.collection.objects.link(rig)
bpy.context.view_layer.objects.active = rig
bpy.ops.object.mode_set(mode="EDIT")
eb = arm_data.edit_bones
def bone(name, head, tail, parent=None, connect=False):
    b = eb.new(name); b.head = P(head); b.tail = P(tail); b.roll = 0
    if parent: b.parent = eb[parent]; b.use_connect = connect
    return b
bone("Root", (0, 0, 0), (0, 0, 0.1))
if MODE == "butterfly":
    bone("Body", (0, 0, 0.12), (0, 0, 0.47), "Root")
    bone("Head", (0, 0, 0.47), (0, 0, 0.8), "Body", True)
    bone("Wing.L", (0.07, 0.02, 0.45), (0.48, 0.02, 0.6), "Body")
    bone("Wing.R", (-0.07, 0.02, 0.45), (-0.48, 0.02, 0.6), "Body")
else:
    zc = float(np.median(z[np.abs(x) < 0.08]))
    ys = np.linspace(mn[1], mx[1], 6)
    prev = "Root"
    for k in range(5):
        bone(f"Spine{k}", (0, ys[k], zc), (0, ys[k + 1], zc), prev, k > 0); prev = f"Spine{k}"
bpy.ops.object.mode_set(mode="OBJECT")
arm_data.bones["Root"].use_deform = False
DEFORM = [b.name for b in arm_data.bones if b.name != "Root"]
names = DEFORM; NI = {n: i for i, n in enumerate(names)}
def band(v, lo, hi): return np.clip((v - lo) / (hi - lo), 0, 1)
W = np.zeros((len(V), len(names)))
if MODE == "butterfly":
    head = band(z, 0.46, 0.52) * (np.abs(x) < 0.16)
    wl = band(x, 0.06, 0.12) * (y > -0.06); wr = band(-x, 0.06, 0.12) * (y > -0.06)
    W[:, NI["Body"]] = 1
    W[:, NI["Head"]] = head; W[:, NI["Body"]] *= (1 - head)
    for n, w in (("Wing.L", wl), ("Wing.R", wr)):
        W *= (1 - w)[:, None]; W[:, NI[n]] += w
else:
    ys = np.linspace(mn[1], mx[1], 6); cen = (ys[:-1] + ys[1:]) / 2; step = cen[1] - cen[0]
    for k in range(5):
        W[:, NI[f"Spine{k}"]] = np.clip(1 - np.abs(y - cen[k]) / step, 0, 1)
    W[y <= cen[0], NI["Spine0"]] = 1; W[y >= cen[-1], NI["Spine4"]] = 1
W /= np.maximum(W.sum(1, keepdims=True), 1e-9)
for n in names: body.vertex_groups.new(name=n)
vg = [body.vertex_groups[n] for n in names]
for vi in range(len(V)):
    row = W[vi]
    for k in np.nonzero(row > 0.02)[0]: vg[k].add([vi], float(row[k]), "REPLACE")
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
        l = dict(l); hv = Vector(l.get(ROOTB, Vector())); hv.z += h[f % n]; l[ROOTB] = hv
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

def fly(beats, amp, lean, bob):
    """`beats` wing beats per loop; wings sweep back (tips towards +Y) on the downstroke and open again;
    the body rises on each downstroke and sinks between (bob against the beat), head counter-nods."""
    def f(p):
        ph = TAU * beats * p
        w = 0.5 - 0.5 * math.cos(ph)                     # 0 open .. 1 swept back
        r = {"Wing.L": [("z", -8 + amp * w), ("x", -6 * w)], "Wing.R": [("z", 8 - amp * w), ("x", -6 * w)],
             "Body": [("x", lean + 3 * math.sin(ph - 1.2)), ("y", 4 * math.sin(TAU * p))],
             "Head": [("x", -lean * 0.6 - 3 * math.sin(ph - 1.6)), ("z", 6 * math.sin(TAU * p + 1))]}
        return r, {"Body": Vector((0, 0, bob * S * math.sin(ph - 1.0)))}
    return f

def swim(amp, lag, wiggle=0.0):
    def f(p):
        r = {}
        for k in range(5):
            a = amp * (0.25 + 0.3 * k)
            r[f"Spine{k}"] = [("z", a * math.sin(TAU * (p - lag * k)) + (wiggle * math.sin(TAU * 2 * p) if k == 0 else 0)),
                              ("x", 1.5 * math.sin(TAU * (p - lag * k) + 1))]
        return r, {}
    return f

if MODE == "butterfly":
    acts = {"Idle": make_action("Idle", 48, fly(4, 62, 4, 0.025), ground=None),
            "Walk": make_action("Walk", 20, fly(2, 70, 14, 0.02), ground=None),
            "Run": make_action("Run", 12, fly(2, 76, 24, 0.015), ground=None)}
    SPEEDS = {"Walk": 1.5, "Run": 4.5}
else:
    acts = {"Idle": make_action("Idle", 90, swim(6, 0.12, 2), ground=None),
            "Walk": make_action("Walk", 36, swim(11, 0.14), ground=None),
            "Run": make_action("Run", 16, swim(17, 0.16), ground=None)}
    SPEEDS = {"Walk": 1.2, "Run": 4.0}
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
