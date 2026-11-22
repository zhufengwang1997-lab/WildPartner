"""Procedurally builds every environment asset the Tripo set does not cover (PRD 4.2), headless:

    blender -b -P tools/blender/generate_assets.py -- <out_dir> [only_name ...]

Style: chunky stylised shapes (blob canopies, faceted rocks) that read well with WildPartner/Toon.
Conventions shared with Unity:
  * UVs point into the shared palette texture (tools/blender/palette.py) -> one material for everything
  * vertex colour R = wind sway weight (0 = rigid, 1 = leaf tips), used by the Toon shader's _WIND
  * objects named <Asset>_LOD0/1/2 -> Unity builds the LOD Group automatically
  * foliage uses custom "sphere" normals (pointing out from each canopy cluster) for soft round shading
All randomness is seeded -> re-running produces identical meshes.
"""
import bpy, bmesh, math, random, sys, os
from mathutils import Vector, Matrix, noise

sys.path.append(os.path.dirname(os.path.abspath(__file__)))
import palette  # noqa: E402

argv = sys.argv[sys.argv.index("--") + 1:]
OUT = argv[0]
ONLY = set(argv[1:])
os.makedirs(OUT, exist_ok=True)

FLAT, SMOOTH, SPHERE = 0, 1, 2


class B:
    """Accumulates parts into one bmesh with palette UVs, wind colours and per-loop normals."""

    def __init__(self):
        self.bm = bmesh.new()
        self.uv = self.bm.loops.layers.uv.new("UVMap")
        self.col = self.bm.loops.layers.color.new("Col")
        self.face_mode = {}      # face -> FLAT/SMOOTH/SPHERE
        self.sphere_c = {}       # face -> centre for SPHERE mode

    def _new_faces(self, verts):
        fs = set()
        for v in verts:
            fs.update(v.link_faces)
        return list(fs)

    def finish(self, verts, swatch, wind=None, mode=FLAT, centre=None, grad=None):
        """wind: callable(co)->0..1 or a constant. grad: (z0, z1) range for the palette gradient."""
        faces = self._new_faces(verts)
        zs = [v.co.z for v in verts]
        z0, z1 = grad if grad else (min(zs), max(zs))
        u0, v0, u1, v1 = palette.uv_rect(swatch)
        uc = (u0 + u1) / 2
        for f in faces:
            self.face_mode[f] = mode
            if mode == SPHERE:
                self.sphere_c[f] = centre
            for l in f.loops:
                t = min(max((l.vert.co.z - z0) / (z1 - z0 + 1e-6), 0), 1)
                l[self.uv].uv = (uc, v0 + (v1 - v0) * t)
                w = wind(l.vert.co) if callable(wind) else (wind or 0.0)
                l[self.col] = (w, 0.0, 0.0, 1.0)
        return faces

    # --- primitives -------------------------------------------------------------------------
    def blob(self, centre, radius, scale=(1, 1, 1), subdiv=2, rough=0.18, seed=0, swatch="leaf_green",
             wind=0.8, sphere_centre=None, mode=SPHERE):
        m = Matrix.Translation(centre) @ Matrix.Diagonal((*scale, 1))
        r = bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=radius, matrix=m)
        vs = r["verts"]
        for v in vs:
            d = (v.co - Vector(centre)).normalized()
            n = noise.noise(v.co * (1.3 / radius) + Vector((seed * 7.1, seed * 3.3, 0)))
            v.co += d * n * rough * radius
        return self.finish(vs, swatch, wind, mode, Vector(sphere_centre or centre))

    def cyl(self, p0, p1, r0, r1, seg=8, swatch="trunk_dark", wind=0.0, mode=SMOOTH, caps=True, grad=None):
        p0, p1 = Vector(p0), Vector(p1)
        d = p1 - p0
        L = d.length
        rot = Vector((0, 0, 1)).rotation_difference(d.normalized()).to_matrix().to_4x4()
        m = Matrix.Translation((p0 + p1) / 2) @ rot
        r = bmesh.ops.create_cone(self.bm, cap_ends=caps, cap_tris=False, segments=seg, radius1=r0,
                                  radius2=r1, depth=L, matrix=m)
        return self.finish(r["verts"], swatch, wind, mode, grad=grad)

    def box(self, centre, size, swatch="wood", rot_z=0.0, wind=0.0, rot=None, grad=None):
        m = Matrix.Translation(centre) @ (rot if rot else Matrix.Rotation(rot_z, 4, "Z")) @ Matrix.Diagonal((*size, 1))
        r = bmesh.ops.create_cube(self.bm, size=1.0, matrix=m)
        return self.finish(r["verts"], swatch, wind, FLAT, grad=grad)

    def rock(self, centre, radius, scale=(1, 1, 0.7), subdiv=1, seed=0, swatch="rock", rough=0.35):
        m = Matrix.Translation(centre) @ Matrix.Diagonal((*scale, 1))
        r = bmesh.ops.create_icosphere(self.bm, subdivisions=subdiv, radius=radius, matrix=m)
        for v in r["verts"]:
            d = (v.co - Vector(centre)).normalized()
            v.co += d * noise.noise(v.co * (0.9 / radius) + Vector((seed * 5.0, 0, seed))) * rough * radius
        return self.finish(r["verts"], swatch, 0.0, FLAT)

    def quad_blade(self, base, height, width, lean, angle, swatch="grass", seg=2):
        """A tapered grass blade (triangle strip), wind weight = height fraction squared."""
        bm = self.bm
        dirv = Vector((math.cos(angle), math.sin(angle), 0))
        side = Vector((-dirv.y, dirv.x, 0))
        verts = []
        rows = []
        for i in range(seg + 1):
            t = i / seg
            c = Vector(base) + Vector((0, 0, height * t)) + dirv * lean * t * t
            w = width * (1 - t) * 0.5
            if i < seg:
                rows.append((bm.verts.new(c - side * w), bm.verts.new(c + side * w)))
            else:
                rows.append((bm.verts.new(c),))
        for i in range(seg):
            a = rows[i]
            b = rows[i + 1]
            if len(b) == 2:
                bm.faces.new((a[0], a[1], b[1], b[0]))
            else:
                bm.faces.new((a[0], a[1], b[0]))
        for r in rows:
            verts.extend(r)
        z0 = base[2]
        return self.finish(verts, swatch, lambda co: ((co.z - z0) / height) ** 1.5, SMOOTH, grad=(z0, z0 + height))

    # --- output ------------------------------------------------------------------------------
    def to_object(self, name):
        me = bpy.data.meshes.new(name)
        bm = self.bm
        bm.normal_update()
        # per-loop normals in the same order Blender will store loops
        loop_normals = []
        for f in bm.faces:
            mode = self.face_mode.get(f, FLAT)
            for l in f.loops:
                if mode == FLAT:
                    loop_normals.append(f.normal.copy())
                elif mode == SPHERE:
                    c = self.sphere_c[f]
                    loop_normals.append(((l.vert.co - c).normalized() * 0.8 + l.vert.normal * 0.2).normalized())
                else:
                    loop_normals.append(l.vert.normal.copy())
        bm.to_mesh(me)
        bm.free()
        me.normals_split_custom_set(loop_normals)
        ob = bpy.data.objects.new(name, me)
        bpy.context.scene.collection.objects.link(ob)
        return ob


