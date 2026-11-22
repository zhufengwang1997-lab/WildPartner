"""Process one Tripo GLB for Unity (headless, PRD-补充 §4):

    blender -b -P tools/blender/process_tripo.py -- <in.glb> <out_dir> <Name> <size> <lod_tris> [preview.png] [yaw_deg]

  size      "h=9"  -> bounding-box height 9 m;  "l=16" -> longest horizontal side 16 m
  lod_tris  comma list, e.g. "6000,1500,300" -> meshes <Name>_LOD0.. (Unity builds the LODGroup from the names)
  yaw_deg   optional extra rotation about Z (to make the model's front face -Y = Unity +Z)

Steps: import -> join -> drop loose specks -> scale -> pivot at the centre of the ground contact (the trunk
of a tree, not its canopy) -> decimate per LOD (collapse, keeps the silhouette and UVs) -> base-colour
texture kept (max 2048 px) -> FBX + PNG. Prints one JSON line (RESULT ...) and renders a preview.
"""
import bpy, bmesh, sys, os, json, math
import numpy as np
from mathutils import Vector, Matrix

argv = sys.argv[sys.argv.index("--") + 1:]
SRC, OUT, NAME, SIZE, LODS = argv[0], argv[1], argv[2], argv[3], [int(t) for t in argv[4].split(",")]
PREVIEW = argv[5] if len(argv) > 5 and argv[5] else None
YAW = float(argv[6]) if len(argv) > 6 else 0.0
os.makedirs(OUT, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=SRC)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
bpy.ops.object.select_all(action="DESELECT")
for o in meshes: o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1: bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)
for o in list(bpy.context.scene.objects):
    if o != obj: bpy.data.objects.remove(o)
if YAW:
    obj.data.transform(Matrix.Rotation(math.radians(YAW), 4, "Z"))

def tris(o): return sum(len(p.vertices) - 2 for p in o.data.polygons)
tris_in = tris(obj)

# Tripo meshes are split into thousands of pieces along UV seams (duplicate vertices at the same spot).
# Decimating them separately opens cracks between pieces, so weld coincident vertices first; UVs are
# per face-corner and survive the weld.
_co = np.array([v.co[:] for v in obj.data.vertices])
_weld = float((_co.max(0) - _co.min(0)).max()) * 2e-5
bm = bmesh.new(); bm.from_mesh(obj.data)
bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=_weld)
bm.to_mesh(obj.data); bm.free(); obj.data.update()

# scale
co = np.array([v.co[:] for v in obj.data.vertices])
mn, mx = co.min(0), co.max(0)
kind, val = SIZE.split("=")
ext = mx - mn
s = float(val) / (ext[2] if kind == "h" else max(ext[0], ext[1]))
obj.data.transform(Matrix.Scale(s, 4))
co *= s; mn *= s; mx *= s
# pivot: centre of the vertices in the lowest 3 % of the height (ground contact), at the lowest point
low = co[co[:, 2] < mn[2] + 0.03 * (mx[2] - mn[2])]
cx, cy = (low[:, 0].mean(), low[:, 1].mean()) if len(low) > 20 else ((mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2)
if kind == "l":   # long pieces (bridge, fence, book): the feet are not the centre, use the footprint centre
    cx, cy = (mn[0] + mx[0]) / 2, (mn[1] + mx[1]) / 2
obj.data.transform(Matrix.Translation((-cx, -cy, -mn[2])))
obj.data.update()

# wind weight in vertex colour R (Toon shader _WIND): 0 on the trunk / ground, 1 at the canopy tips
if NAME.startswith("Tree") or NAME == "Bush":
    H_ = float(mx[2] - mn[2]); lo, hi = (0.28, 0.95) if NAME.startswith("Tree") else (0.05, 1.0)
    ca = obj.data.color_attributes.new("Col", "FLOAT_COLOR", "POINT")
    for v in obj.data.vertices:
        t = min(max((v.co.z / H_ - lo) / (hi - lo), 0.0), 1.0)
        r = math.sqrt(v.co.x ** 2 + v.co.y ** 2) / max(H_ * 0.4, 1e-3)   # outer leaves flutter more
        w = t * t * (3 - 2 * t) * (0.6 + 0.4 * min(r, 1.0))
        ca.data[v.index].color = (w, 0.0, 0.0, 1.0)
    obj.data.color_attributes.active_color = ca

# base colour -> PNG (max 2048)
tex_path = None
for slot in obj.material_slots:
    mat = slot.material
    if not mat or not mat.use_nodes: continue
    for n in mat.node_tree.nodes:
        if n.type == "BSDF_PRINCIPLED":
            links = n.inputs["Base Color"].links
            if links and links[0].from_node.type == "TEX_IMAGE":
                img = links[0].from_node.image
                w, hh = img.size
                if max(w, hh) > 2048: img.scale(2048 * w // max(w, hh), 2048 * hh // max(w, hh))
                tex_path = os.path.join(OUT, f"{NAME}_BaseColor.png")
                img.filepath_raw = tex_path; img.file_format = "PNG"; img.save()
                break
    if tex_path: break

# LODs: copies decimated to the requested triangle counts
lods = []
for i, target in enumerate(LODS):
    o = obj.copy(); o.data = obj.data.copy(); bpy.context.scene.collection.objects.link(o)
    o.name = f"{NAME}_LOD{i}"; o.data.name = o.name
    t = tris(o)
    if t > target:
        bpy.context.view_layer.objects.active = o
        m = o.modifiers.new("dec", "DECIMATE"); m.ratio = target / t; m.use_collapse_triangulate = True
        bpy.ops.object.select_all(action="DESELECT"); o.select_set(True)
        bpy.ops.object.modifier_apply(modifier=m.name)
    lods.append(o)
bpy.data.objects.remove(obj)

bpy.ops.object.select_all(action="DESELECT")
for o in lods: o.select_set(True)
fbx = os.path.join(OUT, f"{NAME}.fbx")
bpy.ops.export_scene.fbx(filepath=fbx, use_selection=True, object_types={"MESH"},
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", path_mode="STRIP", embed_textures=False,
                         mesh_smooth_type="FACE", bake_anim=False, colors_type="LINEAR")
size = [round(float(v), 2) for v in (mx - mn)]
print("RESULT " + json.dumps({"name": NAME, "tris_in": tris_in, "lods": [tris(o) for o in lods], "size_m": size,
                              "texture": bool(tex_path)}))

if PREVIEW:
    for o in lods[1:]: o.hide_render = True
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_WORKBENCH"
    sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "TEXTURE"
    sc.render.resolution_x, sc.render.resolution_y = 420, 420
    sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.86, 0.87, 0.9)
    H = max(size)
    cd = bpy.data.cameras.new("c"); cd.type = "ORTHO"; cd.ortho_scale = H * 1.15
    cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
    d = Vector((0.6, -1, 0.35)).normalized()   # front-left 3/4: the -Y side is the model's front
    cam.location = Vector((0, 0, size[2] * 0.5)) + d * H * 3
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = PREVIEW
    bpy.ops.render.render(write_still=True)
