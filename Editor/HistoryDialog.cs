using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text;
using Terminal.Gui;

namespace psedit
{
    internal class HistoryDialog
    {
        public static void Show(Runspace runspace)
        {
            var listView = new ListView();

            using (var ps = PowerShell.Create())
            {
                ps.Runspace = runspace;
                ps.AddScript("Get-History");
                var errors = ps.Invoke().Select(m => m.ToString());

                listView.SetSource(errors.Select(m => m.ToString()).ToList());
                listView.Height = Dim.Fill();
                listView.Width = Dim.Fill();

                var dialog = new Dialog();
                dialog.Title = "History";
                dialog.Add(listView);

                Application.Run(dialog);
            }

        }
    }
}
