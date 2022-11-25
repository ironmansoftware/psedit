using System;
using System.Collections.Generic;
using System.Data;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Management.Automation.Runspaces;
using System.Text;
using Terminal.Gui;

namespace psedit
{
    internal class SyntaxErrorDialog
    {
        public static void Show(ParseError[] errors)
        {
            var dataTable = new DataTable();
            dataTable.Columns.Add("Line");
            dataTable.Columns.Add("Column");
            dataTable.Columns.Add("Message");

            foreach (var error in errors)
            {
                dataTable.Rows.Add(error.Extent.StartLineNumber, error.Extent.EndLineNumber, error.Message);

            }

            var tableView = new TableView(dataTable);
            tableView.Height = Dim.Fill();
            tableView.Width = Dim.Fill();

            var dialog = new Dialog();
            dialog.Title = "Syntax Errors";
            dialog.Add(tableView);

            Application.Run(dialog);

        }
    }
}
