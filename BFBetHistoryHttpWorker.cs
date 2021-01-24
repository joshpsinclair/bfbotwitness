using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.IO;
using Newtonsoft.Json;
using System.Net.Http;
using DataSetCreator;
using Differences;
using PeriodicBackgroundSubscriptionService;
using BFBetHistoryWitness;

namespace BFBetHistoryWitness
{
    // Subscribes to the BetHistoryItemWitness and sends changes over http
    public class BFBetHistoryHttpWorker : IObserver<Object>
    {

        private HttpClient _client;
        Serilog.Core.Logger _logger;

        private List<BFBetHistoryItem> _requiresProcessing;


        internal BFBetHistoryHttpWorker(Serilog.Core.Logger logger)
        {
            _client = new HttpClient();
            _client.DefaultRequestHeaders.Add("client", "30ccd1c2-7d9a-4b59-a625-db657aa58b84");
            _client.DefaultRequestHeaders.Add("secret", "5650d740-741d-4281-a064-786d3789904a");
            _requiresProcessing = new List<BFBetHistoryItem>();
            _logger = logger;
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
                status = "BET_STATUS_UNMATCHED";
            } else if (item.Status.ToLower() == "matched")
            {
                status = "BET_STATUS_MATCHED";
            } else if (item.Status.ToLower() == "settled") 
            {
                status = "BET_STATUS_SETTLED";
            } else 
            {
                status = "BET_STATUS_UNKOWN";
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

        private async void HandleRequest(BFBetHistoryItem item)
        {
            int maxTries=3;
            Dictionary<string, string> payload=SerializeForPostPayload(item);
            for (int attemptNumber=1; attemptNumber <= maxTries; attemptNumber++) {
                var content = new FormUrlEncodedContent(payload);
                string url = "https://www.over250k.com:9000/api/horses/placedbets/" + item.BetID + "/";
                _logger.Information("{url}", url);
                try {
                    // If it doesnt exist we need to create it
                    var existsResponse = await _client.GetAsync(url);
                    HttpResponseMessage createUpdateResponse;
                    _logger.Information("{status}", existsResponse.StatusCode);
                    if (existsResponse.IsSuccessStatusCode == false)
                    {
                        createUpdateResponse=await _client.PostAsync("https://www.over250k.com:9000/api/horses/placedbets/", content);
                        _logger.Information("Creating resource for {payload}", payload);
                    } else {
                        createUpdateResponse=await _client.PutAsync("https://www.over250k.com:9000/api/horses/placedbets/" + item.BetID + "/", content);
                        _logger.Information("Updating resource for {payload}", payload);
                    }

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

        public virtual async void OnNext(Object item)
        {
            BFBetHistoryItem castedItem = (BFBetHistoryItem)item;
            await Task.Run(() => HandleRequest(castedItem));

            foreach (BFBetHistoryItem i in _requiresProcessing) {
                await Task.Run(() => HandleRequest(i));
            }
        }
    }
}