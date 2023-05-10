using System;
using System.Collections.Generic;
using NStack;

namespace psedit
{
    public static class EditorExtensions
    {
        public static List<Rune> GetLine(List<List<Rune>> lines, int line)
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

        public static bool SetCol(ref int col, int width, int cols)
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

        public static List<Rune> ToRunes(ustring str)
        {
            List<Rune> runes = new List<Rune>();
            foreach (var x in str.ToRunes())
            {
                runes.Add(x);
            }
            return runes;
        }

    }
}