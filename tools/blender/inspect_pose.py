"""Render a model from front / side / top / 3-4 (orthographic, flat textured) to check rig-ability:
    blender -b -P tools/blender/inspect_pose.py -- <in.glb|fbx> <out.png> [label]
Prints one JSON line with dimensions, tris, mesh islands and non-manifold edge count."""
import bpy, sys, os, json, math, bmesh
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out = argv[0], argv[1]
bpy.ops.wm.read_factory_settings(use_empty=True)
if src.lower().endswith(".glb"):
    bpy.ops.import_scene.gltf(filepath=src)
else:
    bpy.ops.import_scene.fbx(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
dg = bpy.context.evaluated_depsgraph_get()
pts = [o.matrix_world @ Vector(c) for o in meshes for c in o.bound_box]
lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
size = hi - lo; ctr = (hi + lo) / 2
tris = islands = nonman = 0
for o in meshes:
    bm = bmesh.new(); bm.from_mesh(o.data)
    tris += sum(len(f.verts) - 2 for f in bm.faces)
    nonman += sum(1 for e in bm.edges if not e.is_manifold)
    seen = set()
    for v in bm.verts:
        if v.index in seen: continue
        islands += 1; stack = [v]
        while stack:
            x = stack.pop()
            if x.index in seen: continue
            seen.add(x.index)
            stack.extend(e.other_vert(x) for e in x.link_edges)
    bm.free()

sc = bpy.context.scene
sc.render.engine = "BLENDER_WORKBENCH"
sc.display.shading.light = "STUDIO"
sc.display.shading.color_type = "TEXTURE"
sc.display.shading.show_cavity = True
sc.render.resolution_x = sc.render.resolution_y = 512
sc.render.film_transparent = False
sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.9, 0.9, 0.92)
cam_data = bpy.data.cameras.new("cam"); cam_data.type = "ORTHO"
cam_data.ortho_scale = max(size) * 1.15
cam = bpy.data.objects.new("cam", cam_data); sc.collection.objects.link(cam); sc.camera = cam
d = max(size) * 3
views = {"front": (0, -1, 0), "side": (1, 0, 0), "top": (0, 0, 1), "three_quarter": (0.7, -0.7, 0.45), "back": (0, 1, 0)}
tmp = []
for name, v in views.items():
    v = Vector(v).normalized()
    cam.location = ctr + v * d
    cam.rotation_euler = (-v).to_track_quat("-Z", "Y" if name != "top" else "Y").to_euler()
    p = os.path.splitext(out)[0] + f"_{name}.png"
    sc.render.filepath = p; bpy.ops.render.render(write_still=True); tmp.append(p)
print("POSE " + json.dumps({"src": os.path.basename(src), "size_xyz": [round(x, 3) for x in size], "tris": tris, "islands": islands, "non_manifold_edges": nonman, "renders": tmp}, ensure_ascii=False))
