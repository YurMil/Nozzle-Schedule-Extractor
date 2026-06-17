using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace NozzleScheduleExtractor
{
    internal static class XlsxWriter
    {
        public static void Write(List<NozzleRow> nozzleRows, string path)
        {
            var rows = new List<string[]>();
            rows.Add(NozzleColumns.Headers);

            // Cells (sheet-row,col, both 0-based in `rows`) carrying a Warning, for highlighting.
            var flagged = new HashSet<string>();
            for (int k = 0; k < nozzleRows.Count; k++)
            {
                rows.Add(TsvWriter.ToCells(nozzleRows[k]));
                foreach (Diagnostic d in nozzleRows[k].Diagnostics)
                {
                    if (d.Severity != Severity.Warning) continue;
                    int col = NozzleColumns.ColumnOf(d.Field);
                    if (col >= 0) flagged.Add((k + 1) + "," + col);
                }
            }

            if (File.Exists(path)) File.Delete(path);
            using (var archive = ZipFile.Open(path, ZipArchiveMode.Create))
            {
                AddEntry(archive, "[Content_Types].xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Types xmlns=""http://schemas.openxmlformats.org/package/2006/content-types"">
  <Default Extension=""rels"" ContentType=""application/vnd.openxmlformats-package.relationships+xml""/>
  <Default Extension=""xml"" ContentType=""application/xml""/>
  <Override PartName=""/xl/workbook.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml""/>
  <Override PartName=""/xl/worksheets/sheet1.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml""/>
  <Override PartName=""/xl/styles.xml"" ContentType=""application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml""/>
  <Override PartName=""/docProps/core.xml"" ContentType=""application/vnd.openxmlformats-package.core-properties+xml""/>
  <Override PartName=""/docProps/app.xml"" ContentType=""application/vnd.openxmlformats-officedocument.extended-properties+xml""/>
</Types>");
                AddEntry(archive, "_rels/.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument"" Target=""xl/workbook.xml""/>
  <Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties"" Target=""docProps/core.xml""/>
  <Relationship Id=""rId3"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties"" Target=""docProps/app.xml""/>
</Relationships>");
                AddEntry(archive, "xl/_rels/workbook.xml.rels", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Relationships xmlns=""http://schemas.openxmlformats.org/package/2006/relationships"">
  <Relationship Id=""rId1"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet"" Target=""worksheets/sheet1.xml""/>
  <Relationship Id=""rId2"" Type=""http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles"" Target=""styles.xml""/>
</Relationships>");
                AddEntry(archive, "xl/workbook.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<workbook xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"" xmlns:r=""http://schemas.openxmlformats.org/officeDocument/2006/relationships"">
  <sheets><sheet name=""Nozzle Schedule"" sheetId=""1"" r:id=""rId1""/></sheets>
</workbook>");
                AddEntry(archive, "xl/styles.xml", StylesXml());
                AddEntry(archive, "xl/worksheets/sheet1.xml", SheetXml(rows, flagged));
                AddEntry(archive, "docProps/core.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<cp:coreProperties xmlns:cp=""http://schemas.openxmlformats.org/package/2006/metadata/core-properties"" xmlns:dc=""http://purl.org/dc/elements/1.1/""><dc:creator>NozzleScheduleExtractor</dc:creator></cp:coreProperties>");
                AddEntry(archive, "docProps/app.xml", @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<Properties xmlns=""http://schemas.openxmlformats.org/officeDocument/2006/extended-properties""><Application>NozzleScheduleExtractor</Application></Properties>");
            }
        }

        private static string SheetXml(List<string[]> rows, HashSet<string> flagged)
        {
            var sb = new StringBuilder();
            sb.Append(@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?><worksheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">");
            sb.Append("<sheetViews><sheetView workbookViewId=\"0\"><pane ySplit=\"1\" topLeftCell=\"A2\" activePane=\"bottomLeft\" state=\"frozen\"/></sheetView></sheetViews>");
            sb.Append("<cols>");
            int[] widths = { 10, 24, 18, 12, 16, 26, 12, 22, 10, 10, 10, 11, 11, 11 };
            for (int i = 0; i < widths.Length; i++)
                sb.AppendFormat(CultureInfo.InvariantCulture, "<col min=\"{0}\" max=\"{0}\" width=\"{1}\" customWidth=\"1\"/>", i + 1, widths[i]);
            sb.Append("</cols><sheetData>");
            for (int r = 0; r < rows.Count; r++)
            {
                sb.AppendFormat("<row r=\"{0}\">", r + 1);
                for (int c = 0; c < rows[r].Length; c++)
                {
                    string cellRef = TextUtil.ColumnName(c + 1) + (r + 1).ToString(CultureInfo.InvariantCulture);
                    string style = r == 0 ? "1" : (flagged.Contains(r + "," + c) ? "3" : "2");
                    sb.AppendFormat("<c r=\"{0}\" t=\"inlineStr\" s=\"{1}\"><is><t>{2}</t></is></c>", cellRef, style, TextUtil.Xml(rows[r][c]));
                }
                sb.Append("</row>");
            }
            sb.Append("</sheetData><autoFilter ref=\"A1:N" + rows.Count.ToString(CultureInfo.InvariantCulture) + "\"/></worksheet>");
            return sb.ToString();
        }

        private static string StylesXml()
        {
            return @"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""yes""?>
<styleSheet xmlns=""http://schemas.openxmlformats.org/spreadsheetml/2006/main"">
  <fonts count=""2""><font><sz val=""11""/><name val=""Calibri""/></font><font><b/><sz val=""11""/><name val=""Calibri""/></font></fonts>
  <fills count=""4""><fill><patternFill patternType=""none""/></fill><fill><patternFill patternType=""gray125""/></fill><fill><patternFill patternType=""solid""><fgColor rgb=""FFD9EAF7""/><bgColor indexed=""64""/></patternFill></fill><fill><patternFill patternType=""solid""><fgColor rgb=""FFFFF2CC""/><bgColor indexed=""64""/></patternFill></fill></fills>
  <borders count=""2""><border><left/><right/><top/><bottom/><diagonal/></border><border><left style=""thin""/><right style=""thin""/><top style=""thin""/><bottom style=""thin""/><diagonal/></border></borders>
  <cellStyleXfs count=""1""><xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0""/></cellStyleXfs>
  <cellXfs count=""4"">
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""0"" xfId=""0""/>
    <xf numFmtId=""0"" fontId=""1"" fillId=""2"" borderId=""1"" xfId=""0"" applyFont=""1"" applyFill=""1"" applyBorder=""1"" applyAlignment=""1""><alignment horizontal=""center"" vertical=""center"" wrapText=""1""/></xf>
    <xf numFmtId=""0"" fontId=""0"" fillId=""0"" borderId=""1"" xfId=""0"" applyBorder=""1"" applyAlignment=""1""><alignment horizontal=""center"" vertical=""center"" wrapText=""1""/></xf>
    <xf numFmtId=""0"" fontId=""0"" fillId=""3"" borderId=""1"" xfId=""0"" applyFill=""1"" applyBorder=""1"" applyAlignment=""1""><alignment horizontal=""center"" vertical=""center"" wrapText=""1""/></xf>
  </cellXfs>
  <cellStyles count=""1""><cellStyle name=""Normal"" xfId=""0"" builtinId=""0""/></cellStyles>
  <dxfs count=""0""/>
  <tableStyles count=""0"" defaultTableStyle=""TableStyleMedium2"" defaultPivotStyle=""PivotStyleLight16""/>
</styleSheet>";
        }

        private static void AddEntry(ZipArchive archive, string name, string content)
        {
            var entry = archive.CreateEntry(name, CompressionLevel.Optimal);
            using (var writer = new StreamWriter(entry.Open(), new UTF8Encoding(false)))
                writer.Write(content);
        }
    }
}
