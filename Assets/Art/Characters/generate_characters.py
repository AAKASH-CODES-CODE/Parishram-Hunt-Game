import bpy
import bmesh
import math
import os
from mathutils import Vector, Euler

OUT_DIR = r"C:\Users\AAKASH\Desktop\3D\CoachingPropHunt\Assets\Art\Characters"
os.makedirs(OUT_DIR, exist_ok=True)


def clear_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)


def make_mat(name, rgba, roughness=0.45, metallic=0.05, emission=None, emission_strength=0.0):
    mat = bpy.data.materials.new(name=name)
    mat.use_nodes = True
    bsdf = mat.node_tree.nodes.get("Principled BSDF")
    if bsdf:
        bsdf.inputs["Base Color"].default_value = (rgba[0], rgba[1], rgba[2], 1.0)
        bsdf.inputs["Roughness"].default_value = roughness
        bsdf.inputs["Metallic"].default_value = metallic
        if emission is not None and "Emission Color" in bsdf.inputs:
            bsdf.inputs["Emission Color"].default_value = (emission[0], emission[1], emission[2], 1.0)
            bsdf.inputs["Emission Strength"].default_value = emission_strength
    return mat


def create_empty(name, location=(0, 0, 0), rotation=(0, 0, 0), parent=None):
    obj = bpy.data.objects.new(name, None)
    obj.empty_display_type = 'PLAIN_AXES'
    obj.empty_display_size = 0.08
    obj.location = location
    obj.rotation_euler = rotation
    bpy.context.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    return obj


def smooth_bevel(obj, width=0.015, segments=2):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    if width > 0:
        bev = obj.modifiers.new(name="Bevel", type='BEVEL')
        bev.width = width
        bev.segments = segments
        bev.limit_method = 'ANGLE'
        bpy.ops.object.modifier_apply(modifier=bev.name)
    for poly in obj.data.polygons:
        poly.use_smooth = True
    obj.select_set(False)


def add_box(name, size, loc, mat, parent=None, rot=(0, 0, 0), bevel=0.014, seg=2):
    mesh = bpy.data.meshes.new(name + "_mesh")
    bm = bmesh.new()
    bmesh.ops.create_cube(bm, size=1.0)
    bmesh.ops.scale(bm, vec=size, verts=bm.verts)
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    obj.location = loc
    obj.rotation_euler = rot
    obj.data.materials.append(mat)
    bpy.context.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    smooth_bevel(obj, width=bevel, segments=seg)
    return obj


def add_capsule_limb(name, rad_top, rad_bot, height, loc, mat, parent=None, rot=(0, 0, 0), segs=16):
    mesh = bpy.data.meshes.new(name + "_mesh")
    bm = bmesh.new()
    bmesh.ops.create_cone(
        bm,
        cap_ends=True,
        cap_tris=False,
        segments=segs,
        radius1=rad_bot,
        radius2=rad_top,
        depth=height,
    )
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    obj.location = loc
    obj.rotation_euler = rot
    obj.data.materials.append(mat)
    bpy.context.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    smooth_bevel(obj, width=min(rad_top, rad_bot) * 0.16, segments=2)
    return obj


def add_sphere(name, scale, loc, mat, parent=None, rot=(0, 0, 0), segs=20, rings=14):
    mesh = bpy.data.meshes.new(name + "_mesh")
    bm = bmesh.new()
    bmesh.ops.create_uvsphere(bm, u_segments=segs, v_segments=rings, radius=0.5)
    bmesh.ops.scale(bm, vec=scale, verts=bm.verts)
    bm.to_mesh(mesh)
    bm.free()

    obj = bpy.data.objects.new(name, mesh)
    obj.location = loc
    obj.rotation_euler = rot
    obj.data.materials.append(mat)
    bpy.context.collection.objects.link(obj)
    if parent is not None:
        obj.parent = parent
    for poly in obj.data.polygons:
        poly.use_smooth = True
    return obj


