"""Copy-only current-schema fixture helper; directory/BAT allocation is preserved.

Run from the repository root with a current valid emulator card. Source input is
never written. The host codec validates/re-encodes record checksums and commit.
"""
from pathlib import Path
import struct,json,hashlib,sys
if len(sys.argv)!=4 or sys.argv[1] not in ('extract','write'):raise ValueError('Usage: card.py extract|write SOURCE_CARD OUTPUT_DIRECTORY')
source=Path(sys.argv[2]);root=Path(sys.argv[3]);root.mkdir(parents=True,exist_ok=True)
target_card=root/'MemoryCardA.USA.raw'
if source.resolve()==target_card.resolve() or (target_card.exists() and source.samefile(target_card)):raise ValueError('Fixture output must differ from original card')
card=bytearray(source.read_bytes());sector=8192
directory=max((card[sector:2*sector],card[2*sector:3*sector]),key=lambda b:struct.unpack_from('>H',b,0x1ffa)[0])
bat=max((card[3*sector:4*sector],card[4*sector:5*sector]),key=lambda b:struct.unpack_from('>H',b,4)[0])
files={}
for i in range(127):
 at=i*64
 if directory[at:at+6]!=b'AMTE01':continue
 name=directory[at+8:at+40].split(b'\0',1)[0].decode('ascii')
 if not name.startswith('AT-'):continue
 block,count=struct.unpack_from('>HH',directory,at+54);blocks=[];data=bytearray()
 for _ in range(count):
  assert 5<=block<len(card)//sector and block not in blocks
  blocks.append(block);data.extend(card[block*sector:(block+1)*sector]);block=struct.unpack_from('>H',bat,10+(block-5)*2)[0]
 assert block==65535 and data[:8]==b'DNDSAVE1'
 length,offset=struct.unpack_from('>II',data,8);files[name]=(blocks,data,length,offset)
names=['Town','Houses','Shops','Player','Commit'];bank=next(b for b in (0,1) if all(f'AT-{name}{b}' in files for name in names))
if sys.argv[1]=='extract':
 for n,name in enumerate(names):
  _,data,length,offset=files[f'AT-{name}{bank}'];(root/('commit' if n==4 else 'part'+str(n))).write_bytes(data[offset:offset+length])
else:
 for b in (0,1):
  for n,name in enumerate(names):
   key=f'AT-{name}{b}'
   if key not in files:continue
   blocks,data,length,offset=files[key];payload=(root/('nextcommit' if n==4 else 'next'+str(n))).read_bytes();assert offset+len(payload)<=len(data)
   struct.pack_into('>I',data,8,len(payload))
   data[offset:offset+len(payload)]=payload
   for i,block in enumerate(blocks):card[block*sector:(block+1)*sector]=data[i*sector:(i+1)*sector]
 target=root;(target/'MemoryCardA.USA.raw').write_bytes(card)
 meta={'source_card_sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'fixture_card_sha256':hashlib.sha256(card).hexdigest(),'test_only':'Submission fixture with mixed trees, maximum garden, closed cottage; original card unchanged.'};(target/'fixture.json').write_text(json.dumps(meta,indent=2));print(meta)
