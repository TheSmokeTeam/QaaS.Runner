using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using Microsoft.Extensions.Configuration;
using NUnit.Framework;
using QaaS.Framework.SDK.ContextObjects;
using QaaS.Framework.SDK.DataSourceObjects;
using QaaS.Framework.SDK.Hooks.Generator;
using QaaS.Framework.SDK.Session.DataObjects;
using QaaS.Framework.SDK.Session.SessionDataObjects;

namespace QaaS.Runner.Sessions.Tests.Actions;

/// <summary>
///  Utility class that returns custom DataSources and names DataSource filters.
/// </summary>
public static class TestResourceDataSources
{
    /// <summary>
    /// Returns DataSources and filters that include only the ones names Test, Test2 or have Other in the start of their names.
    /// The DataSources Test and OtherTest2 generate 100 numbers (0-99), and Test2 and OtherTest return "a", "ab", "abc" in order.
    /// The generated data are all ints.
    /// </summary>
    public static IEnumerable<TestCaseData> ValidDataSourceNamesAndAppropriateFilters()
    {
        var dataSources = new List<DataSource>()
        {
            new() { Generator = new TestGenerator1(), Lazy = true, Name = "Test" },
            new() {Generator = new TestGenerator2(), Lazy = true, Name = "Test2"},
            new() {Generator = new TestGenerator2(), Lazy = true, Name = "OtherTest"},
            new() {Generator = new TestGenerator1(), Lazy = true, Name = "OtherTest2"},
            new() {Generator = new TestGenerator2(), Lazy = true, Name = "ImNotIncluded..."}
        };

        // generators that should be used with the filters
        var generators = new List<IGenerator>
        {
            new TestGenerator2(),
            new TestGenerator1(),
            new TestGenerator1(),
            new TestGenerator2()
        };
        var expectedData = generators.SelectMany(generator => generator.Generate(null, null));
        
        yield return new TestCaseData(
            new List<string>() { "Test", "Test2" }, // name filters
            new List<string>() { "^Other" }, // pattern filters
            dataSources,
            expectedData.ToList());
    }
    
    public class TestGenerator1 : IGenerator
    {
        public List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration)
        {
            throw new NotImplementedException();
        }

        public Context Context { get; set; }
        
        public IEnumerable<Data<object>> Generate(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
        {
            for (var i = 0; i < 100; i++)
            {
                yield return new Data<object> { Body = i };
            }
        }
    }
    
    public class TestGenerator2 : IGenerator
    {
        public List<ValidationResult>? LoadAndValidateConfiguration(IConfiguration configuration)
        {
            throw new NotImplementedException();
        }

        public Context Context { get; set; }
        
        public IEnumerable<Data<object>> Generate(IImmutableList<SessionData> sessionDataList, IImmutableList<DataSource> dataSourceList)
        {
            var minuses = new List<int> { -100, -101, -102 };
            foreach (var minuse in minuses)
            {
                yield return new Data<object> { Body = minuse };
            }
        }
    }
}