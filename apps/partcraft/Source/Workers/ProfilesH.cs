using System;
using System.Linq;
using System.Collections.Generic;
using SolidWorks.Interop.sldworks;

// Port of Profiles_h1: GetLongAxis, GetCrossSectionAxes, GetEndPlaneNormals,
// ClassifyEndCut, DetermineCutHeight and SwapToLyingHeight. Original SWP is retained.
// No material data enters this calculation. Unsupported geometry fails explicitly.
public static class ProfilesH {
 public const string MacroHash="741E1216878BD4C150F88223C27B781E5053E291F362F7DEDCA450ACE614E2AB";
 public static double Dot(double[] a,double[] b){return a.Zip(b,(x,y)=>x*y).Sum();}
 static double[] Sub(double[] a,double[] b){return a.Zip(b,(x,y)=>x-y).ToArray();}
 static double Norm(double[] a){return Math.Sqrt(Dot(a,a));}
 static double[] Unit(double[] a){double n=Norm(a);if(n<1e-12)throw new Exception("Zero vector");return a.Select(x=>x/n).ToArray();}
 static double[] N(object a){return NativeWeldmentAdapter.Items(a).Select(Convert.ToDouble).ToArray();}
 static double Extreme(Body2 b,double[] a){double x,y,z;if(!b.GetExtremePoint(a[0],a[1],a[2],out x,out y,out z))throw new Exception("GetExtremePoint returned false");return Dot(a,new[]{x,y,z});}
 static double Min(Body2 b,double[] a){return -Extreme(b,a.Select(x=>-x).ToArray());}
 public static double Swap(double cut,double nominalHeight,double nominalWidth){
  if(nominalHeight<=0||nominalWidth<=0)throw new Exception("Nominal section dimensions not verified");
  if(Math.Abs(nominalHeight-nominalWidth)<=.75)return nominalHeight;
  if(Math.Abs(cut-nominalHeight)<=1.5)return nominalWidth;
  if(Math.Abs(cut-nominalWidth)<=1.5)return nominalHeight;
  throw new Exception("Cut side does not match verified nominal section");
 }
 static double Snap(double value,double h,double w){if(Math.Abs(value-h)<=1.5)return h;if(Math.Abs(value-w)<=1.5)return w;return value;}
 public static double Combine(double side1,double side2,double h,double w){
  double cut;
  if(side1==0&&side2==0)cut=w; // original macro's defined straight/straight convention
  else if(side1==0)cut=Snap(side2,h,w);
  else if(side2==0)cut=Snap(side1,h,w);
  else {side1=Snap(side1,h,w);side2=Snap(side2,h,w);if(Math.Abs(side1-side2)>.75)throw new Exception("Cuts use different section planes; see part drawing");cut=(side1+side2)/2;}
  return Swap(cut,h,w);
 }
 static double Classify(double[] n,double[] axis,double[] u,double du,double[] v,double dv){
  double a=Dot(n,axis);double[] t=n.Zip(axis,(x,y)=>x-a*y).ToArray();
  if(Norm(t)<.01)return 0;t=Unit(t);double a1=Math.Abs(Dot(t,u)),a2=Math.Abs(Dot(t,v));
  if(a1<.98&&a2<.98)throw new Exception("Compound end cut; see part drawing");return 1000*(a1>=a2?du:dv);
 }
 public static Dictionary<string,object> Read(Body2 body,double h,double w){
  double[] axis=null;double max=0;
  foreach(Edge edge in NativeWeldmentAdapter.Items(body.GetEdges())){var p=edge.GetStartVertex();var q=edge.GetEndVertex();if(p==null||q==null)continue;double[] d=Sub(N(q.GetPoint()),N(p.GetPoint()));double len=Norm(d);if(len>max){max=len;axis=Unit(d);}}
  if(axis==null)throw new Exception("Profiles+h: longitudinal axis unavailable");
  var dirs=new List<double[]>();var areas=new List<double>();
  var planes=new List<Tuple<double[],double[],double>>();
  foreach(Face2 face in NativeWeldmentAdapter.Items(body.GetFaces())){var surface=(Surface)face.GetSurface();if(!surface.IsPlane())continue;var p=N(surface.PlaneParams);double[] n=Unit(p.Take(3).ToArray());double area=face.GetArea();planes.Add(Tuple.Create(n,N(face.GetBox()),area));if(Math.Abs(Dot(n,axis))>=.12)continue;int i=dirs.FindIndex(x=>Math.Abs(Dot(x,n))>=.985);if(i<0){if(dirs.Count>=60)throw new Exception("Profiles+h: section direction limit");dirs.Add(n);areas.Add(area);}else areas[i]+=area;}
  int first=-1,second=-1;double best=-1;
  for(int i=0;i<dirs.Count;i++)for(int j=i+1;j<dirs.Count;j++)if(Math.Abs(Dot(dirs[i],dirs[j]))<=.3&&areas[i]+areas[j]>best){first=i;second=j;best=areas[i]+areas[j];}
  if(first<0)throw new Exception("Profiles+h: two transverse axes unavailable");
  double[] u=dirs[first],v=dirs[second];double du=Extreme(body,u)-Min(body,u),dv=Extreme(body,v)-Min(body,v),lo=Min(body,axis),hi=Extreme(body,axis);
  Tuple<double[],double[],double> end1=null,end2=null;
  foreach(var plane in planes){if(Math.Abs(Dot(plane.Item1,axis))<=.15)continue;double[] box=plane.Item2;double t=Dot(axis,new[]{(box[0]+box[3])/2,(box[1]+box[4])/2,(box[2]+box[5])/2});if(t<=lo+(hi-lo)*.35&&(end1==null||plane.Item3>end1.Item3))end1=plane;if(t>=hi-(hi-lo)*.35&&(end2==null||plane.Item3>end2.Item3))end2=plane;}
  if(end1==null||end2==null||Object.ReferenceEquals(end1,end2))throw new Exception("Profiles+h: two end planes unavailable");
  double side1=Classify(end1.Item1,axis,u,du,v,dv),side2=Classify(end2.Item1,axis,u,du,v,dv);
  return new Dictionary<string,object>{{"H",Combine(side1,side2,h,w)},{"axis",axis},{"sectionAxis1",u},{"sectionAxis2",v},{"sectionExtent1Mm",1000*du},{"sectionExtent2Mm",1000*dv},{"endNormal1",end1.Item1},{"endNormal2",end2.Item1},{"cutSide1Mm",side1},{"cutSide2Mm",side2},{"straightConvention",side1==0&&side2==0?"Profiles+h: cutSide=sourceWidth, H=other nominal dimension":"NOT_APPLICABLE"},{"sourceMacroSHA256",MacroHash}};
 }
 public static void Test(){
  if(Swap(40,20,40)!=20||Swap(20,20,40)!=40||Swap(25,25,25)!=25||Combine(0,0,20,40)!=20||Combine(0,40,20,40)!=20||Combine(20,0,20,40)!=40)throw new Exception("Profiles+h H regression");
  bool fail=false;try{Combine(20,40,20,40);}catch{fail=true;}if(!fail)throw new Exception("Compound H accepted");
  fail=false;try{Swap(30,20,40);}catch{fail=true;}if(!fail)throw new Exception("Unknown section accepted");
  Console.WriteLine("Profiles+h H rules: 8/8 PASS; no COM");
 }
}
