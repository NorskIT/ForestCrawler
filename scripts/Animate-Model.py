"""Original authored poses and contact-constrained gait, baked for a Generic Unity rig."""
import bpy, math, json, shutil
from pathlib import Path
from mathutils import Vector, Matrix, Quaternion
ROOT=Path(__file__).resolve().parents[1]; OUT=ROOT/'assets/prepared'; DEST=ROOT/'unity/Assets/Crawler'
bpy.ops.wm.open_mainfile(filepath=str(OUT/'normalized.blend'))
arm=bpy.data.objects['CrawlerRig']; mesh=bpy.data.objects['CrawlerBody']; scene=bpy.context.scene
mapping={'Bone':'Head','Bone_end':'HeadTip','Bone.001':'Chest','Bone.002':'ShoulderL','Bone.003':'UpperArmL','Bone.004':'ForearmL','Bone.004_end':'HandL','Bone.005':'ShoulderR','Bone.006':'UpperArmR','Bone.007':'ForearmR','Bone.007_end':'HandR','Bone.008':'Spine','Bone.009':'HipR','Bone.010':'ThighR','Bone.011':'CalfR','Bone.011_end':'FootR','Bone.012':'HipL','Bone.013':'ThighL','Bone.014':'CalfL','Bone.014_end':'FootL'}
for old,new in mapping.items():
    arm.data.bones[old].name=new
    if old in mesh.vertex_groups: mesh.vertex_groups[old].name=new
bpy.context.view_layer.objects.active=arm; arm.select_set(True)
bpy.ops.object.mode_set(mode='EDIT')
eb=arm.data.edit_bones
motion_root=eb.new('Root'); motion_root.head=(0,0,0); motion_root.tail=(0,0,.1)
hips=eb.new('Hips'); hips.head=(0,.15,1.66); hips.tail=(0,.15,1.86)
hips.parent=motion_root
eb['Chest'].head=hips.head; eb['Chest'].tail=(0,.02,2.344)
eb['Chest'].parent=hips; eb['Head'].parent=eb['Chest']
eb['HipL'].parent=hips; eb['HipR'].parent=hips
for side in ('L','R'):
    foot=eb['Foot'+side]; foot.tail=foot.head+Vector((0,-.18,0)); foot.use_connect=False
    hand=eb['Hand'+side]; hand.tail=hand.head+Vector((0,0,-.09))
bpy.ops.object.mode_set(mode='OBJECT')
# End joints in the download extend a full calf below the soles; corrected endpoints do not alter skin weights.
rest={p.name:p.bone.matrix_local.copy() for p in arm.pose.bones}
for p in arm.pose.bones: p.rotation_mode='QUATERNION'
fps=60; scene.render.fps=fps
def q(axis, degrees): return Quaternion(Vector(axis),math.radians(degrees))
def pose_rot(name,axis,degrees): arm.pose.bones[name].rotation_quaternion=q(axis,degrees)
def head_pose(nod,tilt,turn):
    # Compound local rotations on the supplied unusual neck; no random per-frame noise.
    arm.pose.bones['Head'].rotation_quaternion=q((1,0,0),nod) @ q((0,1,0),tilt) @ q((0,0,1),turn)
def ease(a,b,t): return a+(b-a)*(t*t*(3-2*t))
def curve(keys,t):
    for (ta,va),(tb,vb) in zip(keys,keys[1:]):
        if ta<=t<=tb: return ease(va,vb,(t-ta)/(tb-ta))
    return keys[-1][1]
def solve_leg(side,ankle):
    a=arm.pose.bones['Thigh'+side]; b=arm.pose.bones['Calf'+side]; foot=arm.pose.bones['Foot'+side]
    bpy.context.view_layer.update()
    hip=a.head.copy(); target=Vector(ankle); delta=target-hip
    l1=a.bone.length; l2=b.bone.length; dist=max(abs(l1-l2)+.01,min(delta.length,l1+l2-.005)); direction=delta.normalized()
    pole=Vector((0,-1,0)); pole=(pole-direction*pole.dot(direction)).normalized()
    along=(l1*l1-l2*l2+dist*dist)/(2*dist); knee=hip+direction*along+pole*math.sqrt(max(0,l1*l1-along*along))
    def point(pb,start,end):
        current=pb.matrix.copy(); rot=(current.to_3x3()@Vector((0,1,0))).rotation_difference((end-start).normalized())
        pb.matrix=Matrix.Translation(start) @ rot.to_matrix().to_4x4() @ current.to_3x3().to_4x4()
        bpy.context.view_layer.update()
    point(a,hip,knee); point(b,knee,target)
    foot.matrix=Matrix.Translation(target) @ rest[foot.name].to_3x3().to_4x4()

