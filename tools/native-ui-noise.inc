/* Included once in draw_resources.c, using SDK-owned/accounted texture storage. */
#ifndef DND_GAMECUBE_STORAGE
__attribute__((weak)) void dnd_draw_backend_invalidate_cache(void){}
#endif
int animaltown_ui_noise_prepare(DndArray *package){
    if(dnd_device_pass()!=0||town_ui_noise_scope)return -1;
    int replacement=package?dnd_texture_package(package,1):0;
    if(package&&!replacement)return -1;
    if(replacement&&dnd_texture_sampler(replacement,1,1,5,1,0)<0){dnd_draw_release(replacement);return -1;}
    int old=town_ui_noise_texture;town_ui_noise_texture=replacement;
    if(old)dnd_draw_release(old);
    return 0;
}
int animaltown_ui_noise_match(const void *pixels){
    Resource *r=find(town_ui_noise_texture,TEXTURE);
    return town_ui_noise_scope&&dnd_device_pass()==2&&r&&r->data==pixels;
}
int animaltown_ui_noise_scope(int enabled){
    if(dnd_device_pass()!=2||(enabled!=0&&enabled!=1)||(enabled&&!find(town_ui_noise_texture,TEXTURE)))return -1;
    if(town_ui_noise_scope==enabled)return 0;
    if(dnd_canvas_flush(active_canvas)<0)return -1;
    town_ui_noise_scope=enabled;dnd_draw_backend_invalidate_cache();return 0;
}