def export(asset, objects):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objects:
        o.select_set(True)
    bpy.ops.export_scene.fbx(filepath=os.path.join(OUT, asset + ".fbx"), use_selection=True,
                             object_types={"MESH"}, apply_unit_scale=True, apply_scale_options="FBX_SCALE_UNITS",
                             axis_forward="-Z", axis_up="Y", mesh_smooth_type="OFF", colors_type="LINEAR",
                             use_mesh_modifiers=False, bake_anim=False)
    tris = [sum(len(p.vertices) - 2 for p in o.data.polygons) for o in objects]
    print(f"ASSET {asset}: " + ", ".join(f"{o.name}={t}" for o, t in zip(objects, tris)))
    for o in objects:
        bpy.data.objects.remove(o, do_unlink=True)


# =============================================================================================
# Trees
# =============================================================================================

def broadleaf(name, seed, leaf="leaf_green", trunk="trunk_dark", height=9.0, birch=False):
    lods = []
    for lod, (sub, nblob, seg) in enumerate([(3, 9, 8), (2, 6, 6), (1, 4, 4)]):
        rnd = random.Random(seed)
        b = B()
        H = height
        trunk_h = H * (0.55 if not birch else 0.6)
        r0 = 0.45 if not birch else 0.22
        lean = Vector((rnd.uniform(-0.6, 0.6), rnd.uniform(-0.6, 0.6), 0))
        top = Vector((0, 0, trunk_h)) + lean
        wind_trunk = lambda co: max(co.z / H, 0) ** 2 * 0.25
        b.cyl((0, 0, -0.3), top, r0, r0 * 0.55, seg, trunk, wind_trunk, grad=(0, trunk_h))
        if birch and lod == 0:  # dark birch markings: thin rings
            for k in range(5):
                z = rnd.uniform(0.6, trunk_h - 0.8)
                p = Vector((0, 0, z)) + lean * (z / trunk_h)
                rr = r0 * (1 - 0.45 * z / trunk_h) + 0.01
                b.cyl(p, p + Vector((0, 0, 0.12)), rr, rr, seg, "birch_mark", wind_trunk, caps=False)
        # branches
        nbr = 3 if lod < 2 else 0
        for k in range(nbr):
            a = k * 2.1 + rnd.uniform(-0.3, 0.3)
            z = trunk_h * rnd.uniform(0.55, 0.85)
            p0 = Vector((0, 0, z)) + lean * (z / trunk_h)
            p1 = p0 + Vector((math.cos(a) * H * 0.22, math.sin(a) * H * 0.22, H * 0.18))
            b.cyl(p0, p1, r0 * 0.35, r0 * 0.15, max(seg // 2, 4), trunk, wind_trunk, caps=False)
        # canopy blobs around a centre above the trunk
        cc = top + Vector((0, 0, H * 0.18))
        crad = H * (0.32 if not birch else 0.26)
        rnd2 = random.Random(seed * 31 + 7)
        blobs = [(cc, crad * 0.8)]
        for k in range(nblob - 1):
            a = rnd2.uniform(0, math.tau)
            e = rnd2.uniform(-0.35, 0.8)
            d = Vector((math.cos(a) * math.cos(e), math.sin(a) * math.cos(e), math.sin(e) * 0.7))
            blobs.append((cc + d * crad * rnd2.uniform(0.55, 0.85), crad * rnd2.uniform(0.45, 0.65)))
        if lod > 0:
            blobs = blobs[:nblob]
        for i, (c, r) in enumerate(blobs):
            b.blob(c, r, (1, 1, 0.85), sub, 0.22, seed + i, leaf,
                   wind=lambda co: 0.55 + 0.45 * min(max((co.z - trunk_h * 0.6) / (H * 0.6), 0), 1),
                   sphere_centre=cc)
        lods.append(b.to_object(f"{name}_LOD{lod}"))
    export(name, lods)


def pine(name, seed, height=12.0, leaf="pine"):
    lods = []
    for lod, (tiers, seg) in enumerate([(5, 10), (4, 7), (3, 5)]):
        rnd = random.Random(seed)
        b = B()
        H = height * rnd.uniform(0.9, 1.1)
        b.cyl((0, 0, -0.3), (0, 0, H * 0.4), 0.35, 0.25, max(seg // 2, 4), "trunk_dark", lambda co: 0.0)
        for t in range(tiers):
            f = t / tiers
            z0 = H * (0.18 + 0.72 * f)
            r = H * 0.3 * (1 - f * 0.75)
            h = H * 0.32 * (1 - f * 0.3)
            vs_before = set(b.bm.verts)
            m = Matrix.Translation((0, 0, z0 + h / 2)) @ Matrix.Rotation(rnd.uniform(0, 1), 4, "Z")
            res = bmesh.ops.create_cone(b.bm, cap_ends=True, cap_tris=True, segments=seg, radius1=r, radius2=0.05,
                                        depth=h, matrix=m)
            vs = res["verts"]
            for v in vs:  # droop and jitter the rim
                if v.co.z < z0 + 0.1:
                    v.co.z -= r * 0.15
                    v.co.x *= rnd.uniform(0.85, 1.1)
                    v.co.y *= rnd.uniform(0.85, 1.1)
            b.finish(vs, leaf, lambda co: 0.35 + 0.6 * min(co.z / H, 1), SPHERE, Vector((0, 0, z0 + h * 0.2)), grad=(z0 - r * 0.15, z0 + h))
        lods.append(b.to_object(f"{name}_LOD{lod}"))
    export(name, lods)


def bush(name, seed, swatch="bush", size=1.6):
    lods = []
    for lod, (sub, n) in enumerate([(3, 6), (1, 3)]):
        rnd = random.Random(seed)
        b = B()
        c0 = Vector((0, 0, size * 0.45))
        for k in range(n):
            a = rnd.uniform(0, math.tau)
            d = rnd.uniform(0, size * 0.45) if k else 0
            c = c0 + Vector((math.cos(a) * d, math.sin(a) * d, rnd.uniform(-0.1, 0.3) * size))
            b.blob(c, size * rnd.uniform(0.4, 0.55), (1, 1, 0.8), sub, 0.22, seed + k, swatch,
                   wind=lambda co: 0.25 + 0.5 * min(co.z / (size * 1.2), 1), sphere_centre=c0, )
        lods.append(b.to_object(f"{name}_LOD{lod}"))
    export(name, lods)


def rock(name, seed, size=1.5, moss=True, big=False):
    lods = []
    for lod, sub in enumerate([3 if big else 2, 2 if big else 1]):
        rnd = random.Random(seed)
        b = B()
        n = 4 if big else 2
        for k in range(n):
            a = rnd.uniform(0, math.tau)
            d = size * 0.35 * k
            c = Vector((math.cos(a) * d, math.sin(a) * d, size * 0.25 * (0.6 if big else 1)))
            sc = (rnd.uniform(0.9, 1.3), rnd.uniform(0.8, 1.1), rnd.uniform(0.55, 0.9) * (1.8 if big else 1))
            b.rock(c, size * rnd.uniform(0.45, 0.7), sc, sub, seed * 3 + k, "cliff" if big else "rock")
        if moss and lod == 0 and not big:
            b.blob(Vector((0, 0, size * 0.55)), size * 0.35, (1.3, 1.1, 0.3), 1, 0.15, seed, "rock_moss", 0.0,
                   mode=SMOOTH)
        lods.append(b.to_object(f"{name}_LOD{lod}"))
    export(name, lods)


def grass(name, seed, n=26, swatch="grass", radius=0.45, h=(0.35, 0.7)):
    rnd = random.Random(seed)
    b = B()
    for k in range(n):
        a = rnd.uniform(0, math.tau)
        r = math.sqrt(rnd.random()) * radius
        base = (math.cos(a) * r, math.sin(a) * r, -0.03)
        b.quad_blade(base, rnd.uniform(*h), rnd.uniform(0.05, 0.09), rnd.uniform(0.05, 0.2), rnd.uniform(0, math.tau), swatch)
    export(name, [b.to_object(f"{name}_LOD0")])


def flowers(name, seed, petal="flower_white", n=7, radius=0.5):
    rnd = random.Random(seed)
    b = B()
    for k in range(n):
        a = rnd.uniform(0, math.tau)
        r = math.sqrt(rnd.random()) * radius
        x, y = math.cos(a) * r, math.sin(a) * r
        hh = rnd.uniform(0.25, 0.5)
        b.cyl((x, y, 0), (x + rnd.uniform(-0.04, 0.04), y, hh), 0.012, 0.01, 4, "stem", lambda co: min(co.z / 0.5, 1), caps=False)
        top = Vector((x, y, hh))
        b.blob(top + Vector((0, 0, 0.01)), 0.025, (1, 1, 0.6), 0, 0, k, "flower_center", 1.0, mode=SMOOTH)
        for p in range(6):
            pa = p / 6 * math.tau + rnd.uniform(-0.1, 0.1)
            pc = top + Vector((math.cos(pa) * 0.045, math.sin(pa) * 0.045, 0))
            b.blob(pc, 0.03, (1.4, 0.7, 0.25), 0, 0, k * 10 + p, petal, 1.0, mode=SMOOTH)
    for k in range(10):  # leaves at the base
        a = rnd.uniform(0, math.tau)
        r = rnd.uniform(0, radius)
        b.quad_blade((math.cos(a) * r, math.sin(a) * r, -0.02), rnd.uniform(0.15, 0.3), 0.08, 0.1, rnd.uniform(0, math.tau), "stem")
    export(name, [b.to_object(f"{name}_LOD0")])


def mushrooms(name, seed, n=5):
    rnd = random.Random(seed)
    b = B()
    for k in range(n):
        a = rnd.uniform(0, math.tau)
        r = rnd.uniform(0, 0.25) if k else 0
        x, y = math.cos(a) * r, math.sin(a) * r
        h = rnd.uniform(0.12, 0.28)
        cr = h * rnd.uniform(0.45, 0.6)
        b.cyl((x, y, 0), (x, y, h), cr * 0.28, cr * 0.22, 6, "mushroom_stem", 0.0)
        b.blob((x, y, h), cr, (1, 1, 0.55), 1, 0.05, k, "mushroom_cap", 0.0, mode=SMOOTH)
        for d in range(4):  # white dots
            da = rnd.uniform(0, math.tau)
            b.blob((x + math.cos(da) * cr * 0.5, y + math.sin(da) * cr * 0.5, h + cr * 0.4), cr * 0.12, (1, 1, 0.4), 0, 0, d,
                   "mushroom_stem", 0.0, mode=SMOOTH)
    export(name, [b.to_object(f"{name}_LOD0")])


# =============================================================================================
# Landmarks & buildings
# =============================================================================================

def standing_stone(name, seed, h=3.2):
    rnd = random.Random(seed)
    b = B()
    m = Matrix.Translation((0, 0, h / 2 - 0.2)) @ Matrix.Rotation(rnd.uniform(-0.08, 0.08), 4, "X") @ Matrix.Diagonal((0.9, 0.55, h / 2, 1))
    r = bmesh.ops.create_icosphere(b.bm, subdivisions=1, radius=1.0, matrix=m)
    for v in r["verts"]:
        v.co.x += noise.noise(v.co * 1.3 + Vector((seed, 0, 0))) * 0.25
        v.co.y += noise.noise(v.co * 1.1 + Vector((0, seed, 0))) * 0.15
        if v.co.z > h - 0.6:
            v.co.z = min(v.co.z, h - 0.3 + noise.noise(v.co * 3) * 0.2)
    b.finish(r["verts"], "stone_ruin", 0.0, FLAT)
    b.blob(Vector((0.2, 0.1, h * 0.75)), 0.4, (1.2, 0.9, 0.35), 1, 0.2, seed, "rock_moss", 0.0, mode=SMOOTH)
    export(name, [b.to_object(f"{name}_LOD0"), _lod1_copy(name)])


def _lod1_copy(name):
    """LOD1 for small landmarks: just a simplified duplicate via decimate."""
    src = bpy.data.objects[f"{name}_LOD0"]
    me = src.data.copy()
    ob = bpy.data.objects.new(f"{name}_LOD1", me)
    bpy.context.scene.collection.objects.link(ob)
    m = ob.modifiers.new("d", "DECIMATE")
    m.ratio = 0.4
    bpy.context.view_layer.objects.active = ob
    ob.select_set(True)
    bpy.ops.object.modifier_apply(modifier="d")
    ob.select_set(False)
    return ob


def altar(name):
    b = B()
    b.box((0, 0, 0.3), (2.2, 1.4, 0.6), "stone_ruin")
    b.box((0, 0, 0.75), (1.8, 1.0, 0.3), "stone_ruin")
    b.rock(Vector((0.6, 0.2, 1.0)), 0.22, (1, 1, 0.8), 1, 3, "gold", 0.2)
    export(name, [b.to_object(f"{name}_LOD0")])


def watchtower(name):
    b = B()
    H = 11.0
    b.cyl((0, 0, -1), (0, 0, H * 0.55), 2.2, 1.9, 10, "stone_ruin", 0.0, FLAT)
    b.cyl((0, 0, H * 0.55), (0, 0, H * 0.58), 2.4, 2.4, 10, "wood_dark", 0.0, FLAT)
    for k in range(4):  # posts
        a = k / 4 * math.tau + math.pi / 4
        p = Vector((math.cos(a) * 1.8, math.sin(a) * 1.8, H * 0.58))
        b.cyl(p, p + Vector((0, 0, 2.4)), 0.15, 0.15, 6, "wood", 0.0, FLAT)
    for k in range(10):  # railing
        a0, a1 = k / 10 * math.tau, (k + 1) / 10 * math.tau
        p0 = Vector((math.cos(a0) * 2.3, math.sin(a0) * 2.3, H * 0.58 + 0.9))
        p1 = Vector((math.cos(a1) * 2.3, math.sin(a1) * 2.3, H * 0.58 + 0.9))
        b.cyl(p0, p1, 0.07, 0.07, 4, "wood", 0.0, FLAT, caps=False)
    m = Matrix.Translation((0, 0, H * 0.58 + 2.4 + 1.4))
    r = bmesh.ops.create_cone(b.bm, cap_ends=True, cap_tris=True, segments=10, radius1=3.0, radius2=0.05, depth=2.8, matrix=m)
    b.finish(r["verts"], "roof_blue", 0.0, FLAT)
    b.cyl((0, 0, H * 0.58 + 5.2), (0, 0, H * 0.58 + 6.6), 0.04, 0.04, 4, "metal", 0.0)
    b.box((0.35, 0, H * 0.58 + 6.3), (0.7, 0.04, 0.4), "roof_blue", wind=0.6)
    b.box((0, -2.05, 1.0), (1.1, 0.4, 2.0), "wood_dark")  # door
    export(name, [b.to_object(f"{name}_LOD0"), _lod1_copy(name)])


def bridge(name, length=16.0, width=3.0):
    b = B()
    n = 22
    arch = 1.4
    for k in range(n):
        t = (k + 0.5) / n
        x = (t - 0.5) * length
        z = math.sin(t * math.pi) * arch
        b.box((x, 0, z), (length / n * 0.92, width, 0.15), "wood_light" if k % 3 else "wood",
              rot=Matrix.Rotation(-math.cos(t * math.pi) * arch * math.pi / length * 1.0, 4, "Y"))
    for side in (-1, 1):
        y = side * (width / 2 - 0.1)
        for k in range(7):
            t = k / 6
            x = (t - 0.5) * length
            z = math.sin(t * math.pi) * arch
            b.cyl((x, y, z - 1.5), (x, y, z + 1.0), 0.12, 0.12, 6, "wood_dark", 0.0, FLAT)
        for k in range(12):
            t0, t1 = k / 12, (k + 1) / 12
            p0 = Vector(((t0 - 0.5) * length, y, math.sin(t0 * math.pi) * arch + 0.95))
            p1 = Vector(((t1 - 0.5) * length, y, math.sin(t1 * math.pi) * arch + 0.95))
            b.cyl(p0, p1, 0.08, 0.08, 5, "wood", 0.0, FLAT, caps=False)
    export(name, [b.to_object(f"{name}_LOD0")])


def cabin(name):
    """Exterior shell of the witch's cabin (door/windows are the Tripo props placed in Unity)."""
    b = B()
    W, D, Hw = 7.0, 5.5, 3.2
    b.box((0, 0, 0.25), (W + 0.6, D + 0.6, 0.5), "stone_ruin")                 # foundation
    # log walls: stacked horizontal cylinders
    for i in range(9):
        z = 0.55 + i * (Hw / 9)
        sw = "wood" if i % 2 else "wood_dark"
        b.cyl((-W / 2, -D / 2, z), (W / 2, -D / 2, z), 0.19, 0.19, 6, sw, 0.0, SMOOTH)
        b.cyl((-W / 2, D / 2, z), (W / 2, D / 2, z), 0.19, 0.19, 6, sw, 0.0, SMOOTH)
        b.cyl((-W / 2, -D / 2, z), (-W / 2, D / 2, z), 0.19, 0.19, 6, sw, 0.0, SMOOTH)
        b.cyl((W / 2, -D / 2, z), (W / 2, D / 2, z), 0.19, 0.19, 6, sw, 0.0, SMOOTH)
    b.box((0, 0, 0.5 + Hw / 2), (W - 0.2, D - 0.2, Hw), "plaster")             # inner fill (closes gaps)
    # gable roof (two slabs) + gable triangles
    rh = 2.6
    top = 0.5 + Hw
    ang = math.atan2(rh, D / 2 + 0.6)
    slab_len = math.hypot(rh, D / 2 + 0.8)
    for s in (-1, 1):
        c = Vector((0, s * (D / 4 + 0.2), top + rh / 2))
        b.box(c, (W + 1.4, slab_len, 0.28), "roof_purple", rot=Matrix.Rotation(-s * ang, 4, "X"))
    for x in (-W / 2, W / 2):
        bm = b.bm
        v = [bm.verts.new((x, -D / 2, top)), bm.verts.new((x, D / 2, top)), bm.verts.new((x, 0, top + rh))]
        bm.faces.new(v if x > 0 else list(reversed(v)))
        b.finish(v, "wood_dark", 0.0, FLAT)
    # chimney
    b.box((W / 2 - 1.2, D / 4, top + 1.6), (0.9, 0.9, 3.4), "chimney")
    b.box((W / 2 - 1.2, D / 4, top + 3.35), (1.1, 1.1, 0.25), "stone_ruin")
    # porch planks
    b.box((0, -D / 2 - 1.0, 0.35), (3.2, 1.8, 0.15), "wood_light")
    export(name, [b.to_object(f"{name}_LOD0"), _lod1_copy(name)])


def fence(name, length=3.0):
    b = B()
    for x in (-length / 2, length / 2):
        b.box((x, 0, 0.55), (0.14, 0.14, 1.1), "wood_dark")
    for z in (0.45, 0.85):
        b.box((0, 0, z), (length, 0.07, 0.12), "wood")
    export(name, [b.to_object(f"{name}_LOD0")])


def lamp_post(name):
    b = B()
    b.box((0, 0, 1.6), (0.16, 0.16, 3.2), "wood_dark")
    b.box((0.35, 0, 3.05), (0.8, 0.1, 0.1), "wood_dark")
    b.box((0.7, 0, 2.75), (0.28, 0.28, 0.4), "metal")
    b.box((0.7, 0, 2.75), (0.2, 0.2, 0.32), "lamp_glow")
    export(name, [b.to_object(f"{name}_LOD0")])


def flag_pole(name):
    b = B()
    b.cyl((0, 0, 0), (0, 0, 4.2), 0.08, 0.06, 6, "wood_dark", 0.0)
    b.box((0.45, 0, 4.0), (0.9, 0.06, 0.06), "wood_dark")
    # banner: subdivided strip so it can sway
    bm = b.bm
    cols, rows = 2, 6
    grid = [[bm.verts.new((0.05 + 0.8 * i / cols, 0, 3.95 - 1.6 * j / rows)) for i in range(cols + 1)] for j in range(rows + 1)]
    for j in range(rows):
        for i in range(cols):
            bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
    vs = [v for row in grid for v in row]
    b.finish(vs, "banner_purple", lambda co: min((3.95 - co.z) / 1.6, 1) * 0.9 + 0.1, SMOOTH)
    b.blob(Vector((0.45, -0.02, 3.2)), 0.18, (1, 0.1, 1), 1, 0, 1, "gold", 0.6, mode=FLAT)  # moon emblem
    export(name, [b.to_object(f"{name}_LOD0")])


def campfire(name):
    rnd = random.Random(5)
    b = B()
    for k in range(9):
        a = k / 9 * math.tau
        b.rock(Vector((math.cos(a) * 0.75, math.sin(a) * 0.75, 0.1)), 0.2, (1.2, 1, 0.8), 0, k, "rock", 0.3)
    for k in range(4):
        a = k / 4 * math.tau + 0.3
        p0 = Vector((math.cos(a) * 0.55, math.sin(a) * 0.55, 0.05))
        b.cyl(p0, Vector((0, 0, 0.55)), 0.08, 0.06, 6, "wood_dark", 0.0)
    b.blob(Vector((0, 0, 0.05)), 0.45, (1, 1, 0.15), 1, 0.2, 2, "ash", 0.0, mode=SMOOTH)
    export(name, [b.to_object(f"{name}_LOD0")])


def crate(name):
    b = B()
    b.box((0, 0, 0.35), (0.9, 0.65, 0.7), "wood")
    b.box((0, 0, 0.72), (0.95, 0.7, 0.08), "wood_dark")
    b.box((0, -0.34, 0.45), (0.12, 0.04, 0.16), "gold")
    export(name, [b.to_object(f"{name}_LOD0")])


def log_bench(name):
    b = B()
    b.cyl((-0.9, 0, 0.35), (0.9, 0, 0.35), 0.22, 0.22, 8, "wood", 0.0)
    for x in (-0.6, 0.6):
        b.box((x, 0, 0.12), (0.2, 0.5, 0.24), "wood_dark")
    export(name, [b.to_object(f"{name}_LOD0")])


# =============================================================================================
# Creatures the Tripo set lacks (procedural, rigid parts -> animated procedurally in Unity)
# =============================================================================================

def owl(name):
    """Forest owl (feathered creature, FR-G03). ~0.5 m tall, faces -Y (Unity +Z)."""
    b = B()
    b.blob(Vector((0, 0, 0.22)), 0.2, (1, 0.9, 1.15), 2, 0.06, 1, "owl_body", 0.0, mode=SMOOTH)
    b.blob(Vector((0, -0.08, 0.2)), 0.14, (1, 0.6, 1.1), 2, 0.05, 2, "owl_belly", 0.0, mode=SMOOTH)
    b.blob(Vector((0, -0.02, 0.45)), 0.15, (1.1, 1, 0.9), 2, 0.04, 3, "owl_body", 0.0, mode=SMOOTH)
    for s in (-1, 1):
        b.blob(Vector((s * 0.06, -0.13, 0.47)), 0.045, (1, 0.5, 1), 1, 0, 4, "owl_eye", 0.0, mode=SMOOTH)
        b.blob(Vector((s * 0.06, -0.155, 0.47)), 0.02, (1, 0.5, 1), 1, 0, 5, "black", 0.0, mode=SMOOTH)
        # ear tufts
        b.cyl((s * 0.08, -0.01, 0.56), (s * 0.12, 0.02, 0.66), 0.035, 0.005, 5, "owl_body", 0.0)
        # wings (flattened blobs on the sides) -> named so Unity can flap them? kept in one mesh, flap via body bob
        b.blob(Vector((s * 0.17, 0.03, 0.24)), 0.13, (0.35, 0.9, 1.3), 1, 0.08, 6, "owl_body", 0.0, mode=SMOOTH)
        b.cyl((s * 0.05, -0.02, 0.03), (s * 0.05, -0.06, 0.0), 0.02, 0.015, 4, "gold", 0.0)
    b.cyl((0, -0.15, 0.43), (0, -0.19, 0.4), 0.02, 0.003, 4, "gold", 0.0)
    b.blob(Vector((0, 0.12, 0.08)), 0.08, (0.8, 1.2, 0.4), 1, 0.05, 7, "owl_body", 0.0, mode=SMOOTH)  # tail
    export(name, [b.to_object(f"{name}_LOD0")])


def koi(name):
    """Ink-red river koi (from the user's concept sheet). ~0.6 m long, faces -Y."""
    b = B()
    b.blob(Vector((0, 0, 0.15)), 0.16, (0.75, 1.7, 0.85), 2, 0.03, 1, "koi_body", lambda co: min(max(co.y / 0.3, 0), 1), mode=SMOOTH)
    for s in (-1, 1):
        b.blob(Vector((s * 0.1, -0.18, 0.19)), 0.035, (1, 1, 1), 1, 0, 2, "koi_red", 0.0, mode=SMOOTH)
        b.blob(Vector((s * 0.15, -0.05, 0.08)), 0.09, (1.4, 0.8, 0.15), 1, 0.1, 3, "koi_fin", 0.3, mode=SMOOTH)
    b.blob(Vector((0.04, 0.02, 0.25)), 0.08, (0.4, 1.3, 0.5), 1, 0.05, 4, "koi_red", 0.0, mode=SMOOTH)  # red patch
    b.blob(Vector((0, 0.05, 0.3)), 0.12, (0.12, 1.2, 0.7), 1, 0.15, 5, "koi_fin", 0.4, mode=SMOOTH)    # dorsal
    for s in (-1, 1):  # flowing tail
        b.blob(Vector((s * 0.08, 0.38, 0.15)), 0.16, (0.5, 1.2, 0.12), 1, 0.2, 6, "koi_fin",
               lambda co: min(max((co.y - 0.2) / 0.4, 0), 1), mode=SMOOTH)
    export(name, [b.to_object(f"{name}_LOD0")])


# =============================================================================================
# Background
# =============================================================================================

def snow_mountains(name):
    rnd = random.Random(11)
    b = B()
    for k in range(9):
        x = k * 120 + rnd.uniform(-30, 30)
        h = rnd.uniform(160, 300)
        r = rnd.uniform(110, 170)
        m = Matrix.Translation((x, rnd.uniform(-40, 40), h / 2 - 20))
        res = bmesh.ops.create_cone(b.bm, cap_ends=False, cap_tris=False, segments=9, radius1=r, radius2=2, depth=h, matrix=m)
        vs = res["verts"]
        for v in vs:
            v.co.x += noise.noise(v.co * 0.02 + Vector((k, 0, 0))) * 25
            v.co.y += noise.noise(v.co * 0.02 + Vector((0, k, 0))) * 25
        b.finish(vs, "mountain", 0.0, FLAT)
        # snow cap
        m2 = Matrix.Translation((x, 0, h - 20 - h * 0.17))
        res2 = bmesh.ops.create_cone(b.bm, cap_ends=False, cap_tris=False, segments=9, radius1=r * 0.36, radius2=1.5, depth=h * 0.34, matrix=m2)
        for v in res2["verts"]:
            v.co.x += noise.noise(v.co * 0.03) * 8
        b.finish(res2["verts"], "snow", 0.0, FLAT)
    export(name, [b.to_object(f"{name}_LOD0")])


def cloud(name, seed):
    rnd = random.Random(seed)
    b = B()
    for k in range(7):
        c = Vector((rnd.uniform(-12, 12), rnd.uniform(-5, 5), rnd.uniform(0, 4)))
        b.blob(c, rnd.uniform(4, 7), (1.3, 1, 0.6), 1, 0.1, k, "cloud", 0.0, sphere_centre=Vector((0, 0, -4)))
    export(name, [b.to_object(f"{name}_LOD0")])


JOBS = {
    "Tree_Broadleaf_A": lambda: broadleaf("Tree_Broadleaf_A", 1, "leaf_green", height=10),
    "Tree_Broadleaf_B": lambda: broadleaf("Tree_Broadleaf_B", 2, "leaf_green_dark", height=12),
    "Tree_Autumn_A": lambda: broadleaf("Tree_Autumn_A", 3, "leaf_autumn", height=9),
    "Tree_Autumn_B": lambda: broadleaf("Tree_Autumn_B", 4, "leaf_yellow", height=8),
    "Tree_Birch_A": lambda: broadleaf("Tree_Birch_A", 5, "leaf_birch", "trunk_birch", height=10, birch=True),
    "Tree_Pine_A": lambda: pine("Tree_Pine_A", 6, 12),
    "Tree_Pine_B": lambda: pine("Tree_Pine_B", 7, 15),
    "Bush_A": lambda: bush("Bush_A", 8, "bush", 1.6),
    "Bush_B": lambda: bush("Bush_B", 9, "leaf_green", 2.2),
    "Rock_A": lambda: rock("Rock_A", 10, 1.4),
    "Rock_B": lambda: rock("Rock_B", 11, 2.4),
    "Cliff_A": lambda: rock("Cliff_A", 12, 9, moss=False, big=True),
    "Cliff_B": lambda: rock("Cliff_B", 13, 13, moss=False, big=True),
    "Grass_A": lambda: grass("Grass_A", 14),
    "Grass_B": lambda: grass("Grass_B", 15, 18, "grass_dry", 0.35, (0.25, 0.5)),
    "Flowers_White": lambda: flowers("Flowers_White", 16, "flower_white"),
    "Flowers_Orange": lambda: flowers("Flowers_Orange", 17, "flower_orange", 5),
    "Flowers_Purple": lambda: flowers("Flowers_Purple", 18, "flower_purple", 6),
    "Mushrooms": lambda: mushrooms("Mushrooms", 19),
    "StandingStone_A": lambda: standing_stone("StandingStone_A", 20),
    "StandingStone_B": lambda: standing_stone("StandingStone_B", 21, 2.4),
    "Altar": lambda: altar("Altar"),
    "Watchtower": lambda: watchtower("Watchtower"),
    "Bridge": lambda: bridge("Bridge"),
    "Cabin": lambda: cabin("Cabin"),
    "Fence": lambda: fence("Fence"),
    "LampPost": lambda: lamp_post("LampPost"),
    "FlagPole": lambda: flag_pole("FlagPole"),
    "Campfire": lambda: campfire("Campfire"),
    "Crate": lambda: crate("Crate"),
    "LogBench": lambda: log_bench("LogBench"),
    "Owl": lambda: owl("Owl"),
    "Koi": lambda: koi("Koi"),
    "SnowMountains": lambda: snow_mountains("SnowMountains"),
    "Cloud_A": lambda: cloud("Cloud_A", 22),
}

bpy.ops.wm.read_factory_settings(use_empty=True)
for k, job in JOBS.items():
    if ONLY and k not in ONLY:
        continue
    try:
        job()
    except Exception as e:  # keep going; report at the end
        import traceback
        traceback.print_exc()
        print(f"FAILED {k}: {e}")
