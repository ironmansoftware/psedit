using System;
using System.Collections.Generic;
using System.Management.Automation.Runspaces;
using Terminal.Gui;
using System.Collections.Concurrent;

namespace psedit
{

    public class EditorTextView : TextView
    {
        public ConcurrentDictionary<Point, string> Errors { get; set; } = new ConcurrentDictionary<Point, string>();
        public ConcurrentDictionary<Point, string> ColumnErrors { get; set; } = new ConcurrentDictionary<Point, string>();
        public bool modified = false;
        public Runspace _runspace;
        public List<List<Rune>> Runes { get; private set; }
        private EditorContext editorContext;
        public bool CanFormat = false;
        public bool CanRun = false;
        public bool CanSyntaxHighlight = false;
        public LanguageEnum _language = LanguageEnum.Powershell;
        public EditorTextView(Runspace runspace)
        {
            AllowsTab = false;
            _runspace = runspace;
            SetLanguage(LanguageEnum.Powershell);
        }
        public void SetLanguage(LanguageEnum language)
        {
            _language = language;

            // initialize autocomplete for selected language
            if (language == LanguageEnum.Powershell)
            {
                editorContext = new PowerShellEditorContext(TabWidth, _runspace);
                Autocomplete = new PowershellAutocomplete(_runspace);
                Autocomplete.MaxWidth = 30;
                Autocomplete.MaxHeight = 10;
                Autocomplete.HostControl = this;
                Autocomplete.SelectionKey = Key.Enter;
            }
            else if (language == LanguageEnum.JSON)
            {
                editorContext = new JSONEditorContext(TabWidth);
            }
            else 
            {
                editorContext = null;
            }

            // reset formatting
            if (editorContext != null)
            {
                CanFormat = editorContext.CanFormat;
                CanRun = editorContext.CanRun;
                CanSyntaxHighlight = editorContext.CanSyntaxHighlight;
            }
            else 
            {
                CanFormat = false;
                CanRun = false;
                CanSyntaxHighlight = false;
            }
        }
        public void Format()
        {
            Text = editorContext.Format(Text.ToString());
        }
        public string Run(string path, bool exit = false)
        {
            var output = String.Empty;

            if (exit == true)
            {
                editorContext.RunCurrentRunspace(path);
            }
            else 
            {
                output = editorContext.Run(path);
            }

            return output;
        }
        public string RunText(string text, bool exit = false)
        {
            var output = String.Empty;
            
            if (editorContext.CanRun)
            {
                if (exit == true)
                {
                    editorContext.RunTextCurrentRunspace(text);
                }
                else
                {
                    output = editorContext.RunText(text);
                }
            }
            return output;
        }
        private void ColorNormal()
        {
            // this is default color / background when there is no content
            Driver.SetAttribute(Terminal.Gui.Attribute.Make(Color.Green, Color.Black));
        }

        private void ColorSelected()
        {
            // this is default color / background when content is selected
            Driver.SetAttribute(Terminal.Gui.Attribute.Make(Color.Green, Color.Blue));
        }
        public override void Redraw(Rect bounds)
        {
            if (IsDirty)
            {
                modified = true;
            }

            var text = Text.ToString();
            Runes = EditorExtensions.StringToRunes(text);
            ColorNormal();

            var offB = OffSetBackground();
            int right = Frame.Width + offB.width + RightOffset;
            int bottom = Frame.Height + offB.height + BottomOffset;
            var row = 0;

            if (editorContext != null)
            {
                editorContext.ParseText(bounds.Height, TopRow, LeftColumn, LeftColumn + right, Text.ToString(), Runes);
                ColumnErrors = editorContext.ColumnErrors;
                Errors = editorContext.Errors;
            }

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
                for (int idxCol = LeftColumn; idxCol < lineRuneCount; idxCol++)
                {
                    var rune = idxCol >= lineRuneCount ? ' ' : line[idxCol];
                    var cols = Rune.ColumnWidth(rune);
                    if (editorContext != null)
                    {
                        var point = new Point(idxCol, row);
                        var errorPoint = new Point(idxCol, idxRow);
                        var color = editorContext.GetColorByPoint(point);

                        if (Selecting && PointInSelection(idxCol, idxRow))
                        {
                            Driver.SetAttribute(Terminal.Gui.Attribute.Make(color, Color.Blue));
                        }
                        else if (ColumnErrors.ContainsKey(errorPoint))
                        {
                            Driver.SetAttribute(Terminal.Gui.Attribute.Make(color, Color.Red));
                        }
                        else 
                        {
                            Driver.SetAttribute(Terminal.Gui.Attribute.Make(color, Color.Black));
                        }
                    }
                    else if (Selecting && PointInSelection(idxCol, idxRow))
                    {
                        ColorSelected();
                    }
                    else 
                    {
                        ColorNormal();
                    }
                    // add rune with previously set color
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
                                AddRune(col + i, row, ' ');
                            }
                        }
                    }
                    else 
                    {
                        AddRune(col, row, rune);
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
                Move(0, row);

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
            if (editorContext != null)
            {
                if (editorContext.CanAutocomplete)
                {
                    // draw autocomplete
                    Autocomplete.GenerateSuggestions();

                    var renderAt = new Point(
                        CursorPosition.X - LeftColumn,
                        Autocomplete.PopupInsideContainer
                            ? (CursorPosition.Y + 1) - TopRow
                            : 0);

                    Autocomplete.RenderOverlay(renderAt);
                }      
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
    }
}