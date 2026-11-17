"""Read-only decoder for the SDK's checksummed CardRecordStore payload.

The caller validates the GameCube directory/BAT chain and DNDSAVE1 envelope.
Match the pinned SDK's two-checkpoint selection, bounds and record hashes.
"""
import struct

BLOCK=8192

def archive_records(payload):
    if len(payload)%BLOCK or not 6<=len(payload)//BLOCK<=256:
        raise ValueError('Invalid card record-store size')
    blocks=len(payload)//BLOCK
    def fnv(data):
        value=2166136261
        for byte in data:value=((value^byte)*16777619)&0xffffffff
        return value
    candidates=[]
    for bank in (0,1):
        page=payload[bank*BLOCK:(bank+1)*BLOCK]
        try:
            magic,version,sequence,count=struct.unpack_from('<4I',page)
            if magic!=0x31525444 or version!=1 or not 1<=sequence<=0x7fffffff or count!=blocks or fnv(page[:-4])!=struct.unpack_from('<I',page,BLOCK-4)[0]:continue
            records={};spans=[]
            for index in range(64):
                at=32+index*48
                if page[at]==0:continue
                name=page[at:at+32].split(b'\0',1)[0]
                if not 1<=len(name)<=31 or any(byte<32 or byte>126 for byte in name):raise ValueError('Invalid archive record name')
                name=name.decode('ascii')
                if name in records:raise ValueError('Duplicate archive record')
                start,offset,length,checksum=struct.unpack_from('<4I',page,at+32)
                if not 2<=start<blocks or offset>=BLOCK or length>(blocks-start)*BLOCK-offset:raise ValueError('Archive record outside file')
                begin=start*BLOCK+offset;end=begin+max(1,length)
                if any(begin<other_end and other_begin<end for other_begin,other_end in spans):raise ValueError('Overlapping archive records')
                data=payload[begin:begin+length]
                if fnv(data)!=checksum:raise ValueError('Archive record checksum failed')
                records[name]=data;spans.append((begin,end))
            candidates.append((sequence,bank,records))
        except (ValueError,struct.error):continue
    if not candidates:raise ValueError('No verified archive checkpoint')
    # Equal generations choose the first checkpoint, as in the SDK.
    return max(candidates,key=lambda candidate:(candidate[0],-candidate[1]))[2]
