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
using DataSetCreator;
using Differences;
using PeriodicBackgroundSubscriptionService;


namespace BFBetHistoryWitness
{

class BFBetHistoryDifferences : IDifferences {

    private readonly IDataSetCreator  _dataSetCreator;
    private readonly IObjectDifferences _differencesInspector;
    private DataSet _cache;
    public BFBetHistoryDifferences(IDataSetCreator c, IObjectDifferences d) {
        _dataSetCreator=c;
        _differencesInspector=d;
        _cache=_dataSetCreator.Create();
    }

    public List<Object> Differences() {
        DataSet current = _dataSetCreator.Create();
        List<Object> ret= _differencesInspector.Differences(_cache.Tables[0], current.Tables[0]);
        _cache=current;
        return ret;
    }
}


public class BFBetHistoryItem
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
    private string eventType;

    internal BFBetHistoryItem(string betID,
                            string betType,
                            string tipster,
                            string name,
                            float priceRequested,
                            float averagePrice,
                            string status,
                            float matched,
                            string datePlaced,
                            string selection,
                            string startTime,
                            string eventType)
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
        this.eventType = eventType;
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
    public string EventType { get { return this.eventType;}}
   }

   public class BFBetHistoryCopier : IObserver<Object> {

        private String _filePath { get; set; }
        private String _folderPath { get; set; }

        public BFBetHistoryCopier(String filePath, String folderPath) {
            _filePath=filePath;
            _folderPath=folderPath;
        }
        public virtual void OnCompleted( ){
       }

        public virtual void OnError(Exception e) {
        }

        public static String FormatDateTime(DateTime dt) {
            String y = dt.Date.Year.ToString();
            String m = String.Format("{0:D2}", dt.Date.Month);
            String d = String.Format("{0:D2}", dt.Date.Day);
            return y+m+d;
        }

        public virtual void OnNext(Object o) {
            System.IO.Directory.CreateDirectory(_folderPath);
            String fileName = FormatDateTime(DateTime.Now)+".gz";
            String destFile = System.IO.Path.Combine(_folderPath, fileName);
            System.IO.File.Copy(_filePath, destFile, true);
        }
   }



    public class BFBetHistoryWorker : IObservable<Object>, IObserver<Object>
    {
        private readonly Serilog.Core.Logger _logger;
        private readonly IDifferences _changesWitness;
        private List<IObserver<Object>> _observers;
        private int _delay;

        public BFBetHistoryWorker(IDifferences changesWitness)
        {
            _changesWitness = changesWitness;
            _logger = new LoggerConfiguration()
                                    .WriteTo.Console()
                                    .CreateLogger();
            _observers = new List<IObserver<Object>>();
        }
        
        public BFBetHistoryWorker(IDifferences changesWitness, Serilog.Core.Logger logger)
        {
            _changesWitness = changesWitness;
            _logger = logger;
            _observers = new List<IObserver<Object>>();
        }

        //Allows outside classes to subscribe
        public IDisposable Subscribe(IObserver<Object> observer)
        {
            if (!_observers.Contains(observer))
            {
                _observers.Add(observer);
            }
            return new Unsubscriber<Object>(_observers, observer);
        }

        public virtual void OnCompleted()
        {}

        public virtual void OnError(Exception e)
        {}

        public virtual void OnNext(Object o)
        {
            List<BFBetHistoryItem> items = this.CreateBFBetHistoryItemsFromDifferences();
            this.NotifySubscribersOfBFBetHistoryItems(items);
        }

        public void NotifySubscribersOfBFBetHistoryItems(List<BFBetHistoryItem> items) 
        {
            foreach (BFBetHistoryItem item in items) {
                foreach (IObserver<Object> observer in _observers) {
                    observer.OnNext(item);
                }
            }
        }

        public List<BFBetHistoryItem> CreateBFBetHistoryItemsFromDifferences() {
            // Check for any differences between now and last we checked
            List<BFBetHistoryItem> ret = new List<BFBetHistoryItem>();
            try {
                List<Object> differences = _changesWitness.Differences();
                _logger.Information("Found {n} changes since last execution", differences.Count());
                foreach (DataRow r in differences) {
                    BFBetHistoryItem item = new BFBetHistoryItem(r["BetId"].ToString(),
                                                    r["BetType"].ToString(),
                                                    r["StrategyName"].ToString(),
                                                    r["Name"].ToString(),
                                                    float.Parse(r["PriceRequested"].ToString()),
                                                    float.Parse(r["AvgPrice"].ToString()),
                                                    r["Status"].ToString(),
                                                    float.Parse(r["Matched"].ToString()),
                                                    r["PlacedDate"].ToString(),
                                                    r["SelectionName"].ToString(),
                                                    r["startTime"].ToString(),
                                                    r["EventTypeName"].ToString());
                    ret.Add(item);
                }
            }
            catch (System.Xml.XmlException exception) {
                _logger.Warning(exception.ToString());
            }
            catch (Exception exception) {
                _logger.Error(exception.ToString());
            }
            return ret;
        }
    }
}
