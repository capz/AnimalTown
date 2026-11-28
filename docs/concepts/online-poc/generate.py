"""Render reviewable online UI boards using AnimalTown's existing font/palette."""
from pathlib import Path
from html import escape
from fontTools.ttLib import TTFont
from fontTools.pens.svgPathPen import SVGPathPen
root=Path(__file__).resolve().parents[3]
font=TTFont(root/'assets/fonts/gorditas/Gorditas-Bold.ttf');glyphs=font.getGlyphSet();cmap=font.getBestCmap();units=font['head'].unitsPerEm
INK='#31483e';CREAM='#fff5dd';LEAF='#356954';FOCUS='#dde7bc'
def text(x,y,value,size=19,color=INK):return f'<text x="{x}" y="{y}" font-size="{size}" fill="{color}">{escape(value)}</text>'
def heading(x,y,value,size=29):
 out=[];at=0
 for ch in value:
  name=cmap.get(ord(ch),'space');pen=SVGPathPen(glyphs);glyphs[name].draw(pen)
  out.append(f'<path d="{pen.getCommands()}" transform="translate({at},0)"/>');at+=glyphs[name].width
 return f'<g fill="{INK}" transform="translate({x},{y}) scale({size/units},-{size/units})">'+''.join(out)+'</g>'
def rect(x,y,w,h,color=CREAM,r=20):return f'<rect x="{x}" y="{y}" width="{w}" height="{h}" rx="{r}" fill="{color}"/>'
def row(y,label,chosen=False,right=''):
 return (rect(64,y-27,512,43,FOCUS if chosen else '#ece2c5',16)+text(84,y,label)+text(530-len(right)*8,y,right,16))
def frame(title,subtitle=''):
 return '<rect width="640" height="480" fill="url(#dots)"/>'+rect(48,29,552,432,'#31483e')+rect(44,24,552,432)+heading(74,72,title)+text(76,106,subtitle,16,LEAF)
