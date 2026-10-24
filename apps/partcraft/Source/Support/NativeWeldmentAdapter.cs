using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using SolidWorks.Interop.sldworks;

// Native data adapter, not a new profile/angle reconstruction algorithm.
public class NativeCutListValue {
    public string name,raw,resolved;public bool wasResolved,linked;public int resultCode;
}
public class AuthoritativeFolderProfile {
    public string cutList,source="SolidWorks CutListFolder",profilePolicy="Description retained as assigned; ACTUAL PROFILE requires confirmed native geometry/Structural Member section",materialRole="REFERENCE_ONLY_COLOR_ASSIGNMENT_NOT_ACTUAL_MATERIAL";
    public NativeCutListValue Description,MATERIAL,LENGTH,ANGLE1,ANGLE2,QUANTITY;
    public int physicalBodyCount;
    public double? angle1Degrees,angle2Degrees;
}
public class ActualProfileEvidence {
    public bool geometryVerified;
    public string nativeEvidence,verifiedSectionKey;
    public double? widthMm,heightMm,wallMm;
}
public static class NativeWeldmentAdapter {
    public const string MacroSHA256="741E1216878BD4C150F88223C27B781E5053E291F362F7DEDCA450ACE614E2AB";
    public static object[] Items(object o){if(o==null)return new object[0];var values=o as System.Collections.IEnumerable;if(values==null||o is string)throw new ArgumentException("Expected array/enumerable collection, not scalar");return values.Cast<object>().ToArray();}
    static NativeCutListValue Find(Dictionary<string,NativeCutListValue> values,params string[] names){
        foreach(string name in names){var found=values.Where(p=>String.Equals(p.Key,name,StringComparison.OrdinalIgnoreCase)).ToArray();if(found.Length>1)throw new InvalidOperationException("Ambiguous Cut List property "+name);if(found.Length==1)return found[0].Value;}return null;
    }
    public static AuthoritativeFolderProfile Profile(string folder,Dictionary<string,NativeCutListValue> values,int physicalCount){
        var p=new AuthoritativeFolderProfile{cutList=folder,physicalBodyCount=physicalCount,Description=Find(values,"Description","Описание"),MATERIAL=Find(values,"MATERIAL","Материал"),LENGTH=Find(values,"LENGTH","ДЛИНА"),ANGLE1=Find(values,"ANGLE1","УГОЛ1"),ANGLE2=Find(values,"ANGLE2","УГОЛ2"),QUANTITY=Find(values,"QUANTITY","КОЛИЧЕСТВО")};
        p.angle1Degrees=p.ANGLE1==null?null:NativeNumber(p.ANGLE1.resolved,true);p.angle2Degrees=p.ANGLE2==null?null:NativeNumber(p.ANGLE2.resolved,true);return p;
    }
    static double[] RectangularDescription(string description){
        // Parse only an explicit rectangular/square nominal designation.
        // Preserve Description unchanged even if this optional parser cannot read it.
        var m=Regex.Match(description??"",@"^\s*(\d+(?:[.,]\d+)?)\s*[xхХX×]\s*(\d+(?:[.,]\d+)?)(?:\s*[xхХX×]\s*(\d+(?:[.,]\d+)?))?\s*$");
        if(!m.Success)return null;double a=NativeNumber(m.Groups[1].Value,false).Value,b=NativeNumber(m.Groups[2].Value,false).Value;
        if(a<=0||b<=0)return null;return new[]{a,b};
    }
    // Retained only for regression comparison with the previous policy.
    // Not exposed to the 1111 production adapter.
    static double? HFromDescription(string description,int? geometricallyVerifiedCutSideIndex){
        var dims=RectangularDescription(description);if(dims==null||!geometricallyVerifiedCutSideIndex.HasValue)return null;
        if(geometricallyVerifiedCutSideIndex==0)return dims[1];if(geometricallyVerifiedCutSideIndex==1)return dims[0];return null;
    }
    public static double? HFromActualProfile(ActualProfileEvidence profile,int? verifiedCutSideIndex){
        if(profile==null||!profile.geometryVerified||String.IsNullOrWhiteSpace(profile.nativeEvidence)||!profile.widthMm.HasValue||!profile.heightMm.HasValue||profile.widthMm<=0||profile.heightMm<=0)return null;
        if(verifiedCutSideIndex==0)return profile.heightMm;if(verifiedCutSideIndex==1)return profile.widthMm;return null;
    }
    public static string ProductionGroupKey(ActualProfileEvidence profile,double? lengthMm,double? angle1,double? angle2,double? h,string dir1,string dir2,string verifiedProcessingKey){
        if(profile==null||!profile.geometryVerified||String.IsNullOrWhiteSpace(profile.nativeEvidence)||String.IsNullOrWhiteSpace(profile.verifiedSectionKey)||!lengthMm.HasValue||!angle1.HasValue||!angle2.HasValue||!h.HasValue||String.IsNullOrWhiteSpace(dir1)||String.IsNullOrWhiteSpace(dir2)||String.IsNullOrWhiteSpace(verifiedProcessingKey))return null;
        var numbers=new[]{lengthMm.Value,angle1.Value,angle2.Value,h.Value};if(numbers.Any(x=>Double.IsNaN(x)||Double.IsInfinity(x))||lengthMm<=0||h<=0)return null;
        // Explicitly no MATERIAL or Description in this production key.
        return String.Join("|",new[]{profile.verifiedSectionKey}.Concat(numbers.Select(x=>x.ToString("R",CultureInfo.InvariantCulture))).Concat(new[]{dir1,dir2,verifiedProcessingKey}).Select(x=>x.Length+":"+x));
    }
    public static double? NativeNumber(string s,bool angle){
        if(String.IsNullOrWhiteSpace(s))return null;s=s.Trim();
        string pattern=angle?@"^[+-]?\d+(?:[.,]\d+)?\s*°?$":@"^[+-]?\d+(?:[.,]\d+)?$";
        if(!Regex.IsMatch(s,pattern))return null;double v;
        if(!Double.TryParse(s.Replace("°","").Trim().Replace(',','.'),NumberStyles.AllowLeadingSign|NumberStyles.AllowDecimalPoint,CultureInfo.InvariantCulture,out v)||Double.IsNaN(v)||Double.IsInfinity(v))return null;
        return v;
    }
    // Profiles+h::SwapToLyingHeight, with fail-closed nominal-data policy.
    // Actual cut-side measurement is supplied by the macro's native face logic,
    // never by a world-axis bounding box or a material/feature name.
    static double? LyingHeight(double measuredCutSide,double? height,double? width){
        const double equal=.75,snap=1.5; // exact original macro constants
        if(!height.HasValue||!width.HasValue||height<=0||width<=0||measuredCutSide<=0)return null;
        if(Math.Abs(height.Value-width.Value)<=equal)return height;
        if(Math.Abs(measuredCutSide-height.Value)<=snap)return width;
        if(Math.Abs(measuredCutSide-width.Value)<=snap)return height;
        return null;
    }
    public static bool Inside(string root,string path){
        return !String.IsNullOrWhiteSpace(root)&&!String.IsNullOrWhiteSpace(path)&&Path.GetFullPath(path).StartsWith(Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar)+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase);
    }
    // Callable insertion seam for the existing drawing pipeline. It is NOT
    // invoked by the read-only support checker, nor on an original model.
    public static WeldmentCutListAnnotation InsertNativeTable(SldWorks sw,SolidWorks.Interop.sldworks.View view,string workspace,string exactCopiedModel,string exactConfiguration,string template){
        if(!Inside(workspace,exactCopiedModel)||!Directory.Exists(workspace)||!File.Exists(exactCopiedModel))throw new InvalidOperationException("Owned copied model required");
        if(!String.Equals(new DirectoryInfo(workspace).Name,"Workspace",StringComparison.Ordinal))throw new InvalidOperationException("Verified prepared Workspace required");
        string p=Path.GetFullPath(workspace);while(p!=null){if((File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new InvalidOperationException("Reparse workspace rejected");p=Path.GetDirectoryName(p);}
        if(!File.Exists(template)||!String.Equals(Path.GetExtension(template),".sldwldtbt",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Existing native weldment table template required");
        if(!String.Equals(Path.GetFullPath(view.GetReferencedModelName()),Path.GetFullPath(exactCopiedModel),StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Drawing view model differs");
        var model=sw.GetOpenDocumentByName(exactCopiedModel) as ModelDoc2;
        if(model==null||String.IsNullOrEmpty(exactConfiguration)||model.GetConfigurationByName(exactConfiguration)==null||view.ReferencedConfiguration!=exactConfiguration)throw new InvalidOperationException("Exact loaded view configuration required; no constructed As Welded name");
        var drawing=sw.ActiveDoc as ModelDoc2;
        if(drawing==null||drawing.GetType()!=3||!Inside(workspace,drawing.GetPathName()))throw new InvalidOperationException("Active saved owned drawing required");
        bool belongs=Items(((DrawingDoc)drawing).GetViews()).SelectMany(Items).Any(v=>sw.IsSame(v,view)==1);
        if(!belongs)throw new InvalidOperationException("View does not belong to active owned drawing");
        var table=view.InsertWeldmentTable(false,.02,.25,1,exactConfiguration,template);
        if(table==null)throw new InvalidOperationException("Native IView.InsertWeldmentTable returned null");
        return table;
    }
    public static object ReadNativeTable(WeldmentCutListAnnotation native){
        var table=(TableAnnotation)(object)native;var columns=new List<object>();
        for(int c=0;c<table.ColumnCount;c++){
            string property=null,error=null;try{property=native.GetColumnCustomProperty(c);}catch(Exception e){error=e.ToString();}
            columns.Add(new{column=c,title=table.GetColumnTitle(c),type=table.GetColumnType(c),nativeCustomProperty=property,error});
        }
        var rows=new List<string[]>();for(int r=0;r<table.RowCount;r++){
            var text=new string[table.ColumnCount];for(int c=0;c<text.Length;c++)text[c]=table.get_DisplayedText2(r,c,true);rows.Add(text);
        }
        return new{api="IView.InsertWeldmentTable / ITableAnnotation.DisplayedText2",columns,rows,selectionRequired=false,sourceTextRewritten=false};
    }
    public static int Tests(){
        int passed=0;Action<bool> check=x=>{if(!x)throw new Exception("Adapter regression "+passed);passed++;};
        check(Items(new System.Collections.ArrayList{1,2}).Length==2);check(Items(new object[]{1,2}).Length==2);check(Items(null).Length==0);check(NativeNumber("-",true)==null);check(NativeNumber(null,true)==null);check(NativeNumber("",true)==null);
        check(NativeNumber("0°",true)==0);check(NativeNumber("41°",true)==41);check(NativeNumber("51,5°",true)==51.5);
        check(NativeNumber("\"ANGLE1@@@x\"",true)==null);check(NativeNumber("15 mm",false)==null);
        check(NativeNumber("3375.19",false)==3375.19);check(LyingHeight(40,20,40)==20);check(LyingHeight(20,20,40)==40);
        check(LyingHeight(15,15,15)==15);check(LyingHeight(40,null,40)==null);check(LyingHeight(22,20,40)==null);
        check(Inside(@"C:\Копия",@"C:\Копия\модель.SLDPRT"));check(!Inside(@"C:\Копия",@"C:\Копия-old\модель.SLDPRT"));
        var props=new Dictionary<string,NativeCutListValue>{{"Description",new NativeCutListValue{resolved="40x20x1,5",raw="40x20x1,5"}},{"ANGLE1",new NativeCutListValue{resolved="-"}},{"ANGLE2",new NativeCutListValue{resolved="41°"}},{"QUANTITY",new NativeCutListValue{resolved="2"}}};
        var folder=Profile("10",props,2);check(folder.Description.resolved=="40x20x1,5");check(folder.angle1Degrees==null);check(folder.angle2Degrees==41);check(folder.QUANTITY.resolved=="2"&&folder.physicalBodyCount==2);
        check(HFromDescription(folder.Description.resolved,0)==20);check(HFromDescription(folder.Description.resolved,1)==40);
        check(HFromDescription(folder.Description.resolved,null)==null);check(HFromDescription("40×20×1.5",0)==20);
        check(HFromDescription("25х25х1,5",1)==25);check(HFromDescription("unknown",0)==null);
        check(Profile("empty",new Dictionary<string,NativeCutListValue>(),1).Description==null);
        props["MATERIAL"]=new NativeCutListValue{resolved="DK 15x15 square"};props["LENGTH"]=new NativeCutListValue{resolved="460.92"};
        var conflict=Profile("3",props,1);check(conflict.Description.resolved=="40x20x1,5"&&conflict.MATERIAL.resolved=="DK 15x15 square");
        check(conflict.LENGTH.resolved=="460.92");check(HFromDescription("40x20x1,5",7)==null);
        var actual=new ActualProfileEvidence{geometryVerified=true,nativeEvidence="native verified section fixture",verifiedSectionKey="RECT40x25x1.5-with-corner-signature",widthMm=40,heightMm=25,wallMm=1.5};
        check(HFromActualProfile(actual,0)==25);check(HFromActualProfile(actual,1)==40);
        check(HFromActualProfile(null,0)==null);check(HFromActualProfile(new ActualProfileEvidence{widthMm=40,heightMm=25},0)==null);
        string key=ProductionGroupKey(actual,100,0,41,25,"A","B","NO_ADDITIONAL_MACHINING_VERIFIED");check(key!=null);
        props["MATERIAL"].resolved="Red display paint only";check(ProductionGroupKey(actual,100,0,41,25,"A","B","NO_ADDITIONAL_MACHINING_VERIFIED")==key);
        check(ProductionGroupKey(actual,101,0,41,25,"A","B","NO_ADDITIONAL_MACHINING_VERIFIED")!=key);
        check(ProductionGroupKey(actual,100,0,41,25,"A","B","DRILLED_VERIFIED")!=key);
        check(ProductionGroupKey(actual,100,null,41,25,"A","B","NONE")==null);
        check(ProductionGroupKey(actual,100,0,41,25,"A","B",null)==null);
        check(ProductionGroupKey(actual,100,0,41,25,null,"B","NONE")==null);
        Console.WriteLine("Native weldment adapter: "+passed+"/"+passed+" PASS; no COM");return 0;
    }
}
