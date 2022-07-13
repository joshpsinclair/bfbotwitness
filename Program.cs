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
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using HttpMock;

namespace BFBotWitness
{
    public class BFBotWitnessConfig
    {
	    public List<string> AcceptableEventTypes { get; set; }
	    public String BFBetHistoryPath { get; set; }
	    public String BFBetHistoryItemIdentifier { get; set; }
        public Int16 CompareInterval { get; set; }
        public Int32 RefreshInterval {get; set; }
        public String BFBetHistoryFileName { get; set; }
        public String AppDataDir { get; set; }
        public String LogFileName { get; set; }
        public String BackupsDir { get; set ; }
    }

    public class EndpointItem {
        public String Protocol { get; set; }
        public String Host { get; set ;}
        public String Port { get; set; }
        public String LoginEndpoint { get; set; }
        public String PlacedBetsEndpoint { get; set; }
        public String PlacedBetsResourceEndpoint { get; set; }
    }

    public class SessionDataForm : Form {
        public SessionData SessionData { get; set; }
    }

    public class Program
    {
        
        public static void StartButtonClicked(object sender, EventArgs e) {
            Button button = (Button)sender;
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

            String endpointsPath = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "endpoints.json");
            String jsonData = File.ReadAllText(endpointsPath);
            EndpointItem item = JsonSerializer.Deserialize<EndpointItem>(jsonData);

            HttpHost httpHost = new HttpHost(item.Protocol, item.Host, item.Port);
            HttpHostEndpoint loginEndpoint = new HttpHostEndpoint(httpHost, item.LoginEndpoint);

            messageLabel.Text="Acquiring session data";
            SessionData sd = SessionDataUtils.AcquireSessionData(username, password, loginEndpoint);
            if (sd.Tokens.SessionId != "") {
                form.SessionData=sd;
                form.Close();
            } else {
                messageLabel.Text="Failed credentials check";
            }
        }

        public static void BetHistoryPathButtonClicked(object sender, EventArgs e) {
            Button button = (Button)sender;
            SessionDataForm form = (SessionDataForm)button.Parent;
            using (OpenFileDialog openFileDialog = new OpenFileDialog()) {
                openFileDialog.InitialDirectory = "c:\\";
                openFileDialog.Filter = "Compressed Files: (*.gz) | *.gz";
                openFileDialog.FilterIndex = 2;
                openFileDialog.RestoreDirectory = true;

                if (openFileDialog.ShowDialog() == DialogResult.OK)
                {
                    //Get the path of specified file
                    String filePath = openFileDialog.FileName;
                    foreach (Control c in form.Controls) {
                        if (c is TextBox) {
                            if (c.AccessibleName == "bethistory") {
                                c.Text=filePath;
                            }
                        }
                    }
                }
            }
        }

        public static void BetHistoryTextBoxChanged(object sender, EventArgs e) {
            TextBox textBox = (TextBox)sender;
            SessionDataForm form = (SessionDataForm)textBox.Parent;
            Label label;

            foreach (Control c in form.Controls) {
                if (c.AccessibleName == "pathmessage") {
                    label=(Label)c;
                    if (File.Exists(textBox.Text) == false) {
                        label.Text="This does not appear to be a valid bet history file";
                    } else {
                        label.Text="This file path looks good!";
                    }
                }
            }
        }

