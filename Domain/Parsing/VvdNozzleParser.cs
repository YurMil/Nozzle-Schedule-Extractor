using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace NozzleScheduleExtractor
{
    internal sealed class VvdNozzleParser : INozzleScheduleParser
    {
        private readonly Dictionary<string, NozzleRow> _rows = new Dictionary<string, NozzleRow>(StringComparer.OrdinalIgnoreCase);

        // Hoisted, compiled regexes for the hot full-text scans. Recreating these on every
        // Parse() call was wasteful; the .NET internal cache only covers the inline Regex.* calls.
        private static readonly RegexOptions Opts = RegexOptions.IgnoreCase | RegexOptions.Compiled;
        private static readonly Regex BomFlangeRx = new Regex(@"(?<id>N\.\d+[A-Z*]*)\s+1\s+Flange:(?<std>EN\s*1092|DIN\s+\d+|ASME\s+B16\.5).{0,120}?(?:(?:Class\s+)?(?<asme>\d+)\s+lbs|PN\s*(?<pn>\d+)).{0,120}?\b(?<type>WN|LJ|RT|PL)\b", Opts);
        private static readonly Regex BomNozzleRx = new Regex(@"(?<id>N\.\d+[A-Z*]*)\s+1\s+Nozzle,[^-]+-\s*(?<desc>.*?)(?<size>DN\s*\d+|\d+"")?\s+do=(?<od>[\d.,]+),wt=(?<wt>[\d.,]+).*?ID\s+\d+,\s+(?<matstd>EN\s+\d{5}(?:-\d)?:\d{4}),\s+(?<mat>1\.\d{4})", Opts);
        private static readonly Regex BomRingRx = new Regex(@"(?<id>N\.\d+[A-Z*]*)\s+1\s+Reinforcement Ring-?\s*(?<desc>.*?)\s+do=(?<od>[\d.,]+),di=(?<idim>[\d.,]+).*?(?:wt|thk|s)=(?<wt>[\d.,]+).*?ID\s+\d+,\s+(?<matstd>EN\s+\d{5}(?:-\d)?:\d{4}),\s+(?<mat>1\.\d{4})", Opts);
        private static readonly Regex BomComponentRx = new Regex(@"(?<id>N\.\d+[A-Z*]*)\s+1\s+(?<chunk>.*?)(?=\s+[A-Z]{1,3}\.?\d+(?:\.\d+)?[A-Z*]*\s+1\s+|$)", Opts);
        private static readonly Regex LoadsSummaryRx = new Regex(@"(?<id>N\.\d+[A-Z*]*)\s*.*?Load Case\s+\d+.*?Fz\s*=\s*(?<Fz>-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+))\s*kN,\s*My\s*=\s*(?<My>-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+))\s*kNm,\s*Mx\s*=\s*(?<Mx>-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+))\s*kNm,\s*Fl\s*=\s*(?<Fx>-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+))\s*kN,\s*Fc\s*=\s*(?<Fy>-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+))\s*kN,\s*Mt\s*=\s*(?<Mz>-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+))", Opts);

        public List<NozzleRow> Parse(string text)
        {
            _rows.Clear();
            ParseHistory(text);
            ParseNozzleList(text);
            ParseBom(text);
            ParseMawpFlanges(text);
            ParseNozzleLoads(text);
            ParseDetailedSections(text);
            ApplyCopyNotes(text);
            InferMissingValues();

            return _rows.Values
                .OrderBy(r => TextUtil.DisplayNumberSortKey(r.Key))
                .ToList();
        }

        private NozzleRow Get(string id)
        {
            string key = TextUtil.NormId(id);
            NozzleRow row;
            if (!_rows.TryGetValue(key, out row))
            {
                row = new NozzleRow { Key = key };
                _rows[key] = row;
            }
            return row;
        }

        private void ParseHistory(string text)
        {
            int end = text.IndexOf("Design Data & Process Information", StringComparison.OrdinalIgnoreCase);
            string history = end > 0 ? text.Substring(0, end) : text;
            foreach (string raw in TextUtil.Lines(history))
            {
                string line = TextUtil.Normalize(raw);
                Match m = Regex.Match(line, @"(?:^|\s)(?<id>N\.\d+[A-Z*]*)\s+(?<kind>Nozzle,\s*Seamless Pipe|Nozzle,\s*Plate Body|Reinforcement Ring)(?:\s+(?<desc>.*?))?(?:\s+\d{2}\s+\w{3}\.?\s+\d{4}|$)", RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                NozzleRow row = Get(m.Groups["id"].Value);
                row.ComponentKind = TextUtil.Normalize(m.Groups["kind"].Value);
                string desc = TextUtil.Normalize(m.Groups["desc"].Value);
                desc = Regex.Replace(desc, @"^\d{2}\s+", "");
                SetDescription(row, desc);
            }
        }

        private void ParseNozzleList(string text)
        {
            foreach (string block in BlocksBetween(text, "Nozzle List", new[] { "Nozzle Loads", "Maximum Component Utilization", "Appendix", "Calculation Cover Sheet" }))
            {
                foreach (string raw in TextUtil.Lines(block))
                {
                    string line = TextUtil.Normalize(raw);
                    if (!Regex.IsMatch(line, @"^N\.\d+", RegexOptions.IgnoreCase)) continue;

                    Match flanged = Regex.Match(line, @"^(?<id>N\.\d+[A-Z*]*)\s+(?<desc>.*?)(?:(?<size>DN\s*\d+|\d+"")\s+)?(?<std>EN\s*1092(?:-\d)?|DIN\s+\d+|ASME\s+B16\.\d)\s+(?:(?:Class\s+)?(?<asme>\d+)\s+lbs|PN\s*(?<pn>\d+))\s+(?<type>WN|LJ|RT|PL)\b", RegexOptions.IgnoreCase);
                    if (flanged.Success)
                    {
                        NozzleRow row = Get(flanged.Groups["id"].Value);
                        SetDescription(row, flanged.Groups["desc"].Value);
                        if (flanged.Groups["size"].Success) row.Size = flanged.Groups["size"].Value.Replace(" ", "");
                        row.Standard = NormalizeStandard(flanged.Groups["std"].Value);
                        if (flanged.Groups["pn"].Success) row.PressureClass = "PN" + flanged.Groups["pn"].Value;
                        if (flanged.Groups["asme"].Success) row.PressureClass = "Class " + flanged.Groups["asme"].Value;
                        row.NozzleType = MapFlangeType(flanged.Groups["type"].Value);
                        continue;
                    }

                    Match loose = Regex.Match(line, @"^(?<id>N\.\d+[A-Z*]*)\s+(?<desc>.*?)(?<size>DN\s*\d+|\d+"")\b", RegexOptions.IgnoreCase);
                    if (loose.Success)
                    {
                        NozzleRow row = Get(loose.Groups["id"].Value);
                        SetDescription(row, loose.Groups["desc"].Value);
                        row.Size = loose.Groups["size"].Value.Replace(" ", "");
                    }
                }
            }
        }

        private void ParseBom(string text)
        {
            foreach (string block in BlocksBetween(text, "Bill of Materials", new[] { "Center of Gravity", "MAWP", "Test Pressure", "Nozzle List", "Maximum Component Utilization", "Appendix" }))
            {
                ParseBomBlock(block);
            }

            string compact = TextUtil.Normalize(text);
            foreach (Match m in BomFlangeRx.Matches(compact))
            {
                NozzleRow row = Get(m.Groups["id"].Value);
                if (TextUtil.IsBlank(row.Standard) || IsFlangeStandard(row.Standard)) row.Standard = NormalizeStandard(m.Groups["std"].Value);
                if (TextUtil.IsBlank(row.PressureClass) && m.Groups["pn"].Success) row.PressureClass = "PN" + m.Groups["pn"].Value;
                if (TextUtil.IsBlank(row.PressureClass) && m.Groups["asme"].Success) row.PressureClass = "Class " + m.Groups["asme"].Value;
                if (TextUtil.IsBlank(row.NozzleType)) row.NozzleType = MapFlangeType(m.Groups["type"].Value);
            }

            foreach (Match m in BomNozzleRx.Matches(compact))
            {
                NozzleRow row = Get(m.Groups["id"].Value);
                row.ComponentKind = TextUtil.IsBlank(row.ComponentKind) ? "Nozzle" : row.ComponentKind;
                SetDescription(row, m.Groups["desc"].Value);
                if (m.Groups["size"].Success) row.Size = m.Groups["size"].Value.Replace(" ", "");
                row.PipeDimension = "D" + TextUtil.Fmt(m.Groups["od"].Value) + " x " + TextUtil.Fmt(m.Groups["wt"].Value);
                row.Material = m.Groups["mat"].Value;
                if (TextUtil.IsBlank(row.Standard)) row.Standard = NormalizeStandard(m.Groups["matstd"].Value);
            }

            foreach (Match m in BomRingRx.Matches(compact))
            {
                NozzleRow row = Get(m.Groups["id"].Value);
                row.ComponentKind = "Reinforcement Ring";
                SetDescription(row, m.Groups["desc"].Value);
                if (TextUtil.IsBlank(row.PipeDimension))
                    row.PipeDimension = "D" + TextUtil.Fmt(m.Groups["od"].Value) + " x " + TextUtil.Fmt(m.Groups["wt"].Value);
                if (TextUtil.IsBlank(row.Size))
                    row.Size = "D" + TextUtil.Fmt(m.Groups["idim"].Value);
                row.Material = m.Groups["mat"].Value;
                if (TextUtil.IsBlank(row.Standard)) row.Standard = NormalizeStandard(m.Groups["matstd"].Value);
            }
        }

        private void ParseBomBlock(string block)
        {
            string compact = TextUtil.Normalize(block);
            foreach (Match m in BomComponentRx.Matches(compact))
                ParseBomComponent(m.Groups["id"].Value, m.Groups["chunk"].Value);
        }

        private void ParseBomComponent(string id, string chunk)
        {
            chunk = TextUtil.Normalize(chunk);
            NozzleRow row = Get(id);

            if (Regex.IsMatch(chunk, @"^Flange:", RegexOptions.IgnoreCase))
            {
                ApplyBomFlange(row, chunk);
                return;
            }

            if (Regex.IsMatch(chunk, @"^Nozzle", RegexOptions.IgnoreCase))
            {
                row.ComponentKind = ExtractComponentKind(chunk);
                SetDescription(row, ExtractServiceFromBomDescription(chunk));
                ApplyBomNozzleGeometry(row, chunk);
                ApplyMaterial(row, chunk, true);
                return;
            }

            if (Regex.IsMatch(chunk, @"^Reinforcement Ring", RegexOptions.IgnoreCase))
            {
                row.ComponentKind = "Reinforcement Ring";
                SetDescription(row, ExtractServiceFromBomDescription(chunk));
                ApplyBomRingGeometry(row, chunk);
                ApplyMaterial(row, chunk, true);
                return;
            }

            if (Regex.IsMatch(chunk, @"^Reinforcement Pad", RegexOptions.IgnoreCase))
            {
                if (TextUtil.IsBlank(row.ComponentKind))
                    row.ComponentKind = "Reinforcement Pad";
                if (TextUtil.IsBlank(row.PipeDimension))
                    ApplyBomRingGeometry(row, chunk);
                ApplyMaterial(row, chunk, false);
            }
        }

        private static void ApplyBomFlange(NozzleRow row, string text)
        {
            Match std = Regex.Match(text, @"Flange:(?<std>EN\s*1092|DIN\s+\d+|ASME\s+B16\.5)", RegexOptions.IgnoreCase);
            if (std.Success) row.Standard = NormalizeStandard(std.Groups["std"].Value);
            Match pn = Regex.Match(text, @"\bPN\s*(?<pn>\d+)\b", RegexOptions.IgnoreCase);
            Match asme = Regex.Match(text, @"\bClass\s*(?<asme>\d+)\s*lbs\b", RegexOptions.IgnoreCase);
            if (pn.Success) row.PressureClass = "PN" + pn.Groups["pn"].Value;
            if (asme.Success) row.PressureClass = "Class " + asme.Groups["asme"].Value;
            Match type = Regex.Match(text, @"\b(?<type>WN|LJ|RT|PL)\s+-\s+Type|\b(?<type2>WN|LJ|RT|PL)\b", RegexOptions.IgnoreCase);
            if (type.Success)
                row.NozzleType = MapFlangeType(type.Groups["type"].Success ? type.Groups["type"].Value : type.Groups["type2"].Value);
        }

        private static void ApplyBomNozzleGeometry(NozzleRow row, string text)
        {
            Match size = Regex.Match(text, @"\b(?<size>DN\s*\d+|\d+"")\b", RegexOptions.IgnoreCase);
            if (size.Success) row.Size = size.Groups["size"].Value.Replace(" ", "");
            Match od = Regex.Match(text, @"\bdo=(?<od>[\d.,]+)", RegexOptions.IgnoreCase);
            Match wt = Regex.Match(text, @"\bwt=(?<wt>[\d.,]+)", RegexOptions.IgnoreCase);
            if (od.Success && wt.Success)
                row.PipeDimension = "D" + TextUtil.Fmt(od.Groups["od"].Value) + " x " + TextUtil.Fmt(wt.Groups["wt"].Value);
        }

        private static void ApplyBomRingGeometry(NozzleRow row, string text)
        {
            Match od = Regex.Match(text, @"\b(?:do|PAD OD)=(?<od>[\d.,]+)", RegexOptions.IgnoreCase);
            Match idim = Regex.Match(text, @"\b(?:di|ID)=(?<idim>[\d.,]+)", RegexOptions.IgnoreCase);
            Match thk = Regex.Match(text, @"\b(?:thk|wt|width)=(?<thk>[\d.,]+)", RegexOptions.IgnoreCase);
            if (od.Success && thk.Success)
                row.PipeDimension = "D" + TextUtil.Fmt(od.Groups["od"].Value) + " x " + TextUtil.Fmt(thk.Groups["thk"].Value);
            if (idim.Success)
                row.Size = "D" + TextUtil.Fmt(idim.Groups["idim"].Value);
        }

        private static void ApplyMaterial(NozzleRow row, string text, bool overwrite)
        {
            Match mat = Regex.Match(text, @"\b(?<std>EN\s+\d{5}(?:-\d)?:\d{4}|TSG\s+R0004),\s*(?<mat>1\.\d{4})", RegexOptions.IgnoreCase);
            if (!mat.Success)
                mat = Regex.Match(text, @"\b(?<std>EN\s+\d{5}(?:-\d)?),\s*(?<mat>1\.\d{4})", RegexOptions.IgnoreCase);
            if (!mat.Success) return;
            if (overwrite || TextUtil.IsBlank(row.Material)) row.Material = mat.Groups["mat"].Value;
            if ((overwrite || TextUtil.IsBlank(row.Standard)) && !IsFlangeStandard(row.Standard))
                row.Standard = NormalizeStandard(mat.Groups["std"].Value);
        }

        private static string ExtractComponentKind(string description)
        {
            Match m = Regex.Match(description, @"^(?<kind>Nozzle,\s*[^-]+)", RegexOptions.IgnoreCase);
            return m.Success ? TextUtil.Normalize(m.Groups["kind"].Value) : "Nozzle";
        }

        private static string ExtractServiceFromBomDescription(string description)
        {
            Match m = Regex.Match(description, @"-\s*(?<service>.*?)(?=\s*(?:DN\s*\d+|\d+""|do=|PAD\s+OD=|ID\s+\d+)|$)", RegexOptions.IgnoreCase);
            if (!m.Success) return "";
            string service = TextUtil.Normalize(m.Groups["service"].Value);
            service = service.Trim(' ', ',', '-');
            return service;
        }

        private void ParseMawpFlanges(string text)
        {
            foreach (string raw in TextUtil.Lines(text))
            {
                string line = TextUtil.Normalize(raw);
                Match m = Regex.Match(line, @"^(?<id>N\.\d+[A-Z*]*)\s+Standard Flange\s+(?<size>DN\s*\d+|\d+"")\s+(?<std>EN\s*1092|DIN\s+\d+|ASME\s+B16\.\d)\s+(?:(?:Class\s+)?(?<asme>\d+)\s+lbs|PN\s*(?<pn>\d+))\s+(?<type>WN|LJ|RT|PL)\b", RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                NozzleRow row = Get(m.Groups["id"].Value);
                if (TextUtil.IsBlank(row.Size)) row.Size = m.Groups["size"].Value.Replace(" ", "");
                if (TextUtil.IsBlank(row.Standard)) row.Standard = NormalizeStandard(m.Groups["std"].Value);
                if (TextUtil.IsBlank(row.PressureClass) && m.Groups["pn"].Success) row.PressureClass = "PN" + m.Groups["pn"].Value;
                if (TextUtil.IsBlank(row.PressureClass) && m.Groups["asme"].Success) row.PressureClass = "Class " + m.Groups["asme"].Value;
                if (TextUtil.IsBlank(row.NozzleType)) row.NozzleType = MapFlangeType(m.Groups["type"].Value);
            }
        }

        private void ParseNozzleLoads(string text)
        {
            string compact = TextUtil.Normalize(text);
            foreach (Match m in LoadsSummaryRx.Matches(compact))
                ApplyLoads(Get(m.Groups["id"].Value), m);

            string[] pages = Regex.Split(text, @"<<<PAGE\s+\d+>>>");
            for (int i = 0; i < pages.Length; i++)
            {
                if (pages[i].IndexOf("Table NOZZLE LOADS", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                string section = pages[i];
                if (!LooksLikeCompleteLoadTable(section) && i + 1 < pages.Length)
                    section += "\n" + pages[i + 1];
                string id = FindNozzleIdNearLoadTable(section);
                if (TextUtil.IsBlank(id)) continue;
                ApplyLoadTable(Get(id), section);
            }
        }

        private void ParseDetailedSections(string text)
        {
            string[] pages = Regex.Split(text, @"<<<PAGE\s+\d+>>>");
            for (int i = 0; i < pages.Length; i++)
            {
                string page = pages[i];
                if (page.IndexOf("DATA FOR NOZZLE", StringComparison.OrdinalIgnoreCase) < 0 &&
                    page.IndexOf("OUTSIDE NOZZLE DIAMETER", StringComparison.OrdinalIgnoreCase) < 0 &&
                    page.IndexOf("Table NOZZLE LOADS", StringComparison.OrdinalIgnoreCase) < 0)
                    continue;

                Match h = Regex.Match(page, @"DATA FOR NOZZLE:\s*(?<id>N\.\d+[A-Z*]*)|(?<id2>N\.\d+[A-Z*]*)\s+(?:Nozzle|Reinforcement)", RegexOptions.IgnoreCase);
                if (!h.Success) continue;
                string id = h.Groups["id"].Success ? h.Groups["id"].Value : h.Groups["id2"].Value;
                string section = page;
                if (page.IndexOf("Table NOZZLE LOADS", StringComparison.OrdinalIgnoreCase) >= 0 && i + 1 < pages.Length)
                    section += "\n" + pages[i + 1];
                ApplySection(Get(id), section);
            }
        }

        private void ApplySection(NozzleRow row, string section)
        {
            Match size = Regex.Match(section, @"Size of Flange and Nozzle:\s*(DN\s*\d+|\d+"")", RegexOptions.IgnoreCase);
            if (size.Success && TextUtil.IsBlank(row.Size)) row.Size = size.Groups[1].Value.Replace(" ", "");

            Match deb = Regex.Match(section, @"OUTSIDE NOZZLE DIAMETER.*?:deb\s+([\d.,]+)\s+mm", RegexOptions.IgnoreCase);
            Match enb = Regex.Match(section, @"NOMINAL NOZZLE THICKNESS.*?:enb\s+([\d.,]+)\s+mm", RegexOptions.IgnoreCase);
            if (deb.Success && enb.Success && TextUtil.IsBlank(row.PipeDimension))
                row.PipeDimension = "D" + TextUtil.Fmt(deb.Groups[1].Value) + " x " + TextUtil.Fmt(enb.Groups[1].Value);

            Match mat = Regex.Match(section, @"\b(EN\s+\d{5}(?:-\d)?:\d{4}),\s+(1\.\d{4})", RegexOptions.IgnoreCase);
            if (mat.Success && TextUtil.IsBlank(row.Material))
            {
                row.Material = mat.Groups[2].Value;
                if (TextUtil.IsBlank(row.Standard)) row.Standard = NormalizeStandard(mat.Groups[1].Value);
            }

            Match pressure = Regex.Match(section, @"Pressure Class:\s*(?:EN1092|EN\s*1092|DIN\s+\d+)\s+:Class\s+PN\s*(\d+)", RegexOptions.IgnoreCase);
            if (pressure.Success && TextUtil.IsBlank(row.PressureClass)) row.PressureClass = "PN" + pressure.Groups[1].Value;

            Match asme = Regex.Match(section, @"Pressure Class:\s*ASME\s+B16\.\d+:Class\s+(\d+)\s+lbs", RegexOptions.IgnoreCase);
            if (asme.Success && TextUtil.IsBlank(row.PressureClass)) row.PressureClass = "Class " + asme.Groups[1].Value;

            Match flange = Regex.Match(section, @"Flange Type:\s*(WN|LJ|RT|PL)\b", RegexOptions.IgnoreCase);
            if (flange.Success && TextUtil.IsBlank(row.NozzleType)) row.NozzleType = MapFlangeType(flange.Groups[1].Value);

            ApplyLoadTable(row, section);
        }

        private static bool LooksLikeCompleteLoadTable(string section)
        {
            return Regex.IsMatch(section, @"Radial Load\s+Fz\s+kN\s+", RegexOptions.IgnoreCase) &&
                   Regex.IsMatch(section, @"Torsional Moment\s+Mt\s+kNm\s+", RegexOptions.IgnoreCase);
        }

        private static string FindNozzleIdNearLoadTable(string section)
        {
            Match data = Regex.Match(section, @"DATA FOR NOZZLE:\s*(?<id>N\.\d+[A-Z*]*)", RegexOptions.IgnoreCase);
            if (data.Success) return data.Groups["id"].Value;

            Match footer = Regex.Match(section, @"(?:^|\n)\s*(?<id>N\.\d+[A-Z*]*)\s+\d{1,2}\s+\w{3}\.?\s+\d{4}", RegexOptions.IgnoreCase);
            if (footer.Success) return footer.Groups["id"].Value;

            Match heading = Regex.Match(section, @"(?:^|\n)\s*\d+\s+(?<id>N\.\d+[A-Z*]*)\s+(?:Nozzle|Reinforcement)", RegexOptions.IgnoreCase);
            if (heading.Success) return heading.Groups["id"].Value;

            // Last resort: only trust a bare id if the section mentions exactly one distinct
            // nozzle. Picking the first of several ids risks attaching a load table to the
            // wrong nozzle silently, so we skip instead.
            var distinct = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(section, @"\b(?<id>N\.\d+[A-Z*]*)\b", RegexOptions.IgnoreCase))
                distinct.Add(TextUtil.NormId(m.Groups["id"].Value));
            return distinct.Count == 1 ? distinct.First() : "";
        }

        private static void ApplyLoadTable(NozzleRow row, string section)
        {
            var patterns = new Dictionary<string, string>
            {
                { "Fz", @"Radial Load\s+Fz\s+kN\s+(?<values>(?:-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+)\s*)+)" },
                { "My", @"Longitudinal Moment\s+My\s+kNm\s+(?<values>(?:-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+)\s*)+)" },
                { "Mx", @"Circumferential Moment:?\s+Mx\s+kNm\s+(?<values>(?:-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+)\s*)+)" },
                { "Fx", @"Longitudinal Shear Force\s+Fl\s+kN\s+(?<values>(?:-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+)\s*)+)" },
                { "Fy", @"Circumferential Shear Force\s+Fc\s+kN\s+(?<values>(?:-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+)\s*)+)" },
                { "Mz", @"Torsional Moment\s+Mt\s+kNm\s+(?<values>(?:-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+)\s*)+)" }
            };

            foreach (var kv in patterns)
            {
                Match m = Regex.Match(section, kv.Value, RegexOptions.IgnoreCase);
                if (!m.Success) continue;
                string value = PickMaxAbsValue(m.Groups["values"].Value);
                if (!TextUtil.IsBlank(value))
                    row.Loads[kv.Key] = value;
            }
        }

        private static string PickMaxAbsValue(string values)
        {
            MatchCollection matches = Regex.Matches(values ?? "", @"-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+)");
            if (matches.Count == 0) return "";

            double best = 0;
            bool hasBest = false;
            foreach (Match m in matches)
            {
                double value;
                if (!Double.TryParse(m.Value.Replace(",", "."), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out value))
                    continue;
                if (!hasBest || Math.Abs(value) > Math.Abs(best))
                {
                    best = value;
                    hasBest = true;
                }
            }
            return hasBest ? TextUtil.Fmt(best.ToString(System.Globalization.CultureInfo.InvariantCulture)) : "";
        }

        private void ApplyCopyNotes(string text)
        {
            foreach (Match m in Regex.Matches(text, @"(?<target>N\.\d+[A-Z*]*)\s+.*?\(Copy of\s+(?<src>N\.\d+[A-Z*]*)\)", RegexOptions.IgnoreCase))
            {
                NozzleRow target = Get(m.Groups["target"].Value);
                NozzleRow source;
                if (_rows.TryGetValue(TextUtil.NormId(m.Groups["src"].Value), out source))
                    target.MergeFrom(source);
            }
        }

        private void InferMissingValues()
        {
            foreach (NozzleRow row in _rows.Values)
            {
                if (TextUtil.IsBlank(row.Description) && !TextUtil.IsBlank(row.ComponentKind))
                    row.Description = row.ComponentKind;
                if (TextUtil.IsBlank(row.NozzleType))
                    row.NozzleType = row.IsReinforcement ? "PL-FLG, FF" : "";
                if (!TextUtil.IsBlank(row.Standard))
                    row.Standard = NormalizeStandard(row.Standard);
            }
        }

        private static IEnumerable<string> BlocksBetween(string text, string startMarker, string[] endMarkers)
        {
            int search = 0;
            while (true)
            {
                int start = text.IndexOf(startMarker, search, StringComparison.OrdinalIgnoreCase);
                if (start < 0) yield break;
                int end = -1;
                foreach (string marker in endMarkers)
                {
                    int candidate = text.IndexOf(marker, start + startMarker.Length, StringComparison.OrdinalIgnoreCase);
                    if (candidate > start && (end < 0 || candidate < end)) end = candidate;
                }
                if (end < 0) end = Math.Min(text.Length, start + 8000);
                yield return text.Substring(start, end - start);
                search = start + startMarker.Length;
            }
        }

        private static void ApplyLoads(NozzleRow row, Match m)
        {
            foreach (string key in new[] { "Fx", "Fy", "Fz", "Mx", "My", "Mz" })
                row.Loads[key] = TextUtil.Fmt(m.Groups[key].Value);
        }

        private static void SetDescription(NozzleRow row, string value)
        {
            string desc = TextUtil.Normalize(value);
            desc = Regex.Replace(desc, @"\b(Nozzle,?\s*Seamless Pipe|Nozzle,?\s*Plate Body|Reinforcement Ring)\b", "", RegexOptions.IgnoreCase);
            desc = Regex.Replace(desc, @"\b\d{2}\s+\w{3}\.?\s+\d{4}\s+\d{1,2}:\d{2}\b", "", RegexOptions.IgnoreCase);
            desc = Regex.Replace(desc, @"\b\d{1,2}\s+\w{3}\.?\s+\d{4}\s+\d{1,2}:\d{2}\b", "", RegexOptions.IgnoreCase);
            desc = Regex.Replace(desc, @"\b\w{3}\.?\s+\d{4}\s+\d{1,2}:\d{2}\b", "", RegexOptions.IgnoreCase);
            desc = Regex.Replace(desc, @"^(?:-|,|\s)+", "");
            desc = Regex.Replace(desc, @"\s+(?:DN\s*\d+|\d+"")$", "", RegexOptions.IgnoreCase);
            if (!TextUtil.IsBlank(desc))
                row.Description = desc;
        }

        private static string NormalizeStandard(string value)
        {
            value = TextUtil.Normalize(value).Replace(" ", "");
            value = Regex.Replace(value, @"^EN1092(?:-\d)?(?::\d{4})?$", "EN1092", RegexOptions.IgnoreCase);
            return value;
        }

        private static bool IsFlangeStandard(string value)
        {
            value = NormalizeStandard(value);
            return Regex.IsMatch(value ?? "", @"^(EN1092|DIN\d+|ASMEB16\.\d)$", RegexOptions.IgnoreCase);
        }

        private static string MapFlangeType(string value)
        {
            value = (value ?? "").ToUpperInvariant();
            if (value == "WN") return "WN-FLG";
            if (value == "LJ") return "LJ-FLG";
            if (value == "RT") return "RT-FLG";
            if (value == "PL") return "PL-FLG";
            return value;
        }
    }
}
