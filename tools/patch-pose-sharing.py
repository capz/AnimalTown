"""Add exact live-pose reuse to the copied SDK evaluator, preserving public ABI."""
from pathlib import Path
import sys,shutil
root=Path(sys.argv[1]);game=Path(__file__).resolve().parent
shutil.copyfile(game/'native-pose-sharing.inc',root/'source/native-pose-sharing.inc')
p=root/'source/model_animation.c';s=p.read_text()
changes=[('static int next_instance = 1;','#include "native-pose-sharing.inc"\nstatic int next_instance = 1;'),('int dnd_model_evaluate(DndInstance *in) {','static int town_pose_evaluate_base(DndInstance *in) {'),('      dnd_model_free(in->vertices[i], &in->bytes);','      town_pose_drop(in,i);'),('  dnd_model_drop_vertices(in);\n  dnd_model_free(in->vertices,','  town_pose_forget(in);\n  dnd_model_drop_vertices(in);\n  dnd_model_free(in->vertices,')]
for old,new in changes:
 if s.count(old)!=1:raise RuntimeError('Pinned pose-sharing hook changed: '+old)
 s=s.replace(old,new)
p.write_text(s)
