using System;
using System.Linq;
using NUnit;
using NUnit.Framework;
using BFBotWitness;
using System.Data;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using System.Xml;
using HttpMock;
using System.Net.Http;
using Serilog;

namespace BFBotWitness
{
    
    public class Utils {
        public static string ProjectDir() {
            string workingDir = Environment.CurrentDirectory;
            string projectDir=Directory.GetParent(workingDir).Parent.Parent.FullName;
            return projectDir;
        }

        public static string BinaryDir() {
            return System.AppDomain.CurrentDomain.BaseDirectory;
        }

        public static XDocument CreateXMLDoc() {
            XDocument doc = new XDocument( new XElement( "body", 
                                           new XElement( "level1", 
                                               new XElement( "level2", "text" ), 
                                               new XElement( "level2", "other text" ) ) ) );
            return doc;
        }
    }

    public class DataTableCreator {
        public DataTable Create(List<String> columns,
                                    List<Object> rows) {
            DataTable table = new DataTable("DataTable");
            foreach (String col in columns) {
                DataColumn colObj = new DataColumn(col, typeof(String));
                table.Columns.Add(colObj);
            }

            foreach (List<String> row in rows) {
                DataRow rowObj = table.NewRow();
                for (int i=0; i<row.Count; i++) {
                    rowObj[i]=row[i];
                }

                table.Rows.Add(rowObj);
            }

            return table;
        }
    }

    public static class CompressFile {
        public static void Compress(string source, string dest) {
            FileInfo fileName = new FileInfo(source);
            using (FileStream fs = fileName.OpenRead()) {
                using(FileStream to = File.Create(dest)) {
                    using (GZipStream compression = new GZipStream(to, CompressionMode.Compress)) {
                        fs.CopyTo(compression);
                    }   
                }
            }
        }
    }


    public class MockObserver : IObserver<Object>
    {
        public float OnNextCallCount;

        internal MockObserver() {
            OnNextCallCount=0;
        }
        
        public virtual void OnCompleted(){ }
        public virtual void OnError(Exception e){ }
        public virtual void OnNext(Object item) {
            OnNextCallCount+=1;
        }
    }

    [TestFixture]
    public class BFBotWitness_DataTableDifferences
    {

        [Test]
        public void Differences_InputsAreDifferent_DifferenceCountIs1()
        {
            DataTableCreator creator = new DataTableCreator();
            List<String> columns = new List<String>() {"id", "differentCol"};
            List<Object> aRow = new List<Object>();
            List<Object> bRow = new List<Object>();
            List<String> innerRowA = new List<String>() {"TheID", "DifferentA"};
            List<String> innerRowB = new List<String>() {"TheID", "DifferentB"};
            aRow.Add(innerRowA);
            bRow.Add(innerRowB);
            DataTable a = creator.Create(columns, aRow);
            DataTable b = creator.Create(columns, bRow);
            DataTableDifferences obj = new DataTableDifferences("id");
            System.Collections.Generic.List<object> differences = obj.Differences(a, b);
            Assert.AreEqual(differences.Count, 1);
        }

        [Test]
        public void Differences_InputsAreSame_DifferenceCountIs0()
        {
            DataTableCreator creator = new DataTableCreator();
            List<String> columns = new List<String>() {"id", "differentCol"};
            List<Object> aRow = new List<Object>();
            List<Object> bRow = new List<Object>();
            List<String> innerRowA = new List<String>() {"TheID", "DifferentA"};
            List<String> innerRowB = new List<String>() {"TheID", "DifferentA"};
            aRow.Add(innerRowA);
            bRow.Add(innerRowB);
            DataTable a = creator.Create(columns, aRow);
            DataTable b = creator.Create(columns, bRow);
            DataTableDifferences obj = new DataTableDifferences("id");
            System.Collections.Generic.List<object> differences = obj.Differences(a, b);
            Assert.AreEqual(differences.Count, 0);
        }

