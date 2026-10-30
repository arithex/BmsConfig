using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using BmsConfig.Core;
using Microsoft.Win32;
using ValueSource = BmsConfig.Core.ValueSource;

namespace BmsConfig
{
    public partial class MainWindow : Window
    {
        const string AllCategories = "(All categories)";

        readonly ObservableCollection<ParamItem> _items = new ObservableCollection<ParamItem>();
        readonly ICollectionView _view;

        ConfigSet _config;
        string _folder;
        bool _suppressEvents;
        string _sortProperty;
        ListSortDirection _sortDirection;
        WindowState _preFullScreenState;

        public MainWindow()
        {
            InitializeComponent();

            Version v = Assembly.GetExecutingAssembly().GetName().Version;
            string version = "ver " + v.Major + "." + v.Minor + (v.Build > 0 ? "." + v.Build : "");
            VersionText.Text = version;
            Title = "Falcon BMS Config Editor  " + version;

            LogoImage.Source = LoadLargestIconFrame();

            _view = CollectionViewSource.GetDefaultView(_items);
            _view.Filter = FilterItem;
            ParamList.ItemsSource = _view;

            _suppressEvents = true;
            switch (ThemeManager.Mode)
            {
                case ThemeMode.Light: ThemeLight.IsChecked = true; break;
                case ThemeMode.Dark: ThemeDark.IsChecked = true; break;
                default: ThemeSystem.IsChecked = true; break;
            }
            CategoryCombo.ItemsSource = new[] { AllCategories };
            CategoryCombo.SelectedIndex = 0;
            _suppressEvents = false;

            WindowFitting.FitToWorkArea(this); // also centers the window

            SourceInitialized += (s, e) => ThemeManager.ApplyTitleBar(this);
            Loaded += OnLoaded;
            PreviewKeyDown += OnWindowPreviewKeyDown;
            Drop += OnDrop;
            DragOver += OnDragOver;

            UpdateEmptyStates();
            ShowDetail(null);
        }

        /// <summary>
        /// The largest image in Config.ico (its 256px PNG frame), for the header logo.  An Image element pointed
        /// directly at the .ico would just use its first frame, which is typically a small one.
        /// </summary>
        static BitmapSource LoadLargestIconFrame()
        {
            try
            {
                BitmapDecoder decoder = BitmapDecoder.Create(new Uri("pack://application:,,,/Config.ico"),
                    BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                return decoder.Frames
                    .OrderByDescending(f => f.PixelWidth)
                    .ThenByDescending(f => f.Format.BitsPerPixel)
                    .First();
            }
            catch (Exception)
            {
                return null; // no logo is better than no app
            }
        }

        // ---- startup / install selection -----------------------------------------------------

        void OnLoaded(object sender, RoutedEventArgs e)
        {
            string exeDir = AppDomain.CurrentDomain.BaseDirectory;
            List<BmsInstall> installs = BmsInstalls.Discover();
            BmsInstall current = BmsInstalls.FindContaining(installs, exeDir);

            // Not registered (eg. a portable copy, or under Wine): still offer the install Config.exe lives in.
            if (current == null && ConfigSet.IsConfigFolder(Path.Combine(exeDir, "User", "Config")))
            {
                current = new BmsInstall("This folder", exeDir.TrimEnd(Path.DirectorySeparatorChar));
                installs.Insert(0, current);
            }

            _suppressEvents = true;
            InstallCombo.ItemsSource = installs;
            InstallCombo.IsEnabled = installs.Count > 0;
            _suppressEvents = false;

            // A folder on the command line wins (handy for testing, and for shortcuts).
            string[] args = Environment.GetCommandLineArgs();
            if (args.Length > 1 && Directory.Exists(args[1]) && ConfigSet.IsConfigFolder(args[1]))
            {
                LoadFolder(args[1]);
                return;
            }

            if (current == null && installs.Count == 1)
                current = installs[0];

            if (current != null)
            {
                InstallCombo.SelectedItem = current; // -> OnInstallChanged
            }
            else if (installs.Count == 0)
            {
                StatusText.Text = "No Falcon BMS installation was found in the registry.  Please locate your config folder.";
                BrowseForFolder(null);
            }
        }

        void OnInstallChanged(object sender, SelectionChangedEventArgs e)
        {
            InstallPlaceholder.Visibility = InstallCombo.SelectedItem == null ? Visibility.Visible : Visibility.Collapsed;
            if (_suppressEvents)
                return;

            BmsInstall install = InstallCombo.SelectedItem as BmsInstall;
            if (install == null)
                return;

            if (ConfigSet.IsConfigFolder(install.ConfigFolder))
            {
                LoadFolder(install.ConfigFolder);
            }
            else
            {
                StatusText.Text = "Config files not found in " + install.ConfigFolder + ".  Please locate them.";
                BrowseForFolder(Directory.Exists(install.BaseDir) ? install.BaseDir : null);
            }
        }

        void OnOpenFolderClick(object sender, RoutedEventArgs e) { BrowseForFolder(_folder); }
        void OnOpenFolderCommand(object sender, ExecutedRoutedEventArgs e) { BrowseForFolder(_folder); }

        /// <summary>
        /// Lets the user locate the config folder, by picking its "Falcon BMS.cfg".  (A file dialog is used rather
        /// than a folder dialog: it's native to WPF, and behaves well under Wine.)
        /// </summary>
        void BrowseForFolder(string initialDir)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                Title = "Locate your Falcon BMS config folder (select \"" + ConfigSet.BmsFileName + "\")",
                Filter = "Falcon BMS config|Falcon BMS*.cfg|All cfg files (*.cfg)|*.cfg",
                FileName = ConfigSet.BmsFileName,
                CheckFileExists = true,
            };
            if (!string.IsNullOrEmpty(initialDir) && Directory.Exists(initialDir))
                dlg.InitialDirectory = initialDir;

