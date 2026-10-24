using System;
using System.IO;
public static class ManufacturingSourceBendTestsV13 {
    static SourceBoundaryStateV13 State(){return new SourceBoundaryStateV13{dirty=false,updateStamp=1753,path=@"D:\Проект\Workspace\Деталь.SLDPRT",configuration="По умолчанию<As Machined>"};}
    static BendBindingV06 Binding(int options){return new BendBindingV06{bodyName="Body",configurationApiName="Default",flatPatternFeature="Flat",modelPath="copy.SLDPRT",isolatedSolidCount=1,options=options,flatPatternBodyVerified=true,contextUnchanged=true};}
    public static int Run(){
        int count=0,failed=0;Action<string,Func<bool>> test=(name,run)=>{bool ok=false;try{ok=run();}catch(Exception ex){Console.Error.WriteLine(ex);}count++;if(!ok)failed++;Console.WriteLine("Source/BEND "+name+": "+(ok?"PASS":"FAIL"));};
        test("unchanged source accepted",()=>State().Same(State()));
        test("dirty change rejected despite unchanged path",()=>{var b=State();b.dirty=true;return !State().Same(b);});
        test("update stamp change rejected even when clean",()=>{var b=State();b.updateStamp++;return !State().Same(b);});
        test("configuration change rejected",()=>{var b=State();b.configuration="Other";return !State().Same(b);});
        test("source path change rejected",()=>{var b=State();b.path=@"D:\Other\Деталь.SLDPRT";return !State().Same(b);});
        string cut=ManufacturingDxfV01.Circle(new MPPoint(5,5),5),line=ManufacturingDxfV01.Line(new MPPoint(0,0),new MPPoint(1,1));
        test("native options 5 removes entire exact CUT multiset",()=>{var r=ManufacturingBendDxfV06.Validate(ManufacturingDxfV01.Wrap(cut+line),ManufacturingDxfV01.Wrap(cut),Binding(5));return r.structureAccepted&&r.isBendOnlyProven&&r.outputEntityCount==1&&r.matchedCutRecords==1;});
        test("options 5 with missing CUT cannot prove separate bends",()=>{var r=ManufacturingBendDxfV06.Validate(ManufacturingDxfV01.Wrap(line),ManufacturingDxfV01.Wrap(cut),Binding(5));return !r.isBendOnlyProven&&r.status=="NEEDS_REVIEW";});
        test("unsupported export mask not accepted as BEND",()=>!Binding(1).Verified());
        test("options 5 with only CUT is incomplete",()=>!ManufacturingBendDxfV06.Validate(ManufacturingDxfV01.Wrap(cut),ManufacturingDxfV01.Wrap(cut),Binding(5)).structureAccepted);
        test("snapshot copies independent saved bytes",()=>{
            string dir=Path.Combine("work","source-snapshot-test-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(dir);
            string source=Path.Combine(dir,"source.SLDPRT"),snapshot=Path.Combine(dir,"snapshot.SLDPRT"),copy=Path.Combine(dir,"body.SLDPRT");
            File.WriteAllText(source,"saved geometry fixture");string hash=ManufacturingSourceFilesV03.Hash(source,"TEST");ManufacturingSourceFilesV03.CopySavedSource(source,snapshot,hash,false);
            File.SetAttributes(snapshot,FileAttributes.ReadOnly);ManufacturingSourceFilesV03.CopySavedSource(snapshot,copy,hash,false);File.WriteAllText(copy,"owned changes");
            return ManufacturingSourceFilesV03.Hash(source,"TEST")==hash&&ManufacturingSourceFilesV03.Hash(snapshot,"TEST")==hash;
        });
        Console.WriteLine("Source/BEND regressions: "+(count-failed)+"/"+count+" PASS; no COM");return failed==0?0:1;
    }
}
