using System;
using System.Globalization;
class StageProgressBar : System.Windows.Forms.Control {
 int value;
 public int Value {get{return value;}set{int next=Math.Max(0,Math.Min(100,value));if(this.value!=next){this.value=next;Invalidate();}}}
 public StageProgressBar(){SetStyle(System.Windows.Forms.ControlStyles.UserPaint|System.Windows.Forms.ControlStyles.AllPaintingInWmPaint|System.Windows.Forms.ControlStyles.OptimizedDoubleBuffer,true);}
 protected override void OnPaint(System.Windows.Forms.PaintEventArgs e){
  e.Graphics.Clear(System.Drawing.Color.Gainsboro);
  using(var fill=new System.Drawing.SolidBrush(System.Drawing.Color.SteelBlue))e.Graphics.FillRectangle(fill,0,0,Width*value/100,Height);
  e.Graphics.DrawRectangle(System.Drawing.Pens.Gray,0,0,Width-1,Height-1);
  using(var format=new System.Drawing.StringFormat{Alignment=System.Drawing.StringAlignment.Center,LineAlignment=System.Drawing.StringAlignment.Center})e.Graphics.DrawString(value+"%",Font,System.Drawing.Brushes.Black,new System.Drawing.RectangleF(0,0,Width,Height),format);
 }
}
class RunProgress {
 public static readonly string[] Names={"Подключение и проверка исходника","Рабочая копия","Групповой native DXF","Model Space и проверка файлов","Закрытие рабочей копии","CAM_ARCS: дуги и топология","Контроль оригинала и отчёт","Раскладка рабочих DXF"};
 public bool[] Done=new bool[8]; public double[] Seconds=new double[8]; public int Current=-1; public string Context="",Detail="";
 public int Completed {get{int n=0;foreach(bool b in Done)if(b)n++;return n;}}
 public int BatchTotal=0,BatchCurrent=0,BatchDone=0,CamTotal=0,CamCurrent=0,CamDone=0; public double BatchSeconds=0;public string BodyNames=""; public bool NativePending=false; public int NativeGeneration=0;
 public int Percent {get{double fraction=Current==2&&BatchTotal>0&&!Done[2]?(double)BatchDone/BatchTotal:Current==5&&CamTotal>0&&!Done[5]?(double)CamDone/CamTotal:0;return (int)Math.Floor((Completed+fraction)*100/8);}}
 public bool Apply(string line){
  if(line.StartsWith("NATIVE_ENTER ")){NativePending=true;NativeGeneration++;return true;}
  if(line.StartsWith("NATIVE_RETURN ")){NativePending=false;return true;}
  if(line.StartsWith("@@BATCH|")||line.StartsWith("@@CAM|")){
   var a=line.Split('|');int n,total;if(a.Length<5||!int.TryParse(a[1],out n)||!int.TryParse(a[2],out total)||n<1||total<n)return false;
   if(a[0]=="@@BATCH"){
    if(a[3]=="BEGIN"){if(n!=BatchDone+1||BatchTotal!=0&&BatchTotal!=total)return false;BatchTotal=total;BatchCurrent=n;BodyNames=a[4];Detail="Экспорт группы "+n+" из "+total+" — "+BodyNames;return true;}
    if(a[3]=="DONE"&&n==BatchCurrent&&n==BatchDone+1&&total==BatchTotal){BatchDone=n;double seconds;if(double.TryParse(a[4],NumberStyles.Float,CultureInfo.InvariantCulture,out seconds)&&seconds>0&&!Double.IsNaN(seconds)&&!Double.IsInfinity(seconds))BatchSeconds+=seconds;NativePending=false;return true;}
   }else if(a[3]=="DONE"&&n==CamDone+1&&(CamTotal==0||CamTotal==total)){CamTotal=total;CamDone=n;return true;}
   return false;
  }
  var cam=System.Text.RegularExpressions.Regex.Match(line,@"^\[(\d+)/(\d+)\] CAM_ARCS (.+)$");
  if(cam.Success){int n,total;if(!int.TryParse(cam.Groups[1].Value,out n)||!int.TryParse(cam.Groups[2].Value,out total)||n<1||total<n)return false;CamCurrent=n;CamTotal=total;Detail="CAM_ARCS: деталь "+n+" из "+total+" — "+cam.Groups[3].Value;return true;}
  if(line.StartsWith("@@CONTEXT|")){Context=line.Substring(10);return true;}
  if(line.StartsWith("@@STAGE|")){
   var a=line.Split('|');int i;double seconds;
   if(a.Length!=4||!int.TryParse(a[1],out i)||i<0||i>=8||!double.TryParse(a[3],NumberStyles.Float,CultureInfo.InvariantCulture,out seconds)||Double.IsNaN(seconds)||Double.IsInfinity(seconds)||seconds<0)return false;
   if(a[2]=="BEGIN"){if(Done[i])return false;Current=i;Detail="";return true;}
   if(a[2]=="DONE"){if(Current!=i||Done[i])return false;Done[i]=true;Seconds[i]=seconds;return true;}return false;
  }
  if(line.StartsWith("BEGIN ")){if(Current!=2||BatchCurrent==0)Detail=line.Substring(6);return true;}
  if(line.StartsWith("[")&&line.Contains("] CAM_ARCS ")){Detail=line;return true;}
  return false;
 }
 public string Checklist(){var lines=new string[8];for(int i=0;i<8;i++)lines[i]=(Done[i]?"✓ ":Current==i?"▶ ":"○ ")+Names[i]+(Done[i]&&Seconds[i]>=0&&!Double.IsNaN(Seconds[i])&&!Double.IsInfinity(Seconds[i])?" — "+Seconds[i].ToString("F1",CultureInfo.InvariantCulture)+" s":"");return String.Join("\r\n",lines);}
 public static void Test(){
  var p=new RunProgress();if(p.Percent!=0)throw new Exception("initial progress");
  if(p.Apply("@@STAGE|9|DONE|0")||p.Apply("@@STAGE|0|DONE|1"))throw new Exception("invalid event accepted");
  for(int i=0;i<7;i++){p.Apply("@@STAGE|"+i+"|BEGIN|0");p.Apply("@@STAGE|"+i+"|DONE|2");}
  if(p.Percent>=100||p.Completed!=7)throw new Exception("premature completion");
  if(p.Apply("@@STAGE|6|DONE|2"))throw new Exception("duplicate completion");
  p.Apply("@@STAGE|7|BEGIN|0");p.Apply("@@STAGE|7|DONE|0.2");if(p.Percent!=100)throw new Exception("publication not counted");
  var batch=new RunProgress();batch.Apply("@@STAGE|0|BEGIN|0");batch.Apply("@@STAGE|0|DONE|1");batch.Apply("@@STAGE|1|BEGIN|0");batch.Apply("@@STAGE|1|DONE|1");batch.Apply("@@STAGE|2|BEGIN|0");
  batch.Apply("@@BATCH|1|2|BEGIN|A, B");if(batch.Percent!=25)throw new Exception("Batch begin falsely advances bar");batch.Apply("NATIVE_ENTER test");if(!batch.NativePending)throw new Exception("No pending-call indicator");batch.Apply("NATIVE_RETURN test");if(batch.NativePending)throw new Exception("Stale pending-call indicator");batch.Apply("@@BATCH|1|2|DONE|");if(batch.Percent!=31)throw new Exception("Batch completion not counted");if(batch.Apply("@@BATCH|1|2|DONE|"))throw new Exception("Duplicate batch accepted");
  batch.Apply("@@BATCH|2|2|BEGIN|C");batch.Apply("@@BATCH|2|2|DONE|");if(batch.Percent!=37)throw new Exception("Native subprogress incorrect");
  batch.Apply("[1/3] CAM_ARCS A.dxf");batch.Apply("@@CAM|1|3|DONE|A.dxf");if(batch.CamDone!=1||batch.CamTotal!=3)throw new Exception("CAM count incorrect");
  p.Apply("@@CONTEXT|abc|config");if(p.Context!="abc|config")throw new Exception("context lost");
 }
}
