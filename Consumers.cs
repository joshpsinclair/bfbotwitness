using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using Newtonsoft.Json;
using System.Net.Http;
using BFBotWitness;

namespace BFBotWitness
{

    public interface IHttpHost {
        String Protocol();
        String Host();
        String Port();
        String FullyQualifiedHost();
    }

    public interface IHttpEndpoint {
        Uri BuildEndpoint(Dictionary<String, String> p);
    }

    public interface IAccept {
        Boolean Accept(object a);
    }

    public class AcceptAll : IAccept {
        public Boolean Accept (Object a) {
            return true;
        }
    }

    public class HttpHost : IHttpHost {
        private String _protocol;
        private String _host;
        private String _port;

        public HttpHost(String protocol, String host, String port) {
            _protocol=protocol;
            _host=host;
            _port=port;
        }

        public String Protocol() {
            return _protocol;
        }

        public String Host() {
            return _host;
        }

        public String Port() {
            return _port;
        }

        public String FullyQualifiedHost() {
            return _protocol+"://"+_host+":"+_port;
        }
    }

    public class BaseHttpEndpoint : IHttpEndpoint {
        public String Endpoint;

        public BaseHttpEndpoint(String endpoint) {
            Endpoint=endpoint;
        }

        public String FormatEndpointWithParams(Dictionary<String, String> p) {
            String formattedEndpoint = (String)Endpoint.Clone();
            foreach(var d in p) formattedEndpoint = formattedEndpoint.Replace('{'+d.Key+'}', d.Value);
            return formattedEndpoint;
        }

        public virtual Uri BuildEndpoint(Dictionary<String, String> p) {
            return new Uri(FormatEndpointWithParams(p));
        }
    }

    public class HttpHostEndpoint : BaseHttpEndpoint {
        private IHttpHost _httpHost;

        public HttpHostEndpoint(IHttpHost httpHost, String endpoint) : base(endpoint) {
            _httpHost=httpHost;
        }

        public override Uri BuildEndpoint(Dictionary<String, String> p) {
            return new Uri(_httpHost.FullyQualifiedHost()+'/'+base.FormatEndpointWithParams(p));
        }
    }

    /*
    A class that implements the IAccept interface, compares a list of strings
    that is injected on initialization to the BFBetHistoryItem.EventType
    */
    public class EventTypeFilter : IAccept {

        private List<String> _acceptable;
        internal EventTypeFilter(List<String> acceptable) {
            _acceptable = acceptable;
        }
        public Boolean Accept(Object a) {
            BFBetHistoryItem casted = (BFBetHistoryItem)a;
            return _acceptable.Any(s=>casted.EventType.Equals(s));
        }
    }

    // Subscribes to the bethistory.modified events, making backups of the raw compressed file
    public class BFBetHistoryCopier : IBFBotConsumer {

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
    
    
    // Subscribes and handles the bethistory.modified and sessiondata.modified events 
    public class BFBetHistoryHttpWorker : IBFBotConsumer
    {

        private HttpClient _client;
        private SessionData _sessionData;
        private IHttpEndpoint _listResourceEndpoint;
        private IHttpEndpoint _individualResourceEndpoint;
        Serilog.Core.Logger _logger;

        private List<BFBetHistoryItem> _requiresProcessing;
        private IAccept _acceptItem;

        internal BFBetHistoryHttpWorker(HttpClient client,
                                        SessionData sessionData,
                                        Serilog.Core.Logger logger, 
                                        IAccept acceptItem,
                                        IHttpEndpoint listResourceEndpoint,
                                        IHttpEndpoint individualResourceEndpoint) {
            _client=client;
            _sessionData=sessionData;
            _requiresProcessing = new List<BFBetHistoryItem>();
            _logger = logger;
            _acceptItem = acceptItem;
            _listResourceEndpoint = listResourceEndpoint;
            _individualResourceEndpoint = individualResourceEndpoint;
        }

        public virtual void OnCompleted()
        {

        }

        public virtual void OnError(Exception e)
        {

        }

        private Dictionary<string, string> SerializeForPostPayload(BFBetHistoryItem item) {
            string status;
            if (item.Status == "" | item.Status == null) {
                status = "BET_STATUS_OPEN_UNMATCHED";
            } else if (item.Status.ToLower() == "matched")
            {
                status = "BET_STATUS_OPEN_MATCHED";
            } else if (item.Status.ToLower() == "settled") 
            {
                status = "BET_STATUS_SETTLED";
            } else 
            {
                status = "BET_STATUS_UNKNOWN";
            }
            string tipster;
                if (item.Tipster == "" | item.Tipster == null) {
                    tipster = "STANDIN_STRATEGY";
                } else {
                    tipster = item.Tipster;
                }
            var payload = new Dictionary<string, string>
                    {
                        {"bookieID", item.BetID},
                        { "bookieName", "BetFair"},
                        { "stakeAmount", item.Matched.ToString()},
                        { "strategy", tipster},
                        { "odds", item.AveragePrice.ToString()},
                        { "status", status},
                        { "timestamp", item.DatePlaced},
                        {"eventStartTime", item.StartTime},
                        { "selectionName", item.Selection.Split(".")[1].Trim()},
                        { "betType", "BET_TYPE_EXCHANGE_WIN_BACK"},
                    };
            return payload;
        }

