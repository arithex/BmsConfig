using System.IO;
using System.Linq;
using System.Text;
using BmsConfig.Core;
using Xunit;

namespace BmsConfig.Tests
{
    public class CfgDocumentTests
    {
        const string Sample =
            "///////////////////////////////\r\n" +
            "// This is a category header //\r\n" +
            "///////////////////////////////\r\n" +
            "\r\n" +
            "set g_nFooNumber 42 // This is the Foo value.  Range: [0-42]\r\n" +
            "set g_fBarNumber   42.42   // This is the Bar value.  Range: [0.0-100.0]\r\n" +
            "\r\n" +
            "// This is also a category header //\r\n" +
            "set g_bSomethingEnabled 1\r\n" +
            "set g_bSomethingDisabled 0\r\n" +
            "# a hash comment\r\n" +
            "set g_sSomeTextParam \"this is a string value\" // A string value\r\n" +
            "set g_nBroken abc // not an int: ignored\r\n" +
            "this line is garbage\r\n" +
            "set g_nFooNumber 7 // dup: last writer wins\r\n";

        [Fact]
        public void RoundTripsUnmodified()
        {
            Assert.Equal(Sample, CfgDocument.Parse(Sample).ToText());

            string lf = Sample.Replace("\r\n", "\n").TrimEnd('\n');
            Assert.Equal(lf, CfgDocument.Parse(lf).ToText());
        }

        [Fact]
        public void ParsesLineKinds()
        {
            CfgDocument doc = CfgDocument.Parse(Sample);
            Assert.Equal(new[] { "This is a category header", "This is also a category header" },
                doc.Lines.Where(l => l.Kind == CfgLineKind.Category).Select(l => l.CategoryName));

            Assert.Equal(new[] { "g_nFooNumber", "g_fBarNumber", "g_bSomethingEnabled", "g_bSomethingDisabled", "g_sSomeTextParam", "g_nFooNumber" },
                doc.Lines.Where(l => l.Kind == CfgLineKind.Set).Select(l => l.Name));

            CfgLine bar = doc.FindLast("g_fBarNumber");
            Assert.Equal("42.42", bar.RawValue);
            Assert.Equal("This is the Bar value.  Range: [0.0-100.0]", bar.Description);
            Assert.Equal("\"this is a string value\"", doc.FindLast("g_sSomeTextParam").RawValue);
            Assert.Equal("7", doc.FindLast("g_nFooNumber").RawValue);
            Assert.Null(doc.FindLast("g_nBroken"));
        }

        [Fact]
        public void SetValueEditsOnlyTheValueToken()
        {
            CfgDocument doc = CfgDocument.Parse(Sample);
            doc.SetValue("g_fBarNumber", "1.5", respectCutLine: false);
            Assert.Equal(Sample.Replace("set g_fBarNumber   42.42   //", "set g_fBarNumber   1.5   //"), doc.ToText());
        }

        [Fact]
        public void SetValueEditsLastDuplicate()
        {
            CfgDocument doc = CfgDocument.Parse(Sample);
            doc.SetValue("g_nFooNumber", "3", respectCutLine: false);
            Assert.Contains("set g_nFooNumber 42 //", doc.ToText());
            Assert.Contains("set g_nFooNumber 3 // dup", doc.ToText());
        }

        [Fact]
        public void InsertsAboveCutLine()
        {
            string user =
                "set g_bFoo 1\n" +
                "\n" +
                "// LAUNCHER OVERRIDES BEGIN HERE - DO NOT EDIT OR ADD BELOW THIS LINE\n" +
                "set g_nBar 5\n";

            CfgDocument doc = CfgDocument.Parse(user);
            doc.SetValue("g_nNew", "1", respectCutLine: true);
            doc.SetValue("g_nBar", "6", respectCutLine: true);

            Assert.Equal(
                "set g_bFoo 1\n" +
                "set g_nNew 1\n" +
                "set g_nBar 6\n" +
                "\n" +
                "// LAUNCHER OVERRIDES BEGIN HERE - DO NOT EDIT OR ADD BELOW THIS LINE\n" +
                "set g_nBar 5\n",
                doc.ToText());
        }

        [Fact]
        public void RemoveValueRespectsCutLine()
        {
            string user =
                "set g_nBar 1\n" +
                "// LAUNCHER OVERRIDES BEGIN HERE - DO NOT EDIT OR ADD BELOW THIS LINE\n" +
                "set g_nBar 5\n";

            CfgDocument doc = CfgDocument.Parse(user);
            Assert.Equal(1, doc.RemoveValue("g_nBar", respectCutLine: true));
            Assert.Equal(
                "// LAUNCHER OVERRIDES BEGIN HERE - DO NOT EDIT OR ADD BELOW THIS LINE\n" +
                "set g_nBar 5\n",
                doc.ToText());
        }

