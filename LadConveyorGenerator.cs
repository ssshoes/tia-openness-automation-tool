using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;

namespace TIA_Project_v11.Autogen
{
    /// <summary>
    /// Generator for conveyor function blocks in Ladder (LAD) representation.  This class reads a
    /// CSV listing of conveyors and a template ladder XML (exported from TIA Portal) and
    /// produces a new XML with one network per conveyor.  Each network calls
    /// <c>FB_CONV_UNI</c> and wires its inputs/outputs according to the rules described below.
    ///
    /// The template file must contain exactly one <see cref="SW.Blocks.FB"/> element with
    /// a single compile unit containing a ladder <c>FlgNet</c>.  In the provided sample
    /// template (see fb_conv_uni.xml), the compile unit references three conveyor names:
    ///
    ///  * <c>M0881-R</c> – the instance of <c>FB_CONV_UNI</c> representing the current conveyor.
    ///  * <c>M0882-R</c> – the next conveyor (wired to the <c>NEXT_READY</c> input).
    ///  * <c>M0880-F</c> – the previous conveyor (wired to the <c>LOAD_REQ</c> input).
    ///
    /// Additionally, the template references constants and global arrays whose names
    /// are specific to either motor rollers (suffix "MROLLERS") or motors/conveyors
    /// (suffix "MOTORS" or "CONVS").  The generator replaces these with different
    /// constants depending on the conveyor direction: direction "R" (roller) retains
    /// the MROLLERS constants, whereas direction "F" (frequency inverter) uses
    /// motors/conveyor constants.  See the <see cref="ConstantMapR"/> and
    /// <see cref="ConstantMapF"/> dictionaries for details.
    ///
    /// Wiring rules:
    ///
    ///  * Each conveyor gets a static member declaration named <c>Mnnnn-X</c> in the FB
    ///    interface (<c>X</c> is the direction).  The member type is always
    ///    <c>FB_CONV_UNI</c> and accessibility flags mirror those in the template.
    ///  * For each conveyor, a clone of the template compile unit is created.  The
    ///    instance name (<c>M0881-R</c>) is replaced with the current conveyor name.
    ///  * The next and previous conveyor names (<c>M0882-R</c> and <c>M0880-F</c>) are
    ///    replaced with the actual next and previous conveyors from the CSV.  If the
    ///    current conveyor has no next (last in list) or has direction "F" (so
    ///    <c>NEXT_READY</c> should not be wired), the <c>NEXT_READY</c> input is
    ///    disconnected by replacing the ident connector with an open connector.
    ///  * If the current conveyor has no previous (first in list), the <c>LOAD_REQ</c>
    ///    input is disconnected similarly.
    ///  * All occurrences of the numeric index from the template (e.g. 881) in literal
    ///    array accesses are replaced with the conveyor number.
    ///  * Constant names are substituted via <see cref="ConstantMapR"/> and
    ///    <see cref="ConstantMapF"/> depending on conveyor direction.
    ///  * All ID and UID attributes within the cloned compile units are offset by a
    ///    large value to avoid collisions.  The offset increases with each conveyor.
    ///
    /// After constructing the new FB XML, it should be imported into the project via
    /// <c>plc.BlockGroup.Blocks.Import</c> with <c>ExportOptions.WithDefaults</c> or
    /// similar.  This generator does not perform the import itself; it only writes
    /// the modified XML to disk.
    /// </summary>
    public static class LadConveyorGenerator
    {
        /// <summary>
        /// Represents a conveyor entry parsed from the CSV list.
        /// </summary>
        // Using a regular class instead of a 'record' to be compatible with C# 7.3
        public class ConveyorSpec
        {
            public int Number { get; }
            public string Suffix { get; }   // R / F / FH
            public int CsvIndex { get; }

            public ConveyorSpec(int number, string suffix)
                : this(number, suffix, 0)
            {
            }

            public ConveyorSpec(int number, string suffix, int csvIndex)
            {
                Number = number;
                Suffix = suffix;
                CsvIndex = csvIndex;
            }

            public string VarName
            {
                get { return "M" + Number.ToString("0000") + "-" + Suffix; }
            }

            public bool IsRoller
            {
                get { return Suffix.Equals("R", StringComparison.OrdinalIgnoreCase); }
            }

            public bool IsInverter
            {
                get
                {
                    return Suffix.Equals("F", StringComparison.OrdinalIgnoreCase)
                        || Suffix.Equals("FH", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private class DriveSpec
        {
            public int Number { get; }
            public string Suffix { get; }
            public int CsvIndex { get; }

            public DriveSpec(int number, string suffix, int csvIndex)
            {
                Number = number;
                Suffix = suffix;
                CsvIndex = csvIndex;
            }

            public string VarName
            {
                get { return "M" + Number.ToString("0000") + "-" + Suffix; }
            }

            public bool IsConveyor
            {
                get
                {
                    return Suffix.Equals("R", StringComparison.OrdinalIgnoreCase)
                        || Suffix.Equals("F", StringComparison.OrdinalIgnoreCase)
                        || Suffix.Equals("FH", StringComparison.OrdinalIgnoreCase);
                }
            }

            public bool IsFratPart
            {
                get
                {
                    return Suffix.Equals("IR", StringComparison.OrdinalIgnoreCase)
                        || Suffix.Equals("IB", StringComparison.OrdinalIgnoreCase)
                        || Suffix.Equals("IL", StringComparison.OrdinalIgnoreCase);
                }
            }
        }

        private class FratSpec
        {
            public DriveSpec Roller { get; }
            public DriveSpec Belt { get; }
            public DriveSpec Lift { get; }

            public FratSpec(DriveSpec roller, DriveSpec belt, DriveSpec lift)
            {
                Roller = roller;
                Belt = belt;
                Lift = lift;
            }

            public string InstanceName
            {
                get
                {
                    return "M" + Roller.Number.ToString("0000") + "_"
                        + Lift.Number.ToString("0000") + "-I";
                }
            }

            public int CsvIndex
            {
                get { return Math.Min(Roller.CsvIndex, Math.Min(Belt.CsvIndex, Lift.CsvIndex)); }
            }
        }

        private class GeneratedNetwork
        {
            public int CsvIndex { get; }
            public int CloneOrdinal { get; }
            public XElement Member { get; }
            public XElement CompileUnit { get; }

            public GeneratedNetwork(int csvIndex, int cloneOrdinal, XElement member, XElement compileUnit)
            {
                CsvIndex = csvIndex;
                CloneOrdinal = cloneOrdinal;
                Member = member;
                CompileUnit = compileUnit;
            }
        }

        private class LineElement
        {
            public int CsvIndex { get; }
            public string InstanceName { get; }
            public string ReadySignalName { get; }
            public string UnloadReqSignalName { get; }

            public LineElement(int csvIndex, string instanceName, string readySignalName, string unloadReqSignalName)
            {
                CsvIndex = csvIndex;
                InstanceName = instanceName;
                ReadySignalName = readySignalName;
                UnloadReqSignalName = unloadReqSignalName;
            }
        }


        /// <summary>
        /// Constants mapping for roller conveyors (direction 'R').  Keys are names
        /// appearing in the template; values are replacement names for the generated FB.
        /// When a key is not found, the original constant name is retained.
        /// </summary>
        public static readonly IDictionary<string, string> ConstantMapR = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // For rollers we leave the names unchanged because the template already targets MROLLERS
            { "RUNTIME_MROLLERS", "RUNTIME_MROLLERS" },
            { "CLEAR_TIME_MROLLERS", "CLEAR_TIME_MROLLERS" },
            { "JAM_TIME_MROLLERS", "JAM_TIME_MROLLERS" },
            { "JAM_DETECTION_MROLLERS", "JAM_DETECTION_MROLLERS" },
            { "JAM_AUTORESET_MROLLERS", "JAM_AUTORESET_MROLLERS" },
            { "MROLLERS_UNIQUE", "MROLLERS_UNIQUE" },
            { "MROLLERS_TACTING", "MROLLERS_TACTING" },
        };

        /// <summary>
        /// Constants mapping for inverter/motor conveyors (direction 'F').  Keys are names
        /// appearing in the template; values are replacement names for the generated FB.
        /// Adjust these values to match your project's DB and constant naming scheme.
        /// </summary>
        public static readonly IDictionary<string, string> ConstantMapF = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "RUNTIME_MROLLERS", "RUNTIME_CONVS_LONG_1" },
            { "CLEAR_TIME_MROLLERS", "CLEAR_TIME_MOTORS" },
            { "JAM_TIME_MROLLERS", "JAM_TIME_MOTORS" },
            { "JAM_DETECTION_MROLLERS", "JAM_DETECTION_MOTORS" },
            { "JAM_AUTORESET_MROLLERS", "JAM_AUTORESET_MOTORS" },
            { "MROLLERS_UNIQUE", "MOTORS_UNIQUE" },
            { "MROLLERS_TACTING", "MOTORS_TACTING" },
        };

