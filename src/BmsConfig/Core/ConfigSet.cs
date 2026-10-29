using System;
using System.Collections.Generic;
using System.IO;

namespace BmsConfig.Core
{
    public enum CfgFile
    {
        Bms,
        User,
        Vr,
    }

    public enum ValueSource
    {
        None,
        Bms,
        User,
        /// <summary>From Falcon BMS User.cfg, below the Launcher cut line.</summary>
        Launcher,
        Vr,
    }

    /// <summary>
    /// One param, merged across the 3 cfg files.
    /// </summary>
    public sealed class ParamEntry
    {
        public string Name { get; internal set; }
        public ParamType Type { get; internal set; }
        public string Category { get; internal set; }
        public string Description { get; internal set; }
        public ParamConstraints Constraints { get; internal set; }
        public bool IsFeatureFlag { get { return FeatureFlags.IsFeatureFlag(Name); } }

        /// <summary>Value in Falcon BMS.cfg (the baseline default), or null if not present there.</summary>
        public string BmsValue { get; internal set; }
        /// <summary>Effective value in Falcon BMS User.cfg (including the Launcher section), or null.</summary>
        public string UserValue { get; internal set; }
        /// <summary>True if the effective User.cfg value comes from below the Launcher cut line.</summary>
        public bool UserValueFromLauncher { get; internal set; }
        /// <summary>Effective value in Falcon BMS VR.cfg, or null.</summary>
        public string VrValue { get; internal set; }

        /// <summary>True if User.cfg has a commented-out set line for this param (above the cut line).</summary>
        public bool UserCommentedOut { get; internal set; }
        /// <summary>True if VR.cfg has a commented-out set line for this param.</summary>
        public bool VrCommentedOut { get; internal set; }

        /// <summary>Order of first appearance (Falcon BMS.cfg first, then User.cfg, then VR.cfg).</summary>
        public int Ordinal { get; internal set; }

        public string GetEffectiveValue(bool includeVr)
        {
            ValueSource source;
            return GetEffectiveValue(includeVr, out source);
        }

        public string GetEffectiveValue(bool includeVr, out ValueSource source)
        {
            if (includeVr && VrValue != null)
            {
                source = ValueSource.Vr;
                return VrValue;
            }
            if (UserValue != null)
            {
                source = UserValueFromLauncher ? ValueSource.Launcher : ValueSource.User;
                return UserValue;
            }
            if (BmsValue != null)
            {
                source = ValueSource.Bms;
                return BmsValue;
            }
            source = ValueSource.None;
            return null;
        }
    }

    /// <summary>
    /// The set of 3 cfg files in a BMS config folder, plus the merged (last-writer-wins) view of all params.
    /// </summary>
    public sealed class ConfigSet
    {
        public const string BmsFileName = "Falcon BMS.cfg";
        public const string UserFileName = "Falcon BMS User.cfg";
        public const string VrFileName = "Falcon BMS VR.cfg";
        public const string OtherCategory = "Other";

        public string Folder { get; private set; }
        public CfgDocument Bms { get; private set; }
        public CfgDocument User { get; private set; }
        public CfgDocument Vr { get; private set; }

        public IReadOnlyList<ParamEntry> Entries { get { return _entries; } }
        public IReadOnlyList<string> Categories { get { return _categories; } }

        readonly List<ParamEntry> _entries = new List<ParamEntry>();
        readonly List<string> _categories = new List<string>();
        readonly Dictionary<string, ParamEntry> _byName = new Dictionary<string, ParamEntry>(StringComparer.OrdinalIgnoreCase);

        public static string GetPath(string folder, CfgFile file)
        {
            switch (file)
            {
                case CfgFile.Bms: return Path.Combine(folder, BmsFileName);
                case CfgFile.User: return Path.Combine(folder, UserFileName);
                default: return Path.Combine(folder, VrFileName);
            }
        }

        /// <summary>A folder is usable if it contains Falcon BMS.cfg (the other two are optional).</summary>
        public static bool IsConfigFolder(string folder)
        {
            return !string.IsNullOrEmpty(folder) && File.Exists(GetPath(folder, CfgFile.Bms));
        }

        public static ConfigSet Load(string folder)
        {
            return FromDocuments(
                folder,
                CfgDocument.Load(GetPath(folder, CfgFile.Bms)),
                CfgDocument.LoadOrCreate(GetPath(folder, CfgFile.User)),
                CfgDocument.LoadOrCreate(GetPath(folder, CfgFile.Vr)));
        }

        public static ConfigSet FromDocuments(string folder, CfgDocument bms, CfgDocument user, CfgDocument vr)
        {
            ConfigSet set = new ConfigSet { Folder = folder, Bms = bms, User = user, Vr = vr };
            set.Merge();
            return set;
        }

        public ParamEntry Find(string name)
        {
            ParamEntry e;
            return _byName.TryGetValue(name, out e) ? e : null;
        }

        public CfgDocument GetDocument(CfgFile file)
        {
            switch (file)
            {
                case CfgFile.Bms: return Bms;
                case CfgFile.User: return User;
                default: return Vr;
            }
        }

