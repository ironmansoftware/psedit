using System.Management.Automation.Runspaces;

namespace PowerShellToolsPro.Cmdlets.Editor
{
    internal class VariableDialog
    {
        public static void Show(Runspace runspace)
        {
            var dataTable = new System.Data.DataTable();
            dataTable.Columns.Add("Name");
            dataTable.Columns.Add("Value");

            using (var ps = System.Management.Automation.PowerShell.Create())
            {
                ps.Runspace = runspace;
                ps.AddCommand("Get-Variable");
                var variables = ps.Invoke();
                foreach (var variable in variables)
                {
                    var name = variable.Members["Name"].Value?.ToString() ?? "";
                    var value = variable.Members["Value"].Value?.ToString() ?? "";
                    dataTable.Rows.Add(name, value);
                }
            }

            var tableView = new Terminal.Gui.TableView(dataTable);
            tableView.Height = Terminal.Gui.Dim.Fill();
            tableView.Width = Terminal.Gui.Dim.Fill();

            var dialog = new Terminal.Gui.Dialog();
            dialog.Title = "Variables";
            dialog.Add(tableView);

            Terminal.Gui.Application.Run(dialog);
        }
    }
}
