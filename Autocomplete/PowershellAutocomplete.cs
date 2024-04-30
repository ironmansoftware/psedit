using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using Terminal.Gui;

namespace psedit
{
    public class PowershellAutocomplete : Autocomplete
    {
        private readonly Runspace _runspace;

        public PowershellAutocomplete(Runspace runspace)
        {
            _runspace = runspace;
        }

        private IEnumerable<string> _suggestions;

        public void Force()
        {
            var host = (EditorTextView)HostControl;
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

        private void TryGenerateSuggestions(int columnOffset = 0)
        {
            var host = (EditorTextView)HostControl;
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
                    var word = GetCurrentWord(columnOffset);
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

        public override void GenerateSuggestions(int columnOffset = 0)
        {
            try
            {
                TryGenerateSuggestions(columnOffset);
            }
            catch { }
        }

        public override bool IsWordChar(Rune rune)
        {
            var c = (char)rune;
            return Char.IsLetterOrDigit(c) || c == '$' || c == '-' || c == ':';
        }

        ///<inheritdoc/>
        protected override string GetCurrentWord(int columnOffset = 0)
        {
            var host = (TextView)HostControl;
            var currentLine = host.GetCurrentLine();
            var cursorPosition = Math.Min(host.CurrentColumn, currentLine.Count);
            return IdxToWord(currentLine, cursorPosition, columnOffset);
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