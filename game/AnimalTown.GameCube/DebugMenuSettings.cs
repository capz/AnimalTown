using System;
using Dolphin.Input;
using Dolphin.Diagnostics;

internal static class DebugMenuSettings
{
    private static readonly DebugMenuGesture gesture=new DebugMenuGesture();
    internal static bool Visible=>UserOptionsStorage.Current.ShowDebugMenus;
    internal static bool Read(ControllerState input)
    {
        bool other=!input.IsConnected||(input.HeldButtons&~ControllerButton.Z)!=ControllerButton.None||
            (input.PressedButtons&~ControllerButton.Z)!=ControllerButton.None||
            Math.Abs(input.Stick.X)>.35f||Math.Abs(input.Stick.Y)>.35f||
            Math.Abs(input.CStick.X)>.35f||Math.Abs(input.CStick.Y)>.35f;
        if(!gesture.Read(input.Z.WasPressed,other))return false;
        var options=UserOptionsStorage.Current;options.ShowDebugMenus=!options.ShowDebugMenus;
        UserOptionsStorage.Save(options);
        DebugOverlay.WriteLine("ANIMALTOWN DEBUG MENUS VISIBLE "+(Visible?1:0));
        return true;
    }
}
