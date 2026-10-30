using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace BmsConfig.Core
{
    public enum ParamType
    {
        Unknown,
        Bool,
        Int,
        Float,
        String,
    }

    public static class ParamTypes
    {
        /// <summary>The datatype is implied by the param name: g_bXxx, g_nXxx, g_fXxx, g_sXxx.</summary>
        public static ParamType FromName(string name)
        {
            if (name == null || name.Length < 3 || !name.StartsWith("g_", StringComparison.OrdinalIgnoreCase))
                return ParamType.Unknown;

            switch (name[2])
            {
                case 'b': return ParamType.Bool;
                case 'n': return ParamType.Int;
                case 'f': return ParamType.Float;
                case 's': return ParamType.String;
                default: return ParamType.Unknown;
            }
        }

        public static string DisplayName(ParamType type)
        {
            switch (type)
            {
                case ParamType.Bool: return "Boolean";
                case ParamType.Int: return "Integer";
                case ParamType.Float: return "Float";
                case ParamType.String: return "String";
                default: return "Unknown";
            }
        }
    }

    /// <summary>
    /// Constraints parsed from a param's description text:
    ///   "Range: [0-100]", "Range: [0-]", "Range: [20-120, by 20]",
    ///   "Values: [Low,Medium,High]" (labels for 0,1,2...), "Values: [8,9,10,12,16]" (the allowed numbers).
    /// </summary>
    public sealed class ParamConstraints
    {
        /// <summary>Discrete values are offered as buttons, when there are at most this many.</summary>
        public const int MaxChoices = 21;

        static readonly Regex s_range = new Regex(
            @"Range:\s*\[\s*(?<min>[+-]?\d+(?:\.\d+)?)?\s*(?:-|\.\.|,)\s*(?<max>[+-]?\d+(?:\.\d+)?)?\s*(?:,\s*by\s+(?<step>\+?\d+(?:\.\d+)?)\s*)?\]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        static readonly Regex s_number = new Regex(@"^[+-]?(\d+\.?\d*|\.\d+)$", RegexOptions.CultureInvariant);

        static readonly Regex s_enum = new Regex(
            @"Values:\s*\[(?<labels>[^\]]*)\]",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        public static readonly ParamConstraints None = new ParamConstraints();

        public double? Min { get; private set; }
        public double? Max { get; private set; }
        /// <summary>From "Range: [min-max, by step]": allowed values are min, min+step, min+2*step... (up to max).</summary>
        public double? Step { get; private set; }
        /// <summary>From "Values: [Low,Medium,High]": labels for the integer values 0,1,2...</summary>
        public IReadOnlyList<string> EnumLabels { get; private set; }
        /// <summary>From an all-numeric "Values: [8,9,10,12,16]": the only allowed values.</summary>
        public IReadOnlyList<double> NumericValues { get; private set; }

        public bool HasRange { get { return Min.HasValue || Max.HasValue; } }
        public bool IsEnum { get { return EnumLabels != null && EnumLabels.Count > 0; } }

        public static ParamConstraints Parse(string description)
        {
            if (string.IsNullOrEmpty(description))
                return None;

            ParamConstraints c = new ParamConstraints();

            Match m = s_range.Match(description);
            if (m.Success)
            {
                if (m.Groups["min"].Success)
                    c.Min = double.Parse(m.Groups["min"].Value, CultureInfo.InvariantCulture);
                if (m.Groups["max"].Success)
                    c.Max = double.Parse(m.Groups["max"].Value, CultureInfo.InvariantCulture);

                // A step needs a starting point; a zero step is meaningless.
                double step;
                if (m.Groups["step"].Success && c.Min.HasValue
                    && double.TryParse(m.Groups["step"].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out step) && step > 0)
                    c.Step = step;
            }

            m = s_enum.Match(description);
            if (m.Success)
            {
                List<string> labels = new List<string>();
                foreach (string s in m.Groups["labels"].Value.Split(','))
                    labels.Add(s.Trim());
                if (labels.Count > 0 && !(labels.Count == 1 && labels[0].Length == 0))
                {
                    // All numbers -> the allowed values themselves; otherwise labels for 0,1,2...
                    if (labels.TrueForAll(l => s_number.IsMatch(l)))
                        c.NumericValues = labels.ConvertAll(l => double.Parse(l, NumberStyles.Float, CultureInfo.InvariantCulture));
                    else
                        c.EnumLabels = labels;
                }
            }

            return c;
        }

        /// <summary>
        /// The discrete values allowed for an int or float param (from "Values: [numbers]", or a Range with a
        /// step), as cfg tokens -- or null if there are none, or more than MaxChoices.
        /// </summary>
        public IReadOnlyList<string> GetChoices(ParamType type)
        {
            if (type != ParamType.Int && type != ParamType.Float)
                return null;

            List<double> values = new List<double>();
            if (NumericValues != null)
            {
                values.AddRange(NumericValues);
            }
            else if (Step.HasValue && Max.HasValue)
            {
                for (int i = 0; i <= MaxChoices; i++)
                {
                    double v = Math.Round(Min.Value + i * Step.Value, 9);
                    if (v > Max.Value + 1e-9)
                        break;
                    values.Add(v);
                }
            }

            if (values.Count == 0 || values.Count > MaxChoices)
                return null;

            List<string> tokens = new List<string>();
            foreach (double v in values)
            {
                if (type == ParamType.Int && v != Math.Floor(v))
                    continue; // eg. "Values: [1.5]" on an int param
                string token = ValueConverter.FormatNumber(type, v);
                if (!tokens.Contains(token))
                    tokens.Add(token);
            }
            return tokens.Count > 0 ? tokens : null;
        }

        /// <summary>Is the value one of the NumericValues, and on the Step grid (if those apply)?</summary>
        public bool IsAllowedValue(double value, out string error)
        {
            error = null;
            const double tolerance = 1e-9;

            if (NumericValues != null && !NumericValues.Any(v => Math.Abs(v - value) <= tolerance * Math.Max(1, Math.Abs(v))))
            {
                error = "Value must be one of: " + string.Join(", ", NumericValues.Select(v => v.ToString(CultureInfo.InvariantCulture))) + ".";
                return false;
            }

            if (Step.HasValue)
            {
                double steps = (value - Min.Value) / Step.Value;
                if (Math.Abs(steps - Math.Round(steps)) > 1e-6)
                {
                    error = "Value must be a multiple of " + Step.Value.ToString(CultureInfo.InvariantCulture)
                        + " from " + Min.Value.ToString(CultureInfo.InvariantCulture) + ".  " + Describe();
                    return false;
                }
            }
            return true;
        }

        public string Describe()
        {
            if (!HasRange)
                return null;

            string min = Min.HasValue ? Min.Value.ToString(CultureInfo.InvariantCulture) : "";
            string max = Max.HasValue ? Max.Value.ToString(CultureInfo.InvariantCulture) : "";
            string step = Step.HasValue ? ", by " + Step.Value.ToString(CultureInfo.InvariantCulture) : "";
            if (Max.HasValue)
                return "Range: [" + min + " to " + max + step + "]";
            return Min == 0 ? "Range: [0 or greater" + step + "]" : "Range: [" + min + " or greater" + step + "]";
        }
    }

    /// <summary>
    /// Conversions between cfg-file tokens, user input, and display text.
    /// </summary>
    public static class ValueConverter
    {
        static readonly Regex s_decimalInt = new Regex(@"^[+-]?\d+$", RegexOptions.CultureInvariant);
        static readonly Regex s_hexInt = new Regex(@"^0[xX](?<hex>[0-9A-Fa-f]{1,16})$", RegexOptions.CultureInvariant);
        static readonly Regex s_float = new Regex(@"^[+-]?(\d+\.?\d*|\.\d+)$", RegexOptions.CultureInvariant);

        // Quote characters users commonly paste in, by mistake: straight, curly ("smart") and low-9 quotes, primes, backticks.
        const string OpenQuotes = "\"“”„‟″«»'‘’‚‛′`´";

        // ---- reading cfg files --------------------------------------------------------------

        /// <summary>Does the token from a cfg file conform to the datatype?  Non-conforming lines are ignored.</summary>
        public static bool IsValidCfgToken(ParamType type, string token)
        {
            switch (type)
            {
                case ParamType.Bool:
                    return ParseCfgBool(token).HasValue;
                case ParamType.Int:
                    return ParseCfgInt(token).HasValue;
                case ParamType.Float:
                    return ParseCfgFloat(token).HasValue;
                default:
                    return token.Length > 0;
            }
        }

        public static bool? ParseCfgBool(string token)
        {
            token = token.Trim();
            if (token == "1" || token.Equals("true", StringComparison.OrdinalIgnoreCase))
                return true;
            if (token == "0" || token.Equals("false", StringComparison.OrdinalIgnoreCase))
                return false;
            return null;
        }

        public static long? ParseCfgInt(string token)
        {
            long value;
            token = token.Trim();
            if (s_decimalInt.IsMatch(token) && long.TryParse(token, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
                return value;

            Match m = s_hexInt.Match(token);
            ulong hex;
            if (m.Success && ulong.TryParse(m.Groups["hex"].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out hex) && hex <= long.MaxValue)
                return (long)hex;

            return null;
        }

        public static double? ParseCfgFloat(string token)
        {
            double value;
            token = token.Trim();
            if (s_float.IsMatch(token) && double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && !double.IsInfinity(value))
                return value;
            return null;
        }

        /// <summary>Removes the surrounding doublequotes from a cfg string token.</summary>
        public static string UnquoteCfgString(string token)
        {
            if (token != null && token.Length >= 2 && token[0] == '"' && token[token.Length - 1] == '"')
                return token.Substring(1, token.Length - 2);
            return token;
        }

        // ---- display ---------------------------------------------------------------------------

        public static string ToDisplay(ParamType type, string rawValue, ParamConstraints constraints = null)
        {
            if (rawValue == null)
                return "";

            switch (type)
            {
                case ParamType.Bool:
                    bool? b = ParseCfgBool(rawValue);
                    return b.HasValue ? (b.Value ? "ON" : "Off") : rawValue;

                case ParamType.Int:
                    long? n = ParseCfgInt(rawValue);
                    if (n.HasValue && constraints != null && constraints.IsEnum && n.Value >= 0 && n.Value < constraints.EnumLabels.Count)
                        return constraints.EnumLabels[(int)n.Value];
                    return n.HasValue ? n.Value.ToString(CultureInfo.InvariantCulture) : rawValue;

                case ParamType.String:
                    return "\"" + UnquoteCfgString(rawValue) + "\"";

                default:
                    return rawValue;
            }
        }

        /// <summary>The text to pre-fill into an editor textbox, for a cfg value.</summary>
        public static string ToEditText(ParamType type, string rawValue)
        {
            if (rawValue == null)
                return "";

            switch (type)
            {
                case ParamType.String:
                    return UnquoteCfgString(rawValue);
                case ParamType.Int:
                    long? n = ParseCfgInt(rawValue);
                    return n.HasValue ? n.Value.ToString(CultureInfo.InvariantCulture) : rawValue;
                default:
                    return rawValue;
            }
        }

        /// <summary>Are two cfg tokens semantically equal (eg. "5" vs "5.0" for a float; "0x10" vs "16" for an int)?</summary>
        public static bool AreEquivalent(ParamType type, string a, string b)
        {
            if (a == null || b == null)
                return a == b;

            switch (type)
            {
                case ParamType.Bool: return ParseCfgBool(a) == ParseCfgBool(b);
                case ParamType.Int: return ParseCfgInt(a) == ParseCfgInt(b);
                case ParamType.Float: return ParseCfgFloat(a) == ParseCfgFloat(b);
                case ParamType.String: return UnquoteCfgString(a) == UnquoteCfgString(b);
                default: return a == b;
            }
        }

        // ---- user input -> cfg token -------------------------------------------------------------

        /// <summary>
        /// Validates user input and converts it to the token to be written into the cfg file.
        /// Returns false (with an error message) if the input is invalid.
        /// </summary>
        public static bool TryConvertInput(string name, ParamType type, ParamConstraints constraints, string input, out string cfgToken, out string error)
        {
            cfgToken = null;
            error = null;
            constraints = constraints ?? ParamConstraints.None;
            input = (input ?? "").Trim();

            switch (type)
            {
                case ParamType.Bool:
                    return TryConvertBool(input, out cfgToken, out error);
                case ParamType.Int:
                    return TryConvertInt(input, constraints, out cfgToken, out error);
                case ParamType.Float:
                    return TryConvertFloat(input, constraints, out cfgToken, out error);
                case ParamType.String:
                    return TryConvertString(name, input, out cfgToken, out error);
                default:
                    if (input.Length == 0 || HasWhitespace(input) || input.IndexOf('"') >= 0)
                    {
                        error = "Value must be a single word, without spaces or quotes.";
                        return false;
                    }
                    cfgToken = input;
                    return true;
            }
        }

        /// <summary>
        /// Checks a value already in a cfg file against the param's constraints (range, step, allowed values,
        /// feature-flag syntax).  Such values are kept as-is, but flagged in the UI.  Returns the problem, or null.
        /// </summary>
        public static string CheckCfgValue(string name, ParamType type, ParamConstraints constraints, string rawValue)
        {
            if (rawValue == null || type == ParamType.Bool || type == ParamType.Unknown)
                return null;

            string token, error;
            return TryConvertInput(name, type, constraints, ToEditText(type, rawValue), out token, out error) ? null : error;
        }

        static bool TryConvertBool(string input, out string cfgToken, out string error)
        {
            cfgToken = null;
            error = null;
            switch (input.ToLowerInvariant())
            {
                case "1": case "on": case "true": case "yes":
                    cfgToken = "1";
                    return true;
                case "0": case "off": case "false": case "no":
                    cfgToken = "0";
                    return true;
            }
            error = "Value must be ON or Off.";
            return false;
        }

        static bool TryConvertInt(string input, ParamConstraints constraints, out string cfgToken, out string error)
        {
            cfgToken = null;
            error = null;
            long value;

            if (constraints.IsEnum)
            {
                for (int i = 0; i < constraints.EnumLabels.Count; i++)
                {
                    if (string.Equals(constraints.EnumLabels[i], input, StringComparison.OrdinalIgnoreCase))
                    {
                        cfgToken = i.ToString(CultureInfo.InvariantCulture);
                        return true;
                    }
                }
            }

            Match m = s_hexInt.Match(input);
            if (m.Success)
            {
                ulong hex;
                if (!ulong.TryParse(m.Groups["hex"].Value, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out hex) || hex > uint.MaxValue)
                {
                    error = "Hexadecimal value is too large (max 0xFFFFFFFF).";
                    return false;
                }

                // Hex input is a bit pattern: for a param that may be negative, reinterpret as a signed 32-bit value.
                bool unsigned = constraints.Min.HasValue && constraints.Min.Value >= 0;
                value = (!unsigned && hex > int.MaxValue) ? (long)unchecked((int)(uint)hex) : (long)hex;
            }
            else if (!s_decimalInt.IsMatch(input) || !long.TryParse(input, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value))
            {
                error = constraints.IsEnum
                    ? "Value must be one of: " + string.Join(", ", constraints.EnumLabels) + "."
                    : "Value must be a whole number (decimal, or hexadecimal with a 0x prefix).";
                return false;
            }

            if (value < int.MinValue || value > uint.MaxValue)
            {
                error = "Value is out of range for a 32-bit integer.";
                return false;
            }

            if (!CheckRange(value, constraints, out error))
                return false;

            if (constraints.IsEnum && (value < 0 || value >= constraints.EnumLabels.Count))
            {
                error = "Value must be one of: " + string.Join(", ", constraints.EnumLabels) + ".";
                return false;
            }

            cfgToken = value.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        static bool TryConvertFloat(string input, ParamConstraints constraints, out string cfgToken, out string error)
        {
            cfgToken = null;
            error = null;
            double value;

            if (!s_float.IsMatch(input) || !double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out value) || double.IsInfinity(value) || double.IsNaN(value))
            {
                error = "Value must be a decimal number, like 1.5 (no exponents, Infinity or NaN).";
                return false;
            }

            if (!CheckRange(value, constraints, out error))
                return false;

            // Normalize the text a little, but otherwise keep what the user typed (eg. "5.0" stays "5.0").
            string text = input.TrimStart('+');
            bool negative = text.StartsWith("-", StringComparison.Ordinal);
            if (negative)
                text = text.Substring(1);
            if (text.StartsWith(".", StringComparison.Ordinal))
                text = "0" + text;
            if (text.EndsWith(".", StringComparison.Ordinal))
                text = text + "0";
            text = text.TrimStart('0');
            if (text.Length == 0 || text[0] == '.')
                text = "0" + text;
            if (negative && value != 0)
                text = "-" + text;

            cfgToken = text;
            return true;
        }

        static bool TryConvertString(string name, string input, out string cfgToken, out string error)
        {
            cfgToken = null;
            error = null;

            // Strip one layer of surrounding quotes -- straight, curly, or single -- commonly pasted by mistake.
            if (input.Length >= 2 && OpenQuotes.IndexOf(input[0]) >= 0 && OpenQuotes.IndexOf(input[input.Length - 1]) >= 0)
                input = input.Substring(1, input.Length - 2);

            if (input.IndexOf('"') >= 0)
            {
                error = "String values cannot contain doublequote characters.";
                return false;
            }
            if (input.IndexOf('\r') >= 0 || input.IndexOf('\n') >= 0)
            {
                error = "String values cannot contain line breaks.";
                return false;
            }

            string featureError;
            if (!FeatureFlags.Validate(name, input, out featureError))
            {
                error = featureError;
                return false;
            }

            cfgToken = "\"" + input + "\"";
            return true;
        }

        static bool CheckRange(double value, ParamConstraints constraints, out string error)
        {
            error = null;
            if ((constraints.Min.HasValue && value < constraints.Min.Value)
                || (constraints.Max.HasValue && value > constraints.Max.Value))
            {
                error = "Value is out of range.  " + constraints.Describe();
                return false;
            }
            return constraints.IsAllowedValue(value, out error);
        }

        /// <summary>Formats a number as a cfg token: integers in decimal; floats with at least one decimal place.</summary>
        public static string FormatNumber(ParamType type, double value)
        {
            if (type == ParamType.Int)
                return ((long)Math.Round(value)).ToString(CultureInfo.InvariantCulture);
            return Math.Round(value, 9).ToString("0.0########", CultureInfo.InvariantCulture);
        }

        static bool HasWhitespace(string s)
        {
            foreach (char c in s)
                if (char.IsWhiteSpace(c))
                    return true;
            return false;
        }
    }

    /// <summary>
    /// The 4 special "feature flag" params.  These may ONLY exist in Falcon BMS.cfg, and are strictly validated.
    /// </summary>
    public static class FeatureFlags
    {
        static readonly Regex s_allMinus = new Regex(@"^all(\-([A-Z0-9_]+))*$", RegexOptions.CultureInvariant);
        static readonly Regex s_nonePlus = new Regex(@"^none(\+([A-Z0-9_]+))*$", RegexOptions.CultureInvariant);

        static readonly Dictionary<string, Regex> s_flags = new Dictionary<string, Regex>(StringComparer.OrdinalIgnoreCase)
        {
            { "g_sCpuPerfOptimizations", s_allMinus },
            { "g_sPriorityFixes", s_allMinus },
            { "g_sCompatibleChanges", s_allMinus },
            { "g_sNonCompatibleChanges", s_nonePlus },
        };

        public static bool IsFeatureFlag(string name)
        {
            return name != null && s_flags.ContainsKey(name);
        }

        public static bool Validate(string name, string value, out string error)
        {
            error = null;
            Regex regex;
            if (name == null || !s_flags.TryGetValue(name, out regex))
                return true;

            if (regex.IsMatch(value))
                return true;

            error = (regex == s_allMinus)
                ? "Must be \"all\", optionally followed by -FLAG items (uppercase), eg. all-FOO-BAR_2"
                : "Must be \"none\", optionally followed by +FLAG items (uppercase), eg. none+FOO+BAR_2";
            return false;
        }
    }
}
