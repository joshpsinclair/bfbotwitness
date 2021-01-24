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


namespace DataSetCreator {
    interface IDataSetCreator
    {
        DataSet Create();
    }

    interface IDataSetFromPathCreator
    {
        DataSet CreateFromPath(string path);
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
}

/*class FileWitness {
        private readonly Serilog.Core.Logger _logger;
        private readonly string _path;

        private readonly IDifferences _differencesInspector;

        private readonly DataSet _cacheDataSet;


        public FileWitness(string path, IDifferences d, Serilog.Core.Logger logger) {
            _path=path;
            _differencesInspector=d;
            _logger=logger;
        }

        public FileWitness(string path, IDifferences d) {
            _path=path;
            _differencesInspector=d;
            _logger= new LoggerConfiguration()
                            .WriteTo.Console()
                            .CreateLogger();
        }

        public List<Object> WitnessChanges() {
            return _differencesInspector.Differences();
    }
} */