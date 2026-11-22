"""Cut the front door out of the Tripo cabin so it can swing open in game (试玩反馈 1123b-3):

    blender -b -P tools/blender/cut_cabin_door.py -- Assets/_Game/Models/Tripo/Cabin.fbx

The cabin's door is part of the Tripo mesh (front face, Blender -Y). The faces of the door leaf (inside the
arched opening, on the door plane, facing out) are split off LOD0 into their own object `Cabin_Door` whose
origin sits on the hinge (the side opposite the ring handle), so Unity only has to rotate it about local up.
LOD1/LOD2 keep the closed door: they are only seen from far away, and the door is only open for a moment.
Re-running on an already cut file does nothing (it checks for Cabin_Door). Writes the FBX in place with the
same export settings as process_tripo.py.
"""
import bpy, bmesh, sys, math
from mathutils import Vector

path = sys.argv[sys.argv.index("--") + 1]
bpy.ops.wm.read_factory_settings(use_empty=True)
bpy.ops.import_scene.fbx(filepath=path)
objs = {o.name: o for o in bpy.context.scene.objects if o.type == "MESH"}
if "Cabin_Door" in objs:
    print("CUT already done"); sys.exit(0)
lod0 = objs["Cabin_LOD0"]

# measured on the model (orthographic front render + ray casts): the door plane is at y = -2.31, the leaf spans
# x = ±0.68, from the threshold z = 0.60 up to an arch of radius 0.68 centred at z = 1.82
HALF_W, Z0, ARCH_C, PLANE_Y = 0.68, 0.58, 1.82, -2.31
HINGE_X = HALF_W   # ring handle is on the left (x < 0) seen from the front, so the hinge is on the right

def in_leaf(c):
    if abs(c.x) > HALF_W - 0.02 or c.z < Z0: return False
    top = ARCH_C + math.sqrt(max(0.0, (HALF_W - 0.02) ** 2 - c.x * c.x))
    return c.z < top and -2.62 < c.y < -2.12   # the leaf is a slab with relief (planks, ring): all of its faces

mw = lod0.matrix_world
bpy.context.view_layer.objects.active = lod0
bpy.ops.object.select_all(action="DESELECT"); lod0.select_set(True)
bpy.ops.object.mode_set(mode="EDIT")
bm = bmesh.from_edit_mesh(lod0.data)
nm = mw.to_3x3()
count = 0
for f in bm.faces:
    c = mw @ f.calc_center_median()
    n = (nm @ f.normal).normalized()
    f.select = in_leaf(c)
    count += f.select
bmesh.update_edit_mesh(lod0.data)
print("CUT faces", count)
bpy.ops.mesh.separate(type="SELECTED")
bpy.ops.object.mode_set(mode="OBJECT")
door = [o for o in bpy.context.selected_objects if o != lod0][0]
door.name = "Cabin_Door"; door.data.name = "Cabin_Door"

# origin on the hinge line, at the threshold
bpy.context.scene.cursor.location = Vector((HINGE_X, PLANE_Y, Z0))
bpy.ops.object.select_all(action="DESELECT"); door.select_set(True)
bpy.context.view_layer.objects.active = door
bpy.ops.object.origin_set(type="ORIGIN_CURSOR")
mn = Vector((1e9,) * 3); mx = Vector((-1e9,) * 3)
for v in door.data.vertices:
    w = door.matrix_world @ v.co
    mn = Vector(map(min, mn, w)); mx = Vector(map(max, mx, w))
print("DOOR bounds", [round(x, 2) for x in mn], [round(x, 2) for x in mx], "origin", [round(x, 2) for x in door.location])

bpy.ops.object.select_all(action="SELECT")
bpy.ops.export_scene.fbx(filepath=path, use_selection=True, object_types={"MESH"},
                         apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                         axis_forward="-Z", axis_up="Y", path_mode="STRIP", embed_textures=False,
                         mesh_smooth_type="FACE", bake_anim=False, colors_type="LINEAR")
print("CUT wrote", path)
