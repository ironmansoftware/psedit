using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using System.Diagnostics;
using System.IO;
using System.Xml;

namespace psedit
{
    public class XmlEditorContext : EditorContext
    {
        private List<ParseResult> parsedTokens;
        private List<ErrorParseResult> parsedErrors = new List<ErrorParseResult>();
        
        public XmlEditorContext(int TabWidth)
        {
            _tabWidth = TabWidth;
            CanFormat = true;
            CanSyntaxHighlight = true;
        }

        public Terminal.Gui.Color GetColor(XmlNodeType nodeType)
        {
            var theme = ThemeService.Instance;
            switch (nodeType)
            {
                case XmlNodeType.Element:
                case XmlNodeType.EndElement:
                    return theme.GetColor("Accent"); // Tag names
                case XmlNodeType.Attribute:
                    return theme.GetColor("Info"); // Attribute names
                case XmlNodeType.Text:
                case XmlNodeType.CDATA:
                case XmlNodeType.SignificantWhitespace:
                    return theme.GetColor("String"); // Content
                case XmlNodeType.Comment:
                    return theme.GetColor("Comment"); // Comments
                case XmlNodeType.ProcessingInstruction:
                case XmlNodeType.XmlDeclaration:
                    return theme.GetColor("Warning"); // Processing instructions/declarations
                case XmlNodeType.DocumentType:
                    return theme.GetColor("Warning"); // DOCTYPE
                default:
                    return theme.GetColor("Foreground");
            }
        }

