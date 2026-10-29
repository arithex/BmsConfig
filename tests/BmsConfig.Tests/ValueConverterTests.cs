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
            const string desc = "Quality. Enum:[Low, Medium, High]";
            Assert.Equal("2", Convert("g_nX", "high", desc));
            Assert.Equal("1", Convert("g_nX", "1", desc));
            Assert.Null(Convert("g_nX", "3", desc));
            Assert.Equal("Medium", ValueConverter.ToDisplay(ParamType.Int, "1", ParamConstraints.Parse(desc)));
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
