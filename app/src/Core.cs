using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using System.IO.Compression;

namespace PromptDesk {
public class Field { public string Id, Title; public bool Visible=true; }
public class Variable { public string Token="", Label="", Value=""; }
public class Entry {
 public string Id=Guid.NewGuid().ToString(), Hotkey="", LastUsed="";
 public bool Favorite;
 public Dictionary<string,string> Cells=new Dictionary<string,string>();
 public List<Variable> Variables=new List<Variable>();
 public string Get(string k) { return Cells.ContainsKey(k)?Cells[k]:""; }
 public void Set(string k,string v) { Cells[k]=v??""; }
 public XElement Xml() { return new XElement("record",new XAttribute("id",Id),new XAttribute("favorite",Favorite),new XAttribute("hotkey",Hotkey),new XAttribute("lastUsed",LastUsed),Cells.Select(c=>new XElement("cell",new XAttribute("key",c.Key),c.Value)),new XElement("variables",Variables.Select(v=>new XElement("var",new XAttribute("token",v.Token),new XAttribute("label",v.Label),new XAttribute("value",v.Value))))); }
 public static Entry Read(XElement x) { var e=new Entry(); e.Id=(string)x.Attribute("id")??e.Id; e.Favorite=(string)x.Attribute("favorite")=="true"; e.Hotkey=(string)x.Attribute("hotkey")??""; e.LastUsed=(string)x.Attribute("lastUsed")??""; foreach(var c in x.Elements("cell"))e.Set((string)c.Attribute("key"),c.Value); var vs=x.Element("variables"); if(vs!=null)foreach(var v in vs.Elements("var"))e.Variables.Add(new Variable{Token=(string)v.Attribute("token")??"",Label=(string)v.Attribute("label")??"",Value=(string)v.Attribute("value")??""});return e; }
 public Entry Clone() { var e=Read(Xml());e.Id=Guid.NewGuid().ToString();e.Hotkey="";e.LastUsed="";return e; }
}
public class Store {
 public List<Field> Fields=new List<Field>(); public List<Entry> Entries=new List<Entry>(); public List<string> Categories=new List<string>(); public List<string> Models=new List<string>(); public List<string> ColumnOrder=new List<string>(); public List<string> CategoryOrder=new List<string>(); public List<string> DisabledChoices=new List<string>();
 public string Folder, Pathname;
 public Store(string folder) {Folder=folder;Pathname=Path.Combine(folder,"datos.xml");Directory.CreateDirectory(folder);Directory.CreateDirectory(Path.Combine(folder,"Historial"));}
 public void Load(string file) {
  ReadDocument(XDocument.Load(file));
 }
 public void ReadDocument(XDocument d){if(d.Root==null || d.Root.Name!="promptdesk" || (string)d.Root.Attribute("version")!="1")throw new Exception("El archivo no es un respaldo compatible de PromptDesk.");
  var fs=d.Root.Element("columns");var rs=d.Root.Element("records");if(fs==null||rs==null)throw new Exception("Respaldo incompleto.");
  var fields=fs.Elements("col").Select(x=>new Field{Id=(string)x.Attribute("id"),Title=(string)x.Attribute("title"),Visible=(string)x.Attribute("visible")!="false"}).ToList();
  if(fields.Any(f=>String.IsNullOrEmpty(f.Id)||String.IsNullOrEmpty(f.Title))||fields.Select(f=>f.Id).Distinct().Count()!=fields.Count)throw new Exception("Columnas inválidas.");
  foreach(var id in new[]{"collection","category","name","description","model","prompt","note"})if(!fields.Any(f=>f.Id==id))throw new Exception("Falta la columna "+id);
  var entries=rs.Elements("record").Select(Entry.Read).ToList();if(entries.Select(e=>e.Id).Distinct().Count()!=entries.Count)throw new Exception("Identificadores repetidos.");
  var co=d.Root.Element("categoryOrder");CategoryOrder=co==null?new List<string>():co.Elements("category").Select(x=>x.Value).Distinct().ToList();var disabled=d.Root.Element("disabledChoices");DisabledChoices=disabled==null?new List<string>():disabled.Elements("choice").Select(x=>x.Value).Distinct().ToList();
  var ms=d.Root.Element("models");Models=ms==null?new List<string>():ms.Elements("model").Select(x=>x.Value).Distinct().ToList();var order=d.Root.Element("columnOrder");ColumnOrder=order==null?new List<string>():order.Elements("column").Select(x=>x.Value).Distinct().ToList();
  var cats=d.Root.Element("categories");Fields=fields;Entries=entries;Categories=cats==null?new List<string>():cats.Elements("category").Select(x=>x.Value).Distinct().ToList();
 }
 public XDocument Document() {return new XDocument(new XElement("promptdesk",new XAttribute("version","1"),new XElement("columns",Fields.Select(f=>new XElement("col",new XAttribute("id",f.Id),new XAttribute("title",f.Title),new XAttribute("visible",f.Visible)))),new XElement("categories",Categories.Select(c=>new XElement("category",c))),new XElement("models",Models.Select(m=>new XElement("model",m))),new XElement("columnOrder",ColumnOrder.Select(c=>new XElement("column",c))),new XElement("categoryOrder",CategoryOrder.Select(c=>new XElement("category",c))),new XElement("disabledChoices",DisabledChoices.Select(c=>new XElement("choice",c))),new XElement("records",Entries.Select(e=>e.Xml()))));}
 public void Save() {
  string temp=Pathname+".tmp",hist=Path.Combine(Folder,"Historial",DateTime.UtcNow.ToString("yyyyMMdd-HHmmss-fffffff")+".xml");
  using(var stream=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){Document().Save(stream);stream.Flush(true);}
  if(File.Exists(Pathname)) File.Replace(temp,Pathname,hist); else File.Move(temp,Pathname);
  // Keep the latest 200 recoverable snapshots. No cleanup of user exports.
  foreach(var file in new DirectoryInfo(Path.Combine(Folder,"Historial")).GetFiles("*.xml").OrderByDescending(f=>f.Name).Skip(200))try{file.Delete();}catch{}
 }
}
public static class Templates {
 public static string Render(Entry e) {
  string source=e.Get("prompt");var vars=e.Variables.Where(v=>v.Token.Length>0).OrderByDescending(v=>v.Token.Length).ToList();
  if(vars.Select(v=>v.Token).Distinct().Count()!=vars.Count)throw new Exception("Hay variables repetidas.");
  if(vars.Count==0)return source;
  string pattern=String.Join("|",vars.Select(v=>Pattern(v.Token)).ToArray());
  // One pass: a replacement value is never interpreted as another variable.
  return Regex.Replace(source,pattern,m=>{
   var v=vars.First(a=>a.Token==m.Value);string value=v.Value;
   if(source.TrimStart().StartsWith("{")) value=value.Replace("\\","\\\\").Replace("\"","\\\"").Replace("\r","\\r").Replace("\n","\\n").Replace("\t","\\t");
   return value;
  });
 }
 public static string Pattern(string token) {return (Regex.IsMatch(token.Substring(0,1),@"\w")?@"(?<!\w)":"")+Regex.Escape(token)+(Regex.IsMatch(token.Substring(token.Length-1),@"\w")?@"(?!\w)":"");}
}
public class SheetData { public string Name; public List<List<string>> Rows=new List<List<string>>(); }
public static class Excel {
 static XNamespace N="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
 static XDocument ReadXml(ZipArchive zip,string name){var en=zip.GetEntry(name);if(en==null)throw new Exception("No se encuentra "+name);if(en.Length>40*1024*1024)throw new Exception("Hoja demasiado grande.");using(var s=en.Open())using(var xr=XmlReader.Create(s,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))return XDocument.Load(xr);}
 public static List<SheetData> Read(string path) {
  using(var zip=ZipFile.OpenRead(path)){
   var strings=new List<string>();if(zip.GetEntry("xl/sharedStrings.xml")!=null)strings=ReadXml(zip,"xl/sharedStrings.xml").Descendants(N+"si").Select(x=>String.Concat(x.Descendants(N+"t").Select(t=>t.Value))).ToList();
   XNamespace rel="http://schemas.openxmlformats.org/package/2006/relationships",rid="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
   var relationships=ReadXml(zip,"xl/_rels/workbook.xml.rels").Descendants(rel+"Relationship").ToDictionary(x=>(string)x.Attribute("Id"),x=>(string)x.Attribute("Target"));
   var sheets=new List<SheetData>();foreach(var sheet in ReadXml(zip,"xl/workbook.xml").Descendants(N+"sheet")) {
    string target=relationships[(string)sheet.Attribute(rid+"id")];target=target.StartsWith("/")?target.TrimStart('/'):"xl/"+target;
    var data=new SheetData{Name=(string)sheet.Attribute("name")};
    foreach(var row in ReadXml(zip,target).Descendants(N+"sheetData").Elements(N+"row")){
     var cells=new List<string>();foreach(var c in row.Elements(N+"c")){
      string addr=(string)c.Attribute("r")??"A1";int col=0;foreach(char ch in addr.TakeWhile(Char.IsLetter))col=col*26+(Char.ToUpperInvariant(ch)-'A'+1);
      if(col>256)throw new Exception("La importación admite hasta 256 columnas.");while(cells.Count<col)cells.Add("");
      string value=(string)c.Element(N+"v")??"",type=(string)c.Attribute("t");
      if(type=="s")value=strings[Int32.Parse(value)];else if(type=="inlineStr")value=String.Concat(c.Descendants(N+"t").Select(t=>t.Value));
      if(col>0)cells[col-1]=value;
     }data.Rows.Add(cells);if(data.Rows.Count>10001)throw new Exception("La importación admite hasta 10.000 prompts por hoja.");
    }sheets.Add(data);
   }return sheets;
  }
 }
 static void Put(ZipArchive z,string name,XDocument d){using(var s=z.CreateEntry(name).Open())d.Save(s);}
 static string Col(int i){string s="";for(i++;i>0;i=(i-1)/26)s=(char)('A'+(i-1)%26)+s;return s;}
 public static void Write(string path,Store store) {
  // Build a complete file first so an export failure cannot truncate an existing file.
  string temp=path+".tmp";if(File.Exists(temp))File.Delete(temp);
  try {using(var z=ZipFile.Open(temp,ZipArchiveMode.Create)){
   XNamespace ct="http://schemas.openxmlformats.org/package/2006/content-types",r="http://schemas.openxmlformats.org/package/2006/relationships",rid="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
   Put(z,"[Content_Types].xml",new XDocument(new XElement(ct+"Types",new XElement(ct+"Default",new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),new XElement(ct+"Default",new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")),new XElement(ct+"Override",new XAttribute("PartName","/xl/workbook.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),new XElement(ct+"Override",new XAttribute("PartName","/xl/worksheets/sheet1.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
   Put(z,"_rels/.rels",new XDocument(new XElement(r+"Relationships",new XElement(r+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type",rid.NamespaceName+"/officeDocument"),new XAttribute("Target","xl/workbook.xml")))));
   Put(z,"xl/workbook.xml",new XDocument(new XElement(N+"workbook",new XElement(N+"sheets",new XElement(N+"sheet",new XAttribute("name","Prompts"),new XAttribute("sheetId","1"),new XAttribute(rid+"id","rId1"))))));
   Put(z,"xl/_rels/workbook.xml.rels",new XDocument(new XElement(r+"Relationships",new XElement(r+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type",rid.NamespaceName+"/worksheet"),new XAttribute("Target","worksheets/sheet1.xml")))));
   var rows=new List<List<string>>{store.Fields.Select(f=>f.Title).Concat(new[]{"Favorito","Atajo"}).ToList()};rows.AddRange(store.Entries.Select(e=>store.Fields.Select(f=>e.Get(f.Id)).Concat(new[]{e.Favorite?"Sí":"No",e.Hotkey}).ToList()));
   var sd=new XElement(N+"sheetData");for(int i=0;i<rows.Count;i++){var row=new XElement(N+"row",new XAttribute("r",i+1));for(int j=0;j<rows[i].Count;j++){if(rows[i][j].Length>32767)throw new Exception("Excel limita las celdas a 32.767 caracteres. Usá el respaldo completo para este catálogo.");row.Add(new XElement(N+"c",new XAttribute("r",Col(j)+(i+1)),new XAttribute("t","inlineStr"),new XElement(N+"is",new XElement(N+"t",new XAttribute(XNamespace.Xml+"space","preserve"),rows[i][j]))));}sd.Add(row);}
   Put(z,"xl/worksheets/sheet1.xml",new XDocument(new XElement(N+"worksheet",sd)));
  }if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
  }finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
}
