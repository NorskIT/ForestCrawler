"""Normalize the supplied FBX and render a reproducible rig inspection."""
import bpy, math, json
from pathlib import Path
from mathutils import Vector, Matrix
ROOT=Path(__file__).resolve().parents[1]
OUT=ROOT/'assets/prepared'
OUT.mkdir(parents=True,exist_ok=True)
bpy.ops.object.select_all(action='SELECT'); bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(ROOT/'assets/source/fbx/Smiley.fbx'))
arm=next(o for o in bpy.context.scene.objects if o.type=='ARMATURE')
mesh=next(o for o in bpy.context.scene.objects if o.type=='MESH')
points=[mesh.matrix_world@v.co for v in mesh.data.vertices]
lo=Vector(tuple(min(p[i] for p in points) for i in range(3)))
hi=Vector(tuple(max(p[i] for p in points) for i in range(3)))
scale=2.5/(hi.z-lo.z)
# Blender forward is -Y. The source face points -Y.
normal=Matrix.Rotation(math.pi,4,'Z') @ Matrix.Scale(scale,4) @ Matrix.Translation(Vector((-(lo.x+hi.x)/2,-(lo.y+hi.y)/2,-lo.z)))
world={o.name:o.matrix_world.copy() for o in (arm,mesh)}
for obj in (arm,mesh):
    obj.parent=None
    obj.data.transform(normal@world[obj.name])
    obj.matrix_world=Matrix.Identity(4)
mesh.parent=arm
arm.name='CrawlerRig'; mesh.name='CrawlerBody'
for obj in list(bpy.context.scene.objects):
    if obj not in (arm,mesh): bpy.data.objects.remove(obj,do_unlink=True)
for image in bpy.data.images:
    filename=Path(image.filepath).name
    candidate=ROOT/'assets/source/fbx'/filename
    if candidate.exists(): image.filepath=str(candidate); image.reload()
for mat in mesh.data.materials:
    if not mat: continue
    filename='Body.png' if mat.name=='Material.002' else 'Eye.png'
    mat.name='CrawlerBodyMaterial' if filename=='Body.png' else 'CrawlerEyeMaterial'
    mat.use_nodes=True; nodes=mat.node_tree.nodes; nodes.clear()
    output=nodes.new('ShaderNodeOutputMaterial'); bsdf=nodes.new('ShaderNodeBsdfPrincipled'); tex=nodes.new('ShaderNodeTexImage')
    tex.image=bpy.data.images.load(str(ROOT/'assets/source/fbx'/filename),check_existing=True)
    mat.node_tree.links.new(tex.outputs['Color'],bsdf.inputs['Base Color'])
    mat.node_tree.links.new(bsdf.outputs['BSDF'],output.inputs['Surface'])
    bsdf.inputs['Roughness'].default_value=.85
scene=bpy.context.scene
scene.render.engine='BLENDER_EEVEE_NEXT'; scene.render.resolution_x=900; scene.render.resolution_y=900; scene.render.resolution_percentage=100
scene.world.color=(.12,.12,.12)
def track(obj,target): obj.rotation_euler=(Vector(target)-obj.location).to_track_quat('-Z','Y').to_euler()
bpy.ops.object.camera_add(location=(3,-6,2.5)); camera=bpy.context.object; track(camera,(0,0,1.25)); scene.camera=camera
camera.data.type='ORTHO'; camera.data.ortho_scale=3.3
for loc,power,size in [((2,-4,5),650,4),((-3,-1,3),400,3),((0,3,4),800,3)]:
    bpy.ops.object.light_add(type='AREA',location=loc); light=bpy.context.object; light.data.energy=power; light.data.shape='DISK'; light.data.size=size; track(light,(0,0,1.2))
scene.render.filepath=str(ROOT/'artifacts/source-front.png'); bpy.ops.render.render(write_still=True)
mapping={b.name:{'head':list(b.head_local),'tail':list(b.tail_local),'parent':b.parent.name if b.parent else None} for b in arm.data.bones}
(OUT/'normalized-rig.json').write_text(json.dumps(mapping,indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'normalized.blend'))