        [Test]
        public void Differences_InputsArentCastable_RaisesException()
        {
            DataTableDifferences obj = new DataTableDifferences("id");
            Assert.Throws<InvalidCastException>(() => obj.Differences("a", "b"));
        }
        
    }

    [TestFixture]
    public class BFBotWitness_BFBetHistoryDifferences
    {

        public void WriteDatasetAsCompressedXML(DataSet d, string path) {
            using (StringWriter sw = new StringWriter()) {
                d.WriteXml(sw, XmlWriteMode.IgnoreSchema);
                string result = sw.ToString();
                using (FileStream fs = File.Create(path)) {
                    using (GZipStream compression = new GZipStream(fs, CompressionMode.Compress)) {
                        using(StreamWriter writer = new StreamWriter(compression)) {
                            writer.Write(result);
                        }
                    }
                }
            }
        }



        [Test]
        public void Differences_ChangesBetweenCalls_DifferenceCountIs1()
        {
            string idColName="id";
            string differenceColName="differentCol";
            string projectDir = Utils.BinaryDir();


            // Create an initial Compressed XML file
            DataTableCreator creator = new DataTableCreator();
            List<String> columns = new List<String>() {idColName, differenceColName};
            List<Object> aRow = new List<Object>();
            List<String> innerRowA = new List<String>() {"TheID", "DifferentA"};
            aRow.Add(innerRowA);
            DataTable a = creator.Create(columns, aRow);
            DataSet sa = new DataSet("Dataset");
            sa.Tables.Add(a);
            string path=Path.Combine(projectDir, "tmp.gz");
            WriteDatasetAsCompressedXML(sa, path);
            
            IDataSetFromPathCreator statelessCreator = new DataSetFromCompressedXMLFilePathCreator();
            IDataSetCreator statefullCreator = new DataSetFromPathCreator(path,
                                                                                statelessCreator);
            IObjectDifferences datatableDifferences = new DataTableDifferences(idColName);
            IDifferences differencesWitness = new BFBetHistoryDifferences(statefullCreator, datatableDifferences);

            // No Changes Yet
            Assert.AreEqual(0, differencesWitness.Differences().Count);
            
            // Update the file with different values
            List<String> columns2 = new List<String>() {idColName,  differenceColName};
            List<Object> aRow2 = new List<Object>();
            List<String> innerRowA2 = new List<String>() {"TheID", "DifferentB"};
            aRow2.Add(innerRowA2);
            DataTable b = creator.Create(columns2, aRow2);
            DataSet sb = new DataSet("Dataset");
            sb.Tables.Add(b);
            WriteDatasetAsCompressedXML(sb, path);

            // 1 difference reflecing the changes we made
            Assert.AreEqual(1, differencesWitness.Differences().Count);
        }
    }

    [TestFixture]
    public class BFBotWitness_BFBetHistoryWorker {
        [Test]
        public void CreateBFBetHistoryItemsFromDifferences_DoesDetectDifferences_DifferencesCountIs1() {
            string projectDir = Utils.BinaryDir();
            string dest = Path.Combine(projectDir, "testFile_1.gz");
            string originalSource1 = Path.Combine(projectDir, "testFile_1.xml");
            CompressFile.Compress(originalSource1, dest);

            IDataSetFromPathCreator statelessCreator = new DataSetFromCompressedXMLFilePathCreator();
            IDataSetCreator statefullCreator = new DataSetFromPathCreator(dest, statelessCreator);
            IObjectDifferences datatableDifferences = new DataTableDifferences("BetId");
            IDifferences differencesWitness = new BFBetHistoryDifferences(statefullCreator, datatableDifferences);

            BFBetHistoryWorker worker = new BFBetHistoryWorker("someid", differencesWitness);

            List<BFBetHistoryItem> items = worker.CreateBFBetHistoryItemsFromDifferences();
            Assert.AreEqual(items.Count, 0);
            List<BFBetHistoryItem> items2 = worker.CreateBFBetHistoryItemsFromDifferences();

            string originalSource2 = Path.Combine(projectDir, "testFile_2.xml");
            CompressFile.Compress(originalSource2, dest);

            List<BFBetHistoryItem> updatedItems = worker.CreateBFBetHistoryItemsFromDifferences();
            Assert.AreEqual(updatedItems.Count, 1);
        }