        /// <summary>
        /// Reads conveyor specifications from a CSV file.  The CSV must contain
        /// two columns: a conveyor number and a direction ("F" or "R").  The
        /// first row is treated as a header and ignored.  Blank or malformed
        /// rows are skipped.  The returned list preserves the order in which
        /// entries appear in the file; ordering is important for wiring rules.
        /// </summary>
        public static List<ConveyorSpec> ReadCsv(string csvPath)
        {
            return ReadDriveCsv(csvPath)
                .Where(d => d.IsConveyor)
                .Select(d => new ConveyorSpec(d.Number, d.Suffix, d.CsvIndex))
                .ToList();
        }

        private static List<DriveSpec> ReadDriveCsv(string csvPath)
        {
            var list = new List<DriveSpec>();
            if (!File.Exists(csvPath))
                return list;

            var rx = new Regex(@"^\s*M(?<no>\d{1,4})-(?<t>FH|IR|IB|IL|R|F)\s*$",
                RegexOptions.IgnoreCase);

            int csvIndex = 0;
            foreach (var rawLine in File.ReadLines(csvPath))
            {
                var line = (rawLine ?? "").Trim();
                if (string.IsNullOrWhiteSpace(line))
                {
                    csvIndex++;
                    continue;
                }

                var m = rx.Match(line);
                if (!m.Success)
                {
                    csvIndex++;
                    continue;
                }

                int no;
                if (!int.TryParse(m.Groups["no"].Value, out no))
                {
                    csvIndex++;
                    continue;
                }

                string suffix = m.Groups["t"].Value.ToUpperInvariant();

                list.Add(new DriveSpec(no, suffix, csvIndex));
                csvIndex++;
            }

            return list;
        }

        private static List<FratSpec> BuildFrats(IEnumerable<DriveSpec> drives, Action<string> logger)
        {
            var fratParts = drives
                .Where(d => d.IsFratPart)
                .OrderBy(d => d.CsvIndex)
                .ToList();
            var byKey = new Dictionary<string, DriveSpec>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in fratParts)
            {
                string key = part.Number.ToString(CultureInfo.InvariantCulture) + "-" + part.Suffix;
                if (!byKey.ContainsKey(key))
                    byKey.Add(key, part);
                else
                    logger?.Invoke("Duplicate FRAT CSV entry ignored: " + part.VarName);
            }

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var frats = new List<FratSpec>();
            foreach (var roller in fratParts.Where(d => d.Suffix.Equals("IR", StringComparison.OrdinalIgnoreCase)).OrderBy(d => d.Number))
            {
                string rollerKey = roller.Number.ToString(CultureInfo.InvariantCulture) + "-IR";
                if (used.Contains(rollerKey))
                    continue;

                DriveSpec belt;
                DriveSpec lift;
                string beltKey = (roller.Number + 1).ToString(CultureInfo.InvariantCulture) + "-IB";
                string liftKey = (roller.Number + 2).ToString(CultureInfo.InvariantCulture) + "-IL";
                if (byKey.TryGetValue(beltKey, out belt) && byKey.TryGetValue(liftKey, out lift))
                {
                    frats.Add(new FratSpec(roller, belt, lift));
                    used.Add(rollerKey);
                    used.Add(beltKey);
                    used.Add(liftKey);
                }
            }

            foreach (var part in fratParts)
            {
                string key = part.Number.ToString(CultureInfo.InvariantCulture) + "-" + part.Suffix;
                if (!used.Contains(key))
                    logger?.Invoke("FRAT part has no complete IR/IB/IL consecutive group and was skipped: " + part.VarName);
            }

            return frats.OrderBy(f => f.CsvIndex).ToList();
        }

