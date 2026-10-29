using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Drawing;
using System.Reflection;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using Microsoft.Win32;
[assembly: AssemblyTitle("PromptDesk")]
[assembly: AssemblyProduct("PromptDesk")]
[assembly: AssemblyDescription("Tu biblioteca de prompts")]
[assembly: AssemblyVersion("1.1.3.0")]
[assembly: AssemblyFileVersion("1.1.3.0")]
namespace PromptDesk {
public static class Branding {
 public const string Version="1.1.3";
 public const string AppId="Neyen.PromptDesk.Desktop";
 static readonly Guid StoreId=new Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99");
 [StructLayout(LayoutKind.Sequential)] struct Key{public Guid id;public uint pid;public Key(uint p){id=new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3");pid=p;}}
 [StructLayout(LayoutKind.Sequential)] struct Value{public ushort vt,a,b,c;public IntPtr text,reserved;}
 [ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface IPropertyStore{
  [PreserveSig]int GetCount(out uint n);[PreserveSig]int GetAt(uint i,out Key key);[PreserveSig]int GetValue(ref Key key,out Value value);[PreserveSig]int SetValue(ref Key key,ref Value value);[PreserveSig]int Commit();
 }
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string id);
 [DllImport("shell32.dll")]static extern int SHGetPropertyStoreForWindow(IntPtr handle,ref Guid iid,[MarshalAs(UnmanagedType.Interface)]out IPropertyStore store);
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern void SHChangeNotify(uint ev,uint flags,string first,IntPtr second);
 [DllImport("ole32.dll")]static extern int PropVariantClear(ref Value value);
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr handle,int msg,IntPtr first,IntPtr second);
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
 [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)] static extern int SetWindowTheme(IntPtr handle,string subApp,string subId);
 static bool shellPrepared;static string shellIcon;static Icon icon,largeIcon;static Image logo;
 static bool Windows{get{return Environment.OSVersion.Platform==PlatformID.Win32NT;}}
 static string Exe{get{return Assembly.GetExecutingAssembly().Location;}}
 static string BrandFolder{get{return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"PromptDesk","Branding");}}
 public static void Initialize(){if(Windows)try{SetCurrentProcessExplicitAppUserModelID(AppId);}catch(EntryPointNotFoundException){}}
 public static void DarkControls(IntPtr handle){if(Windows)try{SetWindowTheme(handle,"DarkMode_Explorer",null);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
 public static void DarkCaption(IntPtr handle){if(!Windows)return;try{int dark=1;DwmSetWindowAttribute(handle,20,ref dark,4);int bg=0x001F140C;DwmSetWindowAttribute(handle,35,ref bg,4);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}BindWindow(handle);}
 static void WriteError(Exception e){try{Directory.CreateDirectory(BrandFolder);File.AppendAllText(Path.Combine(BrandFolder,"shell-errors.txt"),DateTime.Now.ToString("o")+" "+e+Environment.NewLine);}catch{}}
 static void Put(IPropertyStore store,uint pid,string text){var key=new Key(pid);var value=new Value{vt=31,text=Marshal.StringToCoTaskMemUni(text)};try{Marshal.ThrowExceptionForHR(store.SetValue(ref key,ref value));}finally{Marshal.FreeCoTaskMem(value.text);}}
 static object NewLink(){return Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("00021401-0000-0000-C000-000000000046")));}
 static string ReadId(string path){object link=null;try{link=NewLink();((IPersistFile)link).Load(path,0);var key=new Key(5);Value value;Marshal.ThrowExceptionForHR(((IPropertyStore)link).GetValue(ref key,out value));try{return value.vt==31?Marshal.PtrToStringUni(value.text):"";}finally{PropVariantClear(ref value);}}catch{return "";}finally{if(link!=null)Marshal.FinalReleaseComObject(link);}}
 static string Get(object o,string name){return Convert.ToString(o.GetType().InvokeMember(name,BindingFlags.GetProperty,null,o,null));}
 static void Set(object o,string name,string text){o.GetType().InvokeMember(name,BindingFlags.SetProperty,null,o,new object[]{text});}
 public static bool OwnShortcut(string target,string id){try{return id==AppId||String.Equals(Path.GetFileName(target),"PromptDesk.exe",StringComparison.OrdinalIgnoreCase);}catch{return false;}}
 static void FixLink(object shell,string path,string backup,int index){
  if(File.Exists(path))File.Copy(path,Path.Combine(backup,index.ToString("D3")+".lnk"),false);
  object shortcut=null;try{shortcut=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});Set(shortcut,"TargetPath",Exe);Set(shortcut,"WorkingDirectory",Path.GetDirectoryName(Exe));Set(shortcut,"IconLocation",shellIcon+",0");Set(shortcut,"Description","PromptDesk");shortcut.GetType().InvokeMember("Save",BindingFlags.InvokeMethod,null,shortcut,null);}finally{if(shortcut!=null)Marshal.FinalReleaseComObject(shortcut);}
  object link=NewLink();try{var file=(IPersistFile)link;file.Load(path,2);var store=(IPropertyStore)link;Put(store,5,AppId);Marshal.ThrowExceptionForHR(store.Commit());file.Save(path,true);}finally{Marshal.FinalReleaseComObject(link);}
  SHChangeNotify(0x2000,0x5,path,IntPtr.Zero);
 }
 static void PrepareShell(){
  if(shellPrepared)return;shellPrepared=true;Directory.CreateDirectory(BrandFolder);shellIcon=Path.Combine(BrandFolder,"PromptDesk-taskbar-v2.ico");
  byte[] bytes;using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("PromptDesk.ico"))using(var memory=new MemoryStream()){s.CopyTo(memory);bytes=memory.ToArray();}
  bool iconChanged=!File.Exists(shellIcon)||!File.ReadAllBytes(shellIcon).SequenceEqual(bytes);
  if(iconChanged){string temp=shellIcon+".tmp";File.WriteAllBytes(temp,bytes);if(File.Exists(shellIcon))File.Replace(temp,shellIcon,shellIcon+".previous");else File.Move(temp,shellIcon);}
  string state=Path.Combine(BrandFolder,"shell-location-v2.txt");if(!iconChanged&&File.Exists(state)&&File.ReadAllText(state)==Exe)return;
  string backup=Path.Combine(BrandFolder,"Shortcuts-"+DateTime.Now.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N").Substring(0,6));Directory.CreateDirectory(backup);
  string desktop=Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),start=Environment.GetFolderPath(Environment.SpecialFolder.Programs),pinned=Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"Microsoft","Internet Explorer","Quick Launch","User Pinned","TaskBar");
  object shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell"));var paths=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var log=new StringBuilder();bool complete=true;
  try{
   foreach(string folder in new[]{desktop,start,pinned}){if(!Directory.Exists(folder))continue;foreach(string path in Directory.GetFiles(folder,"*.lnk")){object l=null;try{l=shell.GetType().InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});if(OwnShortcut(Get(l,"TargetPath"),ReadId(path)))paths.Add(path);}catch(Exception e){WriteError(e);}finally{if(l!=null)Marshal.FinalReleaseComObject(l);}}}
   foreach(string folder in new[]{desktop,start})if(Directory.Exists(folder)){string path=Path.Combine(folder,"PromptDesk.lnk");if(!File.Exists(path))paths.Add(path);}
   int index=0;foreach(string path in paths){string saved=Path.Combine(backup,index.ToString("D3")+".lnk");try{FixLink(shell,path,backup,index++);log.AppendLine(path+" => "+Exe);}catch(Exception e){complete=false;WriteError(e);try{if(File.Exists(saved))File.Copy(saved,path,true);}catch(Exception restoreError){WriteError(restoreError);}}}
  }finally{Marshal.FinalReleaseComObject(shell);File.WriteAllText(Path.Combine(backup,"repair-log.txt"),log.ToString());}
  using(var key=Registry.CurrentUser.CreateSubKey("Software\\PromptDesk"))key.SetValue("InstallDir",Path.GetDirectoryName(Exe));
  SHChangeNotify(0x08000000,0,null,IntPtr.Zero);if(complete)File.WriteAllText(state,Exe);
 }
 static void BindWindow(IntPtr handle){
  try{PrepareShell();}catch(Exception e){WriteError(e);}
  try{IPropertyStore store;Guid iid=StoreId;Marshal.ThrowExceptionForHR(SHGetPropertyStoreForWindow(handle,ref iid,out store));try{Put(store,2,"\""+Exe+"\"");Put(store,4,System.Diagnostics.FileVersionInfo.GetVersionInfo(Exe).ProductName??"PromptDesk");Put(store,3,(shellIcon??Exe)+",0");Put(store,5,AppId);}finally{Marshal.FinalReleaseComObject(store);}}catch(Exception e){WriteError(e);}
  try{SendMessage(handle,0x0080,new IntPtr(0),Icon.Handle);if(largeIcon==null)using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("PromptDesk.ico"))largeIcon=new Icon(s,64,64);SendMessage(handle,0x0080,new IntPtr(1),largeIcon.Handle);}catch(Exception e){WriteError(e);}
 }
 public static Icon Icon{get{if(icon==null)using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("PromptDesk.ico"))icon=new Icon(s,32,32);return icon;}}
 public static Image Logo{get{if(logo==null)using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("PromptDesk.png"))using(var i=Image.FromStream(s))logo=new Bitmap(i);return logo;}}
}
}
