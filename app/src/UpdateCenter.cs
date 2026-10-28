using System;
using System.IO;
using System.Diagnostics;
using System.Drawing;
using System.Windows.Forms;
using System.Xml.Linq;
namespace PromptDesk {
public static class UpdateCenter {
 public static string ChannelFile {get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PromptDesk","update-channel.txt");}}
 public static string ReadChannel(){string channel=File.Exists(ChannelFile)?File.ReadAllText(ChannelFile).Trim():"";return channel.Length>0?channel:"https://github.com/neyen-quan/PromptDesk/releases/latest/download/latest.json";}
 public static bool ValidChannel(string text){Uri u;return Uri.TryCreate(text,UriKind.Absolute,out u)&&u.Scheme=="https"&&String.IsNullOrEmpty(u.UserInfo);}
 public static void Show(Form owner,Func<bool> closeSaved){using(var f=UI.Dialog("Actualizar PromptDesk",640,330)){
  var text=new Label{Dock=DockStyle.Top,Height=112,Padding=new Padding(18),ForeColor=UI.Ink,Text="Versión instalada: "+Branding.Version+"\n\n"+(ReadChannel().Length==0?"Descarga directa pendiente de configurar el canal de versiones.\nPodés instalar un ZIP de parche sin extraerlo.":"Buscá versiones nuevas o instalá un ZIP de parche.\nLa descarga y extracción se realizan automáticamente.")};
  var bar=new FlowLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(14),BackColor=Color.Transparent};
  bar.Controls.Add(UI.Button("Buscar actualizaciones",()=>{if(ReadChannel().Length==0){MessageBox.Show(f,"Todavía no hay una dirección de versiones configurada. Cuando el canal esté publicado, se conecta desde Configurar canal.","PromptDesk");return;}Launch(owner,f,closeSaved,"feed",ReadChannel());}));
  bar.Controls.Add(UI.Button("Instalar ZIP",()=>{using(var pick=new OpenFileDialog{Filter="Parche PromptDesk (*.zip)|*.zip",Title="Elegí el ZIP del parche; no hace falta extraerlo"})if(pick.ShowDialog(f)==DialogResult.OK)Launch(owner,f,closeSaved,"zip",pick.FileName);}));
  bar.Controls.Add(UI.Button("Configurar canal",()=>{string value=UI.Ask(f,"Dirección HTTPS del canal de actualizaciones",ReadChannel());if(value==null)return;value=value.Trim();if(value.Length>0&&!ValidChannel(value))throw new Exception("El canal debe ser una dirección HTTPS válida.");Directory.CreateDirectory(Path.GetDirectoryName(ChannelFile));File.WriteAllText(ChannelFile,value);f.Close();}));bar.Controls.Add(UI.Button("Cerrar",()=>f.Close()));f.Controls.Add(bar);f.Controls.Add(text);f.ShowDialog(owner);
 }}
 static void Launch(Form owner,Form dialog,Func<bool> closeSaved,string mode,string value){
  string install=AppDomain.CurrentDomain.BaseDirectory,engine=Path.Combine(install,"UpdatePromptDesk.ps1");if(!File.Exists(engine))throw new Exception("Falta el actualizador. Aplicá el parche de transición completo.");
  string work=Path.Combine(Path.GetTempPath(),"PromptDesk-update-"+Guid.NewGuid().ToString("N"));Directory.CreateDirectory(work);File.Copy(engine,Path.Combine(work,"UpdatePromptDesk.ps1"));
  string request=Path.Combine(work,"request.xml");new XDocument(new XElement("update",new XElement("target",install),new XElement("mode",mode),new XElement("value",value),new XElement("pid",Process.GetCurrentProcess().Id))).Save(request);
  var process=Process.Start(new ProcessStartInfo("powershell.exe","-NoProfile -STA -ExecutionPolicy Bypass -WindowStyle Hidden -File \""+Path.Combine(work,"UpdatePromptDesk.ps1")+"\" -RequestFile \""+request+"\""){UseShellExecute=false,CreateNoWindow=true});
  dialog.Enabled=false;dialog.UseWaitCursor=true;
  var timer=new System.Windows.Forms.Timer{Interval=250};timer.Tick+=(sender,args)=>{if(File.Exists(Path.Combine(work,"ready"))){timer.Stop();timer.Dispose();dialog.Enabled=true;dialog.Close();if(!closeSaved())File.WriteAllText(Path.Combine(work,"cancel"),"cancel");}else if(process.HasExited){timer.Stop();timer.Dispose();dialog.Enabled=true;dialog.UseWaitCursor=false;}};timer.Start();
 }
}
}
