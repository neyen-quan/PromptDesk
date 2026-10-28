using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Windows.Forms;
namespace PromptDesk {
public static class Installation {
 [StructLayout(LayoutKind.Sequential)] struct PropertyKey{public Guid fmtid;public uint pid;public PropertyKey(string g,uint p){fmtid=new Guid(g);pid=p;}}
 [StructLayout(LayoutKind.Sequential)] struct PropVariant{public ushort vt,w1,w2,w3;public IntPtr text,reserved;}
 [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPropertyStore {
  [PreserveSig]int GetCount(out uint count);[PreserveSig]int GetAt(uint i,out PropertyKey key);[PreserveSig]int GetValue(ref PropertyKey key,out PropVariant value);[PreserveSig]int SetValue(ref PropertyKey key,ref PropVariant value);[PreserveSig]int Commit();
 }
 static void Link(string filename,string exe){
  object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));object shortcut=null;
  try{shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{filename});Action<string,object> set=(name,value)=>shortcut.GetType().InvokeMember(name,BindingFlags.SetProperty,null,shortcut,new[]{value});set("TargetPath",exe);set("WorkingDirectory",Path.GetDirectoryName(exe));set("IconLocation",exe+",0");set("Description","PromptDesk · Tu biblioteca de prompts");shortcut.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);}finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);Marshal.FinalReleaseComObject(shell);}
  object link=Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")));
  try{var file=(IPersistFile)link;file.Load(filename,2);var store=(IPropertyStore)link;var key=new PropertyKey("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3",5);var val=new PropVariant{vt=31,text=Marshal.StringToCoTaskMemUni(Branding.AppId)};try{Marshal.ThrowExceptionForHR(store.SetValue(ref key,ref val));Marshal.ThrowExceptionForHR(store.Commit());file.Save(filename,true);}finally{Marshal.FreeCoTaskMem(val.text);}}finally{Marshal.FinalReleaseComObject(link);}
 }
 public static void Run(){
  try{
   var me=Process.GetCurrentProcess();if(Process.GetProcessesByName(me.ProcessName).Any(p=>p.Id!=me.Id)){MessageBox.Show("Cerrá PromptDesk con la X y volvé a ejecutar INSTALAR_O_ACTUALIZAR.vbs. Tus datos se conservan.","PromptDesk");return;}
   string source=AppDomain.CurrentDomain.BaseDirectory,dest=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","PromptDesk");Directory.CreateDirectory(dest);
   foreach(string name in new[]{"PromptDesk.exe","PromptDesk.exe.config","seed.xml","LEEME.txt","UpdatePromptDesk.ps1"}){string from=Path.Combine(source,name),to=Path.Combine(dest,name);if(String.Equals(Path.GetFullPath(from),Path.GetFullPath(to),StringComparison.OrdinalIgnoreCase))continue;File.Copy(from,to+".incoming",true);if(File.Exists(to))File.Replace(to+".incoming",to,null);else File.Move(to+".incoming",to);}
   string exe=Path.Combine(dest,"PromptDesk.exe");Link(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"PromptDesk.lnk"),exe);Link(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"PromptDesk.lnk"),exe);
   MessageBox.Show("PromptDesk "+Branding.Version+" está listo.\n\nTenés un acceso directo nuevo en el escritorio y en Inicio. Tus prompts y ajustes se conservan.\n\nSi tenías el icono anterior anclado: desanclalo, abrí PromptDesk desde el acceso nuevo y volvé a anclarlo una vez.","PromptDesk",MessageBoxButtons.OK,MessageBoxIcon.Information);
   Process.Start(exe);
  }catch(Exception e){MessageBox.Show("No se pudo completar la instalación. Tus datos siguen en su carpeta habitual.\n\n"+e.Message,"PromptDesk",MessageBoxButtons.OK,MessageBoxIcon.Error);}
 }
}
}
