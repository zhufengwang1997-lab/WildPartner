"""Batch-clean Tripo GLB models for Unity (run headless):

    blender -b -P tools/blender/process_glb.py -- <in.glb> <out_dir> <name> <target_height_m> <max_tris>

Steps: import GLB -> join meshes -> decimate to <= max_tris -> scale so the bounding box height is
<target_height_m> -> origin at bottom centre, facing -Y in Blender (= +Z forward in Unity) -> export
FBX (mesh only) + the base-colour texture as PNG (max 2048 px). Prints one JSON line with stats.
"""
import bpy, sys, os, json, math
from mathutils import Vector

argv = sys.argv[sys.argv.index("--") + 1:]
src, out_dir, name, target_h, max_tris = argv[0], argv[1], argv[2], float(argv[3]), int(argv[4])
os.makedirs(out_dir, exist_ok=True)

bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.gltf(filepath=src)
meshes = [o for o in bpy.context.scene.objects if o.type == "MESH"]
bpy.ops.object.select_all(action="DESELECT")
for o in meshes:
    o.select_set(True)
bpy.context.view_layer.objects.active = meshes[0]
if len(meshes) > 1:
    bpy.ops.object.join()
obj = bpy.context.view_layer.objects.active
# Unparent (glTF importer may add an empty root) keeping the world transform, then bake it.
bpy.ops.object.parent_clear(type="CLEAR_KEEP_TRANSFORM")
bpy.ops.object.transform_apply(location=True, rotation=True, scale=True)

def tri_count(o):
    return sum(len(p.vertices) - 2 for p in o.data.polygons)

tris_in = tri_count(obj)
if tris_in > max_tris:
    m = obj.modifiers.new("dec", "DECIMATE")
    m.ratio = max_tris / tris_in
    bpy.ops.object.modifier_apply(modifier=m.name)
tris_out = tri_count(obj)

# Scale to target height and put the origin at the bottom centre.
bb = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
mn = Vector((min(v.x for v in bb), min(v.y for v in bb), min(v.z for v in bb)))
mx = Vector((max(v.x for v in bb), max(v.y for v in bb), max(v.z for v in bb)))
h = mx.z - mn.z
s = target_h / h if h > 0 else 1.0
obj.scale = (s, s, s)
bpy.ops.object.transform_apply(scale=True)
bb = [obj.matrix_world @ Vector(c) for c in obj.bound_box]
cx = (min(v.x for v in bb) + max(v.x for v in bb)) / 2
cy = (min(v.y for v in bb) + max(v.y for v in bb)) / 2
mz = min(v.z for v in bb)
bpy.context.scene.cursor.location = (cx, cy, mz)
bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
obj.location = (0, 0, 0)
obj.name = name
size = [round(max(v[i] for v in bb) - min(v[i] for v in bb), 3) for i in range(3)]

# Base-colour texture -> PNG.
tex_path = None
for slot in obj.material_slots:
    mat = slot.material
    if not mat or not mat.use_nodes:
        continue
    for n in mat.node_tree.nodes:
        if n.type == "BSDF_PRINCIPLED":
            links = n.inputs["Base Color"].links
            if links and links[0].from_node.type == "TEX_IMAGE":
                img = links[0].from_node.image
                w, hh = img.size
                if max(w, hh) > 2048:
                    img.scale(2048 * w // max(w, hh), 2048 * hh // max(w, hh))
                tex_path = os.path.join(out_dir, f"{name}_BaseColor.png")
                img.filepath_raw = tex_path
                img.file_format = "PNG"
                img.save()
                break
    if tex_path:
        break

fbx_path = os.path.join(out_dir, f"{name}.fbx")
bpy.ops.export_scene.fbx(filepath=fbx_path, use_selection=False, object_types={"MESH"},
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", path_mode="STRIP", embed_textures=False,
                         mesh_smooth_type="FACE", bake_anim=False)
print("RESULT " + json.dumps({"name": name, "tris_in": tris_in, "tris_out": tris_out, "size_m": size,
                              "fbx": fbx_path, "texture": tex_path}, ensure_ascii=False))