def build_common_palette():
    return {
        "hunter_navy": make_mat("HunterNavy", (0.110, 0.192, 0.329), roughness=0.48, metallic=0.10),
        "hunter_vest": make_mat("HunterVest", (0.075, 0.110, 0.185), roughness=0.55, metallic=0.18),
        "hunter_accent": make_mat("HunterAccent", (0.949, 0.424, 0.247), roughness=0.35, metallic=0.22,
                                  emission=(0.949, 0.424, 0.247), emission_strength=0.35),
        "visor_glow": make_mat("HunterVisorGlow", (1.0, 0.54, 0.15), roughness=0.18, metallic=0.45,
                               emission=(1.0, 0.48, 0.12), emission_strength=1.45),
        "prop_teal": make_mat("PropTeal", (0.102, 0.596, 0.541), roughness=0.52, metallic=0.04),
        "prop_accent": make_mat("PropAccent", (0.408, 0.839, 0.741), roughness=0.40, metallic=0.06),
        "gold": make_mat("AccentGold", (0.961, 0.729, 0.243), roughness=0.32, metallic=0.42),
        "skin": make_mat("SkinWarm", (0.800, 0.596, 0.447), roughness=0.58, metallic=0.02),
        "hair": make_mat("HairCharcoal", (0.133, 0.141, 0.173), roughness=0.65, metallic=0.03),
        "denim": make_mat("DenimSlate", (0.188, 0.259, 0.384), roughness=0.68, metallic=0.02),
        "shoe_dark": make_mat("ShoeDark", (0.149, 0.165, 0.196), roughness=0.45, metallic=0.08),
        "shoe_sole": make_mat("ShoeSoleWhite", (0.92, 0.94, 0.96), roughness=0.38, metallic=0.04),
        "glove": make_mat("GloveDark", (0.12, 0.14, 0.18), roughness=0.48, metallic=0.12),
        "metal_gun": make_mat("GunMetal", (0.16, 0.19, 0.24), roughness=0.30, metallic=0.72),
        "eye_dark": make_mat("EyeDark", (0.06, 0.07, 0.10), roughness=0.22, metallic=0.10),
        "cyan_glow": make_mat("CyanCore", (0.25, 0.90, 0.95), roughness=0.20, metallic=0.30,
                              emission=(0.25, 0.90, 0.95), emission_strength=1.6),
    }


def add_sneaker_leg(side_sign, hip_name, shin_name, x_offset, mat_pant, mat_shoe, mat_sole, mat_accent, root):
    # Blender -Y is forward (which becomes +Z forward in Unity!)
    hip = create_empty(hip_name, (x_offset, 0.0, 0.78), parent=root)
    add_capsule_limb(hip_name + "_Thigh", 0.084, 0.072, 0.38, (0.0, 0.0, -0.19), mat_pant, parent=hip)
    add_sphere(hip_name + "_KneeCap", (0.145, 0.145, 0.13), (0.0, -0.01, -0.37), mat_pant, parent=hip)

    shin = create_empty(shin_name, (0.0, 0.0, -0.37), parent=hip)
    add_capsule_limb(shin_name + "_Calf", 0.072, 0.056, 0.33, (0.0, 0.0, -0.165), mat_pant, parent=shin)

    # Boot/Sneaker grounded right at world Z=0 (-0.78 + -0.37 -> need bottom at -0.41 from shin pivot)
    add_box(shin_name + "_Sole", (0.135, 0.245, 0.045), (0.0, -0.035, -0.387), mat_sole, parent=shin, bevel=0.010)
    add_box(shin_name + "_ShoeUpper", (0.128, 0.225, 0.075), (0.0, -0.028, -0.340), mat_shoe, parent=shin, bevel=0.018)
    add_box(shin_name + "_ToeCap", (0.122, 0.085, 0.052), (0.0, -0.115, -0.352), mat_accent, parent=shin, bevel=0.016)
    return hip, shin