        public List<ParseResult> ParseXmlToken(string text, List<List<Rune>> Runes)
        {
            List<ParseResult> resultList = new List<ParseResult>();
            var theme = ThemeService.Instance;
            int lastProcessedPosition = 1;
            int lastProcessedLine = 0;

            try
            {
                var settings = new XmlReaderSettings
                {
                    ConformanceLevel = ConformanceLevel.Fragment,
                    IgnoreWhitespace = false,
                    IgnoreComments = false,
                    IgnoreProcessingInstructions = false,
                    DtdProcessing = DtdProcessing.Ignore
                };

                using (var stringReader = new StringReader(text))
                using (var reader = XmlReader.Create(stringReader, settings))
                {
                    while (reader.Read())
                    {
                        var lineInfo = reader as IXmlLineInfo;
                        if (lineInfo != null && lineInfo.HasLineInfo())
                        {
                            int lineNumber = lineInfo.LineNumber;
                            int linePosition = lineInfo.LinePosition;
                            
                            if (lastProcessedLine != lineNumber)
                            {
                                lastProcessedLine = lineNumber;
                                lastProcessedPosition = 1;
                            }

                            // Handle different node types
                            switch (reader.NodeType)
                            {
                                case XmlNodeType.Element:
                                    // Handle element start tag
                                    // LinePosition points to the first character of the element name (after '<')
                                    var elementStartCol = linePosition;
                                    var elementEndCol = elementStartCol + reader.Name.Length;
                                    
                                    // Add '<' character (one position before element name)
                                    AddParseResult(resultList, lineNumber, elementStartCol - 1, elementStartCol, GetColor(XmlNodeType.Element));
                                    // Add element name
                                    AddParseResult(resultList, lineNumber, elementStartCol, elementEndCol, GetColor(XmlNodeType.Element));
                                    
                                    int lastAttrEnd = elementEndCol;
                                    
                                    // Handle attributes
                                    if (reader.HasAttributes)
                                    {
                                        int attrLine = lineNumber;
                                        
                                        while (reader.MoveToNextAttribute())
                                        {
                                            var attrLineInfo = reader as IXmlLineInfo;
                                            if (attrLineInfo != null && attrLineInfo.HasLineInfo())
                                            {
                                                attrLine = attrLineInfo.LineNumber;
                                                // LinePosition points to first character of attribute name
                                                var attrCol = attrLineInfo.LinePosition;
                                                
                                                // Attribute name
                                                var attrNameEnd = attrCol + reader.Name.Length;
                                                AddParseResult(resultList, attrLine, attrCol, attrNameEnd, GetColor(XmlNodeType.Attribute));
                                                
                                                // Find the actual positions by looking at the source text
                                                int attrLineIndex = attrLine - 1;
                                                if (attrLineIndex >= 0 && attrLineIndex < Runes.Count)
                                                {
                                                    var line = EditorExtensions.GetLine(Runes, attrLineIndex);
                                                    var lineText = new string(line.Select(r => (char)r.Value).ToArray());
                                                    
                                                    // Find '=' after attribute name
                                                    // attrNameEnd is 1-based, lineText is 0-based
                                                    int equalPos = lineText.IndexOf('=', Math.Max(0, attrNameEnd - 1));
                                                    if (equalPos >= 0)
                                                    {
                                                        // Color '=' (equalPos is 0-based, convert to 1-based for ParseResult)
                                                        AddParseResult(resultList, attrLine, equalPos + 1, equalPos + 2, GetColor(XmlNodeType.Element));
                                                        
                                                        // Find opening quote after '='
                                                        int quotePos = equalPos + 1;
                                                        while (quotePos < lineText.Length && lineText[quotePos] != '"' && lineText[quotePos] != '\'')
                                                        {
                                                            quotePos++;
                                                        }
                                                        
                                                        if (quotePos < lineText.Length)
                                                        {
                                                            // Color opening quote (quotePos is 0-based, convert to 1-based)
                                                            AddParseResult(resultList, attrLine, quotePos + 1, quotePos + 2, GetColor(XmlNodeType.Element));
                                                            
                                                            // Color attribute value
                                                            var attrValue = reader.Value;
                                                            if (!string.IsNullOrEmpty(attrValue))
                                                            {
                                                                // Value starts after the opening quote
                                                                int valueStart = quotePos + 1; // 0-based position after quote
                                                                int valueEnd = valueStart + attrValue.Length; // 0-based position of closing quote
                                                                // Convert to 1-based for ParseResult
                                                                AddParseResult(resultList, attrLine, valueStart + 1, valueEnd + 1, theme.GetColor("String"));
                                                                
                                                                // Color closing quote (valueEnd is 0-based position of closing quote)
                                                                AddParseResult(resultList, attrLine, valueEnd + 1, valueEnd + 2, GetColor(XmlNodeType.Element));
                                                                
                                                                lastAttrEnd = valueEnd + 2; // 1-based position after closing quote
                                                            }
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        reader.MoveToElement();
                                    }
                                    
                                    // Handle closing bracket - find it in the text
                                    // For self-closing: "/>"  For regular: ">"
                                    int closingBracketLine = lineNumber;
                                    if (reader.IsEmptyElement)
                                    {
                                        // Self-closing element "/>"
                                        // We need to find where it is - it should be after the last attribute or element name
                                        // Read from source to find the actual position
                                        int lineIndex = closingBracketLine - 1;
                                        if (lineIndex >= 0 && lineIndex < Runes.Count)
                                        {
                                            var line = EditorExtensions.GetLine(Runes, lineIndex);
                                            var lineText = new string(line.Select(r => (char)r.Value).ToArray());
                                            int searchStart = Math.Max(0, lastAttrEnd - 1);
                                            int slashPos = lineText.IndexOf('/', searchStart);
                                            if (slashPos >= 0 && slashPos < lineText.Length - 1)
                                            {
                                                // Color "/>"
                                                AddParseResult(resultList, closingBracketLine, slashPos + 1, slashPos + 3, GetColor(XmlNodeType.Element));
                                            }
                                        }
                                    }
                                    else
                                    {
                                        // Regular closing ">"
                                        int lineIndex = closingBracketLine - 1;
                                        if (lineIndex >= 0 && lineIndex < Runes.Count)
                                        {
                                            var line = EditorExtensions.GetLine(Runes, lineIndex);
                                            var lineText = new string(line.Select(r => (char)r.Value).ToArray());
                                            int searchStart = Math.Max(0, lastAttrEnd - 1);
                                            int bracketPos = lineText.IndexOf('>', searchStart);
                                            if (bracketPos >= 0)
                                            {
                                                // Color ">"
                                                AddParseResult(resultList, closingBracketLine, bracketPos + 1, bracketPos + 2, GetColor(XmlNodeType.Element));
                                            }
                                        }
                                    }
                                    break;

                                case XmlNodeType.EndElement:
                                    // Handle end tag
                                    var endStartCol = linePosition;
                                    var endEndCol = endStartCol + reader.Name.Length;
                                    
                                    // Add '</' characters
                                    AddParseResult(resultList, lineNumber, endStartCol - 2, endStartCol, GetColor(XmlNodeType.EndElement));
                                    // Add element name
                                    AddParseResult(resultList, lineNumber, endStartCol, endEndCol, GetColor(XmlNodeType.EndElement));
                                    
                                    // Add closing '>'
                                    int endLineIndex = lineNumber - 1;
                                    if (endLineIndex >= 0 && endLineIndex < Runes.Count)
                                    {
                                        var line = EditorExtensions.GetLine(Runes, endLineIndex);
                                        var lineText = new string(line.Select(r => (char)r.Value).ToArray());
                                        int searchStart = Math.Max(0, endEndCol - 1);
                                        int bracketPos = lineText.IndexOf('>', searchStart);
                                        if (bracketPos >= 0)
                                        {
                                            AddParseResult(resultList, lineNumber, bracketPos + 1, bracketPos + 2, GetColor(XmlNodeType.EndElement));
                                        }
                                    }
                                    break;

                                case XmlNodeType.Text:
                                case XmlNodeType.CDATA:
                                case XmlNodeType.SignificantWhitespace:
                                    var textValue = reader.Value;
                                    if (!string.IsNullOrWhiteSpace(textValue))
                                    {
                                        // Handle multiline text content
                                        var textLines = textValue.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                                        int currentLine = lineNumber;
                                        int currentPos = linePosition;
                                        
                                        foreach (var line in textLines)
                                        {
                                            if (!string.IsNullOrEmpty(line))
                                            {
                                                var textStart = currentPos;
                                                var textEnd = textStart + line.Length;
                                                AddParseResult(resultList, currentLine, textStart, textEnd, GetColor(reader.NodeType));
                                            }
                                            currentLine++;
                                            currentPos = 1; // Next lines start at column 1
                                        }
                                    }
                                    break;

                                case XmlNodeType.Comment:
                                    var commentValue = reader.Value;
                                    // Handle multiline comments
                                    var commentLines = commentValue.Split(new[] { '\r', '\n' }, StringSplitOptions.None);
                                    int commentLineNum = lineNumber;
                                    int commentPos = linePosition - 4; // First line accounts for "<!--"
                                    
                                    for (int i = 0; i < commentLines.Length; i++)
                                    {
                                        var line = commentLines[i];
                                        var commentStart = commentPos;
                                        var commentEnd = commentStart + line.Length;
                                        
                                        // First line includes "<!--"
                                        if (i == 0)
                                        {
                                            commentEnd += 4;
                                        }
                                        // Last line includes "-->"
                                        if (i == commentLines.Length - 1)
                                        {
                                            commentEnd += 3;
                                        }
                                        
                                        if (commentStart < commentEnd)
                                        {
                                            AddParseResult(resultList, commentLineNum, commentStart, commentEnd, GetColor(XmlNodeType.Comment));
                                        }
                                        
                                        commentLineNum++;
                                        commentPos = 1; // Next lines start at column 1
                                    }
                                    break;

                                case XmlNodeType.XmlDeclaration:
                                case XmlNodeType.ProcessingInstruction:
                                    var piTarget = reader.Name;
                                    var piValue = reader.Value;
                                    var piStart = linePosition - 2; // account for "<?"
                                    var piEnd = piStart + piTarget.Length + piValue.Length + 4; // account for "<?" and "?>"
                                    AddParseResult(resultList, lineNumber, piStart, piEnd, GetColor(reader.NodeType));
                                    break;
                            }
                            
                            lastProcessedPosition = linePosition;
                        }
                    }
                }
            }
            catch (XmlException ex)
            {
                // Handle XML parsing errors
                if (!Errors.ContainsKey(new Point(ex.LinePosition, ex.LineNumber)))
                {
                    var errorMessage = ex.Message;
                    
                    // Remove line and position info from the end of the error message, its already being set
                    var linePattern = System.Text.RegularExpressions.Regex.Match(errorMessage, @"(.+?)\s+Line\s+\d+,\s+position\s+\d+\.$");
                    if (linePattern.Success)
                    {
                        errorMessage = linePattern.Groups[1].Value.TrimEnd();
                    }
                    
                    // Clean up the error message if needed
                    if (errorMessage.Contains("'.'"))
                    {
                        errorMessage = errorMessage.Split(new[] { "'.' " }, StringSplitOptions.None)[0];
                    }
                    
                    Errors.TryAdd(new Point(ex.LinePosition, ex.LineNumber), errorMessage);
                    
                    // Determine error range on the line
                    int errorStartIndex = lastProcessedPosition;
                    int errorEndIndex = ex.LinePosition;
                    
                    // If we haven't advanced on this line, highlight from start of error position
                    if (lastProcessedLine != ex.LineNumber)
                    {
                        errorStartIndex = 1;
                        errorEndIndex = ex.LinePosition;
                    }
                    
                    // If error position is at or before our current position, highlight rest of line
                    if (errorEndIndex <= errorStartIndex || errorEndIndex <= 0)
                    {
                        errorStartIndex = lastProcessedPosition > 0 ? lastProcessedPosition : 1;
                        // Highlight to end of line
                        int lineIndex = ex.LineNumber - 1;
                        if (lineIndex >= 0 && lineIndex < Runes.Count)
                        {
                            var errorLine = EditorExtensions.GetLine(Runes, lineIndex);
                            errorEndIndex = errorLine.Count + 1;
                        }
                        else
                        {
                            errorEndIndex = errorStartIndex + 10; // Default length
                        }
                    }
                    
                    parsedErrors.Add(new ErrorParseResult
                    {
                        StartIndex = errorStartIndex,
                        EndIndex = errorEndIndex,
                        LineNumber = ex.LineNumber,
                        ErrorMessage = errorMessage
                    });
                }
            }
            catch
            {
                // do nothing
            }



            return resultList;
        }

        private void AddParseResult(List<ParseResult> resultList, int lineNumber, int startIndex, int endIndex, Color color)
        {
            if (startIndex < endIndex)
            {
                var result = new ParseResult 
                { 
                    StartIndex = startIndex, 
                    EndIndex = endIndex, 
                    Color = color, 
                    LineNumber = lineNumber 
                };
                resultList.Add(result);
            }
        }

        public override void ParseText(int height, int topRow, int left, int right, string text, List<List<Rune>> Runes)
        {
            if (_originalText == text && topRow == _lastParseTopRow && right == _lastParseRightColumn)
            {
                return;
            }

            if (_originalText != text)
            {
                parsedErrors.Clear();
                Errors.Clear();
                ColumnErrors.Clear();
                parsedTokens = ParseXmlToken(text, Runes);
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
                var col = left;
                var tokenCol = 1 + left;
                var rowErrors = parsedErrors.Where(e => e.LineNumber == idxRow + 1);
                var rowTokens = parsedTokens.Where(p => p.LineNumber == idxRow + 1);
                for (int idxCol = left; idxCol < lineRuneCount; idxCol++)
                {
                    var colError = rowErrors.FirstOrDefault(e =>
                        (e.StartIndex == null && e.EndIndex == null) ||
                        (e.StartIndex == null && tokenCol <= e.EndIndex) ||
                        (tokenCol >= e.StartIndex && e.EndIndex == null) ||
                        (tokenCol >= e.StartIndex && tokenCol <= e.EndIndex));
                    
                    if (colError != null)
                    {
                        ColumnErrors.TryAdd(new Point(idxCol, idxRow), colError.ErrorMessage);
                    }
                    var xmlParseMatch = rowTokens.Where(p => (tokenCol >= p.StartIndex && tokenCol < p.EndIndex)).FirstOrDefault();
                    var color = Color.Green;
                    if (xmlParseMatch != null)
                    {
                        color = xmlParseMatch.Color;
                    }
                    var rune = idxCol >= lineRuneCount ? ' ' : line[idxCol];
                    var cols = Rune.ColumnWidth(rune);
                    var point = new Point(idxCol, row);
                    returnDict.Add(point, color);
                    tokenCol++;
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

        public override string Format(string text)
        {
            try
            {
                // Trim BOM and whitespace that might cause "Data at the root level is invalid"
                text = text.Trim('\uFEFF', '\u200B', ' ', '\t', '\r', '\n');
                
                // If still empty or whitespace, return original
                if (string.IsNullOrWhiteSpace(text))
                {
                    return text;
                }
                
                var doc = new XmlDocument();
                doc.LoadXml(text);

                var settings = new XmlWriterSettings
                {
                    Indent = true,
                    IndentChars = new string(' ', 2),
                    NewLineChars = "\n",
                    NewLineHandling = NewLineHandling.Replace,
                    OmitXmlDeclaration = !text.TrimStart().StartsWith("<?xml"),
                    Encoding = System.Text.Encoding.UTF8
                };

                using (var stringWriter = new StringWriter())
                {
                    using (var xmlWriter = XmlWriter.Create(stringWriter, settings))
                    {
                        doc.Save(xmlWriter);
                        xmlWriter.Flush();
                    }
                    var result = stringWriter.ToString();
                    
                    // Ensure we return something, even if empty
                    if (string.IsNullOrEmpty(result))
                    {
                        return text;
                    }
                    return result;
                }
            }
            catch
            {
                return text;
            }
        }
    }
}
