#ifndef ANIMALTOWN_FACE_CULLING_H
#define ANIMALTOWN_FACE_CULLING_H
/* GX enums: none=0, front=1, back=2. Mirroring reverses projected winding.
 * Every 3D pass culls backfaces, regardless of imported double-sided flags.
 * Overlay passes retain their requested state and orthographic UI winding. */
static inline unsigned town_face_cull(int scene3d,int requested,float determinant){
    return scene3d||requested?(determinant<0?1u:2u):0u;
}
#endif
