"""Render every frame of each action in a rigged .blend (3/4 view) for animated previews:
    blender -b Art/Rigged/<Name>_rig.blend -P tools/blender/render_anim_gif.py -- <out_dir> <height_m> [Action:step ...] [view=q|side]
Frames land in <out_dir>/<Action>/NNN.png; tools/make_gif.py turns them into GIFs."""
import bpy, sys, os
from mathutils import Vector
argv = sys.argv[sys.argv.index("--") + 1:]
OUT, H = argv[0], float(argv[1])
opts = [a for a in argv[2:] if "=" in a]; VIEW = dict(o.split("=") for o in opts).get("view", "q")
steps = dict(a.split(":") for a in argv[2:] if ":" in a)
sc = bpy.context.scene
rig = next(o for o in sc.objects if o.type == "ARMATURE")
sc.render.engine = "BLENDER_WORKBENCH"
sc.display.shading.light = "STUDIO"; sc.display.shading.color_type = "TEXTURE"
sc.render.resolution_x, sc.render.resolution_y = 300, 400
sc.world = sc.world or bpy.data.worlds.new("w"); sc.world.color = (0.86, 0.87, 0.9)
cd = bpy.data.cameras.new("cam"); cd.type = "ORTHO"; cd.ortho_scale = H * 1.25
cam = bpy.data.objects.new("cam", cd); sc.collection.objects.link(cam); sc.camera = cam
bpy.ops.mesh.primitive_plane_add(size=H * 3, location=(0, 0, 0))
d = (Vector((1, 0.35, 0.08)) if VIEW == "side" else Vector((0.75, -1, 0.18))).normalized()
cam.location = Vector((0, 0, H * 0.5)) + d * H * 4
cam.rotation_euler = (-d).to_track_quat("-Z", "Y").to_euler()
for act in bpy.data.actions:
    st = int(steps.get(act.name, 1))
    rig.animation_data.action = act
    os.makedirs(os.path.join(OUT, act.name), exist_ok=True)
    for f in range(0, int(act.frame_range[1]), st):
        sc.frame_set(f)
        sc.render.filepath = os.path.join(OUT, act.name, f"{f:03d}.png")
        bpy.ops.render.render(write_still=True)
