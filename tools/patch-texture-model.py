"""Patch the copied, pinned loader; never modify the SDK package or checkout."""
from pathlib import Path
import sys
root=Path(sys.argv[1]);path=root/'source/model.c';source=path.read_text()
def replace(old,new):
 global source
 if source.count(old)!=1:raise RuntimeError('Pinned external texture loader changed: '+old)
 source=source.replace(old,new)
source='#include "native-texture-stream.h"\n'+source
replace('(dnd_model_u32(package + 4) != 1 && dnd_model_u32(package + 4) != 2)','(dnd_model_u32(package + 4) != 1 && dnd_model_u32(package + 4) != 2 && dnd_model_u32(package + 4) != 3)')
replace('if(dnd_model_u32(package+4)==2)','if(dnd_model_u32(package+4)>=2)')
replace('    unsigned size = 0;','''    if(dnd_model_u32(package+4)==3){
      unsigned id=t->size,identity=number(&r[3]);
      if(!r[3].ok||!town_texture_identity(id,t->width,t->height,identity))return false;
      if(town_texture_acquire(t,id)!=1)return false;
      continue;
    }
    unsigned size = 0;''')
replace('      m->bytes -= (t->size + 63u) & ~31u;','''      if(t->cache < -1){town_texture_release(t);continue;}
      m->bytes -= (t->size + 63u) & ~31u;''')
path.write_text(source)