        [Fact]
        public void RemoveValueCommentsOutLinesWithNotes()
        {
            string user =
                "set g_bPlain 1\n" +
                "  set g_bNoted 1 // NOTE: turn this off before multiplayer\n" +
                "set g_bEmptyComment 1 //\n";

            CfgDocument doc = CfgDocument.Parse(user);
            Assert.Equal(1, doc.RemoveValue("g_bPlain", respectCutLine: true));
            Assert.Equal(1, doc.RemoveValue("g_bNoted", respectCutLine: true));
            Assert.Equal(1, doc.RemoveValue("g_bEmptyComment", respectCutLine: true));

            Assert.Equal("  // set g_bNoted 1 // NOTE: turn this off before multiplayer\n", doc.ToText());
            Assert.Null(doc.FindLast("g_bNoted"));
            Assert.NotNull(doc.FindLastCommented("g_bNoted"));
            Assert.Equal(0, doc.RemoveValue("g_bNoted", respectCutLine: true));
        }

        [Fact]
        public void SetValueUncommentsCommentedOutLine()
        {
            string user =
                "  // set g_bNoted 1 // NOTE: turn this off before multiplayer\n" +
                "#set g_nHash 5\n" +
                "// LAUNCHER OVERRIDES BEGIN HERE - DO NOT EDIT OR ADD BELOW THIS LINE\n" +
                "// set g_nBelow 1 // launcher\n";

            CfgDocument doc = CfgDocument.Parse(user);
            doc.SetValue("g_bNoted", "0", respectCutLine: true);
            doc.SetValue("g_nHash", "7", respectCutLine: true);
            doc.SetValue("g_nBelow", "2", respectCutLine: true); // the commented line below the cut is left alone

            Assert.Equal(
                "  set g_bNoted 0 // NOTE: turn this off before multiplayer\n" +
                "set g_nHash 7\n" +
                "set g_nBelow 2\n" +
                "// LAUNCHER OVERRIDES BEGIN HERE - DO NOT EDIT OR ADD BELOW THIS LINE\n" +
                "// set g_nBelow 1 // launcher\n",
                doc.ToText());
        }

        [Fact]
        public void RemoveThenSetRoundTrips()
        {
            const string line = "set g_bNoted 1 // NOTE: turn this off before multiplayer\r\n";
            CfgDocument doc = CfgDocument.Parse(line);
            doc.RemoveValue("g_bNoted", respectCutLine: true);
            doc.SetValue("g_bNoted", "1", respectCutLine: true);
            Assert.Equal(line, doc.ToText());
        }

        [Fact]
        public void CommentedSetIsNotACategory()
        {
            CfgDocument doc = CfgDocument.Parse("// set g_bFoo 1 //\n// Real Category //\n// set g_nBad abc // not an int\n");
            Assert.Equal(CfgLineKind.CommentedSet, doc.Lines[0].Kind);
            Assert.Equal(CfgLineKind.Category, doc.Lines[1].Kind);
            Assert.Equal(CfgLineKind.Other, doc.Lines[2].Kind);
        }

        [Fact]
        public void SavesWithoutBom()
        {
            string dir = Directory.CreateTempSubdirectory().FullName;
            string path = Path.Combine(dir, "test.cfg");
            File.WriteAllBytes(path, Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes("set g_sX \"é\"\r\n")).ToArray());

            CfgDocument doc = CfgDocument.Load(path);
            Assert.Equal("\"é\"", doc.FindLast("g_sX").RawValue);
            doc.Save();

