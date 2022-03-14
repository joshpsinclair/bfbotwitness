using System;
using System.Net.Http;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using PeriodicBackgroundSubscriptionService;

namespace BFBetHistoryWitness
{
    
    public class Credentials {
        public String username { get; set; }
        public String password { get; set; }
    }

    public class Tokens {
        public String sessionid { get; set; }
        public String csrftoken { get; set; }
    }

    public class SessionData
    {
        public Credentials credentials { get; set; }
        public Tokens tokens { get; set; }
    }

    class SessionDataUtils {

        public static SessionData AcquireSessionData(String username, String password) {
            var handler = new HttpClientHandler(){
                AllowAutoRedirect=false
            };

            HttpClient client = new HttpClient(handler);
            string url_1="https://www.over250k.com:9000/login/";
            HttpResponseMessage response_1 = client.GetAsync(url_1).Result;
            string raw = response_1.Headers.GetValues("Set-Cookie").FirstOrDefault();
            string csrf = raw.Split(";")[0].Split("=")[1];
            
            client.DefaultRequestHeaders.Add("csrftoken", csrf);

            string url_2="https://www.over250k.com:9000/login/";
            Dictionary<string, string> payload = new Dictionary<string, string>();
            payload.Add("username", username);
            payload.Add("password", password);
            payload.Add("csrfmiddlewaretoken", csrf);
            payload.Add("next", "/upload/");
            var content = new FormUrlEncodedContent(payload);
            HttpResponseMessage response_2 = client.PostAsync(url_2, content).Result;

            Credentials c = new Credentials();
            Tokens t = new Tokens();
            SessionData sd = new SessionData();
            sd.credentials=c;
            sd.tokens=t;
            sd.credentials.username=username;
            sd.credentials.password=password;
            sd.tokens.sessionid="";
            sd.tokens.csrftoken="";
            if ((int)response_2.StatusCode == 500) {
                return sd;
            }

            foreach (String s in response_2.Headers.GetValues("Set-Cookie")) {
                if (s.Contains("sessionid")) {
                    String sessionid = s.Split(";")[0].Split("=")[1];
                    sd.tokens.csrftoken=csrf;
                    sd.tokens.sessionid=sessionid;
                }
            }
            return sd;
        }
    }

    public class SessionDataGetter : IObservable<Object>, IObserver<Object>
    {
        
        private readonly Credentials _credentials;
        private readonly Serilog.Core.Logger _logger;
        private List<IObserver<Object>> _observers;

        public SessionDataGetter(Credentials credentials, Serilog.Core.Logger logger)
        {
            _credentials = credentials;
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

        // This is called by the PeriodicService this class observes at a pre specified interval
        public virtual void OnNext(Object o)
        {
            // acquire a refreshed SessionData
            SessionData sessionData = SessionDataUtils.AcquireSessionData(_credentials.username,
                                                                            _credentials.password);
            if (sessionData.tokens.csrftoken != "" & sessionData.tokens.sessionid != "") {
                foreach(IObserver<Object> observer in _observers) {
                    observer.OnNext(sessionData);
                }
            } else {
                _logger.Warning("Attempt to get refreshed SessionData failed, this likely wont be a problem unless the warning continues");
            }
        }
    }
}