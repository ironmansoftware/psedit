using System;
using System.Diagnostics;
using System.IO;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Terminal.Gui;
using System.Collections.Generic;
using PowerShellToolsPro.Cmdlets.Editor;

namespace psedit
{
    [Cmdlet("Show", "PSEditor")]
    [Alias("psedit")]
    public class ShowEditorCommand : PSCmdlet
    {
        private EditorTextView textEditor;
        private StatusBar statusBar;
        private StatusItem fileNameStatus;
        private StatusItem position;
        private StatusItem cursorStatus;
        private StatusItem languageStatus;
        private Toplevel top;
        private Runspace _runspace;
        private string currentDirectory;
        private string _fileName;
        #region Find and replace variables
        private Window _winDialog;
        private TabView _tabView;
        private string _textToFind;
        private string _textToReplace;
        private bool _matchCase;
        private bool _matchWholeWord;
        #endregion
        private List<string> _allowedFileTypes = new List<string>();

        [Parameter(ParameterSetName = "Path", ValueFromPipeline = true, Position = 0)]
        public string Path { get; set; }

        [Parameter(ParameterSetName = "Path", ValueFromPipeline = true, Position = 1, Mandatory = false)]
        public int Line { get; set; }

        [Parameter(ParameterSetName = "Path", ValueFromPipeline = true, Position = 2, Mandatory = false)]
        public int Column { get; set; }

        [Parameter()]
        public string ConfigurationFile { get; set; }

        private byte[] _originalText = new System.IO.MemoryStream().ToArray();

        [Parameter()]
        public SwitchParameter DisableFormatOnSave { get; set; }

        protected override void BeginProcessing()
        {
            string configPath = "";
            if (!string.IsNullOrEmpty(ConfigurationFile))
            {
                // evaluate provided config path
                configPath = GetUnresolvedProviderPathFromPSPath(ConfigurationFile);
                if (!File.Exists(configPath))
                {
                    throw new ArgumentException("Invalid filepath provided for Configuration File");
                }
            }
            else
            {
                // evaluate if theme exists in My Documents
                var myDocumentsThemePath = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "psedit.json");
                if (File.Exists(myDocumentsThemePath))
                {
                    configPath = myDocumentsThemePath;
                }
                // evaluate if theme exists in working directory
                var workingDirectoryThemePath = GetUnresolvedProviderPathFromPSPath("psedit.json");
                if (File.Exists(workingDirectoryThemePath))
                {
                    configPath = workingDirectoryThemePath;
                }
            }
            // Load theme from specified config file or default location
            if (File.Exists(configPath))
            {
                ThemeService.Instance.LoadTheme(configPath);
            }

            _runspace = RunspaceFactory.CreateRunspace();
            _runspace.Open();
            currentDirectory = SessionState.Path.CurrentLocation.Path;
            // populate the allowed file types for dialogs on startup
            _allowedFileTypes.Add(".ps1");
            _allowedFileTypes.Add(".psm1");
            _allowedFileTypes.Add(".psd1");
            _allowedFileTypes.Add(".json");
            _allowedFileTypes.Add(".txt");
            _allowedFileTypes.Add(".yaml");
            _allowedFileTypes.Add(".yml");

            // ...existing code...
        }

