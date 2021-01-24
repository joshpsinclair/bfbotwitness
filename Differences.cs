using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.IO;
using System.IO.Compression;
using System.Data;
using System.Xml;
using Serilog;
using System.Text;

namespace Differences {
    public interface IDifferences 
{
    List<Object> Differences();
}

public interface IObjectDifferences
{
    List<Object> Differences(Object a, Object b);
}


class DataTableDifferences : IObjectDifferences {

    private string _primaryKeyColumn;

    public DataTableDifferences(string primaryKeyColumn) {
        _primaryKeyColumn=primaryKeyColumn;
    }

    public virtual List<Object> Differences(Object a, Object b) {
        DataTable ta = (DataTable)a;
        DataTable tb = (DataTable)b;

        List<Object> ret = new List<Object>();

        foreach (DataRow bRow in tb.Rows) {
            string bRowID = bRow[_primaryKeyColumn].ToString();
            foreach (DataRow aRow in ta.Rows) {
                string aRowID = aRow[_primaryKeyColumn].ToString();
                if (bRowID == aRowID) {
                    // check if indiviudal items are different
                    foreach(DataColumn col in tb.Columns) {
                        string bRowValue=bRow[col.ToString()].ToString();
                        string aRowValue=aRow[col.ToString()].ToString();
                        if (bRowValue.Equals(aRowValue) == false) {
                            ret.Add(bRow);
                            goto StartNewComparison;
                        }
                        
                    }

                    goto StartNewComparison;
                }
            }
            // If we didnt jump to StartNewComparison then no Rows in 'a'
            // are the same id as 'b'
            ret.Add(bRow);
            StartNewComparison:
                continue;
        }
        Console.WriteLine(ret.Count());
        return ret;
    }
}
}