def add_stylized_head(parent, mat_skin, mat_hair, mat_eye, is_hunter, mat_cap=None, mat_visor=None, mat_gold=None):
    head = create_empty("HeadPivot", (0.0, 0.0, 0.54), parent=parent)
    add_capsule_limb("Neck", 0.058, 0.066, 0.10, (0.0, 0.0, 0.03), mat_skin, parent=head)
    # Sculpted ovoid head with subtle jaw taper
    add_sphere("HeadCranium", (0.255, 0.270, 0.295), (0.0, -0.008, 0.185), mat_skin, parent=head)
    add_sphere("EarL", (0.045, 0.065, 0.080), (0.128, 0.005, 0.182), mat_skin, parent=head)
    add_sphere("EarR", (0.045, 0.065, 0.080), (-0.128, 0.005, 0.182), mat_skin, parent=head)

    if is_hunter:
        # Back hair under cap + Tactical Visor + Peaked Cap + Mic Headset
        add_sphere("HairBack", (0.262, 0.245, 0.205), (0.0, 0.025, 0.215), mat_hair, parent=head)
        add_box("CapCrown", (0.268, 0.285, 0.115), (0.0, 0.005, 0.285), mat_cap, parent=head, bevel=0.032, seg=3)
        add_box("CapBrim", (0.235, 0.155, 0.028), (0.0, -0.155, 0.245), mat_gold, parent=head, rot=(0.14, 0.0, 0.0), bevel=0.010)
        # Sleek curved visor bar across eye-line (-Y front)
        add_box("VisorFrame", (0.245, 0.085, 0.072), (0.0, -0.128, 0.195), mat_cap, parent=head, bevel=0.014)
        add_box("VisorLens", (0.215, 0.040, 0.050), (0.0, -0.154, 0.195), mat_visor, parent=head, bevel=0.010)
        add_box("CommsPod", (0.045, 0.075, 0.075), (-0.142, -0.025, 0.185), mat_gold, parent=head, bevel=0.010)
    else:
        # Expressive eyes, nose, hair swoop & glasses frame for Student Prop character
        add_sphere("EyeL", (0.034, 0.025, 0.044), (0.054, -0.132, 0.192), mat_eye, parent=head)
        add_sphere("EyeR", (0.034, 0.025, 0.044), (-0.054, -0.132, 0.192), mat_eye, parent=head)
        add_sphere("Nose", (0.030, 0.038, 0.032), (0.0, -0.142, 0.162), mat_skin, parent=head)
        add_sphere("HairTop", (0.274, 0.282, 0.215), (0.0, 0.015, 0.240), mat_hair, parent=head)
        add_box("HairBangs", (0.235, 0.105, 0.075), (0.0, -0.102, 0.268), mat_hair, parent=head, rot=(-0.22, 0.0, 0.10), bevel=0.024, seg=3)
        add_box("HeadbandStripe", (0.268, 0.275, 0.035), (0.0, 0.010, 0.242), mat_gold, parent=head, bevel=0.012)
    return head


def build_blaster_mesh(parent, mats, offset=(0.0, 0.0, 0.0)):
    gun = create_empty("BlasterRoot", offset, parent=parent)
    # Main receiver body pointing toward Blender -Y (= Unity +Z)
    add_box("Receiver", (0.068, 0.34, 0.095), (0.0, -0.08, 0.03), mats["metal_gun"], parent=gun, bevel=0.012)
    add_box("TopRail", (0.042, 0.24, 0.032), (0.0, -0.07, 0.088), mats["hunter_navy"], parent=gun, bevel=0.008)
    add_box("EnergyCore", (0.076, 0.14, 0.046), (0.0, -0.08, 0.036), mats["visor_glow"], parent=gun, bevel=0.008)
    add_capsule_limb("Barrel", 0.024, 0.028, 0.16, (0.0, -0.30, 0.036), mats["hunter_accent"], parent=gun, rot=(math.radians(90), 0.0, 0.0))
    add_capsule_limb("MuzzleRing", 0.034, 0.034, 0.032, (0.0, -0.37, 0.036), mats["cyan_glow"], parent=gun, rot=(math.radians(90), 0.0, 0.0))
    add_box("Grip", (0.048, 0.068, 0.125), (0.0, 0.025, -0.045), mats["glove"], parent=gun, rot=(math.radians(16), 0.0, 0.0), bevel=0.010)
    create_empty("MuzzleTip", (0.0, -0.40, 0.036), parent=gun)
    return gun


