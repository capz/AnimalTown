namespace AnimalTown;

// Fixed 17x17 rough-water patch. Cache spatial phases until the patch moves;
// only four time sine/cosine pairs change per frame. Height remains the oracle.
public sealed class WindWakerWaveGrid
{
    private const int Width=WindWakerWaveField.Cells+1;
    private readonly double[] xCos=new double[4*Width],xSin=new double[4*Width];
    private readonly double[] zCos=new double[4*Width],zSin=new double[4*Width];
    private readonly double[] rowCos=new double[4*Width],rowSin=new double[4*Width];
    private float originX,originZ;
    private bool prepared;
    public void Prepare(float x0,float z0,double seconds)
    {
        if(!prepared||originX!=x0||originZ!=z0){
            for(int wave=0;wave<4;wave++)for(int i=0;i<Width;i++){
                double frequency=wave==0?1.20:wave==1?1.48:wave==2?1.82:2.13;
                double dx=wave==0?.98:wave==2?-.98:.20;
                double dz=wave==1?.98:wave==3?-.98:.20;
                double x=frequency*dx*(x0+i*WindWakerWaveField.CellSize);
                double z=frequency*dz*(z0+i*WindWakerWaveField.CellSize);
                int at=wave*Width+i;xCos[at]=Math.Cos(x);xSin[at]=Math.Sin(x);zCos[at]=Math.Cos(z);zSin[at]=Math.Sin(z);
            }
            originX=x0;originZ=z0;prepared=true;
        }
        for(int wave=0;wave<4;wave++){
            double speed=wave==0?.72:wave==1?.65:wave==2?.77:.70;
            double cosine=Math.Cos(seconds*speed),sine=Math.Sin(seconds*speed);
            for(int z=0;z<Width;z++){
                int at=wave*Width+z;
                rowCos[at]=zCos[at]*cosine+zSin[at]*sine;
                rowSin[at]=zSin[at]*cosine-zCos[at]*sine;
            }
        }
    }
    public float Height(int x,int z)
    {
        if(!prepared)throw new InvalidOperationException("Prepare wave phases before sampling.");
        if((uint)x>=Width||(uint)z>=Width)throw new ArgumentOutOfRangeException("cell");
        float edge=WindWakerWaveField.EdgeWeight(x,z);if(edge==0)return WindWakerWaveField.BaseLift;
        double sum=0;for(int wave=0;wave<4;wave++){
            int column=wave*Width+x,row=wave*Width+z;
            sum+=xCos[column]*rowCos[row]-xSin[column]*rowSin[row];
        }
        return WindWakerWaveField.BaseLift+edge*(float)(.11+.022*sum);
    }
}
