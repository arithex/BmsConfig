using System.ComponentModel;
using BmsConfig.Core;

namespace BmsConfig
{
    /// <summary>
    /// A row in the master list: one param, displayed according to the current view options.
    /// </summary>
    public sealed class ParamItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public ParamEntry Entry { get; private set; }
        bool _includeVr;

        public ParamItem(ParamEntry entry, bool includeVr)
        {
            Entry = entry;
            _includeVr = includeVr;
        }

        public string Name { get { return Entry.Name; } }
        public string Category { get { return Entry.Category; } }
        public int Ordinal { get { return Entry.Ordinal; } }

        public string Value { get { return ValueConverter.ToDisplay(Entry.Type, Entry.GetEffectiveValue(_includeVr), Entry.Constraints); } }
        public string Default { get { return ValueConverter.ToDisplay(Entry.Type, Entry.BmsValue, Entry.Constraints); } }

        public ValueSource Source
        {
            get
            {
                ValueSource source;
                Entry.GetEffectiveValue(_includeVr, out source);
                return source;
            }
        }

        public string SourceText { get { return SourceToText(Source); } }

        /// <summary>True if the value is overridden by User.cfg (or VR.cfg, when included in the view).</summary>
        public bool IsModified
        {
            get
            {
                ValueSource s = Source;
                return s == ValueSource.User || s == ValueSource.Launcher || s == ValueSource.Vr;
            }
        }

        public static string SourceToText(ValueSource source)
        {
            switch (source)
            {
                case ValueSource.Bms: return "BMS";
                case ValueSource.User: return "User";
                case ValueSource.Launcher: return "Launcher";
                case ValueSource.Vr: return "VR";
                default: return "";
            }
        }

        public bool Matches(string search)
        {
            if (string.IsNullOrEmpty(search))
                return true;

            return Contains(Entry.Name, search) || Contains(Entry.Description, search) || Contains(Entry.Category, search);
        }

        static bool Contains(string text, string search)
        {
            return text != null && text.IndexOf(search, System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public void Update(ParamEntry entry, bool includeVr)
        {
            Entry = entry;
            _includeVr = includeVr;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(null)); // all properties
        }
    }
}
