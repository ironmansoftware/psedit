using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Terminal.Gui;

namespace psedit
{
    public class MarkdownEditorContext : EditorContext
    {
        private List<ParseResult> parsedTokens;

        public MarkdownEditorContext(int TabWidth)
        {
            _tabWidth = TabWidth;
            CanSyntaxHighlight = true;
        }
        public List<ParseResult> ParseMarkdownToken(string text, List<List<Rune>> Runes)
        {
            List<ParseResult> returnValue = new List<ParseResult>();
            var theme = ThemeService.Instance;

            try
            {
                var lines = text.Split('\n');
                bool inCodeBlock = false;
                
                for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    var line = lines[lineIndex];
                    int lineNumber = lineIndex + 1;
                    
                    // Check for code block delimiters (```)
                    if (Regex.IsMatch(line, @"^\s*```"))
                    {
                        inCodeBlock = !inCodeBlock;
                        returnValue.Add(new ParseResult
                        {
                            LineNumber = lineNumber,
                            StartIndex = 1,
                            EndIndex = line.Length + 1,
                            Color = theme.GetColor("Accent")
                        });
                        continue;
                    }
                    
                    // If we're inside a code block, highlight the entire line
                    if (inCodeBlock)
                    {
                        returnValue.Add(new ParseResult
                        {
                            LineNumber = lineNumber,
                            StartIndex = 1,
                            EndIndex = line.Length + 1,
                            Color = theme.GetColor("Accent")
                        });
                        continue;
                    }
                    
                    // Headers (# ## ### #### ##### ######)
                    var headerMatch = Regex.Match(line, @"^(#{1,6})\s+(.*)$");
                    if (headerMatch.Success)
                    {
                        returnValue.Add(new ParseResult
                        {
                            LineNumber = lineNumber,
                            StartIndex = 1,
                            EndIndex = line.Length + 1,
                            Color = theme.GetColor("Accent")
                        });
                        continue;
                    }
                    
                    // Blockquotes (> at start) - color only the marker, then continue parsing
                    var blockquoteMatch = Regex.Match(line, @"^(>\s*)");
                    if (blockquoteMatch.Success)
                    {
                        returnValue.Add(new ParseResult
                        {
                            LineNumber = lineNumber,
                            StartIndex = 1,
                            EndIndex = blockquoteMatch.Length + 1,
                            Color = theme.GetColor("Warning")
                        });
                        // Don't continue - let other patterns color the rest of the line
                    }
                    
                    // Horizontal rules (--- or *** or ___)
                    if (Regex.IsMatch(line.Trim(), @"^(\*{3,}|-{3,}|_{3,})$"))
                    {
                        returnValue.Add(new ParseResult
                        {
                            LineNumber = lineNumber,
                            StartIndex = 1,
                            EndIndex = line.Length + 1,
                            Color = theme.GetColor("Accent")
                        });
                        continue;
                    }
                    
                    // Lists (- or * or + at start, or numbered lists)
                    var listMatch = Regex.Match(line, @"^\s*([*\-+]|\d+\.)\s+");
                    if (listMatch.Success)
                    {
                        returnValue.Add(new ParseResult
                        {
                            LineNumber = lineNumber,
                            StartIndex = listMatch.Index + 1,
                            EndIndex = listMatch.Index + listMatch.Length + 1,
                            Color = theme.GetColor("Warning")
                        });
                    }
                    
                    // Bold (**text** or __text**)
                    foreach (Match match in Regex.Matches(line, @"(\*\*|__)(.+?)\1"))
                    {
                        returnValue.Add(new ParseResult
                        {
                            LineNumber = lineNumber,
                            StartIndex = match.Index + 1,
                            EndIndex = match.Index + match.Length + 1,
                            Color = theme.GetColor("Info")
                        });
                    }
                    
                    // Italic (*text* or _text_)
                    foreach (Match match in Regex.Matches(line, @"(?<!\*)\*(?!\*)([^*]+)\*(?!\*)|(?<!_)_(?!_)([^_]+)_(?!_)"))
                    {
                        returnValue.Add(new ParseResult
                        {
                            LineNumber = lineNumber,
                            StartIndex = match.Index + 1,
                            EndIndex = match.Index + match.Length + 1,
                            Color = theme.GetColor("Info")
                        });
                    }
                    
                    // Inline code (`text`)
                    foreach (Match match in Regex.Matches(line, @"`([^`]+)`"))
                    {
                        returnValue.Add(new ParseResult
                        {
                            LineNumber = lineNumber,
                            StartIndex = match.Index + 1,
                            EndIndex = match.Index + match.Length + 1,
                            Color = theme.GetColor("Accent")
                        });
                    }
                    
                    // Links ([text](url))
                    foreach (Match match in Regex.Matches(line, @"\[([^\]]+)\]\(([^\)]+)\)"))
                    {
                        // Color the link text [text]
                        var textGroup = match.Groups[1];
                        returnValue.Add(new ParseResult
                        {
                            LineNumber = lineNumber,
                            StartIndex = match.Index + 1, // Start of [
                            EndIndex = textGroup.Index + textGroup.Length + 1 + 1, // End after ]
                            Color = theme.GetColor("Info")
                        });
                        
                        // Color the URL (url)
                        var urlGroup = match.Groups[2];
                        returnValue.Add(new ParseResult
                        {
                            LineNumber = lineNumber,
                            StartIndex = urlGroup.Index - 1 + 1, // Start of ( 
                            EndIndex = match.Index + match.Length + 1, // End after )
                            Color = theme.GetColor("Accent")
                        });
                    }
                }
            }
            catch (Exception)
            {
                // If parsing fails, return empty list (no syntax highlighting)
                return new List<ParseResult>();
            }

            return returnValue;
        }

        public override void ParseText(int height, int topRow, int left, int right, string text, List<List<Rune>> Runes)
        {
            // Quick exit when text is the same and the top row / right col has not changed
            if (_originalText == text && topRow == _lastParseTopRow && right == _lastParseRightColumn)
            {
                return;
            }

            if (_originalText != text)
            {
                // Clear errors before parsing new ones
                Errors.Clear();
                ColumnErrors.Clear();

                parsedTokens = ParseMarkdownToken(text, Runes);
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

                for (int idxCol = left; idxCol < lineRuneCount; idxCol++)
                {
                    var rune = idxCol >= lineRuneCount ? ' ' : line[idxCol];
                    var cols = Rune.ColumnWidth(rune);

                    // Get token, note that we must provide +1 for the end column, as Start will be 1 and End will be 2 for the example: A
                    var colToken = rowTokens.FirstOrDefault(e =>
                     (e.StartIndex == null && e.EndIndex == null) ||
                     (e.StartIndex == null && tokenCol + 1 <= e.EndIndex) ||
                     (tokenCol >= e.StartIndex && e.EndIndex == null) ||
                     (tokenCol >= e.StartIndex && tokenCol + 1 <= e.EndIndex));

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
                                // Handle tab spacing
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
    }
}
