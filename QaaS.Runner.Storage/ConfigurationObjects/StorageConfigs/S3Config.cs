using System.ComponentModel;
using QaaS.Framework.Configurations.CommonConfigurationObjects;

namespace QaaS.Runner.Storage.ConfigurationObjects.StorageConfigs;

public record S3Config : S3BucketConfig, IStorageConfig
{
    [Description("The prefix of the relevant objects in the s3 bucket")]
    [DefaultValue("")]
    public string Prefix { get; set; } = "";

    [Description("The delimiter of the relevant objects in the s3 bucket")]
    [DefaultValue("")]
    public string Delimiter { get; set; } = "";

    [Description("The maximum number of times to retry when an action against the S3 fails due to maximum" +
                 " S3 supported IOPS, if no value is given will retry indefinitely")]
    public int? MaximumRetryCount { get; set; } // By default null which means no limit to the amounts of retries

    [Description(
        "Whether to skip the retrieval of empty s3 objects or not, if true skips them if false doesnt skip them")]
    [DefaultValue(true)]
    public bool SkipEmptyObjects { get; set; } = true;
}