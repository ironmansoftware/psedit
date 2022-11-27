using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using NStack;
using Terminal.Gui;
using System.Collections.Concurrent;

namespace psedit
{
    public class PowerShellEditorTextView : TextView
    {
        public ConcurrentDictionary<Point, ParseError> Errors { get; set; } = new ConcurrentDictionary<Point, ParseError>();
        private ParseError[] _errors;

        public PowerShellEditorTextView(Runspace runspace)
        {
            Autocomplete = new PowerShellAutocomplete(runspace);
            Autocomplete.MaxWidth = 30;
            Autocomplete.MaxHeight = 10;
        }

        public void ColorToken(Token[] tokens, int line, int column, bool selection)
        {
            var token = tokens.FirstOrDefault(m =>
                m.Extent.StartLineNumber <= (line + 1) &&
                m.Extent.EndLineNumber >= (line + 1) &&
                m.Extent.StartColumnNumber <= (column + 1) &&
                m.Extent.EndColumnNumber >= (column + 1));

            var error = _errors?.FirstOrDefault(m => m.Extent.StartLineNumber <= (line + 1) &&
                m.Extent.EndLineNumber >= (line + 1) &&
                m.Extent.StartColumnNumber <= (column + 1) &&
                m.Extent.EndColumnNumber >= (column + 1));

            var hasError = error != null;

            var background = Color.Black;
            if (selection)
            {
                background = Color.Blue;
            }
            else if (hasError)
            {
                // Verify if error has already been reported
                var foundError = Errors.Where( err => 
                                                error.Extent.StartColumnNumber == err.Value.Extent.StartColumnNumber &&
                                                error.Extent.EndColumnNumber == err.Value.Extent.EndColumnNumber &&
                                                error.Extent.StartLineNumber == err.Value.Extent.StartLineNumber &&
                                                error.Extent.EndLineNumber == err.Value.Extent.EndLineNumber);

                if (!foundError.Any())
                {
                    Errors.TryAdd(new Point(column, line), error);
                }

                background = Color.Red;
            }

            if (token != null)
            {
                switch (token.Kind)
                {
                    case TokenKind.StringExpandable:
                    case TokenKind.StringLiteral:
                    case TokenKind.HereStringExpandable:
                    case TokenKind.HereStringLiteral:
                        Driver.SetAttribute(Terminal.Gui.Attribute.Make(Color.Brown, background));
                        break;
                    case TokenKind.Variable:
                        Driver.SetAttribute(Terminal.Gui.Attribute.Make(Color.BrightBlue, background));
                        break;
                    case TokenKind.Comment:
                        Driver.SetAttribute(Terminal.Gui.Attribute.Make(Color.Green, background));
                        break;
                    case TokenKind.Command:
                    case TokenKind.Generic:
                        Driver.SetAttribute(Terminal.Gui.Attribute.Make(Color.Gray, background));
                        break;
                    default:
                        if (selection)
                        {
                            Driver.SetAttribute(Terminal.Gui.Attribute.Make(Color.Black, background));
                            break;
                        }
                        else if (hasError)
                        {
                            Driver.SetAttribute(Terminal.Gui.Attribute.Make(Color.Black, background));
                            break;
                        }

                        ColorNormal();
                        break;
                }
            }
        }

        public List<List<Rune>> Runes { get; private set; }

