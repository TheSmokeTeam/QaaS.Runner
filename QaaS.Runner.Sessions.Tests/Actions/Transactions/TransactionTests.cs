using System.Collections.Generic;
using System.Linq;
using Moq;
using NUnit.Framework;
using NUnit.Framework.Legacy;
using QaaS.Framework.Policies;
using QaaS.Framework.Protocols.Protocols;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Session;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Runner.Sessions.Tests.Actions.Utils;
using Serilog;
using Serilog.Events;
using Serilog.Extensions.Logging;

namespace QaaS.Runner.Sessions.Tests.Actions.Transactions;

[TestFixture]
public class TransactionTests
{
    private static Mock<ITransactor>? _transactor;
    private static List<Data<object>> _infoSent = [];

    [Test,
     TestCaseSource(typeof(TestResourceDataSources),
         nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters))]
    public void
        TestTransactAndInitializeIterableSerializableSaveIterator_ReceivesValidDataSourceNamesAndAppropriateFiltersAndLoopPolicy_SendsTheProperDataToTheSenderObject(
            List<string> names,
            List<string> patterns,
            List<DataSource> dataSources,
            List<Data<object>> expectedData)
    {
        // Arrange
        var amountOfDataToSend = expectedData.Count * 2;
        var transactor = CreationalFunctions.CreateTransactorWithLoop(
            ref _transactor!,
            "test data",
            ref _infoSent,
            patterns.ToArray(),
            names.ToArray(), 
            amountOfDataToSend);

        // Act
        transactor.InitializeIterableSerializableSaveIterator([], dataSources);
        transactor.Act();

        // Assert
        var orderedExpectedData = expectedData.SelectMany(d => new[] { d.Body, d.Body }).Order();
        var orderedReceivedData = _infoSent.Select(d => d.Body).Order();
        CollectionAssert.AreEqual(orderedExpectedData, orderedReceivedData);
    }

    private static Sessions.Actions.Transactions.Transaction InitTransactorWithIterations(
        string[]? dsPatterns,
        string[]? dsNames,
        int numberOfIterations,
        string dataToGet,
        int msgPerSec = 100)
    {
        _infoSent = CreationalFunctions.InitTransactor(ref _transactor!, dataToGet);

        return new Sessions.Actions.Transactions.Transaction(
            "TestPub", _transactor!.Object, 0,
            new DataFilter() { Body = true, Timestamp = true, MetaData = false },
            new DataFilter() { Body = true, Timestamp = true, MetaData = false },
            new LoadBalancePolicy(msgPerSec, 1000),
            false, numberOfIterations, 0,
            null,
            null,
            null,
            dsPatterns, dsNames,
            new SerilogLoggerFactory(new LoggerConfiguration().MinimumLevel
                .Is(LogEventLevel.Information).WriteTo
                .Console().CreateLogger()).CreateLogger("DefaultLogger"));
    }

    [Test,
     TestCaseSource(typeof(TestResourceDataSources),
         nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters))]
    public void
        TestTransact_ReceivesValidDataSourceNamesAndAppropriateFiltersAndIterationsPolicy_SendsTheProperDataToTheSenderObject(
            List<string> names,
            List<string> patterns,
            List<DataSource> dataSources,
            List<Data<object>> expectedData)
    {
        // Arrange
        const int iterationNumber = 2;
        var transactor = InitTransactorWithIterations(patterns.ToArray(), names.ToArray(), iterationNumber, "test data");

        // Act
        transactor.InitializeIterableSerializableSaveIterator([], dataSources);
        transactor.Act();

        // Assert
        _transactor!.Verify(t => t.Transact(It.IsAny<Data<object>>()),
            Times.Exactly(expectedData.Count * iterationNumber));
    }

    [Test,
     TestCaseSource(typeof(TestResourceDataSources),
         nameof(TestResourceDataSources.ValidDataSourceNamesAndAppropriateFilters))]
    public void TestExportRunningCommunicationData_ReceivesRcdToExport_ExportTheGivenDataToTheRcd(
        List<string> names,
        List<string> patterns,
        List<DataSource> dataSources,
        List<Data<object>> expectedData)
    {
        // Arrange
        const string sessionName = "test session";
        var context = CreationalFunctions.CreateContext(sessionName, dataSources);
        var transactor = InitTransactorWithIterations(
            patterns.ToArray(),
            names.ToArray(),
            1, 
            "test data");

        // Act
        transactor.ExportRunningCommunicationData(context, sessionName);
        transactor.InitializeIterableSerializableSaveIterator([], dataSources);
        transactor.Act();

        // Arrange
        var expectedSentData = expectedData.Select(d => d.Body).Order().ToList();
        var expectedReceivedData = Enumerable.Repeat("test data", expectedData.Count).ToList();
        var receivedData =
            context.InternalRunningSessions.RunningSessionsDict[sessionName].Outputs![0].Data.Select(d => d!.Body)
                .Order().ToList();
        var sentData =
            context.InternalRunningSessions.RunningSessionsDict[sessionName].Inputs![0].Data.Select(d => d!.Body)
                .Order().ToList();
        CollectionAssert.AreEqual(expectedReceivedData, receivedData);
        CollectionAssert.AreEqual(expectedSentData, sentData);
    }
}