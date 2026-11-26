"""Propose equal world/review panes using the game's real font and screenshots."""
from pathlib import Path
from base64 import b64encode

template = Path(__file__).with_name('generate.py')
scope = {'__file__': str(template)}
exec(template.read_text().split("board('connection.svg'")[0], scope)
board, rect, text, heading = (scope[name] for name in ('board', 'rect', 'text', 'heading'))
root = scope['root']

def screenshot(path):
    return 'data:image/png;base64,' + b64encode((root / path).read_bytes()).decode()

home = screenshot('docs/screenshots/seasonal-shadows/summer.png')
residents = screenshot('docs/screenshots/offline-jobs/clothing-final-112b.png')
preview_count = 0

def preview(label, npc=False):
    global preview_count
    preview_count += 1
    clip = f'world-{preview_count}'
    source = residents if npc else home
    return (
        f'<defs><clipPath id="{clip}"><rect width="320" height="480"/></clipPath></defs>'
        f'<g clip-path="url(#{clip})"><image x="-165" width="800" height="600" href="{source}"/></g>'
        + rect(12, 15, 296, 32, '#fff5dd', 14)
        + text(24, 37, 'Current town · focused subject', 14)
        + rect(12, 398, 296, 66, '#fff5dd', 16)
        + text(24, 423, label, 16)
        + text(24, 448, 'Stick: inspect the current scene', 13)
    )

def panel(title, subtitle, details, choices, subject, npc=False):
    content = preview(subject, npc)
    content += rect(320, 0, 320, 480, '#31483e', 0)
    content += rect(324, 0, 316, 480, '#fff5dd', 0)
    content += heading(340, 43, title, 24)
    content += text(340, 73, subtitle, 14, '#356954')
    for i, line in enumerate(details):
        content += text(340, 117 + i * 24, line, 15)
    for i, choice in enumerate(choices):
        y = 302 + i * 50
        content += rect(336, y, 288, 42, '#dde7bc' if i == 0 else '#ece2c5', 14)
        content += text(350, y + 27, choice, 15)
    content += text(340, 458, 'D-pad / A: choose   B: later', 13)
    return content

board('resident-conflicts.svg', 'Online review · current town + decisions', [
    ('1 · Equal halves; selection controls the world focus',
     panel('Resident review', '1 of 3 · saved offline changes',
           ['Select a change to inspect its', 'current place in your town.', 'Guests wait during review.'],
           ['Mira joined offline', 'Rowan moved to Mossbank', 'Fern was deleted offline'],
           'Mira’s current home')),
    ('2 · Added profile: inspect the home as it currently exists',
     panel('Mira joined', 'New resident · home and progress',
           ['Keep her saved home, pockets', 'and offline progress.', '', 'Add her to the online directory.'],
           ['Add Mira to online town', 'Finish later'], 'Mira’s current home')),
    ('3 · Deleted profile: inspect the recipient before transferring',
     panel('Fern was deleted', 'Pending online progress',
           ['1,240 Bells · 2 letters · 1 gift', '', 'Only new online value moves.', 'Previously discarded possessions', 'stay discarded.'],
           ['Give pending value to Mira', 'Choose another resident'], 'Mira · selected recipient')),
    ('4 · Completed move: inspect the town as it is now',
     panel('Rowan moved', 'Completed offline on both cards',
           ['2 unfinished errands · 1 letter', 'Keep rewards already earned.', 'Retire affected job parcels.', 'Return undelivered mail.', 'Keep the completed move.'],
           ['Accept move and settle', 'Finish later'], 'Current residents · Rowan has left', True)),
    ('5 · Capacity: world stays visible while the decision waits',
     panel('Make room first', 'Mira · selected recipient',
           ['Her mail or reward storage is full.', 'The pending value is held safely.', '', 'Choose someone else or return', 'after making room.'],
           ['Choose another resident', 'Finish later'], 'Mira’s current home')),
    ('6 · Last profile: inspect the town before closing it',
     panel('Last resident', 'Review before deleting the card',
           ['Close the gate and settle activity.', 'Keep confirmed guest earnings.', 'Review placements and mail.', '', 'Deletion waits for a safe closure.'],
           ['Review and close online town', 'Keep resident for now'], 'Current town · closure review')),
])
