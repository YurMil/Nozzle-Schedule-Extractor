using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace NozzleScheduleExtractor
{
    internal static class ParserRegressionTests
    {
        private static int _assertions;

        public static int Main(string[] args)
        {
            try
            {
                string fixtureRoot = args.Length > 0 ? args[0] : FindFixtureRoot();
                BomOverridesNozzleListAndCopyNotes(fixtureRoot);
                PadDoesNotOverwriteNozzleMaterial(fixtureRoot);
                DetailPageFillsFallbacksAndTableLoads(fixtureRoot);
                StructuredTableOverridesGarbledText(fixtureRoot);
                ValidatorFlagsConflictsAndBadGeometry(fixtureRoot);
                FallbackExtractorPrefersFirstUsableResult();
                Console.WriteLine("PASS: " + _assertions + " assertions");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL: " + ex.Message);
                return 1;
            }
        }

        private static void BomOverridesNozzleListAndCopyNotes(string fixtureRoot)
        {
            List<NozzleRow> rows = ParseFixture(fixtureRoot, "bom_with_copy_notes.txt");

            NozzleRow n1 = Row(rows, "N.1");
            Equal("N.1 description", "Vent Gas", n1.Description);
            Equal("N.1 type", "WN-FLG", n1.NozzleType);
            Equal("N.1 size", "DN50", n1.Size);
            Equal("N.1 class", "PN40", n1.PressureClass);
            Equal("N.1 pipe", "D60,3 x 3,91", n1.PipeDimension);
            Equal("N.1 material", "1.4301", n1.Material);
            Equal("N.1 standard", "EN1092", n1.Standard);
            Equal("N.1 Fx", "1,2", n1.Loads["Fx"]);
            Equal("N.1 Fy", "-4,4", n1.Loads["Fy"]);
            Equal("N.1 Fz", "-5", n1.Loads["Fz"]);
            Equal("N.1 Mx", "-3,75", n1.Loads["Mx"]);
            Equal("N.1 My", "2,5", n1.Loads["My"]);
            Equal("N.1 Mz", "0,5", n1.Loads["Mz"]);

            NozzleRow n4 = Row(rows, "N.4");
            Equal("N.4 copied type", "WN-FLG", n4.NozzleType);
            Equal("N.4 copied size", "DN50", n4.Size);
            Equal("N.4 copied material", "1.4301", n4.Material);
            Equal("N.4 copied Fx", "1,2", n4.Loads["Fx"]);
        }

        private static void PadDoesNotOverwriteNozzleMaterial(string fixtureRoot)
        {
            List<NozzleRow> rows = ParseFixture(fixtureRoot, "pad_material_fallback.txt");
            NozzleRow n2 = Row(rows, "N.2");

            Equal("N.2 component kind", "Nozzle, Seamless Pipe", n2.ComponentKind);
            Equal("N.2 material stays nozzle neck", "1.4301", n2.Material);
            Equal("N.2 pipe stays nozzle neck", "D33,7 x 3,38", n2.PipeDimension);
            Equal("N.2 standard stays material standard", "EN10216-5:2021", n2.Standard);
        }

        private static void DetailPageFillsFallbacksAndTableLoads(string fixtureRoot)
        {
            List<NozzleRow> rows = ParseFixture(fixtureRoot, "detail_load_table.txt");
            NozzleRow n3 = Row(rows, "N.3");

            Equal("N.3 type", "PL-FLG", n3.NozzleType);
            Equal("N.3 size", "DN80", n3.Size);
            Equal("N.3 class", "PN10", n3.PressureClass);
            Equal("N.3 pipe", "D88,9 x 5,49", n3.PipeDimension);
            Equal("N.3 material", "1.4404", n3.Material);
            Equal("N.3 standard", "EN10216-5:2021", n3.Standard);
            Equal("N.3 Fz max abs", "-9", n3.Loads["Fz"]);
            Equal("N.3 My max abs", "-0,7", n3.Loads["My"]);
            Equal("N.3 Mx max abs", "-2,4", n3.Loads["Mx"]);
            Equal("N.3 Fx max abs", "-3,2", n3.Loads["Fx"]);
            Equal("N.3 Fy max abs", "4,1", n3.Loads["Fy"]);
            Equal("N.3 Mz max abs", "-1,2", n3.Loads["Mz"]);
        }

        private static void StructuredTableOverridesGarbledText(string fixtureRoot)
        {
            // The flat-text load table carries stray leaked numbers (888, 77, ...) the way a
            // shifted column looks once layout is lost. The structured <<<TABLE>>> block has
            // clean cells, so the parser must prefer it and ignore the garbled text values.
            List<NozzleRow> rows = ParseFixture(fixtureRoot, "structured_load_table.txt");
            NozzleRow n7 = Row(rows, "N.7");

            Equal("N.7 structured Fz", "-15", n7.Loads["Fz"]);
            Equal("N.7 structured My", "-0,9", n7.Loads["My"]);
            Equal("N.7 structured Mx", "-3,1", n7.Loads["Mx"]);
            Equal("N.7 structured Fx", "-4,5", n7.Loads["Fx"]);
            Equal("N.7 structured Fy", "5,2", n7.Loads["Fy"]);
            Equal("N.7 structured Mz", "-1,6", n7.Loads["Mz"]);
        }

        private static void ValidatorFlagsConflictsAndBadGeometry(string fixtureRoot)
        {
            // Nozzle List and the MAWP flange table disagree on size/class for N.5, and the
            // detail page yields wall thickness >= radius. The validator must flag all three
            // and drop the row to Low confidence, without changing the chosen values.
            List<NozzleRow> rows = ParseFixture(fixtureRoot, "validation_conflict.txt");
            NozzleRow n5 = Row(rows, "N.5");

            Equal("N.5 keeps Nozzle List size", "DN50", n5.Size);
            Equal("N.5 keeps Nozzle List class", "PN16", n5.PressureClass);
            Equal("N.5 confidence", "Low", n5.Confidence.ToString());

            True("N.5 size conflict flagged", HasDiagnostic(n5, "Size", "conflict"));
            True("N.5 class conflict flagged", HasDiagnostic(n5, "PressureClass", "conflict"));
            True("N.5 geometry flagged", HasDiagnostic(n5, "PipeDimension", "thickness"));
        }

        private static bool HasDiagnostic(NozzleRow row, string field, string messagePart)
        {
            foreach (Diagnostic d in row.Diagnostics)
                if (d.Field == field && d.Message.IndexOf(messagePart, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            return false;
        }

        private static void True(string name, bool condition)
        {
            _assertions++;
            if (!condition)
                throw new Exception(name + ": expected true");
        }

        private static void FallbackExtractorPrefersFirstUsableResult()
        {
            // Primary throws (e.g. pdfplumber not installed) -> falls back to secondary.
            Equal("fallback on throw", "secondary",
                new FallbackReportTextExtractor(
                    new StubExtractor(() => { throw new Exception("boom"); }),
                    new StubExtractor(() => "secondary")).ExtractText("x"));

            // Primary returns blank -> falls back to secondary.
            Equal("fallback on blank", "secondary",
                new FallbackReportTextExtractor(
                    new StubExtractor(() => "   "),
                    new StubExtractor(() => "secondary")).ExtractText("x"));

            // Primary returns usable text -> secondary is never consulted.
            Equal("prefers primary", "primary",
                new FallbackReportTextExtractor(
                    new StubExtractor(() => "primary"),
                    new StubExtractor(() => { throw new Exception("must not run"); })).ExtractText("x"));

            // All extractors throw -> the last error surfaces.
            string message = "";
            try
            {
                new FallbackReportTextExtractor(
                    new StubExtractor(() => { throw new Exception("first"); }),
                    new StubExtractor(() => { throw new Exception("last"); })).ExtractText("x");
            }
            catch (Exception ex)
            {
                message = ex.Message;
            }
            Equal("rethrows last error", "last", message);
        }

        private sealed class StubExtractor : IReportTextExtractor
        {
            private readonly Func<string> _result;
            public StubExtractor(Func<string> result) { _result = result; }
            public string ExtractText(string reportPath) { return _result(); }
        }

        private static List<NozzleRow> ParseFixture(string fixtureRoot, string fileName)
        {
            string path = Path.Combine(fixtureRoot, fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException("Fixture not found: " + path);

            return new VvdNozzleParser().Parse(File.ReadAllText(path));
        }

        private static NozzleRow Row(List<NozzleRow> rows, string key)
        {
            NozzleRow row = rows.FirstOrDefault(r => String.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase));
            if (row == null)
                throw new Exception("Expected row not found: " + key);
            return row;
        }

        private static void Equal(string name, string expected, string actual)
        {
            _assertions++;
            if (!String.Equals(expected, actual, StringComparison.Ordinal))
                throw new Exception(name + ": expected '" + expected + "', actual '" + actual + "'");
        }

        private static string FindFixtureRoot()
        {
            string dir = AppDomain.CurrentDomain.BaseDirectory;
            for (int i = 0; i < 6; i++)
            {
                string candidate = Path.Combine(dir, "Tests", "Fixtures");
                if (Directory.Exists(candidate))
                    return candidate;

                DirectoryInfo parent = Directory.GetParent(dir);
                if (parent == null)
                    break;
                dir = parent.FullName;
            }

            throw new DirectoryNotFoundException("Could not locate Tests\\Fixtures from " + AppDomain.CurrentDomain.BaseDirectory);
        }
    }
}