        private static List<LineElement> BuildLineElements(IEnumerable<ConveyorSpec> conveyors, IEnumerable<FratSpec> frats)
        {
            var lineElements = new List<LineElement>();

            foreach (var conv in conveyors)
            {
                lineElements.Add(new LineElement(conv.CsvIndex, conv.VarName, "READY", "UNLOAD_REQ"));
            }

            foreach (var frat in frats)
            {
                lineElements.Add(new LineElement(frat.CsvIndex, frat.InstanceName, "READY_STRAIGHT", "UNLOAD_REQ_STRAIGHT"));
            }

            return lineElements
                .OrderBy(e => e.CsvIndex)
                .ThenBy(e => e.InstanceName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static LineElement FindPreviousLineElement(List<LineElement> lineElements, string instanceName)
        {
            int idx = lineElements.FindIndex(e => string.Equals(e.InstanceName, instanceName, StringComparison.OrdinalIgnoreCase));
            return idx > 0 ? lineElements[idx - 1] : null;
        }

        private static LineElement FindNextLineElement(List<LineElement> lineElements, string instanceName)
        {
            int idx = lineElements.FindIndex(e => string.Equals(e.InstanceName, instanceName, StringComparison.OrdinalIgnoreCase));
            return idx >= 0 && idx < lineElements.Count - 1 ? lineElements[idx + 1] : null;
        }



        /// <summary>
        /// Generates a modified XML file for a conveyor function block based on a
        /// template and CSV definition.  The resulting file will contain one static
        /// member and one compile unit per conveyor.  Each compile unit is a clone
        /// of the template network with names, array indices, wiring and constants
        /// adjusted for the particular conveyor.  This method does not import the
        /// generated file into a TIA project; it only writes it to disk.
        /// </summary>
        /// <param name="templateXmlPath">Path to the template ladder XML file (exported from TIA).</param>
        /// <param name="csvPath">Path to the CSV file defining conveyors.</param>
        /// <param name="outputXmlPath">Path to the file that should be created.</param>
        /// <param name="logger">Optional logging callback.</param>
        public static void GenerateXmlFromTemplate(string templateXmlPath, string csvPath, string outputXmlPath, Action<string> logger)
        {
            // Read CSV
            var drives = ReadDriveCsv(csvPath);
            var conveyors = drives
                .Where(d => d.IsConveyor)
                .Select(d => new ConveyorSpec(d.Number, d.Suffix, d.CsvIndex))
                .ToList();
            var frats = BuildFrats(drives, logger);
            var lineElements = BuildLineElements(conveyors, frats);
            if (conveyors.Count == 0 && frats.Count == 0)
            {
                logger?.Invoke("CSV file contains no valid conveyors or FRAT groups.");
                throw new InvalidOperationException("No conveyors or FRAT groups defined in CSV.");
            }

            // Load template document
            var doc = XDocument.Load(templateXmlPath, LoadOptions.PreserveWhitespace);
            // Locate the function block (FB).  The template may use a dotted element name
            // like "SW.Blocks.FB" so we look for any element whose local name is "FB" or
            // ends with ".FB" (case-insensitive).  This makes the generator robust to
            // different export formats.
            var fbNode = doc.Descendants().FirstOrDefault(e =>
     string.Equals(e.Name.LocalName, "FB", StringComparison.OrdinalIgnoreCase) ||
     e.Name.LocalName.EndsWith(".FB", StringComparison.OrdinalIgnoreCase));   // np. "SW.Blocks.FB"

            if (fbNode == null)
                throw new InvalidOperationException("Template does not contain an FB node.");


            // Find the Interface.Static section
            XNamespace ifNs = "http://www.siemens.com/automation/Openness/SW/Interface/v5";

            // <Interface> jest bez namespace, a dopiero <Sections> ma namespace ifNs
            var interfaceNode = fbNode.Descendants().FirstOrDefault(e => e.Name.LocalName == "Interface");
            if (interfaceNode == null)
                throw new InvalidOperationException("Template missing Interface node.");

            // Locate the <Sections> element directly under the Interface.  Do not search descendants,
            // because nested members (e.g. timers) also contain <Sections> elements.  Using
            // Descendants() here could pick the wrong section (e.g. inside a timer), leading to
            // attempts to add Members to an invalid parent.  See exported XML for examples.
            var sectionsNode = interfaceNode.Element(ifNs + "Sections");
            if (sectionsNode == null)
                throw new InvalidOperationException("Template missing Interface Sections node.");

            // Locate the primary Static section directly under Interface/Sections.  Do not search
            // descendants for the same reason as above.
            var staticSection = sectionsNode.Elements(ifNs + "Section")
                .FirstOrDefault(s => string.Equals((string)s.Attribute("Name"), "Static", StringComparison.OrdinalIgnoreCase));
            if (staticSection == null)
                throw new InvalidOperationException("Template missing Interface Static section.");
            var inputSection = sectionsNode.Elements(ifNs + "Section")
                .FirstOrDefault(s => string.Equals((string)s.Attribute("Name"), "Input", StringComparison.OrdinalIgnoreCase));
            if (inputSection == null)
                throw new InvalidOperationException("Template missing Interface Input section.");
            var tempSection = sectionsNode.Elements(ifNs + "Section")
                .FirstOrDefault(s => string.Equals((string)s.Attribute("Name"), "Temp", StringComparison.OrdinalIgnoreCase));
            if (tempSection == null)
                throw new InvalidOperationException("Template missing Interface Temp section.");
            EnsureInterfaceMember(inputSection, ifNs, "AUTO", "Bool");
            EnsureInterfaceMember(inputSection, ifNs, "MANUAL", "Bool");
            EnsureInterfaceMember(tempSection, ifNs, "AUTO_TMP", "Bool");
            EnsureInterfaceMember(tempSection, ifNs, "MANUAL_TMP", "Bool");

            // Find the sample static member (pattern Mxxxx-X) to clone.  Only consider direct
            // children of the interface Static section (Members nested deeper belong to
            // timer/auxiliary structures and should not be cloned).  The template should contain
            // exactly one sample member representing a conveyor.  It is identified by a name of
            // the form "Mdddd-S" where dddd is a four‑digit number and S is a direction (R, F or FH).
            var sampleMember = staticSection.Elements(ifNs + "Member")
                .FirstOrDefault(e =>
                    e.Attribute("Name") != null &&
                    Regex.IsMatch((string)e.Attribute("Name"), @"^M\d{4}-(?:R|F|FH)$", RegexOptions.IgnoreCase));
            if (conveyors.Count > 0 && sampleMember == null)
                throw new InvalidOperationException("Template missing a sample conveyor member (Mdddd-S).");
            string originalMemberName = sampleMember != null ? (string)sampleMember.Attribute("Name") : null;

            var sampleFratMember = staticSection.Elements(ifNs + "Member")
                .FirstOrDefault(e =>
                    e.Attribute("Name") != null &&
                    Regex.IsMatch((string)e.Attribute("Name"), @"^M\d{4}_\d{4}-I$", RegexOptions.IgnoreCase));
            if (frats.Count > 0 && sampleFratMember == null)
                throw new InvalidOperationException("Template missing a sample FRAT member (Mdddd_dddd-I).");

            // Derive the numeric part of the template sample member (e.g. M0881-R -> 881).  This
            // base number represents the starting index used in the template for ERR_BITS and
            // CONV_STATES arrays.  We will map our conveyor numbers back onto this base range
            // to keep array indices consistent.  If the template uses a four‑digit number with
            // leading zeros, int.Parse will handle that correctly.
            int templateBaseNumber = 0;
            var digitsMatch = Regex.Match(originalMemberName ?? string.Empty, @"^M(\d{1,4})", RegexOptions.IgnoreCase);
            if (digitsMatch.Success)
            {
                int.TryParse(digitsMatch.Groups[1].Value, out templateBaseNumber);
            }
            // Determine offset between the first conveyor's number and the template's base number.
            // This value was previously used to map conveyor numbers onto a contiguous range
            // starting at the template's base.  However, array indices should now reflect the
            // actual conveyor number (e.g. M0454 → 454).  We still compute the offset for
            // informational purposes but do not use it when assigning indices.
            int indexOffset = conveyors.Count > 0 ? conveyors[0].Number - templateBaseNumber : 0;

            // Determine the 'current', 'next' and 'prev' names in template by scanning compile unit
            var cuTemplate = fbNode.Descendants()
          .FirstOrDefault(e =>
              (string.Equals(e.Name.LocalName, "CompileUnit", StringComparison.OrdinalIgnoreCase) ||
               e.Name.LocalName.EndsWith(".CompileUnit", StringComparison.OrdinalIgnoreCase))
              &&
              e.Descendants().Any(d =>
                  string.Equals((string)d.Attribute("Name"), "FB_CONV_UNI", StringComparison.OrdinalIgnoreCase))
          );

            if (conveyors.Count > 0 && cuTemplate == null)
                throw new InvalidOperationException("Template missing compile unit containing FB_CONV_UNI call.");


            var cuFratTemplate = fbNode.Descendants()
          .FirstOrDefault(e =>
              (string.Equals(e.Name.LocalName, "CompileUnit", StringComparison.OrdinalIgnoreCase) ||
               e.Name.LocalName.EndsWith(".CompileUnit", StringComparison.OrdinalIgnoreCase))
              &&
              e.Descendants().Any(d =>
                  string.Equals((string)d.Attribute("Name"), "FB_FRAT_CBR30x_3TO3", StringComparison.OrdinalIgnoreCase))
          );

            if (frats.Count > 0 && cuFratTemplate == null)
                throw new InvalidOperationException("Template missing compile unit containing FB_FRAT_CBR30x_3TO3 call.");

            // Identify original names by regex scanning compile unit string.  Allow for FH
            string cuString = cuTemplate != null ? cuTemplate.ToString(SaveOptions.DisableFormatting) : string.Empty;
            var matchNames = Regex.Matches(cuString, @"M\d{4}-(?:FH|R|F)", RegexOptions.IgnoreCase);
            // Unique names in order of appearance
            List<string> uniqueNames = matchNames.Cast<Match>().Select(m => m.Value).Distinct().ToList();
            // Expect at least three names: current, next and prev.  If fewer, use the sample member name as fallback.
            if (conveyors.Count > 0 && uniqueNames.Count < 3)
            {
                logger?.Invoke($"Found {uniqueNames.Count} conveyor names in compile unit, expected at least 3.");
            }
            string oldCurrent = uniqueNames.ElementAtOrDefault(0) ?? originalMemberName;
            string oldNext = uniqueNames.ElementAtOrDefault(1) ?? originalMemberName;
            string oldPrev = uniqueNames.ElementAtOrDefault(2) ?? originalMemberName;

            // Build list to hold generated static members and compile units.
            var generatedNetworks = new List<GeneratedNetwork>();

            // Determine highest existing ID/UID to offset clones
            int maxId = 0;
            foreach (var attr in fbNode.DescendantsAndSelf().Attributes("ID"))
            {
                if (int.TryParse(attr.Value, out int v) && v > maxId)
                    maxId = v;
            }
            foreach (var attr in fbNode.DescendantsAndSelf().Attributes("UId"))
            {
                if (int.TryParse(attr.Value, out int v) && v > maxId)
                    maxId = v;
            }
            int idOffsetBase = maxId + 100;
            int cloneOrdinal = 0;

            for (int idx = 0; idx < conveyors.Count; idx++)
            {
                var conv = conveyors[idx];
                string currName = conv.VarName;

                // NEXT_READY takes the READY signal from the previous line element.
                // LOAD_REQ takes the UNLOAD_REQ signal from the next line element.
                // FRAT line elements use the straight lane by default.
                LineElement nextReadyElement = FindPreviousLineElement(lineElements, currName);
                LineElement loadReqElement = FindNextLineElement(lineElements, currName);
                string nextReadyName = nextReadyElement != null ? nextReadyElement.InstanceName : null;
                string loadReqName = loadReqElement != null ? loadReqElement.InstanceName : null;

                // Compute array index for ERR_BITS and CONV_STATES.  The indices should
                // reflect the actual numeric part of the conveyor name, rather than a
                // contiguous sequence derived from the template.  For example,
                // M0454-F should index `CONV_STATES[454]` and `ERR_BITS[454]`.
                int arrayIndex = conv.Number;

                // Clone the static member and update its name
                // Deep clone the sample member.  Use XElement constructor which
                // preserves the element name and attributes but does not bring
                // along namespace prefix declarations.  Afterwards normalize
                // namespace on the clone and all descendants to match the
                // interface namespace (ifNs).  Without this, cloned
                // members may carry a prefixed name (e.g. ns0:Member) that
                // causes the importer to reject them.  See NormalizeMemberNamespace.
                var memberClone = new XElement(sampleMember);
                memberClone.SetAttributeValue("Name", currName);
                // Normalize namespaces on the cloned member to avoid prefixed names
                NormalizeMemberNamespace(memberClone, ifNs);
                // Clone the compile unit and perform textual replacements
                string unitString = cuTemplate.ToString(SaveOptions.DisableFormatting);
                // 1. Replace conveyor names (current, next, prev) using regex word boundaries
                // Use explicit pattern construction to avoid escape issues
                // Current conveyor
                string patternCurr = "\\b" + Regex.Escape(oldCurrent) + "\\b";
                unitString = Regex.Replace(unitString, patternCurr, currName);
                // NEXT_READY conveyor (may be null)
                if (nextReadyName != null)
                {
                    string patternNext = "\\b" + Regex.Escape(oldNext) + "\\b";
                    unitString = Regex.Replace(unitString, patternNext, nextReadyName);
                }
                else
                {
                    // Remove NEXT_READY connection when there is no swapped neighbour.
                    // Pattern: IdentCon containing oldNext and READY
                    string pattern = "<IdentCon[^>]*>\\s*<Symbol>\\s*<Component Name=\"" + Regex.Escape(oldNext) + "\" />\\s*<Component Name=\"READY\" />\\s*</Symbol>\\s*</IdentCon>";
                    unitString = Regex.Replace(unitString,
                        pattern,
                        m =>
                        {
                            // Preserve UId if present
                            var uidMatch = Regex.Match(m.Value, "UId=\"(\\d+)\"");
                            string uid = uidMatch.Success ? uidMatch.Groups[1].Value : null;
                            return uid != null ? "<OpenCon UId=\"" + uid + "\" />" : "<OpenCon />";
                        },
                        RegexOptions.Singleline);
                }
                // LOAD_REQ source conveyor (may be null)
                if (loadReqName != null)
                {
                    string patternPrev = "\\b" + Regex.Escape(oldPrev) + "\\b";
                    unitString = Regex.Replace(unitString, patternPrev, loadReqName);
                }
                else
                {
                    // Remove UNLOAD_REQ connection when there is no swapped neighbour.
                    string pattern = "<IdentCon[^>]*>\\s*<Symbol>\\s*<Component Name=\"" + Regex.Escape(oldPrev) + "\" />\\s*<Component Name=\"UNLOAD_REQ\" />\\s*</Symbol>\\s*</IdentCon>";
                    unitString = Regex.Replace(unitString,
                        pattern,
                        m =>
                        {
                            var uidMatch = Regex.Match(m.Value, "UId=\"(\\d+)\"");
                            string uid = uidMatch.Success ? uidMatch.Groups[1].Value : null;
                            return uid != null ? "<OpenCon UId=\"" + uid + "\" />" : "<OpenCon />";
                        },
                        RegexOptions.Singleline);
                }
                // 2. Replace numeric indices: Previously, all numbers between tags were replaced with the
                // current conveyor number. This led to unintended replacements (e.g. constants, times).
                // The replacement of ERR_BITS and CONV_STATES indices is handled later by FixConstantValueIndices.
                // 3. Replace constant names depending on direction
                IDictionary<string, string> cMap = conv.IsRoller ? ConstantMapR : ConstantMapF;
                foreach (var kv in cMap)
                {
                    string constPattern = "\\b" + Regex.Escape(kv.Key) + "\\b";
                    unitString = Regex.Replace(unitString, constPattern, kv.Value, RegexOptions.IgnoreCase);
                }
                // 4. Replace sensor names Sdddd.OUT-X
                unitString = Regex.Replace(unitString, @"S\d{4}\.OUT-R", "S" + conv.Number.ToString("0000", CultureInfo.InvariantCulture) + ".OUT-R", RegexOptions.IgnoreCase);
                // 5. Replace ERR_BITS and CONV_STATES indices with the computed array index
                unitString = Regex.Replace(unitString, "ERR_BITS\\[\\d{1,4}\\]", "ERR_BITS[" + arrayIndex + "]", RegexOptions.IgnoreCase);
                unitString = Regex.Replace(unitString, "CONV_STATES\\[\\d{1,4}\\]", "CONV_STATES[" + arrayIndex + "]", RegexOptions.IgnoreCase);
                // 6. Replace RUNC references (e.g. M0881-R.RUNC).  Initially map all
                // RUNC references to the form "<conveyorName>.RUNC".  For inverter
                // conveyors (F/FH), the final target is a drive output (e.g.
                // M1003-F_OUT and subcomponent Run_fw(CW)), but that mapping is
                // handled later by the helper FixRunAndErrComponents after the
                // XML has been parsed.  Keeping the simple ".RUNC" here
                // simplifies pattern matching in that helper.
                string runcPattern = "\\b" + Regex.Escape(currName) + "\\.RUNC\\b";
                string runcReplacement = conv.VarName + ".RUNC";
                unitString = Regex.Replace(unitString, runcPattern, runcReplacement);

                // 6b. Replace ERR references.  The template uses M0881-R.ERR to drive
                // HW_ERR_NO.  We map all ERR references to "<conveyorName>.ERR" at
                // this stage.  If the conveyor is an inverter (F/FH), the final
                // target is a drive input fault (e.g. M1003-F_IN and Fault).
                // This mapping is performed later by FixRunAndErrComponents.
                string errPattern = "\\b" + Regex.Escape(currName) + "\\.ERR\\b";
                string errReplacement = conv.VarName + ".ERR";
                unitString = Regex.Replace(unitString, errPattern, errReplacement);
                // Convert back to XElement
                var unitElement = XElement.Parse(unitString, LoadOptions.PreserveWhitespace);
                if (nextReadyElement != null)
                {
                    ReplaceSignalComponent(unitElement, nextReadyElement.InstanceName, "READY", nextReadyElement.ReadySignalName);
                }
                if (loadReqElement != null)
                {
                    ReplaceSignalComponent(unitElement, loadReqElement.InstanceName, "UNLOAD_REQ", loadReqElement.UnloadReqSignalName);
                }

                // Adjust RUNC and ERR components.  At this point all occurrences
                // of "<conveyorName>.RUNC" and "<conveyorName>.ERR" have been
                // substituted, but for inverter conveyors (F/FH) those access
                // paths need to be broken into drive-specific components.  For
                // example, M1003-F.RUNC becomes M1003-F_OUT → Run_fw(CW) and
                // M1003-F.ERR becomes M1003-F_IN → Fault.  This helper also
                // leaves roller conveyors (R) unchanged.
                FixRunAndErrComponents(unitElement, conv);
                // Remove unwanted connections for NEXT_READY and UNLOAD_REQ when the swapped
                // neighbour does not exist. This is more robust than a regex: it looks
                // for any IdentCon whose Symbol contains both the conveyor name and the
                // signal name and replaces it with an OpenCon preserving the UId.
                if (nextReadyName == null)
                {
                    RemoveConnectionForSignal(unitElement, oldNext, "READY");
                }
                if (loadReqName == null)
                {
                    RemoveConnectionForSignal(unitElement, oldPrev, "UNLOAD_REQ");
                }
                // Fix constant value indices in ERR_BITS and CONV_STATES arrays.
                // We use arrayIndex (equal to conv.Number) so that each array entry
                // corresponds directly to the conveyor number (e.g. M0454 → 454).
                FixConstantValueIndices(unitElement, arrayIndex);
                // Offset IDs to avoid duplicates.  We'll add offset per conveyor
                int currentCloneOrdinal = cloneOrdinal++;
                int offset = idOffsetBase + currentCloneOrdinal * 1000;
                OffsetIds(unitElement, offset);
                // Also offset any non-numeric text IDs (e.g. "A", "B", "C") to avoid
                // duplicate Simatic ML identifiers across cloned compile units.
                OffsetTextIds(unitElement, currentCloneOrdinal);
                generatedNetworks.Add(new GeneratedNetwork(conv.CsvIndex, currentCloneOrdinal, memberClone, unitElement));
            }

            if (frats.Count > 0)
            {
                string originalFratMemberName = (string)sampleFratMember.Attribute("Name");
                string fratTemplateString = cuFratTemplate.ToString(SaveOptions.DisableFormatting);
                string oldRollerName = Regex.Match(fratTemplateString, @"M\d{4}-IR", RegexOptions.IgnoreCase).Value;
                string oldBeltName = Regex.Match(fratTemplateString, @"M\d{4}-IB", RegexOptions.IgnoreCase).Value;
                string oldLiftName = Regex.Match(fratTemplateString, @"M\d{4}-IL", RegexOptions.IgnoreCase).Value;

                if (string.IsNullOrEmpty(oldRollerName) || string.IsNullOrEmpty(oldBeltName) || string.IsNullOrEmpty(oldLiftName))
                    throw new InvalidOperationException("FRAT template must contain Mdddd-IR, Mdddd-IB and Mdddd-IL references.");

                int oldRollerNumber = ParseDriveNumber(oldRollerName);
                int oldBeltNumber = ParseDriveNumber(oldBeltName);
                int oldLiftNumber = ParseDriveNumber(oldLiftName);

                foreach (var frat in frats)
                {
                    LineElement nextReadyElement = FindPreviousLineElement(lineElements, frat.InstanceName);
                    LineElement loadReqElement = FindNextLineElement(lineElements, frat.InstanceName);
                    var memberClone = new XElement(sampleFratMember);
                    memberClone.SetAttributeValue("Name", frat.InstanceName);
                    NormalizeMemberNamespace(memberClone, ifNs);

                    string unitString = fratTemplateString;
                    unitString = unitString.Replace(originalFratMemberName, frat.InstanceName);
                    unitString = unitString.Replace(oldRollerName, frat.Roller.VarName);
                    unitString = unitString.Replace(oldBeltName, frat.Belt.VarName);
                    unitString = unitString.Replace(oldLiftName, frat.Lift.VarName);
                    unitString = ReplaceSensorNumber(unitString, oldRollerNumber, frat.Roller.Number);
                    unitString = ReplaceSensorNumber(unitString, oldBeltNumber, frat.Belt.Number);
                    unitString = ReplaceSensorNumber(unitString, oldLiftNumber, frat.Lift.Number);

                    var unitElement = XElement.Parse(unitString, LoadOptions.PreserveWhitespace);
                    var numberMap = new Dictionary<int, int>
                    {
                        { oldRollerNumber, frat.Roller.Number },
                        { oldBeltNumber, frat.Belt.Number },
                        { oldLiftNumber, frat.Lift.Number },
                    };
                    FixArrayIndicesByNumberMap(unitElement, numberMap);
                    ConnectCallInputToLineElement(unitElement, "NEXT_READY_STRAIGHT", nextReadyElement, true);
                    ConnectCallInputToLineElement(unitElement, "LOAD_REQ_STRAIGHT", loadReqElement, false);
                    ConnectCallInputToLiteral(unitElement, "DEFAULT_EXIT", "Word", "1");

                    int currentCloneOrdinal = cloneOrdinal++;
                    int offset = idOffsetBase + currentCloneOrdinal * 1000;
                    OffsetIds(unitElement, offset);
                    OffsetTextIds(unitElement, currentCloneOrdinal);
                    generatedNetworks.Add(new GeneratedNetwork(frat.CsvIndex, currentCloneOrdinal, memberClone, unitElement));
                }
            }
            // Remove existing sample members (pattern Mxxxx-X) to avoid duplicates. The template
            // may include multiple sample members (e.g. M0880-F, M0881-R, etc.). We remove
            // all members matching M\d{4}-[FR] so that only the newly generated members remain.
            // Remove any existing sample members (Mdddd-R/F/FH) from the interface Static section.
            // Only direct children of the Static section are considered to avoid removing
            // nested Members belonging to timers or other substructures.
            foreach (var member in staticSection.Elements(ifNs + "Member").ToList())
            {
                string nm = (string)member.Attribute("Name");
                if (nm != null && (Regex.IsMatch(nm, @"^M\d{4}-(?:R|F|FH)$", RegexOptions.IgnoreCase)
                    || Regex.IsMatch(nm, @"^M\d{4}_\d{4}-I$", RegexOptions.IgnoreCase)))
                {
                    member.Remove();
                }
            }

            if (conveyors.Count > 0)
            {
                // Find Constant section
                var constantSection = fbNode
                    .Descendants()
                    .FirstOrDefault(e =>
                        e.Name.LocalName == "Section" &&
                        (string)e.Attribute("Name") == "Constant");

                if (constantSection == null)
                {
                    if (sectionsNode == null)
                        throw new InvalidOperationException("Interface missing Sections element.");

                    constantSection = new XElement(ifNs + "Section",
                        new XAttribute("Name", "Constant"));

                    sectionsNode.Add(constantSection);
                }

                // Add MOTORS_UNIQUE if missing
                bool hasMotorsUnique = constantSection.Elements(ifNs + "Member")
                    .Any(e => (string)e.Attribute("Name") == "MOTORS_UNIQUE");

                if (!hasMotorsUnique)
                {
                    constantSection.Add(
                        new XElement(ifNs + "Member",
                            new XAttribute("Name", "MOTORS_UNIQUE"),
                            new XAttribute("Datatype", "Bool"),
                            new XAttribute("Accessibility", "Public"),
                            new XElement(ifNs + "StartValue", "false")
                        )
                    );
                }

                // Add MOTORS_TACTING if missing
                bool hasMotorsTacting = constantSection.Elements(ifNs + "Member")
                    .Any(e => (string)e.Attribute("Name") == "MOTORS_TACTING");

                if (!hasMotorsTacting)
                {
                    constantSection.Add(
                        new XElement(ifNs + "Member",
                            new XAttribute("Name", "MOTORS_TACTING"),
                            new XAttribute("Datatype", "Bool"),
                            new XAttribute("Accessibility", "Public"),
                            new XElement(ifNs + "StartValue", "false")
                        )
                    );
                }

                bool hasSpeedMrollers1 = constantSection.Elements(ifNs + "Member")
                    .Any(e => (string)e.Attribute("Name") == "SPEED_MROLLERS_1");

                if (conveyors.Any(c => c.IsRoller) && !hasSpeedMrollers1)
                {
                    constantSection.Add(
                        new XElement(ifNs + "Member",
                            new XAttribute("Name", "SPEED_MROLLERS_1"),
                            new XAttribute("Datatype", "USInt"),
                            new XAttribute("Accessibility", "Public"),
                            new XElement(ifNs + "StartValue", "50")
                        )
                    );
                }

                bool hasSpeedMotors1 = constantSection.Elements(ifNs + "Member")
                    .Any(e => (string)e.Attribute("Name") == "SPEED_MOTORS_1");

                if (conveyors.Any(c => c.IsInverter) && !hasSpeedMotors1)
                {
                    constantSection.Add(
                        new XElement(ifNs + "Member",
                            new XAttribute("Name", "SPEED_MOTORS_1"),
                            new XAttribute("Datatype", "Int"),
                            new XAttribute("Accessibility", "Public"),
                            new XElement(ifNs + "StartValue", "5000")
                        )
                    );
                }
            }


            var orderedGeneratedNetworks = generatedNetworks
                .OrderBy(g => g.CsvIndex)
                .ThenBy(g => g.CloneOrdinal)
                .ToList();

            // Append new members
            foreach (var item in orderedGeneratedNetworks)
            {
                staticSection.Add(item.Member);
            }
            // Find ObjectList in FB
            var objectList = fbNode.Elements().FirstOrDefault(e => e.Name.LocalName == "ObjectList");
            if (objectList == null)
                throw new InvalidOperationException("FB missing ObjectList element.");
            // Remove all existing compile units before adding the new ones.  The
            // TIA importer requires that the 'CompileUnits' composition be
            // completely empty; otherwise it reports that objects modeled as
            // IOrdered must be empty.  We therefore remove *every* compile
            // unit from the ObjectList (regardless of its content) prior to
            // appending our generated networks.
            foreach (var cu in objectList.Elements().Where(e =>
                string.Equals(e.Name.LocalName, "CompileUnit", StringComparison.OrdinalIgnoreCase) ||
                e.Name.LocalName.EndsWith(".CompileUnit", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                cu.Remove();
            }
            var autoManualTmpUnit = CreateAutoManualTmpCompileUnit(fbNode, idOffsetBase + (cloneOrdinal + 1) * 1000);
            objectList.Add(autoManualTmpUnit);
            var mrollerSpeedDirUnit = CreateMrollerSpeedDirCompileUnit(fbNode, conveyors.Where(c => c.IsRoller).ToList(), idOffsetBase + (cloneOrdinal + 2) * 1000);
            if (mrollerSpeedDirUnit != null)
            {
                objectList.Add(mrollerSpeedDirUnit);
            }
            var inverterSpeedUnit = CreateInverterSpeedCompileUnit(fbNode, conveyors.Where(c => c.IsInverter).ToList(), idOffsetBase + (cloneOrdinal + 3) * 1000);
            if (inverterSpeedUnit != null)
            {
                objectList.Add(inverterSpeedUnit);
            }
            // Append new compile units (in order of conveyors)
            foreach (var item in orderedGeneratedNetworks)
            {
                objectList.Add(item.CompileUnit);
            }
            // Write to output.  Do not renumber the entire document (NormalizeAllIds), because the
            // root FB element must retain its original ID (usually "0") for the importer.  IDs
            // within cloned compile units have already been offset per conveyor via OffsetIds
            // and OffsetTextIds, ensuring uniqueness.
            doc.Save(outputXmlPath);
            logger?.Invoke($"Generated ladder XML: {outputXmlPath}");
        }

        /// <summary>
        /// Adds a fixed offset to all numeric ID and UId attributes in a given XElement.
        /// This prevents collisions when cloning compile units.  The offset is
        /// added to any attribute value that parses as an integer.
        /// </summary>
        private static void OffsetIds(XElement element, int offset)
        {
            foreach (var attr in element.DescendantsAndSelf().Attributes())
            {
                if (attr.Name.LocalName.Equals("ID", StringComparison.OrdinalIgnoreCase) ||
                    attr.Name.LocalName.Equals("UId", StringComparison.OrdinalIgnoreCase))
                {
                    if (int.TryParse(attr.Value, out int v))
                    {
                        attr.Value = (v + offset).ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
        }
        private static void NormalizeAllIds(XDocument doc)
        {
            // Startujemy od 1, żeby nie było ID="0"
            int nextId = 1;
            int nextUId = 1;

            foreach (var el in doc.Descendants())
            {
                var idAttr = el.Attributes().FirstOrDefault(a => a.Name.LocalName == "ID");
                if (idAttr != null)
                {
                    idAttr.Value = nextId.ToString(CultureInfo.InvariantCulture);
                    nextId++;
                }

                var uidAttr = el.Attributes().FirstOrDefault(a => a.Name.LocalName.Equals("UId", StringComparison.OrdinalIgnoreCase));
                if (uidAttr != null)
                {
                    uidAttr.Value = nextUId.ToString(CultureInfo.InvariantCulture);
                    nextUId++;
                }
            }
        }

        private static void EnsureInterfaceMember(XElement section, XNamespace ifNs, string name, string datatype)
        {
            bool exists = section.Elements(ifNs + "Member")
                .Any(e => string.Equals((string)e.Attribute("Name"), name, StringComparison.OrdinalIgnoreCase));
            if (!exists)
            {
                section.Add(new XElement(ifNs + "Member",
                    new XAttribute("Name", name),
                    new XAttribute("Datatype", datatype)));
            }
        }

        private static XElement CreateAutoManualTmpCompileUnit(XElement fbNode, int idBase)
        {
            XNamespace flgNs = "http://www.siemens.com/automation/Openness/SW/NetworkSource/FlgNet/v5";
            string compileUnitName = GetCompileUnitElementName(fbNode);
            int id = idBase;
            int uid = idBase + 100;

            int autoAccess = uid++;
            int autoTmpAccess = uid++;
            int manualAccess = uid++;
            int manualTmpAccess = uid++;
            int autoContact = uid++;
            int autoCoil = uid++;
            int manualContact = uid++;
            int manualCoil = uid++;
            int wire = uid++;

            var flgNet = new XElement(flgNs + "FlgNet",
                new XElement(flgNs + "Parts",
                    CreateLocalVariableAccess(flgNs, autoAccess, "AUTO"),
                    CreateLocalVariableAccess(flgNs, autoTmpAccess, "AUTO_TMP"),
                    CreateLocalVariableAccess(flgNs, manualAccess, "MANUAL"),
                    CreateLocalVariableAccess(flgNs, manualTmpAccess, "MANUAL_TMP"),
                    new XElement(flgNs + "Part", new XAttribute("Name", "Contact"), new XAttribute("UId", autoContact.ToString(CultureInfo.InvariantCulture))),
                    new XElement(flgNs + "Part", new XAttribute("Name", "Coil"), new XAttribute("UId", autoCoil.ToString(CultureInfo.InvariantCulture))),
                    new XElement(flgNs + "Part", new XAttribute("Name", "Contact"), new XAttribute("UId", manualContact.ToString(CultureInfo.InvariantCulture))),
                    new XElement(flgNs + "Part", new XAttribute("Name", "Coil"), new XAttribute("UId", manualCoil.ToString(CultureInfo.InvariantCulture)))),
                new XElement(flgNs + "Wires",
                    CreatePowerrailWire(flgNs, wire++, autoContact, manualContact),
                    CreateOperandWire(flgNs, wire++, autoAccess, autoContact),
                    CreateContactToCoilWire(flgNs, wire++, autoContact, autoCoil),
                    CreateOperandWire(flgNs, wire++, autoTmpAccess, autoCoil),
                    CreateOperandWire(flgNs, wire++, manualAccess, manualContact),
                    CreateContactToCoilWire(flgNs, wire++, manualContact, manualCoil),
                    CreateOperandWire(flgNs, wire++, manualTmpAccess, manualCoil)));

            return new XElement(compileUnitName,
                new XAttribute("ID", (id++).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("CompositionName", "CompileUnits"),
                new XElement("AttributeList",
                    new XElement("NetworkSource", flgNet),
                    new XElement("ProgrammingLanguage", "LAD")),
                new XElement("ObjectList",
                    CreateMultilingualText("Comment", id++, id++, string.Empty),
                    CreateMultilingualText("Title", id++, id++, "AUTO MANUAL TMP")));
        }

        private static XElement CreateLocalVariableAccess(XNamespace ns, int uid, string name)
        {
            return new XElement(ns + "Access",
                new XAttribute("Scope", "LocalVariable"),
                new XAttribute("UId", uid.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "Symbol",
                    new XElement(ns + "Component", new XAttribute("Name", name))));
        }

        private static XElement CreatePowerrailWire(XNamespace ns, int uid, params int[] contactUids)
        {
            var wire = new XElement(ns + "Wire",
                new XAttribute("UId", uid.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "Powerrail"));

            foreach (var contactUid in contactUids)
            {
                wire.Add(new XElement(ns + "NameCon",
                    new XAttribute("UId", contactUid.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("Name", "in")));
            }

            return wire;
        }

        private static XElement CreateOperandWire(XNamespace ns, int uid, int accessUid, int partUid)
        {
            return new XElement(ns + "Wire",
                new XAttribute("UId", uid.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "IdentCon", new XAttribute("UId", accessUid.ToString(CultureInfo.InvariantCulture))),
                new XElement(ns + "NameCon",
                    new XAttribute("UId", partUid.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("Name", "operand")));
        }

        private static XElement CreateContactToCoilWire(XNamespace ns, int uid, int contactUid, int coilUid)
        {
            return new XElement(ns + "Wire",
                new XAttribute("UId", uid.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "NameCon",
                    new XAttribute("UId", contactUid.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("Name", "out")),
                new XElement(ns + "NameCon",
                    new XAttribute("UId", coilUid.ToString(CultureInfo.InvariantCulture)),
                    new XAttribute("Name", "in")));
        }

        private static XElement CreateMrollerSpeedDirCompileUnit(XElement fbNode, IList<ConveyorSpec> rollers, int idBase)
        {
            if (rollers == null || rollers.Count == 0)
                return null;

            XNamespace stNs = "http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v4";
            string compileUnitName = GetCompileUnitElementName(fbNode);
            int id = idBase;
            int uid = idBase + 100;

            var structuredText = new XElement(stNs + "StructuredText");
            AddSclLine(structuredText, ref uid, "IF NOT #MANUAL_TMP THEN");
            foreach (var roller in rollers)
            {
                AddSclLine(structuredText, ref uid, "    \"" + roller.VarName + ".SPDC\" := #SPEED_MROLLERS_1;");
            }

            AddSclBlankLine(structuredText, ref uid);

            foreach (var roller in rollers)
            {
                AddSclLine(structuredText, ref uid, "    \"" + roller.VarName + ".DIRC\" := FALSE;");
            }
            AddSclLine(structuredText, ref uid, "END_IF;");

            return new XElement(compileUnitName,
                new XAttribute("ID", (id++).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("CompositionName", "CompileUnits"),
                new XElement("AttributeList",
                    new XElement("NetworkSource", structuredText),
                    new XElement("ProgrammingLanguage", "SCL")),
                new XElement("ObjectList",
                    CreateMultilingualText("Comment", id++, id++, string.Empty),
                    CreateMultilingualText("Title", id++, id++, "mrollers speed & dir set")));
        }

        private static XElement CreateInverterSpeedCompileUnit(XElement fbNode, IList<ConveyorSpec> inverters, int idBase)
        {
            if (inverters == null || inverters.Count == 0)
                return null;

            XNamespace stNs = "http://www.siemens.com/automation/Openness/SW/NetworkSource/StructuredText/v4";
            string compileUnitName = GetCompileUnitElementName(fbNode);
            int id = idBase;
            int uid = idBase + 100;

            var structuredText = new XElement(stNs + "StructuredText");
            AddSclLine(structuredText, ref uid, "IF NOT #MANUAL_TMP THEN");
            foreach (var inverter in inverters)
            {
                AddSclLine(structuredText, ref uid, "    \"" + inverter.VarName + "_OUT\".Frequency_setpoint := #SPEED_MOTORS_1;");
            }
            AddSclLine(structuredText, ref uid, "END_IF;");

            return new XElement(compileUnitName,
                new XAttribute("ID", (id++).ToString(CultureInfo.InvariantCulture)),
                new XAttribute("CompositionName", "CompileUnits"),
                new XElement("AttributeList",
                    new XElement("NetworkSource", structuredText),
                    new XElement("ProgrammingLanguage", "SCL")),
                new XElement("ObjectList",
                    CreateMultilingualText("Comment", id++, id++, string.Empty),
                    CreateMultilingualText("Title", id++, id++, "INVERTERS SPEED SET")));
        }

        private static string GetCompileUnitElementName(XElement fbNode)
        {
            var template = fbNode.Descendants()
                .FirstOrDefault(e =>
                    string.Equals(e.Name.LocalName, "CompileUnit", StringComparison.OrdinalIgnoreCase) ||
                    e.Name.LocalName.EndsWith(".CompileUnit", StringComparison.OrdinalIgnoreCase));

            return template != null ? template.Name.LocalName : "SW.Blocks.CompileUnit";
        }

        private static XElement CreateMultilingualText(string compositionName, int textId, int itemId, string text)
        {
            return new XElement("MultilingualText",
                new XAttribute("ID", textId.ToString(CultureInfo.InvariantCulture)),
                new XAttribute("CompositionName", compositionName),
                new XElement("ObjectList",
                    new XElement("MultilingualTextItem",
                        new XAttribute("ID", itemId.ToString(CultureInfo.InvariantCulture)),
                        new XAttribute("CompositionName", "Items"),
                        new XElement("AttributeList",
                            new XElement("Culture", "en-US"),
                            string.IsNullOrEmpty(text) ? new XElement("Text") : new XElement("Text", text)))));
        }

        private static void AddSclLine(XElement structuredText, ref int uid, string text)
        {
            XNamespace ns = structuredText.Name.Namespace;
            structuredText.Add(new XElement(ns + "Text",
                new XAttribute("UId", (uid++).ToString(CultureInfo.InvariantCulture)),
                text));
            structuredText.Add(new XElement(ns + "NewLine",
                new XAttribute("Num", "1"),
                new XAttribute("UId", (uid++).ToString(CultureInfo.InvariantCulture))));
        }

        private static void AddSclBlankLine(XElement structuredText, ref int uid)
        {
            XNamespace ns = structuredText.Name.Namespace;
            structuredText.Add(new XElement(ns + "NewLine",
                new XAttribute("Num", "1"),
                new XAttribute("UId", (uid++).ToString(CultureInfo.InvariantCulture))));
        }

        /// <summary>
        /// Offsets non-numeric IDs on multilingual text elements to avoid duplicate
        /// Simatic ML identifiers.  Any ID attribute whose value is not an integer
        /// will be suffixed with an underscore and the specified index.  This
        /// ensures uniqueness across cloned compile units.  Numeric IDs are left
        /// untouched (they are handled by <see cref="OffsetIds"/>).
        /// </summary>
        /// <param name="element">The compile unit or element to process.</param>
        /// <param name="index">The unique index used for suffixing.</param>
        private static void OffsetTextIds(XElement element, int index)
        {
            // Build suffix once to avoid repeated ToString conversions
            string suffix = "_" + index.ToString(CultureInfo.InvariantCulture);
            foreach (var attr in element.DescendantsAndSelf().Attributes("ID"))
            {
                // Only modify IDs that are not purely numeric
                if (!int.TryParse(attr.Value, out int _))
                {
                    attr.Value = attr.Value + suffix;
                }
            }
        }

        /// <summary>
        /// Fixes constant value indices for ERR_BITS and CONV_STATES arrays inside a compile unit.
        /// These arrays are indexed by the conveyor number. This helper traverses the cloned
        /// compile unit and replaces the value of ConstantValue elements whose parent
        /// Component has Name="ERR_BITS" or "CONV_STATES". Without this targeted replacement,
        /// generic numeric substitutions could corrupt other numeric values such as times.
        /// </summary>
        /// <param name="element">The root element of the cloned compile unit.</param>
        /// <param name="number">The conveyor number to insert as the index.</param>
        private static void FixConstantValueIndices(XElement element, int number)
        {
            // Traverse all Component elements
            foreach (var comp in element.Descendants().Where(e => e.Name.LocalName == "Component"))
            {
                var compName = (string)comp.Attribute("Name");
                if (string.Equals(compName, "ERR_BITS", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(compName, "CONV_STATES", StringComparison.OrdinalIgnoreCase))
                {
                    // Within this component, find the ConstantValue element and update it
                    var constValue = comp.Descendants().FirstOrDefault(e => e.Name.LocalName == "ConstantValue");
                    if (constValue != null)
                    {
                        constValue.Value = number.ToString(CultureInfo.InvariantCulture);
                    }
                }
            }
        }

        private static int ParseDriveNumber(string driveName)
        {
            var match = Regex.Match(driveName ?? string.Empty, @"^M(?<no>\d{1,4})-", RegexOptions.IgnoreCase);
            if (!match.Success)
                return 0;

            int number;
            return int.TryParse(match.Groups["no"].Value, out number) ? number : 0;
        }

        private static string ReplaceSensorNumber(string text, int oldNumber, int newNumber)
        {
            if (oldNumber <= 0 || oldNumber == newNumber)
                return text;

            string oldSensor = "S" + oldNumber.ToString("0000", CultureInfo.InvariantCulture);
            string newSensor = "S" + newNumber.ToString("0000", CultureInfo.InvariantCulture);
            return Regex.Replace(text, "\\b" + Regex.Escape(oldSensor) + "(?=\\.)", newSensor, RegexOptions.IgnoreCase);
        }

        private static void FixArrayIndicesByNumberMap(XElement element, IDictionary<int, int> numberMap)
        {
            if (numberMap == null || numberMap.Count == 0)
                return;

            foreach (var comp in element.Descendants().Where(e => e.Name.LocalName == "Component"))
            {
                var compName = (string)comp.Attribute("Name");
                if (!string.Equals(compName, "ERR_BITS", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(compName, "CONV_STATES", StringComparison.OrdinalIgnoreCase))
                    continue;

                var constValue = comp.Descendants().FirstOrDefault(e => e.Name.LocalName == "ConstantValue");
                if (constValue == null)
                    continue;

                int oldNumber;
                int newNumber;
                if (int.TryParse((constValue.Value ?? string.Empty).Trim(), out oldNumber) &&
                    numberMap.TryGetValue(oldNumber, out newNumber))
                {
                    constValue.Value = newNumber.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private static void ReplaceSignalComponent(XElement root, string instanceName, string oldSignalName, string newSignalName)
        {
            if (string.IsNullOrEmpty(instanceName) ||
                string.IsNullOrEmpty(oldSignalName) ||
                string.IsNullOrEmpty(newSignalName) ||
                string.Equals(oldSignalName, newSignalName, StringComparison.OrdinalIgnoreCase))
                return;

            foreach (var symbol in root.Descendants().Where(e => e.Name.LocalName == "Symbol"))
            {
                var components = symbol.Elements().Where(e => e.Name.LocalName == "Component").ToList();
                if (components.Count < 2)
                    continue;

                bool hasInstance = components.Any(c => string.Equals((string)c.Attribute("Name"), instanceName, StringComparison.OrdinalIgnoreCase));
                if (!hasInstance)
                    continue;

                foreach (var component in components)
                {
                    var nameAttr = component.Attribute("Name");
                    if (nameAttr != null && string.Equals(nameAttr.Value, oldSignalName, StringComparison.OrdinalIgnoreCase))
                    {
                        nameAttr.Value = newSignalName;
                    }
                }
            }
        }

        private static void ConnectCallInputToLineElement(XElement unitElement, string parameterName, LineElement target, bool useReadySignal)
        {
            if (target == null)
                return;

            string signalName = useReadySignal ? target.ReadySignalName : target.UnloadReqSignalName;
            XElement access = CreateSignalAccess(unitElement, target.InstanceName, signalName, out int uid);
            if (access == null)
                return;

            AddAccessAndReplaceOpenConnector(unitElement, parameterName, access, uid);
        }

        private static void ConnectCallInputToLiteral(XElement unitElement, string parameterName, string constantType, string constantValue)
        {
            XElement access = CreateLiteralAccess(unitElement, constantType, constantValue, out int uid);
            if (access == null)
                return;

            AddAccessAndReplaceOpenConnector(unitElement, parameterName, access, uid);
        }

        private static XElement CreateSignalAccess(XElement unitElement, string instanceName, string signalName, out int uid)
        {
            uid = GetNextOrReusableOpenConnectorUid(unitElement, null);
            var parts = GetPartsElement(unitElement);
            if (parts == null || uid <= 0)
                return null;

            XNamespace ns = parts.Name.Namespace;
            return new XElement(ns + "Access",
                new XAttribute("Scope", "LocalVariable"),
                new XAttribute("UId", uid.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "Symbol",
                    new XElement(ns + "Component", new XAttribute("Name", instanceName)),
                    new XElement(ns + "Component", new XAttribute("Name", signalName))));
        }

        private static XElement CreateLiteralAccess(XElement unitElement, string constantType, string constantValue, out int uid)
        {
            uid = GetNextOrReusableOpenConnectorUid(unitElement, null);
            var parts = GetPartsElement(unitElement);
            if (parts == null || uid <= 0)
                return null;

            XNamespace ns = parts.Name.Namespace;
            return new XElement(ns + "Access",
                new XAttribute("Scope", "LiteralConstant"),
                new XAttribute("UId", uid.ToString(CultureInfo.InvariantCulture)),
                new XElement(ns + "Constant",
                    new XElement(ns + "ConstantType", constantType),
                    new XElement(ns + "ConstantValue", constantValue)));
        }

        private static void AddAccessAndReplaceOpenConnector(XElement unitElement, string parameterName, XElement access, int uid)
        {
            var parts = GetPartsElement(unitElement);
            if (parts == null || access == null || uid <= 0)
                return;

            XElement wire = FindWireForParameter(unitElement, parameterName);
            if (wire == null)
                return;

            XElement openCon = wire.Elements().FirstOrDefault(e => e.Name.LocalName == "OpenCon");
            if (openCon == null)
                return;

            string openUid = (string)openCon.Attribute("UId");
            if (!string.IsNullOrEmpty(openUid))
            {
                access.SetAttributeValue("UId", openUid);
                int parsedUid;
                if (int.TryParse(openUid, out parsedUid))
                    uid = parsedUid;
            }

            if (!parts.Elements().Any(e => string.Equals((string)e.Attribute("UId"), uid.ToString(CultureInfo.InvariantCulture), StringComparison.OrdinalIgnoreCase)))
            {
                var firstNonAccess = parts.Elements().FirstOrDefault(e => e.Name.LocalName != "Access");
                if (firstNonAccess != null)
                    firstNonAccess.AddBeforeSelf(access);
                else
                    parts.Add(access);
            }

            XNamespace ns = wire.Name.Namespace;
            openCon.ReplaceWith(new XElement(ns + "IdentCon", new XAttribute("UId", uid.ToString(CultureInfo.InvariantCulture))));
        }

        private static XElement FindWireForParameter(XElement unitElement, string parameterName)
        {
            return unitElement.Descendants()
                .FirstOrDefault(e =>
                    e.Name.LocalName == "Wire" &&
                    e.Elements().Any(c =>
                        c.Name.LocalName == "NameCon" &&
                        string.Equals((string)c.Attribute("Name"), parameterName, StringComparison.OrdinalIgnoreCase)));
        }

        private static XElement GetPartsElement(XElement unitElement)
        {
            return unitElement.Descendants().FirstOrDefault(e => e.Name.LocalName == "Parts");
        }

        private static int GetNextOrReusableOpenConnectorUid(XElement unitElement, string parameterName)
        {
            if (!string.IsNullOrEmpty(parameterName))
            {
                XElement wire = FindWireForParameter(unitElement, parameterName);
                XElement openCon = wire != null ? wire.Elements().FirstOrDefault(e => e.Name.LocalName == "OpenCon") : null;
                int openUid;
                if (openCon != null && int.TryParse((string)openCon.Attribute("UId"), out openUid))
                    return openUid;
            }

            int maxUid = 0;
            foreach (var attr in unitElement.DescendantsAndSelf().Attributes("UId"))
            {
                int value;
                if (int.TryParse(attr.Value, out value) && value > maxUid)
                    maxUid = value;
            }

            return maxUid + 1;
        }

        /// <summary>
        /// Fixes access paths for RUNC and ERR variables inside a compile unit.
        /// All occurrences of "<conveyorName>.RUNC" and "<conveyorName>.ERR"
        /// are already present in the XML element tree when this helper is
        /// called.  For roller conveyors (suffix 'R'), no changes are made.
        /// For inverter conveyors (suffix 'F' or 'FH'), the helper replaces
        /// each Component whose Name matches "<conveyorName>.RUNC" with
        /// two Components: the first having Name="<conveyorName>_OUT", and
        /// the second having Name="Run_fw(CW)".  Likewise, each
        /// Component whose Name matches "<conveyorName>.ERR" is replaced
        /// with Name="<conveyorName>_IN" followed by Name="Fault".
        /// This avoids embedding quotes inside the Name attribute and
        /// aligns with TIA's expected symbol structure.
        /// </summary>
        /// <param name="unitElement">The root element of the compile unit.</param>
        /// <param name="conv">The conveyor definition used to determine
        /// whether it is an inverter and to obtain the VarName.</param>
        private static void FixRunAndErrComponents(XElement unitElement, ConveyorSpec conv)
        {
            // Only inverter conveyors need adjustment; roller conveyors already
            // have the correct access path.  conv.VarName yields the fully
            // qualified name including suffix (e.g. M1003-F or M1003-FH).
            if (!conv.IsInverter)
                return;
            // Build search targets for RUNC and ERR
            string runcTarget = conv.VarName + ".RUNC";
            string errTarget = conv.VarName + ".ERR";
            // Iterate through all Component elements
            foreach (var comp in unitElement.Descendants().Where(e => e.Name.LocalName == "Component").ToList())
            {
                var nameAttr = comp.Attribute("Name");
                if (nameAttr == null)
                    continue;
                string nameVal = nameAttr.Value;
                // Use ordinal comparison to match exactly
                if (string.Equals(nameVal, runcTarget, StringComparison.Ordinal))
                {
                    // Change this component to <VarName>_OUT and insert Run_fw(CW) after
                    nameAttr.Value = conv.VarName + "_OUT";
                    // Create new component with the same namespace as current
                    XElement newComp = new XElement(comp.Name.Namespace + "Component");
                    newComp.SetAttributeValue("Name", "Run_fw(CW)");
                    comp.AddAfterSelf(newComp);
                }
                else if (string.Equals(nameVal, errTarget, StringComparison.Ordinal))
                {
                    // Change this component to <VarName>_IN and insert Fault after
                    nameAttr.Value = conv.VarName + "_IN";
                    XElement newComp = new XElement(comp.Name.Namespace + "Component");
                    newComp.SetAttributeValue("Name", "Fault");
                    comp.AddAfterSelf(newComp);
                }
            }
        }

        /// <summary>
        /// Removes an IdentCon connection in a compile unit that connects a specific
        /// conveyor instance to a given signal (e.g. READY or UNLOAD_REQ) and
        /// replaces it with an OpenCon while preserving the UId attribute.  This
        /// helper parses the XML structure rather than using regular expressions,
        /// making it resilient to whitespace and attribute ordering.  It only
        /// affects IdentCon elements that contain both the specified conveyor
        /// name and signal name within their Symbol/Component elements.
        /// </summary>
        /// <param name="root">The root element of the compile unit to process.</param>
        /// <param name="conveyorName">The conveyor instance name to match (e.g. "M0881-R").</param>
        /// <param name="signalName">The signal name to match (e.g. "READY" or "UNLOAD_REQ").</param>
        private static void RemoveConnectionForSignal(XElement root, string conveyorName, string signalName)
        {
            if (string.IsNullOrEmpty(conveyorName) || string.IsNullOrEmpty(signalName))
                return;
            // Find all IdentCon elements in the unit
            var identCons = root.Descendants().Where(e => string.Equals(e.Name.LocalName, "IdentCon", StringComparison.OrdinalIgnoreCase)).ToList();
            foreach (var identCon in identCons)
            {
                // Collect all Component elements under this IdentCon
                var components = identCon.Descendants().Where(e => e.Name.LocalName == "Component").ToList();
                bool hasConveyor = components.Any(c => string.Equals((string)c.Attribute("Name"), conveyorName, StringComparison.OrdinalIgnoreCase));
                bool hasSignal = components.Any(c => string.Equals((string)c.Attribute("Name"), signalName, StringComparison.OrdinalIgnoreCase));
                if (hasConveyor && hasSignal)
                {
                    // Preserve existing UId if present
                    var uidAttr = identCon.Attribute("UId");
                    string uid = uidAttr != null ? uidAttr.Value : null;
                    // Create an OpenCon element in the same namespace as IdentCon
                    XNamespace ns = identCon.Name.Namespace;
                    var openCon = new XElement(ns + "OpenCon");
                    if (!string.IsNullOrEmpty(uid))
                    {
                        openCon.SetAttributeValue("UId", uid);
                    }
                    // Replace the IdentCon with OpenCon
                    identCon.ReplaceWith(openCon);
                }
            }
        }

        /// <summary>
        /// Recursively assigns the specified namespace to the given element and all of
        /// its descendants.  This helper is used to ensure that cloned interface
        /// members use the same namespace as the surrounding Interface sections.  If
        /// the cloned elements retain a prefix (e.g. ns0:Member) but the prefix
        /// definition is not in scope at the point of insertion, TIA importer
        /// rejects the XML with an error like "It is not possible to add
        /// 'ns0:Member' to the parent element".  Assigning the uniform
        /// namespace ensures the default (unprefixed) namespace applies.
        /// </summary>
        /// <param name="element">Root element to normalize.</param>
        /// <param name="ns">The namespace to assign.</param>
        private static void NormalizeMemberNamespace(XElement element, XNamespace ns)
        {
            // Change the element name to use the provided namespace with the same local name
            element.Name = ns + element.Name.LocalName;
            // Recursively normalize all child elements
            foreach (var desc in element.Elements())
            {
                NormalizeMemberNamespace(desc, ns);
            }
        }
    }
}
