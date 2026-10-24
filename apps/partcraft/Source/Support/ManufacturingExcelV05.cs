using System;
using System.IO;
using System.IO.Compression;
using System.Xml;
using System.Text;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
public static class ManufacturingExcelV05 {
    const string Ns="http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    static string Column(int n){string s="";while(n>0){n--;s=(char)('A'+n%26)+s;n/=26;}return s;}
    static void Entry(ZipArchive z,string name,Action<XmlWriter> write){using(var stream=z.CreateEntry(name).Open())using(var x=XmlWriter.Create(stream,new XmlWriterSettings{Encoding=new UTF8Encoding(false),CloseOutput=false})){x.WriteStartDocument();write(x);x.WriteEndDocument();}}
    static void Raw(ZipArchive z,string name,string xml){using(var s=z.CreateEntry(name).Open())using(var w=new StreamWriter(s,new UTF8Encoding(false)))w.Write(xml);}
    public static void Write(string path,Dictionary<string,List<object[]>> tables){
        using(var file=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var z=new ZipArchive(file,ZipArchiveMode.Create)){
            string content="<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\"><Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/><Default Extension=\"xml\" ContentType=\"application/xml\"/><Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/><Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>";
            for(int i=1;i<=tables.Count;i++)content+="<Override PartName=\"/xl/worksheets/sheet"+i+".xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>";
            Raw(z,"[Content_Types].xml",content+"</Types>");
            Raw(z,"_rels/.rels","<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\"><Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/></Relationships>");
            string rel="<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">";for(int i=1;i<=tables.Count;i++)rel+="<Relationship Id=\"rId"+i+"\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet"+i+".xml\"/>";
            Raw(z,"xl/_rels/workbook.xml.rels",rel+"<Relationship Id=\"styles\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/></Relationships>");
            Entry(z,"xl/workbook.xml",x=>{x.WriteStartElement("workbook",Ns);x.WriteStartElement("sheets");int i=0;foreach(var t in tables){i++;x.WriteStartElement("sheet");x.WriteAttributeString("name",t.Key);x.WriteAttributeString("sheetId",i.ToString());x.WriteAttributeString("r","id","http://schemas.openxmlformats.org/officeDocument/2006/relationships","rId"+i);x.WriteEndElement();}x.WriteEndElement();x.WriteEndElement();});
            Raw(z,"xl/styles.xml","<styleSheet xmlns=\""+Ns+"\"><numFmts count=\"1\"><numFmt numFmtId=\"164\" formatCode=\"0.000\"/></numFmts><fonts count=\"2\"><font><sz val=\"11\"/><name val=\"Calibri\"/></font><font><b/><sz val=\"11\"/><name val=\"Calibri\"/></font></fonts><fills count=\"3\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill><fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFC2D9EA\"/><bgColor indexed=\"64\"/></patternFill></fill></fills><borders count=\"1\"><border/></borders><cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs><cellXfs count=\"3\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment wrapText=\"1\" vertical=\"top\"/></xf><xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"0\" xfId=\"0\" applyAlignment=\"1\"><alignment wrapText=\"1\"/></xf><xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyNumberFormat=\"1\"/></cellXfs><cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles></styleSheet>");
            int index=0;foreach(var table in tables){index++;var data=table.Value;Entry(z,"xl/worksheets/sheet"+index+".xml",x=>{
                int cols=data.Max(r=>r.Length);x.WriteStartElement("worksheet",Ns);x.WriteStartElement("sheetViews");x.WriteStartElement("sheetView");x.WriteAttributeString("workbookViewId","0");x.WriteStartElement("pane");x.WriteAttributeString("ySplit","1");x.WriteAttributeString("topLeftCell","A2");x.WriteAttributeString("activePane","bottomLeft");x.WriteAttributeString("state","frozen");x.WriteEndElement();x.WriteEndElement();x.WriteEndElement();
                x.WriteStartElement("cols");for(int c=1;c<=cols;c++){x.WriteStartElement("col");x.WriteAttributeString("min",c.ToString());x.WriteAttributeString("max",c.ToString());x.WriteAttributeString("width",c==1?"16":c==cols?"65":"23");x.WriteAttributeString("customWidth","1");x.WriteEndElement();}x.WriteEndElement();
                x.WriteStartElement("sheetData");for(int r=0;r<data.Count;r++){x.WriteStartElement("row");x.WriteAttributeString("r",(r+1).ToString());x.WriteAttributeString("ht",r==0?"48":"45");x.WriteAttributeString("customHeight","1");for(int c=0;c<data[r].Length;c++){
                    object v=data[r][c]??"НЕ ПРОВЕРЕНО";bool numeric=v is double||v is float||v is decimal||v is int||v is long;
                    if(numeric&&!NV.Finite(Convert.ToDouble(v)))throw new InvalidOperationException("Nonfinite workbook value");
                    x.WriteStartElement("c");x.WriteAttributeString("r",Column(c+1)+(r+1));x.WriteAttributeString("s",r==0?"1":numeric&&(v is double||v is float||v is decimal)?"2":"0");
                    if(numeric)x.WriteElementString("v",Convert.ToString(v,CultureInfo.InvariantCulture));else{x.WriteAttributeString("t","inlineStr");x.WriteStartElement("is");x.WriteStartElement("t");x.WriteAttributeString("xml","space","http://www.w3.org/XML/1998/namespace","preserve");x.WriteString(v.ToString());x.WriteEndElement();x.WriteEndElement();}x.WriteEndElement();
                }x.WriteEndElement();}x.WriteEndElement();x.WriteStartElement("autoFilter");x.WriteAttributeString("ref","A1:"+Column(cols)+data.Count);x.WriteEndElement();x.WriteStartElement("pageSetup");x.WriteAttributeString("orientation","landscape");x.WriteAttributeString("fitToWidth","1");x.WriteAttributeString("fitToHeight","0");x.WriteEndElement();x.WriteEndElement();
            });}
        }
    }
}
