/* Inserted into the pinned SDK GX recording fixture's main, while its real
 * static/skinned model, textures, camera and allocator instrumentation live. */
 {
  assert(animaltown_player_occlusion(1)==-1); /* no active 3D pass */
  assert(dnd_model_frame_begin(camera,camera)==0);
  assert(animaltown_player_occlusion(2)==-1);
  DndMaterial saved_materials[4];memcpy(saved_materials,materials,sizeof(materials));
  float saved_skin[18];memcpy(saved_skin,skinned,sizeof(skinned));
  for(int path=0;path<2;path++) {
   /* Existing native comparison switch is unavailable mid-frame. */
   assert(dnd_model_frame_end()==0&&dnd_model_indexed_path(path)==path);
   assert(dnd_model_frame_begin(camera,camera)==0);
   int start=draws;assert(dnd_model_draw(123,world)==0);
   Draw normal[3];for(int i=0;i<3;i++)normal[i]=record[(start+i)%16];
   size_t allocated=allocations;
   int normals=indexed_normals,positions=indexed_positions;
   assert(animaltown_player_occlusion(1)==1);
   start=draws;assert(dnd_model_draw(123,world)==0&&draws==start+3);
   assert(allocations==allocated&&indexed_normals==normals);
   assert(indexed_positions==positions+(path?9:0));
   for(int i=0;i<3;i++) {
    Draw hidden=record[(start+i)%16];
    assert(hidden.zcompare==GX_GREATER&&hidden.zwrite==0&&hidden.blend==GX_BM_NONE);
    assert(!memcmp(hidden.position,normal[i].position,sizeof(hidden.position)));
    assert(!memcmp(hidden.uv,normal[i].uv,sizeof(hidden.uv)));
    assert(hidden.cull==normal[i].cull); /* mirrored and two-sided meshes */
    assert(hidden.compare==(i==0?GX_GEQUAL:GX_GREATER));
    assert(hidden.cutoff==(i==0?normal[i].cutoff:0));
    assert(hidden.count==normal[i].count);
    for(int s=0;s<hidden.count;s++) {
     Stage stage=hidden.stages[s];
     assert(stage.a==GX_CC_ZERO&&stage.b==GX_CC_ZERO&&stage.c==GX_CC_ZERO&&stage.d==GX_CC_ONE);
     assert(stage.op==GX_TEV_ADD&&stage.dest==GX_TEVPREV);
     assert(!memcmp(stage.alpha,normal[i].stages[s].alpha,sizeof(stage.alpha)));
    }
   }
   assert(animaltown_player_occlusion(0)==0);
   start=draws;assert(dnd_model_draw(123,world)==0);
   for(int i=0;i<3;i++) {
    Draw restored=record[(start+i)%16];
    assert(restored.zcompare==GX_LEQUAL);
    assert_geometry_equal(restored,normal[i]);
    assert(!memcmp(restored.stages,normal[i].stages,sizeof(restored.stages)));
   }
   assert(!memcmp(saved_materials,materials,sizeof(materials)));
   assert(!memcmp(saved_skin,skinned,sizeof(skinned)));
   /* Multi-stage imported materials must also end white, retaining alpha. */
   for(int i=0;i<2;i++){materials[i].combiner=i+1;materials[i].secondary=0;}
   assert(animaltown_player_occlusion(1)==1);start=draws;
   assert(dnd_model_draw(123,world)==0);
   for(int i=0;i<2;i++) {
    Draw hidden=record[(start+i)%16];assert(hidden.count==3);
    for(int s=0;s<3;s++)assert(hidden.stages[s].d==GX_CC_ONE);
   }
   assert(animaltown_player_occlusion(0)==0);
   memcpy(materials,saved_materials,sizeof(materials));
   assert(allocations==allocated);
  }
  assert(dnd_model_frame_end()==0&&dnd_model_indexed_path(1)==1);
  puts("Player occlusion: reversed read-only depth, white RGB, cutouts, skin reuse, direct/indexed paths and state restoration passed.");
 }
