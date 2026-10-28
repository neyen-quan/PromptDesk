using System;
using System.IO;
using System.Linq;
using System.Xml.Linq;
namespace PromptDesk {
public static class Tests {
 static void Check(bool value,string message){if(!value)throw new Exception(message);}
 public static void Run(){
  string dir=Path.Combine(Path.GetTempPath(),"PromptDesk-test-"+Guid.NewGuid());Directory.CreateDirectory(dir);
  try {
   Check(Branding.Icon.Width==32&&Branding.Logo.Width>=256,"Recursos de identidad incompletos");
   var db=new Store(dir);db.Load(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"seed.xml"));
   Check(db.Entries.Count==0,"La distribución pública contiene registros personales");
   for(int i=0;i<18;i++){var fixture=new Entry();fixture.Set("collection",i<9?"Personales":"404");fixture.Set("category","Prueba");fixture.Set("name","Prueba "+i);fixture.Set("prompt","Texto de prueba "+i);db.Entries.Add(fixture);}
   db.Save();var original=db.Entries[0].Get("prompt");db.Entries[0].Set("prompt","nuevo\ntexto\tñ & < > \" 🚀");db.Save();
   var reread=new Store(dir);reread.Load(db.Pathname);Check(reread.Entries[0].Get("prompt")==db.Entries[0].Get("prompt"),"Guardado pierde texto");
   var backups=Directory.GetFiles(Path.Combine(dir,"Historial"),"*.xml");Check(backups.Length==1,"Historial ausente");reread.Load(backups[0]);Check(reread.Entries[0].Get("prompt")==original,"Respaldo no conserva estado anterior");
   string exported=Path.Combine(dir,"roundtrip.xlsx");Excel.Write(exported,db);var data=Excel.Read(exported)[0];Check(data.Rows.Count==db.Entries.Count+1,"Exportación pierde filas");
   for(int i=0;i<db.Entries.Count;i++)for(int c=0;c<db.Fields.Count;c++)Check(data.Rows[i+1][c]==db.Entries[i].Get(db.Fields[c].Id),"Excel pierde contenido");
   var e=new Entry();e.Set("prompt","X degrees, EXTRA X-ray SIDE {{ropa}}");e.Variables.Add(new Variable{Token="X",Value="15"});e.Variables.Add(new Variable{Token="SIDE",Value="left"});e.Variables.Add(new Variable{Token="{{ropa}}",Value="SIDE $1"});
   Check(Templates.Render(e)=="15 degrees, EXTRA 15-ray left SIDE $1","Sustitución incorrecta o recursiva");Check(e.Get("prompt").StartsWith("X"),"Se modificó el original");
   var json=new Entry();json.Set("prompt","{\"task\":\"{{text}}\"}");json.Variables.Add(new Variable{Token="{{text}}",Value="a\"b\nc"});Check(Templates.Render(json)=="{\"task\":\"a\\\"b\\nc\"}","JSON sin escape");
   var clone=db.Entries[0].Clone();clone.Set("prompt","copia");Check(clone.Id!=db.Entries[0].Id&&db.Entries[0].Get("prompt")!="copia"&&clone.Hotkey=="","Duplicación comparte identidad o datos");
   uint mods,key;Check(MainWindow.ParseKey("Ctrl+Alt+NumPad0",out mods,out key)&&mods==3&&key==96,"Atajo NumPad0 inválido");Check(!MainWindow.ParseKey("Ctrl+Alt+Delete",out mods,out key),"Atajo inválido aceptado");
   db.Entries[0].Set("prompt",new string('x',32768));bool failed=false;try{Excel.Write(exported,db);}catch{failed=true;}Check(failed,"No se detectó límite de Excel");Check(Excel.Read(exported)[0].Rows.Count==19,"Falló preservación del Excel anterior");
   db.Fields.Add(new Field{Id="custom_upgrade",Title="Campo personal"});db.ColumnOrder.Add("custom_upgrade");db.CategoryOrder.Add("Personales::Mi categoría");db.DisabledChoices.Add("model::Mi modelo");db.Models.Add("Mi modelo");db.Entries[0].Set("custom_upgrade","valor que se conserva");db.Entries[0].Favorite=true;db.Entries[0].Hotkey="Alt+NumPad1";db.Save();reread.Load(db.Pathname);
   Check(reread.Fields.Any(f=>f.Id=="custom_upgrade")&&reread.ColumnOrder.Contains("custom_upgrade")&&reread.CategoryOrder.Contains("Personales::Mi categoría")&&reread.DisabledChoices.Contains("model::Mi modelo")&&reread.Models.Contains("Mi modelo")&&reread.Entries[0].Get("custom_upgrade")=="valor que se conserva"&&reread.Entries[0].Favorite&&reread.Entries[0].Hotkey=="Alt+NumPad1","Actualización pierde ajustes");
   File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"pruebas-ok.txt"),"OK: carga inicial, guardado, historial, Excel ida/vuelta, Unicode, variables, JSON, duplicación, parser de atajos, exportación fallida, recursos del logo y conservación de ajustes al actualizar.\r\n"+DateTime.Now);
  }finally{Directory.Delete(dir,true);}
 }
}
}
