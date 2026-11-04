namespace AnimalTown;

public static class VillagerNames
{
    public static string Generate(int seed,int model)
    {
        string[] first={"Am","Bel","Cor","Da","Em","Fen","Gil","Haz","Ivy","Jun","Kel","Lin","Mar","Nor","Or","Pip","Quin","Ros","Syl","Tor","Val","Wil"};
        string[] last={"ber","ley","rie","na","den","by","lie","son","ra","win","lo","ra","ton","sy","wen","na","vin","ly"};
        uint r=unchecked((uint)seed+(uint)(model+1)*2654435761u);r^=r>>16;r=unchecked(r*2246822519u);r^=r>>13;
        return first[(int)(r%(uint)first.Length)]+last[(int)((r>>8)%(uint)last.Length)];
    }
    public static string Legacy(int model)=>model==0?"Rowan":model==1?"Moss":model==2?"Sunny":model==3?"Mica":model==4?"Pip":"Rook";
}
