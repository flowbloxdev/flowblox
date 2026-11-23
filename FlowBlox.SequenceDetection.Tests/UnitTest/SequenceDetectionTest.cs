using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.Reflection;
using FlowBlox.SequenceDetection.Util;
using FlowBlox.SequenceDetection;
using FlowBlox.SequenceDetection.Model;

namespace FlowBlox.SequenceDetectionTests.UnitTest
{
    [TestClass]
    [TestCategory(FlowBloxTestCategories.UnitTest)]
    public class SequenceDetectionTest
    {
        public SequenceDetectionTest()
        {
        }

        [TestMethod]
        public void FindPatternForMockup_Data_Complex()
        {
            var c1 = ResourceUtil.GetResourceAsStringRelatedTo<SequenceDetectionTest>(Assembly.GetExecutingAssembly(), "Mockup.HTMLContent_Complex_Data_C1.html");
            var c2 = ResourceUtil.GetResourceAsStringRelatedTo<SequenceDetectionTest>(Assembly.GetExecutingAssembly(), "Mockup.HTMLContent_Complex_Data_C2.html");
            var sequenceDetection = SequenceDetectionService.Instance;
            var patternName = sequenceDetection.Detect(new SequenceDetectionInputData()
            {
                Entries = new List<SequenceDetectionInputEntry>()
                {
                    new SequenceDetectionInputEntry(c1, "Salon vegan", 1),
                    new SequenceDetectionInputEntry(c2, "Haartechnik Klang", 1)
                }
            });

            var patternStrasse = sequenceDetection.Detect(new SequenceDetectionInputData()
            {
                Entries = new List<SequenceDetectionInputEntry>()
                {
                    new SequenceDetectionInputEntry(c1, "Saarlandstr. 98", 1),
                    new SequenceDetectionInputEntry(c2, "Ruhrallee 69", 1)
                }
            });

            var patternPLZ = sequenceDetection.Detect(new SequenceDetectionInputData()
            {
                Entries = new List<SequenceDetectionInputEntry>()
                {
                    new SequenceDetectionInputEntry(c1, "44139", 1),
                    new SequenceDetectionInputEntry(c2, "44139", 1)
                }
            });

            Assert.IsNotNull(patternName);
            Assert.IsNotNull(patternStrasse);
            Assert.IsNotNull(patternPLZ);

            List<string> result = new List<string>();

            SequenceSearch.Instance.SearchFor(c1, patternName!, ref result);
            SequenceSearch.Instance.SearchFor(c1, patternStrasse!, ref result);
            SequenceSearch.Instance.SearchFor(c1, patternPLZ!, ref result);
        }

        [TestMethod]
        public void FindPatternForMockup_Links_Complex()
        {
            var c1 = ResourceUtil.GetResourceAsStringRelatedTo<SequenceDetectionTest>(Assembly.GetExecutingAssembly(), "Mockup.HTMLContent_Complex_MultipleMatches_C1.html");
            var c2 = ResourceUtil.GetResourceAsStringRelatedTo<SequenceDetectionTest>(Assembly.GetExecutingAssembly(), "Mockup.HTMLContent_Complex_MultipleMatches_C2.html");
            var sequenceDetection = SequenceDetectionService.Instance;
            var patternDescription = sequenceDetection.Detect(new SequenceDetectionInputData()
            {
                Entries = new List<SequenceDetectionInputEntry>()
                {
                    new SequenceDetectionInputEntry(c1, "https://www.gelbeseiten.de/gsbiz/168d03a5-77f4-4ba1-bf7a-05812519a5be", 50),
                    new SequenceDetectionInputEntry(c2, "https://www.gelbeseiten.de/gsbiz/aaa1e245-35ec-4232-aa3b-8e72d4fe8b9a", 11)
                }
            });

            Assert.IsNotNull(patternDescription);

            List<string> result = new List<string>();
            SequenceSearch.Instance.SearchFor(c2, patternDescription!, ref result);
        }

