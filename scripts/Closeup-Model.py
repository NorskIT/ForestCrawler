"""Create a separate optimized close-up mesh with localized authored facial shape keys."""
import bpy, math
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'assets/prepared/ForestCrawler.blend'))
arm=bpy.data.objects['CrawlerRig']; source=bpy.data.objects['Crawler_LOD0']
face=source.copy(); face.data=source.data.copy(); bpy.context.collection.objects.link(face); face.name='CrawlerCloseup'
face.hide_set(False); face.hide_render=False
face.shape_key_add(name='Basis')
for name in ('JawStretch','MouthAsymmetry','EyeContraction'):
    key=face.shape_key_add(name=name)
    for v,p in zip(face.data.vertices,key.data):
        x,y,z=v.co
        front=max(0,min(1,(-y+.07)/.12))
        mouth=math.exp(-((z-2.24)/.085)**4-((x)/.14)**4)*front
        if name=='JawStretch': p.co.z-=.065*mouth; p.co.y-=.025*mouth
        elif name=='MouthAsymmetry': p.co.z+=.045*mouth*math.tanh(x/.025); p.co.x+=.012*mouth
        else:
            eye=math.exp(-((z-2.40)/.055)**2-((abs(x)-.073)/.045)**2)*front
            p.co.z+=(2.40-z)*.6*eye
arm.animation_data.action=bpy.data.actions['idle']; bpy.context.scene.frame_set(1)
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True); face.select_set(True); bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=str(ROOT/'unity/Assets/Crawler/CrawlerCloseup.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y')
assert len(face.data.shape_keys.key_blocks)==4
for key in face.data.shape_keys.key_blocks[1:]:
    moved=sum((p.co-v.co).length>.0001 for v,p in zip(face.data.vertices,key.data))
    assert moved>50, (key.name,moved)
    print('FACE_SHAPE',key.name,moved)
