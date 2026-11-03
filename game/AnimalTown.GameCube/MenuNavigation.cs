using System;

// Allocation-free menu directions, shared by the D-pad and main stick.
internal sealed class MenuNavigation
{
    internal const int Left=1,Right=2,Down=4,Up=8;
    private int held,tab=-1;
    private string view="";
    private long repeatAt;
    private bool armed;

    internal int Read(string nextView,int nextTab,float x,float y,int pressed,long now,bool active)
    {
        float ax=Math.Abs(x),ay=Math.Abs(y);
        int direction=0;
        // Hysteresis prevents small changes around the engage threshold from
        // generating new presses. Use one dominant axis to avoid diagonal jumps.
        float threshold=held==0?.55f:.35f;
        if(Math.Max(ax,ay)>=threshold)direction=ax>ay?(x<0?Left:Right):(y<0?Down:Up);
        if(view!=nextView||tab!=nextTab||!active){
            view=nextView;tab=nextTab;held=0;armed=direction==0;
        }
        if(!active)return 0;
        int result=0;
        if(direction==0){held=0;armed=true;}
        else if(armed){
            if(direction!=held){held=direction;repeatAt=now+350000;result=direction;}
            else if(now>=repeatAt){repeatAt=now+120000;result=direction;}
        }
        // A physical D-pad press wins over the stick in the same update.
        pressed&=15;
        if((pressed&Up)!=0)return Up;
        if((pressed&Down)!=0)return Down;
        if((pressed&Left)!=0)return Left;
        if((pressed&Right)!=0)return Right;
        return result;
    }
}
