using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using SolidWorks.Interop.sldworks;
using SolidWorks.Interop.swconst;

namespace NozzleScheduleExtractor
{
    internal static class SolidWorksTableInserter
    {
        public static string InsertIntoActiveDrawing(List<NozzleRow> rows)
        {
            if (rows == null || rows.Count == 0)
                throw new InvalidOperationException("No nozzle schedule rows. Run extraction first.");

            ISldWorks sw = (ISldWorks)Marshal.GetActiveObject("SldWorks.Application");
            IModelDoc2 model = sw.ActiveDoc as IModelDoc2;
            if (model == null)
                throw new InvalidOperationException("SOLIDWORKS has no active document.");

            if (model.GetType() != (int)swDocumentTypes_e.swDocDRAWING)
                throw new InvalidOperationException("Active SOLIDWORKS document is not a drawing.");

            IDrawingDoc drawing = model as IDrawingDoc;
            if (drawing == null)
                throw new InvalidOperationException("Active document cannot be accessed as drawing.");

            string title = model.GetTitle();
            string path = model.GetPathName();
            bool wasDirty = model.GetSaveFlag();

            ISheet sheet = drawing.GetCurrentSheet() as ISheet;
            string sheetName = sheet != null ? sheet.GetName() : "";

            int rowCount = rows.Count + 1;
            int colCount = NozzleColumns.Headers.Length;

            IModelDocExtension ext = model.Extension;
            TableAnnotation table = ext.InsertGeneralTableAnnotation(
                false,
                0.02,
                0.26,
                (int)swTableHeaderPosition_e.swTableHeader_Top,
                "",
                rowCount,
                colCount);

            if (table == null)
                throw new InvalidOperationException("SOLIDWORKS did not create the general table.");

            FillTable(table, rows);

            string dirtyText = wasDirty ? "document was already modified" : "document is now modified";
            return "Inserted " + rows.Count + " rows into active drawing '" + title + "'" +
                   (String.IsNullOrWhiteSpace(sheetName) ? "" : ", sheet '" + sheetName + "'") +
                   (String.IsNullOrWhiteSpace(path) ? "" : ", file '" + path + "'") +
                   ". " + dirtyText + ".";
        }

        private static void FillTable(ITableAnnotation table, List<NozzleRow> rows)
        {
            for (int c = 0; c < NozzleColumns.Headers.Length; c++)
                table.set_Text2(0, c, false, NozzleColumns.Headers[c]);

            for (int r = 0; r < rows.Count; r++)
            {
                string[] cells = TsvWriter.ToCells(rows[r]);
                for (int c = 0; c < cells.Length; c++)
                    table.set_Text2(r + 1, c, false, cells[c]);
            }

            double[] widths =
            {
                0.018, 0.052, 0.032, 0.024, 0.030, 0.050, 0.024,
                0.034, 0.022, 0.022, 0.022, 0.024, 0.024, 0.024
            };

            for (int c = 0; c < widths.Length; c++)
            {
                try { table.SetColumnWidth(c, widths[c], (int)swTableRowColSizeChangeBehavior_e.swTableRowColChange_TableSizeCanChange); }
                catch { }
            }

            for (int r = 0; r < rows.Count + 1; r++)
            {
                try { table.SetRowHeight(r, 0.007, (int)swTableRowColSizeChangeBehavior_e.swTableRowColChange_TableSizeCanChange); }
                catch { }
            }

            try
            {
                table.TextHorizontalJustification = (int)swTextJustification_e.swTextJustificationCenter;
                table.TextVerticalJustification = (int)swVerticalJustification_e.swVerticalJustificationMiddle;
            }
            catch { }
        }
    }
}
