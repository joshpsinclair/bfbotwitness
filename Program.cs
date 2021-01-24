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
using DataSetCreator;
using Differences;
using PeriodicBackgroundSubscriptionService;
using BFBetHistoryWitness;

namespace BFBetHistoryWitness
{
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Program was started");
            CreateHostBuilder(args).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    var logger = new LoggerConfiguration()
                                    .WriteTo.Console()
                                    .CreateLogger();

                    // Create the necessary classes that know how to determine differences
                    // in the bet history file
                    IDataSetFromPathCreator statelessCreator = new DataSetFromCompressedXMLFilePathCreator();
                    IDataSetCreator statefullCreator = new DataSetFromPathCreator(@"/Users/joshsinclair/Desktop/uk_bets_history_old.gz",
                                                                                statelessCreator);
                    IObjectDifferences datatableDifferences = new DataTableDifferences("BetId");
                    IDifferences differencesWitness = new BFBetHistoryDifferences(statefullCreator, datatableDifferences);


                    // A Periodic Background Service
                    PeriodicService backgroundService = new PeriodicService(logger, 2500);

                    // A changes worker that subscribes to our periodic service, determinies changes in 
                    // bet history file and notfies subscribers of these changes
                    BFBetHistoryWorker worker = new BFBetHistoryWorker(differencesWitness, logger);
                    IDisposable workerUnsubscriber = backgroundService.Subscribe(worker);

                    // An http worker that subscribes to the changes worker and  updates the database
                    // of these changes
                    BFBetHistoryHttpWorker observer = new BFBetHistoryHttpWorker(logger);
                    IDisposable unsubscriber = worker.Subscribe(observer);

                    // Attach our periodic service so it runs
                    services.AddHostedService<PeriodicService>(provider => backgroundService);
                });
    }
}
