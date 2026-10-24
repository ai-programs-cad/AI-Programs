using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
public static class DxfGeometryTestsV02 {
    static GV V(string x,string y){return new GV(GQ.Parse(x),GQ.Parse(y));}
    static GE Line(GV a,GV b){return new GE{type="LINE",rawType="LINE",a=a,b=b};}
    static GE Spline(string middle){return new GE{type="SPLINE",rawType="SPLINE",degree=2,control=new[]{V("0","0"),V("5",middle),V("10","0")},knots=new GQ[]{0,0,0,1,1,1},weights=new GQ[]{1,1,1}};}
    public static int Run(){int count=0,failed=0;Action<string,Func<bool>> test=(name,fn)=>{count++;bool ok=false;try{ok=fn();}catch(Exception ex){Console.Error.WriteLine(ex);}if(!ok)failed++;Console.WriteLine(name+": "+(ok?"PASS":"FAIL"));};var id=new GT{a=1,d=1};
        test("Exact decimal arithmetic keeps sub-double change",()=>!(GQ.Parse("1.00000000000000000001")-1).Eq(0));
        test("LINE reverse parameterization",()=>DxfGeometryV02.Bound(Line(V("0","0"),V("10","0")),Line(V("10","0"),V("0","0")),id).Value.Eq(0));
        test("Sub-tolerance LINE whole bound",()=>DxfGeometryV02.Bound(Line(V("0","0"),V("10","0")),Line(V("0","0.0001"),V("10","0.0001")),id).Value<=DxfGeometryV02.Tolerance);
        test("Changed radius rejected",()=>DxfGeometryV02.Bound(new GE{type="CIRCLE",center=V("0","0"),radius=5},new GE{type="CIRCLE",center=V("0","0"),radius=GQ.Parse("5.01")},id).Value>DxfGeometryV02.Tolerance);
        test("Minor vs major ARC not accepted",()=>DxfGeometryV02.Bound(new GE{type="ARC",center=V("0","0"),radius=5,start=0,span=90},new GE{type="ARC",center=V("0","0"),radius=5,start=0,span=270},id).Value>DxfGeometryV02.Tolerance);
        test("Rational positive-weight spline full control hull bound",()=>DxfGeometryV02.Bound(Spline("2"),Spline("2.0001"),id).Value.Eq(GQ.Parse("0.0001")));
        test("Spline changed shape rejected",()=>DxfGeometryV02.Bound(Spline("2"),Spline("2.01"),id).Value>DxfGeometryV02.Tolerance);
        test("Spline reversed full parameterization",()=>{var a=Spline("2");var b=Spline("2");b.control=b.control.Reverse().ToArray();return DxfGeometryV02.Bound(a,b,id).Value.Eq(0);});
        test("Affine knot-domain normalization",()=>{var a=Spline("2");var b=Spline("2");b.knots=new GQ[]{4,4,4,9,9,9};return DxfGeometryV02.Bound(a,b,id).Value.Eq(0);});
        var square=new List<GE>{Line(V("0","0"),V("10","0")),Line(V("10","0"),V("10","8")),Line(V("10","8"),V("0","8")),Line(V("0","8"),V("0","0"))};
        test("Closed full loop",()=>{bool closed;int countLoops;List<int[]> components;DxfGeometryV02.Topology(square,out closed,out countLoops,out components);return closed&&countLoops==1;});
        test("Missing edge is open",()=>{bool closed;int loops;List<int[]> components;DxfGeometryV02.Topology(square.Take(3).ToList(),out closed,out loops,out components);return !closed;});
        test("Rotation and translation found, shuffled edges",()=>{var t=new GT{a=-1,d=-1,t=V("31","42")};var moved=square.Select(e=>Line(t.Apply(e.b),t.Apply(e.a))).Reverse().ToList();return DxfGeometryV02.Match(square,moved).matched;});
        test("Scale two cannot pass",()=>!DxfGeometryV02.Match(square,square.Select(e=>Line(new GV(2*e.a.x,2*e.a.y),new GV(2*e.b.x,2*e.b.y))).ToList()).matched);
        test("Hole displacement rejected",()=>{var a=square.Concat(new[]{new GE{type="CIRCLE",center=V("3","3"),radius=1}}).ToList();var b=square.Concat(new[]{new GE{type="CIRCLE",center=V("4","3"),radius=1}}).ToList();return !DxfGeometryV02.Match(a,b).matched;});
        test("Bend-line displacement rejected",()=>!DxfGeometryV02.Match(square.Concat(new[]{Line(V("2","0"),V("2","8"))}).ToList(),square.Concat(new[]{Line(V("3","0"),V("3","8"))}).ToList()).matched);
        test("Unsupported different knot basis is not certified",()=>{var a=Spline("2");var b=Spline("2");b.degree=1;return !DxfGeometryV02.Bound(a,b,id).HasValue;});
        test("Collinear complete cubic SPLINE equals LINE representation",()=>{
            Directory.CreateDirectory("work");string p=Path.Combine("work","line-spline-"+Guid.NewGuid().ToString("N")+".dxf");
            string raw="0\nSPLINE\n70\n8\n71\n3\n72\n8\n73\n4\n40\n0\n40\n0\n40\n0\n40\n0\n40\n1\n40\n1\n40\n1\n40\n1\n10\n0\n20\n0\n10\n3\n20\n0\n10\n7\n20\n0\n10\n10\n20\n0\n";
            File.WriteAllText(p,ManufacturingDxfV04.Wrap(raw));var curve=DxfGeometryV02.Read(p).entities[0];return curve.type=="LINE"&&curve.rawType=="SPLINE"&&DxfGeometryV02.Bound(Line(V("0","0"),V("10","0")),curve,id).Value.Eq(0);
        });
        test("Missing required LINE coordinate cannot become zero",()=>{string p=Path.Combine("work","incomplete-line-"+Guid.NewGuid().ToString("N")+".dxf");File.WriteAllText(p,ManufacturingDxfV04.Wrap("0\nLINE\n10\n0\n20\n0\n11\n10\n"));try{DxfGeometryV02.Read(p);return false;}catch(InvalidOperationException){return true;}});
        Console.WriteLine("Geometry regressions: "+(count-failed)+"/"+count+" PASS; no COM");return failed==0?0:1;
    }
}
