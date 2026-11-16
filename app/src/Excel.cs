using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Xml;
using System.Xml.Linq;
using System.IO.Compression;
using System.Text;

namespace PromptDesk {
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
 static void WriteRow(XmlWriter writer, IEnumerable<string> values, int number)
 {
  writer.WriteStartElement("row", N.NamespaceName);
  writer.WriteAttributeString("r", number.ToString());
  int column = 0;
  foreach (string text in values)
  {
   if (text.Length > 32767)
    throw new Exception("Excel limita las celdas a 32.767 caracteres. Usá el respaldo completo para este catálogo.");
   writer.WriteStartElement("c", N.NamespaceName);
   writer.WriteAttributeString("r", Col(column++) + number);
   writer.WriteAttributeString("t", "inlineStr");
   writer.WriteStartElement("is", N.NamespaceName);
   writer.WriteStartElement("t", N.NamespaceName);
   writer.WriteAttributeString("xml", "space", XNamespace.Xml.NamespaceName, "preserve");
   writer.WriteString(text);
   writer.WriteEndElement();
   writer.WriteEndElement();
   writer.WriteEndElement();
  }
  writer.WriteEndElement();
 }
 static void WriteWorksheet(ZipArchive archive, Store store)
 {
  // The worksheet is streamed directly into ZIP. Prompt text is never copied
  // into a second complete list and XML tree before exporting a large library.
  using (Stream stream = archive.CreateEntry("xl/worksheets/sheet1.xml").Open())
  using (XmlWriter writer = XmlWriter.Create(stream, new XmlWriterSettings
   { Encoding = new UTF8Encoding(false), NewLineHandling = NewLineHandling.Entitize }))
  {
   writer.WriteStartDocument();
   writer.WriteStartElement("worksheet", N.NamespaceName);
   writer.WriteStartElement("sheetData", N.NamespaceName);
   WriteRow(writer, store.Fields.Select(field => field.Title).Concat(new[] { "Favorito", "Atajo" }), 1);
   int number = 2;
   foreach (Entry entry in store.Entries)
    WriteRow(writer, store.Fields.Select(field => entry.Get(field.Id))
     .Concat(new[] { entry.Favorite ? "Sí" : "No", entry.Hotkey }), number++);
   writer.WriteEndElement();
   writer.WriteEndElement();
   writer.WriteEndDocument();
  }
 }
 public static void Write(string path,Store store) {
  // Build a complete file first so an export failure cannot truncate an existing file.
  string temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";
  try {using(var z=ZipFile.Open(temp,ZipArchiveMode.Create)){
   XNamespace ct="http://schemas.openxmlformats.org/package/2006/content-types",r="http://schemas.openxmlformats.org/package/2006/relationships",rid="http://schemas.openxmlformats.org/officeDocument/2006/relationships";
   Put(z,"[Content_Types].xml",new XDocument(new XElement(ct+"Types",new XElement(ct+"Default",new XAttribute("Extension","rels"),new XAttribute("ContentType","application/vnd.openxmlformats-package.relationships+xml")),new XElement(ct+"Default",new XAttribute("Extension","xml"),new XAttribute("ContentType","application/xml")),new XElement(ct+"Override",new XAttribute("PartName","/xl/workbook.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml")),new XElement(ct+"Override",new XAttribute("PartName","/xl/worksheets/sheet1.xml"),new XAttribute("ContentType","application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml")))));
   Put(z,"_rels/.rels",new XDocument(new XElement(r+"Relationships",new XElement(r+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type",rid.NamespaceName+"/officeDocument"),new XAttribute("Target","xl/workbook.xml")))));
   Put(z,"xl/workbook.xml",new XDocument(new XElement(N+"workbook",new XElement(N+"sheets",new XElement(N+"sheet",new XAttribute("name","Prompts"),new XAttribute("sheetId","1"),new XAttribute(rid+"id","rId1"))))));
   Put(z,"xl/_rels/workbook.xml.rels",new XDocument(new XElement(r+"Relationships",new XElement(r+"Relationship",new XAttribute("Id","rId1"),new XAttribute("Type",rid.NamespaceName+"/worksheet"),new XAttribute("Target","worksheets/sheet1.xml")))));
   WriteWorksheet(z,store);
  }if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
  }finally{if(File.Exists(temp))File.Delete(temp);}
 }
}
}
