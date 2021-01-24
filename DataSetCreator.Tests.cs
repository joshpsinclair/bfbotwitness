using System;
using System.Linq;
using System.Xml.Linq;
using NUnit;
using NUnit.Framework;
using DataSetCreator;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Data;

namespace DataSetCreator
{
    [TestFixture]
    public class DataSetCreator_DataSetFromCompressedXMLFilePathCreator
    {
        public XDocument CreateXMLDoc() {
            XDocument doc = new XDocument( new XElement( "body", 
                                           new XElement( "level1", 
                                               new XElement( "level2", "text" ), 
                                               new XElement( "level2", "other text" ) ) ) );
            return doc;
        }

        [Test]
        public void CreateFromPath_PathDoesNotExist_RaiseException()
        {
            var s = new DataSetFromCompressedXMLFilePathCreator();
            Assert.Throws<System.IO.FileNotFoundException>(() => s.CreateFromPath("/doesntexist"));
        }

        [Test]
        public void CreateFromPath_FileNotCompressed_RaiseException()
        {
            string path = "tmp.xml";
            XDocument doc = this.CreateXMLDoc();
            doc.Save(path);
            var s = new DataSetFromCompressedXMLFilePathCreator();
            Assert.Throws<System.IO.InvalidDataException>(() => s.CreateFromPath(path));
            File.Delete(path);
        }

        [Test]
        public void CreateFromPath_NotXMLFile_RaiseException()
        {
            string path = "tmp.txt";
            FileStream fs = File.Create(path);
            GZipStream compression = new GZipStream(fs, CompressionMode.Compress);
            compression.Write(Encoding.UTF8.GetBytes("Some Text"));
            compression.Close();
            fs.Close();

            var s = new DataSetFromCompressedXMLFilePathCreator();
            Assert.Throws<XmlException>(() => s.CreateFromPath(path));
            File.Delete(path);
        }

        [Test]
        public void CreateFromPath_CreatesOkay()
        {
            string path = "tmp.gz";
            XDocument doc = this.CreateXMLDoc();
            FileStream fs = File.Create(path);
            GZipStream compression = new GZipStream(fs, CompressionMode.Compress);
            compression.Write(Encoding.UTF8.GetBytes(doc.ToString()));
            compression.Close();
            fs.Close();

            var s = new DataSetFromCompressedXMLFilePathCreator();
            Assert.DoesNotThrow(()=>s.CreateFromPath(path));
            File.Delete(path);
        }
    }
}