        private static SessionDataForm CreateInitialUI(BFBotWitnessConfig witnessConfig) {
            SessionDataForm form1 = new SessionDataForm();
            Button startButton = new Button();
            Button button2 = new Button();
            Button betHistoryFilePathButton = new Button();

            Label usernameLabel = new Label();
            Label passwordLabel = new Label();
            Label messageLabel = new Label();
            Label betHistoryFilePathLabel = new Label();
            Label betHistoryFilePathMessage = new Label();

            TextBox username = new TextBox();
            TextBox password = new TextBox();
            TextBox betHistoryFilePathTextBox = new TextBox();

            Int32 smallVerticalSpacing=25;
            Int32 largeVerticalSpacing=45;
            Int32 y = 10;
            Int32 x = 10;

            username.AccessibleName="username";
            password.AccessibleName="password";

            usernameLabel.Text="Username";
            usernameLabel.Location = new System.Drawing.Point(x, y);
            y+=smallVerticalSpacing;

            username.Text="Username";
            username.Location = new System.Drawing.Point(x, y);
            y+=largeVerticalSpacing;


            passwordLabel.Text="Password";
            passwordLabel.Location = new System.Drawing.Point(x, y);
            password.PasswordChar = '*';
            y+=smallVerticalSpacing;

            password.Text="Password";
            password.Location = new System.Drawing.Point(x, y);
            y+=largeVerticalSpacing;

            String betHistoryLabelText="Bet History Path";
            if (witnessConfig.BFBetHistoryPath == "") {
                betHistoryLabelText+=" (path could not be automatically determined, please manually select file)";
            }
            betHistoryFilePathLabel.Text=betHistoryLabelText;
            betHistoryFilePathLabel.Location = new System.Drawing.Point(x, y);
            betHistoryFilePathLabel.AutoSize=true;
            y+=smallVerticalSpacing;

            betHistoryFilePathTextBox.AccessibleName="bethistory";
            betHistoryFilePathTextBox.Text=witnessConfig.BFBetHistoryPath;
            betHistoryFilePathTextBox.Location = new System.Drawing.Point(x, y);
            betHistoryFilePathTextBox.Size = new System.Drawing.Size(450, 27);
            betHistoryFilePathTextBox.TextChanged += new System.EventHandler(BetHistoryTextBoxChanged);

            int cx = betHistoryFilePathTextBox.Location.X + betHistoryFilePathTextBox.Size.Width;
            betHistoryFilePathButton.Text="Select Path";
            betHistoryFilePathButton.Location = new System.Drawing.Point(cx, y);
            betHistoryFilePathButton.Click += new System.EventHandler(BetHistoryPathButtonClicked);
            y+=smallVerticalSpacing;

            betHistoryFilePathMessage.Location = new System.Drawing.Point(x, y);
            betHistoryFilePathMessage.AccessibleName="pathmessage";
            betHistoryFilePathMessage.AutoSize=true;
            y+=largeVerticalSpacing;

            startButton.Text="Start";
            startButton.Location = new System.Drawing.Point(x,y);
            startButton.Click += new System.EventHandler(StartButtonClicked);
            y+=largeVerticalSpacing;

            messageLabel.Text="";
            messageLabel.Location = new System.Drawing.Point(x, y);
            messageLabel.Width = 250;
            messageLabel.AccessibleName="message";
            y+=smallVerticalSpacing;

            form1.Text="BFBotWitness";

            form1.Controls.Add(usernameLabel);
            form1.Controls.Add(username);
            form1.Controls.Add(passwordLabel);
            form1.Controls.Add(password);
            form1.Controls.Add(betHistoryFilePathLabel);
            form1.Controls.Add(betHistoryFilePathTextBox);
            form1.Controls.Add(betHistoryFilePathButton);
            form1.Controls.Add(betHistoryFilePathMessage);
            form1.Controls.Add(startButton);
            form1.Controls.Add(messageLabel);

            form1.AutoSize=true;
            return form1;
        }