def build_hunter():
    clear_scene()
    mats = build_common_palette()
    root = create_empty("HunterCharacterRoot", (0, 0, 0))

    # NOTE: Because Blender (-X, Z, -Y) maps via glTFast to Unity (+X right, +Y up, +Z forward):
    # Blender -X becomes Unity +X (Character's RIGHT), and Blender +X becomes Unity -X (Character's LEFT)!
    add_sneaker_leg(1, "LegL_Pivot", "ShinL_Pivot", +0.112, mats["denim"], mats["shoe_dark"], mats["shoe_sole"], mats["hunter_accent"], root)
    add_sneaker_leg(-1, "LegR_Pivot", "ShinR_Pivot", -0.112, mats["denim"], mats["shoe_dark"], mats["shoe_sole"], mats["hunter_accent"], root)

    body = create_empty("BodyPivot", (0.0, 0.0, 0.78), parent=root)
    # Sculpted tactical coaching agent torso + armor vest + shoulder pads + belt
    add_box("TorsoJacket", (0.37, 0.22, 0.48), (0.0, 0.0, 0.26), mats["hunter_navy"], parent=body, bevel=0.042, seg=3)
    add_box("TacticalVest", (0.39, 0.255, 0.34), (0.0, -0.008, 0.29), mats["hunter_vest"], parent=body, bevel=0.032, seg=2)
    add_box("ChestBadge", (0.11, 0.03, 0.075), (0.10, -0.138, 0.36), mats["gold"], parent=body, bevel=0.006)
    add_box("ChestPulseCore", (0.09, 0.032, 0.09), (0.0, -0.138, 0.28), mats["visor_glow"], parent=body, bevel=0.012)
    add_box("UtilityBelt", (0.405, 0.245, 0.075), (0.0, 0.0, 0.055), mats["hunter_accent"], parent=body, bevel=0.014)
    add_box("BackTank", (0.24, 0.125, 0.32), (0.0, 0.145, 0.29), mats["hunter_vest"], parent=body, bevel=0.025)
    add_box("BackLightStrip", (0.06, 0.03, 0.22), (0.0, 0.205, 0.29), mats["visor_glow"], parent=body, bevel=0.008)

    # Arms (Left at +X, Right at -X in Blender so Unity gets Right at +X)
    arm_l = create_empty("ArmL_Pivot", (+0.265, 0.0, 0.45), parent=body)
    add_sphere("ShoulderL", (0.145, 0.145, 0.135), (0.0, 0.0, -0.01), mats["hunter_accent"], parent=arm_l)
    add_capsule_limb("UpperArmL", 0.056, 0.048, 0.24, (0.0, 0.0, -0.12), mats["hunter_navy"], parent=arm_l)
    add_capsule_limb("ForeArmL", 0.048, 0.042, 0.20, (0.0, 0.0, -0.32), mats["hunter_navy"], parent=arm_l)
    add_sphere("HandL", (0.088, 0.092, 0.095), (0.0, 0.0, -0.43), mats["glove"], parent=arm_l)

    arm_r = create_empty("ArmR_Pivot", (-0.265, 0.0, 0.45), parent=body)
    add_sphere("ShoulderR", (0.145, 0.145, 0.135), (0.0, 0.0, -0.01), mats["hunter_accent"], parent=arm_r)
    add_capsule_limb("UpperArmR", 0.056, 0.048, 0.24, (0.0, 0.0, -0.12), mats["hunter_navy"], parent=arm_r)
    add_capsule_limb("ForeArmR", 0.048, 0.042, 0.20, (0.0, 0.0, -0.32), mats["hunter_navy"], parent=arm_r)
    add_sphere("HandR", (0.088, 0.092, 0.095), (0.0, 0.0, -0.43), mats["glove"], parent=arm_r)

    # GunAnchor inside Right Hand (oriented cleanly in Unity arm grip)
    create_empty("GunAnchor", (0.0, -0.055, -0.44), rotation=(0.0, 0.0, 0.0), parent=arm_r)

    add_stylized_head(body, mats["skin"], mats["hair"], mats["eye_dark"], is_hunter=True,
                      mat_cap=mats["hunter_navy"], mat_visor=mats["visor_glow"], mat_gold=mats["gold"])

    out_path = os.path.join(OUT_DIR, "Hunter_Character.glb")
    bpy.ops.export_scene.gltf(filepath=out_path, export_format='GLB', export_yup=True)
    print("EXPORTED:", out_path)


