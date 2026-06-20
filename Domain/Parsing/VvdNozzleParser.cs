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
        private static readonly Regex LoadsSummaryRx = new Regex(@"(?<id>N\.\d+[A-Z*]*)\s*.{0,150}?Load Case\s+\d+.{0,150}?Fz\s*=\s*(?<Fz>-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+))\s*kN,\s*My\s*=\s*(?<My>-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+))\s*kNm,\s*Mx\s*=\s*(?<Mx>-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+))\s*kNm,\s*Fl\s*=\s*(?<Fx>-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+))\s*kN,\s*Fc\s*=\s*(?<Fy>-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+))\s*kN,\s*Mt\s*=\s*(?<Mz>-?(?:\d+(?:[\.,]\d*)?|[\.,]\d+))", Opts);
        private static readonly Regex BareIdRx = new Regex(@"\b(?<id>N\.\d+[A-Z*]*)\b", Opts);

        public List<NozzleRow> Parse(string text)
        {
            _rows.Clear();
            ParseHistory(text);
            ParseNozzleList(text);
            ParseNozzleListStateful(text);
            ParseMawpSummaries(text);
            ParseBom(text);
            ParseMawpFlanges(text);
            ParseNozzleLoads(text);
            ParseDetailedSections(text);
            ParseStructuredLoadTables(text);
            ApplyCopyNotes(text);
            InferMissingValues();

            foreach (NozzleRow row in _rows.Values)
                RowValidator.Validate(row);

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
                        if (flanged.Groups["size"].Success)
                        {
                            string size = flanged.Groups["size"].Value.Replace(" ", "");
                            row.Size = size;
                            row.Observe("Size", size, Source.NozzleList);
                        }
                        row.Standard = NormalizeStandard(flanged.Groups["std"].Value);
                        row.Observe("Standard", row.Standard, Source.NozzleList);
                        if (flanged.Groups["pn"].Success) row.PressureClass = "PN" + flanged.Groups["pn"].Value;
                        if (flanged.Groups["asme"].Success) row.PressureClass = "Class " + flanged.Groups["asme"].Value;
                        row.Observe("PressureClass", row.PressureClass, Source.NozzleList);
                        row.NozzleType = MapFlangeType(flanged.Groups["type"].Value);
                        row.Observe("NozzleType", row.NozzleType, Source.NozzleList);
                        continue;
                    }

                    Match loose = Regex.Match(line, @"^(?<id>N\.\d+[A-Z*]*)\s+(?<desc>.*?)(?<size>DN\s*\d+|\d+"")\b", RegexOptions.IgnoreCase);
                    if (loose.Success)
                    {
                        NozzleRow row = Get(loose.Groups["id"].Value);
                        SetDescription(row, loose.Groups["desc"].Value);
                        row.Size = loose.Groups["size"].Value.Replace(" ", "");
                        row.Observe("Size", row.Size, Source.NozzleList);
                    }
                }
            }
        }

        private void ParseNozzleListStateful(string text)
        {
            foreach (string block in BlocksBetween(text, "Nozzle List", new[] { "Nozzle Loads", "Maximum Component Utilization", "Appendix", "Calculation Cover Sheet" }))
            {
                string pendingStd = "";
                string pendingClass = "";
                string pendingType = "";
                bool pendingDn = false;
                NozzleRow pendingRow = null;
                bool waitingForNumericSize = false;
                bool waitingForDescription = false;

                foreach (string raw in TextUtil.Lines(block))
                {
                    string line = TextUtil.Normalize(raw);
                    if (TextUtil.IsBlank(line)) continue;

                    Match stdLine = Regex.Match(line, @"^(?<dn>DN\s+)?(?<std>EN\s*1092|DIN\s+\d+|ASME\s+B16\.\d)\s+(?:(?:Class\s+)?(?<asme>\d+)\s+lbs|PN\s*(?<pn>\d+))\s+(?<type>WN|LJ|RT|PL)\b", RegexOptions.IgnoreCase);
                    if (stdLine.Success)
                    {
                        pendingDn = stdLine.Groups["dn"].Success;
                        pendingStd = NormalizeStandard(stdLine.Groups["std"].Value);
                        pendingClass = stdLine.Groups["pn"].Success ? "PN" + stdLine.Groups["pn"].Value : "Class " + stdLine.Groups["asme"].Value;
                        pendingType = MapFlangeType(stdLine.Groups["type"].Value);
                        continue;
                    }

                    Match idLine = Regex.Match(line, @"^(?<id>N\.\d+[A-Z*]*)\s+(?<rest>.+)$", RegexOptions.IgnoreCase);
                    if (idLine.Success)
                    {
                        NozzleRow row = Get(idLine.Groups["id"].Value);
                        pendingRow = row;
                        waitingForNumericSize = false;
                        waitingForDescription = false;

                        if (!TextUtil.IsBlank(pendingStd)) { row.Standard = pendingStd; row.Observe("Standard", pendingStd, Source.NozzleList); }
                        if (!TextUtil.IsBlank(pendingClass)) { row.PressureClass = pendingClass; row.Observe("PressureClass", pendingClass, Source.NozzleList); }
                        if (!TextUtil.IsBlank(pendingType)) { row.NozzleType = pendingType; row.Observe("NozzleType", pendingType, Source.NozzleList); }

                        // The pending flange context belongs to this nozzle only. Capture the DN
                        // flag locally (it is used below) and clear the rest so a following id
                        // without its own standard/class/type line cannot inherit these values.
                        bool dn = pendingDn;
                        pendingStd = "";
                        pendingClass = "";
                        pendingType = "";
                        pendingDn = false;

                        string rest = idLine.Groups["rest"].Value;
                        Match inlineDn = Regex.Match(rest, @"\bDN\s*(?<size>\d+)\b", RegexOptions.IgnoreCase);
                        if (inlineDn.Success)
                        {
                            row.Size = "DN" + inlineDn.Groups["size"].Value;
                            row.Observe("Size", row.Size, Source.NozzleList);
                        }
                        else
                        {
                            Match inlineStd = Regex.Match(rest, @"^DN\s+(?<std>EN\s*1092|DIN\s+\d+|ASME\s+B16\.\d)\s+(?:(?:Class\s+)?(?<asme>\d+)\s+lbs|PN\s*(?<pn>\d+))\s+(?<type>WN|LJ|RT|PL)\b", RegexOptions.IgnoreCase);
                            if (inlineStd.Success)
                            {
                                row.Standard = NormalizeStandard(inlineStd.Groups["std"].Value);
                                row.Observe("Standard", row.Standard, Source.NozzleList);
                                row.PressureClass = inlineStd.Groups["pn"].Success ? "PN" + inlineStd.Groups["pn"].Value : "Class " + inlineStd.Groups["asme"].Value;
                                row.Observe("PressureClass", row.PressureClass, Source.NozzleList);
                                row.NozzleType = MapFlangeType(inlineStd.Groups["type"].Value);
                                row.Observe("NozzleType", row.NozzleType, Source.NozzleList);
                                waitingForNumericSize = true;
                                waitingForDescription = true;
                                continue;
                            }

                            Match numericSize = Regex.Match(rest, @"^(?<desc>.*?)(?<size>\d+(?:[\.,]\d+)?)\s+\d+(?:[\.,]\d+)?\s+[-\d]", RegexOptions.IgnoreCase);
                            if (numericSize.Success && !dn)
                            {
                                row.Size = "D" + TextUtil.Fmt(numericSize.Groups["size"].Value);
                                row.Observe("Size", row.Size, Source.NozzleList);
                                SetDescription(row, numericSize.Groups["desc"].Value);
                            }
                            else
                            {
                                Match desc = Regex.Match(rest, @"^(?<desc>.*?)(?:\s+-?\d+(?:[\.,]\d+)?\b|$)");
                                string description = desc.Success ? TextUtil.Normalize(desc.Groups["desc"].Value) : "";
                                if (!TextUtil.IsBlank(description) && !Regex.IsMatch(description, @"^DN$", RegexOptions.IgnoreCase))
                                    SetDescription(row, description);
                                else
                                    waitingForDescription = true;
                                waitingForNumericSize = dn;
                            }
                        }
                        continue;
                    }

                    if (pendingRow != null && waitingForDescription)
                    {
                        Match desc = Regex.Match(line, @"^(?<desc>[A-Za-z][A-Za-z0-9 _./-]*?)(?:\s+-?\d+(?:[\.,]\d+)?\b|$)");
                        if (desc.Success && !Regex.IsMatch(desc.Groups["desc"].Value, @"^(Raised Face|DN)$", RegexOptions.IgnoreCase))
                        {
                            SetDescription(pendingRow, desc.Groups["desc"].Value);
                            waitingForDescription = false;
                        }
                    }

                    if (pendingRow != null && waitingForNumericSize)
                    {
                        Match size = Regex.Match(line, @"^(?<size>\d+)\s+Raised Face\b", RegexOptions.IgnoreCase);
                        if (size.Success)
                        {
                            pendingRow.Size = "DN" + size.Groups["size"].Value;
                            pendingRow.Observe("Size", pendingRow.Size, Source.NozzleList);
                            waitingForNumericSize = false;
                        }
                    }
                }
            }
        }

        private void ParseMawpSummaries(string text)
        {
            foreach (string raw in TextUtil.Lines(text))
            {
                string line = TextUtil.Normalize(raw);
                Match m = Regex.Match(line, @"^(?<id>N\.\d+[A-Z*]*)\s+(?<kind>Nozzle,\s*Seamless Pipe|Nozzle,\s*Plate Body|Reinforcement Ring)\s+(?<desc>.*?)\s+\d+(?:[\.,]\d+)?\s+MPa\b", RegexOptions.IgnoreCase);
                if (!m.Success) continue;

                NozzleRow row = Get(m.Groups["id"].Value);
                row.ComponentKind = TextUtil.Normalize(m.Groups["kind"].Value);
                SetDescription(row, m.Groups["desc"].Value);
            }
        }

        private void ParseBom(string text)
        {
            foreach (string block in BlocksBetween(text, "Bill of Materials", new[] { "Center of Gravity", "MAWP", "Test Pressure", "Nozzle List", "Maximum Component Utilization", "Appendix" }))
            {
                ParseBomBlock(block);
                ParseBomLines(block);
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
                if (m.Groups["size"].Success && TextUtil.IsBlank(row.Size)) row.Size = m.Groups["size"].Value.Replace(" ", "");
                if (TextUtil.IsBlank(row.PipeDimension)) row.PipeDimension = "D" + TextUtil.Fmt(m.Groups["od"].Value) + " x " + TextUtil.Fmt(m.Groups["wt"].Value);
                if (TextUtil.IsBlank(row.Material)) row.Material = m.Groups["mat"].Value;
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
                if (TextUtil.IsBlank(row.Material)) row.Material = m.Groups["mat"].Value;
                if (TextUtil.IsBlank(row.Standard)) row.Standard = NormalizeStandard(m.Groups["matstd"].Value);
            }
        }

        private void ParseBomBlock(string block)
        {
            string compact = TextUtil.Normalize(block);
            foreach (Match m in BomComponentRx.Matches(compact))
                ParseBomComponent(m.Groups["id"].Value, m.Groups["chunk"].Value);
        }

        private void ParseBomLines(string block)
        {
            string pendingKind = "";
            string pendingMaterialStandard = "";
            string pendingMaterial = "";
            string pendingFlangeStandard = "";
            string pendingFlangeType = "";
            NozzleRow pendingFlangeRow = null;
            NozzleRow pendingGeometryRow = null;

            foreach (string raw in TextUtil.Lines(block))
            {
                string line = TextUtil.Normalize(raw);
                if (TextUtil.IsBlank(line)) continue;

                Match typeHint = Regex.Match(line, @"\b(?<type>WN|LJ|RT|PL)\s+-\s+Type", RegexOptions.IgnoreCase);
                if (typeHint.Success)
                    pendingFlangeType = MapFlangeType(typeHint.Groups["type"].Value);

                Match componentMaterial = Regex.Match(line, @"(?<kind>Nozzle,\s*Seamless Pipe|Nozzle,\s*Plate Body|Reinforcement Ring(?:-[A-Za-z ]+)?)\s*(?:-\s*)?(?:DN\s*(?<dn>\d+)\s+)?ID\s+\d+,\s+(?<std>EN\s+\d{5}(?:-\d)?:\d{4}|EN\s+\d{5}(?:-\d)?),\s+(?<mat>1\.\d{4})", RegexOptions.IgnoreCase);
                if (componentMaterial.Success)
                {
                    pendingKind = TextUtil.Normalize(componentMaterial.Groups["kind"].Value);
                    pendingMaterialStandard = NormalizeStandard(componentMaterial.Groups["std"].Value);
                    pendingMaterial = componentMaterial.Groups["mat"].Value;
                    pendingGeometryRow = null;
                    // A non-flange component starts here; drop any pending flange context so its
                    // standard/type/size cannot leak into this ring or pipe.
                    pendingFlangeStandard = "";
                    pendingFlangeType = "";
                    pendingFlangeRow = null;
                    continue;
                }

                Match materialOnly = Regex.Match(line, @"^ID\s+\d+,\s+(?<std>EN\s+\d{5}(?:-\d)?:\d{4}|EN\s+\d{5}(?:-\d)?),\s+(?<mat>1\.\d{4})", RegexOptions.IgnoreCase);
                if (materialOnly.Success)
                {
                    pendingMaterialStandard = NormalizeStandard(materialOnly.Groups["std"].Value);
                    pendingMaterial = materialOnly.Groups["mat"].Value;
                    pendingFlangeStandard = "";
                    pendingFlangeType = "";
                    pendingFlangeRow = null;
                    continue;
                }

                Match flangeStd = Regex.Match(line, @"Flange:(?<std>EN\s*1092|DIN\s+\d+|ASME\s+B16\.\d)", RegexOptions.IgnoreCase);
                if (flangeStd.Success)
                {
                    pendingFlangeStandard = NormalizeStandard(flangeStd.Groups["std"].Value);
                    if (typeHint.Success)
                        pendingFlangeType = MapFlangeType(typeHint.Groups["type"].Value);
                    pendingKind = "";
                    pendingMaterialStandard = "";
                    pendingMaterial = "";
                    pendingGeometryRow = null;
                    // A new flange supersedes any previous flange awaiting PN/DN continuation lines.
                    pendingFlangeRow = null;
                    continue;
                }

                if (pendingFlangeRow != null)
                {
                    Match pendingPn = Regex.Match(line, @"\bPN\s*(?<pn>\d+)\b", RegexOptions.IgnoreCase);
                    if (pendingPn.Success)
                    {
                        pendingFlangeRow.PressureClass = "PN" + pendingPn.Groups["pn"].Value;
                        pendingFlangeRow.Observe("PressureClass", pendingFlangeRow.PressureClass, Source.Bom);
                    }

                    Match pendingDnMatch = Regex.Match(line, @"^DN\s*(?<dn>\d+)\b", RegexOptions.IgnoreCase);
                    if (pendingDnMatch.Success)
                    {
                        pendingFlangeRow.Size = "DN" + pendingDnMatch.Groups["dn"].Value;
                        pendingFlangeRow.Observe("Size", pendingFlangeRow.Size, Source.Bom);
                    }
                }

                if (pendingGeometryRow != null &&
                    !Regex.IsMatch(line, @"^N\.\d+[A-Z*]*\s+1\b", RegexOptions.IgnoreCase) &&
                    line.IndexOf("do=", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    ApplyBomNozzleGeometry(pendingGeometryRow, line);
                    ApplyPendingMaterial(pendingGeometryRow, pendingMaterialStandard, pendingMaterial, true);
                    Match service = Regex.Match(line, @"^(?<service>[A-Za-z][A-Za-z0-9 _./-]*?)\s+do=", RegexOptions.IgnoreCase);
                    if (service.Success)
                        SetDescription(pendingGeometryRow, service.Groups["service"].Value);
                    continue;
                }

                Match idLine = Regex.Match(line, @"^(?<id>N\.\d+[A-Z*]*)\s+1(?:\s+(?<rest>.*))?$", RegexOptions.IgnoreCase);
                if (!idLine.Success) continue;

                NozzleRow row = Get(idLine.Groups["id"].Value);
                string rest = TextUtil.Normalize(idLine.Groups["rest"].Value);

                if (rest.IndexOf("flange", StringComparison.OrdinalIgnoreCase) >= 0 || !TextUtil.IsBlank(pendingFlangeStandard))
                {
                    if (!TextUtil.IsBlank(pendingFlangeStandard))
                    {
                        row.Standard = pendingFlangeStandard;
                        row.Observe("Standard", row.Standard, Source.Bom);
                    }
                    if (!TextUtil.IsBlank(pendingFlangeType))
                    {
                        row.NozzleType = pendingFlangeType;
                        row.Observe("NozzleType", row.NozzleType, Source.Bom);
                    }
                    pendingFlangeRow = row;
                    if (rest.IndexOf("flange", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        pendingKind = "";
                        pendingMaterialStandard = "";
                        pendingMaterial = "";
                    }
                }

                if (rest.IndexOf("Reinforcement Ring", StringComparison.OrdinalIgnoreCase) >= 0 || pendingKind.StartsWith("Reinforcement Ring", StringComparison.OrdinalIgnoreCase))
                {
                    row.ComponentKind = rest.IndexOf("Reinforcement Ring", StringComparison.OrdinalIgnoreCase) >= 0 ? ExtractRingKind(rest) : pendingKind;
                    SetDescription(row, ExtractServiceFromBomDescription(row.ComponentKind));
                    ApplyBomRingGeometry(row, rest);
                    ApplyPendingMaterial(row, pendingMaterialStandard, pendingMaterial, true);
                    pendingGeometryRow = null;
                    pendingKind = "";
                    pendingMaterialStandard = "";
                    pendingMaterial = "";
                    continue;
                }

                if (rest.IndexOf("do=", StringComparison.OrdinalIgnoreCase) >= 0 || pendingKind.StartsWith("Nozzle", StringComparison.OrdinalIgnoreCase))
                {
                    row.ComponentKind = Regex.IsMatch(rest, @"^Nozzle", RegexOptions.IgnoreCase)
                        ? ExtractComponentKind(rest)
                        : (TextUtil.IsBlank(pendingKind) ? "Nozzle" : pendingKind);
                    ApplyBomNozzleGeometry(row, rest);
                    ApplyPendingMaterial(row, pendingMaterialStandard, pendingMaterial, true);
                    pendingGeometryRow = row;
                    pendingKind = "";
                    pendingMaterialStandard = "";
                    pendingMaterial = "";
                }

                Match pn = Regex.Match(rest, @"\bPN\s*(?<pn>\d+)\b", RegexOptions.IgnoreCase);
                if (pn.Success) { row.PressureClass = "PN" + pn.Groups["pn"].Value; row.Observe("PressureClass", row.PressureClass, Source.Bom); }
                Match dn = Regex.Match(rest, @"\bDN\s*(?<dn>\d+)\b", RegexOptions.IgnoreCase);
                if (dn.Success) { row.Size = "DN" + dn.Groups["dn"].Value; row.Observe("Size", row.Size, Source.Bom); }
            }
        }

        private static string ExtractRingKind(string text)
        {
            Match m = Regex.Match(text ?? "", @"(?<kind>Reinforcement Ring(?:-[A-Za-z ]+)?)", RegexOptions.IgnoreCase);
            return m.Success ? TextUtil.Normalize(m.Groups["kind"].Value) : "Reinforcement Ring";
        }

        private static void ApplyPendingMaterial(NozzleRow row, string standard, string material, bool overwrite)
        {
            if (TextUtil.IsBlank(material)) return;
            if (overwrite) row.Observe("Material", material, Source.Bom);
            if (overwrite || TextUtil.IsBlank(row.Material))
                row.Material = material;
            if (!TextUtil.IsBlank(standard) && !IsFlangeStandard(row.Standard) && (overwrite || TextUtil.IsBlank(row.Standard)))
                row.Standard = standard;
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
            if (std.Success) { row.Standard = NormalizeStandard(std.Groups["std"].Value); row.Observe("Standard", row.Standard, Source.Bom); }
            Match pn = Regex.Match(text, @"\bPN\s*(?<pn>\d+)\b", RegexOptions.IgnoreCase);
            Match asme = Regex.Match(text, @"\bClass\s*(?<asme>\d+)\s*lbs\b", RegexOptions.IgnoreCase);
            if (pn.Success) { row.PressureClass = "PN" + pn.Groups["pn"].Value; row.Observe("PressureClass", row.PressureClass, Source.Bom); }
            if (asme.Success) { row.PressureClass = "Class " + asme.Groups["asme"].Value; row.Observe("PressureClass", row.PressureClass, Source.Bom); }
            Match type = Regex.Match(text, @"\b(?<type>WN|LJ|RT|PL)\s+-\s+Type|\b(?<type2>WN|LJ|RT|PL)\b", RegexOptions.IgnoreCase);
            if (type.Success)
            {
                row.NozzleType = MapFlangeType(type.Groups["type"].Success ? type.Groups["type"].Value : type.Groups["type2"].Value);
                row.Observe("NozzleType", row.NozzleType, Source.Bom);
            }
        }

        private static void ApplyBomNozzleGeometry(NozzleRow row, string text)
        {
            Match size = Regex.Match(text, @"\b(?<size>DN\s*\d+|\d+"")\b", RegexOptions.IgnoreCase);
            if (size.Success) { row.Size = size.Groups["size"].Value.Replace(" ", ""); row.Observe("Size", row.Size, Source.Bom); }
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
            if (overwrite) row.Observe("Material", mat.Groups["mat"].Value, Source.Bom);
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
                row.Observe("Size", m.Groups["size"].Value.Replace(" ", ""), Source.Mawp);
                row.Observe("Standard", NormalizeStandard(m.Groups["std"].Value), Source.Mawp);
                if (m.Groups["pn"].Success) row.Observe("PressureClass", "PN" + m.Groups["pn"].Value, Source.Mawp);
                if (m.Groups["asme"].Success) row.Observe("PressureClass", "Class " + m.Groups["asme"].Value, Source.Mawp);
                row.Observe("NozzleType", MapFlangeType(m.Groups["type"].Value), Source.Mawp);
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

        // Maps a VVD load-table row label to a schedule load key. The label cell carries
        // the local symbol (Fz/My/Mx/Fl/Fc/Mt); VVD notation is remapped to Fx/Fy/Mz.
        // Each entry is { local symbol, schedule key }.
        private static readonly string[][] LoadSymbols =
        {
            new[] { "Fz", "Fz" }, new[] { "My", "My" }, new[] { "Mx", "Mx" },
            new[] { "Fl", "Fx" }, new[] { "Fc", "Fy" }, new[] { "Mt", "Mz" }
        };

        private static readonly Regex TableBlockRx = new Regex(@"<<<TABLE>>>(?<body>.*?)<<<TABLE END>>>", RegexOptions.Singleline);

        // Consumes the deterministic table blocks emitted by the pdfplumber extractor.
        // Cells are separated by '|', so column boundaries are exact and do not depend on
        // the fragile whitespace layout the text-based ApplyLoadTable relies on. Runs after
        // the text passes and overwrites their load values, since structured cells are the
        // most reliable source.
        private void ParseStructuredLoadTables(string text)
        {
            if (text.IndexOf("<<<TABLE>>>", StringComparison.Ordinal) < 0)
                return;

            string[] pages = Regex.Split(text, @"<<<PAGE\s+\d+>>>");
            foreach (string page in pages)
            {
                if (page.IndexOf("<<<TABLE>>>", StringComparison.Ordinal) < 0)
                    continue;
                string id = FindNozzleIdNearLoadTable(page);
                if (TextUtil.IsBlank(id)) continue;

                NozzleRow row = Get(id);
                foreach (Match block in TableBlockRx.Matches(page))
                    ApplyStructuredLoadTable(row, block.Groups["body"].Value);
            }
        }

        private static void ApplyStructuredLoadTable(NozzleRow row, string body)
        {
            foreach (string raw in TextUtil.Lines(body))
            {
                int sep = raw.IndexOf('|');
                if (sep < 0) continue;

                string key = MapLoadSymbol(raw.Substring(0, sep));
                if (key == null) continue;

                string value = PickMaxAbsValue(raw.Substring(sep + 1).Replace('|', ' '));
                if (!TextUtil.IsBlank(value))
                    row.Loads[key] = value;
            }
        }

        private static string MapLoadSymbol(string label)
        {
            foreach (string[] entry in LoadSymbols)
                if (Regex.IsMatch(label, @"\b" + entry[0] + @"\b", RegexOptions.IgnoreCase))
                    return entry[1];
            return null;
        }

        private void ApplySection(NozzleRow row, string section)
        {
            Match size = Regex.Match(section, @"Size of Flange and Nozzle:\s*(DN\s*\d+|\d+"")", RegexOptions.IgnoreCase);
            if (size.Success)
            {
                row.Observe("Size", size.Groups[1].Value.Replace(" ", ""), Source.Detail);
                if (TextUtil.IsBlank(row.Size)) row.Size = size.Groups[1].Value.Replace(" ", "");
            }

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
            if (pressure.Success)
            {
                row.Observe("PressureClass", "PN" + pressure.Groups[1].Value, Source.Detail);
                if (TextUtil.IsBlank(row.PressureClass)) row.PressureClass = "PN" + pressure.Groups[1].Value;
            }

            Match asme = Regex.Match(section, @"Pressure Class:\s*ASME\s+B16\.\d+:Class\s+(\d+)\s+lbs", RegexOptions.IgnoreCase);
            if (asme.Success)
            {
                row.Observe("PressureClass", "Class " + asme.Groups[1].Value, Source.Detail);
                if (TextUtil.IsBlank(row.PressureClass)) row.PressureClass = "Class " + asme.Groups[1].Value;
            }

            Match flange = Regex.Match(section, @"Flange Type:\s*(WN|LJ|RT|PL)\b", RegexOptions.IgnoreCase);
            if (flange.Success)
            {
                row.Observe("NozzleType", MapFlangeType(flange.Groups[1].Value), Source.Detail);
                if (TextUtil.IsBlank(row.NozzleType)) row.NozzleType = MapFlangeType(flange.Groups[1].Value);
            }

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
            foreach (Match m in BareIdRx.Matches(section))
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
