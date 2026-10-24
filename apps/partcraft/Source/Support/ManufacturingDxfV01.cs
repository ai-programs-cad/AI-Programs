using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Globalization;
using System.Collections.Generic;
public class MPPoint {public double X,Y;public MPPoint(double x,double y){X=x;Y=y;}public double Distance(MPPoint b){return Math.Sqrt((X-b.X)*(X-b.X)+(Y-b.Y)*(Y-b.Y));}}
public class MPDxfResult {public bool accepted,hasAnalyticCurves;public string reason;public int entityCount,closedCurves,closedConnectedContours;public double[] extentsMm;public List<string> entityTypes=new List<string>();public string normalizedText;}
public static class ManufacturingDxfV01 {
    static CultureInfo C=CultureInfo.InvariantCulture;
    public static string N(double x){if(!NV.Finite(x))throw new InvalidOperationException("Nonfinite DXF coordinate");return x.ToString("R",C);}
    public static string Wrap(string entities){
        var s=new StringBuilder("0\nSECTION\n2\nHEADER\n9\n$ACADVER\n1\nAC1015\n9\n$INSUNITS\n70\n4\n9\n$MEASUREMENT\n70\n1\n9\n$HANDSEED\n5\nFFFFFFF\n0\nENDSEC\n0\nSECTION\n2\nTABLES\n");
        s.Append("0\nTABLE\n2\nLTYPE\n5\n10\n100\nAcDbSymbolTable\n70\n1\n0\nLTYPE\n5\n11\n330\n10\n100\nAcDbSymbolTableRecord\n100\nAcDbLinetypeTableRecord\n2\nCONTINUOUS\n70\n0\n3\nSolid line\n72\n65\n73\n0\n40\n0\n0\nENDTAB\n");
        s.Append("0\nTABLE\n2\nLAYER\n5\n20\n100\nAcDbSymbolTable\n70\n2\n");
        foreach(var layer in new[]{new[]{"21","0"},new[]{"22","CUT_THROUGH"}})s.Append("0\nLAYER\n5\n").Append(layer[0]).Append("\n330\n20\n100\nAcDbSymbolTableRecord\n100\nAcDbLayerTableRecord\n2\n").Append(layer[1]).Append("\n70\n0\n62\n7\n6\nCONTINUOUS\n");
        s.Append("0\nENDTAB\n0\nTABLE\n2\nBLOCK_RECORD\n5\n2F\n100\nAcDbSymbolTable\n70\n2\n");
        foreach(var block in new[]{new[]{"30","*Model_Space"},new[]{"31","*Paper_Space"}})s.Append("0\nBLOCK_RECORD\n5\n").Append(block[0]).Append("\n330\n2F\n100\nAcDbSymbolTableRecord\n100\nAcDbBlockTableRecord\n2\n").Append(block[1]).Append("\n70\n4\n");
        s.Append("0\nENDTAB\n0\nENDSEC\n0\nSECTION\n2\nBLOCKS\n");
        foreach(var block in new[]{new[]{"40","41","30","*Model_Space"},new[]{"42","43","31","*Paper_Space"}}){
            s.Append("0\nBLOCK\n5\n").Append(block[0]).Append("\n330\n").Append(block[2]).Append("\n100\nAcDbEntity\n8\n0\n100\nAcDbBlockBegin\n2\n").Append(block[3]).Append("\n70\n0\n10\n0\n20\n0\n30\n0\n3\n").Append(block[3]).Append("\n1\n\n");
            s.Append("0\nENDBLK\n5\n").Append(block[1]).Append("\n330\n").Append(block[2]).Append("\n100\nAcDbEntity\n8\n0\n100\nAcDbBlockEnd\n");
        }
        return s.Append("0\nENDSEC\n0\nSECTION\n2\nENTITIES\n").Append(entities).Append("0\nENDSEC\n0\nEOF\n").ToString();
    }
    public static string Line(MPPoint a,MPPoint b){return "0\nLINE\n8\nCUT_THROUGH\n10\n"+N(a.X)+"\n20\n"+N(a.Y)+"\n30\n0\n11\n"+N(b.X)+"\n21\n"+N(b.Y)+"\n31\n0\n";}
    public static string Circle(MPPoint c,double r){return "0\nCIRCLE\n8\nCUT_THROUGH\n10\n"+N(c.X)+"\n20\n"+N(c.Y)+"\n30\n0\n40\n"+N(r)+"\n";}
    class Pair {public int K;public string V;}
    static double Number(string text){double n=double.Parse(text,C);if(!NV.Finite(n))throw new InvalidOperationException("Nonfinite DXF numeric value");return n;}
    static double Value(List<Pair> p,int k,double fallback=0){var x=p.FirstOrDefault(z=>z.K==k);return x==null?fallback:Number(x.V);}
    static double Required(List<Pair> p,int k){var matches=p.Where(x=>x.K==k).ToArray();if(matches.Length!=1)throw new InvalidOperationException("Missing/duplicate required DXF group "+k);return Number(matches[0].V);}
    static string Text(List<Pair> p,int k){var x=p.FirstOrDefault(z=>z.K==k);return x==null?null:x.V;}
    public static MPDxfResult Check(string text,double[] expectedExtentMm=null,double toleranceMm=.001){
        var result=new MPDxfResult();
        try{
            var lines=text.Replace("\r","").Split('\n').ToList();if(lines.LastOrDefault()=="")lines.RemoveAt(lines.Count-1);
            if(lines.Count%2!=0)throw new InvalidOperationException("Invalid ASCII DXF code/value count");
            var pairs=new List<Pair>();for(int i=0;i<lines.Count;i+=2)pairs.Add(new Pair{K=int.Parse(lines[i].Trim(),C),V=lines[i+1]});
            if(pairs.Count==0||pairs.Last().K!=0||pairs.Last().V!="EOF")throw new InvalidOperationException("DXF EOF terminator missing");
            if(expectedExtentMm!=null&&(expectedExtentMm.Length!=2||expectedExtentMm.Any(v=>!NV.Finite(v)||v<=0)))throw new InvalidOperationException("Invalid reference extents");
            int units=-1;for(int i=0;i<pairs.Count-1;i++)if(pairs[i].K==9&&pairs[i].V=="$INSUNITS")units=(int)Value(pairs.Skip(i+1).Take(1).ToList(),70,-1);
            var entities=new List<List<Pair>>();bool inside=false;int pos=0;
            while(pos<pairs.Count){
                if(pairs[pos].K==0&&pairs[pos].V=="SECTION"&&pos+1<pairs.Count&&pairs[pos+1].K==2){inside=pairs[pos+1].V=="ENTITIES";pos+=2;continue;}
                if(pairs[pos].K==0&&pairs[pos].V=="ENDSEC"){inside=false;pos++;continue;}
                if(inside&&pairs[pos].K==0){int end=pos+1;while(end<pairs.Count&&pairs[end].K!=0)end++;entities.Add(pairs.GetRange(pos,end-pos));pos=end;}
                else pos++;
            }
            if(entities.Count==0)throw new InvalidOperationException("Empty DXF ENTITIES");
            var originalRecords=new Dictionary<List<Pair>,List<List<Pair>>>();var logical=new List<List<Pair>>();
            for(int i=0;i<entities.Count;i++){
                var entity=entities[i];
                if(entity[0].V!="POLYLINE"){logical.Add(entity);continue;}
                if(((int)Value(entity,70)&(2|4|8|16|32|64))!=0)throw new InvalidOperationException("Unsupported fitted/3D/mesh POLYLINE; no curve simplification performed");
                var records=new List<List<Pair>>{entity};var vertices=new List<List<Pair>>();
                while(i+1<entities.Count&&entities[i+1][0].V=="VERTEX"){var v=entities[++i];Required(v,10);Required(v,20);if(Math.Abs(Value(v,30))>1e-7)throw new InvalidOperationException("Nonplanar POLYLINE vertex");vertices.Add(v);records.Add(v);}
                if(i+1>=entities.Count||entities[i+1][0].V!="SEQEND")throw new InvalidOperationException("POLYLINE SEQEND missing");records.Add(entities[++i]);
                var synthetic=new List<Pair>{new Pair{K=0,V="LWPOLYLINE"},new Pair{K=70,V=Value(entity,70).ToString(C)},new Pair{K=90,V=vertices.Count.ToString(C)}};
                foreach(var v in vertices)synthetic.AddRange(v.Where(p=>new[]{10,20,42,40,41}.Contains(p.K)));
                logical.Add(synthetic);originalRecords[synthetic]=records;
            }
            entities=logical;
            var endpoints=new List<MPPoint>();var bounds=new List<MPPoint>();var keys=new HashSet<string>();var output=new StringBuilder();
            Action<MPPoint,MPPoint,string> edge=(a,b,key)=>{if(!keys.Add(key))throw new InvalidOperationException("Duplicate cut entity");endpoints.Add(a);endpoints.Add(b);bounds.Add(a);bounds.Add(b);};
            Func<MPPoint,string> keyPoint=p=>N(Math.Round(p.X,9))+","+N(Math.Round(p.Y,9));
            Action<MPPoint,double,double,double,bool> arc=(center,r,start,end,full)=>{
                result.hasAnalyticCurves=true;
                if(!(r>0)||!NV.Finite(r))throw new InvalidOperationException("Invalid curve radius");
                if(Math.Abs(start)>1e6||Math.Abs(end)>1e6)throw new InvalidOperationException("Unstable curve angle magnitude");
                double rawSpan=end-start;double span=rawSpan%(2*Math.PI);if(span<=0)span+=2*Math.PI;
                start=(start%(2*Math.PI)+2*Math.PI)%(2*Math.PI);full|=Math.Abs(span-2*Math.PI)<1e-12;
                Func<double,MPPoint> point=a=>new MPPoint(center.X+r*Math.Cos(a),center.Y+r*Math.Sin(a));
                if(full){result.closedCurves++;if(!keys.Add("C:"+keyPoint(center)+":"+N(r)))throw new InvalidOperationException("Duplicate circle");bounds.Add(new MPPoint(center.X-r,center.Y-r));bounds.Add(new MPPoint(center.X+r,center.Y+r));}
                else {edge(point(start),point(start+span),"A:"+keyPoint(center)+":"+N(r)+":"+N(start)+":"+N(span));
                    for(int k=0;k<4;k++){double a=k*Math.PI/2;while(a<start)a+=2*Math.PI;if(a<=start+span+1e-12)bounds.Add(point(a));}}
            };
            int nextHandle=0x100;
            foreach(var e in entities){
                string type=e[0].V;
                if(!new[]{"LINE","ARC","CIRCLE","ELLIPSE","LWPOLYLINE"}.Contains(type))throw new InvalidOperationException("Unsupported cut DXF entity "+type+"; no approximated output created");
                if(Math.Abs(Value(e,30))>toleranceMm||Math.Abs(Value(e,31))>toleranceMm||Math.Abs(Value(e,38))>toleranceMm||
                    Math.Abs(Value(e,210))>1e-10||Math.Abs(Value(e,220))>1e-10||Math.Abs(Value(e,230,1)-1)>1e-10)throw new InvalidOperationException("Non-planar/unsupported DXF extrusion");
                if(Value(e,67)!=0||(Text(e,410)!=null&&Text(e,410)!="Model"))throw new InvalidOperationException("Paper-space entities cannot be certified as native cut contours");
                if(type=="LINE"){
                    var a=new MPPoint(Required(e,10),Required(e,20));var b=new MPPoint(Required(e,11),Required(e,21));if(a.Distance(b)<=1e-12)throw new InvalidOperationException("Zero length cut segment");
                    var k1=keyPoint(a);var k2=keyPoint(b);edge(a,b,"L:"+(String.CompareOrdinal(k1,k2)<0?k1+":"+k2:k2+":"+k1));
                }else if(type=="ARC"||type=="CIRCLE")arc(new MPPoint(Required(e,10),Required(e,20)),Required(e,40),type=="ARC"?Required(e,50)*Math.PI/180:0,type=="ARC"?Required(e,51)*Math.PI/180:0,type=="CIRCLE");
                else if(type=="ELLIPSE"){
                    result.hasAnalyticCurves=true;
                    var center=new MPPoint(Required(e,10),Required(e,20));double ax=Required(e,11),ay=Required(e,21),ratio=Required(e,40),u0=Required(e,41),u1=Required(e,42);
                    if(!(ratio>0&&ratio<=1)||!(ax*ax+ay*ay>0)||Math.Abs(u0)>1e6||Math.Abs(u1)>1e6)throw new InvalidOperationException("Invalid ellipse parameters");
                    double bx=-ay*ratio,by=ax*ratio,rawSpan=u1-u0,span=rawSpan%(2*Math.PI);if(span<=0)span+=2*Math.PI;u0=(u0%(2*Math.PI)+2*Math.PI)%(2*Math.PI);
                    Func<double,MPPoint> point=u=>new MPPoint(center.X+ax*Math.Cos(u)+bx*Math.Sin(u),center.Y+ay*Math.Cos(u)+by*Math.Sin(u));
                    string key="E:"+keyPoint(center)+":"+N(ax)+":"+N(ay)+":"+N(ratio)+":"+N(u0)+":"+N(span);
                    if(Math.Abs(span-2*Math.PI)<1e-12){if(!keys.Add(key))throw new InvalidOperationException("Duplicate ellipse");result.closedCurves++;}
                    else edge(point(u0),point(u0+span),key);
                    bounds.Add(point(u0));bounds.Add(point(u0+span));
                    foreach(double baseAngle in new[]{Math.Atan2(bx,ax),Math.Atan2(by,ay)})for(int k=0;k<2;k++){
                        double angle=(baseAngle+k*Math.PI+2*Math.PI)%(2*Math.PI);while(angle<u0)angle+=2*Math.PI;if(angle<=u0+span+1e-12)bounds.Add(point(angle));}
                }else{
                    var vertices=new List<MPPoint>();var bulges=new List<double>();
                    if(e.Any(p=>(p.K==40||p.K==41||p.K==43)&&Number(p.V)!=0))throw new InvalidOperationException("Nonzero polyline width cannot be treated as a verified cut contour");
                    foreach(var p in e){if(p.K==10){vertices.Add(new MPPoint(Number(p.V),0));bulges.Add(0);}if(p.K==20&&vertices.Count>0)vertices.Last().Y=Number(p.V);if(p.K==42&&vertices.Count>0)bulges[bulges.Count-1]=Number(p.V);}
                    if(vertices.Count<2||vertices.Count!=(int)Value(e,90,vertices.Count))throw new InvalidOperationException("Invalid LWPOLYLINE vertex count");
                    if(e.Count(p=>p.K==20)!=vertices.Count)throw new InvalidOperationException("Missing/duplicate polyline ordinate");
                    bool closed=((int)Value(e,70)&1)!=0;int count=closed?vertices.Count:vertices.Count-1;
                    for(int i=0;i<count;i++){var a=vertices[i];var b=vertices[(i+1)%vertices.Count];double bulge=bulges[i];
                        if(a.Distance(b)<=1e-12||Math.Abs(bulge)>1e8)throw new InvalidOperationException("Degenerate/unstable polyline segment");
                        if(bulge==0){var k1=keyPoint(a);var k2=keyPoint(b);edge(a,b,"L:"+(String.CompareOrdinal(k1,k2)<0?k1+":"+k2:k2+":"+k1));}
                        else {double dx=b.X-a.X,dy=b.Y-a.Y,chord=a.Distance(b),h=(1-bulge*bulge)/(4*bulge);var center=new MPPoint((a.X+b.X)/2-dy*h,(a.Y+b.Y)/2+dx*h);
                            double start=Math.Atan2(a.Y-center.Y,a.X-center.X),finish=Math.Atan2(b.Y-center.Y,b.X-center.X);if(bulge<0){double z=start;start=finish;finish=z;}arc(center,chord*(1+bulge*bulge)/(4*Math.Abs(bulge)),start,finish,false);}
                    }
                }
                result.entityTypes.Add(originalRecords.ContainsKey(e)?"POLYLINE":type);
                // Geometry strings stay unchanged. Only layer/ownership metadata is rebuilt.
                var originals=originalRecords.ContainsKey(e)?originalRecords[e]:new List<List<Pair>>{e};
                string parentHandle=null;
                foreach(var record in originals){
                    string recordType=record[0].V,handle=(nextHandle++).ToString("X",C);
                    string owner=parentHandle??"30";if(recordType=="POLYLINE")parentHandle=handle;
                    output.Append("0\n").Append(recordType).Append("\n5\n").Append(handle).Append("\n330\n").Append(owner).Append("\n100\nAcDbEntity\n8\nCUT_THROUGH\n");
                    bool hasSpecific=record.Any(p=>p.K==100&&p.V!="AcDbEntity");
                    if(!hasSpecific){
                        string subclass=recordType=="LINE"?"AcDbLine":recordType=="CIRCLE"||recordType=="ARC"?"AcDbCircle":recordType=="ELLIPSE"?"AcDbEllipse":
                            recordType=="LWPOLYLINE"?"AcDbPolyline":recordType=="POLYLINE"?"AcDb2dPolyline":recordType=="VERTEX"?"AcDbVertex":null;
                        if(subclass!=null)output.Append("100\n").Append(subclass).Append('\n');
                        if(recordType=="VERTEX")output.Append("100\nAcDb2dVertex\n");
                    }
                    bool arcSubclass=hasSpecific;
                    foreach(var p in record.Skip(1).Where(p=>p.K!=8&&p.K!=5&&p.K!=105&&p.K!=330&&p.K!=360&&p.K!=102&&p.K!=6&&p.K<1000&&!(p.K==100&&p.V=="AcDbEntity"))){
                        if(recordType=="ARC"&&!arcSubclass&&(p.K==50||p.K==51)){output.Append("100\nAcDbArc\n");arcSubclass=true;}
                        output.Append(p.K).Append('\n').Append(p.V).Append('\n');
                    }
                }
            }
            var nodes=new List<MPPoint>();var degree=new List<int>();var connection=new List<int[]>();
            for(int i=0;i<endpoints.Count;i+=2){var ids=new int[2];for(int j=0;j<2;j++){var p=endpoints[i+j];var matches=nodes.Select((n,k)=>new{n,k}).Where(x=>x.n.Distance(p)<=1e-7).ToArray();
                if(matches.Length>1)throw new InvalidOperationException("Ambiguous near-coincident contour endpoints");
                    int id=matches.Length==1?matches[0].k:nodes.Count;if(id==nodes.Count){nodes.Add(p);degree.Add(0);}degree[id]++;ids[j]=id;}
                connection.Add(ids);}
            if(degree.Any(d=>d!=2))throw new InvalidOperationException("Open or branching cut contour; geometry was not snapped or altered");
            var visited=new HashSet<int>();foreach(var id in Enumerable.Range(0,nodes.Count)){if(visited.Contains(id))continue;result.closedConnectedContours++;var stack=new Stack<int>();stack.Push(id);
                while(stack.Count>0){int v=stack.Pop();if(!visited.Add(v))continue;foreach(var c in connection.Where(c=>c.Contains(v)))foreach(int w in c)stack.Push(w);}}
            result.extentsMm=new[]{bounds.Min(p=>p.X),bounds.Min(p=>p.Y),bounds.Max(p=>p.X),bounds.Max(p=>p.Y)};
            if(expectedExtentMm!=null){
                double w=result.extentsMm[2]-result.extentsMm[0],h=result.extentsMm[3]-result.extentsMm[1];
                if(Math.Abs(w-expectedExtentMm[0])>toleranceMm||Math.Abs(h-expectedExtentMm[1])>toleranceMm)throw new InvalidOperationException("DXF scale/alignment check failed against isolated flattened BREP extents in mm");
                if(units!=4&&units!=0&&units!=-1)throw new InvalidOperationException("DXF declares incompatible units");
            }else if(units!=4)throw new InvalidOperationException("Millimetre units not verified");
            result.normalizedText=Wrap(output.ToString());result.entityCount=entities.Count;result.accepted=true;result.reason="Closed cut contours, no duplicate supported entities, millimetre metadata and geometry provenance/scale checks. Not a production certificate.";
        }catch(Exception ex){result.accepted=false;result.reason=ex.Message;result.normalizedText=null;}
        return result;
    }
}
