# Pipe controller configuration adapted from capz/DolphinDotNet. GPL-2.0-only.
def pad_config():
    text=''
    for p in range(4):
        text+=f'[GCPad{p+1}]\nDevice = Pipe/0/dnd{p}\nOptions/Always Connected = True\n'
        for button in ('A','B','X','Y','Z','START'):
            text+=f'Buttons/{"Start" if button=="START" else button} = `Button {button}`\n'
        for group,pipe in [('Main Stick','MAIN'),('C-Stick','C')]:
            for direction,axis,sign in [('Up','Y','+'),('Down','Y','-'),('Left','X','-'),('Right','X','+')]:
                text+=f'{group}/{direction} = `Axis {pipe} {axis} {sign}`\n'
            text+=f'{group}/Dead Zone = 0.0\n'
        for button in ('L','R'):
            text+=f'Triggers/{button} = `Button {button}`\nTriggers/{button}-Analog = `Axis {button} +`\n'
        text+='Triggers/Dead Zone = 0.0\n'
        for direction in ('Up','Down','Left','Right'):
            text+=f'D-Pad/{direction} = `Button D_{direction.upper()}`\n'
        text+='\n'
    return text
