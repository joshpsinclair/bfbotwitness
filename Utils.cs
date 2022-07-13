using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Hosting;
using System.Security.Cryptography;
using System.IO;
using System.IO.Compression;
using System.Data;
using System.Xml;
using System.Text;
using System.Net.Http;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Formatters.Binary;


namespace BFBotWitness {

     interface IDataSetCreator
    {
        DataSet Create();
    }

    interface IDataSetFromPathCreator
    {
        DataSet CreateFromPath(string path);
    }

    public interface IDifferences 
    {
        List<Object> Differences();
    }

    public interface IObjectDifferences
    {
        List<Object> Differences(Object a, Object b);
    }

    class DataSetFromCompressedXMLFilePathCreator : IDataSetFromPathCreator {
        /* Raises:
            System.IO.FileNotFoundException
            System.IO.InvalidDataException
            System.Xml.XmlException 
        */
        public  DataSet CreateFromPath(string path) {
            using (var input = new FileStream(path, FileMode.Open,
                                FileAccess.Read, FileShare.ReadWrite))
                {
                    using (var gzipStream = new GZipStream(input,
                                CompressionMode.Decompress))
                    {
                        using (var memory = new MemoryStream())
                        {
                            gzipStream.CopyTo(memory);
                            memory.Seek(0, SeekOrigin.Begin);
                            XmlDocument xmlDoc = new XmlDocument();
                            DataSet d = new DataSet();
                            d.ReadXml(memory);
                            return d;
                        }
                    }
                }
        }
    }

    class DataSetFromPathCreator : IDataSetCreator {
        /*
        This accepts a path and an IDataSetFromPathCreator and packages the creation
        so the IDataSetCreator is adhered to. IDataSetFromPathCreator was designed to
        be stateless. This class adds the state, allows us to
        inject any IDataSetFromPathCreator and any path string we need. 
        */
        public string path;
        public IDataSetFromPathCreator creator;
        public DataSetFromPathCreator(string path, IDataSetFromPathCreator creator)  {
            this.path=path;
            this.creator=creator;
        }

        public DataSet Create() {
            return this.creator.CreateFromPath(this.path);
        }
    }

    class DataTableDifferences : IObjectDifferences {

        private string _primaryKeyColumn;

        public DataTableDifferences(string primaryKeyColumn) {
            _primaryKeyColumn=primaryKeyColumn;
        }

        public List<Object> Differences(Object a, Object b) {
            DataTable ta = (DataTable)a;
            DataTable tb = (DataTable)b;

            List<Object> ret = new List<Object>();

            // O(N^2).  A better solution would create a dictionary
            // from ta.Rows with the _primaryKeyColumn as the key.
            // This would make it O(N). Alternativly hashes could be 
            // created stored by id and compared
            foreach (DataRow bRow in tb.Rows) {
                string bRowID = bRow[_primaryKeyColumn].ToString();
                foreach (DataRow aRow in ta.Rows) {
                    string aRowID = aRow[_primaryKeyColumn].ToString();
                    if (bRowID == aRowID) {
                        // check if indiviudal items are different.
                        foreach(DataColumn col in tb.Columns) {
                            string bRowValue=bRow[col.ToString()].ToString();
                            string aRowValue=aRow[col.ToString()].ToString();
                            if (bRowValue.Equals(aRowValue) == false) {
                                ret.Add(bRow);
                                goto StartNewComparison;
                            }
                            
                        }

                        goto StartNewComparison;
                    }
                }
                // If we didnt jump to StartNewComparison then no Rows in 'a'
                // are the same id as 'b'
                ret.Add(bRow);
                StartNewComparison:
                    continue;
            }
            return ret;
        }
    }

    public class Credentials {
        public String Username { get; set; }
        public String Password { get; set; }
    }

    public class Tokens {
        public String SessionId { get; set; }
        public String CsrfToken { get; set; }
    }

    public class SessionData
    {
        public Credentials Credentials { get; set; }
        public Tokens Tokens { get; set; }
    }

    class SessionDataUtils {

        public static SessionData AcquireSessionData(String username, String password, IHttpEndpoint loginEndpoint) {
            var handler = new HttpClientHandler(){
                AllowAutoRedirect=false
            };

            HttpClient client = new HttpClient(handler);
            string url=loginEndpoint.BuildEndpoint(new Dictionary<String, String>{}).ToString();
            HttpResponseMessage response_1 = client.GetAsync(url).Result;
            string raw = response_1.Headers.GetValues("Set-Cookie").FirstOrDefault();
            string csrf = raw.Split(";")[0].Split("=")[1];
            
            client.DefaultRequestHeaders.Add("csrftoken", csrf);

            Dictionary<string, string> payload = new Dictionary<string, string>();
            payload.Add("username", username);
            payload.Add("password", password);
            payload.Add("csrfmiddlewaretoken", csrf);
            payload.Add("next", "/upload/");
            var content = new FormUrlEncodedContent(payload);
            HttpResponseMessage response_2 = client.PostAsync(url, content).Result;

            Credentials c = new Credentials();
            Tokens t = new Tokens();
            SessionData sd = new SessionData();
            sd.Credentials=c;
            sd.Tokens=t;
            sd.Credentials.Username=username;
            sd.Credentials.Password=password;
            sd.Tokens.SessionId="";
            sd.Tokens.CsrfToken="";
            if ((int)response_2.StatusCode == 500) {
                return sd;
            }

            foreach (String s in response_2.Headers.GetValues("Set-Cookie")) {
                if (s.Contains("sessionid")) {
                    String sessionid = s.Split(";")[0].Split("=")[1];
                    sd.Tokens.CsrfToken=csrf;
                    sd.Tokens.SessionId=sessionid;
                }
            }
            return sd;
        }
    }
    
    public class Unsubscriber<Object> : IDisposable
    {
        private List<IObserver<Object>> _observers;
        private IObserver<Object> _observer;

        internal Unsubscriber(List<IObserver<Object>> observers,
                                IObserver<Object> observer)
        {
            this._observers = observers;
            this._observer = observer;
        }

        public void Dispose()
        {
            if (_observers.Contains(_observer))
                _observers.Remove(_observer);
        }
    } 

   public static class CloningService
    {
        public static T Clone<T>(this T source)
        {
            // Don't serialize a null object, simply return the default for that object
            if (Object.ReferenceEquals(source, null))
            {
                return default(T);
            }
            
            IFormatter formatter = new BinaryFormatter();
            using (MemoryStream stream = new MemoryStream())
            {
                formatter.Serialize(stream, source);
                stream.Seek(0, SeekOrigin.Begin);
                return (T)formatter.Deserialize(stream);
            }
        }
    }
}








