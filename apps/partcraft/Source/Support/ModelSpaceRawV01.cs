using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Globalization;

public sealed class MSTag {
    public int code;public string value;public byte[] codeLine,valueLine;
    public byte[] Bytes(){return codeLine.Concat(valueLine).ToArray();}
    public byte[] WithValue(string requested){
        int end=valueLine.Length;while(end>0&&(valueLine[end-1]==10||valueLine[end-1]==13))end--;
        int begin=0;while(begin<end&&(valueLine[begin]==32||valueLine[begin]==9))begin++;
        int tail=end;while(tail>begin&&(valueLine[tail-1]==32||valueLine[tail-1]==9))tail--;
        return codeLine.Concat(valueLine.Take(begin)).Concat(Encoding.UTF8.GetBytes(requested)).Concat(valueLine.Skip(tail)).ToArray();
    }
}
public sealed class MSRecord {
    public int start,end;public string section,block;public List<MSTag> tags;
    public string Type{get{return tags[0].value;}}
    public string One(int code,string fallback){var values=tags.Where(t=>t.code==code).ToArray();if(values.Length>1)throw new InvalidOperationException("Multiple space/identity tags "+code+" on "+Type);return values.Length==0?fallback:values[0].value;}
    public byte[] Bytes(bool payload){return tags.Where(t=>!payload||(t.code!=330&&t.code!=67&&t.code!=410)).SelectMany(t=>t.Bytes()).ToArray();}
}
public sealed class MSRawDocument {
    public List<MSTag> tags=new List<MSTag>();public List<MSRecord> records=new List<MSRecord>();public int entitiesEnd=-1;
    public static MSRawDocument Read(byte[] raw){
        Func<int,int,byte[]> slice=(start,length)=>{var part=new byte[length];Buffer.BlockCopy(raw,start,part,0,length);return part;};
        var lines=new List<byte[]>();int begin=0;for(int i=0;i<raw.Length;i++)if(raw[i]==10){lines.Add(slice(begin,i+1-begin));begin=i+1;}if(begin<raw.Length)lines.Add(slice(begin,raw.Length-begin));
        if(lines.Count%2!=0)throw new InvalidOperationException("Unpaired DXF byte lines");var d=new MSRawDocument();var utf8=new UTF8Encoding(false,true);
        for(int i=0;i<lines.Count;i+=2)d.tags.Add(new MSTag{code=Int32.Parse(utf8.GetString(lines[i]).Trim().TrimStart('\uFEFF'),CultureInfo.InvariantCulture),value=utf8.GetString(lines[i+1]).Trim(),codeLine=lines[i],valueLine=lines[i+1]});
        string section=null,block=null;
        for(int i=0;i<d.tags.Count;){var t=d.tags[i];if(t.code!=0){i++;continue;}
            if(t.value=="SECTION"){if(section!=null||i+1>=d.tags.Count||d.tags[i+1].code!=2)throw new InvalidOperationException("Malformed SECTION");section=d.tags[i+1].value;i+=2;continue;}
            if(t.value=="ENDSEC"){if(section=="ENTITIES"){if(d.entitiesEnd>=0)throw new InvalidOperationException("Repeated ENTITIES");d.entitiesEnd=i;}section=null;i++;continue;}
            int end=i+1;while(end<d.tags.Count&&d.tags[end].code!=0)end++;
            var r=new MSRecord{start=i,end=end,section=section,block=block,tags=d.tags.GetRange(i,end-i)};
            if(section=="BLOCKS"&&r.Type=="BLOCK"){block=r.One(2,null);r.block=block;}
            if(section=="BLOCKS"&&r.Type=="ENDBLK")block=null;
            if(section!=null)d.records.Add(r);i=end;
        }
        if(d.entitiesEnd<0)throw new InvalidOperationException("Existing ENTITIES section required; no speculative DXF reconstruction");return d;
    }
    public static bool Curve(MSRecord r){return new[]{"LINE","ARC","CIRCLE","SPLINE"}.Contains(r.Type)&&(r.section=="ENTITIES"||r.section=="BLOCKS");}
    public byte[] WithoutCurves(){var skipped=new HashSet<int>(records.Where(Curve).SelectMany(r=>Enumerable.Range(r.start,r.end-r.start)));return tags.Where((t,i)=>!skipped.Contains(i)).SelectMany(t=>t.Bytes()).ToArray();}
}
public sealed class MSMoveResult {public byte[] bytes;public string fromSpace,fromLayout,modelOwner;public int movedRecords;public List<object> changes=new List<object>();}
public static class ModelSpaceRawV01 {
    public static string Hash(byte[] bytes){using(var h=System.Security.Cryptography.SHA256.Create())return BitConverter.ToString(h.ComputeHash(bytes)).Replace("-","");}
    public static MSMoveResult Move(byte[] bytes){
        // Validate native layout ownership and drawing types before changing any bytes.
        var doc=DxfLayoutReaderV03.ReadText(new UTF8Encoding(false,true).GetString(bytes).TrimStart('\uFEFF'));var raw=MSRawDocument.Read(bytes);
        if(doc.units!=4)throw new InvalidOperationException("INSUNITS=4 required");
        var model=raw.records.Where(r=>r.section=="TABLES"&&r.Type=="BLOCK_RECORD"&&r.One(2,null)=="*Model_Space").ToArray();if(model.Length!=1)throw new InvalidOperationException("Nonunique native model BLOCK_RECORD");
        string modelOwner=model[0].One(5,null),layoutHandle=model[0].One(340,null);
        var layouts=doc.records.Where(r=>r.section=="OBJECTS"&&r.Type=="LAYOUT"&&r.One(5,null)==layoutHandle).ToArray();if(layouts.Length!=1)throw new InvalidOperationException("Destination model layout missing");
        int marker=layouts[0].pairs.FindIndex(p=>p.Key==100&&p.Value=="AcDbLayout");var layout=layouts[0].pairs.Skip(marker+1).ToArray();
        if(marker<0||layout.Where(p=>p.Key==330).Select(p=>p.Value).Single()!=modelOwner||layout.Where(p=>p.Key==1).Select(p=>p.Value).Single()!="Model")throw new InvalidOperationException("Destination model layout linkage differs");
        var modelBlock=raw.records.Where(r=>r.section=="BLOCKS"&&r.Type=="BLOCK"&&r.One(2,null)=="*Model_Space").ToArray();
        if(modelBlock.Length!=1||modelBlock[0].One(330,null)!=modelOwner||new[]{10,20,30}.Any(k=>!GQ.Parse(modelBlock[0].One(k,"0")).Eq(0)))throw new InvalidOperationException("Destination model frame needs an unsupported transform");
        var sourceBlock=raw.records.Where(r=>r.section=="BLOCKS"&&r.Type=="BLOCK"&&r.One(2,null)==doc.selectedSpace).ToArray();
        if(sourceBlock.Length!=1||new[]{10,20,30}.Any(k=>!GQ.Parse(sourceBlock[0].One(k,"0")).Eq(0)))throw new InvalidOperationException("Source layout frame needs an unsupported transform");
        foreach(var definition in new[]{sourceBlock[0],modelBlock[0]})if(definition.One(1,"")!=""||(Int32.Parse(definition.One(70,"0"))&28)!=0)throw new InvalidOperationException("External/transformed layout definition unsupported");
        var curves=raw.records.Where(MSRawDocument.Curve).ToArray();
        var selectedHandles=new HashSet<string>(doc.geometry.Select(r=>r.Single(p=>p.Key==5).Value),StringComparer.OrdinalIgnoreCase);
        if(curves.Length!=doc.geometry.Count||curves.Any(r=>!selectedHandles.Contains(r.One(5,null))))throw new InvalidOperationException("Other geometry definitions/layouts exist; conservative processing stopped");
        foreach(var r in curves)if(!GQ.Parse(r.One(210,"0")).Eq(0)||!GQ.Parse(r.One(220,"0")).Eq(0)||!GQ.Parse(r.One(230,"1")).Eq(1)||r.tags.Any(t=>t.code>=30&&t.code<=39&&!GQ.Parse(t.value).Eq(0)))throw new InvalidOperationException("Nonstandard OCS/nonplanar geometry needs an unsupported transform");
        var result=new MSMoveResult{fromSpace=doc.selectedSpace,fromLayout=doc.selectedLayout,modelOwner=modelOwner};
        if(doc.selectedSpace=="*Model_Space"){if(curves.Any(r=>r.section!="ENTITIES"||r.One(330,null)!=modelOwner))throw new InvalidOperationException("Noncanonical model storage");result.bytes=bytes;return result;}
        var replacements=new Dictionary<int,byte[]>();var removed=new Dictionary<int,int>();var moved=new List<byte[]>();
        foreach(var r in curves){
            var pieces=new List<byte[]>();foreach(var tag in r.tags){
                string value=tag.code==330?modelOwner:tag.code==67?"0":tag.code==410?"Model":null;
                pieces.Add(value==null?tag.Bytes():tag.WithValue(value));
            }
            byte[] changed=pieces.SelectMany(x=>x).ToArray();
            if(r.section=="BLOCKS"){removed.Add(r.start,r.end);moved.Add(changed);result.movedRecords++;}else replacements.Add(r.start,changed);
            result.changes.Add(new{handle=r.One(5,null),type=r.Type,oldSection=r.section,newSection="ENTITIES",oldOwner=r.One(330,null),newOwner=modelOwner,old67=r.One(67,null),new67=r.One(67,null)==null?null:"0",old410=r.One(410,null),new410=r.One(410,null)==null?null:"Model",unchangedPayloadSha256=Hash(r.Bytes(true))});
        }
        var recordsByStart=curves.ToDictionary(r=>r.start);using(var output=new MemoryStream()){
            for(int i=0;i<raw.tags.Count;){
                if(i==raw.entitiesEnd)foreach(var r in moved)output.Write(r,0,r.Length);
                int end;if(removed.TryGetValue(i,out end)){i=end;continue;}byte[] replacement;
                if(replacements.TryGetValue(i,out replacement)){output.Write(replacement,0,replacement.Length);i=recordsByStart[i].end;continue;}
                byte[] unchanged=raw.tags[i].Bytes();output.Write(unchanged,0,unchanged.Length);i++;
            }result.bytes=output.ToArray();
        }
        // All non-geometry bytes stay identical: headers, units, layers, line types,
        // layouts, dictionaries, handles and unrelated metadata are not rewritten.
        var after=MSRawDocument.Read(result.bytes);
        if(!raw.WithoutCurves().SequenceEqual(after.WithoutCurves()))throw new InvalidOperationException("Non-geometry bytes changed unexpectedly");
        var afterCurves=after.records.Where(MSRawDocument.Curve).ToDictionary(r=>r.One(5,null),StringComparer.OrdinalIgnoreCase);
        if(afterCurves.Count!=curves.Length)throw new InvalidOperationException("Geometry record loss/duplication");
        foreach(var r in curves){MSRecord target;if(!afterCurves.TryGetValue(r.One(5,null),out target)||target.section!="ENTITIES"||target.One(330,null)!=modelOwner||!r.Bytes(true).SequenceEqual(target.Bytes(true)))throw new InvalidOperationException("Geometry payload changed: "+r.One(5,null));}
        var final=DxfLayoutReaderV03.ReadText(new UTF8Encoding(false,true).GetString(result.bytes).TrimStart('\uFEFF'));
        if(final.selectedSpace!="*Model_Space"||final.geometry.Count!=doc.geometry.Count)throw new InvalidOperationException("Final ownership/layout verification failed");
        return result;
    }
}
