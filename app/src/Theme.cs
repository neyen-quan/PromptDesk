using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
namespace PromptDesk {
public static class Glass {
 public static void Backdrop(Graphics g,Rectangle bounds){if(bounds.Width<=0||bounds.Height<=0)return;using(var fill=new SolidBrush(UI.Bg))g.FillRectangle(fill,bounds);}
 public static GraphicsPath Round(RectangleF r,float radius){var p=new GraphicsPath();float d=Math.Min(radius*2,Math.Min(r.Width,r.Height));p.AddArc(r.X,r.Y,d,d,180,90);p.AddArc(r.Right-d,r.Y,d,d,270,90);p.AddArc(r.Right-d,r.Bottom-d,d,d,0,90);p.AddArc(r.X,r.Bottom-d,d,d,90,90);p.CloseFigure();return p;}
 public static void Surface(Graphics g,Rectangle bounds){if(bounds.Width<12||bounds.Height<12)return;g.SmoothingMode=SmoothingMode.AntiAlias;var r=new RectangleF(1,1,bounds.Width-3,bounds.Height-3);using(var p=Round(r,12))using(var fill=new SolidBrush(UI.Panel))using(var pen=new Pen(UI.Line)){g.FillPath(fill,p);g.DrawPath(pen,p);}}
}
public class GlassPanel:Panel {public GlassPanel(){DoubleBuffered=true;BackColor=Color.Transparent;}protected override void OnPaintBackground(PaintEventArgs e){base.OnPaintBackground(e);Glass.Surface(e.Graphics,ClientRectangle);}}
public class GlassFlow:FlowLayoutPanel {public GlassFlow(){DoubleBuffered=true;BackColor=Color.Transparent;}protected override void OnPaintBackground(PaintEventArgs e){base.OnPaintBackground(e);Glass.Surface(e.Graphics,ClientRectangle);}}
internal static class VariableDialogColors
{
 public static readonly Color Background = Color.FromArgb(252, 246, 233);
 public static readonly Color Surface = Color.FromArgb(242, 230, 210);
 public static readonly Color Field = Color.FromArgb(255, 252, 245);
 public static readonly Color Ink = Color.FromArgb(61, 40, 28);
 public static readonly Color Accent = Color.FromArgb(151, 59, 24);
 public static readonly Color Selected = Color.FromArgb(248, 216, 191);
 public static readonly Color Border = Color.FromArgb(190, 151, 120);
}
public class GlassButton:Control,IButtonControl {
 bool hover,pressed;internal bool Warm;public bool Primary,Quiet,Active;public string IconName="";public ContentAlignment TextAlign=ContentAlignment.MiddleCenter;public DialogResult DialogResult{get;set;}
 public GlassButton(){SetStyle(ControlStyles.UserPaint|ControlStyles.AllPaintingInWmPaint|ControlStyles.OptimizedDoubleBuffer|ControlStyles.SupportsTransparentBackColor|ControlStyles.Selectable|ControlStyles.StandardClick,true);Cursor=Cursors.Hand;BackColor=Color.Transparent;ForeColor=UI.Ink;Font=new Font("Segoe UI",9.5f);Padding=new Padding(12,5,12,5);Margin=new Padding(4);AutoSize=true;Height=38;TabStop=true;AccessibleRole=AccessibleRole.PushButton;}
 public override Size GetPreferredSize(Size proposedSize){var size=TextRenderer.MeasureText(Text,Font);return new Size(Math.Max(74,size.Width+Padding.Horizontal+8+(IconName.Length>0?23:0)),Math.Max(38,size.Height+Padding.Vertical+6));}
 public void NotifyDefault(bool value){Invalidate();}
 public void PerformClick(){if(Enabled)OnClick(EventArgs.Empty);}
 protected override void OnClick(EventArgs e){base.OnClick(e);if(DialogResult!=DialogResult.None){var f=FindForm();if(f!=null)f.DialogResult=DialogResult;}}
 protected override void OnMouseEnter(EventArgs e){hover=true;Invalidate();base.OnMouseEnter(e);}protected override void OnMouseLeave(EventArgs e){hover=false;pressed=false;Invalidate();base.OnMouseLeave(e);}
 protected override void OnMouseDown(MouseEventArgs e){if(e.Button==MouseButtons.Left){pressed=true;Focus();Invalidate();}base.OnMouseDown(e);}protected override void OnMouseUp(MouseEventArgs e){pressed=false;Invalidate();base.OnMouseUp(e);}
 protected override void OnGotFocus(EventArgs e){Invalidate();base.OnGotFocus(e);}protected override void OnLostFocus(EventArgs e){Invalidate();base.OnLostFocus(e);}
 protected override void OnKeyDown(KeyEventArgs e){if(e.KeyCode==Keys.Space||e.KeyCode==Keys.Enter){e.Handled=true;PerformClick();}else base.OnKeyDown(e);}
 protected override bool ProcessMnemonic(char key){if(IsMnemonic(key,Text)&&CanSelect){Focus();PerformClick();return true;}return base.ProcessMnemonic(key);}
 protected override void OnPaint(PaintEventArgs e){
  e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;
  var r=new RectangleF(2,2,Width-5,Height-5);if(r.Width<=0||r.Height<=0)return;
  Color fill=Primary?(pressed?Color.FromArgb(72,174,200):hover?Color.FromArgb(147,229,246):UI.Accent):
      Active?UI.Selected:pressed?Color.FromArgb(35,56,75):hover?Color.FromArgb(30,44,59):Quiet?Color.Transparent:Color.FromArgb(27,40,54);
  if(!Enabled&&Primary)fill=Color.FromArgb(37,70,83);
  if (Warm)
   fill = Primary ? (Enabled ? (pressed ? Color.FromArgb(113, 43, 17) : hover ? Color.FromArgb(136, 51, 20) : VariableDialogColors.Accent) : VariableDialogColors.Border)
    : Active ? VariableDialogColors.Selected : pressed || hover ? VariableDialogColors.Surface : VariableDialogColors.Field;
  Color border = Focused ? UI.Accent : Active ? Color.FromArgb(53,103,122) : UI.Line;
  if (Warm) border = Focused || Active ? VariableDialogColors.Accent : VariableDialogColors.Border;
  using(var path=Glass.Round(r,8)){
   if(fill!=Color.Transparent)using(var brush=new SolidBrush(fill))e.Graphics.FillPath(brush,path);
   if(!Quiet||Active||Focused)using(var pen=new Pen(border))e.Graphics.DrawPath(pen,path);
  }
  Color ink=!Enabled?Color.FromArgb(103,127,145):Primary?Color.FromArgb(10,37,49):Active?UI.Accent:UI.Ink;
  if (Warm) ink = Primary ? Color.White : Active ? VariableDialogColors.Accent : VariableDialogColors.Ink;
  int iconWidth=IconName.Length>0?23:0;
  var textBounds=new Rectangle(10+iconWidth,2,Math.Max(1,Width-20-iconWidth),Height-4);
  if(IconName.Length>0){int x=Text.Length==0?(Width-17)/2:12;Glyph.Draw(e.Graphics,IconName,new Rectangle(x,(Height-17)/2,17,17),ink);}
  var align=TextAlign==ContentAlignment.MiddleLeft?TextFormatFlags.Left:TextFormatFlags.HorizontalCenter;
  TextRenderer.DrawText(e.Graphics,Text,Font,textBounds,ink,align|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis|TextFormatFlags.NoPrefix);
 }

}
public class DarkComboBox:ComboBox {
 public DarkComboBox(){FlatStyle=FlatStyle.Flat;DrawMode=DrawMode.OwnerDrawFixed;ItemHeight=24;BackColor=UI.Input;ForeColor=UI.Ink;}
 protected override void WndProc(ref Message m){base.WndProc(ref m);if(m.Msg==15&&IsHandleCreated){using(var g=Graphics.FromHwnd(Handle)){var r=new Rectangle(Width-23,1,22,Height-2);using(var b=new SolidBrush(UI.Input))g.FillRectangle(b,r);using(var p=new Pen(UI.Line))g.DrawRectangle(p,0,0,Width-1,Height-1);using(var p=new Pen(UI.Muted,1.4f)){int x=Width-12,y=Height/2;g.DrawLines(p,new[]{new Point(x-4,y-2),new Point(x,y+2),new Point(x+4,y-2)});}}}}
 protected override void OnDrawItem(DrawItemEventArgs e){bool selected=(e.State&DrawItemState.Selected)!=0;using(var b=new SolidBrush(selected?UI.Selected:UI.Input))e.Graphics.FillRectangle(b,e.Bounds);string text=e.Index<0?Text:GetItemText(Items[e.Index]);TextRenderer.DrawText(e.Graphics,text,Font,new Rectangle(e.Bounds.X+8,e.Bounds.Y,e.Bounds.Width-10,e.Bounds.Height),selected?UI.Accent:UI.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);}
}
public class DarkForm:Form {
 protected override void OnHandleCreated(EventArgs e){base.OnHandleCreated(e);Branding.DarkCaption(Handle);}
 protected override void OnShown(EventArgs e){UI.ThemeTree(this);base.OnShown(e);}
}
public class DarkMenuColors:ProfessionalColorTable {
 public override Color ToolStripDropDownBackground{get{return UI.Panel;}}public override Color ImageMarginGradientBegin{get{return UI.Panel;}}public override Color ImageMarginGradientMiddle{get{return UI.Panel;}}public override Color ImageMarginGradientEnd{get{return UI.Panel;}}public override Color MenuItemSelected{get{return UI.Selected;}}public override Color MenuItemBorder{get{return UI.Line;}}public override Color MenuBorder{get{return UI.Line;}}
}
}
