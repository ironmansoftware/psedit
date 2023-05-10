
using Terminal.Gui;

namespace psedit
{
    public class ParseResult
    {
        public int? StartIndex = null;
        public int? EndIndex = null;
        public int LineNumber;
        public Color Color;
    }
    public class ErrorParseResult
    {
        public int? StartIndex = null;
        public int? EndIndex = null;
        public int LineNumber;
        public string ErrorMessage;
    }
}