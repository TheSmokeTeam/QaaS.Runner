// using Autofac;
// using NUnit.Framework;
// using QaaS.Framework.SDK.DataSourceObjects;
// using QaaS.Runner.Assertions.ConfigurationObjects;
// using QaaS.Runner.Sessions.Session.Builders;
//
// namespace QaaS.Runner.Tests.BootstapTests;
//
// public class BootstrapTests
// {
//     public static IEnumerable<TestCaseData> TestNewTestCaseSource()
//     {
//         var defaultExecutionBuilder = new ExecutionBuilder()
//         {
//             Assertions = [new AssertionBuilder(), new AssertionBuilder(), new AssertionBuilder()],
//             Sessions = [new SessionBuilder(), new SessionBuilder(), new SessionBuilder()],
//             DataSources = [new DataSourceBuilder(), new DataSourceBuilder(), new DataSourceBuilder()]
//         };
//         var defaultExecutionBuilder2 = new ExecutionBuilder()
//         {
//             Assertions = [new AssertionBuilder(), new AssertionBuilder(), new AssertionBuilder()],
//             Sessions = [new SessionBuilder(), new SessionBuilder(), new SessionBuilder()],
//             DataSources = [new DataSourceBuilder(), new DataSourceBuilder(), new DataSourceBuilder()]
//         };
//         var defaultScope = new ContainerBuilder().Build().BeginLifetimeScope();
//
//         var args1 = new[] { "run", "TestData/test.qaas.yaml" };
//         yield return new TestCaseData(
//                 new Runner(defaultScope, [defaultExecutionBuilder], Globals.Logger), args1)
//             .SetName("RunWithNoArgs");
//
//         var args2 = new[] { "run", "TestData/test.qaas.yaml", "-w", "TestData/override.yaml" };
//         defaultExecutionBuilder2.AddAssertion(new AssertionBuilder());
//         defaultExecutionBuilder2.AddSession(new SessionBuilder());
//         defaultExecutionBuilder2.AddDataSource(new DataSourceBuilder());
//         yield return new TestCaseData(
//                 new Runner(defaultScope, [defaultExecutionBuilder2], Globals.Logger), args2)
//             .SetName("RunWithOverrideFiles");
//
//         var args3 = new[] { "execute", "TestData/executable.yaml" };
//         yield return new TestCaseData(
//                 new Runner(defaultScope, [ defaultExecutionBuilder, defaultExecutionBuilder2], Globals.Logger), args3)
//             .SetName("ExecuteMultipleCommandsWithArgs");
//     }
//
//     [Test, TestCaseSource(nameof(TestNewTestCaseSource))]
//     public void TestNew_CallFunctionWithDifferentArgsAndYamls_ShouldReturnExpectedRunnerObject(Runner expectedOutput,
//         string[] args)
//     {
//         // Get the directory 3 levels up from the current executable
//         var currentDir = Directory.GetCurrentDirectory();
//         var parentDir = Directory.GetParent(currentDir);
//         var grandParentDir = Directory.GetParent(parentDir.FullName);
//         var greatGrandParentDir = Directory.GetParent(grandParentDir.FullName);
//
//         Environment.CurrentDirectory = greatGrandParentDir.FullName;
//
//         // arrange & act
//         var runner = Bootstrap.New(args);
//
//         // assert
//         Assert.That(expectedOutput.ExecutionBuilders.Count, Is.EqualTo(runner.ExecutionBuilders.Count));
//
//         for (var index = 0; index < runner.ExecutionBuilders.Count; index++)
//         {
//             var executionBuilder = runner.ExecutionBuilders[index];
//             var outputExecutionBuilder = expectedOutput.ExecutionBuilders[index];
//
//             Assert.That(outputExecutionBuilder.Assertions.Length, Is.EqualTo(executionBuilder.Assertions.Length));
//             Assert.That(outputExecutionBuilder.Sessions.Length, Is.EqualTo(executionBuilder.Sessions.Length));
//             Assert.That(outputExecutionBuilder.Storages.Length, Is.EqualTo(executionBuilder.Storages.Length));
//             Assert.That(outputExecutionBuilder.DataSources.Length, Is.EqualTo(executionBuilder.DataSources.Length));
//         }
//     }
// }