        private MenuItem CreateAllowsTabChecked()
        {
            var item = new MenuItem
            {
                Title = "Allows Tab"
            };
            item.CheckType |= MenuItemCheckStyle.Checked;
            item.Checked = textEditor.AllowsTab;
            item.Action += () =>
            {
                textEditor.AllowsTab = item.Checked = !item.Checked;
            };

            return item;
        }
        private void SetMenuBar(Toplevel top)
        {

            var menuItems = new List<MenuBarItem>();
            menuItems.Add(new MenuBarItem("_File", new MenuItem[] {
                                new MenuItem ("_New", "", New),
                                new MenuItem ("_Open", "", Open, shortcut: Key.CtrlMask | Key.O),
                                new MenuItem ("_Save", "", () => {
                                    Save(false);
                                }, shortcut: Key.CtrlMask | Key.S),
                                new MenuItem ("Save As", "", () => {
                                    Save(true);
                                }),
                                new MenuItem ("_Quit", "", Quit, shortcut: Key.CtrlMask | Key.Q)
                            }));
            menuItems.Add(new MenuBarItem("_Edit", new MenuItem[] {
                                new MenuItem ("_Find", "", () => Find(), shortcut: Key.CtrlMask | Key.F),
                                new MenuItem ("_Replace", "", () => Replace(), shortcut: Key.CtrlMask | Key.H),
                                null,
                                new MenuItem ("_Select All", "", () => SelectAll(), shortcut: Key.CtrlMask | Key.T),
                                //new MenuItem("Autocomplete", "", Autocomplete, shortcut: Key.CtrlMask | Key.Space),
                            }));

            var formatMenuItem = new List<MenuItem>();
            formatMenuItem.Add(CreateAllowsTabChecked());
            if (textEditor.CanFormat)
            {
                formatMenuItem.Add(null);
                formatMenuItem.Add(new MenuItem("Format", "", Format, shortcut: Key.CtrlMask | Key.ShiftMask | Key.R));
            }
            menuItems.Add(new MenuBarItem("_Format", formatMenuItem.ToArray()));
            var viewMenuItem = new List<MenuItem>();
            if (textEditor.CanSyntaxHighlight)
            {
                viewMenuItem.Add(new MenuItem("Syntax Errors", "", () => { SyntaxErrorDialog.Show(textEditor.Errors); }));
            }
            if (textEditor._language == LanguageEnum.Powershell)
            {
                viewMenuItem.Add(new MenuItem("Errors", "", () => { ErrorDialog.Show(_runspace); }));
                viewMenuItem.Add(new MenuItem("Variables", "", () => { VariableDialog.Show(_runspace); }));
            }
            if (viewMenuItem.Count > 0)
            {
                menuItems.Add(new MenuBarItem("_View", viewMenuItem.ToArray()));
            }
            if (textEditor.CanRun)
            {
                menuItems.Add(new MenuBarItem("_Run", new MenuItem[] {
                                new MenuItem("Run", "", Run, shortcut: Key.F5),
                                new MenuItem("Execution Selection", "", ExecuteSelection, shortcut: Key.F8),
                                new MenuItem("Exit and Run In Console", "", ExitAndRun, shortcut: Key.CtrlMask | Key.ShiftMask | Key.F5),
                            }));
            }

            menuItems.Add(new MenuBarItem("_Help", new[] {
                                new MenuItem("_About", "", () => MessageBox.Query("About", $"PowerShell Pro Tools Terminal Editor\nVersion: {base.MyInvocation.MyCommand.Module.Version.ToString()}\n", "Ok")),
                                new MenuItem("_GitHub", "", () => {
                                    try {
                                        Process.Start(new ProcessStartInfo {
                                            FileName = "https://github.com/ironmansoftware/psedit",
                                            UseShellExecute = true
                                        });
                                    } catch {}
                                })
                            }));
            if (top.MenuBar is not null)
            {
                top.MenuBar.Menus = menuItems.ToArray();
            }
            else
            {
                var menuBar = new MenuBar(menuItems.ToArray());
                top.Add(menuBar);
            }

        }

