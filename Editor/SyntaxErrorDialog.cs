using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Data;
using Terminal.Gui;

namespace psedit
{
    internal class SyntaxErrorDialog
    {
        public static void Show(ConcurrentDictionary<Point, string> errors)
        {
            var dataTable = new DataTable();
            dataTable.Columns.Add("Line");
            dataTable.Columns.Add("Column");
            dataTable.Columns.Add("Message");
            
            // sort errors by line / column
            List<Point> sortedList = new List<Point>(errors.Keys);
            sortedList.Sort((x,y) => {
                var ret = x.Y.CompareTo(y.Y);
                if (ret == 0) ret = x.X.CompareTo(y.X);
                return ret;
            });

            foreach (var error in sortedList)
            {
                dataTable.Rows.Add(error.Y, error.X, errors[error]);
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