        [STAThread]
        public static void Main(string[] args) {
            // Load Config
            var config = new ConfigurationBuilder()
                .SetBasePath(AppDomain.CurrentDomain.BaseDirectory)
                .AddJsonFile("appsettings.json").Build();
            BFBotWitnessConfig witnessConfig = config.Get<BFBotWitnessConfig>();

            // Create logger
            string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), witnessConfig.AppDataDir);
            string logPath = Path.Combine(appDataPath, "log.txt");
            var logger = new LoggerConfiguration()
                            .WriteTo.Console()
                            .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                            .CreateLogger();

            // If we dont have a bet history file saved try to automatically find it
            if (witnessConfig.BFBetHistoryPath == "") {
                String profilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "Local");
                String searchDir = profilePath;
                IEnumerator<String> e = Directory.EnumerateFileSystemEntries(@searchDir, witnessConfig.BFBetHistoryFileName, SearchOption.AllDirectories).GetEnumerator();
                while (true) {
                    try {
                        bool doesExist = e.MoveNext();
                        if (doesExist) {
                            witnessConfig.BFBetHistoryPath=e.Current;
                        } else {
                            break;
                        }
                    } catch (Exception exception) {
                        // We dont handle any errors, if we cant find the file the user can select it themselves
                        logger.Warning(exception.ToString());
                    }
                }
            }
            
            SessionDataForm form = CreateInitialUI(witnessConfig);
            form.ShowDialog();

            // Update config with changes made in ui
            foreach(Control c in form.Controls) {
                if (c.AccessibleName == "bethistory") {
                    TextBox t = (TextBox)c;
                    witnessConfig.BFBetHistoryPath=t.Text;
                }
            }
           
            // Save the config, Extensions.Configuration doesnt provide an easy means of 
            // saving so we manually do it
            JsonSerializerOptions jsonWriteOptions = new JsonSerializerOptions() {WriteIndented = true};
            String json = JsonSerializer.Serialize(witnessConfig, jsonWriteOptions);
            String appSettingsPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
            File.WriteAllText(appSettingsPath, json);

            // Load Endpoints
            String endpointsPath = Path.Combine(System.AppDomain.CurrentDomain.BaseDirectory, "endpoints.json");
            String jsonData = File.ReadAllText(endpointsPath);
            EndpointItem item = JsonSerializer.Deserialize<EndpointItem>(jsonData);


            // Create and run the event loop
            CreateHostBuilder(args, witnessConfig, form.SessionData, item).Build().Run();
        }

        public static IHostBuilder CreateHostBuilder(string[] args, 
                                                    BFBotWitnessConfig config,
                                                    SessionData sd,
                                                    EndpointItem item) =>
            
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    string appDataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), config.AppDataDir);
                    string logPath = Path.Combine(appDataPath, config.LogFileName);
                    string backupPath = Path.Combine(appDataPath, config.BackupsDir);
                    var logger = new LoggerConfiguration()
                                    .WriteTo.Console()
                                    .WriteTo.File(logPath, rollingInterval: RollingInterval.Day)
                                    .CreateLogger();
                   
                    HttpHost httpHost = new HttpHost(item.Protocol, item.Host, item.Port);
                    IDataSetFromPathCreator statelessCreator = new DataSetFromCompressedXMLFilePathCreator();
                    IDataSetCreator statefullCreator = new DataSetFromPathCreator(config.BFBetHistoryPath,
                                                                                statelessCreator);
                    IObjectDifferences datatableDifferences = new DataTableDifferences(config.BFBetHistoryItemIdentifier);
                    IDifferences differencesFinder = new BFBetHistoryDifferences(statefullCreator, datatableDifferences);

                    // A bethistory changes finder that produces events when the bethistory file is modified
                    BFBetHistoryWorker betHistoryWorker = new BFBetHistoryWorker("bethistory", differencesFinder, logger);
                    BFBotProducerConfig betHistoryConfig = new BFBotProducerConfig(betHistoryWorker.Id, betHistoryWorker, config.CompareInterval);

                    // A session data producer for getting refreshed session ids
                    HttpHostEndpoint loginEndpoint = new HttpHostEndpoint(httpHost, item.LoginEndpoint);
                    SessionDataWorker sessionDataWorker = new SessionDataWorker("sessiondata", sd.Credentials, logger, loginEndpoint);
                    BFBotProducerConfig sessionDataConfig = new BFBotProducerConfig(sessionDataWorker.Id, sessionDataWorker, config.RefreshInterval);

                    // A http worker that consumes bethistory modified events and sends them to a server
                    HttpClient client = new HttpClient();
                    client.DefaultRequestHeaders.Add("X-CSRFTOKEN", sd.Tokens.CsrfToken);
                    client.DefaultRequestHeaders.Add("sessionid", sd.Tokens.SessionId);
                    EventTypeFilter filter = new EventTypeFilter(config.AcceptableEventTypes);

                    HttpHostEndpoint listEndpoint = new HttpHostEndpoint(httpHost, item.PlacedBetsEndpoint);
                    HttpHostEndpoint resourceEndpoint = new HttpHostEndpoint(httpHost, item.PlacedBetsResourceEndpoint);

                    BFBetHistoryHttpWorker httpConsumer = new BFBetHistoryHttpWorker(client, sd, logger, filter, listEndpoint, resourceEndpoint);
                    BFBotConsumerConfig consumerConfig = new BFBotConsumerConfig("httpconsumer", httpConsumer, new List<String> {"bethistory.modified", "sessiondata.modified"});
                    
                    // A worker that consumes bethistory modified events to backup the raw bet history file
                    BFBetHistoryCopier copier = new BFBetHistoryCopier(config.BFBetHistoryPath, backupPath);
                    BFBotConsumerConfig backupConsumerConfig = new BFBotConsumerConfig("backupconsumer", copier, new List<String> {"bethistory.modified"});


                    BFBotWitnessEngine witness = new BFBotWitnessEngine(logger, 10);
                    witness.AttachProducer(betHistoryConfig);
                    witness.AttachProducer(sessionDataConfig);
                    witness.AttachConsumer(consumerConfig);
                    witness.AttachConsumer(backupConsumerConfig);
                    services.AddHostedService<BFBotWitnessEngine>(provider => witness);
                });

        /*
        public static IHostBuilder CreateHostBuilder(string[] args, 
                                                    BFBotWitnessConfig config,
                                                    SessionData sd) {
            
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
                                                    */
    }
}
