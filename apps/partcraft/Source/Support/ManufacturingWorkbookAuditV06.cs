using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Xml;
using System.Collections.Generic;
public static class ManufacturingWorkbookAuditV06 {
    static XmlDocument Xml(ZipArchive z,string path){var e=z.GetEntry(path);if(e==null)throw new InvalidOperationException("Workbook part missing: "+path);var d=new XmlDocument();d.XmlResolver=null;using(var s=e.Open())using(var reader=XmlReader.Create(s,new XmlReaderSettings{DtdProcessing=DtdProcessing.Prohibit,XmlResolver=null}))d.Load(reader);return d;}
    static string Value(XmlElement row,string column){foreach(XmlElement c in row.ChildNodes)if(c.Name=="c"&&c.GetAttribute("r").StartsWith(column,StringComparison.Ordinal)&&c.GetAttribute("r").Skip(column.Length).All(Char.IsDigit)){var ns=new XmlNamespaceManager(c.OwnerDocument.NameTable);ns.AddNamespace("s",c.NamespaceURI);var t=c.SelectSingleNode("s:is/s:t",ns)??c.SelectSingleNode("s:v",ns);return t==null?null:t.InnerText;}return null;}
    public static object Verify(string path,List<MPRow> expected){
        if(expected.Select(r=>r.body).Distinct(StringComparer.Ordinal).Count()!=expected.Count)throw new InvalidOperationException("Duplicate expected body names");
        var found=new Dictionary<string,int>(StringComparer.Ordinal);
        using(var f=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))using(var z=new ZipArchive(f,ZipArchiveMode.Read)){
            var book=Xml(z,"xl/workbook.xml");var ns=new XmlNamespaceManager(book.NameTable);ns.AddNamespace("s",book.DocumentElement.NamespaceURI);
            var names=book.SelectNodes("//s:sheet",ns).Cast<XmlElement>().Select(x=>x.GetAttribute("name")).ToArray();
            if(!names.SequenceEqual(new[]{"TUBE CUTTING","SHEET METAL","PLATES","CHECKS"}))throw new InvalidOperationException("Workbook sheet list differs");
            for(int i=1;i<=3;i++){var sheet=Xml(z,"xl/worksheets/sheet"+i+".xml");var sn=new XmlNamespaceManager(sheet.NameTable);sn.AddNamespace("s",sheet.DocumentElement.NamespaceURI);
                foreach(XmlElement row in sheet.SelectNodes("//s:sheetData/s:row",sn)){
                    if(row.GetAttribute("r")=="1")continue;string name=Value(row,"B"),folder=Value(row,"A"),quantity=Value(row,"C"),groupQuantity=Value(row,"D");
                    var body=expected.SingleOrDefault(r=>r.body==name);if(body==null||found.ContainsKey(name)||body.cutList!=folder)throw new InvalidOperationException("Missing/duplicate/unknown workbook body or Cut List: "+name);
                    int q;if(!Int32.TryParse(quantity,out q)||q!=1)throw new InvalidOperationException("Incorrect physical body quantity: "+name);
                    if(body.folderPhysicalCount.HasValue&&groupQuantity!=body.folderPhysicalCount.Value.ToString())throw new InvalidOperationException("Folder physical quantity differs: "+name);
                    string expectedSheet=(body.kind=="TUBE"||body.kind=="RECTANGULAR PROFILE")?"TUBE CUTTING":body.kind;if(expectedSheet!=names[i-1])throw new InvalidOperationException("Body listed on wrong manufacturing sheet: "+name);found.Add(name,q);
                }
            }
        }
        if(found.Count!=expected.Count||found.Values.Sum()!=expected.Count)throw new InvalidOperationException("Workbook body coverage incomplete");
        return new{status="PASS_INVENTORY_AUDIT_ONLY",workbookExists=true,zipXmlReadable=true,expectedBodies=expected.Count,listedBodies=found.Count,totalPhysicalQuantity=found.Values.Sum(),noDuplicates=true,allNamesAndFoldersMatched=true,productionCertified=false};
    }
}