        [TestMethod]
        public void FindPatternForMockup_Links_Complex2()
        {
            var c1 = ResourceUtil.GetResourceAsStringRelatedTo<SequenceDetectionTest>(Assembly.GetExecutingAssembly(), "Mockup.HTMLContent_Complex_MultipleMatches2_C1.html");
            var c2 = ResourceUtil.GetResourceAsStringRelatedTo<SequenceDetectionTest>(Assembly.GetExecutingAssembly(), "Mockup.HTMLContent_Complex_MultipleMatches2_C2.html");
            var sequenceDetection = SequenceDetectionService.Instance;
            var patternDescription = sequenceDetection.Detect(new SequenceDetectionInputData()
            {
                Entries = new List<SequenceDetectionInputEntry>()
                {
                    new SequenceDetectionInputEntry(c1, "https://www.gelbeseiten.de/gsbiz/c837f483-243b-4663-b5cf-f68df0b43818", 15),
                    new SequenceDetectionInputEntry(c2, "https://www.gelbeseiten.de/gsbiz/aaa1e245-35ec-4232-aa3b-8e72d4fe8b9a", 11)
                }
            });

            Assert.IsNotNull(patternDescription);

            List<string> result = new List<string>();
            SequenceSearch.Instance.SearchFor(c2, patternDescription!, ref result);
        }

        [TestMethod]
        public void FindPatternForMockup_Basic_MultipleMatches_Test()
        {
            var c1 = ResourceUtil.GetResourceAsStringRelatedTo<SequenceDetectionTest>(Assembly.GetExecutingAssembly(), "Mockup.HTMLContent_MultipleMatches_Test_C1.html");
            var c2 = ResourceUtil.GetResourceAsStringRelatedTo<SequenceDetectionTest>(Assembly.GetExecutingAssembly(), "Mockup.HTMLContent_MultipleMatches_Test_C2.html");
            var sequenceDetection = SequenceDetectionService.Instance;
            var patternDescription = sequenceDetection.Detect(new SequenceDetectionInputData()
            {
                Entries = new List<SequenceDetectionInputEntry>()
                {
                    new SequenceDetectionInputEntry(c1, "#1", 10),
                    new SequenceDetectionInputEntry(c2, "#1", 10)
                }
            });

            Assert.IsNotNull(patternDescription);

            List<string> result = new List<string>();
            SequenceSearch.Instance.SearchFor(c1, patternDescription!, ref result);
        }

        [TestMethod]
        public void FindPatternForLargeDocuments_WithOneExpectedResult()
        {
            var padding = string.Concat(Enumerable.Repeat(
                "<record><label>Unrelated content</label></record>\r\n", 30000));
            var first = padding + "<item><value>alpha17</value></item>" + padding;
            var second = padding + "<item><value>beta23</value></item>" + padding;
            var pattern = SequenceDetectionService.Instance.Detect(new SequenceDetectionInputData
            {
                Timeout = 5,
                Entries =
                [
                    new SequenceDetectionInputEntry(first, "alpha17", 1),
                    new SequenceDetectionInputEntry(second, "beta23", 1)
                ]
            });

            Assert.IsNotNull(pattern);
            var firstResults = new List<string>();
            var secondResults = new List<string>();
            SequenceSearch.Instance.SearchFor(first, pattern, ref firstResults);
            SequenceSearch.Instance.SearchFor(second, pattern, ref secondResults);

            CollectionAssert.AreEqual(new[] { "alpha17" }, firstResults);
            CollectionAssert.AreEqual(new[] { "beta23" }, secondResults);
        }

        [TestMethod]
        public void FindPatternForBooksToScrape_ProductRatingWithDuplicateRating()
        {
            var content = ResourceUtil.GetResourceAsStringRelatedTo<SequenceDetectionTest>(
                Assembly.GetExecutingAssembly(),
                "Mockup.HTMLContent_DuplicateMatch_MultilineTermination.html");
            var pattern = SequenceDetectionService.Instance.Detect(new SequenceDetectionInputData
            {
                Entries =
                [
                    new SequenceDetectionInputEntry(content, "Two", 1)
                ]
            });

            Assert.IsNotNull(pattern);

            var results = new List<string>();
            SequenceSearch.Instance.SearchFor(content, pattern, ref results);

            CollectionAssert.AreEqual(new[] { "Two" }, results);
        }
    }
}
