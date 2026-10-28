using System;
using System.Linq;
using System.Collections.Generic;
using System.Xml.Linq;
namespace PromptDesk {
public class UndoHistory {
 List<string> past=new List<string>(),future=new List<string>();string current,group="";DateTime time=DateTime.MinValue;
 public UndoHistory(Store db){current=Snapshot(db);}
 string Snapshot(Store db){var x=db.Document();foreach(var r in x.Descendants("record"))r.SetAttributeValue("lastUsed","");return x.ToString(SaveOptions.DisableFormatting);}
 public void Record(Store db,string key){string next=Snapshot(db);if(next==current)return;bool merge=key.Length>0&&key==group&&(DateTime.UtcNow-time).TotalMilliseconds<1000;if(!merge){past.Add(current);if(past.Count>150)past.RemoveAt(0);}current=next;group=key;time=DateTime.UtcNow;future.Clear();}
 public void UpdateBaseline(Store db){current=Snapshot(db);group="";}
 public bool CanUndo{get{return past.Count>0;}} public bool CanRedo{get{return future.Count>0;}}
 public bool Undo(Store db){if(!CanUndo)return false;future.Add(current);current=past[past.Count-1];past.RemoveAt(past.Count-1);Apply(db,current);group="";return true;}
 public bool Redo(Store db){if(!CanRedo)return false;past.Add(current);current=future[future.Count-1];future.RemoveAt(future.Count-1);Apply(db,current);group="";return true;}
 void Apply(Store db,string state){var usage=db.Entries.ToDictionary(e=>e.Id,e=>e.LastUsed);db.ReadDocument(XDocument.Parse(state));foreach(var e in db.Entries)if(usage.ContainsKey(e.Id))e.LastUsed=usage[e.Id];}
}
}