        public override void Redraw(Rect bounds)
        {
            var text = Text.ToString();
            Parser.ParseInput(text, out Token[] tokens, out ParseError[] errors);
            Errors.Clear();
            _errors = errors;
            Runes = StringToRunes(text);

            ColorNormal();

            var offB = OffSetBackground();
            int right = Frame.Width + offB.width + RightOffset;
            int bottom = Frame.Height + offB.height + BottomOffset;
            var row = 0;
            for (int idxRow = TopRow; idxRow < Runes.Count; idxRow++)
            {
                var line = GetLine(Runes, idxRow);
                int lineRuneCount = line.Count;
                var col = 0;

                Move(0, row);
                for (int idxCol = LeftColumn; idxCol < lineRuneCount; idxCol++)
                {
                    var rune = idxCol >= lineRuneCount ? ' ' : line[idxCol];
                    var cols = Rune.ColumnWidth(rune);
                    if (idxCol < line.Count && Selecting && PointInSelection(idxCol, idxRow))
                    {
                        ColorSelection(line, idxCol);
                    }
                    else if (idxCol == CurrentColumn && idxRow == CurrentRow && !Selecting && !Used
                      && HasFocus && idxCol < lineRuneCount)
                    {
                        ColorUsed(line, idxCol);
                    }
                    else
                    {
                        ColorNormal(line, idxCol);
                    }

                    if (rune == '\t')
                    {
                        cols += TabWidth + 1;
                        if (col + cols > right)
                        {
                            cols = right - col;
                        }
                        for (int i = 0; i < cols; i++)
                        {
                            if (col + i < right)
                            {
                                ColorToken(tokens, row, col + i, Selecting && PointInSelection(idxCol, idxRow));
                                AddRune(col + i, row, ' ');
                            }
                        }
                    }
                    else
                    {
                        ColorToken(tokens, row, col, Selecting && PointInSelection(idxCol, idxRow));
                        AddRune(col, row, rune);
                    }
                    if (!SetCol(ref col, bounds.Right, cols))
                    {
                        break;
                    }
                    if (idxCol + 1 < lineRuneCount && col + Rune.ColumnWidth(line[idxCol + 1]) > right)
                    {
                        break;
                    }
                }
                if (col < right)
                {
                    ColorNormal();
                    ClearRegion(col, row, right, row + 1);
                }
                row++;
            }
            if (row < bottom)
            {
                ColorNormal();
                ClearRegion(bounds.Left, row, right, bottom);
            }

            PositionCursor();

            if (SelectedLength > 0)
                return;

            // draw autocomplete
            Autocomplete.GenerateSuggestions();

            var renderAt = new Point(
                CursorPosition.X - LeftColumn,
                Autocomplete.PopupInsideContainer
                    ? (CursorPosition.Y + 1) - TopRow
                    : 0);

            Autocomplete.RenderOverlay(renderAt);
        }

        public List<Rune> GetLine(List<List<Rune>> lines, int line)
        {
            if (lines.Count > 0)
            {
                if (line < lines.Count)
                {
                    return lines[line];
                }
                else
                {
                    return lines[lines.Count - 1];
                }
            }
            else
            {
                lines.Add(new List<Rune>());
                return lines[0];
            }
        }

        void ClearRegion(int left, int top, int right, int bottom)
        {
            for (int row = top; row < bottom; row++)
            {
                Move(left, row);
                for (int col = left; col < right; col++)
                    AddRune(col, row, ' ');
            }
        }

        bool PointInSelection(int col, int row)
        {
            long start, end;
            GetEncodedRegionBounds(out start, out end);
            var q = ((long)(uint)row << 32) | (uint)col;
            return q >= start && q <= end - 1;
        }

        void GetEncodedRegionBounds(out long start, out long end)
        {
            long selection = ((long)(uint)SelectionStartRow << 32) | (uint)SelectionStartColumn;
            long point = ((long)(uint)CurrentRow << 32) | (uint)CurrentColumn;
            if (selection > point)
            {
                start = point;
                end = selection;
            }
            else
            {
                start = selection;
                end = point;
            }
        }

        (int width, int height) OffSetBackground()
        {
            int w = 0;
            int h = 0;
            if (SuperView?.Frame.Right - Frame.Right < 0)
            {
                w = SuperView.Frame.Right - Frame.Right - 1;
            }
            if (SuperView?.Frame.Bottom - Frame.Bottom < 0)
            {
                h = SuperView.Frame.Bottom - Frame.Bottom - 1;
            }
            return (w, h);
        }

        internal static bool SetCol(ref int col, int width, int cols)
        {
            if (col + cols <= width)
            {
                col += cols;
                return true;
            }

            return false;
        }

