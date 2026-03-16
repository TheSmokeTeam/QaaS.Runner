using Amazon.S3;
using QaaS.Framework.Protocols.Utils.S3Utils;
using QaaS.Runner.Storage.ConfigurationObjects;
using QaaS.Runner.Storage.ConfigurationObjects.StorageConfigs;

namespace QaaS.Runner.Storage;

public class S3Storage : BaseStorage
{
    private readonly S3Config _configuration;

    public S3Storage(S3Config configuration, Formatting jsonStorageFormat) : base(jsonStorageFormat)
    {
        _configuration = configuration;
    }

    protected virtual IS3Client BuildS3Client(S3Config config)
    {
        return new S3Client(new AmazonS3Client(config.AccessKey, config.SecretKey,
            new AmazonS3Config
            {
                ServiceURL = config.ServiceURL,
                ForcePathStyle = config.ForcePathStyle
            }), _context.Logger, config.MaximumRetryCount);
    }

    protected override void StoreSerialized(
        IList<KeyValuePair<string, byte[]>> sessionFileNameAndSerializedSessionDataItemsToStorePair, string? caseName)
    {
        using var s3Client = BuildS3Client(_configuration);
        foreach (var _ in s3Client.PutObjectsInS3BucketSync(_configuration.StorageBucket!,
                     sessionFileNameAndSerializedSessionDataItemsToStorePair.Select(pair =>
                         new KeyValuePair<string, byte[]>(
                             $"{CaseStorageHandler.HandleCaseWithS3(_configuration, caseName)}{pair.Key}",
                             pair.Value))))
        {
        }
    }

    protected override IEnumerable<byte[]> RetrieveSerialized(string? caseName)
    {
        using var s3Client = BuildS3Client(_configuration);
        return s3Client.GetAllObjectsInS3BucketUnOrdered(_configuration.StorageBucket!,
                CaseStorageHandler.HandleCaseWithS3(_configuration, caseName), _configuration.Delimiter,
                _configuration.SkipEmptyObjects)
            .Select(pair => pair.Value ?? []);
    }
}
