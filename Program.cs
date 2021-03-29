using System;
using System.Collections.Generic;
using System.Windows.Forms;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
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
    public class BFBetHistoryWitnessConfig
    {
	    public List<string> AcceptableEventTypes { get; set; }
	    public String BFBetHistoryPath { get; set; }
	    public String BFBetHistoryItemIdentifier { get; set; }
        public Int16 CompareInterval { get; set; }
        public String LogFilePath { get; set; }
        public String Client { get; set; }
        public String Secret { get; set; }
    }


    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Program was started");

            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json").Build();

            var section = config.GetSection(nameof(BFBetHistoryWitnessConfig));
            BFBetHistoryWitnessConfig witnessConfig = section.Get<BFBetHistoryWitnessConfig>();
            CreateHostBuilder(args, witnessConfig).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args, BFBetHistoryWitnessConfig config) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    var logger = new LoggerConfiguration()
                                    .WriteTo.Console()
                                    .WriteTo.File(config.LogFilePath, rollingInterval: RollingInterval.Day)
                                    .CreateLogger();

                    // Create the necessary classes that know how to determine differences
                    // in the bet history file
                    IDataSetFromPathCreator statelessCreator = new DataSetFromCompressedXMLFilePathCreator();
                    IDataSetCreator statefullCreator = new DataSetFromPathCreator(config.BFBetHistoryPath,
                                                                                statelessCreator);
                    IObjectDifferences datatableDifferences = new DataTableDifferences(config.BFBetHistoryItemIdentifier);
                    IDifferences differencesWitness = new BFBetHistoryDifferences(statefullCreator, datatableDifferences);


                    // A Periodic Background Service
                    PeriodicService backgroundService = new PeriodicService(logger, config.CompareInterval);

                    // A changes worker that subscribes to our periodic service, determinies changes in 
                    // bet history file and notfies subscribers of these changes
                    BFBetHistoryWorker worker = new BFBetHistoryWorker(differencesWitness, logger);
                    IDisposable workerUnsubscriber = backgroundService.Subscribe(worker);

                    // A http worker that subscribes to the changes worker and  updates the database
                    // with these changes, takes an IAccept class to filter BFBetHistoryItems
                    // based on BFBetHistoryItem.EventType
                    HttpClient client = new HttpClient();
                    client.DefaultRequestHeaders.Add("client", config.Client);
                    client.DefaultRequestHeaders.Add("secret", config.Secret);
                    EventTypeFilter filter = new EventTypeFilter(config.AcceptableEventTypes);
                    BFBetHistoryHttpWorker observer = new BFBetHistoryHttpWorker(client, logger, filter);
                    IDisposable unsubscriber = worker.Subscribe(observer);

                    // Attach our periodic service so it runs 
                    services.AddHostedService<PeriodicService>(provider => backgroundService);
                });
    }
}
