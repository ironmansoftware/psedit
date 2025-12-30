using System;
using System.Collections.Generic;
using System.Linq;
using Terminal.Gui;
using System.Diagnostics;
using System.IO;
using YamlDotNet.Core;
using YamlDotNet.Core.Events;
using YamlDotNet.Serialization;

namespace psedit
{
    public class YamlEditorContext : EditorContext
    {
        private List<ParseResult> parsedTokens;
        private List<ErrorParseResult> parsedErrors = new List<ErrorParseResult>();
        public YamlEditorContext(int TabWidth)
        {
            _tabWidth = TabWidth;
            CanFormat = true;
            CanSyntaxHighlight = true;
        }
        public Terminal.Gui.Color GetColor(ParsingEvent token)
        {
            var theme = ThemeService.Instance;
            switch (token)
            {
                case StreamStart _:
                case StreamEnd _:
                case DocumentStart _:
                case DocumentEnd _:
                    return theme.GetColor("Warning");
                case Scalar scalar:
                    if (scalar.IsKey)
                    {
                        return theme.GetColor("Info");
                    }
                    return theme.GetColor("String");
                case SequenceStart _:
                    return theme.GetColor("Warning");
                case SequenceEnd _:
                    return theme.GetColor("Warning");
                case MappingStart _:
                case MappingEnd _:
                    return theme.GetColor("Accent");
                case Comment _:
                    return theme.GetColor("Comment");
                default:
                    return theme.GetColor("Foreground");
            }
        }
        public List<ParseResult> ParseYamlToken(string text, List<List<Rune>> Runes)
        {
            List<ParseResult> returnValue = new List<ParseResult>();
            var reader = new StringReader(text);
            var parser = new YamlDotNet.Core.Parser(reader);
            int oldPos = 1;
            int oldLine = 0;
            bool reading = true;
            while (reading)
            {
                try
                {
                    if (!parser.MoveNext())
                    {
                        reading = false;
                        break;
                    }
                    var token = parser.Current;
                    // skip tokens that dont have a position
                    if (token is StreamStart or StreamEnd or DocumentStart or DocumentEnd or MappingStart or MappingEnd or SequenceStart or SequenceEnd)
                    {
                        continue;
                    }
                    var lineNumber = parser.Current != null ? (int)parser.Current.Start.Line : oldLine + 1;
                    if (oldLine != lineNumber)
                    {
                        oldLine = lineNumber;
                        oldPos = 1;
                    }
                    var startIndex = parser.Current != null ? (int)parser.Current.Start.Column : oldPos + 1;
                    var endIndex = parser.Current != null ? (int)parser.Current.End.Column - 1 : oldPos + 1;
                    var color = GetColor(token);
                    var result = new ParseResult { StartIndex = startIndex, EndIndex = endIndex, Color = color, LineNumber = lineNumber };
                    returnValue.Add(result);
                    oldPos = endIndex;
                }
                catch (YamlDotNet.Core.YamlException ex)
                {
                    if (Errors.ContainsKey(new Point((int)ex.Start.Column, (int)ex.Start.Line)))
                    {
                        reading = false;
                        break;
                    }
                    if (oldLine != (int)ex.Start.Line)
                    {
                        oldLine = (int)ex.Start.Line;
                        oldPos = 1;
                    }
                    var startIndex = oldPos;
                    var endIndex = (int)ex.Start.Column;
                    var errorMessage = ex.Message;
                    Errors.TryAdd(new Point((int)ex.Start.Column, (int)ex.Start.Line), errorMessage);
                    parsedErrors.Add(new ErrorParseResult
                    {
                        StartIndex = (int?)startIndex,
                        EndIndex = (int?)endIndex,
                        LineNumber = (int)ex.Start.Line,
                        ErrorMessage = errorMessage
                    });
                    oldPos = (int)endIndex;
                }
            }
            return returnValue;
        }
        public override void ParseText(int height, int topRow, int left, int right, string text, List<List<Rune>> Runes)
        {
            if (_originalText == text && topRow == _lastParseTopRow && right == _lastParseRightColumn && _lastParseHeight == height)
            {
                return;
            }
            if (_originalText != text)
            {
                parsedErrors.Clear();
                Errors.Clear();
                ColumnErrors.Clear();
                parsedTokens = ParseYamlToken(text, Runes);
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
                    var yamlParseMatch = rowTokens.Where(p => (tokenCol >= p.StartIndex && tokenCol <= p.EndIndex)).FirstOrDefault();
                    var color = Color.Green;
                    if (yamlParseMatch != null)
                    {
                        color = yamlParseMatch.Color;
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
                var deserializer = new DeserializerBuilder().Build();
                var serializer = new SerializerBuilder().Build();
                var obj = deserializer.Deserialize<object>(text);
                returnValue = serializer.Serialize(obj);
            }
            catch
            {
                returnValue = text;
            }
            return returnValue;
        }
    }
}