clips=[('idle',8.0),('scream',1.65),('charge',.70)]
stride=5.6; contacts=[]
for name,duration in clips:
    arm.animation_data_clear(); action=bpy.data.actions.new(name); arm.animation_data_create(); arm.animation_data.action=action
    frames=round(duration*fps)
    for f in range(frames+1):
        scene.frame_set(f+1); t=f/fps; phase=t/duration
        for p in arm.pose.bones: p.matrix_basis=Matrix.Identity(4)
        hips=arm.pose.bones['Hips']
        if name=='idle':
            hips.matrix=Matrix.Translation((0,0,-.055)) @ rest['Hips']
            pose_rot('Chest',(1,0,0),9 + .35*math.sin(t*math.tau/4))
            tilt=curve([(0,0),(1.25,0),(1.38,-24),(1.48,-17),(3.1,-17),(3.23,27),(3.34,19),(4.8,19),(5.82,19),(5.92,-18),(6.02,22),(6.12,-12),(6.23,8),(6.4,5),(7.35,5),(7.8,0),(8,0)],t)
            turn=curve([(0,0),(2.1,0),(2.22,14),(2.35,10),(4.15,10),(4.27,-19),(4.4,-13),(6.45,-13),(6.6,3),(7.8,0),(8,0)],t)
            nod=curve([(0,0),(1.25,0),(1.38,7),(1.6,3),(4.8,3),(4.91,-8),(5.03,4),(5.18,0),(8,0)],t)
            head_pose(nod,tilt,turn)
            shoulder=curve([(0,0),(3.05,0),(3.2,7),(3.42,2),(5.7,2),(6.0,-3),(6.3,0),(8,0)],t)
            pose_rot('UpperArmL',(0,0,1),-5-shoulder); pose_rot('UpperArmR',(0,0,1),7+shoulder*.35)
            pose_rot('ForearmL',(0,0,1),-5); pose_rot('ForearmR',(0,0,1),8)
            for side in ('L','R'): solve_leg(side,rest['Foot'+side].translation)
        elif name=='scream':
            intensity=curve([(0,0),(.18,-.35),(.32,1.1),(.52,1),(.95,.9),(1.25,.5),(1.65,0)],t)
            crouch=curve([(0,-.055),(.18,-.10),(.32,-.04),(.6,-.08),(1.12,-.13),(1.65,-.19)],t)
            hips.matrix=Matrix.Translation((0,0,crouch)) @ rest['Hips']
            pose_rot('Chest',(1,0,0),9-18*intensity+curve([(0,0),(1.1,0),(1.65,14)],t))
            tilt=curve([(0,0),(.18,-13),(.32,28),(.5,21),(.60,-27),(.69,25),(.78,-20),(.88,18),(1.0,-12),(1.14,15),(1.3,12),(1.65,10)],t)
            turn=curve([(0,0),(.22,-8),(.36,13),(.61,-11),(.8,10),(1.06,-8),(1.25,4),(1.65,0)],t)
            head_pose(32*intensity-12*(t/duration),tilt,turn)
            pose_rot('UpperArmL',(0,0,1),-34*intensity); pose_rot('UpperArmR',(0,0,1),23*intensity)
            arm.pose.bones['ForearmL'].rotation_quaternion=q((1,0,0),-38*intensity) @ q((0,0,1),-12*intensity)
            arm.pose.bones['ForearmR'].rotation_quaternion=q((1,0,0),-55*intensity) @ q((0,0,1),15*intensity)
            for side in ('L','R'): solve_leg(side,rest['Foot'+side].translation)
        else:
            hips.matrix=Matrix.Translation((0,0,-.19 + curve([(0,0),(.20,-.035),(.40,.07),(.50,0),(.70,-.035),(.90,.07),(1,0)],phase))) @ rest['Hips']
            sway=curve([(0,-2),(.16,3),(.4,1),(.57,-4),(.85,-1),(1,-2)],phase)
            arm.pose.bones['Chest'].rotation_quaternion=q((1,0,0),23) @ q((0,0,1),sway)
            tilt=curve([(0,10),(.09,-12),(.17,16),(.30,10),(.50,10),(.61,-8),(.70,14),(.84,10),(1,10)],phase)
            nod=curve([(0,-12),(.12,-5),(.24,-15),(.42,-12),(.6,-7),(.73,-14),(1,-12)],phase)
            head_pose(nod,tilt,curve([(0,0),(.2,7),(.42,0),(.67,-9),(.86,0),(1,0)],phase))
            for side,offset in [('L',0),('R',.5)]:
                p=(phase+offset)%1; base=rest['Foot'+side].translation.copy()
                # A 20% stance travels 1.12m rearward at 8m/s; swing uses asymmetric authored lift/passing/landing.
                if p<.20: forward=.56-stride*p; height=0
                else:
                    s=(p-.20)/.80
                    forward=curve([(0,-.56),(.32,-.65),(.68,.30),(1,.56)],s)
                    height=curve([(0,0),(.25,.55),(.55,.65),(.82,.18),(1,0)],s)
                base.y-=forward; base.z+=height; solve_leg(side,base)
                swing=curve([(0,-28),(.2,15),(.55,35),(.85,-15),(1,-28)],p)
                arm.pose.bones['UpperArm'+side].rotation_quaternion=q((1,0,0),swing*(1 if side=='L' else .72)) @ q((0,0,1),-10 if side=='L' else 7)
                arm.pose.bones['Forearm'+side].rotation_quaternion=q((1,0,0),-38-abs(swing)*(.8 if side=='L' else .45)) @ q((0,0,1),-9 if side=='L' else 12)
        bpy.context.view_layer.update()
        for bone in arm.pose.bones:
            bone.keyframe_insert('location',frame=f+1,group=bone.name); bone.keyframe_insert('rotation_quaternion',frame=f+1,group=bone.name)
    action.use_fake_user=True
    scene.frame_start=1; scene.frame_end=frames+1
    for frame in ([1,187] if name=='idle' else [20,40,65] if name=='scream' else [1,8,15,22,29,36]):
        scene.frame_set(min(frame,frames+1)); scene.render.filepath=str(ROOT/f'artifacts/{name}-{frame:03}.png'); bpy.ops.render.render(write_still=True)
