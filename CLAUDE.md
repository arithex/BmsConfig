I need a simple GUI app to help my users modify and maintain a set of config files, for my game, Falcon BMS.

### Platform:

I need it to be C#, .Net Framework 4.8 for Win10/11 (target 'AnyCPU' -- with 64-bit preferred).  WPF is fine, but no other dependencies apart from Windows and .Net Framework.  (I need to distribute it as a single exe -- let's name it Config.exe -- and it needs to work in Linux/Wine environments and Win11-ARM64 too, so my goal is to keep things simple as possible.  Our other tools are mostly WPF targeting .Net Framework 4.8, and working well.)

### CFG file schema / grammar:

The ".cfg" file schema is a DSL (domain-specific language), consisting of simple "set {param_name} {value}" lines, followed by optional line-comment-style description text.  Supported datatypes are bool, int, float, and string -- the name of each property indicates the type.  Eg.

```
set g_nFooNumber 42 // This is an integer value
set g_fBarNumber 42.42 // This is a floating-point value
set g_bSomethingEnabled 1 // A boolean value, set to 'true
set g_bSomethingDisabled 0 // A boolean value, set to 'false'
set g_sSomeTextParam "this is a string value" // A string value, enclosed in doublequotes
```

The cfg grammar allows flexible whitespace, including blank lines.. and comment lines beginning with either # or //

The file itself is UTF-8 encoding (but may or may not have a BOM -- when writing, omit the BOM).

### Categories for params:

A comment line that begins and ends in a trailing // should be considered a "category" header for the subsequent block of params.  There may be some superfluous "ascii art" around those lines, to be ignored.. example:

```
///////////////////////////////
// This is a category header //
///////////////////////////////

set g_nFooNumber 42 // This is the Foo value.  Range: [0-42]
set g_fBarNumber 42.42 // This is the Bar value.  Range: [0.0-100.0]

// This is also a category header //
set g_bSomethingEnabled 1
set g_bSomethingDisabled 0
```

### Error handling:

Any lines which don't conform to the grammar, should simply be ignored (treated like comment lines).

### Duplicate params:

If there are duplicate lines (two or more lines setting the same param) it's "last-writer-wins".. ie. it's meant to look like an imperative language, using "set" as a verb.

### Document model:

As a tenet, I want this app to be a "cfg file editor" not a "cfg file generator".  That means, to honor and preserve any comments and blank lines, and preserve the overall ordering.

There are 3 cfg files in play:
- "Falcon BMS.cfg": we generally treat this as the readonly manifest of params, their baseline default values, their categories and comment/description text.
- "Falcon BMS User.cfg": this is where the majority of user changes will be saved.  Note, it may contain additional params not present in the first file -- that is fine, we can file those under an "Other" category.
- "Falcon BMS VR.cfg": these are overrides that will only apply when the game is launched in VR mode.

These files all exist in the same directory -- typically the "./User/Config" subdirectory, relative to the location of Config.exe.

There may be multiple versions of Falcon BMS installed, sxs.  The installs are listed in the following registry key -- the 'baseDir' value contains the absolute path to that location (which is where Config.exe will reside).

```
C:\Users\LocalAdmin>reg query "HKLM\SOFTWARE\WOW6432Node\Benchmark Sims" /s

HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Benchmark Sims\Falcon BMS 4.37
    baseDir    REG_SZ    C:\Falcon BMS 4.37

HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Benchmark Sims\Falcon BMS 4.38
    baseDir    REG_SZ    C:\Falcon BMS 4.38

HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Benchmark Sims\Falcon BMS 4.38 (Dev)
    baseDir    REG_SZ    C:\Falcon BMS 4.38 (Dev)

HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Benchmark Sims\Falcon BMS 4.38 (Internal)
    baseDir    REG_SZ    C:\Falcon BMS 4.38 (Internal)
```

The 3 cfg files reside in the `./User/Config` subfolder of those baseDir(s).

If the cfg files are not found there -- fallback to show a folder-open dialog, to let the user locate them.

The app should feature a dropdown list, allowing user to select which BMS install to edit -- if the current Config.exe resides in one of those baseDir directories, that one should be selected by default.

### Sample cfg:

For a sample cfg (first 100 lines of "Falcon BMS.cfg") see:
https://gist.github.com/arithex/82e3b8ec7a7078e82bba8c3f1f369db2

### Special params:

As an exception to the above rule about treating "Falcon BMS.cfg" as a readonly manifest -- there are 4 special params that may ONLY exist in that file, and therefore must be written back there.

These four are essentially "feature flag" strings, and any user input for these four must be strictly validated with the following regexes:

```
g_sCpuPerfOptimizations
g_sPriorityFixes
g_sCompatibleChanges
    "^all(\-([A-Z0-9_]+))*$"

g_sNonCompatibleChanges
    "^none(\+([A-Z0-9_]+))*$"
```

### The view:

The goal of the app should be to present a unified view of config values, last-writer-wins, in the implicit order of "Falcon BMS.cfg", "Falcon BMS User.cfg", and optionally "Falcon BMS VR.cfg"

I'm envisioning, a conventional master/detail listview UX.. nominally sized for 1920x1080 display, but resizable.

When editing values in the detail pane, user can choose to apply the edits/additions to User.cfg or VR.cfg (or both).

Changes should be applied immediately (auto-saved).

One small quirk: when editing or adding lines to "Falcon BMS User.cfg", they must be placed above the "cut line" which looks like
```
// LAUNCHER OVERRIDES BEGIN HERE - DO NOT EDIT OR ADD BELOW THIS LINE
```

### Look and Feel:

The README.md contains a couple UI mockups, from a prototype of this app.

The color palette and general look, should look like this UI mockup.

For boolean values, prefer text labels "ON" and "Off", not true/false or 1/0.

### Input validation and conversions:

Some cfg params may need a range enforced.  If the description-comment contains `Range:[0-100]` treat that as the allowable range (inclusive).  `Range:[0-]` means the value is unsigned (can't be negative) but no upper bound.

Integer values: may be signed or unsigned.  Always serialized as base-10 decimal, but accept hexadecimal input with leading 0x prefix .. eg "0x10000001" converts to 268435457.

Some integer values may be enumerations.. if the description-comment contains `Enum:[Low,Medium,High]` those labels can be presented, instead of 0,1,2.

Floating-point values: no Infinities or NaNs, and no exponential notation.

String values: a common user error is to paste in unicode fancy-quotes, instead of ascii 0x22 doublequotes.  If the user pastes a string already wrapped in doublequote (0x22 or fancy quotes) or singalquotes, remove them (but when serializing, always use 0x22 doublequotes for string values)
