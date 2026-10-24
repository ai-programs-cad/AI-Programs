"""Read-only CFB/VBA extraction; never executes VBA."""
from pathlib import Path
import struct,math,json,hashlib
import sys
path=Path(sys.argv[1])
data=path.read_bytes()
u32=lambda b,o:struct.unpack_from('<I',b,o)[0]
ss=1<<struct.unpack_from('<H',data,30)[0];ms=1<<struct.unpack_from('<H',data,32)[0]
sector=lambda n:data[(n+1)*ss:(n+2)*ss]
difat=list(struct.unpack_from('<109I',data,76))
nextid=u32(data,68)
for _ in range(u32(data,72)):
    z=struct.unpack('<%dI'%(ss//4),sector(nextid));difat.extend(z[:-1]);nextid=z[-1]
fat=[]
for n in difat:
    if n<0xfffffffa:fat.extend(struct.unpack('<%dI'%(ss//4),sector(n)))
def chain(n,table):
    seen=set()
    while n<0xfffffffa:
        if n in seen:raise ValueError('CFB chain cycle')
        seen.add(n);yield n;n=table[n]
directory=b''.join(sector(n) for n in chain(u32(data,48),fat))
entries=[]
for i in range(0,len(directory),128):
    d=directory[i:i+128];l=struct.unpack_from('<H',d,64)[0]
    if l>=2:entries.append(dict(name=d[:l-2].decode('utf-16le'),type=d[66],start=u32(d,116),size=struct.unpack_from('<Q',d,120)[0]))
root=next(e for e in entries if e['type']==5)
mini=b''.join(sector(n) for n in chain(root['start'],fat))[:root['size']]
minifat=[]
if u32(data,64):
    raw=b''.join(sector(n) for n in chain(u32(data,60),fat));minifat=list(struct.unpack('<%dI'%(len(raw)//4),raw))
def stream(e):
    if e['size']<u32(data,56):return b''.join(mini[n*ms:(n+1)*ms] for n in chain(e['start'],minifat))[:e['size']]
    return b''.join(sector(n) for n in chain(e['start'],fat))[:e['size']]
def decompress(raw):
    if raw[0]!=1:raise ValueError('signature')
    out=bytearray();pos=1
    while pos+2<=len(raw):
        header=struct.unpack_from('<H',raw,pos)[0];size=(header&0xfff)+3
        if ((header>>12)&7)!=3:raise ValueError('chunk signature')
        stop=min(len(raw),pos+size);pos+=2;start=len(out)
        if not header&0x8000:out.extend(raw[pos:stop]);pos=stop;continue
        while pos<stop:
            flags=raw[pos];pos+=1
            for bit in range(8):
                if pos>=stop:break
                if flags&(1<<bit):
                    token=struct.unpack_from('<H',raw,pos)[0];pos+=2
                    bits=max(4,(len(out)-start-1).bit_length())
                    length=(token&((1<<(16-bits))-1))+3;offset=(token>>(16-bits))+1
                    if offset>len(out)-start:raise ValueError('copy offset')
                    for k in range(length):out.append(out[-offset])
                else:out.append(raw[pos]);pos+=1
                if len(out)-start>4096:raise ValueError('chunk too long')
    return bytes(out)
out=Path(sys.argv[2]);out.mkdir(parents=True,exist_ok=True)
found=[]
for e in entries:
    if e['type']!=2:continue
    raw=stream(e)
    for offset in range(len(raw)):
        if raw[offset]!=1:continue
        try:
            text=decompress(raw[offset:])
            if b'Attribute VB_Name' not in text or not any(x in text for x in (b'Sub ',b'Function ')):continue
            target=out/(e['name'].replace('/','_')+'.bas')
            target.write_text(text.decode('cp1251',errors='replace'),encoding='utf-8')
            found.append({'stream':e['name'],'offset':offset,'path':str(target),'bytes':len(text)});break
        except (ValueError,IndexError,struct.error):pass
report={'macroPath':str(path),'sha256':hashlib.sha256(data).hexdigest(),'executed':False,'modules':found}
(out/'extraction.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8')
print(json.dumps(report,ensure_ascii=False,indent=2))