            byte[] bytes = File.ReadAllBytes(path);
            Assert.NotEqual(0xEF, bytes[0]);
            Assert.Equal("set g_sX \"é\"\r\n", Encoding.UTF8.GetString(bytes));
            Directory.Delete(dir, true);
        }
    }

    public class ConfigSetTests
    {
        [Fact]
        public void ListsCommentedOutParams()
        {
            CfgDocument bms = CfgDocument.Parse("// GENERAL //\nset g_bKnown 1 // Known\n// set g_bBmsCommented 1 // ignored in Falcon BMS.cfg\n");
            CfgDocument user = CfgDocument.Parse(
                "// set g_bKnown 0 // my note\n" +
                "// set g_bExperimental 1 // NOTE: off before MP\n" +
                "// LAUNCHER OVERRIDES BEGIN HERE - DO NOT EDIT OR ADD BELOW THIS LINE\n" +
                "// set g_bBelowCut 1 // x\n");
            CfgDocument vr = CfgDocument.Parse("// set g_nVrOnly 2 // vr note\n");

            ConfigSet set = ConfigSet.FromDocuments("x", bms, user, vr);

            ParamEntry known = set.Find("g_bKnown");
            Assert.Equal("GENERAL", known.Category);
            Assert.True(known.UserCommentedOut);
            Assert.Null(known.UserValue);
            Assert.Equal("1", known.GetEffectiveValue(true));

            ParamEntry experimental = set.Find("g_bExperimental");
            Assert.Equal("Other", experimental.Category);
            Assert.Equal("NOTE: off before MP", experimental.Description);
            Assert.True(experimental.UserCommentedOut);
            Assert.Null(experimental.BmsValue);
            Assert.Null(experimental.GetEffectiveValue(true));

            Assert.True(set.Find("g_nVrOnly").VrCommentedOut);
            Assert.Null(set.Find("g_bBmsCommented"));
            Assert.Null(set.Find("g_bBelowCut"));
            Assert.Contains("Other", set.Categories);
        }

        [Fact]
        public void ParsesRealWorldHeader()
        {
            // From the top of a real Falcon BMS.cfg: the banner "categories" have no params, so they don't appear.
            CfgDocument bms = CfgDocument.Parse(
                "///////////////////////////////////////////////////////////////////////\r\n" +
                "// Settings for Falcon 4.38.2                                        //\r\n" +
                "//                                                                   //\r\n" +
                "// WARNING: this file will be overwritten by future updates!         //\r\n" +
                "///////////////////////////////////////////////////////////////////////\r\n" +
                "\r\n" +
                "///////////////////////////////////////////////////\r\n" +
                "// GENERAL                                       //\r\n" +
                "///////////////////////////////////////////////////\r\n" +
                "set g_fCursorSpeed 1.0                // Customizes the cursor control speed.\r\n" +
                "set g_nPrintToFile 1 // Enables output of detailed briefing data to \"briefing.txt\" file.\r\n" +
                "///////////////////////////////////////////////////\r\n" +
                "// MULTIPLAYER                                   //\r\n" +
                "///////////////////////////////////////////////////\r\n" +
                "set g_sTacviewPassword \"\" // Tacview server password (character string)\r\n");

            ConfigSet set = ConfigSet.FromDocuments("x", bms, new CfgDocument(), new CfgDocument());
            Assert.Equal(new[] { "GENERAL", "MULTIPLAYER" }, set.Categories);
            Assert.Equal("Enables output of detailed briefing data to \"briefing.txt\" file.", set.Find("g_nPrintToFile").Description);
            Assert.Equal("\"\"", set.Find("g_sTacviewPassword").BmsValue);
            Assert.Equal("MULTIPLAYER", set.Find("g_sTacviewPassword").Category);
        }

        [Fact]
        public void MergesLastWriterWins()
        {
            CfgDocument bms = CfgDocument.Parse(
                "// GENERAL //\n" +
                "set g_bA 1 // A\n" +
                "set g_nB 2 // B Enum:[Low,Medium,High]\n" +
                "// GRAPHICS //\n" +
                "set g_fC 1.0 // C Range:[0-]\n");
            CfgDocument user = CfgDocument.Parse(
                "set g_nB 1\n" +
                "set g_nUnknown 9 // something extra\n" +
                "// LAUNCHER OVERRIDES BEGIN HERE - DO NOT EDIT OR ADD BELOW THIS LINE\n" +
                "set g_fC 2.0\n");
            CfgDocument vr = CfgDocument.Parse("set g_bA 0\n");

            ConfigSet set = ConfigSet.FromDocuments("x", bms, user, vr);
            Assert.Equal(new[] { "GENERAL", "GRAPHICS", "Other" }, set.Categories);

            ValueSource src;
            Assert.Equal("1", set.Find("g_bA").GetEffectiveValue(false, out src));
            Assert.Equal(ValueSource.Bms, src);
            Assert.Equal("0", set.Find("g_bA").GetEffectiveValue(true, out src));
            Assert.Equal(ValueSource.Vr, src);
            Assert.Equal("1", set.Find("g_nB").GetEffectiveValue(true, out src));
            Assert.Equal(ValueSource.User, src);
            Assert.Equal("2.0", set.Find("g_fC").GetEffectiveValue(true, out src));
            Assert.Equal(ValueSource.Launcher, src);

            ParamEntry unknown = set.Find("g_nUnknown");
            Assert.Equal("Other", unknown.Category);
            Assert.Equal("something extra", unknown.Description);

            Assert.Equal(new[] { "Low", "Medium", "High" }, set.Find("g_nB").Constraints.EnumLabels);
            Assert.Equal(0, set.Find("g_fC").Constraints.Min);
            Assert.Null(set.Find("g_fC").Constraints.Max);
        }
    }
}