        protected override void ProcessRecord()
        {
            textEditor = new EditorTextView(_runspace);
            textEditor.UnwrappedCursorPosition += (k) =>
            {
                UpdatePosition();
            };
            Application.RootKeyEvent = (k) =>
            {
                UpdatePosition();
                return true;
            };

            fileNameStatus = new StatusItem(Key.CtrlMask | Key.Q, "Unsaved", () => { Quit(); });
            // Load the path if it was passed in
            if (Path != null)
            {
                Path = GetUnresolvedProviderPathFromPSPath(Path);
                LoadFile();
            }
            // Set the cursor position if it was passed in
            if (Line > 0 || Column > 0)
            {
                if (!(Line > 0))
                {
                    Line = 1;
                }
                if (!(Column > 0))
                {
                    Column = 1;
                }
                textEditor.CursorPosition = new Point(Column - 1, Line - 1);
            }
            Application.Init();
            top = Application.Top;
            languageStatus = new StatusItem(Key.Unknown, "Text", () => { });
            var versionStatus = new StatusItem(Key.Unknown, base.MyInvocation.MyCommand.Module.Version.ToString(), () => { });
            position = new StatusItem(Key.Unknown, "", () => { });
            cursorStatus = new StatusItem(Key.Unknown, "", () => { });
            statusBar = new StatusBar(new StatusItem[] { fileNameStatus, versionStatus, position, cursorStatus, languageStatus });

            SetMenuBar(top);

            top.KeyPress += (e) => {
                var keys = ShortcutHelper.GetModifiersKey(e.KeyEvent);
                if (_winDialog != null && (e.KeyEvent.Key == Key.Esc)) {
                    DisposeWinDialog();
                }
                else if (_winDialog == null && (e.KeyEvent.Key == (Key.F | Key.CtrlMask)))
                {
                    Find();
                }
            };
            
            textEditor.X = 0;
            textEditor.Y = 1;
            textEditor.Height = Dim.Fill() - 1;
            textEditor.Width = Dim.Fill();
            textEditor.SetNeedsDisplay();

            textEditor.TextChanged += () =>
            {
                if (!fileNameStatus.Title.EndsWith("*") && textEditor.modified == true)
                {
                    fileNameStatus.Title += "*";
                    statusBar.SetNeedsDisplay();
                }
            };

            top.Add(textEditor);
            top.Add(statusBar);

            try
            {
                Application.Run();
                Application.Shutdown();
            }
            catch { }
            finally
            {
                _runspace.Dispose();
                if (!Console.IsInputRedirected)
                {
                    Console.Write("\u001b[?1h\u001b[?1003l");
                }
            }
        }
        private void SetLanguage(string path)
        {
            var extension = System.IO.Path.GetExtension(path);

            switch (extension)
            {
                case ".ps1": case ".psm1": case ".psd1":
                    textEditor.SetLanguage(LanguageEnum.Powershell);
                    break;
                case ".json":
                    textEditor.SetLanguage(LanguageEnum.JSON);
                    break;
                case ".txt":
                    textEditor.SetLanguage(LanguageEnum.Text);
                    break;
                case ".yml": case ".yaml":
                    textEditor.SetLanguage(LanguageEnum.YAML);
                    break;
                default:
                    textEditor.SetLanguage(LanguageEnum.Text);
                    break;
            }
            SetMenuBar(top);
        }

