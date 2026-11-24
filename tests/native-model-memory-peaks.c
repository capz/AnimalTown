/* Production-loader allocation/pose audit for actual cooked DNMD packages.
 * Compile with model.c/model_animation.c/verified_copy.c/aram.c, function/data
 * sections + --gc-sections, and individual --wrap options for malloc, calloc,
 * realloc, aligned_alloc and free. Define ANIMALTOWN_MODEL_PEAK_RETAIN when
 * linking the unpatched SDK to audit the production selected-player compaction.
 * Usage: native-model-memory-peaks [--catalog FILE] [--max-bind-error FLOAT]
 *   [--max-pose-error FLOAT] [--cohort-size COUNT] [PACKAGE ...]
 *   [--pair FULL LOD MAP ...]
 * JSON lines separate native allocations from conservative caller-package
 * overlap. Host pointers are wider than console pointers; allocator bookkeeping
 * outside requested blocks, renderer/managed jobs and hardware are not measured.
 */
#include "dnd_model_internal.h"
#include "dnd_aram.h"
#include <assert.h>
#include <limits.h>
#include <math.h>
#include <stdbool.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>
#include <string.h>

void *__real_malloc(size_t);
void *__real_calloc(size_t,size_t);
void *__real_realloc(void*,size_t);
void *__real_aligned_alloc(size_t,size_t);
void __real_free(void*);
static struct {void *data;size_t bytes;} allocations[8192];
static size_t native_bytes,external_bytes,peak_native,peak_total,largest_allocation,live;
static double maximum_bind_error=.0125;
static double maximum_pose_error=.05;
static unsigned cohort_size=8; /* Current BuildingWorld.Actors, including visitors. */
static bool initializing_aram;
static void *host_aram_memory;
static struct {unsigned package,hash,shared,instance,one,two;} catalog[6];
static bool has_catalog;
static void sample(void){
    if(native_bytes>peak_native)peak_native=native_bytes;
    if(native_bytes+external_bytes>peak_total)peak_total=native_bytes+external_bytes;
}
static void track(void *data,size_t bytes){
    if(!data)return;
    for(unsigned i=0;i<sizeof allocations/sizeof *allocations;i++)if(!allocations[i].data){
        allocations[i].data=data;allocations[i].bytes=bytes;native_bytes+=bytes;live++;
        if(bytes>largest_allocation)largest_allocation=bytes;
        sample();return;
    }
    assert(!"allocation tracker capacity exceeded");
}
static size_t forget(void *data){
    if(!data)return 0;
    for(unsigned i=0;i<sizeof allocations/sizeof *allocations;i++)if(allocations[i].data==data){
        size_t bytes=allocations[i].bytes;allocations[i].data=NULL;native_bytes-=bytes;live--;return bytes;
    }
    assert(!"untracked native free");return 0;
}
void *__wrap_malloc(size_t bytes){void *p=__real_malloc(bytes);track(p,bytes);return p;}
void *__wrap_calloc(size_t count,size_t bytes){
    assert(!count||bytes<=SIZE_MAX/count);void *p=__real_calloc(count,bytes);
    // The host backend represents separate ARAM as a 16MiB heap allocation.
    // Exclude that one emulation block from console MEM1 admission evidence.
    if(initializing_aram){assert(!host_aram_memory&&count*bytes==16*1024*1024);host_aram_memory=p;return p;}
    track(p,count*bytes);return p;
}
void *__wrap_aligned_alloc(size_t align,size_t bytes){void *p=__real_aligned_alloc(align,bytes);track(p,bytes);return p;}
void *__wrap_realloc(void *old,size_t bytes){
    if(!old)return __wrap_malloc(bytes);
    if(!bytes){forget(old);__real_free(old);return NULL;}
    size_t previous=forget(old);void *p=__real_realloc(old,bytes);
    if(!p){track(old,previous);return NULL;}track(p,bytes);return p;
}
void __wrap_free(void *p){if(p&&p==host_aram_memory)host_aram_memory=NULL;else forget(p);__real_free(p);}
bool dnd_device_active(void){return false;}
int dnd_fs_close(int file){(void)file;return 0;}
#ifdef ANIMALTOWN_MODEL_PEAK_RETAIN
#include "../tools/native-building-retained.inc"
#endif

