using Microsoft.VisualStudio.TestTools.UnitTesting;
using System.IO;
using Shouldly;
using Yuniql.Core;
using Yuniql.Extensibility;

namespace Yuniql.PlatformTests
{

    [TestClass]
    public class LocalVersionServiceTests : TestBase
    {
        private ITestDataService _testDataService;
        private IMigrationServiceFactory _migrationServiceFactory;
        private ITraceService _traceService;
        private TestConfiguration _testConfiguration;
        [TestInitialize]
        public void Setup()
        {
            _testConfiguration = base.ConfigureWithEmptyWorkspace();

            //create test data service provider
            var testDataServiceFactory = new TestDataServiceFactory();
            _testDataService = testDataServiceFactory.Create(_testConfiguration.Platform);

            //create data service factory for migration proper
            _traceService = new FileTraceService();
            _migrationServiceFactory = new MigrationServiceFactory(_traceService);
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (Directory.Exists(_testConfiguration.WorkspacePath))
                Directory.Delete(_testConfiguration.WorkspacePath, true);
        }

        [TestMethod]
        public void Test_Init()
        {
            //act
            var localVersionService = new LocalVersionService(_traceService);
            localVersionService.Init(_testConfiguration.WorkspacePath);

            //assert
            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "_init")).ShouldBe(true);
            File.Exists(Path.Combine(Path.Combine(_testConfiguration.WorkspacePath, "_init"), "README.md")).ShouldBe(true);

            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "_pre")).ShouldBe(true);
            File.Exists(Path.Combine(Path.Combine(_testConfiguration.WorkspacePath, "_pre"), "README.md")).ShouldBe(true);

            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "v0.00")).ShouldBe(true);
            File.Exists(Path.Combine(Path.Combine(_testConfiguration.WorkspacePath, "v0.00"), "README.md")).ShouldBe(true);

            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "_draft")).ShouldBe(true);
            File.Exists(Path.Combine(Path.Combine(_testConfiguration.WorkspacePath, "_draft"), "README.md")).ShouldBe(true);

            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "_post")).ShouldBe(true);
            File.Exists(Path.Combine(Path.Combine(_testConfiguration.WorkspacePath, "_post"), "README.md")).ShouldBe(true);

            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "_erase")).ShouldBe(true);
            File.Exists(Path.Combine(Path.Combine(_testConfiguration.WorkspacePath, "_erase"), "README.md")).ShouldBe(true);

            File.Exists(Path.Combine(_testConfiguration.WorkspacePath, "README.md")).ShouldBe(true);
            File.Exists(Path.Combine(_testConfiguration.WorkspacePath, "Dockerfile")).ShouldBe(true);
            File.Exists(Path.Combine(_testConfiguration.WorkspacePath, ".gitignore")).ShouldBe(true);
        }

        [TestMethod]
        public void Test_Init_Called_Multiple_Is_Handled()
        {
            //act
            var localVersionService = new LocalVersionService(_traceService);
            localVersionService.Init(_testConfiguration.WorkspacePath);
            localVersionService.Init(_testConfiguration.WorkspacePath);
            localVersionService.Init(_testConfiguration.WorkspacePath);

            //assert
            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "_init")).ShouldBe(true);
            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "_pre")).ShouldBe(true);
            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "v0.00")).ShouldBe(true);
            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "_draft")).ShouldBe(true);
            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "_post")).ShouldBe(true);
            File.Exists(Path.Combine(_testConfiguration.WorkspacePath, "README.md")).ShouldBe(true);
            File.Exists(Path.Combine(_testConfiguration.WorkspacePath, "Dockerfile")).ShouldBe(true);
            File.Exists(Path.Combine(_testConfiguration.WorkspacePath, ".gitignore")).ShouldBe(true);
        }

        [TestMethod]
        public void Test_Vnext_Major_Version()
        {
            //act
            var localVersionService = new LocalVersionService(_traceService);
            localVersionService.Init(_testConfiguration.WorkspacePath);
            localVersionService.IncrementMajorVersion(_testConfiguration.WorkspacePath, null);

            //assert
            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "v1.00")).ShouldBe(true);
        }

        [TestMethod]
        public void Test_Vnext_Major_Version_With_Template_File()
        {
            //act
            var localVersionService = new LocalVersionService(_traceService);
            localVersionService.Init(_testConfiguration.WorkspacePath);
            localVersionService.IncrementMajorVersion(_testConfiguration.WorkspacePath, "Test.sql");

            //assert
            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "v1.00")).ShouldBe(true);
            File.Exists(Path.Combine(Path.Combine(_testConfiguration.WorkspacePath, "v1.00"), "Test.sql")).ShouldBe(true);
        }

        [TestMethod]
        public void Test_Vnext_Minor_Version()
        {
            //act
            var localVersionService = new LocalVersionService(_traceService);
            localVersionService.Init(_testConfiguration.WorkspacePath);
            localVersionService.IncrementMinorVersion(_testConfiguration.WorkspacePath, null);

            //assert
            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "v0.01")).ShouldBe(true);
        }

        [TestMethod]
        public void Test_Vnext_Minor_Version_With_Template_File()
        {
            //act
            var localVersionService = new LocalVersionService(_traceService);
            localVersionService.Init(_testConfiguration.WorkspacePath);
            localVersionService.IncrementMinorVersion(_testConfiguration.WorkspacePath, "Test.sql");

            //assert
            Directory.Exists(Path.Combine(_testConfiguration.WorkspacePath, "v0.01")).ShouldBe(true);
            File.Exists(Path.Combine(Path.Combine(_testConfiguration.WorkspacePath, "v0.01"), "Test.sql")).ShouldBe(true);
        }

        [TestMethod]
        public void Test_Get_Latest_Version()
        {
            //act
            var localVersionService = new LocalVersionService(_traceService);
            localVersionService.Init(_testConfiguration.WorkspacePath);
            localVersionService.IncrementMajorVersion(_testConfiguration.WorkspacePath, null);
            localVersionService.IncrementMinorVersion(_testConfiguration.WorkspacePath, null);
            localVersionService.IncrementMinorVersion(_testConfiguration.WorkspacePath, null);

            //assert
            localVersionService.GetLatestVersion(_testConfiguration.WorkspacePath).ShouldBe("v1.02");
        }
    }
}