        void Merge()
        {
            bool hasOther = false;

            // Falcon BMS.cfg: the manifest of params, defaults, categories and descriptions.
            string category = OtherCategory;
            foreach (CfgLine line in Bms.Lines)
            {
                if (line.Kind == CfgLineKind.Category)
                {
                    category = line.CategoryName;
                }
                else if (line.Kind == CfgLineKind.Set)
                {
                    ParamEntry e = GetOrAdd(line, category);
                    e.BmsValue = line.RawValue;
                    if (!string.IsNullOrEmpty(line.Description))
                        e.Description = line.Description;
                    if (category == OtherCategory)
                        hasOther = true;
                }
            }

            // Falcon BMS User.cfg: user overrides; unknown params get filed under "Other".
            bool belowCut = false;
            foreach (CfgLine line in User.Lines)
            {
                if (line.Kind == CfgLineKind.CutLine)
                {
                    belowCut = true;
                }
                else if (line.Kind == CfgLineKind.Set)
                {
                    ParamEntry e = GetOrAdd(line, OtherCategory);
                    e.UserValue = line.RawValue;
                    e.UserValueFromLauncher = belowCut;
                    if (e.Category == OtherCategory)
                        hasOther = true;
                }
                else if (line.Kind == CfgLineKind.CommentedSet && !belowCut)
                {
                    // Listed even if it's not in Falcon BMS.cfg, so the user can set it again.
                    ParamEntry e = GetOrAdd(line, OtherCategory);
                    e.UserCommentedOut = true;
                    if (e.Category == OtherCategory)
                        hasOther = true;
                }
            }

            // Falcon BMS VR.cfg: overrides, only when launched in VR.
            foreach (CfgLine line in Vr.Lines)
            {
                if (line.Kind == CfgLineKind.Set)
                {
                    ParamEntry e = GetOrAdd(line, OtherCategory);
                    e.VrValue = line.RawValue;
                    if (e.Category == OtherCategory)
                        hasOther = true;
                }
                else if (line.Kind == CfgLineKind.CommentedSet)
                {
                    ParamEntry e = GetOrAdd(line, OtherCategory);
                    e.VrCommentedOut = true;
                    if (e.Category == OtherCategory)
                        hasOther = true;
                }
            }

            foreach (ParamEntry e in _entries)
            {
                e.Constraints = ParamConstraints.Parse(e.Description);
                if (!_categories.Contains(e.Category) && e.Category != OtherCategory)
                    _categories.Add(e.Category);
            }
            if (hasOther)
                _categories.Add(OtherCategory);
        }

        ParamEntry GetOrAdd(CfgLine line, string category)
        {
            ParamEntry e;
            if (!_byName.TryGetValue(line.Name, out e))
            {
                e = new ParamEntry
                {
                    Name = line.Name,
                    Type = ParamTypes.FromName(line.Name),
                    Category = category,
                    Description = line.Description,
                    Ordinal = _entries.Count,
                };
                _byName.Add(line.Name, e);
                _entries.Add(e);
            }
            else if (string.IsNullOrEmpty(e.Description) && !string.IsNullOrEmpty(line.Description))
            {
                e.Description = line.Description;
            }
            return e;
        }

        // ---- editing -----------------------------------------------------------------------------

        /// <summary>
        /// Writes a value to one cfg file, and saves it immediately.  The file is re-read from disk first,
        /// so that changes made by other tools (eg. the Launcher) since we loaded it are not clobbered.
        /// </summary>
        public static void WriteValue(string folder, CfgFile file, string name, string cfgToken)
        {
            if (file != CfgFile.Bms && FeatureFlags.IsFeatureFlag(name))
                throw new InvalidOperationException(name + " may only be set in " + BmsFileName + ".");
            if (file == CfgFile.Bms && !FeatureFlags.IsFeatureFlag(name))
                throw new InvalidOperationException(BmsFileName + " is read-only, except for the feature-flag params.");

            CfgDocument doc = (file == CfgFile.Bms)
                ? CfgDocument.Load(GetPath(folder, file))
                : CfgDocument.LoadOrCreate(GetPath(folder, file));

            doc.SetValue(name, cfgToken, respectCutLine: file == CfgFile.User);
            doc.Save();
        }

        /// <summary>
        /// Removes a param from User.cfg or VR.cfg (reverting it to the default), and saves immediately.
        /// Lines with a trailing comment (eg. the user's notes) are commented out rather than deleted.
        /// In User.cfg, only lines above the Launcher cut line are affected.
        /// </summary>
        public static bool RemoveValue(string folder, CfgFile file, string name)
        {
            if (file == CfgFile.Bms)
                throw new InvalidOperationException(BmsFileName + " is read-only.");

            string path = GetPath(folder, file);
            if (!File.Exists(path))
                return false;

            CfgDocument doc = CfgDocument.Load(path);
            if (doc.RemoveValue(name, respectCutLine: file == CfgFile.User) == 0)
                return false;

            doc.Save();
            return true;
        }
    }
}