def lines(y,values):return ''.join(text(76,y+i*27,v,18) for i,v in enumerate(values))
def hint(value):return text(74,435,value,15)
def board(filename,caption,panels):
 out=['<svg xmlns="http://www.w3.org/2000/svg" width="1440" height="1740" viewBox="0 0 1440 1740" role="img" aria-label="'+escape(caption)+'"><defs><pattern id="dots" width="48" height="48" patternUnits="userSpaceOnUse"><rect width="48" height="48" fill="#95c6ba"/><circle cx="8" cy="8" r="2" fill="#b1d8cd"/><circle cx="32" cy="32" r="2" fill="#b1d8cd"/></pattern></defs><g font-family="DejaVu Sans,sans-serif">',rect(0,0,1440,1740,'#f4f0e5',0),heading(40,45,caption,27),text(40,74,'Proposed layouts · existing Gorditas headings, cream panels, leaf accents and controller hints',17)]
 for i,(label,content) in enumerate(panels):
  x=40+(i%2)*700;y=125+(i//2)*535
  out.append(text(x,y-12,label,18)+f'<g transform="translate({x},{y})">'+content+'</g>')
 out.append('</g></svg>');(Path(__file__).parent/filename).write_text(''.join(out))
board('connection.svg','Online play · connection and travel',[
 ('1 · Online gate',frame('Online','Maplewood · linked as Mira')+row(156,'Open gate',True)+row(209,'Visit a friend')+row(262,'Sync town')+row(315,'Link account / service')+lines(377,['Gate closed · 0 of 2 guests'])+hint('A: choose   B: back')),
 ('2 · Available friends',frame('Visit a friend','Open towns · mutual friends')+row(157,'Rowan / Pinegrove',True,'1 / 2')+row(210,'Fern / Mossbank',False,'0 / 2')+row(263,'Reed / Willowbay',False,'Full')+lines(359,['Choose a town to visit.','Your home stays on your selected card.'])+hint('A: visit   X: refresh   B: back')),
 ('3 · Link account and configure service',frame('Link your town','Use the existing browser approval flow')+lines(157,['Open the address shown below on your phone.'])+text(76,208,'…trycloudflare.com/link',21)+heading(76,268,'482 731',38)+lines(326,['Linking Maplewood as Mira.','Waiting for approval…'])+hint('B: cancel')),
 ('4 · Loading and reconnect states',frame('Travelling','Pinegrove · Rowan’s town')+lines(160,['Connecting…','Loading the town and your travel pockets.'])+rect(76,250,488,15,'#ece2c5',7)+rect(76,250,275,15,LEAF,7)+lines(319,['If interrupted: Reconnecting…','Your accepted actions will be recovered.'])+hint('B: cancel / return safely')),
 ('5 · Guest pause during review',frame('A moment, please','Pinegrove · owner review in progress')+lines(164,['Rowan is reviewing changes to the town.','Your visit will resume when review ends.','Your purchases and earnings are safe.'])+row(331,'Return home',True)+hint('A: return home')),
 ('6 · Return and save',frame('Heading home','Maplewood · memory card in Slot A')+lines(162,['Your home could not be saved to Slot A.','Your progress is waiting safely.','Reinsert the card, then try again.'])+row(303,'Retry saving',True)+row(356,'Return to title')+hint('A: choose')),
])
board('review-and-social.svg','Online play · review and social',[
 ('7 · Review a guest’s final placement',frame('Town review','1 of 3 · Rowan’s wooden chair')+rect(245,126,150,135,'#95c6ba',22)+rect(290,151,52,47,'#b07751',6)+rect(290,193,60,14,'#854f30',4)+rect(292,205,8,26,'#854f30',2)+rect(338,205,8,26,'#854f30',2)+row(306,'Keep in town',True)+row(359,'Send to koban')+text(76,404,'Guests are waiting. Each decision is saved.',15)+hint('A: confirm   B: finish later')),
 ('8 · Review an offline / hosted conflict',frame('Choose this spot','Your offline placement overlaps a guest’s item')+row(167,'Keep my placement',True)+row(220,'Keep guest placement')+lines(289,['The displaced item goes to the koban.','Your purchases and earnings stay yours.','Inspect the two placements before choosing.'])+hint('A: choose   B: finish later')),
 ('9 · Eligible guest jobs',frame('Town jobs','Pinegrove · available resident requests')+row(160,'Parcel delivery / Rowan',True)+row(213,'Find borrowed item / Fern')+lines(299,['Only requests you can finish on this visit','appear here. Rewards travel home.'])+hint('A: view request   B: back')),
 ('10 · Chat uses the existing keyboard',frame('Chat','Speaking as Mira · everyone in the town')+rect(72,128,496,64,'#ece2c5',15)+text(87,166,'Hello, everyone!',20)+''.join(row(232+i*43,s,i==0) for i,s in enumerate(['q  w  e  r  t  y  u  i  o  p','a  s  d  f  g  h  j  k  l','z  x  c  v  b  n  m  Space']))+hint('A: type   X: erase   START: send   B: back')),
 ('11 · Resident-only mail recipient list',frame('Write a letter','Residents of Pinegrove')+row(162,'Rowan',True)+row(215,'Fern')+row(268,'Reed')+text(76,351,'Choose someone in the town.',16)+text(76,380,'Letters during visits cannot carry presents.',16)+hint('A: choose resident   B: back')),
 ('12 · Letter composer and failed-send draft',frame('Letter to Rowan','Existing keyboard and send confirmation')+rect(72,128,496,132,'#ece2c5',15)+text(87,165,'Thank you for welcoming me!',19)+text(87,198,'I enjoyed visiting Pinegrove.',19)+lines(305,['Send failed. Your draft is still here.','Try again when connected.'])+hint('A: type   X: erase   START: send   B: back')),
])
