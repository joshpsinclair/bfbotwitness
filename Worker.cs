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




public class BetHistoryItem
{
    private string betID;
    private string betType;
    private string tipster;
    private string name;
    private float priceRequested;
    private float averagePrice;
    private string status;
    private float matched;
    private string datePlaced;
    private string selection;
    private string startTime;

    internal BetHistoryItem(string betID,
                            string betType,
                            string tipster,
                            string name,
                            float priceRequested,
                            float averagePrice,
                            string status,
                            float matched,
                            string datePlaced,
                            string selection,
                            string startTime)
    {
        this.betID = betID;
        this.betType = betType;
        this.tipster = tipster;
        this.name = name;
        this.priceRequested = priceRequested;
        this.averagePrice = averagePrice;
        this.status = status;
        this.matched = matched;
        this.datePlaced = datePlaced;
        this.selection = selection;
        this.startTime = startTime;
    }

    public string BetID { get { return this.betID; } }
    public string BetType {  get { return this.betType; } }
    public string Tipster {  get { return this.tipster; } }
    public string Name {  get { return this.name; } }
    public float PriceRequested {  get { return this.priceRequested; } }
    public float AveragePrice {  get { return this.averagePrice; } }
    public string Status {  get { return this.status; } }
    public float Matched {  get { return this.matched; } }
    public string DatePlaced {  get { return this.datePlaced; } }
    public string Selection {  get { return this.selection;  } }
    public string StartTime { get { return  this.startTime;}} 
   }





namespace BFBotWitness
{


    internal class Unsubscriber<BetHistoryItem> : IDisposable
    {
        private List<IObserver<BetHistoryItem>> _observers;
        private IObserver<BetHistoryItem> _observer;

        internal Unsubscriber(List<IObserver<BetHistoryItem>> observers,
                                IObserver<BetHistoryItem> observer)
        {
            this._observers = observers;
            this._observer = observer;
        }

        public void Dispose()
        {
            if (_observers.Contains(_observer))
                _observers.Remove(_observer);
        }
    }

    public class Worker : BackgroundService, IObservable<BetHistoryItem>
    {
        private readonly Serilog.Core.Logger _logger;
        private readonly string _cachePath;
        private readonly string _witnessPath;
        private List<IObserver<BetHistoryItem>> _observers;
        private DataSet datasetA;
        




        public Worker(string cachePath, string witnessPath, Serilog.Core.Logger logger)
        {
            _cachePath = cachePath;
            _witnessPath = witnessPath;
            _logger = logger;
            _observers = new List<IObserver<BetHistoryItem>>();
        }






        //Allows outside classes to subscribe
        public IDisposable Subscribe(IObserver<BetHistoryItem> observer)
        {
            if (!_observers.Contains(observer))
            {
                _observers.Add(observer);
            }
            return new Unsubscriber<BetHistoryItem>(_observers, observer);
        }






        // Attempts to create a DataSet from a gzipped xml file.
        private DataSet CreateDataSetFromFile(string f)
        {

            using (var input = new FileStream(f, FileMode.Open,
                              FileAccess.Read, FileShare.ReadWrite))
            {
                using (var gzipStream = new GZipStream(input,
                               CompressionMode.Decompress))
                {
                    using (var memory = new MemoryStream())
                    {
                        gzipStream.CopyTo(memory);
                        memory.Seek(0, SeekOrigin.Begin);
                        XmlDocument xmlDoc = new XmlDocument();
                        DataSet d = new DataSet();
                        d.ReadXml(memory);
                        return d;
                    }
                }
            }
        }


        // Gets the rows that are in b but not in a, or different in b than in a
        private DataTable DataTableDifferences(DataTable a, DataTable b)
        { 
            DataTable ret = b.Clone();

            foreach (DataRow bRow in b.Rows) {
                string bRowID = bRow["BetId"].ToString();
                foreach (DataRow aRow in a.Rows) {
                    string aRowID = aRow["BetId"].ToString();
                    if (bRowID == aRowID) {
                        // check if indiviudal items are different
                        foreach(DataColumn col in b.Columns) {
                            string bRowValue=bRow[col.ToString()].ToString();
                            string aRowValue=aRow[col.ToString()].ToString();
                            if (bRowValue != aRowValue) {
                                _logger.Information("{a} does not equal {b}", aRowValue, bRowValue);
                                ret.ImportRow(bRow);
                                goto StartNewComparison;
                            }
                            
                        }

                        goto StartNewComparison;
                    }
                }
                // If we didnt jump to StartNewComparison then no Rows in 'a'
                // are the same id as 'b'
                ret.ImportRow(bRow);
                StartNewComparison:
                    continue;
            }

            return ret;
        }






         
        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            // Try to load our cache file into memory, if that fails attempt to
            // load the witness file. 
            try
            {
                datasetA = CreateDataSetFromFile(_cachePath);
            } catch (FileNotFoundException e)
            {
                try
                {
                    datasetA = CreateDataSetFromFile(_witnessPath);
                } catch (Exception generalException)
                {
                    _logger.Fatal(generalException, "Unable to set an initial " +
                        "state for the program. Both the cache file and the " +
                        "wintess file were unable to be loaded");
                    throw generalException;
                }
            }
            await base.StartAsync(cancellationToken);
        }








        public override async Task StopAsync(CancellationToken cancellationToken)
        {
            await base.StopAsync(cancellationToken);
        }

        private string DataRowToString(DataRow row, DataColumnCollection col)
        {
            string data = string.Empty;
            StringBuilder sb = new StringBuilder();
            for (int i=0; i < row.ItemArray.Count(); i++)
            {
                sb.Append(col[i].ToString());
                sb.Append(":" + row.ItemArray[i]);
                sb.Append("\n");
            }
            data = sb.ToString();
            return data;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    DataSet datasetB = CreateDataSetFromFile(_witnessPath);
                    DataTable differences = DataTableDifferences(datasetA.Tables[0],
                                                        datasetB.Tables[0]);
                    
                    if (differences.Rows.Count > 0)
                    {
                        foreach (DataRow r in differences.Rows)
                        {
                            
                            //_logger.Information("Difference {difference}", DataRowToString(r, differences.Columns));
                            BetHistoryItem item = new BetHistoryItem(r["BetId"].ToString(),
                                                                    r["BetType"].ToString(),
                                                                    r["Tipster"].ToString(),
                                                                    r["Name"].ToString(),
                                                                    float.Parse(r["PriceRequested"].ToString()),
                                                                    float.Parse(r["AvgPrice"].ToString()),
                                                                    r["Status"].ToString(),
                                                                    float.Parse(r["Matched"].ToString()),
                                                                    r["PlacedDate"].ToString(),
                                                                    r["SelectionName"].ToString(),
                                                                    r["startTime"].ToString());

                            foreach (var observer in _observers)
                                observer.OnNext(item);
                        }
                    }
                    
                    datasetA = datasetB;

                }
                catch (FileNotFoundException e)
                {
                    _logger.Error(e, "File does not exist {path}", _witnessPath);
 
                } catch (XmlException generalException)
                {
                    _logger.Error(generalException, "An unexpected error " +
                        "occured, delaying execution till next tick");
                }

                await Task.Delay(2500, stoppingToken);
            }
        }









        public override void Dispose()
        {
            base.Dispose();
        }
    }
}
