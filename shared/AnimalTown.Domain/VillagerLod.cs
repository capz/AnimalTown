namespace AnimalTown;

// Thresholds derive from the fixed camera projection, not gameplay distance.
// Useful coarse meshes enter at 24px and retain up to 30px: native sampled
// fox animation measured .045224 normalized error, about 1.36px at return.
public static class VillagerLod
{
    public const float CoarsePixels=24,FullPixels=30,ModelHeight=1.8f;
    public const float AnimationCoarsePixels=48,AnimationFullPixels=60;
    public const float MaximumLocalError=.04f;
    public static bool CoarseAvailable(int species)=>species==0||species==1;
    public static float PixelHeight(float y,float z,float focusZ,float eyeHeight)
    {
        float depth=(y-CylinderWorld.CameraHeight-eyeHeight)*RenderView.ForwardY+
            (z-focusZ-CylinderWorld.CameraBack)*RenderView.ForwardZ;
        return depth<=.1f?float.MaxValue:ModelHeight*480/(2*RenderView.TanY*depth);
    }
    public static int Select(float pixels,int previous)=>previous==0?(pixels<=CoarsePixels?1:0):(pixels>=FullPixels?0:1);
    public static int SelectAnimation(float pixels,int previous)=>previous==0?(pixels<=AnimationCoarsePixels?1:0):(pixels>=AnimationFullPixels?0:1);
    public static float DepthAtPixels(float pixels)=>ModelHeight*480/(2*RenderView.TanY*pixels);
    public static int AnimationStride(int geometryLod)=>geometryLod==0?1:2;
}
