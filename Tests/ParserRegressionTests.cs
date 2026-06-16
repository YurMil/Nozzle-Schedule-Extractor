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