            if (dlg.ShowDialog(this) == true)
                OpenPath(dlg.FileName);
        }

        /// <summary>Opens a config folder, given the folder itself or any file within it.</summary>
        void OpenPath(string path)
        {
            string folder = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
            if (!ConfigSet.IsConfigFolder(folder))
            {
                MessageBox.Show(this, "\"" + ConfigSet.BmsFileName + "\" was not found in:\n\n" + folder,
                    "Config files not found", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Select the matching install in the dropdown, if there is one.
            _suppressEvents = true;
            InstallCombo.SelectedItem = (InstallCombo.ItemsSource as IEnumerable<BmsInstall>)?
                .FirstOrDefault(i => string.Equals(Path.GetFullPath(i.ConfigFolder).TrimEnd('\\', '/'),
                    Path.GetFullPath(folder).TrimEnd('\\', '/'), StringComparison.OrdinalIgnoreCase));
            _suppressEvents = false;
            InstallPlaceholder.Visibility = InstallCombo.SelectedItem == null ? Visibility.Visible : Visibility.Collapsed;

            LoadFolder(folder);
        }

        void OnDragOver(object sender, DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Link : DragDropEffects.None;
            e.Handled = true;
        }

        void OnDrop(object sender, DragEventArgs e)
        {
            string[] files = e.Data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
                OpenPath(files[0]);
        }

        // ---- loading -------------------------------------------------------------------------

        void LoadFolder(string folder)
        {
            try
            {
                _config = ConfigSet.Load(folder);
                _folder = folder;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to load config files from:\n\n" + folder + "\n\n" + ex.Message,
                    "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            string selected = (ParamList.SelectedItem as ParamItem)?.Name;
            bool includeVr = IncludeVrCheck.IsChecked == true;

            _items.Clear();
            foreach (ParamEntry entry in _config.Entries)
                _items.Add(new ParamItem(entry, includeVr));

            RefreshCategories();
            StatusText.Text = "Loaded from " + folder;
            _view.Refresh();
            UpdateEmptyStates();
            Reselect(selected);
        }

        /// <summary>
        /// Re-reads the cfg files from disk, updating the existing rows in place (so the scroll position and
        /// selection are kept).
        /// </summary>
        void Reload()
        {
            if (_folder == null)
                return;

            ConfigSet config;
            try
            {
                config = ConfigSet.Load(_folder);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to reload config files:\n\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            _config = config;
            bool includeVr = IncludeVrCheck.IsChecked == true;
            ParamItem selected = ParamList.SelectedItem as ParamItem;

            Dictionary<string, ParamItem> existing = _items.ToDictionary(i => i.Name, StringComparer.OrdinalIgnoreCase);
            bool sameSet = existing.Count == config.Entries.Count && config.Entries.All(en => existing.ContainsKey(en.Name));

            if (sameSet)
            {
                foreach (ParamEntry entry in config.Entries)
                    existing[entry.Name].Update(entry, includeVr);
            }
            else
            {
                _items.Clear();
                foreach (ParamEntry entry in config.Entries)
                {
                    ParamItem item;
                    if (existing.TryGetValue(entry.Name, out item))
                        item.Update(entry, includeVr);
                    else
                        item = new ParamItem(entry, includeVr);
                    _items.Add(item);
                }
            }

            RefreshCategories();
            _view.Refresh();
            UpdateEmptyStates();
            Reselect(selected?.Name);
        }

        void Reselect(string name)
        {
            ParamItem item = name == null ? null : _items.FirstOrDefault(i => string.Equals(i.Name, name, StringComparison.OrdinalIgnoreCase));
            if (item != null && _view.Contains(item))
            {
                ParamList.SelectedItem = item;
                ParamList.ScrollIntoView(item);
                ShowDetail(item);
            }
            else
            {
                ParamList.SelectedItem = null;
                ShowDetail(null);
            }
        }

        void OnReloadClick(object sender, RoutedEventArgs e) { Reload(); }
        void OnReloadCommand(object sender, ExecutedRoutedEventArgs e) { Reload(); }

        void RefreshCategories()
        {
            string selected = CategoryCombo.SelectedItem as string;
            List<string> categories = new List<string> { AllCategories };
            if (_config != null)
                categories.AddRange(_config.Categories);

            _suppressEvents = true;
            CategoryCombo.ItemsSource = categories;
            CategoryCombo.SelectedItem = categories.Contains(selected) ? selected : AllCategories;
            _suppressEvents = false;
        }

        // ---- filtering / sorting -------------------------------------------------------------

        bool FilterItem(object o)
        {
            ParamItem item = (ParamItem)o;

            string category = CategoryCombo.SelectedItem as string;
            if (category != null && category != AllCategories && item.Category != category)
                return false;

            if (ModifiedOnlyCheck.IsChecked == true && !item.IsModified)
                return false;

            return item.Matches(SearchBox.Text.Trim());
        }

        void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents || _view == null)
                return;

            _view.Refresh();
            UpdateEmptyStates();

            if (ParamList.SelectedItem == null)
                ShowDetail(null);
        }

        void OnIncludeVrChanged(object sender, RoutedEventArgs e)
        {
            bool includeVr = IncludeVrCheck.IsChecked == true;
            foreach (ParamItem item in _items)
                item.Update(item.Entry, includeVr);

            OnFilterChanged(sender, e);
            ShowDetail(ParamList.SelectedItem as ParamItem);
        }

        void OnFindCommand(object sender, ExecutedRoutedEventArgs e)
        {
            SearchBox.Focus();
            SearchBox.SelectAll();
        }

        /// <summary>Click a column header to sort: ascending, descending, then back to file order.</summary>
        void OnColumnHeaderClick(object sender, RoutedEventArgs e)
        {
            GridViewColumnHeader header = e.OriginalSource as GridViewColumnHeader;
            if (header == null || header.Column == null)
                return;

            string property;
            switch (header.Column.Header as string)
            {
                case "PARAMETER": property = "Name"; break;
                case "VALUE": property = "Value"; break;
                case "DEFAULT": property = "Default"; break;
                case "SOURCE": property = "SourceText"; break;
                case "CATEGORY": property = "Category"; break;
                default: return;
            }

            _view.SortDescriptions.Clear();
            if (_sortProperty != property)
            {
                _sortProperty = property;
                _sortDirection = ListSortDirection.Ascending;
            }
            else if (_sortDirection == ListSortDirection.Ascending)
            {
                _sortDirection = ListSortDirection.Descending;
            }
            else
            {
                _sortProperty = null;
            }

            if (_sortProperty != null)
                _view.SortDescriptions.Add(new SortDescription(_sortProperty, _sortDirection));
            _view.SortDescriptions.Add(new SortDescription("Ordinal", ListSortDirection.Ascending));

            if (ParamList.SelectedItem != null)
                ParamList.ScrollIntoView(ParamList.SelectedItem);
        }

        void UpdateEmptyStates()
        {
            int total = _items.Count;
            int shown = _view.Cast<object>().Count();

            ListEmptyState.Visibility = shown == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_config != null)
            {
                ListEmptyText.Text = total == 0 ? "No settings were found in the config files." : "No settings match the current filter.";
                ListEmptyHint.Visibility = Visibility.Collapsed;
            }

            if (_config == null)
            {
                CountText.Text = "";
                return;
            }

            List<string> files = new List<string> { ConfigSet.BmsFileName };
            if (File.Exists(ConfigSet.GetPath(_folder, CfgFile.User)))
                files.Add(ConfigSet.UserFileName);
            if (IncludeVrCheck.IsChecked == true && File.Exists(ConfigSet.GetPath(_folder, CfgFile.Vr)))
                files.Add(ConfigSet.VrFileName);

            CountText.Text = shown + " of " + total + " settings shown    [" + string.Join(" + ", files) + "]";
        }

        // ---- detail pane ---------------------------------------------------------------------

        void OnParamSelected(object sender, SelectionChangedEventArgs e)
        {
            ShowDetail(ParamList.SelectedItem as ParamItem);
        }

        ParamEntry SelectedEntry
        {
            get { return (ParamList.SelectedItem as ParamItem)?.Entry; }
        }

        void ShowDetail(ParamItem item)
        {
            if (item == null)
            {
                DetailPane.Visibility = Visibility.Collapsed;
                DetailEmptyState.Visibility = Visibility.Visible;
                return;
            }

            DetailPane.Visibility = Visibility.Visible;
            DetailEmptyState.Visibility = Visibility.Collapsed;

            ParamEntry entry = item.Entry;
            ParamConstraints c = entry.Constraints;
            bool includeVr = IncludeVrCheck.IsChecked == true;

            DetailName.Text = entry.Name;

            string typeText = "Type: " + ParamTypes.DisplayName(entry.Type);
            if (c.IsEnum)
                typeText += "    Values: " + string.Join(", ", c.EnumLabels.Select((l, i) => i + " = " + l));
            else if (c.NumericValues != null)
                typeText += "    Values: " + string.Join(", ", c.NumericValues.Select(v => ValueConverter.FormatNumber(entry.Type, v)));
            else if (c.HasRange)
                typeText += "    " + c.Describe();
            if (entry.Category == ConfigSet.UnknownCategory && entry.BmsValue == null)
                typeText += "    (not listed in " + ConfigSet.BmsFileName + ")";
            DetailType.Text = typeText;

            if (entry.IsFeatureFlag)
                DetailNotice.Text = "This is a feature-flag param: it can only be set in " + ConfigSet.BmsFileName + ".";
            else if (entry.UserValueFromLauncher)
                DetailNotice.Text = "This param is overridden by the Launcher (below the cut line in " + ConfigSet.UserFileName + "). Edits made here will not take effect while the Launcher sets it.";
            else
                DetailNotice.Text = "";
            DetailNotice.Visibility = DetailNotice.Text.Length > 0 ? Visibility.Visible : Visibility.Collapsed;

            DetailDescription.Text = string.IsNullOrEmpty(entry.Description) ? "(No description.)" : entry.Description;

            DetailDefault.Text = entry.BmsValue != null ? item.Default : "(none)";
            ValueSource source;
            string current = entry.GetEffectiveValue(includeVr, out source);
            DetailCurrent.Text = current != null ? ValueConverter.ToDisplay(entry.Type, current, c) : "(none)";
            DetailCurrentSource.Text = source != ValueSource.None ? "[from " + ParamItem.SourceToText(source) + "]" : "";
            DetailUser.Text = entry.UserValue != null
                ? ValueConverter.ToDisplay(entry.Type, entry.UserValue, c) + (entry.UserValueFromLauncher ? "   (Launcher section)" : "")
                : (entry.UserCommentedOut ? "(none; commented out)" : "(none)");
            DetailVr.Text = entry.VrValue != null
                ? ValueConverter.ToDisplay(entry.Type, entry.VrValue, c)
                : (entry.VrCommentedOut ? "(none; commented out)" : "(none)");

            // Editor
            _suppressEvents = true;
            bool isBool = entry.Type == ParamType.Bool;
            bool isEnum = entry.Type == ParamType.Int && c.IsEnum;
            IReadOnlyList<string> choices = (!isBool && !isEnum) ? c.GetChoices(entry.Type) : null;
            bool isChoice = choices != null;
            BoolEditor.Visibility = isBool ? Visibility.Visible : Visibility.Collapsed;
            EnumEditor.Visibility = isEnum ? Visibility.Visible : Visibility.Collapsed;
            ChoiceEditor.Visibility = isChoice ? Visibility.Visible : Visibility.Collapsed;
            TextEditor.Visibility = (!isBool && !isEnum && !isChoice) ? Visibility.Visible : Visibility.Collapsed;

            ChoiceEditor.Children.Clear();
            if (isChoice)
            {
                Style style = (Style)FindResource("ChoiceButton");
                foreach (string token in choices)
                {
                    RadioButton button = new RadioButton { Content = token, Tag = token, GroupName = "ChoiceValue", Style = style };
                    button.Click += (s, e) => OnEditorChoiceChanged();
                    ChoiceEditor.Children.Add(button);
                }
                SelectChoice(entry.Type, current); // off-grid values (eg. hand-edited) leave no button selected
            }

            if (isBool)
            {
                bool? b = current != null ? ValueConverter.ParseCfgBool(current) : null;
                BoolOn.IsChecked = b == true;
                BoolOff.IsChecked = b == false;
            }
            else if (isEnum)
            {
                EnumEditor.ItemsSource = c.EnumLabels;
                long? n = current != null ? ValueConverter.ParseCfgInt(current) : null;
                EnumEditor.SelectedIndex = (n.HasValue && n.Value >= 0 && n.Value < c.EnumLabels.Count) ? (int)n.Value : -1;
            }
            else
            {
                TextEditor.Text = ValueConverter.ToEditText(entry.Type, current);
            }
            EditorError.Text = "";

            // Save-to targets: the feature flags may only be written to Falcon BMS.cfg.
            SaveToBms.IsChecked = entry.IsFeatureFlag;
            SaveToUser.IsEnabled = !entry.IsFeatureFlag;
            SaveToVr.IsEnabled = !entry.IsFeatureFlag;
            if (entry.IsFeatureFlag)
            {
                SaveToUser.IsChecked = false;
                SaveToVr.IsChecked = false;
            }
            else if (SaveToUser.IsChecked != true && SaveToVr.IsChecked != true)
            {
                SaveToUser.IsChecked = true;
            }
            _suppressEvents = false;

            FillDefaultButton.IsEnabled = entry.BmsValue != null;
            SaveFeedback.Text = "";
            UpdateButtons();
        }

        IEnumerable<CfgFile> SaveTargets
        {
            get
            {
                ParamEntry entry = SelectedEntry;
                if (entry != null && entry.IsFeatureFlag)
                {
                    yield return CfgFile.Bms;
                    yield break;
                }
                if (SaveToUser.IsChecked == true)
                    yield return CfgFile.User;
                if (SaveToVr.IsChecked == true)
                    yield return CfgFile.Vr;
            }
        }

        void UpdateButtons()
        {
            ParamEntry entry = SelectedEntry;
            bool hasTargets = SaveTargets.Any();
            bool inputValid = EditorInput != null && EditorError.Text.Length == 0;

            ApplyButton.IsEnabled = entry != null && hasTargets && inputValid;
            RevertButton.IsEnabled = entry != null && !entry.IsFeatureFlag && hasTargets
                && ((SaveToUser.IsChecked == true && entry.UserValue != null && !entry.UserValueFromLauncher)
                    || (SaveToVr.IsChecked == true && entry.VrValue != null));
        }

        void OnSaveToChanged(object sender, RoutedEventArgs e)
        {
            if (!_suppressEvents)
                UpdateButtons();
        }

        void OnTextEditorChanged(object sender, TextChangedEventArgs e)
        {
            if (_suppressEvents)
                return;

            ParamEntry entry = SelectedEntry;
            if (entry == null)
                return;

            string token, error;
            ValueConverter.TryConvertInput(entry.Name, entry.Type, entry.Constraints, TextEditor.Text, out token, out error);
            EditorError.Text = error ?? "";
            SaveFeedback.Text = "";
            UpdateButtons();
        }

        void OnTextEditorKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                ApplyInput(TextEditor.Text);
                e.Handled = true;
            }
        }

        /// <summary>The new value in whichever editor is showing, or null if none is chosen yet.</summary>
        string EditorInput
        {
            get
            {
                if (BoolEditor.Visibility == Visibility.Visible)
                    return BoolOn.IsChecked == true ? "1" : BoolOff.IsChecked == true ? "0" : null;
                if (EnumEditor.Visibility == Visibility.Visible)
                    return EnumEditor.SelectedIndex >= 0 ? EnumEditor.SelectedIndex.ToString() : null;
                if (ChoiceEditor.Visibility == Visibility.Visible)
                    return ChoiceEditor.Children.OfType<RadioButton>().Where(b => b.IsChecked == true).Select(b => (string)b.Tag).FirstOrDefault();
                return TextEditor.Text;
            }
        }

        /// <summary>Checks the value button equivalent to the given cfg value (eg. "1" matches "1.0"), if any.</summary>
        void SelectChoice(ParamType type, string value)
        {
            foreach (RadioButton button in ChoiceEditor.Children.OfType<RadioButton>())
                button.IsChecked = value != null && ValueConverter.AreEquivalent(type, (string)button.Tag, value);
        }

        void OnApplyClick(object sender, RoutedEventArgs e)
        {
            string input = EditorInput;
            if (input != null)
                ApplyInput(input);
        }

        // ON/Off and enum choices are only staged here; they're saved by Apply & Save, like text values.
        void OnBoolClick(object sender, RoutedEventArgs e) { OnEditorChoiceChanged(); }
        void OnEnumChanged(object sender, SelectionChangedEventArgs e) { OnEditorChoiceChanged(); }

        void OnEditorChoiceChanged()
        {
            if (_suppressEvents)
                return;

            SaveFeedback.Text = "";
            UpdateButtons();
        }

        void OnFillDefaultClick(object sender, RoutedEventArgs e)
        {
            ParamEntry entry = SelectedEntry;
            if (entry == null || entry.BmsValue == null)
                return;

            if (BoolEditor.Visibility == Visibility.Visible)
            {
                bool? b = ValueConverter.ParseCfgBool(entry.BmsValue);
                BoolOn.IsChecked = b == true;
                BoolOff.IsChecked = b == false;
                OnEditorChoiceChanged();
            }
            else if (EnumEditor.Visibility == Visibility.Visible)
            {
                long? n = ValueConverter.ParseCfgInt(entry.BmsValue);
                EnumEditor.SelectedIndex = (n.HasValue && n.Value >= 0 && n.Value < EnumEditor.Items.Count) ? (int)n.Value : -1;
            }
            else if (ChoiceEditor.Visibility == Visibility.Visible)
            {
                SelectChoice(entry.Type, entry.BmsValue);
                OnEditorChoiceChanged();
            }
            else
            {
                TextEditor.Text = ValueConverter.ToEditText(entry.Type, entry.BmsValue);
                TextEditor.Focus();
                TextEditor.SelectAll();
            }
        }

        /// <summary>Validates the input, and writes it to the selected file(s) immediately.</summary>
        void ApplyInput(string input)
        {
            ParamEntry entry = SelectedEntry;
            if (entry == null || _folder == null)
                return;

            string token, error;
            if (!ValueConverter.TryConvertInput(entry.Name, entry.Type, entry.Constraints, input, out token, out error))
            {
                EditorError.Text = error;
                UpdateButtons();
                return;
            }

            List<CfgFile> targets = SaveTargets.ToList();
            if (targets.Count == 0)
            {
                EditorError.Text = "Choose a file to save to.";
                return;
            }

            List<string> saved = new List<string>();
            try
            {
                foreach (CfgFile file in targets)
                {
                    ConfigSet.WriteValue(_folder, file, entry.Name, token);
                    saved.Add(Path.GetFileName(ConfigSet.GetPath(_folder, file)));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to save " + entry.Name + ":\n\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            Reload();
            if (saved.Count > 0)
                SaveFeedback.Text = "Saved " + entry.Name + " to " + string.Join(" and ", saved) + "  (" + DateTime.Now.ToString("T") + ")";
        }

        void OnRevertClick(object sender, RoutedEventArgs e)
        {
            ParamEntry entry = SelectedEntry;
            if (entry == null || _folder == null || entry.IsFeatureFlag)
                return;

            List<string> changed = new List<string>();
            try
            {
                foreach (CfgFile file in SaveTargets)
                {
                    if (ConfigSet.RemoveValue(_folder, file, entry.Name))
                        changed.Add(Path.GetFileName(ConfigSet.GetPath(_folder, file)));
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to update cfg file:\n\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }

            string name = entry.Name;
            Reload();
            if (changed.Count > 0 && SelectedEntry != null)
                SaveFeedback.Text = "Removed " + name + " from " + string.Join(" and ", changed) + "  (" + DateTime.Now.ToString("T") + ")";
        }

        // ---- theme / full screen ---------------------------------------------------------------

        void OnThemeChecked(object sender, RoutedEventArgs e)
        {
            if (_suppressEvents)
                return;

            if (sender == ThemeLight)
                ThemeManager.Apply(ThemeMode.Light);
            else if (sender == ThemeDark)
                ThemeManager.Apply(ThemeMode.Dark);
            else
                ThemeManager.Apply(ThemeMode.System);
        }

        void OnFullScreenClick(object sender, RoutedEventArgs e) { ToggleFullScreen(); }

        void ToggleFullScreen()
        {
            if (WindowStyle == WindowStyle.None)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = _preFullScreenState;
                FullScreenButton.Content = "Full Screen";
            }
            else
            {
                _preFullScreenState = WindowState;
                WindowStyle = WindowStyle.None;
                // Re-maximize, so the window covers the taskbar too.
                WindowState = WindowState.Normal;
                WindowState = WindowState.Maximized;
                FullScreenButton.Content = "Exit Full Screen";
            }
        }

        void OnWindowPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.F11 || (e.Key == Key.Escape && WindowStyle == WindowStyle.None))
            {
                ToggleFullScreen();
                e.Handled = true;
            }
        }
    }
}
