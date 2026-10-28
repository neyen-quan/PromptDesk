using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Reflection;
[assembly: AssemblyTitle("PromptDesk")]
[assembly: AssemblyProduct("PromptDesk")]
[assembly: AssemblyDescription("Tu biblioteca de prompts")]
[assembly: AssemblyVersion("1.1.2.0")]
[assembly: AssemblyFileVersion("1.1.2.0")]
namespace PromptDesk {
public static class Branding {
 public const string Version="1.1.2";
 public const string AppId="Neyen.PromptDesk.Desktop";
 [DllImport("shell32.dll",CharSet=CharSet.Unicode)] static extern int SetCurrentProcessExplicitAppUserModelID(string id);
 [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr hwnd,int attr,ref int value,int size);
 [DllImport("uxtheme.dll",CharSet=CharSet.Unicode)] static extern int SetWindowTheme(IntPtr handle,string subApp,string subId);
 public static void DarkControls(IntPtr handle){if(Environment.OSVersion.Platform==PlatformID.Win32NT)try{SetWindowTheme(handle,"DarkMode_Explorer",null);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
 public static void Initialize(){if(Environment.OSVersion.Platform==PlatformID.Win32NT)try{SetCurrentProcessExplicitAppUserModelID(AppId);}catch(EntryPointNotFoundException){}}
 public static void DarkCaption(IntPtr handle){if(Environment.OSVersion.Platform!=PlatformID.Win32NT)return;try{int dark=1;DwmSetWindowAttribute(handle,20,ref dark,4);int bg=0x001F140C;DwmSetWindowAttribute(handle,35,ref bg,4);}catch(DllNotFoundException){}catch(EntryPointNotFoundException){}}
 static Icon icon;static Image logo;
 public static Icon Icon{get{if(icon==null)using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("PromptDesk.ico"))icon=new Icon(s,32,32);return icon;}}
 public static Image Logo{get{if(logo==null)using(var s=Assembly.GetExecutingAssembly().GetManifestResourceStream("PromptDesk.png"))using(var i=Image.FromStream(s))logo=new Bitmap(i);return logo;}}
}
}
