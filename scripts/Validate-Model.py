"""Measure exported source readiness and deformation; does not claim game/editor validation."""
import bpy, json, math
from pathlib import Path
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[1]
bpy.ops.wm.open_mainfile(filepath=str(ROOT/'assets/prepared/ForestCrawler.blend'))
arm=bpy.data.objects['CrawlerRig']; original=bpy.data.objects['CrawlerBody']; scene=bpy.context.scene
report={'checks':[],'deformations':[],'contactMaxDriftMetres':0,'source':'Blender 4.5.3; not Unity or Valheim'}
errors=[]
def check(name,condition):
    report['checks'].append({'name':name,'passed':bool(condition)})
    if not condition: errors.append(name)
def points(obj):
    evaluated=obj.evaluated_get(bpy.context.evaluated_depsgraph_get()); mesh=evaluated.to_mesh()
    vertices=[evaluated.matrix_world @ v.co for v in mesh.vertices]
    polygons=[tuple(p.vertices) for p in mesh.polygons]
    evaluated.to_mesh_clear()
    return vertices,polygons
def bounds(vertices): return ([min(v[i] for v in vertices) for i in range(3)],[max(v[i] for v in vertices) for i in range(3)])
for level in range(3):
    obj=bpy.data.objects[f'Crawler_LOD{level}']; obj.data.calc_loop_triangles()
    check(f'LOD{level} non-empty and weighted',len(obj.data.loop_triangles)>1000 and all(len(v.groups)>0 for v in obj.data.vertices))
    check(f'LOD{level} valid UV',len(obj.data.uv_layers)>0)
    check(f'LOD{level} armature',any(m.type=='ARMATURE' and m.object==arm for m in obj.modifiers))
for name,length in [('idle',8),('scream',1.65),('charge',.7)]:
    action=bpy.data.actions.get(name); check(name+' authored clip exists',action is not None)
    arm.animation_data.action=action
    scene.frame_set(1); bpy.context.view_layer.update()
    first={bone.name:bone.matrix.copy() for bone in arm.pose.bones}
    head_start=arm.pose.bones['Head'].rotation_quaternion.copy(); maximum_head=0; maximum_step=0; previous_head=head_start.copy()
    for frame in range(round(length*60)+1):
        scene.frame_set(frame+1); bpy.context.view_layer.update()
        head=arm.pose.bones['Head'].rotation_quaternion.copy()
        maximum_head=max(maximum_head,math.degrees(head_start.rotation_difference(head).angle))
        maximum_step=max(maximum_step,math.degrees(previous_head.rotation_difference(head).angle)); previous_head=head
    check(name+' bounded authored head movement',maximum_head>15 and maximum_head<100 and maximum_step<28)
    report.setdefault('headMotion',[]).append({'clip':name,'maxExcursionDegrees':maximum_head,'maxDegreesPer60HzFrame':maximum_step})
    if name in ('idle','charge'):
        check(name+' continuous loop pose',all((bone.matrix.translation-first[bone.name].translation).length<.002 and math.degrees(first[bone.name].to_quaternion().rotation_difference(bone.matrix.to_quaternion()).angle)<.2 for bone in arm.pose.bones))
    for normalized in [0,.15,.30,.50,.70,.85,1]:
        scene.frame_set(1+round(normalized*length*60)); bpy.context.view_layer.update()
        originalVertices,originalPolygons=points(original)
        baseline=BVHTree.FromPolygons(originalVertices,originalPolygons)
        for level in range(3):
            obj=bpy.data.objects[f'Crawler_LOD{level}']; vertices,polygons=points(obj)
            bvh=BVHTree.FromPolygons(vertices,polygons)
            # Bidirectional samples also detect silhouette regions deleted by simplification.
            distances=[bvh.find_nearest(v)[3] for v in originalVertices[::max(1,len(originalVertices)//1500)]]
            distances += [baseline.find_nearest(v)[3] for v in vertices[::max(1,len(vertices)//1500)]]
            maximum=max(d for d in distances if d is not None)
            report['deformations'].append({'clip':name,'phase':normalized,'lod':level,'maxSurfaceError':maximum,'bounds':bounds(vertices)})
            check(f'{name} {normalized:.2f} LOD{level} finite geometry',all(math.isfinite(c) and abs(c)<5 for v in vertices for c in v))
            check(f'{name} {normalized:.2f} LOD{level} surface preservation',maximum<[.055,.10,.20][level])
    if name=='charge':
        for side,offset in [('L',0),('R',.5)]:
            planted=[]
            for frame in range(43):
                t=frame/60; p=(t/.7+offset)%1
                scene.frame_set(frame+1); bpy.context.view_layer.update()
                if p < .20:
                    foot=arm.pose.bones['Foot'+side].head.copy()+Vector((0,-8*t,0))
                    if planted and frame==planted[-1][0]+1:
                        drift=(foot-planted[-1][1]).length
                        report['contactMaxDriftMetres']=max(report['contactMaxDriftMetres'],drift)
                    planted.append((frame,foot))
check('Authored stance drift under 1cm per 60Hz frame',report['contactMaxDriftMetres']<.01)
report['passed']=not errors; report['failures']=errors
(ROOT/'artifacts/model-validation.json').write_text(json.dumps(report,indent=2))
print('FORESTCRAWLER_MODEL_VALIDATION:',json.dumps({'passed':not errors,'checks':len(report['checks']),'contactMaxDriftMetres':report['contactMaxDriftMetres'],'failures':errors}))
if errors: raise RuntimeError('Model quality gate failed: '+', '.join(errors))