# Keep face and joints dense. Separate LOD objects share the same deforming rig.
arm.animation_data.action=bpy.data.actions['idle']; scene.frame_set(1)
original_triangles=sum(len(p.vertices)-2 for p in mesh.data.polygons)
weights=mesh.vertex_groups.new(name='Simplify')
for v in mesh.data.vertices:
    near_joint=min((v.co-arm.data.bones[n].head_local).length for n in ('ThighL','ThighR','CalfL','CalfR','ForearmL','ForearmR'))
    weight=.02 if v.co.z>2.18 or v.co.z<.23 or near_joint<.13 else 1
    weights.add([v.index],weight,'REPLACE')
lods=[]
for level,budget in enumerate((50000,20000,5000)):
    obj=mesh.copy(); obj.data=mesh.data.copy(); bpy.context.collection.objects.link(obj); obj.name=f'Crawler_LOD{level}'
    bpy.context.view_layer.objects.active=obj
    mod=obj.modifiers.new('ProtectedSimplification','DECIMATE'); mod.ratio=min(1,budget/original_triangles); mod.vertex_group='Simplify'; mod.vertex_group_factor=8
    # Apply simplification before armature deformation.
    bpy.ops.object.modifier_move_up(modifier=mod.name)
    bpy.ops.object.modifier_apply(modifier=mod.name)
    obj.data.calc_loop_triangles(); lods.append({'name':obj.name,'triangles':len(obj.data.loop_triangles),'vertices':len(obj.data.vertices)})
    obj.hide_render=level!=0
mesh.hide_render=True; mesh.hide_set(True)
scene.frame_set(1); scene.render.filepath=str(ROOT/'artifacts/optimized-idle.png'); bpy.ops.render.render(write_still=True)
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'ForestCrawler.blend'))
DEST.mkdir(parents=True,exist_ok=True)
for filename in ('Body.png','Eye.png'): shutil.copy2(ROOT/'assets/source/fbx'/filename,DEST/filename)
for path in (ROOT/'assets/source').glob('*.mp3'): shutil.copy2(path,DEST/path.name)
bpy.ops.object.select_all(action='DESELECT'); arm.select_set(True)
for record in lods:
    obj=bpy.data.objects[record['name']]; obj.hide_set(False); obj.select_set(True)
bpy.context.view_layer.objects.active=arm
bpy.ops.export_scene.fbx(filepath=str(DEST/'ForestCrawler.fbx'),use_selection=True,object_types={'ARMATURE','MESH'},add_leaf_bones=False,bake_anim=True,bake_anim_use_all_actions=True,bake_anim_use_nla_strips=False,bake_anim_simplify_factor=0,axis_forward='-Z',axis_up='Y',path_mode='COPY',embed_textures=False)
report={'height':2.5,'originalTriangles':original_triangles,'lods':lods,'clips':dict(clips),'strideMetres':stride,'chargeCycleSeconds':.70,'stanceEnd':.20,'mapping':mapping,'forward':'Unity +Z; Blender -Y'}
(DEST/'rig.json').write_text(json.dumps(report,indent=2)); (ROOT/'artifacts/asset-report.json').write_text(json.dumps(report,indent=2))