        private void Format()
        {
            if (textEditor.CanFormat)
            {
                try
                {
                    var formatValue = textEditor.Text.ToString();
                    if (!System.String.IsNullOrEmpty(formatValue))
                    {
                        var previousCursorPosition = textEditor.CursorPosition;
                        var previousTopRow = textEditor.TopRow;
                        // format text in editor
                        textEditor.Format();
                        if (textEditor.Text != _originalText)
                        {
                            if (!fileNameStatus.Title.EndsWith("*"))
                            {
                                fileNameStatus.Title += "*";
                                textEditor.modified = true;
                            }
                        }
                        else 
                        {
                            textEditor.modified = false;
                            fileNameStatus.Title = fileNameStatus.Title.TrimEnd("*");
                        }
                        textEditor.TopRow = previousTopRow;  
                        textEditor.CursorPosition = previousCursorPosition;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.ErrorQuery("Formatting Failed", ex.Message);
                }
            }
        }
        private bool CanCloseFile()
        {
            if (textEditor.Text == _originalText)
            {
                return true;
            }
            var fileName = _fileName != null ? _fileName : fileNameStatus.Title;
            var r = MessageBox.ErrorQuery("Save File",
                $"Do you want save changes in {fileName}?", "Yes", "No", "Cancel");
            if (r == 0) 
            {
                return Save(false);
            }
            else if (r == 1)
            {
                return true;
            }

            return false;
        }

        private void Open()
        {
            if (!CanCloseFile())
            {
                return;
            }
            var dialog = new OpenDialog("Open file", "", _allowedFileTypes);
            dialog.CanChooseDirectories = false;
            dialog.CanChooseFiles = true;
            dialog.AllowsMultipleSelection = false;
            dialog.DirectoryPath = currentDirectory;

            Application.Run(dialog);

            if (dialog.FilePath.IsEmpty || dialog.Canceled == true)
            {
                return;
            }

            if (!System.IO.Path.HasExtension(dialog.FilePath.ToString()))
            {
                return;
            }

            try
            {
                Path = dialog.FilePath.ToString();
                LoadFile();
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Failed", "Failed to load Window: " + ex.Message, "Ok");
            }
        }

        private void Quit()
        {
            try
            {
                if (!CanCloseFile())
                {
                    return;
                }
                Application.RequestStop();
            }
            catch {}
        }

        private void LoadFile()
        {
            if (Path != null)
            {
                textEditor.LoadFile(Path);
                textEditor.modified = false;
                _originalText = textEditor.Text.ToByteArray();
                _fileName = System.IO.Path.GetFileName(Path);
                currentDirectory = System.IO.Path.GetDirectoryName(Path);
                fileNameStatus.Title = _fileName;
                SetLanguage(Path);
                if (statusBar != null)
                {
                    UpdatePosition();
                }
            }
        }

        private void ExitAndRun()
        {
            var text = textEditor.Text.ToString();

            if (textEditor.CanRun)
            {
                try
                {
                    Application.RequestStop();
                }
                catch { }
                
                if (Path != null)
                {
                    Save(false);
                    textEditor.Run(Path, true);
                }
                else
                {
                    textEditor.RunText(text, true);
                }
            }
        }

        private void Run()
        {
            if (textEditor.CanRun)
            {
                string output = String.Empty;
                if (Path != null)
                {
                    Save(false);
                    output = textEditor.Run(Path);
                }
                else
                {
                    output = textEditor.RunText(textEditor.Text.ToString());
                }

                var dialog = new Dialog();
                var button = new Button("Ok");
                dialog.AddButton(button);

                dialog.Add(new TextView
                {
                    Text = output,
                    Height = Dim.Fill(),
                    Width = Dim.Fill()
                });

                Application.Run(dialog);
            }
        }

        private void ExecuteSelection()
        {
            if (textEditor.CanRun)
            {
                string output = String.Empty;
                output = textEditor.RunText(textEditor.SelectedText.ToString());
    
                var dialog = new Dialog();
                var button = new Button("Ok");
                dialog.AddButton(button);

                dialog.Add(new TextView
                {
                    Text = output,
                    Height = Dim.Fill(),
                    Width = Dim.Fill()
                });

                Application.Run(dialog);
            }
        }

        private void New()
        {
            if (!CanCloseFile())
            {
                return;
            }
            fileNameStatus.Title = "Unsaved";
            _fileName = "Unsaved";
            Path = null;
            _originalText = new System.IO.MemoryStream().ToArray();
            textEditor.Text = _originalText;
            textEditor.SetLanguage(LanguageEnum.Powershell);
            SetMenuBar(top);
        }

        private bool Save(bool saveAs)
        {
            if (string.IsNullOrEmpty(Path) || saveAs)
            {
                var dialog = new SaveDialog(saveAs ? "Save file as" : "Save file", "", _allowedFileTypes);
                dialog.DirectoryPath = currentDirectory;
                Application.Run(dialog);

                if (dialog.FilePath.IsEmpty || dialog.Canceled == true || Directory.Exists(dialog.FilePath.ToString()))
                {
                    return false;
                }
                Path = dialog.FilePath.ToString();
                _fileName = dialog.FileName.ToString();
                fileNameStatus.Title = dialog.FileName;
            }
            statusBar.SetNeedsDisplay();

            try
            {
                if (!DisableFormatOnSave && textEditor.CanFormat)
                {
                    Format();
                }
                File.WriteAllText(Path, textEditor.Text.ToString());
                _originalText = textEditor.Text.ToByteArray();
                fileNameStatus.Title = fileNameStatus.Title.TrimEnd("*");
                textEditor.modified = false;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Failed", "Failed to save. " + ex.Message, "Ok");
            }
            return false;
        }

        private void SelectAll()
        {
            textEditor.SelectAll();
        }

        #region Find and Replaces methods
        private void Replace()
        {
            CreateFindReplace(false);
        }

        private void ReplaceNext()
        {
            ContinueFind(true, true);
        }

        private void ReplacePrevious()
        {
            ContinueFind(false, true);
        }
        private void ReplaceAll()
        {
            if (string.IsNullOrEmpty(_textToFind) || (string.IsNullOrEmpty(_textToReplace) && _winDialog == null)) 
            {
                Replace();
                return;
            }

            if (textEditor.ReplaceAllText(_textToFind, _matchCase, _matchWholeWord, _textToReplace)) 
            {
                MessageBox.Query("Replace All", $"All occurrences of: '{_textToFind}' were replaced with: '{_textToReplace}'", "Ok");
            } 
            else 
            {
                MessageBox.Query("Replace All", $"Found no occurrences of text: '{_textToFind}'", "Ok");
            }
        }
        private void SetFindText()
        {
            _textToFind = !textEditor.SelectedText.IsEmpty
                ? textEditor.SelectedText.ToString()
                : string.IsNullOrEmpty(_textToFind) ? "" : _textToFind;

            _textToReplace = string.IsNullOrEmpty(_textToReplace) ? "" : _textToReplace;
        }
        private View ReplaceTab()
        {
            var d = new View();
            d.DrawContent += (e) => {
                foreach (var v in d.Subviews) {
                    v.SetNeedsDisplay();
                }
            };

            var lblWidth = "Replace:".Length;

            var label = new Label("Find:") {
                Y = 1,
                Width = lblWidth,
                TextAlignment = TextAlignment.Right,
                AutoSize = false
            };
            d.Add(label);

            SetFindText();
            var txtToFind = new TextField(_textToFind) {
                X = Pos.Right (label) + 1,
                Y = Pos.Top (label),
                Width = 20
            };
            txtToFind.Enter += (_) => txtToFind.Text = _textToFind;
            d.Add(txtToFind);

            var btnFindNext = new Button("Replace _Next") {
                X = Pos.Right (txtToFind) + 1,
                Y = Pos.Top (label),
                Width = 20,
                Enabled = !txtToFind.Text.IsEmpty,
                TextAlignment = TextAlignment.Centered,
                IsDefault = true,
                AutoSize = false
            };
            btnFindNext.Clicked += () => ReplaceNext();
            d.Add(btnFindNext);

            label = new Label("Replace:") {
                X = Pos.Left (label),
                Y = Pos.Top (label) + 1,
                Width = lblWidth,
                TextAlignment = TextAlignment.Right
            };
            d.Add(label);

            SetFindText();
            var txtToReplace = new TextField(_textToReplace) {
                X = Pos.Right (label) + 1,
                Y = Pos.Top (label),
                Width = 20
            };
            txtToReplace.TextChanged += (e) => _textToReplace = txtToReplace.Text.ToString();
            d.Add(txtToReplace);

            var btnFindPrevious = new Button("Replace _Previous") {
                X = Pos.Right(txtToFind) + 1,
                Y = Pos.Top(btnFindNext) + 1,
                Width = 20,
                Enabled = !txtToFind.Text.IsEmpty,
                TextAlignment = TextAlignment.Centered,
                AutoSize = false
            };
            btnFindPrevious.Clicked += () => ReplacePrevious();
            d.Add(btnFindPrevious);

            var btnReplaceAll = new Button("Replace _All") {
                X = Pos.Right(txtToFind) + 1,
                Y = Pos.Top(btnFindPrevious) + 1,
                Width = 20,
                Enabled = !txtToFind.Text.IsEmpty,
                TextAlignment = TextAlignment.Centered,
                AutoSize = false
            };
            btnReplaceAll.Clicked += () => ReplaceAll();
            d.Add(btnReplaceAll);

            txtToFind.TextChanged += (e) => {
                _textToFind = txtToFind.Text.ToString();
                textEditor.FindTextChanged();
                btnFindNext.Enabled = !txtToFind.Text.IsEmpty;
                btnFindPrevious.Enabled = !txtToFind.Text.IsEmpty;
                btnReplaceAll.Enabled = !txtToFind.Text.IsEmpty;
            };

            var btnCancel = new Button("Cancel") {
                X = Pos.Right(txtToFind) + 1,
                Y = Pos.Top(btnReplaceAll) + 1,
                Width = 20,
                TextAlignment = TextAlignment.Centered,
                AutoSize = false
            };
            btnCancel.Clicked += () => {
                DisposeWinDialog();
            };
            d.Add(btnCancel);

            var ckbMatchCase = new CheckBox("Match c_ase") {
                X = 0,
                Y = Pos.Top(txtToFind) + 2,
                Checked = _matchCase
            };
            ckbMatchCase.Toggled += (e) => _matchCase = ckbMatchCase.Checked;
            d.Add(ckbMatchCase);

            var ckbMatchWholeWord = new CheckBox("Match _whole word") {
                X = 0,
                Y = Pos.Top(ckbMatchCase) + 1,
                Checked = _matchWholeWord
            };
            ckbMatchWholeWord.Toggled += (e) => _matchWholeWord = ckbMatchWholeWord.Checked;
            d.Add(ckbMatchWholeWord);

            d.Width = lblWidth + txtToFind.Width + btnFindNext.Width + 2;
            d.Height = btnFindNext.Height + btnFindPrevious.Height + btnCancel.Height + 4;

            return d;
        }

        private void Find()
        {
            CreateFindReplace();
        }
        private void FindNext()
        {
            ContinueFind();
        }

        private void ContinueFind(bool next = true, bool replace = false)
        {
            if (!replace && string.IsNullOrEmpty(_textToFind)) {
                Find();
                return;
            } else if (replace && (string.IsNullOrEmpty(_textToFind)
                || (_winDialog == null && string.IsNullOrEmpty(_textToReplace)))) {
                Replace();
                return;
            }

            bool found;
            bool gaveFullTurn;

            if (next) 
            {
                if (!replace) 
                {
                    found = textEditor.FindNextText(_textToFind, out gaveFullTurn, _matchCase, _matchWholeWord);
                } 
                else 
                {
                    found = textEditor.FindNextText(_textToFind, out gaveFullTurn, _matchCase, _matchWholeWord, _textToReplace, true);
                }
            }
            else 
            {
                if (!replace) 
                {
                    found = textEditor.FindPreviousText(_textToFind, out gaveFullTurn, _matchCase, _matchWholeWord);
                } 
                else 
                {
                    found = textEditor.FindPreviousText(_textToFind, out gaveFullTurn, _matchCase, _matchWholeWord, _textToReplace, true);
                }
            }
            if (!found) 
            {
                MessageBox.Query("Find", $"The following text was not found: '{_textToFind}'", "Ok");
            }
            else if (gaveFullTurn) 
            {
                MessageBox.Query("Find", $"No more occurrences were found for the following text: '{_textToFind}'", "Ok");
            }
        }

        private void FindPrevious()
        {
            ContinueFind(false);
        }
        private void CreateFindReplace(bool isFind = true)
        {
            if (_winDialog != null) {
                _winDialog.SetFocus();
                return;
            }

            _winDialog = new Window(isFind ? "Find" : "Replace") {
                X = textEditor.Bounds.Width / 2 - 30,
                Y = textEditor.Bounds.Height / 2 - 10,
                ColorScheme = Colors.Dialog
            };

            _tabView = new TabView() {
                X = 0,
                Y = 0,
                Width = Dim.Fill(),
                Height = Dim.Fill()
            };
            if (isFind) 
            {
                var find = FindTab();
                _tabView.AddTab(new TabView.Tab ("Find", FindTab()), isFind);
                _winDialog.Width = find.Width + 4;
                _winDialog.Height = find.Height + 4;
            }
            else 
            {
                var replace = ReplaceTab();
                _tabView.AddTab(new TabView.Tab ("Replace", replace), !isFind);
                _winDialog.Width = replace.Width + 4;
                _winDialog.Height = replace.Height + 4;
            }

            _tabView.SelectedTabChanged += (s, e) => _tabView.SelectedTab.View.FocusFirst();
            _winDialog.Add(_tabView);

            top.Add(_winDialog);

            _winDialog.SuperView.BringSubviewToFront(_winDialog);
            _winDialog.SetFocus();
        }
        private View FindTab()
        {
            var d = new View();
            d.DrawContent += (e) => {
                foreach (var v in d.Subviews) {
                    v.SetNeedsDisplay();
                }
            };

            var lblWidth = "Replace:".Length;

            var label = new Label("Find:") {
                Y = 1,
                Width = lblWidth,
                TextAlignment = TextAlignment.Right,
                AutoSize = false
            };
            d.Add(label);

            SetFindText();
            var txtToFind = new TextField(_textToFind) {
                X = Pos.Right (label) + 1,
                Y = Pos.Top (label),
                Width = 20
            };
            txtToFind.Enter += (_) => txtToFind.Text = _textToFind;
            d.Add(txtToFind);

            var btnFindNext = new Button("Find _Next") {
                X = Pos.Right (txtToFind) + 1,
                Y = Pos.Top (label),
                Width = 20,
                Enabled = !txtToFind.Text.IsEmpty,
                TextAlignment = TextAlignment.Centered,
                IsDefault = true,
                AutoSize = false
            };
            btnFindNext.Clicked += () => FindNext();
            d.Add(btnFindNext);

            var btnFindPrevious = new Button("Find _Previous") {
                X = Pos.Right (txtToFind) + 1,
                Y = Pos.Top (btnFindNext) + 1,
                Width = 20,
                Enabled = !txtToFind.Text.IsEmpty,
                TextAlignment = TextAlignment.Centered,
                AutoSize = false
            };
            btnFindPrevious.Clicked += () => FindPrevious();
            d.Add(btnFindPrevious);

            txtToFind.TextChanged += (e) => {
                _textToFind = txtToFind.Text.ToString();
                textEditor.FindTextChanged();
                btnFindNext.Enabled = !txtToFind.Text.IsEmpty;
                btnFindPrevious.Enabled = !txtToFind.Text.IsEmpty;
            };

            var btnCancel = new Button("Cancel") {
                X = Pos.Right(txtToFind) + 1,
                Y = Pos.Top(btnFindPrevious) + 2,
                Width = 20,
                TextAlignment = TextAlignment.Centered,
                AutoSize = false
            };
            btnCancel.Clicked += () => {
                DisposeWinDialog();
            };
            d.Add(btnCancel);

            var ckbMatchCase = new CheckBox("Match c_ase") {
                X = 0,
                Y = Pos.Top (txtToFind) + 2,
                Checked = _matchCase
            };
            ckbMatchCase.Toggled += (e) => _matchCase = ckbMatchCase.Checked;
            d.Add(ckbMatchCase);

            var ckbMatchWholeWord = new CheckBox("Match _whole word") {
                X = 0,
                Y = Pos.Top(ckbMatchCase) + 1,
                Checked = _matchWholeWord
            };
            ckbMatchWholeWord.Toggled += (e) => _matchWholeWord = ckbMatchWholeWord.Checked;
            d.Add(ckbMatchWholeWord);

            d.Width = label.Width + txtToFind.Width + btnFindNext.Width + 2;
            d.Height = btnFindNext.Height + btnFindPrevious.Height + btnCancel.Height + 4;

            return d;
        }


        private void DisposeWinDialog()
        {
            _winDialog.Dispose();
            top.Remove(_winDialog);
            _winDialog = null;
        }
        #endregion
        private void UpdatePosition()
        {
            if (statusBar != null)
            {
                if (textEditor.ColumnErrors.ContainsKey(textEditor.CursorPosition))
                {
                    cursorStatus.Title = textEditor.ColumnErrors[textEditor.CursorPosition];
                }
                else
                {
                    cursorStatus.Title = string.Empty;
                }
                if ((textEditor.IsDirty == true || textEditor.modified == true) && _originalText != textEditor.Text && !fileNameStatus.Title.EndsWith("*") && fileNameStatus.Title != "Unsaved")
                {
                    fileNameStatus.Title += "*";
                }
                else if (_originalText == textEditor.Text && fileNameStatus.Title.EndsWith("*"))
                {
                    fileNameStatus.Title = fileNameStatus.Title.TrimEnd("*");
                }

                position.Title = $"Ln {textEditor.CursorPosition.Y + 1}, Col {textEditor.CursorPosition.X + 1}";
                languageStatus.Title = textEditor._language.ToString();
                statusBar.SetNeedsDisplay();
            }

        }
    }
}