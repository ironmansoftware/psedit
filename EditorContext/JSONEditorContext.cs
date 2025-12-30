using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using System.Diagnostics;
using System.IO;
using Newtonsoft.Json;

namespace psedit
{
    public class JSONEditorContext : EditorContext
    {
        private List<ParseResult> parsedTokens;
        private List<ErrorParseResult> parsedErrors = new List<ErrorParseResult>();
        public JSONEditorContext(int TabWidth)
        {
            _tabWidth = TabWidth;
            CanFormat = true;
            CanSyntaxHighlight = true;
        }
        public Terminal.Gui.Color GetColor(JsonToken token)
        {
            Color textColor;
            textColor = Color.White;

            switch (token)
            {
                case JsonToken.StartObject:
                    textColor = Color.BrightYellow;
                    break;
                case JsonToken.EndObject:
                    textColor = Color.BrightYellow;
                    break;
                case JsonToken.StartArray:
                    textColor = Color.BrightYellow;
                    break;
                case JsonToken.EndArray:
                    textColor = Color.BrightYellow;
                    break;
                case JsonToken.PropertyName:
                    textColor = Color.Gray;
                    break;
                case JsonToken.Comment:
                    textColor = Color.Green;
                    break;
                case JsonToken.String:
                    textColor = Color.Brown;
                    break;
                case JsonToken.Integer:
                    textColor = Color.Cyan;
                    break;
                case JsonToken.Float:
                    textColor = Color.Cyan;
                    break;
                case JsonToken.Boolean:
                    textColor = Color.Cyan;
                    break;
                default:
                    textColor = Color.White;
                    break;
            }

            return textColor;
        }
        public List<ParseResult> ParseJsonToken(string text, List<List<Rune>> Runes)
        {
            List<ParseResult> returnValue = new List<ParseResult>();
            Dictionary<Point, Color> returnList = new Dictionary<Point, Color>();

            JsonTextReader reader = new JsonTextReader(new StringReader(text));
            int oldPos = 1;
            int oldLine = 0;
            bool reading = true;
            while (reading)
            {
                try
                {
                    var read = reader.Read();
                    if (read == false)
                    {
                        reading = false;
                    }
                    var lineNumber = reader.LineNumber;
                    if (oldLine != lineNumber)
                    {
                        oldLine = lineNumber;
                        oldPos = 1;
                    }
                    var startIndex = oldPos;
                    var endIndex = reader.LinePosition;
                    var color = GetColor(reader.TokenType);
                    var result = new ParseResult { StartIndex = startIndex, EndIndex = endIndex, Color = color, LineNumber = lineNumber };
                    returnValue.Add(result);
                    oldPos = endIndex;
                }
                catch (JsonReaderException ex)
                {
                    // this is to catch the scenario where newtonsoft json reader will get stuck in a loop in certain error scenarios
                    if (Errors.ContainsKey(new Point((int)ex.LinePosition, (int)ex.LineNumber)))
                    {
                        reading = false;
                        break;
                    }
                    if (oldLine != (int)ex.LineNumber)
                    {
                        oldLine = (int)ex.LineNumber;
                        oldPos = 1;
                    }
                    var startIndex = oldPos;
                    var endIndex = ex.LinePosition;
                    var errorMessage = StringExtensions.ParseNewtonsoftErrorMessage(ex.Message);
                    Errors.TryAdd(new Point((int)ex.LinePosition, (int)ex.LineNumber), errorMessage);

                    parsedErrors.Add(new ErrorParseResult
                    {
                        StartIndex = startIndex,
                        EndIndex = endIndex,
                        LineNumber = ex.LineNumber,
                        ErrorMessage = errorMessage
                    });
                    oldPos = endIndex;
                }
            }
            return returnValue;
        }
        public override void ParseText(int height, int topRow, int left, int right, string text, List<List<Rune>> Runes)
        {
            // quick exit when text is the same and the top row / right col has not changed
            if (_originalText == text && topRow == _lastParseTopRow && right == _lastParseRightColumn && height == _lastParseHeight)
            {
                return;
            }


            if (_originalText != text)
            {
                parsedErrors.Clear();
                Errors.Clear();
                ColumnErrors.Clear();
                parsedTokens = ParseJsonToken(text, Runes);
            }

            Dictionary<Point, Terminal.Gui.Color> returnDict = new Dictionary<Point, Terminal.Gui.Color>();
            int bottom = topRow + height;
            _originalText = text;
            _lastParseTopRow = topRow;
            _lastParseHeight = height;
            _lastParseRightColumn = right;
            long count = 0;
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
                var rowTokens = parsedTokens.Where(p => (p.LineNumber == idxRow + 1));
                var rowErrors = parsedErrors.Where(e => e.LineNumber == idxRow + 1);

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
                    var jsonParseMatch = rowTokens.Where(p => (tokenCol >= p.StartIndex && tokenCol <= p.EndIndex)).FirstOrDefault();
                    var color = Color.Green;
                    if (jsonParseMatch != null)
                    {
                        color = jsonParseMatch.Color;
                    }
                    count++;

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
            var returnValue = String.Empty;
            try
            {
                var parsedJson = JsonConvert.DeserializeObject(text);
                returnValue = JsonConvert.SerializeObject(parsedJson, Formatting.Indented);
            }
            catch
            {
                returnValue = text;
            }

            return returnValue;
        }
    }
}