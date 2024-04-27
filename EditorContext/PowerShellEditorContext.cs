using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using Terminal.Gui;
using System.Text;

namespace psedit
{
    public class PowerShellEditorContext : EditorContext
    {
        private List<ParseResult> parsedTokens;
        private List<ErrorParseResult> parsedErrors;
        private Runspace _runspace;
        public PowerShellEditorContext(int TabWidth, Runspace Runspace)
        {
            _tabWidth = TabWidth;
            _runspace = Runspace;

            CanAutocomplete = true;
            CanRun = true;
            CanSyntaxHighlight = true;
            
            // verify that psscriptanalyzer is available for formatting
            using (var powerShell = PowerShell.Create(RunspaceMode.CurrentRunspace))
            {
                powerShell.AddCommand("Get-Module");
                powerShell.AddParameter("Name", "PSScriptAnalyzer");
                powerShell.AddParameter("ListAvailable", true);
                var result = powerShell.Invoke();
                if (result.Any())
                {
                    CanFormat = true;
                }
            }
        }
        public Terminal.Gui.Color GetColor(Token token)
        {
            Color textColor;
            textColor = Color.White;

            if (token != null)
            {
                switch (token.Kind)
                {
                    case TokenKind.If:
                        textColor = Color.White;
                        break;
                    case TokenKind.Else:
                        textColor = Color.White;
                        break;
                    case TokenKind.LCurly:
                        textColor = Color.BrightYellow;
                        break;
                    case TokenKind.RCurly:
                        textColor = Color.BrightYellow;
                        break;
                    case TokenKind.LParen:
                        textColor = Color.White;
                        break;
                    case TokenKind.RParen:
                        textColor = Color.White;
                        break;
                    case TokenKind.Parameter:
                        textColor = Color.White;
                        break;
                    case TokenKind.Identifier:
                        textColor = Color.BrightYellow;
                        break;
                    case TokenKind.Equals:
                        textColor = Color.White;
                        break;
                    case TokenKind.Param:
                        textColor = Color.White;
                        break;
                    case TokenKind.Function:
                        textColor = Color.BrightBlue;
                        break;
                    case TokenKind.StringExpandable:
                    case TokenKind.StringLiteral:
                    case TokenKind.HereStringExpandable:
                    case TokenKind.HereStringLiteral:
                        textColor = Color.Brown;
                        break;
                    case TokenKind.Variable:
                        textColor = Color.Cyan;
                        break;
                    case TokenKind.Comment:
                        textColor = Color.Green;
                        break;
                    case TokenKind.Command:
                    case TokenKind.Generic:
                        if (token.TokenFlags == TokenFlags.CommandName)
                            textColor = Color.BrightYellow;
                        else
                            textColor = Color.Gray;

                        break;
                    default:
                        textColor = Color.White;
                        break;
                }
            }

            return textColor;
        }
        public List<ErrorParseResult> GetErrors(ParseError[] errors)
        {
            List<ErrorParseResult> returnValue = new List<ErrorParseResult>();

            foreach (var error in errors)
            {
                // Verify if error has already been reported
                Errors.TryAdd(new Point(error.Extent.StartColumnNumber, error.Extent.StartLineNumber), error.Message);

                if (error.Extent.StartLineNumber == error.Extent.EndLineNumber)
                {
                    returnValue.Add(new ErrorParseResult
                    {
                        StartIndex = error.Extent.StartColumnNumber,
                        EndIndex = error.Extent.EndColumnNumber,
                        LineNumber = error.Extent.StartLineNumber,
                        ErrorMessage = error.Message
                    });
                }
                else
                {
                    for (int line = error.Extent.StartLineNumber; line <= error.Extent.EndLineNumber; line++)
                    {
                        if (line == error.Extent.StartLineNumber)
                        {
                            returnValue.Add(new ErrorParseResult
                            {
                                StartIndex = error.Extent.StartColumnNumber,
                                LineNumber = error.Extent.StartLineNumber,
                                ErrorMessage = error.Message
                            });
                        }
                        else if (line == error.Extent.EndLineNumber)
                        {
                            returnValue.Add(new ErrorParseResult
                            {
                                EndIndex = error.Extent.EndColumnNumber,
                                LineNumber = error.Extent.EndLineNumber,
                                ErrorMessage = error.Message
                            });
                        }
                        else
                        {
                            returnValue.Add(new ErrorParseResult
                            {
                                LineNumber = line,
                                ErrorMessage = error.Message
                            });
                        }
                    }
                }
            }
            return returnValue;
        }
        public List<ParseResult> ParsePowershellToken(Token[] tokens, string text, List<List<Rune>> Runes)
        {
            List<ParseResult> returnValue = new List<ParseResult>();
            foreach (var token in tokens)
            {
                if (token.Kind == TokenKind.NewLine)
                {
                    continue;
                }

                var color = GetColor(token);

                if (token.Extent.StartLineNumber == token.Extent.EndLineNumber)
                {
                    returnValue.Add(new ParseResult
                    {
                        StartIndex = token.Extent.StartColumnNumber,
                        EndIndex = token.Extent.EndColumnNumber,
                        LineNumber = token.Extent.StartLineNumber,
                        Color = color
                    });
                }
                else
                {
                    for (int line = token.Extent.StartLineNumber; line <= token.Extent.EndLineNumber; line++)
                    {
                        if (line == token.Extent.StartLineNumber)
                        {
                            returnValue.Add(new ParseResult
                            {
                                StartIndex = token.Extent.StartColumnNumber,
                                LineNumber = token.Extent.StartLineNumber,
                                Color = color
                            });
                        }
                        else if (line == token.Extent.EndLineNumber)
                        {
                            returnValue.Add(new ParseResult
                            {
                                EndIndex = token.Extent.EndColumnNumber,
                                LineNumber = token.Extent.EndLineNumber,
                                Color = color
                            });
                        }
                        else
                        {
                            returnValue.Add(new ParseResult
                            {
                                LineNumber = line,
                                Color = color
                            });
                        }
                    }
                }
            }
            return returnValue;
        }
        public override void ParseText(int height, int topRow, int left, int right, string text, List<List<Rune>> Runes)
        {
            // quick exit when text is the same and the top row / right col has not changed
            if (_originalText == text && topRow == _lastParseTopRow && right == _lastParseRightColumn)
            {
                return;
            }

            if (_originalText != text)
            {
                // clear errors before parsing new ones
                Errors.Clear();
                ColumnErrors.Clear();

                Parser.ParseInput(text, out Token[] tokens, out ParseError[] errors);
                parsedTokens = ParsePowershellToken(tokens, text, Runes);
                parsedErrors = GetErrors(errors);
            }

            Dictionary<Point, Terminal.Gui.Color> returnDict = new Dictionary<Point, Terminal.Gui.Color>();
            int bottom = topRow + height;
            _originalText = text;
            _lastParseTopRow = topRow;
            _lastParseRightColumn = right;
            var row = 0;
            for (int idxRow = topRow; idxRow < Runes.Count; idxRow++)
            {
                if (row > bottom)
                {
                    break;
                }
                var line = EditorExtensions.GetLine(Runes, idxRow);
                int lineRuneCount = line.Count;
                var col = 0;
                var tokenCol = 1 + left;
                var rowTokens = parsedTokens.Where(m => m.LineNumber == idxRow + 1);

                var rowErrors = parsedErrors.Where(e => e.LineNumber == idxRow + 1);

                for (int idxCol = left; idxCol < lineRuneCount; idxCol++)
                {
                    var rune = idxCol >= lineRuneCount ? ' ' : line[idxCol];
                    var cols = Rune.ColumnWidth(rune);

                    // get token, note that we must provide +1 for the end column, as Start will be 1 and End will be 2 for the example: A
                    var colToken = rowTokens.FirstOrDefault(e =>
                     (e.StartIndex == null && e.EndIndex == null) ||
                     (e.StartIndex == null && tokenCol + 1 <= e.EndIndex) ||
                     (tokenCol >= e.StartIndex && e.EndIndex == null) ||
                     (tokenCol >= e.StartIndex && tokenCol + 1 <= e.EndIndex));

                    var colError = rowErrors.FirstOrDefault(e =>
                     (e.StartIndex == null && e.EndIndex == null) ||
                     (e.StartIndex == null && tokenCol + 1 <= e.EndIndex) ||
                     (tokenCol >= e.StartIndex && e.EndIndex == null) ||
                     (tokenCol >= e.StartIndex && tokenCol + 1 <= e.EndIndex));

                    if (colError != null)
                    {
                        ColumnErrors.TryAdd(new Point(idxCol, idxRow), colError.ErrorMessage);
                    }

                    if (rune == '\t')
                    {
                        cols += _tabWidth + 1;
                        if (col + cols > right)
                        {
                            cols = right - col;
                        }
                        for (int i = 1; i < cols; i++)
                        {
                            if (col + i < right)
                            {
                                //var color = colToken.Color;
                                //var point = new Point(col + 1, row);
                            }
                        }
                        tokenCol++;
                    }
                    else
                    {
                        var color = Color.White;
                        if (colToken != null)
                        {
                            color = colToken.Color;

                        }
                        var point = new Point(idxCol, row);
                        returnDict.Add(point, color);
                        tokenCol++;
                    }
                    if (!EditorExtensions.SetCol(ref col, right, cols))
                    {
                        break;
                    }
                    if (idxCol + 1 < lineRuneCount && col + Rune.ColumnWidth(line[idxCol + 1]) > right)
                    {
                        break;
                    }
                }
                row++;
            }

            pointColorDict = returnDict;
        }
        public override string Run(string path)
        {
            StringBuilder output = new StringBuilder();
            using (var ps = PowerShell.Create())
            {
                ps.Runspace = _runspace;
                ps.AddScript($". '{path}' | Out-String");
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
            return output.ToString();
        }
        public override string RunText(string text)
        {
            StringBuilder output = new StringBuilder();
            using (var ps = PowerShell.Create())
            {
                ps.Runspace = _runspace;
                ps.AddScript(text);
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
                    output.AppendLine(ex.ToString());
                }
            }
            return output.ToString();
        }
        public override void RunCurrentRunspace(string path)
        {
            using (var powerShell = PowerShell.Create(RunspaceMode.CurrentRunspace))
            {
                powerShell.AddScript($". {path}");
                powerShell.AddCommand("Out-Default");
                powerShell.Invoke();
            }
        }
        public override void RunTextCurrentRunspace(string text)
        {
            using (var powerShell = PowerShell.Create(RunspaceMode.CurrentRunspace))
            {
                powerShell.AddScript(text);
                powerShell.AddCommand("Out-Default");
                powerShell.Invoke();
            }
        }
        public override string Format(string text)
        {
            var returnValue = String.Empty;
            using (var powerShell = PowerShell.Create(RunspaceMode.CurrentRunspace))
            {
                powerShell.AddCommand("Invoke-Formatter");
                powerShell.AddParameter("ScriptDefinition", text);
                var result = powerShell.Invoke();
                var formatted = result.FirstOrDefault();
                if (formatted != null)
                {
                    returnValue = formatted.BaseObject as string;
                }
            }
            return returnValue;
        }
    }
}