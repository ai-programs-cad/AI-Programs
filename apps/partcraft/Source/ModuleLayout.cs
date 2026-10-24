using System;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
static class ModuleLayout {
 public static void Configure(Form form){
  var journal=form.Controls.OfType<TextBox>().Single();
  var open=form.Controls.OfType<Button>().Single(b=>b.Text=="Открыть результат");
  var start=form.Controls.OfType<Button>().Single(b=>b!=open);
  var stage=form.Controls.OfType<Label>().Single(l=>l.Top==115);
  var bar=form.Controls.OfType<ProgressBar>().Single();
  form.SuspendLayout();
  foreach(Control c in form.Controls.Cast<Control>().ToArray())if(c!=journal&&c!=open&&c!=start&&c!=stage&&c!=bar)c.Visible=false;
  form.AutoScroll=true;form.AutoScrollMargin=new Size(0,18);
  int width=Math.Max(100,form.ClientSize.Width-30);start.SetBounds(15,15,260,35);stage.SetBounds(15,60,width,45);bar.SetBounds(15,110,width,22);
  journal.SetBounds(15,145,width,180);open.SetBounds(15,340,180,34);
  journal.Anchor=AnchorStyles.Top|AnchorStyles.Left|AnchorStyles.Right;
  stage.Anchor=journal.Anchor;bar.Anchor=journal.Anchor;
  form.AutoScrollMinSize=new Size(0,open.Bottom+18);
  form.ResumeLayout(true);
 }
}
