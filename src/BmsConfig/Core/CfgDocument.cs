using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace BmsConfig.Core
{
    public enum CfgLineKind
    {
        /// <summary>Blank line, comment, or anything that doesn't conform to the grammar.</summary>
        Other,
        /// <summary>A comment line of the form "// Some Category //".</summary>
        Category,
        /// <summary>A well-formed "set {name} {value}" line.</summary>
        Set,
        /// <summary>The Launcher's cut line, in Falcon BMS User.cfg.</summary>
        CutLine,
        /// <summary>A commented-out "set" line, eg. "// set {name} {value} // note".</summary>
        CommentedSet,
    }

    /// <summary>
    /// One physical line of a cfg file.  The original text is always retained verbatim,
    /// so that unmodified lines round-trip byte-for-byte.
    /// </summary>
    public sealed class CfgLine
    {
        public string Text { get; internal set; }
        public CfgLineKind Kind { get; internal set; }

        // Kind == Set or CommentedSet
        public string Name { get; internal set; }
        public string RawValue { get; internal set; }
        public string Description { get; internal set; }
        internal int ValueStart { get; set; }
        internal int ValueLength { get; set; }

        // Kind == Category
        public string CategoryName { get; internal set; }

        public override string ToString() { return Text; }
    }

    /// <summary>
    /// A cfg file, as an ordered list of lines.  This is an *editor* model: comments, blank lines,
    /// unknown lines and ordering are all preserved; edits touch only the value token of a line,
    /// or insert/remove whole "set" lines.
    /// </summary>
    public sealed class CfgDocument
    {
        public const string CutLineMarker = "LAUNCHER OVERRIDES BEGIN HERE";

        // set {name} {value} [// description | # description]
        static readonly Regex s_setLine = new Regex(
            @"^\s*set\s+(?<name>[A-Za-z_][A-Za-z0-9_]*)\s+(?<value>""[^""]*""|[^\s""]+)\s*(?:(?://|#)\s?(?<desc>.*))?$",
            RegexOptions.CultureInvariant);

        // A commented-out set line: the comment marker, then a well-formed set line.
        static readonly Regex s_commentedSetLine = new Regex(
            @"^(?<indent>\s*)(?://|#)\s*(?<body>set\s.*)$",
            RegexOptions.CultureInvariant);

        readonly List<CfgLine> _lines = new List<CfgLine>();

        public string FilePath { get; private set; }
        public IReadOnlyList<CfgLine> Lines { get { return _lines; } }

        string _eol = "\r\n";
        bool _trailingNewline = true;

        public CfgDocument() { }

        public static CfgDocument Load(string path)
        {
            byte[] bytes = File.ReadAllBytes(path);
            CfgDocument doc = Parse(DecodeUtf8(bytes));
            doc.FilePath = path;
            return doc;
        }

        /// <summary>Loads the file if it exists, otherwise returns an empty document bound to that path.</summary>
        public static CfgDocument LoadOrCreate(string path)
        {
            if (File.Exists(path))
                return Load(path);

            return new CfgDocument { FilePath = path };
        }

        public static CfgDocument Parse(string text)
        {
            CfgDocument doc = new CfgDocument();
            if (string.IsNullOrEmpty(text))
                return doc;

            doc._eol = text.Contains("\r\n") ? "\r\n" : "\n";
            doc._trailingNewline = text.EndsWith("\n", StringComparison.Ordinal);

            string[] rawLines = text.Split('\n');
            int count = rawLines.Length;
            if (doc._trailingNewline)
                count--; // final empty element after the trailing newline

            for (int i = 0; i < count; i++)
                doc._lines.Add(ParseLine(rawLines[i].TrimEnd('\r')));

            return doc;
        }

        static string DecodeUtf8(byte[] bytes)
        {
            int offset = 0;
            if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
                offset = 3;

            return new UTF8Encoding(false, false).GetString(bytes, offset, bytes.Length - offset);
        }

        public static CfgLine ParseLine(string text)
        {
            CfgLine line = new CfgLine { Text = text, Kind = CfgLineKind.Other };
            string trimmed = text.Trim();

            if (trimmed.IndexOf(CutLineMarker, StringComparison.OrdinalIgnoreCase) >= 0
                && (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("#", StringComparison.Ordinal)))
            {
                line.Kind = CfgLineKind.CutLine;
                return line;
            }

            // Checked before categories: "// set g_bFoo 1 //" is a commented-out param, not a category header.
            Match commented = s_commentedSetLine.Match(text);
            if (commented.Success && TryParseSet(line, text, commented.Groups["body"].Index))
            {
                line.Kind = CfgLineKind.CommentedSet;
                return line;
            }

            if (trimmed.Length >= 4
                && trimmed.StartsWith("//", StringComparison.Ordinal)
                && trimmed.EndsWith("//", StringComparison.Ordinal))
            {
                // Strip the surrounding ascii-art slashes; an empty remainder (eg. "// //" or "/////") is just decoration.
                string inner = trimmed.Trim('/', ' ', '\t');
                if (inner.Length > 0)
                {
                    line.Kind = CfgLineKind.Category;
                    line.CategoryName = inner;
                }
                return line;
            }

            if (TryParseSet(line, text, 0))
                line.Kind = CfgLineKind.Set;

            return line;
        }

        /// <summary>
        /// Parses the set statement starting at text[start], filling in the line's Name, RawValue etc.
        /// (but not its Kind).  Returns false if it isn't a well-formed set statement.
        /// </summary>
        static bool TryParseSet(CfgLine line, string text, int start)
        {
            Match m = s_setLine.Match(text.Substring(start));
            if (!m.Success)
                return false;

            string name = m.Groups["name"].Value;
            string value = m.Groups["value"].Value;

            // Values that don't conform to the param's datatype are ignored (treated as comments).
            if (!ValueConverter.IsValidCfgToken(ParamTypes.FromName(name), value))
                return false;

            line.Name = name;
            line.RawValue = value;
            line.ValueStart = start + m.Groups["value"].Index;
            line.ValueLength = m.Groups["value"].Length;
            line.Description = m.Groups["desc"].Success ? m.Groups["desc"].Value.Trim() : null;
            return true;
        }

        /// <summary>True if the set line has a non-empty trailing comment, eg. a note the user wrote.</summary>
        static bool HasTrailingComment(CfgLine line)
        {
            return !string.IsNullOrEmpty(line.Description);
        }

        /// <summary>"set x 1 // note" -> "// set x 1 // note", keeping the indentation.</summary>
        static string CommentOut(string text)
        {
            int indent = text.Length - text.TrimStart().Length;
            return text.Substring(0, indent) + "// " + text.Substring(indent);
        }

        /// <summary>"// set x 1 // note" -> "set x 1 // note", keeping the indentation.</summary>
        static string Uncomment(string text)
        {
            Match m = s_commentedSetLine.Match(text);
            return m.Groups["indent"].Value + m.Groups["body"].Value;
        }

        /// <summary>Index of the Launcher cut line, or -1 if none.</summary>
        public int CutLineIndex
        {
            get
            {
                for (int i = 0; i < _lines.Count; i++)
                    if (_lines[i].Kind == CfgLineKind.CutLine)
                        return i;
                return -1;
            }
        }

        /// <summary>
        /// The effective (last-writer-wins) set line for the given param, or null.
        /// If aboveCutLine is true, lines at or below the Launcher cut line are not considered.
        /// </summary>
        public CfgLine FindLast(string name, bool aboveCutLine = false)
        {
            int index = FindLastIndex(name, aboveCutLine);
            return index >= 0 ? _lines[index] : null;
        }

        /// <summary>The last commented-out set line for the given param, or null.</summary>
        public CfgLine FindLastCommented(string name, bool aboveCutLine = false)
        {
            int index = FindLastIndex(name, aboveCutLine, CfgLineKind.CommentedSet);
            return index >= 0 ? _lines[index] : null;
        }

        int FindLastIndex(string name, bool aboveCutLine, CfgLineKind kind = CfgLineKind.Set)
        {
            int end = _lines.Count;
            if (aboveCutLine)
            {
                int cut = CutLineIndex;
                if (cut >= 0)
                    end = cut;
            }

            for (int i = end - 1; i >= 0; i--)
            {
                CfgLine line = _lines[i];
                if (line.Kind == kind && string.Equals(line.Name, name, StringComparison.OrdinalIgnoreCase))
                    return i;
            }
            return -1;
        }

        /// <summary>
        /// Sets a param's value.  If a set line already exists, only its value token is replaced (preserving
        /// whitespace and trailing comment).  Failing that, if a commented-out set line exists (eg. from
        /// RemoveValue), it is uncommented and its value replaced.  Otherwise a new line is inserted -- above
        /// the Launcher cut line if there is one, else at the end of the file.
        /// </summary>
        /// <param name="serializedValue">The value as it should appear in the file (eg. already quoted for strings).</param>
        /// <param name="respectCutLine">If true, never edit or insert at/below the Launcher cut line.</param>
        public void SetValue(string name, string serializedValue, bool respectCutLine)
        {
            int index = FindLastIndex(name, respectCutLine);
            if (index < 0)
            {
                index = FindLastIndex(name, respectCutLine, CfgLineKind.CommentedSet);
                if (index >= 0)
                    _lines[index] = ParseLine(Uncomment(_lines[index].Text));
            }

            if (index >= 0)
            {
                CfgLine old = _lines[index];
                string text = old.Text.Substring(0, old.ValueStart) + serializedValue + old.Text.Substring(old.ValueStart + old.ValueLength);
                _lines[index] = ParseLine(text);
                return;
            }

            CfgLine newLine = ParseLine("set " + name + " " + serializedValue);

            int cut = respectCutLine ? CutLineIndex : -1;
            int insertAt = (cut >= 0) ? cut : _lines.Count;

            // Keep any blank lines separating the block above, from the cut line (or end of file).
            while (insertAt > 0 && _lines[insertAt - 1].Text.Trim().Length == 0)
                insertAt--;

            _lines.Insert(insertAt, newLine);
        }

        /// <summary>
        /// Removes all set lines for the given param (only those above the cut line, if respectCutLine).
        /// Lines with a trailing comment are commented out rather than deleted, so the user's notes are kept.
        /// Returns the number of lines removed or commented out.
        /// </summary>
        public int RemoveValue(string name, bool respectCutLine)
        {
            int removed = 0;
            int index;
            while ((index = FindLastIndex(name, respectCutLine)) >= 0)
            {
                if (HasTrailingComment(_lines[index]))
                    _lines[index] = ParseLine(CommentOut(_lines[index].Text));
                else
                    _lines.RemoveAt(index);
                removed++;
            }
            return removed;
        }

        public string ToText()
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < _lines.Count; i++)
            {
                sb.Append(_lines[i].Text);
                if (i < _lines.Count - 1 || _trailingNewline)
                    sb.Append(_eol);
            }
            return sb.ToString();
        }

        /// <summary>Saves as UTF-8, without BOM.</summary>
        public void Save(string path = null)
        {
            path = path ?? FilePath;
            if (path == null)
                throw new InvalidOperationException("No file path specified.");

            File.WriteAllText(path, ToText(), new UTF8Encoding(false));
            FilePath = path;
        }
    }
}
