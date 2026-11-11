"""Build a disposable Dolphin card from bounded profile-fixture records.

The formatted template is read-only. Existing output files are never replaced.
This does not touch any card mounted by the game or a real console.
"""
import argparse
from pathlib import Path
import struct

parser=argparse.ArgumentParser()
parser.add_argument('template',type=Path)
parser.add_argument('records',type=Path)
parser.add_argument('output',type=Path)
args=parser.parse_args()
if args.output.exists():raise FileExistsError(args.output)
card=bytearray(args.template.read_bytes());sector=8192;blocks=len(card)//sector
if len(card)%sector or not 64<=blocks<=2048:raise ValueError('Expected a formatted GameCube raw-card template')
files=sorted(p for p in args.records.iterdir() if p.name.startswith('AT-') and p.is_file())
if not files or len(files)>127:raise ValueError('No fixture records, or too many files')
def put16(data,at,value):struct.pack_into('>H',data,at,value)
def checksum(data,start,end):
    words=struct.unpack('>'+str((end-start)//2)+'H',data[start:end])
    a=sum(words)&65535;b=sum(word^65535 for word in words)&65535
    return (0 if a==65535 else a,0 if b==65535 else b)
directory=bytearray(b'\xff'*sector);bat=bytearray(sector);cursor=5
for index,path in enumerate(files):
    payload=path.read_bytes()
    if payload[:4]!=b'ATSP' or not 32<=len(payload)<=1024*1024:raise ValueError('Invalid fixture record: '+path.name)
    data=bytearray(96);data[:8]=b'DNDSAVE1';struct.pack_into('>II',data,8,len(payload),96)
    data[32:42]=b'AnimalTown';comment=b'Shared town test fixture';data[64:64+len(comment)]=comment;data.extend(payload)
    count=(len(data)+sector-1)//sector;data.extend(bytes(count*sector-len(data)))
    if cursor+count>blocks:raise ValueError('Fixture does not fit the template')
    entry=bytearray(b'\xff'*64);entry[:8]=b'AMTE01\xff\x00';name=path.name.encode('ascii')
    if len(name)>31:raise ValueError('Fixture filename exceeds card limits')
    entry[8:40]=name+bytes(32-len(name));struct.pack_into('>IIHHBBHHHI',entry,40,0,0xffffffff,0,0,4,0,cursor,count,0xffff,32)
    directory[index*64:(index+1)*64]=entry
    for part in range(count):put16(bat,10+(cursor+part-5)*2,65535 if part==count-1 else cursor+part+1)
    card[cursor*sector:(cursor+count)*sector]=data;cursor+=count
for number in (1,2):
    put16(directory,0x1ffa,number);a,b=checksum(directory,0,0x1ffc);put16(directory,0x1ffc,a);put16(directory,0x1ffe,b)
    card[number*sector:(number+1)*sector]=directory
for number in (3,4):
    put16(bat,4,number-2);put16(bat,6,blocks-cursor);put16(bat,8,cursor-1)
    a,b=checksum(bat,4,sector);put16(bat,0,a);put16(bat,2,b)
    card[number*sector:(number+1)*sector]=bat
args.output.parent.mkdir(parents=True,exist_ok=True)
with args.output.open('xb') as output:output.write(card)
print(f'{args.output}: {len(files)} fixture files, {cursor-5} data blocks; template preserved')