        [Test]
        public void CreateBFBetHistoryItemsFromDifferences_DoesNotifyObservers_ObserversCallCount1() {
            string projectDir = Utils.BinaryDir();
            string dest = Path.Combine(projectDir, "testFile_1.gz");
            string compressFile1 = Path.Combine(projectDir, "testFile_1.xml");
            CompressFile.Compress(compressFile1, dest);

            IDataSetFromPathCreator statelessCreator = new DataSetFromCompressedXMLFilePathCreator();
            IDataSetCreator statefullCreator = new DataSetFromPathCreator(dest, statelessCreator);
            IObjectDifferences datatableDifferences = new DataTableDifferences("BetId");
            IDifferences differencesWitness = new BFBetHistoryDifferences(statefullCreator, datatableDifferences);

            BFBetHistoryWorker worker = new BFBetHistoryWorker("someid", differencesWitness);
            List<BFBetHistoryItem> items = worker.CreateBFBetHistoryItemsFromDifferences();

            string compressFile2 = Path.Combine(projectDir, "testFile_2.xml");
            CompressFile.Compress(compressFile2, dest);

            MockObserver mockObserver = new MockObserver();
            IDisposable d = worker.Subscribe(mockObserver);

            List<BFBetHistoryItem> updatedItems =worker.CreateBFBetHistoryItemsFromDifferences();
            worker.NotifySubscribersOfBFBetHistoryItems(updatedItems);
            Assert.AreEqual(mockObserver.OnNextCallCount, 1);
            Assert.AreEqual(1, updatedItems.Count);
        }

        [Test]
        public void CreateBFBetHistoryItemsFromDifferences_DoesHandleIntialEmptyFile_ObserversCallCount1() {
            string projectDir = Utils.BinaryDir();
            string dest = Path.Combine(projectDir, "testFile_1.gz");
            string intialEmptyFile = Path.Combine(projectDir, "testFile_3.xml");
            CompressFile.Compress(intialEmptyFile, dest);

            IDataSetFromPathCreator statelessCreator = new DataSetFromCompressedXMLFilePathCreator();
            IDataSetCreator statefullCreator = new DataSetFromPathCreator(dest, statelessCreator);
            IObjectDifferences datatableDifferences = new DataTableDifferences("BetId");
            IDifferences differencesWitness = new BFBetHistoryDifferences(statefullCreator, datatableDifferences);

            BFBetHistoryWorker worker = new BFBetHistoryWorker("someid", differencesWitness);
            MockObserver mockObserver = new MockObserver();
            IDisposable d = worker.Subscribe(mockObserver);
            
            List<BFBetHistoryItem> items = worker.CreateBFBetHistoryItemsFromDifferences();
            Assert.AreEqual(mockObserver.OnNextCallCount, 0);
            Assert.AreEqual(items.Count, 0);

            string fileHasOneChange=Path.Combine(projectDir, "testFile_2.xml");
            CompressFile.Compress(fileHasOneChange, dest);

            List<BFBetHistoryItem> updatedItems =worker.CreateBFBetHistoryItemsFromDifferences();
            worker.NotifySubscribersOfBFBetHistoryItems(updatedItems);
            Assert.AreEqual(mockObserver.OnNextCallCount, 1);
            Assert.AreEqual(1, updatedItems.Count);
            
        }

    }

    [TestFixture]
    public class DataSetCreator_DataSetFromCompressedXMLFilePathCreator
    {
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
            XDocument doc = Utils.CreateXMLDoc();
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
            XDocument doc = Utils.CreateXMLDoc();
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