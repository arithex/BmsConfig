using BmsConfig.Core;
using Xunit;

namespace BmsConfig.Tests
{
    public class ValueConverterTests
    {
        static string Convert(string name, string input, string description = null)
        {
            string token, error;
            ParamConstraints c = ParamConstraints.Parse(description);
            return ValueConverter.TryConvertInput(name, ParamTypes.FromName(name), c, input, out token, out error) ? token : null;
        }

        [Theory]
        [InlineData("g_bX", "ON", "1")]
        [InlineData("g_bX", "off", "0")]
        [InlineData("g_bX", "1", "1")]
        [InlineData("g_bX", "maybe", null)]
        [InlineData("g_nX", "42", "42")]
        [InlineData("g_nX", "-42", "-42")]
        [InlineData("g_nX", "0x10000001", "268435457")]
        [InlineData("g_nX", "0xFFFFFFFF", "-1")]
        [InlineData("g_nX", "4.2", null)]
        [InlineData("g_nX", "1e3", null)]
        [InlineData("g_fX", "5.0", "5.0")]
        [InlineData("g_fX", ".5", "0.5")]
        [InlineData("g_fX", "-1.", "-1.0")]
        [InlineData("g_fX", "+007.25", "7.25")]
        [InlineData("g_fX", "1e5", null)]
        [InlineData("g_fX", "NaN", null)]
        [InlineData("g_fX", "Infinity", null)]
        [InlineData("g_sX", "hello world", "\"hello world\"")]
        [InlineData("g_sX", "“fancy”", "\"fancy\"")]
        [InlineData("g_sX", "'single'", "\"single\"")]
        [InlineData("g_sX", "\"straight\"", "\"straight\"")]
        [InlineData("g_sX", "", "\"\"")]
        [InlineData("g_sX", "a\"b", null)]
        public void ConvertsInput(string name, string input, string expected)
        {
            Assert.Equal(expected, Convert(name, input));
        }

        [Theory]
        [InlineData("50", "50")]
        [InlineData("101", null)]
        [InlineData("-1", null)]
        [InlineData("0x64", "100")]
        public void EnforcesRange(string input, string expected)
        {
            Assert.Equal(expected, Convert("g_nX", input, "Something.  Range:[0-100]"));
        }

        [Fact]
        public void UnsignedRange()
        {
            Assert.Null(Convert("g_nX", "-1", "Range:[0-]"));
            Assert.Equal("4294967295", Convert("g_nX", "0xFFFFFFFF", "Range:[0-]"));
            Assert.Equal("-5", Convert("g_nX", "-5", "Range:[-6-6]"));
            Assert.Null(Convert("g_fX", "100.5", "Range: [0.0-100.0]"));
        }

        [Fact]
        public void EnumLabels()
        {
            const string desc = "Quality. Values: [Low, Medium, High]";
            Assert.Equal("2", Convert("g_nX", "high", desc));
            Assert.Equal("1", Convert("g_nX", "1", desc));
            Assert.Null(Convert("g_nX", "3", desc));
            Assert.Equal("Medium", ValueConverter.ToDisplay(ParamType.Int, "1", ParamConstraints.Parse(desc)));
        }

        [Fact]
        public void RangeWithStep()
        {
            const string desc = "Something.  Range: [20-120, by 20]";
            ParamConstraints c = ParamConstraints.Parse(desc);
            Assert.Equal(20, c.Step);
            Assert.Equal(new[] { "20", "40", "60", "80", "100", "120" }, c.GetChoices(ParamType.Int));
            Assert.Equal("Range: [20 to 120, by 20]", c.Describe());

            Assert.Equal("60", Convert("g_nX", "60", desc));
            Assert.Equal("40", Convert("g_nX", "0x28", desc));
            Assert.Null(Convert("g_nX", "50", desc));
            Assert.Null(Convert("g_nX", "140", desc));
        }

        [Fact]
        public void FloatRangeWithStep()
        {
            const string desc = "Range: [0.5-2.0, by 0.25]";
            Assert.Equal(new[] { "0.5", "0.75", "1.0", "1.25", "1.5", "1.75", "2.0" },
                ParamConstraints.Parse(desc).GetChoices(ParamType.Float));
            Assert.Equal("1.25", Convert("g_fX", "1.25", desc));
            Assert.Equal("1.0", Convert("g_fX", "1.0", desc));
            Assert.Null(Convert("g_fX", "1.1", desc));

            // Rounding error must not break the grid: 0.1 steps.
            Assert.Equal("0.3", Convert("g_fX", "0.3", "Range: [0.0-1.0, by 0.1]"));
            Assert.Equal(11, ParamConstraints.Parse("Range: [0.0-1.0, by 0.1]").GetChoices(ParamType.Float).Count);
        }

