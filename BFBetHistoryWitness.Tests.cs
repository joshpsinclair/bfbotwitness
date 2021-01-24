using System;
using System.Linq;
using NUnit;
using NUnit.Framework;
using BFBetHistoryWitness;
using Differences;
using System.Data;
using System.Collections.Generic;
using DataSetCreator;
using System.IO;
using System.IO.Compression;
using System.Text;


namespace BFBetHistoryWitness
{
    
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

    [TestFixture]
    public class BFBetHistoryWitness_DataTableDifferences
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
    public class BFBetHistoryWitness_BFBetHistoryDifferences
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

            // Create an initial Compressed XML file
            DataTableCreator creator = new DataTableCreator();
            List<String> columns = new List<String>() {idColName, differenceColName};
            List<Object> aRow = new List<Object>();
            List<String> innerRowA = new List<String>() {"TheID", "DifferentA"};
            aRow.Add(innerRowA);
            DataTable a = creator.Create(columns, aRow);
            DataSet sa = new DataSet("Dataset");
            sa.Tables.Add(a);
            string path=@"/Users/joshsinclair/Projects/BFBotWitness/tmp.gz";
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
            Console.WriteLine("This was called");
            OnNextCallCount+=1;
        }
    }


    [TestFixture]
    public class BFBetHistoryWitness_BFBetHistoryWorker {
        [Test]
        public void CreateBFBetHistoryItemsFromDifferences_DoesDetectDifferences_DifferencesCountIs1() {
            string dest1 = @"/Users/joshsinclair/Projects/BFBotWitness/testFile_1.gz";
            CompressFile.Compress(@"/Users/joshsinclair/Projects/BFBotWitness/testFile_1.xml",
                                dest1);

            string path=@"/Users/joshsinclair/Projects/BFBotWitness/testFile_1.gz";
            IDataSetFromPathCreator statelessCreator = new DataSetFromCompressedXMLFilePathCreator();
            IDataSetCreator statefullCreator = new DataSetFromPathCreator(path,
                                                                                statelessCreator);
            IObjectDifferences datatableDifferences = new DataTableDifferences("BetId");
            IDifferences differencesWitness = new BFBetHistoryDifferences(statefullCreator, datatableDifferences);

            BFBetHistoryWorker worker = new BFBetHistoryWorker(differencesWitness);

            List<BFBetHistoryItem> items = worker.CreateBFBetHistoryItemsFromDifferences();
            Assert.AreEqual(items.Count, 0);
            List<BFBetHistoryItem> items2 = worker.CreateBFBetHistoryItemsFromDifferences();

            string dest2 = @"/Users/joshsinclair/Projects/BFBotWitness/testFile_1.gz";
            CompressFile.Compress(@"/Users/joshsinclair/Projects/BFBotWitness/testFile_2.xml",
                                dest2);

            List<BFBetHistoryItem> updatedItems =worker.CreateBFBetHistoryItemsFromDifferences();
            Assert.AreEqual(updatedItems.Count, 1);
        }

        [Test]
        public void CreateBFBetHistoryItemsFromDifferences_DoesNotifyObservers_ObserversCallCount1() {
            string dest1 = @"/Users/joshsinclair/Projects/BFBotWitness/testFile_1.gz";
            CompressFile.Compress(@"/Users/joshsinclair/Projects/BFBotWitness/testFile_1.xml",
                                dest1);

            string path=@"/Users/joshsinclair/Projects/BFBotWitness/testFile_1.gz";
            IDataSetFromPathCreator statelessCreator = new DataSetFromCompressedXMLFilePathCreator();
            IDataSetCreator statefullCreator = new DataSetFromPathCreator(path,
                                                                                statelessCreator);
            IObjectDifferences datatableDifferences = new DataTableDifferences("BetId");
            IDifferences differencesWitness = new BFBetHistoryDifferences(statefullCreator, datatableDifferences);

            BFBetHistoryWorker worker = new BFBetHistoryWorker(differencesWitness);
            List<BFBetHistoryItem> items = worker.CreateBFBetHistoryItemsFromDifferences();

            string dest2 = @"/Users/joshsinclair/Projects/BFBotWitness/testFile_1.gz";
            CompressFile.Compress(@"/Users/joshsinclair/Projects/BFBotWitness/testFile_2.xml",
                                dest2);

            MockObserver mockObserver = new MockObserver();
            IDisposable d = worker.Subscribe(mockObserver);

            List<BFBetHistoryItem> updatedItems =worker.CreateBFBetHistoryItemsFromDifferences();
            worker.NotifySubscribersOfBFBetHistoryItems(updatedItems);
            Assert.AreEqual(mockObserver.OnNextCallCount, 1);
        }

    }
}