using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Collections.Generic;
using System.Globalization;

public sealed class DLRecord {
    public string section,block;public List<KeyValuePair<int,string>> pairs;
    public string Type {get{return pairs[0].Value;}}
    public string One(int code,string fallback){var values=pairs.Where(p=>p.Key==code).Select(p=>p.Value).ToArray();if(values.Length>1)throw new InvalidOperationException("Duplicate scalar "+code+" in "+Type);return values.Length==0?fallback:values[0];}
}
public sealed class DLDocument {
    public string text;public int units=-1;
    public List<DLRecord> records=new List<DLRecord>();
    public List<List<KeyValuePair<int,string>>> geometry=new List<List<KeyValuePair<int,string>>>();
    public object evidence;public string selectedSpace,selectedLayout;
}
public static class DxfLayoutReaderV03 {
    public static DLDocument Read(string path){return ReadText(File.ReadAllText(path,new UTF8Encoding(false,true)));}
    public static DLDocument ReadText(string text){
        var result=new DLDocument{text=text};var lines=text.Replace("\r\n","\n").TrimEnd('\n').Split('\n');
        if(lines.Length%2!=0)throw new InvalidOperationException("Odd DXF pair count");
        var pairs=new List<KeyValuePair<int,string>>();for(int i=0;i<lines.Length;i+=2)pairs.Add(new KeyValuePair<int,string>(Int32.Parse(lines[i].Trim(),CultureInfo.InvariantCulture),lines[i+1].Trim()));
        if(pairs.Count==0||pairs.Last().Key!=0||pairs.Last().Value!="EOF")throw new InvalidOperationException("DXF EOF absent");
        var headerValues=new Dictionary<string,object>(StringComparer.Ordinal);var sections=new HashSet<string>(StringComparer.Ordinal);string section=null,block=null;var blockDefinitions=new Dictionary<string,DLRecord>(StringComparer.Ordinal);
        for(int i=0;i<pairs.Count;){
            var p=pairs[i];
            if(p.Key==0&&p.Value=="SECTION"){
                if(section!=null||i+1>=pairs.Count||pairs[i+1].Key!=2)throw new InvalidOperationException("Malformed DXF SECTION");
                section=pairs[i+1].Value;if(!sections.Add(section))throw new InvalidOperationException("Repeated DXF SECTION "+section);i+=2;continue;
            }
            if(p.Key==0&&p.Value=="ENDSEC"){if(section==null||block!=null)throw new InvalidOperationException("Unbalanced ENDSEC/BLOCK");section=null;i++;continue;}
            if(section=="HEADER"&&p.Key==9){int he=i+1;while(he<pairs.Count&&pairs[he].Key!=0&&pairs[he].Key!=9)he++;headerValues.Add(p.Value,pairs.Skip(i+1).Take(he-i-1).Select(h=>new{code=h.Key,value=h.Value}).ToArray());}
            if(section=="HEADER"&&p.Key==9&&p.Value=="$INSUNITS"){
                if(result.units!=-1||i+1>=pairs.Count||pairs[i+1].Key!=70)throw new InvalidOperationException("Invalid/repeated INSUNITS");result.units=Int32.Parse(pairs[i+1].Value,CultureInfo.InvariantCulture);
            }
            if(p.Key!=0||section==null){i++;continue;}
            int end=i+1;while(end<pairs.Count&&pairs[end].Key!=0)end++;
            var row=new DLRecord{section=section,block=block,pairs=pairs.GetRange(i,end-i)};
            if(section=="BLOCKS"&&row.Type=="BLOCK"){
                if(block!=null)throw new InvalidOperationException("Nested block definitions unsupported");block=row.One(2,null);
                if(block==null||blockDefinitions.ContainsKey(block))throw new InvalidOperationException("Missing/duplicate block definition");row.block=block;blockDefinitions.Add(block,row);
            }else if(section=="BLOCKS"&&row.Type=="ENDBLK"){
                if(block==null)throw new InvalidOperationException("ENDBLK without BLOCK");row.block=block;block=null;
            }else if(section=="BLOCKS"&&block==null)throw new InvalidOperationException("Orphan entity in BLOCKS");
            result.records.Add(row);i=end;
        }
        if(section!=null||block!=null)throw new InvalidOperationException("Unclosed DXF section/block");
        var table=result.records.Where(r=>r.section=="TABLES"&&r.Type=="BLOCK_RECORD").ToDictionary(r=>r.One(5,null),StringComparer.OrdinalIgnoreCase);
        var layouts=new Dictionary<string,Tuple<string,string>>(StringComparer.OrdinalIgnoreCase);
        foreach(var row in result.records.Where(r=>r.section=="OBJECTS"&&r.Type=="LAYOUT")){
            int marker=row.pairs.FindIndex(p=>p.Key==100&&p.Value=="AcDbLayout");if(marker<0)throw new InvalidOperationException("LAYOUT lacks AcDbLayout subclass");
            var values=row.pairs.Skip(marker+1).ToArray();string name=values.Where(p=>p.Key==1).Select(p=>p.Value).Single();string owner=values.Where(p=>p.Key==330).Select(p=>p.Value).Single();
            layouts.Add(row.One(5,null),Tuple.Create(name,owner));
        }
        bool minimal=table.Count==0&&layouts.Count==0;
        var selected=new List<Tuple<DLRecord,string,string>>();var entityHandles=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var warnings=new List<string>();
        foreach(var row in result.records.Where(r=>r.section=="ENTITIES"||(r.section=="BLOCKS"&&r.Type!="BLOCK"&&r.Type!="ENDBLK"))){
            if(row.section=="BLOCKS"&&!row.block.StartsWith("*Model_Space",StringComparison.Ordinal)&&!row.block.StartsWith("*Paper_Space",StringComparison.Ordinal))continue; // Uninserted ordinary definitions are not drawing geometry.
            string handle=row.One(5,null);if(handle!=null&&!entityHandles.Add(handle))throw new InvalidOperationException("Duplicate drawing entity handle "+handle);
            string owner=row.One(330,null),space,layoutName;string flag=row.One(67,null),layoutHint=row.One(410,null);
            if(minimal){
                if(row.section!="ENTITIES"||owner!=null||(flag!=null&&flag!="0")||(layoutHint!=null&&layoutHint!="Model"))throw new InvalidOperationException("Cannot establish paper-space ownership without BLOCK_RECORD/LAYOUT");
                space="*Model_Space";layoutName="Model";
            }else{
                DLRecord br;if(owner==null||!table.TryGetValue(owner,out br))throw new InvalidOperationException("Missing/unknown owner BLOCK_RECORD for "+handle);
                space=br.One(2,null);Tuple<string,string> layout;
                if(!layouts.TryGetValue(br.One(340,""),out layout)||!String.Equals(layout.Item2,owner,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("BLOCK_RECORD/LAYOUT linkage is not reciprocal: "+owner);
                layoutName=layout.Item1;
                if(space!="*Model_Space"&&!space.StartsWith("*Paper_Space",StringComparison.Ordinal))throw new InvalidOperationException("Drawing entity owner is not a native model/paper layout");
                bool paper=space!="*Model_Space";
                if(row.section=="BLOCKS"){
                    DLRecord definition=blockDefinitions[row.block];
                    if(row.block!=space||!String.Equals(definition.One(330,null),owner,StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Entity owner differs from containing layout block");
                    if(definition.One(10,"0")!="0.0"&&!GQ.Parse(definition.One(10,"0")).Eq(0))throw new InvalidOperationException("Nonzero layout block base point");
                    if(!GQ.Parse(definition.One(20,"0")).Eq(0)||!GQ.Parse(definition.One(30,"0")).Eq(0)||definition.One(1,"")!=""||(Int32.Parse(definition.One(70,"0"))&28)!=0)throw new InvalidOperationException("Nontrivial/XREF layout block unsupported");
                    if(paper&&flag==null)warnings.Add("Entity "+handle+": group 67 omitted in paper layout block; paper location established by BLOCK_RECORD/LAYOUT and physical containment");
                }
                if(flag!=null&&flag!=(paper?"1":"0"))throw new InvalidOperationException("Explicit group 67 contradicts native layout owner: "+handle);
                if(layoutHint!=null&&layoutHint!=layoutName)throw new InvalidOperationException("Group 410 contradicts layout owner: "+handle);
            }
            // Ordinary INSERTs/VIEWPORTs are deliberately not expanded or ignored.
            if(!new[]{"LINE","ARC","CIRCLE","SPLINE"}.Contains(row.Type))throw new InvalidOperationException("Unsupported native drawing entity "+row.Type+" at "+handle);
            selected.Add(Tuple.Create(row,space,layoutName));
        }
        var scopes=selected.Select(r=>r.Item2).Distinct(StringComparer.Ordinal).ToArray();
        if(scopes.Length!=1)throw new InvalidOperationException(scopes.Length==0?"No owned drawing geometry in any native layout; ordinary unused block definitions do not count":"More than one populated layout; merging coordinate spaces is forbidden");
        result.selectedSpace=scopes[0];result.selectedLayout=selected[0].Item3;result.geometry=selected.Select(r=>r.Item1.pairs).ToList();
        result.evidence=new{
            headerVariables=headerValues,sections=sections.ToArray(),minimalOwnerlessModelSpace=minimal,selectedSpace=result.selectedSpace,selectedLayout=result.selectedLayout,
            coordinatesReadWithoutModification=true,layoutCoordinatesAreNotViewportProjected=true,paperSpace=result.selectedSpace!="*Model_Space",
            entityCount=result.geometry.Count,sectionCounts=result.records.GroupBy(r=>r.section).ToDictionary(g=>g.Key,g=>g.GroupBy(r=>r.Type).ToDictionary(t=>t.Key,t=>t.Count())),
            selectedEntities=selected.Select(r=>new{type=r.Item1.Type,handle=r.Item1.One(5,null),section=r.Item1.section,containingBlock=r.Item1.block,owner=r.Item1.One(330,null),flag67=r.Item1.One(67,null),layout410=r.Item1.One(410,null),space=r.Item2,layout=r.Item3,extrusion=new[]{r.Item1.One(210,"0"),r.Item1.One(220,"0"),r.Item1.One(230,"1")}}).ToArray(),
            blockRecordLayoutLinks=table.Select(t=>new{handle=t.Key,name=t.Value.One(2,null),layoutHandle=t.Value.One(340,null)}).ToArray(),warnings=warnings.ToArray(),
            limitations=new[]{"Only a single populated native layout is compared; no concatenation across layouts","No INSERT/XREF expansion or VIEWPORT projection is used","Paper-space geometry is not relocated into model space; CAM import support remains unverified"}
        };return result;
    }
}
