using System.Text;
using Gram.Blazor.Shared;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace Gram.Blazor.UnitTests;

public class ObjectStorageServiceTests
{
    [Fact]
    public async Task LocalFallback_UploadAndDownload_SuccessfullySavesAndReadsStream()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "gram-s3-test-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempDir);
        Environment.SetEnvironmentVariable("GRAM_DATA_DIR", tempDir);

        try
        {
            var config = new ConfigurationBuilder().Build();
            var storage = new StorageService();
            var s3Service = new S3ObjectStorageService(config, storage);

            var testContent = "Hello from S3/Object Storage abstraction!";
            var testBytes = Encoding.UTF8.GetBytes(testContent);
            var key = "conversations/conv-123/test-report.pdf";

            using var uploadStream = new MemoryStream(testBytes);
            var uri = await s3Service.UploadAsync(key, uploadStream, "application/pdf");

            Assert.NotNull(uri);
            Assert.True(await s3Service.ExistsAsync(key));

            var downloadStream = await s3Service.DownloadAsync(key);
            Assert.NotNull(downloadStream);

            using var reader = new StreamReader(downloadStream);
            var downloadedText = await reader.ReadToEndAsync();
            Assert.Equal(testContent, downloadedText);

            var deleted = await s3Service.DeleteAsync(key);
            Assert.True(deleted);
            Assert.False(await s3Service.ExistsAsync(key));
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, recursive: true); } catch { }
            }
        }
    }

    [Fact]
    public void ProviderName_DetectsCloudflareR2_WhenEndpointContainsR2()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["S3:AccessKey"] = "test-access-key",
            ["S3:SecretKey"] = "test-secret-key",
            ["S3:ServiceUrl"] = "https://123456789.r2.cloudflarestorage.com"
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var storage = new StorageService();
        var s3Service = new S3ObjectStorageService(config, storage);

        Assert.Equal("Cloudflare R2 (S3-Compatible)", s3Service.ProviderName);
        Assert.True(s3Service.IsCloudConfigured);
    }

    [Fact]
    public void ProviderName_DetectsAwsS3_WhenNoCustomEndpoint()
    {
        var inMemory = new Dictionary<string, string?>
        {
            ["S3:AccessKey"] = "test-aws-key",
            ["S3:SecretKey"] = "test-aws-secret",
            ["S3:Region"] = "us-west-2"
        };

        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemory).Build();
        var storage = new StorageService();
        var s3Service = new S3ObjectStorageService(config, storage);

        Assert.Equal("AWS S3", s3Service.ProviderName);
        Assert.True(s3Service.IsCloudConfigured);
    }
}
