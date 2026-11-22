"""Exaggerate the hair/skirt bones of a rigged .blend and render, to check what each region drags along:
    blender -b Art/Rigged/<Name>_rig.blend -P tools/blender/test_secondary.py -- <out.png> <height_m>"""
import bpy, sys, os, math
from mathutils import Vector, Quaternion
argv = sys.argv[sys.argv.index("--") + 1:]; OUT, H = argv[0], float(argv[1])
sc = bpy.context.scene; rig = next(o for o in sc.objects if o.type == "ARMATURE")
rig.animation_data.action = None
for pb in rig.pose.bones: pb.rotation_mode = "QUATERNION"; pb.rotation_quaternion = Quaternion(); pb.location = Vector()
def rot(n, axis, deg):
    b = rig.data.bones[n]; a = b.matrix_local.to_3x3().inverted() @ Vector(axis)
    rig.pose.bones[n].rotation_quaternion = Quaternion(a.normalized(), math.radians(deg))
for s in ("L", "R"):
    for nm, a in (("HairA", 14), ("HairB", 12), ("HairC", 10)): rot(f"{nm}.{s}", (1, 0, 0), a)
rot("Skirt.B", (1, 0, 0), 20); rot("Skirt.F", (1, 0, 0), -16); rot("Skirt.L", (0, 1, 0), -20); rot("Skirt.R", (0, 1, 0), 20)
sc.render.engine = "BLENDER_WORKBENCH"; sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "TEXTURE"
sc.render.resolution_x, sc.render.resolution_y = 500, 640
sc.world = bpy.data.worlds.new("w"); sc.world.color = (0.86, 0.87, 0.9)
cd = bpy.data.cameras.new("c"); cd.type = "ORTHO"; cd.ortho_scale = H * 1.25
cam = bpy.data.objects.new("c", cd); sc.collection.objects.link(cam); sc.camera = cam
base, ext = os.path.splitext(OUT)
for view, d in (("back", Vector((0.6, 1, 0.15))), ("side", Vector((1, 0, 0.05))), ("front", Vector((-0.5, -1, 0.1)))):
    d.normalize(); cam.location = Vector((0, 0, H * 0.5)) + d * H * 4
    cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
    sc.render.filepath = f"{base}_{view}{ext}"; bpy.ops.render.render(write_still=True)
