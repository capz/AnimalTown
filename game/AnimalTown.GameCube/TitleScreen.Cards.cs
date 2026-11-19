using AnimalTown;
using Dolphin.Diagnostics;
using Dolphin.Graphics;

internal sealed partial class TitleScreen
{
    private bool cardOpen;
    private int cardChoice;
    private UiFont? cardHeading;
    private SaveCardInfo? cardA,cardB;
    private string cardMessage="";
    private bool ChooseStartCard()
    {
        RefreshCards();
        if(SaveCards.TryAutoSelect(cardA!,cardB!)){
            cardA=cardB=null;
            DebugOverlay.WriteLine("ANIMALTOWN SAVE CARD AUTO "+SaveCards.Device);
            DebugOverlay.WriteLine("ANIMALTOWN SAVE CARD SELECTED "+SaveCards.Device);
            return true;
        }
        cardHeading=UiFont.Load("gorditas-bold");cardOpen=true;cardChoice=SaveCards.DefaultChoice(cardA!,cardB!);
        DebugOverlay.WriteLine("ANIMALTOWN SAVE CARD CHOOSER default "+cardChoice);
        return false;
    }
    private void RefreshCards()
    {
        cardA=SaveCards.Inspect(4);cardB=SaveCards.Inspect(5);cardMessage="";
        DebugOverlay.WriteLine("ANIMALTOWN SAVE CARD A "+cardA.Status+" / B "+cardB.Status);
    }
    private void CloseCards()
    {cardOpen=false;cardHeading?.Release();cardHeading=null;cardA=null;cardB=null;TextureCache.TrimUnused();}
    private bool CardInput(int direction,bool activate,bool back,bool recheck)
    {
        if(back){CloseCards();return false;}
        if(recheck){cardMessage="Checking memory cards...";Draw();RefreshCards();return false;}
        if(direction==MenuNavigation.Up||direction==MenuNavigation.Down){
            int step=direction==MenuNavigation.Up?2:1;
            do{cardChoice=(cardChoice+step)%3;}while(cardChoice<2&&!(cardChoice==0?cardA!:cardB!).CanSave);
            cardMessage="";
        }
        if(!activate)return false;
        if(cardChoice==2){SaveCards.WithoutSaving();DebugOverlay.WriteLine("ANIMALTOWN SAVE CARD NONE");CloseCards();return true;}
        // Recheck at confirmation in case a card was exchanged while the menu
        // was open. Selecting a slot does not create a save or format a card.
        var info=SaveCards.Inspect(cardChoice+4);
        if(cardChoice==0)cardA=info;else cardB=info;
        if(!SaveCards.Select(info)){cardMessage="Check the card, then press X to recheck.";return false;}
        DebugOverlay.WriteLine("ANIMALTOWN SAVE CARD SELECTED "+SaveCards.Device);CloseCards();return true;
    }
    private void DrawCards()
    {
        TitleArt.Panel(ui,76,64,488,352);
        const string heading="Memory card";
        cardHeading!.Draw(ui,heading,320-cardHeading.Measure(heading,1.25f)*.5f,88,TitleArt.Ink,1.25f);
        CenteredText("Choose where to keep your town.",new RectF(100,132,440,28),TitleArt.Ink,.9f);
        if(cardA!=null&&cardB!=null){
            CardRow(0,"Slot A",cardA.Status,cardA.CanSave,178);
            CardRow(1,"Slot B",cardB.Status,cardB.CanSave,232);
            CardRow(2,"Play without saving","",true,286);
        }
        string note=cardMessage.Length>0?cardMessage:cardChoice==2?"Temporary town. Progress will not be saved.":"Your choice is used for all saves this session.";
        CenteredText(note,new RectF(92,345,456,24),TitleArt.Ink,.72f);
        CenteredText("A: continue   X: recheck   B: back",new RectF(100,378,440,24),TitleArt.Ink,.8f);
    }
    private void CardRow(int choice,string label,string detail,bool usable,float y)
    {
        bool focus=cardChoice==choice;
        TitleArt.Rounded(ui,100,y,440,42,20,focus&&usable?TitleArt.Leaf:new Color32(236,226,197));
        var ink=focus&&usable?TitleArt.Cream:usable?TitleArt.Ink:new Color32(128,135,112);
        font.Draw(ui,label,120,y+10,ink,.95f);
        if(detail.Length>0)font.Draw(ui,detail,520-font.Measure(detail,.72f),y+13,ink,.72f);
    }
}