static void reset_peak(void){peak_native=native_bytes;peak_total=native_bytes+external_bytes;largest_allocation=0;}
static unsigned char *read_file(const char *path,unsigned *length){
    FILE *file=fopen(path,"rb");
    if(!file){perror(path);exit(2);}
    assert(!fseek(file,0,SEEK_END));long bytes=ftell(file);
    assert(bytes>0&&bytes<=DND_MODEL_LIMIT&&!fseek(file,0,SEEK_SET));
    unsigned char *data=__real_malloc((size_t)bytes);assert(data);
    assert(fread(data,1,(size_t)bytes,file)==(size_t)bytes&&!fclose(file));
    *length=(unsigned)bytes;return data;
}
static unsigned little(const unsigned char *p){return (unsigned)p[0]|(unsigned)p[1]<<8|(unsigned)p[2]<<16|(unsigned)p[3]<<24;}
static int species(const char *path){
    const char *base=strrchr(path,'/');base=base?base+1:path;
    static const char *names[]={"fox","bunny","giraffe","cat","hamster","spaniel"};
    for(int i=0;i<6;i++)if(!strncmp(base,names[i],strlen(names[i]))&&base[strlen(names[i])]=='-')return i;
    assert(!"unknown model catalog species");return -1;
}
static void read_catalog(const char *path){
    assert(!has_catalog&&!native_bytes);unsigned length;unsigned char *bytes=read_file(path,&length);
    assert(length==160&&little(bytes)==0x564d454d&&little(bytes+4)==1&&little(bytes+8)==6&&little(bytes+12)==24);
    for(unsigned i=0;i<6;i++){
        const unsigned char *p=bytes+16+i*24;
        catalog[i].package=little(p);catalog[i].hash=little(p+4);catalog[i].shared=little(p+8);
        catalog[i].instance=little(p+12);catalog[i].one=little(p+16);catalog[i].two=little(p+20);
        assert(catalog[i].package>=160&&catalog[i].package<=DND_MODEL_LIMIT&&catalog[i].shared>=catalog[i].package&&catalog[i].instance>=512);
        uint64_t one=(uint64_t)catalog[i].shared+((catalog[i].package+63u)&~31u)+catalog[i].instance+64*1024;
        assert(catalog[i].one==one&&catalog[i].two==one+catalog[i].instance);
    }
    __real_free(bytes);has_catalog=true;
}
static void json_string(const char *text){
    putchar('"');for(;*text;text++){
        unsigned char c=(unsigned char)*text;
        if(c=='"'||c=='\\'){putchar('\\');putchar(c);}
        else if(c<32)printf("\\u%04x",c);else putchar(c);
    }putchar('"');
}
typedef struct {int model,instance;unsigned package,hash,model_bytes,instance_bytes;} Loaded;
static Loaded load_kind(const char *path,bool retain){
    Loaded result={0};unsigned char *source=read_file(path,&result.package);
    external_bytes+=result.package;sample();
    result.model=dnd_model_begin_data(source,result.package,3*1024*1024);
    external_bytes-=result.package;__real_free(source);
    if(!result.model){fprintf(stderr,"Production model load failed: %s (package %u, measured peak %zu)\n",path,result.package,peak_total);exit(3);}
    DndModel *model=dnd_model_find(result.model,false);assert(model&&model->ready);
    result.hash=model->hash;
#ifdef ANIMALTOWN_MODEL_PEAK_RETAIN
    if(retain)assert(animaltown_model_retain(result.model)==1);
#else
    assert(!retain);
#endif
    result.model_bytes=model->bytes;
    result.instance=dnd_model_instance(result.model,512*1024);
    if(!result.instance){fprintf(stderr,"Production instance exceeds budget: %s\n",path);exit(3);}
    result.instance_bytes=dnd_instance_find(result.instance)->bytes;
    return result;
}
static Loaded load(const char *path){return load_kind(path,false);}
static void unload(Loaded value){assert(!dnd_model_instance_dispose(value.instance)&&!dnd_model_dispose(value.model));}
static void exercise(Loaded value){
    DndModel *model=dnd_model_find(value.model,false);
    for(unsigned clip=0;clip<model->clips;clip++){
        assert(!dnd_model_play(value.instance,(int)clip,1,0));
        for(int sample=0;sample<=8;sample++)assert(!dnd_model_control(value.instance,4,model->clip[clip].duration*sample/8.f));
        assert(!dnd_model_play(value.instance,(int)((clip+1)%model->clips),1,.2f));
        for(int frame=0;frame<15;frame++)assert(!dnd_model_control(value.instance,0,1.f/60));
    }
    // Retire and rebuild exactly the production skin outputs and joint palette.
    DndInstance *instance=dnd_instance_find(value.instance);
    dnd_model_drop_vertices(instance);
    assert(!dnd_model_evaluate(instance)&&instance->bytes==value.instance_bytes);
}
static Loaded cold(const char *path){
    assert(!native_bytes&&!external_bytes&&!live&&!dnd_model_live_handles());reset_peak();
    Loaded value=load(path);exercise(value);
    DndModel *model=dnd_model_find(value.model,false);unsigned vertices=0,triangles=0,skinned=0;
    for(unsigned i=0;i<model->meshes;i++){
        vertices+=model->mesh[i].vertices;triangles+=model->mesh[i].indices/3;
        if(model->mesh[i].skin>=0)skinned+=model->mesh[i].vertices;
    }
    // Budget charges include texture ownership even when equal images share a
    // cache allocation, including duplicate images within the same package.
    assert(native_bytes<=(size_t)value.model_bytes+value.instance_bytes);
    size_t bound=(size_t)value.model_bytes+value.package+value.instance_bytes+64*1024;
    if(has_catalog){
        int index=species(path);
        if(!strstr(path,"-lod1-"))assert(value.package==catalog[index].package&&value.hash==catalog[index].hash);
        else assert(value.package<=catalog[index].package);
        assert(value.model_bytes<=catalog[index].shared&&value.instance_bytes<=catalog[index].instance);
        bound=catalog[index].one;
    }
    assert(peak_total<=bound);
    printf("{\"kind\":\"cold\",\"path\":");json_string(path);
    printf(",\"pointerBytes\":%zu,\"packageBytes\":%u,\"modelNativeBytes\":%u,\"instanceNativeBytes\":%u,\"retainedNativeBytes\":%zu,\"peakNativeBytes\":%zu,\"peakWithCallerPackageBytes\":%zu,\"largestAllocationBytes\":%zu,\"admissionBoundBytes\":%zu,\"vertices\":%u,\"triangles\":%u,\"skinnedVertices\":%u,\"clips\":%u}\n",
        sizeof(void*),value.package,value.model_bytes,value.instance_bytes,native_bytes,peak_native,peak_total,largest_allocation,bound,vertices,triangles,skinned,model->clips);
    unload(value);assert(!native_bytes&&!live&&!dnd_model_live_handles());return value;
}
static void extra_instances(Loaded value,unsigned count,int extra[31]);
static void dispose_extra(unsigned count,int extra[31]);
static void selected(const char *path,Loaded original,unsigned count){
#ifdef ANIMALTOWN_MODEL_PEAK_RETAIN
    int extra[31];reset_peak();Loaded value=load_kind(path,true);
    extra_instances(value,count,extra);exercise(value);
    size_t bound=(size_t)original.model_bytes+original.package+count*original.instance_bytes+64*1024;
    if(has_catalog){int index=species(path);bound=(size_t)catalog[index].one+(count-1)*catalog[index].instance;}
    assert(peak_total<=bound);
    printf("{\"kind\":\"%s\",\"path\":",count==2?"selectedStartup":"selectedCohortStartup");json_string(path);
    printf(",\"compactedModelNativeBytes\":%u,\"instances\":%u,\"retainedNativeBytes\":%zu,\"peakNativeBytes\":%zu,\"peakWithCallerPackageBytes\":%zu,\"admissionBoundBytes\":%zu}\n",
        value.model_bytes,count,native_bytes,peak_native,peak_total,bound);
    dispose_extra(count,extra);unload(value);assert(!native_bytes&&!live&&!dnd_model_live_handles());
#else
    (void)path;(void)original;(void)count;
#endif
}
static unsigned run_residency(Loaded value,int aram,bool restore){
    assert(!dnd_model_residency_begin(value.model,aram,restore));unsigned steps=0;int rc;
    do{
        rc=dnd_model_residency_step(value.model,4096,64,0);
        (void)dnd_aram_service_broker();assert(++steps<100000);
    }while(rc==0||rc==2);
    assert(rc==1);return steps;
}
static void residency(const char *path,Loaded original){
    if(!dnd_aram_available()){
        initializing_aram=true;assert(dnd_aram_initialize(2*1024*1024));initializing_aram=false;
    }
    Loaded value=load(path);exercise(value);size_t resident=native_bytes;reset_peak();
    int aram=dnd_aram_allocate((int)((value.package+31)&~31u));assert(aram);
    unsigned retire_steps=run_residency(value,aram,false);size_t cold_bytes=native_bytes;
    assert(!dnd_model_find(value.model,false)->ready&&cold_bytes<resident);
    reset_peak();unsigned restore_steps=run_residency(value,aram,true);exercise(value);
    size_t bound=(size_t)original.model_bytes+original.instance_bytes+64*1024;
    if(has_catalog){int index=species(path);bound=(size_t)catalog[index].shared+catalog[index].instance+64*1024;}
    assert(native_bytes==resident&&peak_native-cold_bytes<=bound);
    printf("{\"kind\":\"aramRestore\",\"path\":");json_string(path);
    printf(",\"coldNativeBytes\":%zu,\"residentNativeBytes\":%zu,\"peakAdditionalNativeBytes\":%zu,\"admissionBoundBytes\":%zu,\"retireSteps\":%u,\"restoreSteps\":%u,\"hostAramEmulationExcludedBytes\":%u}\n",
        cold_bytes,resident,peak_native-cold_bytes,bound,retire_steps,restore_steps,16*1024*1024);
    assert(!dnd_aram_dispose(aram));unload(value);assert(!native_bytes&&!live&&!dnd_model_live_handles());
}
static void extra_instances(Loaded value,unsigned count,int extra[31]){
    assert(count>=1&&count<=32);
    for(unsigned i=1;i<count;i++){
        extra[i-1]=dnd_model_instance(value.model,512*1024);assert(extra[i-1]);
        Loaded actor=value;actor.instance=extra[i-1];exercise(actor);
        assert(dnd_instance_find(actor.instance)->bytes==value.instance_bytes);
    }
}
static void dispose_extra(unsigned count,int extra[31]){
    for(unsigned i=1;i<count;i++)assert(!dnd_model_instance_dispose(extra[i-1]));
}
static void cohort(const char *path,Loaded original){
    int extra[31];reset_peak();Loaded value=load(path);extra_instances(value,cohort_size,extra);exercise(value);
    size_t resident=native_bytes;
    size_t bound=(size_t)original.model_bytes+original.package+cohort_size*original.instance_bytes+64*1024;
    if(has_catalog){int index=species(path);bound=(size_t)catalog[index].one+(cohort_size-1)*catalog[index].instance;}
    assert(peak_total<=bound);
    printf("{\"kind\":\"cohortStartup\",\"path\":");json_string(path);
    printf(",\"instances\":%u,\"retainedNativeBytes\":%zu,\"peakNativeBytes\":%zu,\"peakWithCallerPackageBytes\":%zu,\"admissionBoundBytes\":%zu}\n",
        cohort_size,resident,peak_native,peak_total,bound);
    int aram=dnd_aram_allocate((int)((value.package+31)&~31u));assert(aram);
    run_residency(value,aram,false);size_t cold_bytes=native_bytes;reset_peak();
    run_residency(value,aram,true);exercise(value);
    bound=(size_t)original.model_bytes+cohort_size*original.instance_bytes+64*1024;
    if(has_catalog){int index=species(path);bound=(size_t)catalog[index].shared+cohort_size*catalog[index].instance+64*1024;}
    assert(native_bytes==resident&&peak_native-cold_bytes<=bound);
    printf("{\"kind\":\"cohortAramRestore\",\"path\":");json_string(path);
    printf(",\"instances\":%u,\"coldNativeBytes\":%zu,\"residentNativeBytes\":%zu,\"peakAdditionalNativeBytes\":%zu,\"admissionBoundBytes\":%zu}\n",
        cohort_size,cold_bytes,resident,peak_native-cold_bytes,bound);
    assert(!dnd_aram_dispose(aram));dispose_extra(cohort_size,extra);unload(value);assert(!native_bytes&&!live&&!dnd_model_live_handles());
}
static void swap(const char *from,const char *to,size_t bound,unsigned count){
    int old_extra[31],new_extra[31];
    Loaded old=load(from);extra_instances(old,count,old_extra);exercise(old);size_t base=native_bytes;reset_peak();
    Loaded next=load(to);extra_instances(next,count,new_extra);exercise(next);
    size_t additional=peak_total-base;assert(additional<=bound);
    printf("{\"kind\":\"%s\",\"from\":",count==1?"swap":"cohortSwap");json_string(from);printf(",\"to\":");json_string(to);
    printf(",\"instances\":%u,\"existingNativeBytes\":%zu,\"peakAdditionalNativeBytes\":%zu,\"peakAdditionalWithCallerPackageBytes\":%zu,\"admissionBoundBytes\":%zu,\"marginBytes\":%zu}\n",
        count,base,peak_native-base,additional,bound,bound-additional);
    dispose_extra(count,old_extra);dispose_extra(count,new_extra);unload(old);unload(next);assert(!native_bytes&&!live&&!dnd_model_live_handles());
}
static void transformed_vertex(Loaded value,unsigned mesh,unsigned vertex,float result[3]){
    DndInstance *instance=dnd_instance_find(value.instance);float p[6];
    assert(!dnd_model_vertex(value.instance,(int)mesh,(int)vertex,p));
    if(instance->model->mesh[mesh].skin>=0){memcpy(result,p,12);return;}
    const float *matrix=instance->global+(unsigned)instance->model->mesh[mesh].node*16;
    for(int i=0;i<3;i++)result[i]=matrix[i]*p[0]+matrix[4+i]*p[1]+matrix[8+i]*p[2]+matrix[12+i];
}
static double compare_pose(Loaded full,Loaded coarse,const unsigned char *map,unsigned length,unsigned *probes){
    DndModel *a=dnd_model_find(full.model,false),*b=dnd_model_find(coarse.model,false);
    assert(length>=4&&dnd_model_u32(map)==a->meshes&&a->meshes==b->meshes);
    unsigned at=4;double maximum=0;
    for(unsigned mesh=0;mesh<a->meshes;mesh++){
        assert(at<=length-4);unsigned vertices=dnd_model_u32(map+at);at+=4;
        assert(vertices==a->mesh[mesh].vertices&&vertices<=(length-at)/4);
        // A missing map is permissible only for unused source vertices. Check
        // the original index stream independently of the offline collapse map.
        for(unsigned index=0;index<a->mesh[mesh].indices;index++){
            unsigned vertex=dnd_model_u32(a->mesh[mesh].index+index*4);
            assert(vertex<vertices&&(int32_t)dnd_model_u32(map+at+vertex*4)>=0);
        }
        for(unsigned vertex=0;vertex<vertices;vertex++,at+=4){
            int target=(int32_t)dnd_model_u32(map+at);if(target<0){assert(target==-1);continue;}
            assert((unsigned)target<b->mesh[mesh].vertices);
            float x[3],y[3];transformed_vertex(full,mesh,vertex,x);transformed_vertex(coarse,mesh,(unsigned)target,y);
            double distance=0;for(int i=0;i<3;i++){assert(isfinite(x[i])&&isfinite(y[i]));double d=(double)x[i]-y[i];distance+=d*d;}
            distance=sqrt(distance);if(distance>maximum)maximum=distance;(*probes)++;
        }
    }
    assert(at==length);return maximum;
}
static void pose_audit(const char *full_path,const char *coarse_path,const char *map_path){
    Loaded full=load(full_path),coarse=load(coarse_path);unsigned length,probes=0;
    unsigned char *map=read_file(map_path,&length);
    DndModel *a=dnd_model_find(full.model,false),*b=dnd_model_find(coarse.model,false);
    assert(a->clips==b->clips&&a->nodes==b->nodes);
    double bind=compare_pose(full,coarse,map,length,&probes),maximum=bind;unsigned worst_clip=0,worst_sample=0;
    for(unsigned clip=0;clip<a->clips;clip++){
        assert(!strcmp(a->clip[clip].name,b->clip[clip].name)&&a->clip[clip].duration==b->clip[clip].duration);
        assert(!dnd_model_play(full.instance,(int)clip,0,0)&&!dnd_model_play(coarse.instance,(int)clip,0,0));
        for(unsigned sample=0;sample<=32;sample++){
            float time=a->clip[clip].duration*sample/32.f;
            assert(!dnd_model_control(full.instance,4,time)&&!dnd_model_control(coarse.instance,4,time));
            double error=compare_pose(full,coarse,map,length,&probes);
            if(error>maximum){maximum=error;worst_clip=clip;worst_sample=sample;}
        }
    }
    // Bind error is the cooker's declared normalized-world contract; animated
    // error is reported independently, never inferred from weight similarity.
    assert(bind<=maximum_bind_error+1e-6);
    assert(maximum<=maximum_pose_error+1e-6);
    printf("{\"kind\":\"animatedCorrespondence\",\"full\":");json_string(full_path);printf(",\"coarse\":");json_string(coarse_path);
    printf(",\"bindMaximumNormalizedError\":%.9g,\"animatedMaximumNormalizedError\":%.9g,\"maximumAllowedNormalizedError\":%.9g,\"samplesPerClip\":33,\"vertexProbes\":%u,\"worstClip\":%u,\"worstSample\":%u}\n",bind,maximum,maximum_pose_error,probes,worst_clip,worst_sample);
    __real_free(map);unload(full);unload(coarse);assert(!native_bytes&&!live&&!dnd_model_live_handles());
}
int main(int argc,char **argv){
    if(argc<2){fprintf(stderr,"Usage: %s PACKAGE ... | --pair FULL COARSE MAP ...\n",argv[0]);return 2;}
    for(int arg=1;arg<argc;arg++){
        if(!strcmp(argv[arg],"--catalog")){assert(arg+1<argc);read_catalog(argv[++arg]);continue;}
        if(!strcmp(argv[arg],"--cohort-size")){
            assert(arg+1<argc);char *end;unsigned long count=strtoul(argv[++arg],&end,10);
            assert(!*end&&count>=1&&count<=32);cohort_size=(unsigned)count;continue;
        }
        if(!strcmp(argv[arg],"--max-bind-error")){
            assert(arg+1<argc);char *end;maximum_bind_error=strtod(argv[++arg],&end);
            assert(!*end&&isfinite(maximum_bind_error)&&maximum_bind_error>0&&maximum_bind_error<1);continue;
        }
        if(!strcmp(argv[arg],"--max-pose-error")){
            assert(arg+1<argc);char *end;maximum_pose_error=strtod(argv[++arg],&end);
            assert(!*end&&isfinite(maximum_pose_error)&&maximum_pose_error>0&&maximum_pose_error<1);continue;
        }
        if(strcmp(argv[arg],"--pair")){Loaded stats=cold(argv[arg]);selected(argv[arg],stats,2);residency(argv[arg],stats);continue;}
        assert(arg+3<argc);const char *full=argv[++arg],*coarse=argv[++arg],*map=argv[++arg];
        Loaded full_stats=cold(full);selected(full,full_stats,2);
        if(cohort_size>2)selected(full,full_stats,cohort_size);
        residency(full,full_stats);cold(coarse);
        size_t bound=(size_t)full_stats.model_bytes+full_stats.package+full_stats.instance_bytes+64*1024;
        if(has_catalog)bound=catalog[species(full)].one;
        swap(full,coarse,bound,1);swap(coarse,full,bound,1);
        if(cohort_size>1){
            cohort(full,full_stats);
            size_t cohort_bound=bound+(cohort_size-1)*(has_catalog?catalog[species(full)].instance:full_stats.instance_bytes);
            swap(full,coarse,cohort_bound,cohort_size);swap(coarse,full,cohort_bound,cohort_size);
        }
        pose_audit(full,coarse,map);
    }
    return 0;
}
