using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using Terminal.Gui;
using System.Collections.Generic;

namespace psedit
{
    [Cmdlet("Show", "PSEditor")]
    [Alias("psedit")]
    public class ShowEditorCommand : PSCmdlet
    {
        private PowerShellEditorTextView textEditor;
        private StatusBar statusBar;
        private StatusItem fileNameStatus;
        private StatusItem position;
        private StatusItem cursorStatus;
        private Toplevel top;
        private Runspace _runspace;
        private string currentDirectory;

        #region Find and replace variables
        private Window _winDialog;
        private TabView _tabView;
        private string _textToFind;
        private string _textToReplace;
        private bool _matchCase;
        private bool _matchWholeWord;
        #endregion

        [Parameter(ParameterSetName = "Path", ValueFromPipeline = true, Position = 0)]
        public string Path { get; set; }
        private byte[] _originalText = new System.IO.MemoryStream().ToArray();

        [Parameter()]
        public SwitchParameter DisableFormatOnSave { get; set; }

        protected override void BeginProcessing()
        {
            _runspace = RunspaceFactory.CreateRunspace();
            _runspace.Open();
            currentDirectory = SessionState.Path.CurrentLocation.Path;
        }

        protected override void ProcessRecord()
        {
            textEditor = new PowerShellEditorTextView(_runspace);
            textEditor.UnwrappedCursorPosition += (k) =>
            {
                UpdatePosition();
            };
            Application.RootKeyEvent = (k) =>
            {
                UpdatePosition();
                return true;
            };

            fileNameStatus = new StatusItem(Key.Unknown, "Unsaved", () => { });

            if (Path != null)
            {
                Path = GetUnresolvedProviderPathFromPSPath(Path);
                LoadFile();
            }

            Application.Init();
            top = Application.Top;

            var versionStatus = new StatusItem(Key.Unknown, base.MyInvocation.MyCommand.Module.Version.ToString(), () => { });
            position = new StatusItem(Key.Unknown, "", () => { });
            cursorStatus = new StatusItem(Key.Unknown, "", () => { });

            statusBar = new StatusBar(new StatusItem[] { fileNameStatus, versionStatus, position, cursorStatus });

            top.Add(new MenuBar(new MenuBarItem[] {
                new MenuBarItem ("_File", new MenuItem [] {
                        new MenuItem ("_New", "", New),
                        new MenuItem ("_Open", "", Open, shortcut: Key.CtrlMask | Key.O),
                        new MenuItem ("_Save", "", () => {
                            Save(false);
                        }, shortcut: Key.CtrlMask | Key.S),
                        new MenuItem ("Save As", "", () => {
                            Save(true);
                        }),
                        new MenuItem ("_Quit", "", Quit, shortcut: Key.CtrlMask | Key.Q)
                    }),
                    new MenuBarItem("_Edit", new []
                    {
                        new MenuItem ("_Find", "", () => Find(), shortcut: Key.CtrlMask | Key.F),
                        new MenuItem ("_Replace", "", () => Replace(), shortcut: Key.CtrlMask | Key.H),
                        null,
                        new MenuItem ("_Select All", "", () => SelectAll(), shortcut: Key.CtrlMask | Key.T),
                        null,
                        new MenuItem("Format", "", Format, CanFormat, shortcut: Key.CtrlMask | Key.ShiftMask | Key.R),
                        //new MenuItem("Autocomplete", "", Autocomplete, shortcut: Key.CtrlMask | Key.Space),
                    }),
                    new MenuBarItem("_View", new []
                    {
                        new MenuItem("Errors", "", () => ErrorDialog.Show(_runspace)),
                        new MenuItem("Syntax Errors", "", () => SyntaxErrorDialog.Show(textEditor.Errors.Values.ToArray())),
                        //new MenuItem("History", "", () => HistoryDialog.Show(_runspace))
                    }),
                    new MenuBarItem("_Debug", new []
                    {
                        new MenuItem("Run", "", Run, shortcut: Key.F5),
                        new MenuItem("Execution Selection", "", ExecuteSelection, shortcut: Key.F8),
                        new MenuItem("Exit and Run In Console", "", ExitAndRun, shortcut: Key.CtrlMask | Key.ShiftMask | Key.F5),
                    }),
                    new MenuBarItem("_Help", new [] {
                        new MenuItem("_About", "", () => MessageBox.Query("About", $"PowerShell Pro Tools Terminal Editor\nVersion: {base.MyInvocation.MyCommand.Module.Version.ToString()}\n", "Ok")),
                        new MenuItem("_Docs", "", () => {
                            var psi = new ProcessStartInfo
                            {
                                FileName = "https://docs.poshtools.com/powershell-pro-tools-documentation/powershell-module/show-pseditor",
                                UseShellExecute = true
                            };
                            Process.Start (psi);
                        })
                    })
                }));

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

            textEditor.Autocomplete.SelectionKey = Key.Tab;

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
            }
        }