        [Fact]
        public void TooManyStepsMeansNoButtons()
        {
            ParamConstraints c = ParamConstraints.Parse("Range: [0-100, by 1]");
            Assert.Null(c.GetChoices(ParamType.Int));
            Assert.NotNull(ParamConstraints.Parse("Range: [0-20, by 1]").GetChoices(ParamType.Int)); // exactly 21
            Assert.Equal("37", Convert("g_nX", "37", "Range: [0-100, by 1]"));
            Assert.Null(ParamConstraints.Parse("Range: [0-, by 5]").GetChoices(ParamType.Int)); // unbounded
            Assert.Null(Convert("g_nX", "7", "Range: [0-, by 5]"));
        }

        [Fact]
        public void NumericValues()
        {
            const string desc = "The font size to use. Values: [8,9,10,12,16]";
            ParamConstraints c = ParamConstraints.Parse(desc);
            Assert.False(c.IsEnum);
            Assert.Equal(new[] { "8", "9", "10", "12", "16" }, c.GetChoices(ParamType.Int));
            Assert.Equal("12", Convert("g_nFontSize", "12", desc));
            Assert.Null(Convert("g_nFontSize", "11", desc));
            Assert.Equal("12", ValueConverter.ToDisplay(ParamType.Int, "12", c)); // a value, not an index

            const string floats = "Values: [0.5, 0.75, 1.0, 1.5, 2.0]";
            Assert.Equal(new[] { "0.5", "0.75", "1.0", "1.5", "2.0" }, ParamConstraints.Parse(floats).GetChoices(ParamType.Float));
            Assert.Equal("0.75", Convert("g_fCursorSpeed", "0.75", floats));
            Assert.Equal("1", Convert("g_fCursorSpeed", "1", floats));
            Assert.Null(Convert("g_fCursorSpeed", "1.25", floats));
        }

        [Fact]
        public void FlagsExistingValuesThatBreakConstraints()
        {
            ParamConstraints step = ParamConstraints.Parse("Range: [20-120, by 20]");
            Assert.Null(ValueConverter.CheckCfgValue("g_nX", ParamType.Int, step, "40"));
            Assert.NotNull(ValueConverter.CheckCfgValue("g_nX", ParamType.Int, step, "50"));
            Assert.NotNull(ValueConverter.CheckCfgValue("g_nX", ParamType.Int, step, "140"));

            ParamConstraints floats = ParamConstraints.Parse("Values: [0.5, 0.75, 1.0]");
            Assert.Null(ValueConverter.CheckCfgValue("g_fX", ParamType.Float, floats, "1"));
            Assert.NotNull(ValueConverter.CheckCfgValue("g_fX", ParamType.Float, floats, "0.6"));

            ParamConstraints labels = ParamConstraints.Parse("Values: [Low, High]");
            Assert.NotNull(ValueConverter.CheckCfgValue("g_nX", ParamType.Int, labels, "2"));

            Assert.NotNull(ValueConverter.CheckCfgValue("g_sPriorityFixes", ParamType.String, ParamConstraints.None, "\"all-foo\""));
            Assert.Null(ValueConverter.CheckCfgValue("g_sOther", ParamType.String, ParamConstraints.None, "\"anything\""));
            Assert.Null(ValueConverter.CheckCfgValue("g_bX", ParamType.Bool, ParamConstraints.None, "1"));
            Assert.Null(ValueConverter.CheckCfgValue("g_nX", ParamType.Int, step, null));
        }

        [Theory]
        [InlineData("g_sCpuPerfOptimizations", "all", true)]
        [InlineData("g_sPriorityFixes", "all-FOO-BAR_2", true)]
        [InlineData("g_sCompatibleChanges", "all-foo", false)]
        [InlineData("g_sCompatibleChanges", "none", false)]
        [InlineData("g_sNonCompatibleChanges", "none+FOO+BAR", true)]
        [InlineData("g_sNonCompatibleChanges", "none-FOO", false)]
        [InlineData("g_sNonCompatibleChanges", "“none+X”", true)]
        public void FeatureFlags(string name, string input, bool valid)
        {
            Assert.Equal(valid, Convert(name, input) != null);
        }

        [Fact]
        public void Display()
        {
            Assert.Equal("ON", ValueConverter.ToDisplay(ParamType.Bool, "1"));
            Assert.Equal("Off", ValueConverter.ToDisplay(ParamType.Bool, "0"));
            Assert.Equal("\"x\"", ValueConverter.ToDisplay(ParamType.String, "\"x\""));
            Assert.True(ValueConverter.AreEquivalent(ParamType.Float, "5", "5.0"));
            Assert.True(ValueConverter.AreEquivalent(ParamType.Int, "0x10", "16"));
        }
    }
}