        public static List<List<Rune>> StringToRunes(ustring content)
        {
            var lines = new List<List<Rune>>();
            int start = 0, i = 0;
            var hasCR = false;
            // ASCII code 13 = Carriage Return.
            // ASCII code 10 = Line Feed.
            for (; i < content.Length; i++)
            {
                if (content[i] == 13)
                {
                    hasCR = true;
                    continue;
                }
                if (content[i] == 10)
                {
                    if (i - start > 0)
                        lines.Add(ToRunes(content[start, hasCR ? i - 1 : i]));
                    else
                        lines.Add(ToRunes(ustring.Empty));
                    start = i + 1;
                    hasCR = false;
                }
            }
            if (i - start >= 0)
                lines.Add(ToRunes(content[start, null]));
            return lines;
        }

        internal static List<Rune> ToRunes(ustring str)
        {
            List<Rune> runes = new List<Rune>();
            foreach (var x in str.ToRunes())
            {
                runes.Add(x);
            }
            return runes;
        }

    }


    public class PowerShellAutocomplete : Autocomplete
    {
        private readonly Runspace _runspace;

        public PowerShellAutocomplete(Runspace runspace)
        {
            _runspace = runspace;
        }

        private IEnumerable<string> _suggestions;

        public void Force()
        {
            var host = (PowerShellEditorTextView)HostControl;
            var offset = 0;

            for (var lineOffset = 0; lineOffset <= host.CurrentRow; lineOffset++)
            {
                if (lineOffset == host.CurrentRow)
                {
                    offset += host.CurrentColumn;
                }
                else
                {
                    offset += host.Runes[lineOffset].Count + Environment.NewLine.Length;
                }
            }

            using (var powerShell = PowerShell.Create())
            {
                powerShell.Runspace = _runspace;
                var results = CommandCompletion.CompleteInput(host.Text.ToString(), offset, new Hashtable(), powerShell);
                Suggestions = results.CompletionMatches.Select(m => m.CompletionText).ToList().AsReadOnly();
                _suggestions = Suggestions;
            }
        }

        private void TryGenerateSuggestions()
        {
            var host = (PowerShellEditorTextView)HostControl;
            var offset = 0;

            for (var lineOffset = 0; lineOffset <= host.CurrentRow; lineOffset++)
            {
                if (lineOffset == host.CurrentRow)
                {
                    offset += host.CurrentColumn;
                }
                else
                {
                    offset += host.Runes[lineOffset].Count + Environment.NewLine.Length;
                }
            }
            var text = host.Text.ToString();

            if (text.Length == 0 || offset == 0  || host.CurrentColumn == 0)
            {
                ClearSuggestions();
                return;
            }

            var currentChar = text[offset - 1];

            if (currentChar != '-' &&
                currentChar != ':' &&
                currentChar != '.' &&
                currentChar != '.' &&
                currentChar != '\\' &&
                currentChar != '$')
            {
                if (_suggestions != null)
                {
                    var word = GetCurrentWord();
                    if (!System.String.IsNullOrEmpty(word))
                    {
                        Suggestions = _suggestions.Where(m => m.StartsWith(word, StringComparison.OrdinalIgnoreCase)).ToList().AsReadOnly();
                    }
                    else 
                    {
                        ClearSuggestions();
                    }
                }

                return;
            }

            using (var powerShell = PowerShell.Create())
            {
                powerShell.Runspace = _runspace;
                var results = CommandCompletion.CompleteInput(host.Text.ToString(), offset, new Hashtable(), powerShell);
                Suggestions = results.CompletionMatches.Select(m => m.CompletionText).ToList().AsReadOnly();
                _suggestions = Suggestions;
            }
        }

        public override void GenerateSuggestions()
        {
            try
            {
                TryGenerateSuggestions();
            }
            catch { }
        }

        public override bool IsWordChar(Rune rune)
        {
            var c = (char)rune;
            return Char.IsLetterOrDigit(c) || c == '$' || c == '-';
        }

        ///<inheritdoc/>
        protected override string GetCurrentWord()
        {
            var host = (TextView)HostControl;
            var currentLine = host.GetCurrentLine();
            var cursorPosition = Math.Min(host.CurrentColumn, currentLine.Count);
            return IdxToWord(currentLine, cursorPosition);
        }

        /// <inheritdoc/>
        protected override void DeleteTextBackwards()
        {
            ((TextView)HostControl).DeleteCharLeft();
        }

        /// <inheritdoc/>
        protected override void InsertText(string accepted)
        {
            ((TextView)HostControl).InsertText(accepted);
        }
    }
}