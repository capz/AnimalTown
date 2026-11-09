#include "dnd_model_internal.h"
#include <assert.h>
#include <stdio.h>
#include <stdlib.h>
bool dnd_device_active(void){return false;}
int dnd_fs_close(int handle){(void)handle;return 0;}
int main(int argc,char **argv){
    assert(argc>1);unsigned triangles=0;
    for(int part=1;part<argc;part++){
        FILE *file=fopen(argv[part],"rb");assert(file&&!fseek(file,0,SEEK_END));long length=ftell(file);assert(length>0&&length<8*1024*1024&&!fseek(file,0,SEEK_SET));
        unsigned char *bytes=malloc((size_t)length);assert(bytes&&fread(bytes,1,(size_t)length,file)==(size_t)length&&!fclose(file));
        int token=dnd_model_begin_data(bytes,(unsigned)length,8*1024*1024);free(bytes);assert(token);DndModel *model=dnd_model_find(token,false);assert(model&&model->ready);
        for(unsigned m=0;m<model->meshes;m++){
            DndMesh *mesh=&model->mesh[m];
            for(unsigned i=0;i<mesh->indices;i+=3){
                double p[3][3],n[3]={0};
                for(unsigned v=0;v<3;v++){const unsigned char *src=mesh->data+80*dnd_model_u32(mesh->index+(i+v)*4);for(unsigned c=0;c<3;c++){p[v][c]=dnd_model_f32(src+c*4);n[c]+=dnd_model_f32(src+12+c*4);}}
                double a[3],b[3];for(unsigned c=0;c<3;c++){a[c]=p[1][c]-p[0][c];b[c]=p[2][c]-p[0][c];}
                double dot=(a[1]*b[2]-a[2]*b[1])*n[0]+(a[2]*b[0]-a[0]*b[2])*n[1]+(a[0]*b[1]-a[1]*b[0])*n[2];
                assert(dot<=0);triangles++;
            }
        }
        assert(dnd_model_dispose(token)==0);
    }
    printf("Native model winding: %u clockwise triangles verified through the production loader\n",triangles);
}
