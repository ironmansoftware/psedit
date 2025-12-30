using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Terminal.Gui;

namespace psedit
{
    public abstract class EditorContext
    {
        public string _originalText;
        public int _lastParseTopRow;
        public int _lastParseHeight;
        public int _lastParseRightColumn;
        public int _tabWidth;
        public bool CanFormat = false;
        public bool CanAutocomplete = false;
        public bool CanRun = false;
        public bool CanSyntaxHighlight = false;
        public ConcurrentDictionary<Point, string> Errors = new ConcurrentDictionary<Point, string>();
        public ConcurrentDictionary<Point, string> ColumnErrors = new ConcurrentDictionary<Point, string>();
        public Dictionary<Point, Terminal.Gui.Color> pointColorDict = new Dictionary<Point, Terminal.Gui.Color>();
        public abstract void ParseText(int height, int topRow, int left, int right, string text, List<List<Rune>> Runes);
        public virtual string Format(string text)
        {
            throw new NotImplementedException();
        }
        public virtual string Run(string path)
        {
            throw new NotImplementedException();
        }
        public virtual string RunText(string text)
        {
            throw new NotImplementedException();
        }
        public virtual void RunCurrentRunspace(string path)
        {
            throw new NotImplementedException();
        }
        public virtual void RunTextCurrentRunspace(string text)
        {
            throw new NotImplementedException();
        }

        public Color GetColorByPoint(Point point)
        {
            Color returnColor = Color.Green;
            if (pointColorDict.ContainsKey(point))
            {
                returnColor = pointColorDict[point];
            }
            return returnColor;
        }
    }
}