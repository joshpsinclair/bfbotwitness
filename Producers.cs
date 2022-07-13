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
using System.Text.Json;
using System.Text.Json.Serialization;


namespace BFBotWitness
{
    // Gets refreshed session data and produces a .modified event
    public class SessionDataWorker : BFBotProducer
    {
        private readonly Credentials _credentials;
        private readonly Serilog.Core.Logger _logger;
        private List<IObserver<Object>> _observers;
        private IHttpEndpoint _endpoint;

        public SessionDataWorker(String id, Credentials credentials, Serilog.Core.Logger logger, IHttpEndpoint endpoint ) : base (id)
        {
            Id=id;
            _credentials = credentials;
            _logger = logger;
            _observers = new List<IObserver<Object>>();
            _endpoint = endpoint;
        }

        public override IDisposable Subscribe(IObserver<Object> observer)
        {
            if (!_observers.Contains(observer))
            {
                _observers.Add(observer);
            }
            return new Unsubscriber<Object>(_observers, observer);
        }

        public override async Task Run(CancellationToken token) {
            if (!token.IsCancellationRequested) {

                String endpointsPath = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "endpoints.json");
                String jsonData = File.ReadAllText(endpointsPath);
                EndpointItem item = JsonSerializer.Deserialize<EndpointItem>(jsonData); 

                SessionData sessionData = SessionDataUtils.AcquireSessionData(_credentials.Username,
                                                                                _credentials.Password,
                                                                                _endpoint);
                if (sessionData.Tokens.CsrfToken != "" & sessionData.Tokens.SessionId != "") {
                    BFBotEvent e = new BFBotEvent(Id+".modified", sessionData);
                    foreach(IObserver<Object> observer in _observers) {
                        observer.OnNext(e);
                    }
                } else {
                    _logger.Warning("Attempt to get refreshed SessionData failed, this likely wont be a problem unless the warning continues");
                }
            }
        }   
    }

    class BFBetHistoryDifferences : IDifferences {

        private readonly IDataSetCreator  _dataSetCreator;
        private readonly IObjectDifferences _differencesInspector;
        private DataSet _cache;
        public BFBetHistoryDifferences(IDataSetCreator c, IObjectDifferences d) {
            _dataSetCreator=c;
            _differencesInspector=d;
            _cache=_dataSetCreator.Create();
        }

        private DataTable EnsureTable(DataSet dataSet, String name) {
            try {
                return dataSet.Tables[0];
            } catch (System.IndexOutOfRangeException exception) {
                return new DataTable(name);
            }
        }

        public List<Object> Differences() {
            DataSet current = _dataSetCreator.Create();
            List<Object> ret= _differencesInspector.Differences(EnsureTable(_cache, "cache"), 
                                                                EnsureTable(current, "current"));
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

    public class BFBetHistoryWorker : BFBotProducer
    {
        private readonly Serilog.Core.Logger _logger;
        private readonly IDifferences _changesWitness;
        private List<IObserver<Object>> _observers;

        public BFBetHistoryWorker(String id, IDifferences changesWitness) : base(id)
        {
            Id=id;
            _changesWitness = changesWitness;
            _logger = new LoggerConfiguration()
                                    .WriteTo.Console()
                                    .CreateLogger();
            _observers = new List<IObserver<Object>>();
        }
        
        public BFBetHistoryWorker(String id, IDifferences changesWitness, Serilog.Core.Logger logger) : base(id)
        {
            Id=id;
            _changesWitness = changesWitness;
            _logger = logger;
            _observers = new List<IObserver<Object>>();
        }

        public override IDisposable Subscribe(IObserver<Object> observer)
        {
            if (!_observers.Contains(observer))
            {
                _observers.Add(observer);
            }
            return new Unsubscriber<Object>(_observers, observer);
        }

        public override async Task Run(CancellationToken token) {
            if (!token.IsCancellationRequested) {
                List<BFBetHistoryItem> items =  CreateBFBetHistoryItemsFromDifferences();
                NotifySubscribersOfBFBetHistoryItems(items);
            }
        }

        public void NotifySubscribersOfBFBetHistoryItems(List<BFBetHistoryItem> items) 
        {
            if (items.Count() == 0) {
                BFBotEvent e = new BFBotEvent(Id+".nochange", null);
                _observers.ForEach(o => o.OnNext(e));
            }
            foreach (BFBetHistoryItem item in items) {
                foreach (IObserver<Object> observer in _observers) {
                    BFBotEvent e = new BFBotEvent(Id+".modified", item);
                    observer.OnNext(e);
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
            catch (System.IndexOutOfRangeException exception) {
                _logger.Warning("BetFile is empty");
            }
            catch (Exception exception) {
                _logger.Error(exception.ToString());
            }
            return ret;
        }
    }
}
