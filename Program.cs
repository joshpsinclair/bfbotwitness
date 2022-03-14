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
        public Int32 RefreshInterval {get; set; }
    }

    public class SessionDataForm : Form {
        public SessionData sessionData { get; set; }
    }

    public class Program
    {
        
        public static void StartButtonClicked(object Sender, EventArgs e) {
            Button button = (Button)Sender;
            String username= new String("");
            String password= new String("");
            SessionDataForm form = (SessionDataForm)button.Parent;
            Label messageLabel = new Label();
            foreach (Control c in form.Controls) {
                if (c is TextBox) {
                    if (c.AccessibleName == "username") {
                        username=c.Text;
                    }
                    else if (c.AccessibleName == "password") {
                        password=c.Text;
                    }
                }
                if (c.AccessibleName == "message") {
                    messageLabel=(Label)c;
                }
            }

            messageLabel.Text="Acquiring session data";
            SessionData sd = SessionDataUtils.AcquireSessionData(username, password);
            if (sd.tokens.sessionid != "") {
                form.sessionData=sd;
                form.Close();
            } else {
                messageLabel.Text="Failed credentials check";
            }
        }
        
        public static void Main(string[] args)
        {
            SessionDataForm form1 = new SessionDataForm();
            Button startButton = new Button();
            Button button2 = new Button();

            Label usernameLabel = new Label();
            Label passwordLabel = new Label();
            Label messageLabel = new Label();

            TextBox username = new TextBox();
            TextBox password = new TextBox();

            username.AccessibleName="username";
            password.AccessibleName="password";

            usernameLabel.Text="Username";
            usernameLabel.Location = new System.Drawing.Point(10, 10);

            username.Text="Username";
            username.Location = new System.Drawing.Point(10, 35);


            passwordLabel.Text="Password";
            passwordLabel.Location = new System.Drawing.Point(10, 80);
            password.PasswordChar = '*';

            password.Text="Password";
            password.Location = new System.Drawing.Point(10, 105);

            startButton.Text="Start";
            startButton.Location = new System.Drawing.Point(10,150);
            startButton.Click += new System.EventHandler(StartButtonClicked);

            messageLabel.Text="";
            messageLabel.Location = new System.Drawing.Point(10, 180);
            messageLabel.Width = 250;
            messageLabel.AccessibleName="message";

            form1.Text="BetFair Bot Witness";

            form1.Controls.Add(usernameLabel);
            form1.Controls.Add(username);
            form1.Controls.Add(passwordLabel);
            form1.Controls.Add(password);
            form1.Controls.Add(startButton);
            form1.Controls.Add(messageLabel);
            form1.ShowDialog();
            StartWitness(args, form1.sessionData);
        }

        public static void StartWitness(string[] args, SessionData sd) {
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json").Build();

            var section = config.GetSection(nameof(BFBetHistoryWitnessConfig));
            BFBetHistoryWitnessConfig witnessConfig = section.Get<BFBetHistoryWitnessConfig>();
            CreateHostBuilder(args, witnessConfig, sd).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args, 
                                                    BFBetHistoryWitnessConfig config,
                                                    SessionData sd) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    string appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) + "\\BFBotWitnessData";
                    string backupPath = appDataPath + "\\Backups";
                    string logPath = appDataPath + "\\log.txt";
                    var logger = new LoggerConfiguration()
                                    .WriteTo.Console()
                                    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                                    .CreateLogger();
                    
                    // Create the necessary classes that know how to determine differences
                    // in the bet history file
                    IDataSetFromPathCreator statelessCreator = new DataSetFromCompressedXMLFilePathCreator();
                    IDataSetCreator statefullCreator = new DataSetFromPathCreator(config.BFBetHistoryPath,
                                                                                statelessCreator);
                    IObjectDifferences datatableDifferences = new DataTableDifferences(config.BFBetHistoryItemIdentifier);
                    IDifferences differencesWitness = new BFBetHistoryDifferences(statefullCreator, datatableDifferences);


                    // A Periodic Background Service
                    P1 backgroundService = new P1(logger, config.CompareInterval);

                    // A Periodic Background Service that refreshes our session data
                    P2 sessionDataGetterService = new P2(logger, config.RefreshInterval);
                    SessionDataGetter sessionDataGetter = new SessionDataGetter(sd.credentials, logger);
                    IDisposable sdgsUnsubscriber = sessionDataGetterService.Subscribe(sessionDataGetter);

                    // A changes worker that subscribes to our periodic service, determinies changes in 
                    // bet history file and notfies subscribers of these changes
                    BFBetHistoryWorker worker = new BFBetHistoryWorker(differencesWitness, logger);
                    IDisposable workerUnsubscriber = backgroundService.Subscribe(worker);

                    // A backup worker that copies the bet history file to a backup location
                    BFBetHistoryCopier copier = new BFBetHistoryCopier(config.BFBetHistoryPath, backupPath);
                    backgroundService.Subscribe(copier);

                    // A http worker that subscribes to the changes worker and  updates the database
                    // with these changes, takes an IAccept class to filter BFBetHistoryItems
                    // based on BFBetHistoryItem.EventType
                    HttpClient client = new HttpClient();
                    client.DefaultRequestHeaders.Add("X-CSRFTOKEN", sd.tokens.csrftoken);
                    client.DefaultRequestHeaders.Add("sessionid", sd.tokens.sessionid);
                    EventTypeFilter filter = new EventTypeFilter(config.AcceptableEventTypes);
                    BFBetHistoryHttpWorker observer = new BFBetHistoryHttpWorker(client, sd, logger, filter);
                    IDisposable unsubscriber = worker.Subscribe(observer);
                    IDisposable refresherUnsubscriber = sessionDataGetter.Subscribe(observer);
                    // Attach our periodic service so it runs 
                    services.AddHostedService<P2>(provider => sessionDataGetterService);
                    services.AddHostedService<P1>(provider => backgroundService);
                });
    }
}