        private static HttpRequestMessage CookiedRequest(Tokens t) {
            HttpRequestMessage request = new HttpRequestMessage();
            request.Headers.Add("Cookie",  "csrftoken="+t.CsrfToken+"; "+"sessionid="+t.SessionId);
            return request;
        }

        private async Task HandleRequest(BFBetHistoryItem item)
        {
            int maxTries=3;
            Dictionary<string, string> payload=SerializeForPostPayload(item);
            for (int attemptNumber=1; attemptNumber <= maxTries; attemptNumber++) {
                var content = new FormUrlEncodedContent(payload);
                try {
                    // If it doesnt exist we need to create it
                    HttpRequestMessage existsRequest = CookiedRequest(_sessionData.Tokens);
                    existsRequest.Method=HttpMethod.Get;
                    existsRequest.RequestUri=_individualResourceEndpoint.BuildEndpoint(new Dictionary<String, String> {{"id", item.BetID}});
                    var existsResponse = await _client.SendAsync(existsRequest);
                    
                    // depending on the result of our exists reqest either put or post
                    HttpRequestMessage createUpdateRequest = CookiedRequest(_sessionData.Tokens);
                    HttpResponseMessage createUpdateResponse;
                    
                    _logger.Information("{status}", existsResponse.StatusCode);
                    if (existsResponse.IsSuccessStatusCode == false)
                    {
                        createUpdateRequest.Method=HttpMethod.Post;
                        createUpdateRequest.RequestUri = _listResourceEndpoint.BuildEndpoint(new Dictionary<String, String> {});
                        createUpdateRequest.Content=content;
                        _logger.Information("Creating resource for {payload}", payload);
                    } else {
                        createUpdateRequest.Method=HttpMethod.Put;
                        createUpdateRequest.RequestUri =_individualResourceEndpoint.BuildEndpoint(new Dictionary<String, String> {{"id", item.BetID}});
                        createUpdateRequest.Content=content;
                        _logger.Information("Updating resource for {payload}", payload);
                    }

                    createUpdateResponse = await _client.SendAsync(createUpdateRequest);
                    // Create or update the existing record
                    if (createUpdateResponse.IsSuccessStatusCode) {
                        var contentStream = await createUpdateResponse.Content.ReadAsStreamAsync();
                        using var streamReader = new StreamReader(contentStream);
                        using var jsonReader = new JsonTextReader(streamReader);
                        JsonSerializer serializer = new JsonSerializer();
                        try {
                            Dictionary<object,object> responsePayload 
                                                    = serializer.Deserialize<Dictionary<object, object>>(jsonReader);
                            _logger.Information("Successfully created/updated. {item}, {response}", 
                                                item, 
                                                responsePayload);
                            Console.WriteLine("Success! 1");
                        } catch (JsonReaderException e) {
                            _logger.Warning(e, "Successfully created/updated, however there was an error" +
                            "deserialzing the response {item}", item);
                            Console.WriteLine("Success! 2");
                        }
                    } else {
                        var contentStream = await createUpdateResponse.Content.ReadAsStreamAsync();
                        using var streamReader = new StreamReader(contentStream);
                        string responseString = await streamReader.ReadToEndAsync();
                        _logger.Error("Unable to create/update PlacedBet resource with error: {error}, {item}", 
                                        responseString, item);
                    }

                    // Remove the item once successfully handled
                    _requiresProcessing.Remove(item);
                    break;

                
                // Catch any general exceptions, retry is allowed abandoned if exceeded max tries
                } catch (HttpRequestException httpException) {
                    if (attemptNumber == maxTries) {
                        _logger.Error(httpException, "There was an HTTP Error and the max number of retries has been exceeded, item will be tried again later");
                        _requiresProcessing.Add(item);
                    } else {
                        _logger.Warning("The was an HTTP Error, retrying request");
                    }
                    
                }
            }
        }

        public virtual void OnNext(Object item)
        {
            BFBotEvent e = (BFBotEvent)item;
            if (e.Id == "sessiondata.modified") {
                SessionData sd = (SessionData)e.Payload;
                _sessionData=sd;
                var handler = new HttpClientHandler {UseCookies = false};
                _client = new HttpClient(handler);
                _client.DefaultRequestHeaders.Add("X-CSRFTOKEN", sd.Tokens.CsrfToken);
                _logger.Information("Obtained new SessionData {sd}", sd.Tokens.SessionId);
            } else if (e.Id == "bethistory.modified") {
                BFBetHistoryItem castedItem = (BFBetHistoryItem)e.Payload;
                if (_acceptItem.Accept(castedItem)) {
                    Task.Run(() => HandleRequest(castedItem));
                } else {
                    _logger.Information("BetID: " + castedItem.BetID.ToString() + " was rejected for HandleRequest");
                }

                foreach (BFBetHistoryItem i in _requiresProcessing) {
                    Task.Run(() => HandleRequest(castedItem));
                }
            }
        }
        
    }
}