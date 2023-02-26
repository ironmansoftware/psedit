using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using Terminal.Gui;

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

        [Parameter(ParameterSetName = "Path", ValueFromPipeline = true, Position = 0)]
        public string Path { get; set; }

        [Parameter()]
        public SwitchParameter DisableFormatOnSave { get; set; }

        protected override void BeginProcessing()
        {
            _runspace = RunspaceFactory.CreateRunspace();
            _runspace.Open();
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
                textEditor.Text = File.ReadAllText(Path);
                fileNameStatus.Title = System.IO.Path.GetFileName(Path);
            }

            Application.Init();
            top = Application.Top;

            var versionStatus = new StatusItem(Key.Unknown, base.MyInvocation.MyCommand.Module.Version.ToString(), () => { });
            position = new StatusItem(Key.Unknown, "0,0", () => { });
            cursorStatus = new StatusItem(Key.Unknown, "", () => { });

            statusBar = new StatusBar(new StatusItem[] { fileNameStatus, versionStatus, position, cursorStatus });

            top.Add(new MenuBar(new MenuBarItem[] {
                new MenuBarItem ("_File", new MenuItem [] {
                        new MenuItem ("_Open", "", () => {
                            var dialog = new OpenDialog("Open file", "Open file");
                            dialog.CanChooseDirectories = false;
                            dialog.CanChooseFiles = true;
                            dialog.AllowsMultipleSelection = false;
                            dialog.AllowedFileTypes = new [] {".ps1"};

                            Application.Run(dialog);

                            if (dialog.FilePath.IsEmpty)
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
                                textEditor.Text = File.ReadAllText(Path);
                                fileNameStatus.Title = Path;
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
                if (!fileNameStatus.Title.EndsWith("*"))
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
                    var formatted = InvokeCommand.InvokeScript("Invoke-Formatter -ScriptDefinition $args[0]", formatValue).FirstOrDefault();
                    if (formatted != null)
                    {
                        textEditor.Text = formatted.BaseObject as string;
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Formatting Failed", ex.Message);
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

        private void Save(bool saveAs)
        {
            if (string.IsNullOrEmpty(Path) || saveAs)
            {
                var dialog = new SaveDialog("Save file", "Save file");
                dialog.AllowedFileTypes = new string[] { ".ps1" };
                Application.Run(dialog);

                if (dialog.FilePath.IsEmpty || Directory.Exists(dialog.FilePath.ToString())) return;

                Path = dialog.FilePath.ToString();
                fileNameStatus.Title = Path;
            }

            fileNameStatus.Title = fileNameStatus.Title.TrimEnd("*");
            statusBar.SetNeedsDisplay();

            try
            {
                if (!DisableFormatOnSave && CanFormat())
                {
                    Format();
                }
                File.WriteAllText(Path, textEditor.Text.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.ErrorQuery("Failed", "Failed to save. " + ex.Message, "Ok");
            }
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
