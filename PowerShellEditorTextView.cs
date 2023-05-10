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
        public ConcurrentDictionary<Point, string> ColumnErrors { get; set; } = new ConcurrentDictionary<Point, string>();
        private ParseError[] _errors;

        public bool modified = false;

        public PowerShellEditorTextView(Runspace runspace)
        {
            Autocomplete = new PowerShellAutocomplete(runspace);
            Autocomplete.MaxWidth = 30;
            Autocomplete.MaxHeight = 10;
        }

        public Terminal.Gui.Attribute GetColor(Token token, Color background)
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

            return Terminal.Gui.Attribute.Make(textColor, background);
        }

        public void ColorToken(Token token, ParseError error, int line, int column, bool selection)
        {
            var hasError = error != null;

            var background = Color.Black;
            if (selection)
            {
                background = Color.Blue;
            }
            else if (hasError)
            {
                background = Color.Red;
            }
            
            var attributeColors = GetColor(token, background);
            Driver.SetAttribute(attributeColors);

        }

        public List<List<Rune>> Runes { get; private set; }

        private void ColorNormal()
        {
            // this is default color / background when there is no content
            Driver.SetAttribute(Terminal.Gui.Attribute.Make(Color.Green, Color.Black));
        }
        public override void Redraw(Rect bounds)
        {
            if (IsDirty)
            {
                modified = true;
            }
            var text = Text.ToString();
            Parser.ParseInput(text, out Token[] tokens, out ParseError[] errors);
            Errors.Clear();
            ColumnErrors.Clear();
            _errors = errors;
            Runes = EditorExtensions.StringToRunes(text);
            foreach (var error in _errors) 
            {
                // Verify if error has already been reported
                var foundError = Errors.Where( err => 
                                                error.Extent.StartColumnNumber == err.Value.Extent.StartColumnNumber &&
                                                error.Extent.EndColumnNumber == err.Value.Extent.EndColumnNumber &&
                                                error.Extent.StartLineNumber == err.Value.Extent.StartLineNumber &&
                                                error.Extent.EndLineNumber == err.Value.Extent.EndLineNumber);

                if (!foundError.Any())
                {
                    Errors.TryAdd(new Point(error.Extent.StartColumnNumber, error.Extent.StartLineNumber), error);
                }
            }
            ColorNormal();

            var offB = OffSetBackground();
            int right = Frame.Width + offB.width + RightOffset;
            int bottom = Frame.Height + offB.height + BottomOffset;
            var row = 0;
            for (int idxRow = TopRow; idxRow < Runes.Count; idxRow++)
            {
                if (row > bottom)
                {
                    break;
                }
                var line = EditorExtensions.GetLine(Runes, idxRow);
                int lineRuneCount = line.Count;
                var col = 0;

                // identify token for specific row
                var rowTokens = tokens.Where(m =>
                    // single line token
                    m.Extent.StartLineNumber == (idxRow + 1) ||
                    // multiline token
                    (m.Extent.EndLineNumber >= (idxRow + 1) &&
                        m.Extent.StartLineNumber <= (idxRow + 1)
                    ));
                
                var rowErrors = _errors?.Where(m => 
                    m.Extent.StartLineNumber == (idxRow + 1));

                var tokenCol = 1 + LeftColumn;

                Move(0, row);
                for (int idxCol = LeftColumn; idxCol < lineRuneCount; idxCol++)
                {
                    // identify rows with runes
                    var rune = idxCol >= lineRuneCount ? ' ' : line[idxCol];
                    var cols = Rune.ColumnWidth(rune);

                    // get token, note that we must provide +1 for the end column, as Start will be 1 and End will be 2 for the example: A
                    var colToken = rowTokens.FirstOrDefault(m =>
                        // single line token
                        (((m.Extent.StartColumnNumber <= (tokenCol) &&
                        m.Extent.EndColumnNumber >= (tokenCol + 1) &&
                        m.Extent.StartLineNumber == (idxRow + 1))) ||
                        // multiline token
                        (m.Extent.EndLineNumber >= (idxRow + 1) &&
                            m.Extent.StartLineNumber <= (idxRow + 1) &&
                            m.Extent.StartLineNumber != (idxRow + 1))) &&
                        m.Kind != TokenKind.NewLine
                        );


                    // get any errors
                    var colError = rowErrors?.FirstOrDefault(m =>
                        m.Extent.StartColumnNumber <= (tokenCol) &&
                        m.Extent.EndColumnNumber <= (tokenCol)
                        );

                    if (colError != null)
                    {
                        ColumnErrors.TryAdd(new Point(idxCol, idxRow), colError.Message);
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
                                ColorToken(colToken, colError, row, col, Selecting && PointInSelection(idxCol, idxRow));
                                AddRune(col + i, row, ' ');
                            }
                        }
                        tokenCol++;
                    }
                    else 
                    {
                        ColorToken(colToken, colError, row, col, Selecting && PointInSelection(idxCol, idxRow));
                        AddRune(col, row, rune);
                        tokenCol++;
                    }
                    if (!EditorExtensions.SetCol(ref col, bounds.Right, cols))
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