def build_prop_student():
    clear_scene()
    mats = build_common_palette()
    root = create_empty("PropStudentCharacterRoot", (0, 0, 0))

    add_sneaker_leg(1, "LegL_Pivot", "ShinL_Pivot", +0.106, mats["denim"], mats["shoe_dark"], mats["shoe_sole"], mats["prop_accent"], root)
    add_sneaker_leg(-1, "LegR_Pivot", "ShinR_Pivot", -0.106, mats["denim"], mats["shoe_dark"], mats["shoe_sole"], mats["prop_accent"], root)

    body = create_empty("BodyPivot", (0.0, 0.0, 0.78), parent=root)
    # Cozy coaching hoodie + front kangaroo pocket + zipper + detailed 3D college backpack
    add_box("TorsoHoodie", (0.35, 0.22, 0.47), (0.0, 0.0, 0.255), mats["prop_teal"], parent=body, bevel=0.046, seg=3)
    add_box("HoodieCollar", (0.28, 0.235, 0.095), (0.0, 0.022, 0.475), mats["prop_accent"], parent=body, bevel=0.025, seg=2)
    add_box("FrontPocket", (0.25, 0.045, 0.135), (0.0, -0.105, 0.125), mats["prop_accent"], parent=body, bevel=0.018)
    add_box("ZipperStripe", (0.032, 0.025, 0.36), (0.0, -0.112, 0.285), mats["gold"], parent=body, bevel=0.006)

    # Detailed 3D Coaching Backpack
    add_box("BackpackMain", (0.275, 0.145, 0.34), (0.0, 0.145, 0.265), mats["hunter_navy"], parent=body, bevel=0.034, seg=3)
    add_box("BackpackPocket", (0.225, 0.055, 0.165), (0.0, 0.215, 0.185), mats["gold"], parent=body, bevel=0.018)
    add_box("BackpackTopHandle", (0.11, 0.032, 0.045), (0.0, 0.135, 0.445), mats["prop_accent"], parent=body, bevel=0.008)

    arm_l = create_empty("ArmL_Pivot", (+0.252, 0.0, 0.44), parent=body)
    add_sphere("ShoulderL", (0.132, 0.132, 0.125), (0.0, 0.0, -0.01), mats["prop_accent"], parent=arm_l)
    add_capsule_limb("UpperArmL", 0.052, 0.045, 0.23, (0.0, 0.0, -0.115), mats["prop_teal"], parent=arm_l)
    add_capsule_limb("ForeArmL", 0.046, 0.039, 0.19, (0.0, 0.0, -0.305), mats["prop_teal"], parent=arm_l)
    add_sphere("HandL", (0.082, 0.084, 0.086), (0.0, 0.0, -0.41), mats["skin"], parent=arm_l)

    arm_r = create_empty("ArmR_Pivot", (-0.252, 0.0, 0.44), parent=body)
    add_sphere("ShoulderR", (0.132, 0.132, 0.125), (0.0, 0.0, -0.01), mats["prop_accent"], parent=arm_r)
    add_capsule_limb("UpperArmR", 0.052, 0.045, 0.23, (0.0, 0.0, -0.115), mats["prop_teal"], parent=arm_r)
    add_capsule_limb("ForeArmR", 0.046, 0.039, 0.19, (0.0, 0.0, -0.305), mats["prop_teal"], parent=arm_r)
    add_sphere("HandR", (0.082, 0.084, 0.086), (0.0, 0.0, -0.41), mats["skin"], parent=arm_r)

    add_stylized_head(body, mats["skin"], mats["hair"], mats["eye_dark"], is_hunter=False, mat_gold=mats["gold"])

    out_path = os.path.join(OUT_DIR, "Prop_Student_Character.glb")
    bpy.ops.export_scene.gltf(filepath=out_path, export_format='GLB', export_yup=True)
    print("EXPORTED:", out_path)


def build_standalone_blaster():
    clear_scene()
    mats = build_common_palette()
    root = create_empty("HunterBlasterRoot", (0, 0, 0))
    build_blaster_mesh(root, mats)
    out_path = os.path.join(OUT_DIR, "Hunter_Blaster.glb")
    bpy.ops.export_scene.gltf(filepath=out_path, export_format='GLB', export_yup=True)
    print("EXPORTED:", out_path)


if __name__ == "__main__":
    build_hunter()
    build_prop_student()
    build_standalone_blaster()
