using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading;
using System.Text.RegularExpressions;

namespace PromptDesk {
public static class Program {
 [STAThread] public static void Main(string[] args) {
  if(args.Contains("--test")){try{Tests.Run();Environment.ExitCode=0;}catch(Exception e){File.WriteAllText("pruebas-error.txt",e.ToString());Environment.ExitCode=1;}return;}
  if(args.Contains("--ui-test")){try{Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);UiTests.Run(args.Length>1?args[1]:Path.Combine(Path.GetTempPath(),"PromptDesk-visual"));Environment.ExitCode=0;}catch(Exception e){File.WriteAllText("pruebas-ui-error.txt",e.ToString());Environment.ExitCode=1;}return;}
  Branding.Initialize();
  if(args.Contains("--install")){Application.EnableVisualStyles();Installation.Run();return;}
  bool created;using(var mutex=new Mutex(true,"Local\\PromptDesk_Neyen_v1",out created)) {
   if(!created){MessageBox.Show("PromptDesk ya está abierto. Buscá el icono en la bandeja junto al reloj.","PromptDesk");return;}
   Application.EnableVisualStyles();Application.SetCompatibleTextRenderingDefault(false);
   Application.ThreadException+=(s,e)=>MessageBox.Show(e.Exception.Message,"PromptDesk · Error",MessageBoxButtons.OK,MessageBoxIcon.Error);
   try{Application.Run(new MainWindow());}catch(Exception e){MessageBox.Show(e.Message+"\n\nTus datos no se han reiniciado. Revisá los respaldos en %LOCALAPPDATA%\\PromptDesk\\Historial.","No se pudo abrir PromptDesk",MessageBoxButtons.OK,MessageBoxIcon.Error);}
  }
 }
}
public static class UI {
 public static Color Bg=Color.FromArgb(13,19,28),Panel=Color.FromArgb(21,30,42),Input=Color.FromArgb(16,24,35),Ink=Color.FromArgb(232,240,248),Muted=Color.FromArgb(153,173,193),Accent=Color.FromArgb(110,216,240),Line=Color.FromArgb(39,55,72),Selected=Color.FromArgb(26,60,76);
 public static void Style(Control c){c.BackColor=Input;c.ForeColor=Ink;c.Font=new Font("Segoe UI",10);var text=c as TextBoxBase;if(text!=null)text.BorderStyle=BorderStyle.FixedSingle;}
 public static GlassButton Button(string title,Action action){var b=new GlassButton{Text=title,Primary=title=="Copiar prompt"||title=="Guardar"};b.Click+=(sender,args)=>{try{action();}catch(Exception ex){MessageBox.Show(ex.Message,"PromptDesk",MessageBoxButtons.OK,MessageBoxIcon.Error);}};return b;}
 public static Form Dialog(string title,int width,int height){var f=new DarkForm{Icon=Branding.Icon,Text=title,Width=Math.Min(width,Screen.PrimaryScreen.WorkingArea.Width-40),Height=Math.Min(height,Screen.PrimaryScreen.WorkingArea.Height-50),StartPosition=FormStartPosition.CenterParent,MinimizeBox=false,MaximizeBox=false,BackColor=Bg,ForeColor=Ink,Font=new Font("Segoe UI",10),MinimumSize=new Size(400,250)};return f;}
 public static void GridTheme(DataGridView g){g.BackgroundColor=Input;g.ForeColor=Ink;g.BorderStyle=BorderStyle.None;g.EnableHeadersVisualStyles=false;g.ColumnHeadersBorderStyle=DataGridViewHeaderBorderStyle.Single;g.CellBorderStyle=DataGridViewCellBorderStyle.SingleHorizontal;g.ColumnHeadersHeightSizeMode=DataGridViewColumnHeadersHeightSizeMode.DisableResizing;g.ColumnHeadersHeight=42;g.ColumnHeadersDefaultCellStyle=new DataGridViewCellStyle{BackColor=Panel,ForeColor=Muted,Font=new Font("Segoe UI",9.5f,FontStyle.Bold),SelectionBackColor=Panel,SelectionForeColor=Ink,Padding=new Padding(7)};g.DefaultCellStyle=new DataGridViewCellStyle{Font=new Font("Segoe UI",10),BackColor=Input,ForeColor=Ink,SelectionBackColor=Selected,SelectionForeColor=Color.White,Padding=new Padding(7)};g.AlternatingRowsDefaultCellStyle.BackColor=Color.FromArgb(15,26,39);g.RowHeadersDefaultCellStyle.BackColor=Panel;g.RowHeadersDefaultCellStyle.ForeColor=Muted;g.GridColor=Line;g.RowTemplate.Height=46;}
 public static void ThemeTree(Control c){if(c.IsHandleCreated)Branding.DarkControls(c.Handle);if(c is TextBoxBase||c is ListBox||c is ComboBox||c is NumericUpDown){c.BackColor=Input;c.ForeColor=Ink;}var g=c as DataGridView;if(g!=null)GridTheme(g);foreach(Control child in c.Controls)ThemeTree(child);}
 public static Label Caption(string text){return new Label{Text=text,ForeColor=Muted,Font=new Font("Segoe UI",8.5f,FontStyle.Bold),Dock=DockStyle.Top,Height=27,TextAlign=ContentAlignment.MiddleLeft};}
 public static string Ask(IWin32Window owner,string title,string initial){using(var f=Dialog(title,540,190)){var t=new TextBox{Text=initial,Dock=DockStyle.Top,Margin=new Padding(12)};Style(t);var p=new Panel{Dock=DockStyle.Fill,Padding=new Padding(15)};p.Controls.Add(t);var buttons=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=50,FlowDirection=FlowDirection.RightToLeft};var ok=Button("Aceptar",()=>f.DialogResult=DialogResult.OK);buttons.Controls.Add(ok);buttons.Controls.Add(Button("Cancelar",()=>f.DialogResult=DialogResult.Cancel));f.Controls.Add(p);f.Controls.Add(buttons);f.AcceptButton=ok;return f.ShowDialog(owner)==DialogResult.OK?t.Text:null;}}
 public static bool Confirm(IWin32Window owner,string text){return MessageBox.Show(owner,text,"PromptDesk",MessageBoxButtons.YesNo,MessageBoxIcon.Question)==DialogResult.Yes;}
}
public partial class MainWindow:DarkForm {
 Store db; DataGridView grid; ListBox categories; ComboBox collection,model,view; TextBox search,editor; Label details,status,resultCount; System.Windows.Forms.Timer saveTimer; NotifyIcon tray;
 Entry selected; bool busy,dirty; string sortKey="name";bool sortDesc; bool orderPending; int gridGeneration; const string NewChoice="＋ Nuevo…"; UndoHistory history; Dictionary<int,Entry> hotkeys=new Dictionary<int,Entry>();
 [DllImport("user32.dll",SetLastError=true)] static extern bool RegisterHotKey(IntPtr h,int id,uint modifiers,uint key);
 [DllImport("user32.dll")] static extern bool UnregisterHotKey(IntPtr h,int id);
 public MainWindow() : this(null, false) {}
 internal MainWindow(Store suppliedStore, bool testMode) {
  testing = testMode;
  Text="PromptDesk · v"+Branding.Version;MinimumSize=new Size(1050,650);var area=Screen.PrimaryScreen.WorkingArea;Width=Math.Min(1440,Math.Max(1050,area.Width-60));Height=Math.Min(900,Math.Max(650,area.Height-50));StartPosition=FormStartPosition.CenterScreen;BackColor=UI.Bg;ForeColor=UI.Ink;Font=new Font("Segoe UI",10);Icon=Branding.Icon;
  db=suppliedStore ?? new Store(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PromptDesk"));
  if(suppliedStore == null) {
  if(File.Exists(db.Pathname)) {
   try{db.Load(db.Pathname);}catch(Exception loadError){
    MessageBox.Show("No se pudo leer el catálogo. Elegí un respaldo para recuperar; cancelar no modifica tus datos.\n\n"+loadError.Message,"Recuperación de PromptDesk");
    using(var recovery=new OpenFileDialog{Filter="Respaldo PromptDesk (*.xml)|*.xml",InitialDirectory=Path.Combine(db.Folder,"Historial")}){
     if(recovery.ShowDialog()!=DialogResult.OK)throw new Exception("Recuperación cancelada. Se conservaron los archivos existentes.");
     db.Load(recovery.FileName);db.Save();
    }
   }
  }else{db.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"seed.xml"));db.Save();}
  }
  history=new UndoHistory(db);KeyPreview=true;saveTimer=new System.Windows.Forms.Timer{Interval=700};saveTimer.Tick+=(s,e)=>{saveTimer.Stop();Save();};Build();InitializeCloud();
  var menu=new ContextMenuStrip(uiComponents){BackColor=UI.Panel,ForeColor=UI.Ink,Renderer=new ToolStripProfessionalRenderer(new DarkMenuColors())};menu.Items.Add("Abrir PromptDesk",null,(s,e)=>OpenWindow());menu.Items.Add("Salir",null,(s,e)=>{Close();});
  if(!testing){tray=new NotifyIcon{Icon=Branding.Icon,Text="PromptDesk · atajos activos",Visible=true,ContextMenuStrip=menu};tray.DoubleClick+=(s,e)=>OpenWindow();}
  FormClosing+=(sender,args)=>{if(!Save()){args.Cancel=true;return;}if(!PermitCloudClose()){args.Cancel=true;return;}DisposeCloud();saveTimer.Stop();foreach(int id in hotkeys.Keys)UnregisterHotKey(Handle,id);hotkeys.Clear();if(tray!=null){tray.Visible=false;tray.Dispose();}saveTimer.Dispose();searchTimer.Stop();searchTimer.Dispose();uiComponents.Dispose();if(grid.Parent==null)grid.Dispose();if(library.Parent==null)library.Dispose();};
  Shown+=(s,e)=>{RefreshAll();if(!testing)RegisterKeys();};
 }
 void OpenWindow(){Show();WindowState=FormWindowState.Normal;Activate();}
 FlowLayoutPanel Bar(int height){return new GlassFlow{Dock=DockStyle.Top,Height=Math.Max(height,50),Padding=new Padding(5),BackColor=Color.Transparent,WrapContents=false,AutoScroll=true};}
 ComboBox Combo(int width){var c=new DarkComboBox{Width=width,DropDownStyle=ComboBoxStyle.DropDownList,Margin=new Padding(5,5,5,5)};UI.Style(c);return c;}
 void WireGridEvents(){
  grid.CellPainting+=(s,e)=>{if(e.RowIndex<0||e.ColumnIndex<0)return;string key=grid.Columns[e.ColumnIndex].Name;bool choice=key=="category"||key=="model"||key=="collection";if(key!="copy"&&key!="favorite"&&!choice)return;Color bg=grid.Rows[e.RowIndex].Selected?UI.Selected:e.RowIndex%2==0?UI.Input:Color.FromArgb(15,26,39);using(var brush=new SolidBrush(bg))e.Graphics.FillRectangle(brush,e.CellBounds);using(var pen=new Pen(UI.Line))e.Graphics.DrawLine(pen,e.CellBounds.Left,e.CellBounds.Bottom-1,e.CellBounds.Right,e.CellBounds.Bottom-1);if(key=="copy"){var r=new Rectangle(e.CellBounds.X+6,e.CellBounds.Y+7,e.CellBounds.Width-12,e.CellBounds.Height-14);using(var path=Glass.Round(r,6))using(var fill=new SolidBrush(UI.Selected))using(var pen=new Pen(Color.FromArgb(43,103,132))){e.Graphics.SmoothingMode=SmoothingMode.AntiAlias;e.Graphics.FillPath(fill,path);e.Graphics.DrawPath(pen,path);}TextRenderer.DrawText(e.Graphics,"Copiar",grid.Font,r,UI.Accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);}else if(choice){var r=new Rectangle(e.CellBounds.X+8,e.CellBounds.Y,e.CellBounds.Width-31,e.CellBounds.Height);TextRenderer.DrawText(e.Graphics,Convert.ToString(e.FormattedValue),grid.Font,r,UI.Ink,TextFormatFlags.Left|TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);int x=e.CellBounds.Right-16,y=e.CellBounds.Top+e.CellBounds.Height/2;using(var pen=new Pen(UI.Muted,1.3f)){e.Graphics.DrawLines(pen,new[]{new Point(x-4,y-2),new Point(x,y+2),new Point(x+4,y-2)});}}else TextRenderer.DrawText(e.Graphics,Convert.ToString(e.FormattedValue),grid.Font,e.CellBounds,UI.Accent,TextFormatFlags.HorizontalCenter|TextFormatFlags.VerticalCenter);e.Handled=true;};
  grid.SelectionChanged+=(s,e)=>{if(!busy)SelectEntry();};grid.CellContentClick+=(s,e)=>{if(e.RowIndex<0)return;var item=(Entry)grid.Rows[e.RowIndex].Tag;string key=grid.Columns[e.ColumnIndex].Name;if(key=="copy")Copy(item);if(key=="favorite"){item.Favorite=!item.Favorite;Changed();RefreshRows();}};
  grid.CellDoubleClick+=(s,e)=>{if(e.RowIndex<0)return;string key=grid.Columns[e.ColumnIndex].Name;if(key=="prompt"){editor.Focus();return;}if(key=="hotkey"){Shortcut((Entry)grid.Rows[e.RowIndex].Tag);return;}if(db.Fields.Any(f=>f.Id==key)){grid.BeginEdit(true);var combo=grid.EditingControl as ComboBox;if(combo!=null)combo.DroppedDown=true;}};
  grid.CellValidating+=(s,e)=>{if(e.RowIndex>=0&&grid.Columns[e.ColumnIndex].Name=="collection"){string value=Convert.ToString(e.FormattedValue);if(value!="404"&&value!="Personales"){e.Cancel=true;MessageBox.Show("Colección: escribí 404 o Personales.");}}};
  grid.CellEndEdit+=(s,e)=>{if(busy||e.RowIndex<0)return;var item=(Entry)grid.Rows[e.RowIndex].Tag;string key=grid.Columns[e.ColumnIndex].Name;item.Set(key,Convert.ToString(grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Value));Changed();BeginInvoke((Action)(()=>{RefreshAll();}));};
  grid.ColumnHeaderMouseClick+=(s,e)=>{string key=grid.Columns[e.ColumnIndex].Name;sortDesc=sortKey==key&&!sortDesc;sortKey=key;RefreshRows();};
  grid.ColumnDisplayIndexChanged+=(s,e)=>{if(busy||orderPending)return;orderPending=true;int generation=gridGeneration;BeginInvoke((Action)(()=>{if(generation!=gridGeneration)return;orderPending=false;if(!busy){CaptureColumnOrder();Changed();}}));};
  grid.EditingControlShowing+=(s,e)=>{UI.Style(e.Control);Branding.DarkControls(e.Control.Handle);var combo=e.Control as ComboBox;if(combo!=null){combo.SelectionChangeCommitted-=GridChoiceCommitted;combo.SelectionChangeCommitted+=GridChoiceCommitted;}};
  grid.DataError+=(s,e)=>{e.ThrowException=false;e.Cancel=true;status.Text="Elegí una opción del desplegable.";};
  editor.KeyDown+=UndoKeyDown;grid.KeyDown+=UndoKeyDown;
  grid.EditingControlShowing+=(sender,args)=>{args.Control.KeyDown-=UndoKeyDown;args.Control.KeyDown+=UndoKeyDown;};
  categories.DrawMode=DrawMode.OwnerDrawFixed;categories.ItemHeight=42;categories.DrawItem+=(sender,args)=>{if(args.Index<0)return;bool active=(args.State&DrawItemState.Selected)!=0;using(var brush=new SolidBrush(UI.Input))args.Graphics.FillRectangle(brush,args.Bounds);if(active){var r=new Rectangle(args.Bounds.X+2,args.Bounds.Y+3,args.Bounds.Width-4,args.Bounds.Height-6);using(var path=Glass.Round(r,7))using(var brush=new SolidBrush(UI.Selected))args.Graphics.FillPath(brush,path);using(var brush=new SolidBrush(UI.Accent))args.Graphics.FillRectangle(brush,args.Bounds.X+2,args.Bounds.Y+12,3,args.Bounds.Height-24);}TextRenderer.DrawText(args.Graphics,categories.Items[args.Index].ToString(),categories.Font,new Rectangle(args.Bounds.X+14,args.Bounds.Y,args.Bounds.Width-20,args.Bounds.Height),active?UI.Accent:UI.Ink,TextFormatFlags.VerticalCenter|TextFormatFlags.EndEllipsis);};
  editor.TextChanged+=(s,e)=>{if(!busy&&selected!=null){selected.Set("prompt",editor.Text);Changed("prompt:"+selected.Id);UpdateEditorSummary();library.Invalidate();}};
 }
 protected override void OnPaintBackground(PaintEventArgs e){Glass.Backdrop(e.Graphics,ClientRectangle);}

 protected override bool ProcessCmdKey(ref Message msg,Keys keyData){
  if(keyData==(Keys.Control|Keys.F)){search.Focus();search.SelectAll();return true;}
  if(keyData==(Keys.Control|Keys.N)){RunAction(AddEntry);return true;}
  if(keyData==(Keys.Control|Keys.Shift|Keys.C)){RunAction(()=>Copy(selected));return true;}
  if(keyData==(Keys.Control|Keys.S)){Save();return true;}
  if(keyData==(Keys.Control|Keys.D)){RunAction(Duplicate);return true;}
  if(search.Focused&&(keyData==(Keys.Control|Keys.Z)||keyData==(Keys.Control|Keys.Y)))return base.ProcessCmdKey(ref msg,keyData);
  if(keyData==(Keys.Control|Keys.Z)){UndoChange(false);return true;}
  if(keyData==(Keys.Control|Keys.Y)){UndoChange(true);return true;}
  if(keyData==Keys.Escape && search.Focused){search.Clear();return true;}
  return base.ProcessCmdKey(ref msg,keyData);
 }
 void UndoKeyDown(object sender,KeyEventArgs args){if(args.Control&&(args.KeyCode==Keys.Z||args.KeyCode==Keys.Y)){args.SuppressKeyPress=true;args.Handled=true;UndoChange(args.KeyCode==Keys.Y);}}
 void UndoChange(bool redo){grid.EndEdit();if(orderPending){CaptureColumnOrder();Changed();orderPending=false;}saveTimer.Stop();if(!(redo?history.Redo(db):history.Undo(db))){status.Text="No hay cambios para "+(redo?"rehacer":"deshacer")+".";return;}dirty=true;RefreshAll();RegisterKeys();Save();status.Text=(redo?"Cambio rehecho":"Cambio deshecho")+" · Ctrl+Z / Ctrl+Y";}
 string ChoiceId(string key,string scope,string value){return key=="model"?"model::"+value:"category::"+scope+"::"+value;}
 bool ChoiceEnabled(string key,string scope,string value){return !db.DisabledChoices.Contains(ChoiceId(key,scope,value));}
 List<string> CatalogValues(string key,string scope=null){
 if(key=="model")return db.Models.Concat(db.Entries.Select(item=>item.Get("model"))).Where(v=>v.Length>0).Distinct().OrderBy(v=>v).ToList();
 if(scope==null||scope=="Todos")return CatalogValues(key,"Personales").Concat(CatalogValues(key,"404")).Distinct().ToList();
 var values=db.Categories.Where(c=>!c.Contains("::")||c.StartsWith(scope+"::")).Select(c=>c.Contains("::")?c.Substring(c.IndexOf("::")+2):c).Concat(db.Entries.Where(item=>item.Get("collection")==scope).Select(item=>item.Get("category"))).Where(v=>v.Length>0).Distinct().OrderBy(v=>v).ToList();
 var order=db.CategoryOrder.Where(c=>c.StartsWith(scope+"::")).Select(c=>c.Substring(scope.Length+2)).Where(values.Contains);
 return order.Concat(values).Distinct().ToList();}
 void SetChoiceEnabled(string key,string scope,string value,bool enabled){string id=ChoiceId(key,scope,value);db.DisabledChoices.RemoveAll(c=>c==id);if(!enabled)db.DisabledChoices.Add(id);Changed();RefreshAll();}
 void SetCategoryOrder(string scope,IEnumerable<string> values){db.CategoryOrder.RemoveAll(c=>c.StartsWith(scope+"::"));db.CategoryOrder.AddRange(values.Distinct().Select(v=>scope+"::"+v));Changed();RefreshAll();}
 void RemoveChoice(string key,string value,string scope=null){foreach(var item in db.Entries.Where(item=>item.Get(key)==value&&(key=="model"||scope==null||item.Get("collection")==scope)))item.Set(key,"");if(key=="model"){db.Models.RemoveAll(v=>v==value);db.DisabledChoices.RemoveAll(c=>c==ChoiceId(key,"",value));}else foreach(var sc in new[]{"Personales","404"}.Where(sc=>scope==null||sc==scope)){db.Categories.RemoveAll(c=>c==sc+"::"+value||c==value);db.CategoryOrder.RemoveAll(c=>c==sc+"::"+value);db.DisabledChoices.RemoveAll(c=>c==ChoiceId(key,sc,value));}Changed();Save();RefreshAll();}
 void RenameChoice(string key,string scope,string old,string value){foreach(var item in db.Entries.Where(item=>item.Get(key)==old&&(key=="model"||item.Get("collection")==scope)))item.Set(key,value);if(key=="model"){db.Models.RemoveAll(v=>v==old);if(!db.Models.Contains(value))db.Models.Add(value);}else{db.Categories=db.Categories.Select(c=>c==scope+"::"+old?scope+"::"+value:c).Distinct().ToList();db.CategoryOrder=db.CategoryOrder.Select(c=>c==scope+"::"+old?scope+"::"+value:c).Distinct().ToList();}string oldId=ChoiceId(key,scope,old),newId=ChoiceId(key,scope,value);db.DisabledChoices=db.DisabledChoices.Select(c=>c==oldId?newId:c).Distinct().ToList();Changed();RefreshAll();}
 void ManageChoices(string key){using(var f=UI.Dialog(key=="model"?"Gestionar modelos":"Gestionar categorías",650,600)){
 var top=new Panel{Dock=DockStyle.Top,Height=108,Padding=new Padding(12),BackColor=UI.Panel};var scopeBox=Combo(150);scopeBox.Items.AddRange(new object[]{"Personales","404"});scopeBox.SelectedItem=collection.Text=="404"?"404":"Personales";scopeBox.Visible=key=="category";scopeBox.Location=new Point(12,12);top.Controls.Add(scopeBox);var label=new Label{Text=key=="category"?"Marcada: disponible para asignar. Desmarcada: oculta, conserva sus prompts.\nArrastrá desde el nombre para ordenar. El orden también se usa en los desplegables.":"Marcado: disponible para asignar. Desmarcado: oculto, conserva sus prompts.",AutoSize=false,Dock=DockStyle.Bottom,Height=55};top.Controls.Add(label);
 var list=new CheckedListBox{Dock=DockStyle.Fill,BorderStyle=BorderStyle.None,CheckOnClick=true,AllowDrop=key=="category",IntegralHeight=false};UI.Style(list);bool filling=false;Action fill=()=>{filling=true;list.Items.Clear();foreach(var value in CatalogValues(key,scopeBox.Text))list.Items.Add(value,ChoiceEnabled(key,scopeBox.Text,value));filling=false;};fill();scopeBox.SelectedIndexChanged+=(sender,args)=>fill();
 list.ItemCheck+=(sender,args)=>{if(filling)return;if(key=="category"&&Control.MouseButtons==MouseButtons.Left&&list.PointToClient(Control.MousePosition).X>=26){args.NewValue=args.CurrentValue;return;}string value=list.Items[args.Index].ToString(),scope=scopeBox.Text;bool enabled=args.NewValue==CheckState.Checked;f.BeginInvoke((Action)(()=>{SetChoiceEnabled(key,scope,value,enabled);}));};
 int dragIndex=-1;Point dragStart=Point.Empty;list.MouseDown+=(sender,args)=>{dragIndex=args.X<26?-1:list.IndexFromPoint(args.Location);dragStart=args.Location;};list.MouseMove+=(sender,args)=>{if(key!="category"||dragIndex<0||args.Button!=MouseButtons.Left||Math.Abs(args.X-dragStart.X)+Math.Abs(args.Y-dragStart.Y)<8)return;int index=dragIndex;dragIndex=-1;list.DoDragDrop(index,DragDropEffects.Move);};list.DragEnter+=(sender,args)=>{args.Effect=args.Data.GetDataPresent(typeof(int))?DragDropEffects.Move:DragDropEffects.None;};list.DragOver+=(sender,args)=>{args.Effect=DragDropEffects.Move;};list.DragDrop+=(sender,args)=>{if(!args.Data.GetDataPresent(typeof(int)))return;int from=(int)args.Data.GetData(typeof(int));int to=list.IndexFromPoint(list.PointToClient(new Point(args.X,args.Y)));if(to<0)to=list.Items.Count-1;if(from<0||from>=list.Items.Count||to<0||from==to)return;var values=list.Items.Cast<object>().Select(v=>v.ToString()).ToList();string moved=values[from];values.RemoveAt(from);values.Insert(to,moved);SetCategoryOrder(scopeBox.Text,values);fill();list.SelectedItem=moved;};
 var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=115,Padding=new Padding(8),BackColor=UI.Panel};bar.Controls.Add(UI.Button("Crear",()=>{CreateChoice(f,key,scopeBox.Text);fill();RefreshAll();}));bar.Controls.Add(UI.Button("Renombrar",()=>{if(list.SelectedItem==null)return;string old=list.SelectedItem.ToString();string value=UI.Ask(f,"Nuevo nombre",old);if(String.IsNullOrWhiteSpace(value)||value.Trim()==old)return;RenameChoice(key,scopeBox.Text,old,value.Trim());fill();}));
 bar.Controls.Add(UI.Button("Eliminar",()=>{if(list.SelectedItem==null)return;string value=list.SelectedItem.ToString();int count=db.Entries.Count(item=>item.Get(key)==value&&(key=="model"||item.Get("collection")==scopeBox.Text));if(!UI.Confirm(f,"¿Eliminar «"+value+"»?\n\n"+count+" prompts quedarán con este campo vacío. Para ocultarla sin borrar asignaciones, desmarcá la casilla."))return;RemoveChoice(key,value,key=="model"?null:scopeBox.Text);fill();}));
 if(key=="category")bar.Controls.Add(UI.Button("Ordenar A–Z",()=>{SetCategoryOrder(scopeBox.Text,CatalogValues(key,scopeBox.Text).OrderBy(v=>v));fill();}));bar.Controls.Add(UI.Button("Cerrar",()=>{Save();f.Close();}));f.Controls.Add(list);f.Controls.Add(top);f.Controls.Add(bar);f.ShowDialog(this);}}

 void Changed(string group=""){if(history!=null){if(group==null)history.UpdateBaseline(db);else history.Record(db,group);}dirty=true;CloudLocalChanged();UpdateEditorSummary();status.Text="Guardando cambios…";saveTimer.Stop();saveTimer.Start();}
 bool Save(){try{grid.EndEdit();if(!dirty)return true;db.Save();dirty=false;CloudLocalSaved();UpdateEditorSummary();status.Text="●  Todo guardado · "+DateTime.Now.ToString("HH:mm:ss")+" · "+db.Entries.Count+" prompts";return true;}catch(Exception e){saveBadge.Text="Sin guardar";saveBadge.ForeColor=Color.FromArgb(248,174,127);status.Text="ERROR AL GUARDAR · "+e.Message;MessageBox.Show(this,"No se pudo guardar. El programa conservará los cambios en memoria. Exportá un respaldo antes de salir.\n\n"+e.Message,"Error de guardado",MessageBoxButtons.OK,MessageBoxIcon.Error);return false;}}
 void RefreshFilters(){busy=true;string cat=Convert.ToString(categories.SelectedItem),mod=Convert.ToString(model.SelectedItem);categories.Items.Clear();categories.Items.Add("Todas las categorías");var active=collection.Text=="Todos"?CatalogValues("category","Personales").Where(c=>ChoiceEnabled("category","Personales",c)).Concat(CatalogValues("category","404").Where(c=>ChoiceEnabled("category","404",c))).Distinct():CatalogValues("category",collection.Text).Where(c=>ChoiceEnabled("category",collection.Text,c));foreach(string c in active)categories.Items.Add(c);categories.SelectedItem=cat;if(categories.SelectedIndex<0)categories.SelectedIndex=0;model.Items.Clear();model.Items.Add("Todos los modelos");foreach(string m in CatalogValues("model").Where(m=>ChoiceEnabled("model","",m)))model.Items.Add(m);model.SelectedItem=mod;if(model.SelectedIndex<0)model.SelectedIndex=0;busy=false;}

 void RefreshAll(){gridGeneration++;orderPending=false;RefreshFilters();busy=true;grid.CurrentCell=null;grid.Rows.Clear();grid.Columns.Clear();var columns=new List<DataGridViewColumn>();
 columns.Add(new DataGridViewButtonColumn{Name="copy",HeaderText="Copiar",Text="Copiar",FlatStyle=FlatStyle.Flat,UseColumnTextForButtonValue=true,Width=75,ReadOnly=true});
 columns.Add(new DataGridViewButtonColumn{Name="favorite",HeaderText="Fav.",FlatStyle=FlatStyle.Flat,Width=55,ReadOnly=true});
 foreach(var f in db.Fields){DataGridViewColumn col;if(f.Id=="category"||f.Id=="model"||f.Id=="collection")col=new DataGridViewComboBoxColumn{FlatStyle=FlatStyle.Flat,DisplayStyle=DataGridViewComboBoxDisplayStyle.DropDownButton};else col=new DataGridViewTextBoxColumn();col.Name=f.Id;col.HeaderText=f.Title;col.Visible=f.Visible;col.Width=f.Id=="name"?220:f.Id=="description"?260:f.Id=="prompt"?240:150;col.ReadOnly=f.Id=="prompt";columns.Add(col);}
 columns.Add(new DataGridViewTextBoxColumn{Name="hotkey",HeaderText="Atajo global",Width=185,ReadOnly=true});
 foreach(string key in db.ColumnOrder.Concat(columns.Select(c=>c.Name)).Distinct()){var col=columns.FirstOrDefault(c=>c.Name==key);if(col!=null)grid.Columns.Add(col);}
 busy=false;RefreshRows();}

 void RefreshRows(){
  searchTimer.Stop();
  string id=selected==null?null:selected.Id;
  var rows=LibraryQuery.Filter(db.Entries,collection.Text,categories.SelectedIndex>0?categories.Text:null,
      model.SelectedIndex>0?model.Text:null,view.Text,search.Text,sortKey,sortDesc);
  busy=true;
  library.BeginUpdate();
  try{library.Items.Clear();library.Items.AddRange(rows.Cast<object>().ToArray());}
  finally{library.EndUpdate();}
  grid.CurrentCell=null;
  grid.Rows.Clear();
  if(tableMode)PopulateTable(rows);
  selected=rows.FirstOrDefault(e=>e.Id==id)??rows.FirstOrDefault();
  if(selected!=null){
   library.SelectedItem=selected;
   if(tableMode){var row=grid.Rows.Cast<DataGridViewRow>().First(r=>((Entry)r.Tag).Id==selected.Id);row.Selected=true;grid.CurrentCell=row.Cells["copy"];}
  }
  busy=false;
  resultCount.Text=rows.Count+" de "+db.Entries.Count+" prompts";
  listEmpty.Visible=rows.Count==0;
  emptyTitle.Text=db.Entries.Count==0?"Tu biblioteca empieza acá":"No encontramos ese prompt";
  emptyHint.Text=db.Entries.Count==0?"Guardá tus ideas, ordenalas por categoría\ny reutilizalas en un clic.":"Probá otra búsqueda o quitá los filtros\npara volver a ver tu biblioteca.";
  emptyAction.Text=db.Entries.Count==0?"Crear mi primer prompt":"Limpiar filtros";
  ShowSelected();
  UpdateNavigation();
 }

 void PopulateTable(IEnumerable<Entry> rows){
  // Choice catalogues are shared across rows, instead of rescanning the entire
  // library for every cell while typing in search.
  var choices=new Dictionary<string,List<string>>();
  foreach(string scope in new[]{"Personales","404"})
   foreach(string key in new[]{"category","model","collection"})
    choices[key+"::"+scope]=ChoiceValues(key,scope);
  foreach(var e in rows){
   int index=grid.Rows.Add();var row=grid.Rows[index];row.Tag=e;
   row.Cells["favorite"].Value=e.Favorite?"★":"☆";
   foreach(var field in db.Fields){
    string value=e.Get(field.Id);var choice=row.Cells[field.Id] as DataGridViewComboBoxCell;
    if(choice!=null){
     string scope=e.Get("collection")=="404"?"404":"Personales";
     choice.Items.AddRange(choices[field.Id+"::"+scope].Concat(new[]{value}).Distinct().Cast<object>().ToArray());
     if(field.Id!="collection")choice.Items.Add(NewChoice);
    }
    row.Cells[field.Id].Value=field.Id=="prompt"?(value.Length>110?value.Substring(0,110)+"…":value).Replace("\r"," ").Replace("\n"," "):value;
   }
   row.Cells["hotkey"].Value=e.Hotkey;
  }
  grid.ClearSelection();
 }

 void SelectEntry(){
  selected=grid.SelectedRows.Count==0?null:(Entry)grid.SelectedRows[0].Tag;
  ShowSelected();
 }

 void AddEntry(){
  var e=new Entry();e.Set("collection",collection.Text=="404"?"404":"Personales");
  e.Set("category",categories.SelectedIndex>0?categories.Text:"");e.Set("model",model.SelectedIndex>0?model.Text:"");e.Set("name","Nuevo prompt");
  db.Entries.Add(e);selected=e;Changed();RevealEntry(e);entryTitle.Focus();entryTitle.SelectAll();
 }
 void Edit(Entry e){if(e==null)return;if(EditForm(e)){Changed();RefreshAll();}}
 bool EditForm(Entry entry){using(var f=UI.Dialog("Editar prompt",780,690)){
  var fields=new TableLayoutPanel{Dock=DockStyle.Fill,AutoScroll=true,ColumnCount=2,Padding=new Padding(15)};fields.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,140));fields.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));var boxes=new Dictionary<string,Control>();int row=0;
  Func<string> scope=()=>boxes.ContainsKey("collection")?boxes["collection"].Text:entry.Get("collection");
  foreach(var field in db.Fields){fields.RowStyles.Add(new RowStyle(SizeType.Absolute,field.Id=="prompt"?220:field.Id=="note"||field.Id=="description"?75:42));fields.Controls.Add(new Label{Text=field.Title,Dock=DockStyle.Fill,TextAlign=ContentAlignment.MiddleLeft},0,row);Control input;
   if(field.Id=="category"||field.Id=="model"||field.Id=="collection")input=ChoiceBox(f,field.Id,scope,entry.Get(field.Id));
   else{var t=new TextBox{Text=entry.Get(field.Id),Multiline=field.Id=="prompt"||field.Id=="note"||field.Id=="description",ScrollBars=ScrollBars.Vertical};UI.Style(t);input=t;}
   input.Name=field.Id;input.Dock=DockStyle.Fill;fields.Controls.Add(input,1,row++);boxes[field.Id]=input;
  }
  var collectionBox=boxes["collection"] as ComboBox;collectionBox.SelectedIndexChanged+=(sender,args)=>{var cb=(ComboBox)boxes["category"];string previous=cb.Text;FillChoiceBox(cb,"category",scope(),previous);};
  var bottom=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=56};bottom.Controls.Add(UI.Button("Guardar",()=>{if(String.IsNullOrWhiteSpace(boxes["name"].Text)){MessageBox.Show("Poné un nombre.");return;}if(boxes.Values.Any(c=>c.Text==NewChoice))return;foreach(var pair in boxes)entry.Set(pair.Key,pair.Value.Text);RegisterChoice("category",scope(),entry.Get("category"));RegisterChoice("model",scope(),entry.Get("model"));f.DialogResult=DialogResult.OK;}));bottom.Controls.Add(UI.Button("Cancelar",()=>f.DialogResult=DialogResult.Cancel));f.Controls.Add(fields);f.Controls.Add(bottom);return f.ShowDialog(this)==DialogResult.OK;
 }}
 List<string> ChoiceValues(string key,string scope){if(key=="collection")return new List<string>{"Personales","404"};return new[]{""}.Concat(CatalogValues(key,scope).Where(v=>ChoiceEnabled(key,scope,v))).ToList();}

 void RegisterChoice(string key,string scope,string value){if(String.IsNullOrWhiteSpace(value)||value==NewChoice)return;value=value.Trim();if(key=="model"){if(!db.Models.Contains(value))db.Models.Add(value);}else if(key=="category"){string stored=scope+"::"+value;if(!db.Categories.Contains(stored))db.Categories.Add(stored);}}
 string CreateChoice(IWin32Window owner,string key,string scope){string value=UI.Ask(owner,key=="model"?"Nuevo modelo":"Nueva categoría","");if(String.IsNullOrWhiteSpace(value))return null;value=value.Trim();if(value==NewChoice)throw new Exception("Elegí otro nombre.");db.DisabledChoices.RemoveAll(c=>c==ChoiceId(key,scope,value));RegisterChoice(key,scope,value);Changed();return value;}
 void FillChoiceBox(ComboBox cb,string key,string scope,string current){cb.Items.Clear();foreach(var value in ChoiceValues(key,scope).Concat(new[]{current}).Distinct())cb.Items.Add(value);if(key!="collection")cb.Items.Add(NewChoice);cb.SelectedItem=current;}
 ComboBox ChoiceBox(Form owner,string key,Func<string> scope,string current){var cb=Combo(220);FillChoiceBox(cb,key,scope(),current);string previous=current;cb.SelectionChangeCommitted+=(sender,args)=>{if(cb.Text==NewChoice){string created=CreateChoice(owner,key,scope());FillChoiceBox(cb,key,scope(),created??previous);}previous=cb.Text;};return cb;}
 void GridChoiceCommitted(object sender,EventArgs args){var cb=sender as ComboBox;if(cb==null||grid.CurrentCell==null)return;string key=grid.CurrentCell.OwningColumn.Name;if(key!="category"&&key!="model")return;var entry=(Entry)grid.CurrentRow.Tag;if(cb.Text==NewChoice){string value=CreateChoice(this,key,entry.Get("collection"))??entry.Get(key);var cell=(DataGridViewComboBoxCell)grid.CurrentCell;if(!cell.Items.Contains(value))cell.Items.Insert(cell.Items.Count-1,value);if(!cb.Items.Contains(value))cb.Items.Insert(cb.Items.Count-1,value);cb.SelectedItem=value;grid.CurrentCell.Value=value;}grid.CommitEdit(DataGridViewDataErrorContexts.Commit);entry.Set(key,cb.Text);RegisterChoice(key,entry.Get("collection"),cb.Text);Changed();}
 void CaptureColumnOrder(){db.ColumnOrder=grid.Columns.Cast<DataGridViewColumn>().OrderBy(c=>c.DisplayIndex).Select(c=>c.Name).ToList();db.Fields=db.Fields.OrderBy(f=>db.ColumnOrder.IndexOf(f.Id)).ToList();}
 void AddColumn(){string title=UI.Ask(this,"Crear columna · nombre","");if(String.IsNullOrWhiteSpace(title))return;CaptureColumnOrder();var field=new Field{Id="custom_"+Guid.NewGuid().ToString("N"),Title=title.Trim()};db.Fields.Add(field);db.ColumnOrder.Add(field.Id);Changed();RefreshAll();SwitchMode(true);grid.FirstDisplayedScrollingColumnIndex=grid.Columns[field.Id].Index;}
 void AddModel(){string value=CreateChoice(this,"model",collection.Text=="404"?"404":"Personales");if(value==null)return;RefreshAll();model.SelectedItem=value;}

 void Duplicate(){if(selected==null)return;var e=selected.Clone();e.Set("name",e.Get("name")+" · copia");e.Set("collection","Personales");db.Entries.Add(e);selected=e;Changed();RevealEntry(e);}
 void Delete(){if(selected==null)return;if(!UI.Confirm(this,"¿Eliminar «"+selected.Get("name")+"»? Podés recuperar el estado anterior desde Respaldos."))return;db.Entries.Remove(selected);selected=null;Changed();Save();RegisterKeys();RefreshAll();}
 void AddCategory(){
  string scope=collection.Text=="404"?"404":"Personales";
  string name=CreateChoice(this,"category",scope);if(name==null)return;
  RefreshAll();categories.SelectedItem=name;
 }

 void Variables(Entry e){
  if(e==null)return;
  using(var dialog=VariableEditor.Create(e)){
   if(dialog.ShowDialog(this)==DialogResult.OK)Changed();
  }
 }
 bool FillVariables(Entry e){
  if(e.Variables.Count==0)return true;
  using(var dialog=CreateVariableValuesDialog(e))return dialog.ShowDialog()==DialogResult.OK;
 }
 void Copy(Entry e){if(e==null)return;grid.EndEdit();if(e.Get("prompt").Length==0)throw new Exception("Este prompt está vacío.");if(!FillVariables(e))return;string text=Templates.Render(e);Clipboard.SetDataObject(text,true,5,80);e.LastUsed=DateTime.UtcNow.ToString("o");Changed(null);Save();status.Text="Copiado: "+e.Get("name")+" · "+text.Length+" caracteres";if(tray!=null&&(!Visible||WindowState==FormWindowState.Minimized))tray.ShowBalloonTip(1200,"Prompt copiado",e.Get("name"),ToolTipIcon.Info);}
 public static bool ParseKey(string s,out uint modifiers,out uint key){modifiers=0;key=0;if(String.IsNullOrWhiteSpace(s))return false;var parts=s.Split('+').Select(p=>p.Trim()).ToArray();foreach(var p in parts.Take(parts.Length-1)){if(p.Equals("Ctrl",StringComparison.OrdinalIgnoreCase))modifiers|=2;else if(p.Equals("Alt",StringComparison.OrdinalIgnoreCase))modifiers|=1;else if(p.Equals("Shift",StringComparison.OrdinalIgnoreCase))modifiers|=4;else return false;}string last=parts.Last();if(last.StartsWith("NumPad",StringComparison.OrdinalIgnoreCase)&&last.Length==7&&Char.IsDigit(last[6]))key=(uint)(96+last[6]-'0');else return false;return modifiers!=0;}
 void Shortcut(Entry e){if(e==null)return;string value=UI.Ask(this,"Atajo global · ejemplo Ctrl+Alt+NumPad1 (vacío = quitar)",e.Hotkey);if(value==null)return;value=value.Trim();uint mods,key;if(value.Length>0){if(!ParseKey(value,out mods,out key))throw new Exception("Usá Ctrl+Alt+NumPad0 a NumPad9; también se admiten Alt o Shift.");foreach(var other in db.Entries.Where(a=>a.Id!=e.Id)){uint om,ok;if(ParseKey(other.Hotkey,out om,out ok)&&om==mods&&ok==key)throw new Exception("Ese atajo está asignado a "+other.Get("name"));}foreach(var pair in hotkeys.Where(p=>p.Value.Id==e.Id).ToList()){UnregisterHotKey(Handle,pair.Key);hotkeys.Remove(pair.Key);}if(!RegisterHotKey(Handle,999,mods|0x4000,key)){RegisterKeys();throw new Exception("Windows u otra aplicación ya usa ese atajo. Elegí otro.");}UnregisterHotKey(Handle,999);}e.Hotkey=value;Changed();Save();RegisterKeys();RefreshRows();}
 void RegisterKeys(bool showWarnings=true){if(testing)return;foreach(int id in hotkeys.Keys)UnregisterHotKey(Handle,id);hotkeys.Clear();var errors=new List<string>();int next=1;foreach(var e in db.Entries){uint mods,key;if(e.Hotkey.Length==0)continue;if(!ParseKey(e.Hotkey,out mods,out key)||!RegisterHotKey(Handle,next,mods|0x4000,key)){errors.Add(e.Get("name")+" · "+e.Hotkey);continue;}hotkeys[next++]=e;}if(!showWarnings){ReportCloudHotkeyErrors(errors);return;}if(errors.Count>0)MessageBox.Show(this,"No se pudieron activar estos atajos (inválidos u ocupados):\n"+String.Join("\n",errors.ToArray()),"Atajos no disponibles");}
 protected override void WndProc(ref Message m){if(m.Msg==0x0312&&hotkeys.ContainsKey(m.WParam.ToInt32())){var e=hotkeys[m.WParam.ToInt32()];BeginInvoke((Action)(()=>{try{Copy(e);}catch(Exception ex){MessageBox.Show(ex.Message,"No se pudo copiar");}}));}base.WndProc(ref m);}
 void ManageFields(){using(var f=UI.Dialog("Columnas · arrastrá encabezados para reordenar",580,540)){var list=new CheckedListBox{Dock=DockStyle.Fill,CheckOnClick=true};UI.Style(list);foreach(var field in db.Fields)list.Items.Add(field.Title,field.Visible);var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=100};bar.Controls.Add(UI.Button("+ Columna",()=>{string title=UI.Ask(f,"Nombre de columna","");if(String.IsNullOrWhiteSpace(title))return;db.Fields.Add(new Field{Id="custom_"+Guid.NewGuid().ToString("N"),Title=title});list.Items.Add(title,true);Changed();}));bar.Controls.Add(UI.Button("Renombrar",()=>{int i=list.SelectedIndex;if(i<0)return;string title=UI.Ask(f,"Nombre de columna",db.Fields[i].Title);if(String.IsNullOrWhiteSpace(title))return;db.Fields[i].Title=title;list.Items[i]=title;Changed();}));bar.Controls.Add(UI.Button("Eliminar",()=>{int i=list.SelectedIndex;if(i<0)return;var field=db.Fields[i];if(!field.Id.StartsWith("custom_")){MessageBox.Show("Las columnas base se pueden ocultar y renombrar, pero se conservan para las funciones del programa.");return;}if(!UI.Confirm(f,"¿Eliminar la columna y sus valores? El estado anterior queda en Respaldos."))return;db.Fields.RemoveAt(i);foreach(var e in db.Entries)e.Cells.Remove(field.Id);list.Items.RemoveAt(i);Changed();}));bar.Controls.Add(UI.Button("Aplicar y cerrar",()=>{for(int i=0;i<db.Fields.Count;i++)db.Fields[i].Visible=list.GetItemChecked(i);Changed();f.Close();}));f.Controls.Add(list);f.Controls.Add(bar);f.ShowDialog(this);RefreshAll();}}
 void Export(){using(var d=new SaveFileDialog{Filter="Excel (*.xlsx)|*.xlsx",FileName="Mis_prompts.xlsx"})if(d.ShowDialog(this)==DialogResult.OK){grid.EndEdit();Excel.Write(d.FileName,db);status.Text="Excel exportado: "+d.FileName;}}
 void Backups(){using(var f=UI.Dialog("Respaldos e historial recuperable",720,500)){var label=new Label{Text="Se conservan los últimos 200 estados guardados. Restaurar reemplaza el catálogo completo.\nExportá un respaldo a otra unidad para protegerte ante fallas del disco.",Dock=DockStyle.Top,Height=55};var list=new ListBox{Dock=DockStyle.Fill};UI.Style(list);foreach(var path in Directory.GetFiles(Path.Combine(db.Folder,"Historial"),"*.xml").OrderByDescending(p=>p))list.Items.Add(Path.GetFileName(path));var bar=new FlowLayoutPanel{Dock=DockStyle.Bottom,Height=100};bar.Controls.Add(UI.Button("Exportar respaldo completo",()=>{using(var d=new SaveFileDialog{Filter="Respaldo PromptDesk (*.xml)|*.xml",FileName="PromptDesk_"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+".xml"})if(d.ShowDialog(f)==DialogResult.OK)db.Export(d.FileName);}));bar.Controls.Add(UI.Button("Restaurar seleccionado",()=>{if(list.SelectedItem!=null)Restore(Path.Combine(db.Folder,"Historial",list.SelectedItem.ToString()),f);}));bar.Controls.Add(UI.Button("Importar respaldo",()=>{using(var d=new OpenFileDialog{Filter="Respaldo PromptDesk (*.xml)|*.xml"})if(d.ShowDialog(f)==DialogResult.OK)Restore(d.FileName,f);}));bar.Controls.Add(UI.Button("Abrir carpeta de datos",()=>System.Diagnostics.Process.Start(db.Folder)));f.Controls.Add(list);f.Controls.Add(label);f.Controls.Add(bar);f.ShowDialog(this);}}
 void Restore(string path,Form dialog){if(!UI.Confirm(dialog,"¿Restaurar este catálogo completo? Se guardará una copia del estado actual."))return;var candidate=new Store(db.Folder);candidate.Load(path);dirty=true;if(!Save())return;db.Fields=candidate.Fields;db.Entries=candidate.Entries;db.Categories=candidate.Categories;db.Models=candidate.Models;db.ColumnOrder=candidate.ColumnOrder;db.CategoryOrder=candidate.CategoryOrder;db.DisabledChoices=candidate.DisabledChoices;dirty=true;if(Save()){history=new UndoHistory(db);RegisterKeys();RefreshAll();dialog.Close();}}
 void Import(){using(var d=new OpenFileDialog{Filter="Excel (*.xlsx)|*.xlsx"}){if(d.ShowDialog(this)!=DialogResult.OK)return;var sheets=Excel.Read(d.FileName);if(sheets.Count==0)throw new Exception("El archivo no contiene hojas.");using(var f=UI.Dialog("Importar Excel · asignar columnas",850,640)){var sheet=Combo(220);foreach(var s in sheets)sheet.Items.Add(s.Name);sheet.SelectedIndex=0;var header=new NumericUpDown{Minimum=1,Maximum=10000,Value=1,Width=70};var dest=Combo(140);dest.Items.AddRange(new object[]{"Personales","404"});dest.SelectedIndex=0;var top=Bar(55);top.Controls.Add(sheet);top.Controls.Add(new Label{Text="Fila de títulos",AutoSize=true,Padding=new Padding(0,8,0,0)});top.Controls.Add(header);top.Controls.Add(dest);var table=new DataGridView{Dock=DockStyle.Fill,AllowUserToAddRows=false,AutoSizeColumnsMode=DataGridViewAutoSizeColumnsMode.Fill};table.Columns.Add(new DataGridViewTextBoxColumn{Name="source",HeaderText="Columna Excel",ReadOnly=true});table.Columns.Add(new DataGridViewTextBoxColumn{Name="sample",HeaderText="Ejemplo",ReadOnly=true});var map=new DataGridViewComboBoxColumn{Name="target",HeaderText="Usar como"};map.Items.Add("Ignorar");map.Items.Add("Crear columna nueva");foreach(var field in db.Fields)map.Items.Add(field.Title+" ["+field.Id+"]");table.Columns.Add(map);
  Action fill=()=>{table.Rows.Clear();var data=sheets[sheet.SelectedIndex];int h=(int)header.Value-1;if(h>=data.Rows.Count)return;var labels=data.Rows[h];for(int c=0;c<labels.Count;c++){string sample=h+1<data.Rows.Count&&c<data.Rows[h+1].Count?data.Rows[h+1][c]:"";if(sample.Length>120)sample=sample.Substring(0,120)+"…";string target="Crear columna nueva",norm=labels[c].ToLowerInvariant();string key=norm.Contains("prom")?"prompt":norm.Contains("categor")?"category":norm.Contains("nombre")?"name":norm.Contains("descrip")?"description":norm.Contains("modelo")?"model":norm.Contains("nota")?"note":norm.Contains("colecci")?"collection":"";var field=db.Fields.FirstOrDefault(a=>a.Id==key);if(field!=null)target=field.Title+" ["+field.Id+"]";table.Rows.Add(labels[c],sample,target);}};sheet.SelectedIndexChanged+=(s,e)=>fill();header.ValueChanged+=(s,e)=>fill();fill();
  var bottom=Bar(55);bottom.Dock=DockStyle.Bottom;bottom.Controls.Add(UI.Button("Importar como nuevos",()=>{table.EndEdit();var data=sheets[sheet.SelectedIndex];int h=(int)header.Value-1;if(h>=data.Rows.Count)throw new Exception("La fila de títulos no existe.");var mapping=new Dictionary<int,string>();var additions=new List<Field>();foreach(DataGridViewRow r in table.Rows){string target=Convert.ToString(r.Cells[2].Value);if(target=="Ignorar")continue;string key;if(target=="Crear columna nueva"){key="custom_"+Guid.NewGuid().ToString("N");additions.Add(new Field{Id=key,Title=String.IsNullOrWhiteSpace(Convert.ToString(r.Cells[0].Value))?"Columna "+(r.Index+1):Convert.ToString(r.Cells[0].Value)});}else key=target.Substring(target.LastIndexOf('[')+1).TrimEnd(']');if(mapping.Values.Contains(key))throw new Exception("Asignaste dos columnas al mismo campo.");mapping[r.Index]=key;}if(!mapping.Values.Contains("prompt"))throw new Exception("Elegí una columna para Prompt.");var added=new List<Entry>();var existingPrompts=new HashSet<string>(db.Entries.Select(a=>a.Get("prompt")),StringComparer.Ordinal);int duplicate=0,empty=0;foreach(var row in data.Rows.Skip(h+1)){var item=new Entry();item.Set("collection",dest.Text);foreach(var pair in mapping)item.Set(pair.Value,pair.Key<row.Count?row[pair.Key]:"");if(String.IsNullOrWhiteSpace(item.Get("prompt"))){empty++;continue;}if(!existingPrompts.Add(item.Get("prompt"))){duplicate++;continue;}if(item.Get("collection")!="404"&&item.Get("collection")!="Personales")item.Set("collection",dest.Text);if(String.IsNullOrWhiteSpace(item.Get("name")))item.Set("name","Importado "+(added.Count+1));added.Add(item);}if(!UI.Confirm(f,"Agregar "+added.Count+" prompts.\nOmitir "+duplicate+" duplicados exactos y "+empty+" filas sin prompt.\nNo se reemplazará ningún prompt existente."))return;db.Fields.AddRange(additions);db.Entries.AddRange(added);Changed();Save();RefreshAll();f.Close();}));bottom.Controls.Add(UI.Button("Cancelar",()=>f.Close()));f.Controls.Add(table);f.Controls.Add(top);f.Controls.Add(bottom);f.ShowDialog(this);}}}
}
}