        private bool? _canFormat;

        private bool CanFormat()
        {
            if (!_canFormat.HasValue)
            {
                _canFormat = InvokeCommand.InvokeScript("Get-Module PSScriptAnalyzer -ListAvailable").Any();
            }
            return _canFormat.Value;
        }

        private void Format()
        {
            try
            {
                var formatValue = textEditor.Text.ToString();
                if (!System.String.IsNullOrEmpty(formatValue))
                {
                    var previousCursorPosition = textEditor.CursorPosition;
                    var previousTopRow = textEditor.TopRow;
                    using (var powerShell = PowerShell.Create(RunspaceMode.CurrentRunspace))
                    {
                        powerShell.AddCommand("Invoke-Formatter");
                        powerShell.AddParameter("ScriptDefinition", formatValue);
                        var result = powerShell.Invoke();
                        var formatted = result.FirstOrDefault();
                        if (formatted != null)
                        {
                            textEditor.Text = formatted.BaseObject as string;
                            textEditor.CursorPosition = previousCursorPosition;
                            textEditor.TopRow = previousTopRow;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Formatting Failed", ex.Message);
            }
        }
        private bool CanCloseFile()
        {
            if (textEditor.Text == _originalText)
            {
                return true;
            }

            var r = MessageBox.ErrorQuery("Save File",
                $"Do you want save changes in {fileNameStatus.Title}?", "Yes", "No", "Cancel");
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
            List<string> allowedFileTypes = new List<string>();
            allowedFileTypes.Add(".ps1");
            var dialog = new OpenDialog("Open file", "", allowedFileTypes);
            dialog.CanChooseDirectories = false;
            dialog.CanChooseFiles = true;
            dialog.AllowsMultipleSelection = false;
            dialog.DirectoryPath = currentDirectory;

            Application.Run(dialog);

            if (dialog.FilePath.IsEmpty || dialog.Canceled == true)
            {
                return;
            }

            if (!System.IO.Path.HasExtension(dialog.FilePath.ToString()) || !System.IO.Path.GetExtension(dialog.FilePath.ToString()).Equals(".ps1", StringComparison.CurrentCultureIgnoreCase))
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
                _originalText = textEditor.Text.ToByteArray();
                fileNameStatus.Title = System.IO.Path.GetFileName(Path);
            }
        }

        private void ExitAndRun()
        {
            var text = textEditor.Text.ToString();

            try
            {
                Application.Shutdown();
            }
            catch { }

            if (Path != null)
            {
                Save(false);
                using (var powerShell = PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    powerShell.AddScript($". {Path}");
                    powerShell.AddCommand("Out-Default");
                    powerShell.Invoke();
                }
            }
            else
            {
                using (var powerShell = PowerShell.Create(RunspaceMode.CurrentRunspace))
                {
                    powerShell.AddScript(text);
                    powerShell.AddCommand("Out-Default");
                    powerShell.Invoke();
                }
            }
        }

        private void Run()
        {
            StringBuilder output = new StringBuilder();
            if (Path != null)
            {
                Save(false);

                using (var ps = PowerShell.Create())
                {
                    ps.Runspace = _runspace;
                    ps.AddScript($". '{Path}' | Out-String");
                    try
                    {
                        var result = ps.Invoke<string>();

                        if (ps.HadErrors)
                        {
                            foreach (var error in ps.Streams.Error)
                            {
                                output.AppendLine(error.ToString());
                            }
                        }

                        foreach (var r in result)
                        {
                            output.AppendLine(r.ToPlainText());
                        }
                    }
                    catch (Exception ex)
                    {
                        output.AppendLine(ex.ToString());
                    }
                }
            }
            else
            {
                using (var ps = PowerShell.Create())
                {
                    ps.Runspace = _runspace;
                    ps.AddScript(textEditor.Text.ToString());
                    ps.AddCommand("Out-String");
                    try
                    {
                        var result = ps.Invoke<string>(Path);

                        if (ps.HadErrors)
                        {
                            foreach (var error in ps.Streams.Error)
                            {
                                output.AppendLine(error.ToString());
                            }
                        }

                        foreach (var r in result)
                        {
                            output.AppendLine(r.ToPlainText());
                        }
                    }
                    catch (Exception ex)
                    {
                        output.AppendLine(ex.ToString());
                    }
                }
            }

            var dialog = new Dialog();
            var button = new Button("Ok");
            dialog.AddButton(button);

            dialog.Add(new TextView
            {
                Text = output.ToString(),
                Height = Dim.Fill(),
                Width = Dim.Fill()
            });

            Application.Run(dialog);
        }

        private void ExecuteSelection()
        {
            StringBuilder output = new StringBuilder();

            using (var ps = PowerShell.Create())
            {
                ps.Runspace = _runspace;
                ps.AddScript(textEditor.SelectedText.ToString());
                ps.AddCommand("Out-String");
                try
                {
                    var result = ps.Invoke<string>();

                    if (ps.HadErrors)
                    {
                        foreach (var error in ps.Streams.Error)
                        {
                            output.AppendLine(error.ToString());
                        }
                    }

                    foreach (var r in result)
                    {
                        output.AppendLine(r.ToPlainText());
                    }
                }
                catch (Exception ex)
                {
                    output.Append(ex.ToString());
                }
            }

            var dialog = new Dialog();
            var button = new Button("Ok");
            dialog.AddButton(button);

            dialog.Add(new TextView
            {
                Text = output.ToString(),
                Height = Dim.Fill(),
                Width = Dim.Fill()
            });

            Application.Run(dialog);
        }

        private void New()
        {
            if (!CanCloseFile())
            {
                return;
            }
            fileNameStatus.Title = "Unsaved";
            Path = null;
            _originalText = new System.IO.MemoryStream().ToArray();
            textEditor.Text = _originalText;
        }

        private bool Save(bool saveAs)
        {
            if (string.IsNullOrEmpty(Path) || saveAs)
            {
                List<string> allowedFileTypes = new List<string>();
                allowedFileTypes.Add(".ps1");
                var dialog = new SaveDialog(saveAs ? "Save file as" : "Save file", "", allowedFileTypes);
                dialog.DirectoryPath = currentDirectory;
                Application.Run(dialog);

                if (dialog.FilePath.IsEmpty || dialog.Canceled == true || Directory.Exists(dialog.FilePath.ToString()))
                {
                    return false;
                }
                Path = dialog.FilePath.ToString();
                fileNameStatus.Title = dialog.FileName;
            }
            fileNameStatus.Title = fileNameStatus.Title.TrimEnd("*");
            textEditor.modified = false;
            statusBar.SetNeedsDisplay();

            try
            {
                if (!DisableFormatOnSave && CanFormat())
                {
                    Format();
                }
                File.WriteAllText(Path, textEditor.Text.ToString());
                _originalText = textEditor.Text.ToByteArray();
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
            if (textEditor.ColumnErrors.ContainsKey(textEditor.CursorPosition))
            {
                cursorStatus.Title = textEditor.ColumnErrors[textEditor.CursorPosition];
            }
            else
            {
                cursorStatus.Title = string.Empty;
            }
            if (textEditor.modified == true && !fileNameStatus.Title.EndsWith("*") && fileNameStatus.Title != "Unsaved")
            {
                fileNameStatus.Title += "*";
            }
            position.Title = $"Ln {textEditor.CursorPosition.Y + 1}, Col {textEditor.CursorPosition.X + 1}";
            statusBar.SetNeedsDisplay();
        }

        private void Autocomplete()
        {
            var autocomplete = textEditor.Autocomplete as PowerShellAutocomplete;

            autocomplete.Force();
            autocomplete.RenderOverlay(textEditor.CursorPosition);
            textEditor.Redraw(textEditor.Bounds);
        }
    }
}