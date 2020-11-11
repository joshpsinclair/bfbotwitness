using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using Newtonsoft.Json;
using System.Net.Http;

namespace BFBotWitness
{

    // Subscribes to the BetHistoryItemWitness and sends changes over http
    public class HTTPBetHistoryObserver : IObserver<BetHistoryItem>
    {

        private HttpClient _client;
        Serilog.Core.Logger _logger;

        internal HTTPBetHistoryObserver(Serilog.Core.Logger logger)
        {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("client", "30ccd1c2-7d9a-4b59-a625-db657aa58b84");
            _client.DefaultRequestHeaders.Add("secret", "5650d740-741d-4281-a064-786d3789904a");
            _logger = logger;
        }

        public virtual void OnCompleted()
        {

        }

        public virtual void OnError(Exception e)
        {

        }

        private async void HandleRequest(BetHistoryItem item)
        {
            string status;
            if (item.Status == "" | item.Status == null) {
                status = "BET_STATUS_UNMATCHED";
            } else if (item.Status.ToLower() == "matched")
            {
                status = "BET_STATUS_MATCHED";
            } else if (item.Status.ToLower() == "settled") 
            {
                status = "BET_STATUS_SETTLED";
            } else 
            {
                _logger.Error("Encountered an unknown status: {status}. {item}", item.Status, item);
                return;
            }

            string tipster;
            if (item.Tipster == "" | item.Tipster == null) {
                tipster = "STANDIN_STRATEGY";
            } else {
                tipster = item.Tipster;
            }
            
            var values = new Dictionary<string, string>
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
            
            var content = new FormUrlEncodedContent(values);
            string url = "https://www.over250k.com:9000/api/horses/placedbets/" + item.BetID + "/";
            _logger.Information("{url}", url);
            // If it doesnt exist we need to create it
            var existsResponse = await _client.GetAsync(url);
            HttpResponseMessage createUpdateResponse;
            _logger.Information("{status}", existsResponse.StatusCode);
            if (existsResponse.IsSuccessStatusCode == false)
            {
                createUpdateResponse=await _client.PostAsync("https://www.over250k.com:9000/api/horses/placedbets/", content);
                _logger.Information("Creating resource for {item}", values);
            } else {
                createUpdateResponse = await _client.PutAsync("https://www.over250k.com:9000/api/horses/placedbets/" + item.BetID + "/", content);
                _logger.Information("Updating resource for {item}", values);
            }

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
                    } catch (JsonReaderException e) {
                        _logger.Warning(e, "Successfully created/updated, however there was an error" +
                        "deserialzing the response {item}", item);
                    }
                } else {
                    var contentStream = await createUpdateResponse.Content.ReadAsStreamAsync();
                    using var streamReader = new StreamReader(contentStream);
                    string responseString = await streamReader.ReadToEndAsync();
                    _logger.Error("Unable to create/update PlacedBet resource with error: {error}, {item}", 
                                    responseString, item);
                }
        }

        public virtual async void OnNext(BetHistoryItem item)
        {
            await Task.Run(() => HandleRequest(item));
        }


    }




    public class Program
    {
        public static void Main(string[] args)
        {
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {

                    var logger = new LoggerConfiguration()
                                    .WriteTo.Console()
                                    .CreateLogger();

                    // Worker watches for changes in the bet history file
                    Worker worker = new Worker(@"/Users/joshsinclair/Desktop/uk_bets_history_old.gz",
                                            @"/Users/joshsinclair/Desktop/uk_bets_history.gz",
                                            logger);

                    // Create an observer and attach it the the worker
                    // to recieve notifications about changes in bet history
                    HTTPBetHistoryObserver observer = new HTTPBetHistoryObserver(logger);
                    IDisposable unsubscriber = worker.Subscribe(observer);
                    services.AddHostedService<Worker>(provider => worker);
                });
    }
}
