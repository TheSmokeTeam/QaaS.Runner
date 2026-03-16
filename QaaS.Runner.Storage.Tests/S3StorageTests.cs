using Amazon.S3;
using Amazon.S3.Model;
using Moq;
using NUnit.Framework;
using QaaS.Framework.Protocols.Utils.S3Utils;
using QaaS.Runner.Storage.ConfigurationObjects;
using S3Config = QaaS.Runner.Storage.ConfigurationObjects.StorageConfigs.S3Config;

namespace QaaS.Runner.Storage.Tests;

[TestFixture]
public class S3StorageTests
{
    private sealed class FakeS3Client : IS3Client
    {
        public IAmazonS3 Client { get; init; } = new Mock<IAmazonS3>().Object;
        public List<KeyValuePair<string, byte[]>> StoredItems { get; } = [];
        public List<KeyValuePair<S3Object, byte[]?>> ObjectsToReturn { get; } = [];
        public (string BucketName, string Prefix, string Delimiter, bool SkipEmptyObjects)? RetrievalRequest { get; private set; }
        public bool Disposed { get; private set; }

        public Task<IEnumerable<DeleteObjectsResponse>> EmptyS3Bucket(string bucketName, string prefix = "",
            string delimiter = "")
        {
            return Task.FromResult<IEnumerable<DeleteObjectsResponse>>([]);
        }

        public Task<IEnumerable<S3Object>> ListAllObjectsInS3Bucket(string bucketName, string prefix = "",
            string delimiter = "", bool skipEmptyObjects = true)
        {
            return Task.FromResult<IEnumerable<S3Object>>([]);
        }

        public KeyValuePair<S3Object, byte[]?> GetObjectFromObjectMetadata(S3Object s3ObjectMetadata, string bucketName)
        {
            return new KeyValuePair<S3Object, byte[]?>(s3ObjectMetadata, null);
        }

        public IEnumerable<KeyValuePair<S3Object, byte[]?>> GetAllObjectsInS3BucketUnOrdered(string bucketName,
            string prefix = "", string delimiter = "", bool skipEmptyObjects = true)
        {
            RetrievalRequest = (bucketName, prefix, delimiter, skipEmptyObjects);
            return ObjectsToReturn;
        }

        public IEnumerable<PutObjectResponse> PutObjectsInS3BucketSync(string bucketName,
            IEnumerable<KeyValuePair<string, byte[]>> s3KeyValueItems)
        {
            foreach (var item in s3KeyValueItems)
            {
                StoredItems.Add(new KeyValuePair<string, byte[]>(item.Key, item.Value));
                yield return new PutObjectResponse();
            }
        }

        public void Dispose()
        {
            Disposed = true;
        }
    }

    private sealed class TestS3Storage(S3Config configuration, Formatting jsonStorageFormat, IS3Client s3Client)
        : S3Storage(configuration, jsonStorageFormat)
    {
        protected override IS3Client BuildS3Client(S3Config config) => s3Client;

        public void StoreSerializedForTest(IList<KeyValuePair<string, byte[]>> items, string? caseName)
        {
            base.StoreSerialized(items, caseName);
        }

        public IReadOnlyList<byte[]> RetrieveSerializedForTest(string? caseName)
        {
            return base.RetrieveSerialized(caseName).ToList();
        }
    }

    [Test]
    public void StoreSerialized_PrefixesKeysWithSanitizedCaseName_AndDisposesClient()
    {
        var fakeClient = new FakeS3Client();
        var storage = new TestS3Storage(new S3Config
        {
            StorageBucket = "bucket",
            Prefix = "root/"
        }, Formatting.None, fakeClient);

        storage.StoreSerializedForTest(
        [
            new KeyValuePair<string, byte[]>("session-a.json", [0x01]),
            new KeyValuePair<string, byte[]>("session-b.json", [0x02])
        ], "case/a");

        Assert.Multiple(() =>
        {
            Assert.That(fakeClient.StoredItems.Select(item => item.Key),
                Is.EqualTo(new[] { "root/case_a/session-a.json", "root/case_a/session-b.json" }));
            Assert.That(fakeClient.Disposed, Is.True);
        });
    }

    [Test]
    public void RetrieveSerialized_UsesConfiguredPrefixAndReturnsEmptyArraysForNullPayloads()
    {
        var fakeClient = new FakeS3Client();
        fakeClient.ObjectsToReturn.Add(new KeyValuePair<S3Object, byte[]?>(new S3Object { Key = "a" }, null));
        fakeClient.ObjectsToReturn.Add(new KeyValuePair<S3Object, byte[]?>(new S3Object { Key = "b" }, [0x04, 0x05]));

        var storage = new TestS3Storage(new S3Config
        {
            StorageBucket = "bucket",
            Prefix = "prefix/",
            Delimiter = "/",
            SkipEmptyObjects = false
        }, Formatting.None, fakeClient);

        var result = storage.RetrieveSerializedForTest("case\\b");

        Assert.Multiple(() =>
        {
            Assert.That(fakeClient.RetrievalRequest, Is.EqualTo(("bucket", "prefix/case_b/", "/", false)));
            Assert.That(result, Has.Count.EqualTo(2));
            Assert.That(result[0], Is.Empty);
            Assert.That(result[1], Is.EqualTo(new byte[] { 0x04, 0x05 }));
            Assert.That(fakeClient.Disposed, Is.True);
        });
    }
}
