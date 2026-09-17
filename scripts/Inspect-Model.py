"""Inspect source geometry and rig without changing the supplied files."""
import bpy, json, sys
from pathlib import Path
from mathutils import Vector
root = Path(__file__).resolve().parents[1]
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
bpy.ops.import_scene.fbx(filepath=str(root/'assets/source/fbx/Smiley.fbx'))
report = {'objects': [], 'bones': []}
for obj in bpy.context.scene.objects:
    report['objects'].append({'name': obj.name, 'type': obj.type, 'dimensions': list(obj.dimensions), 'matrix': [list(r) for r in obj.matrix_world]})
    if obj.type == 'ARMATURE':
        for bone in obj.data.bones:
            report['bones'].append({'name': bone.name, 'parent': bone.parent.name if bone.parent else None, 'head': list(obj.matrix_world @ bone.head_local), 'tail': list(obj.matrix_world @ bone.tail_local)})
out = root/'artifacts'
out.mkdir(exist_ok=True)
(out/'source-inspection.json').write_text(json.dumps(report, indent=2))
bpy.ops.wm.save_as_mainfile(filepath=str(out/'source-inspection.blend'))
