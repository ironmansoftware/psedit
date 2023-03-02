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

        [Parameter(ParameterSetName = "Path", ValueFromPipeline = true, Position = 0)]
        public string Path { get; set; }
		private byte [] _originalText = new System.IO.MemoryStream().ToArray();

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
            textEditor.KeyPress += (k) =>
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
            position = new StatusItem(Key.Unknown, "0,0", () => { });
            cursorStatus = new StatusItem(Key.Unknown, "", () => { });

            statusBar = new StatusBar(new StatusItem[] { fileNameStatus, versionStatus, position, cursorStatus });

            top.Add(new MenuBar(new MenuBarItem[] {
                new MenuBarItem ("_File", new MenuItem [] {
                        new MenuItem ("_New", "", New),
                        new MenuItem ("_Open", "", () => {
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
                        }, shortcut: Key.CtrlMask | Key.O),
                        new MenuItem ("_Save", "", () => {
                            Save(false);
                        }, shortcut: Key.CtrlMask | Key.S),
                        new MenuItem ("Save As", "", () => {
                            Save(true);
                        }),
                        new MenuItem ("_Quit", "", () => {
                            try
                            {
                                if (!CanCloseFile ()) 
                                {
                                    return;
                                }
                                Application.RequestStop();
                            }
                            catch {}
                        }, shortcut: Key.CtrlMask | Key.Q)
                    }),
                    new MenuBarItem("_Edit", new []
                    {
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
            if (!CanCloseFile ()) 
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
                var dialog = new SaveDialog(saveAs ? "Save file as" : "Save file","", allowedFileTypes);
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
            position.Title = $"{textEditor.CursorPosition.X},{textEditor.CursorPosition.Y}";
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