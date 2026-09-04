using System.Text;
using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Configuration;

namespace Gram.Blazor.Shared;

public interface IObjectStorageService
{
    string ProviderName { get; }
    bool IsCloudConfigured { get; }
    string BucketName { get; }

    Task<string> UploadAsync(string key, Stream stream, string contentType = "application/octet-stream", IDictionary<string, string>? metadata = null);
    Task<Stream?> DownloadAsync(string key);
    Task<string> GetPreSignedUrlAsync(string key, TimeSpan expiry);
    Task<bool> DeleteAsync(string key);
    Task<bool> ExistsAsync(string key);
    Task<bool> TestConnectionAsync();
}

public sealed class S3StorageOptions
{
    public string BucketName { get; set; } = "gram-attachments";
    public string Region { get; set; } = "us-east-1";
    public string? AccessKey { get; set; }
    public string? SecretKey { get; set; }
    public string? ServiceUrl { get; set; }
    public bool ForcePathStyle { get; set; } = false;

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(AccessKey) && !string.IsNullOrWhiteSpace(SecretKey);
}

public sealed class S3ObjectStorageService : IObjectStorageService
{
    private readonly IAmazonS3? _s3Client;
    private readonly S3StorageOptions _options;
    private readonly string _fallbackLocalDir;

    public string ProviderName =>
        !string.IsNullOrWhiteSpace(_options.ServiceUrl)
            ? (_options.ServiceUrl.Contains("r2.cloudflarestorage.com", StringComparison.OrdinalIgnoreCase)
                ? "Cloudflare R2 (S3-Compatible)"
                : "S3-Compatible Object Store")
            : "AWS S3";

    public bool IsCloudConfigured => _s3Client != null && _options.IsConfigured;
    public string BucketName => _options.BucketName;

    public S3ObjectStorageService(IConfiguration configuration, StorageService storageService)
    {
        _fallbackLocalDir = Path.Combine(storageService.DataDirectory, "storage_blob_fallback");
        Directory.CreateDirectory(_fallbackLocalDir);

        _options = new S3StorageOptions
        {
            BucketName = Environment.GetEnvironmentVariable("S3_BUCKET_NAME")
                         ?? Environment.GetEnvironmentVariable("AWS_S3_BUCKET")
                         ?? configuration["S3:BucketName"]
                         ?? "gram-attachments",
            Region = Environment.GetEnvironmentVariable("AWS_REGION")
                     ?? Environment.GetEnvironmentVariable("S3_REGION")
                     ?? configuration["S3:Region"]
                     ?? "us-east-1",
            AccessKey = Environment.GetEnvironmentVariable("AWS_ACCESS_KEY_ID")
                        ?? Environment.GetEnvironmentVariable("S3_ACCESS_KEY")
                        ?? configuration["S3:AccessKey"],
            SecretKey = Environment.GetEnvironmentVariable("AWS_SECRET_ACCESS_KEY")
                        ?? Environment.GetEnvironmentVariable("S3_SECRET_KEY")
                        ?? configuration["S3:SecretKey"],
            ServiceUrl = Environment.GetEnvironmentVariable("S3_ENDPOINT")
                         ?? Environment.GetEnvironmentVariable("AWS_ENDPOINT_URL_S3")
                         ?? configuration["S3:ServiceUrl"],
            ForcePathStyle = bool.TryParse(Environment.GetEnvironmentVariable("S3_FORCE_PATH_STYLE") ?? configuration["S3:ForcePathStyle"], out var fps) && fps
        };

        if (_options.IsConfigured)
        {
            var credentials = new BasicAWSCredentials(_options.AccessKey, _options.SecretKey);
            var s3Config = new AmazonS3Config
            {
                ForcePathStyle = _options.ForcePathStyle
            };

            if (!string.IsNullOrWhiteSpace(_options.ServiceUrl))
            {
                s3Config.ServiceURL = _options.ServiceUrl;
                if (!string.IsNullOrWhiteSpace(_options.Region))
                {
                    s3Config.AuthenticationRegion = _options.Region;
                }
            }
            else
            {
                s3Config.RegionEndpoint = RegionEndpoint.GetBySystemName(_options.Region);
            }

            _s3Client = new AmazonS3Client(credentials, s3Config);
        }
    }

    public async Task<string> UploadAsync(string key, Stream stream, string contentType = "application/octet-stream", IDictionary<string, string>? metadata = null)
    {
        var sanitizedKey = SanitizeKey(key);

        if (IsCloudConfigured && _s3Client != null)
        {
            var request = new PutObjectRequest
            {
                BucketName = _options.BucketName,
                Key = sanitizedKey,
                InputStream = stream,
                ContentType = contentType,
                AutoCloseStream = false
            };

            if (metadata != null)
            {
                foreach (var (k, v) in metadata)
                {
                    request.Metadata.Add(k, v);
                }
            }

            // Standard server-side AES256 for AWS S3
            if (string.IsNullOrWhiteSpace(_options.ServiceUrl))
            {
                request.ServerSideEncryptionMethod = ServerSideEncryptionMethod.AES256;
            }

            await _s3Client.PutObjectAsync(request);
            return $"s3://{_options.BucketName}/{sanitizedKey}";
        }

        // Local Fallback Storage
        var localPath = Path.Combine(_fallbackLocalDir, sanitizedKey.Replace('/', '_'));
        await using var fs = new FileStream(localPath, FileMode.Create, FileAccess.Write, FileShare.None);
        await stream.CopyToAsync(fs);
        return $"file://{localPath}";
    }

    public async Task<Stream?> DownloadAsync(string key)
    {
        var sanitizedKey = SanitizeKey(key);

        if (IsCloudConfigured && _s3Client != null)
        {
            try
            {
                var response = await _s3Client.GetObjectAsync(new GetObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = sanitizedKey
                });

                var memoryStream = new MemoryStream();
                await response.ResponseStream.CopyToAsync(memoryStream);
                memoryStream.Position = 0;
                return memoryStream;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return null;
            }
        }

        var localPath = Path.Combine(_fallbackLocalDir, sanitizedKey.Replace('/', '_'));
        if (File.Exists(localPath))
        {
            return File.OpenRead(localPath);
        }

        return null;
    }

    public Task<string> GetPreSignedUrlAsync(string key, TimeSpan expiry)
    {
        var sanitizedKey = SanitizeKey(key);

        if (IsCloudConfigured && _s3Client != null)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _options.BucketName,
                Key = sanitizedKey,
                Expires = DateTime.UtcNow.Add(expiry),
                Verb = HttpVerb.GET
            };

            var url = _s3Client.GetPreSignedURL(request);
            return Task.FromResult(url);
        }

        return Task.FromResult($"/api/attachments/direct/{sanitizedKey}");
    }

    public async Task<bool> DeleteAsync(string key)
    {
        var sanitizedKey = SanitizeKey(key);

        if (IsCloudConfigured && _s3Client != null)
        {
            try
            {
                await _s3Client.DeleteObjectAsync(new DeleteObjectRequest
                {
                    BucketName = _options.BucketName,
                    Key = sanitizedKey
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        var localPath = Path.Combine(_fallbackLocalDir, sanitizedKey.Replace('/', '_'));
        if (File.Exists(localPath))
        {
            File.Delete(localPath);
            return true;
        }

        return false;
    }

    public async Task<bool> ExistsAsync(string key)
    {
        var sanitizedKey = SanitizeKey(key);

        if (IsCloudConfigured && _s3Client != null)
        {
            try
            {
                var meta = await _s3Client.GetObjectMetadataAsync(new GetObjectMetadataRequest
                {
                    BucketName = _options.BucketName,
                    Key = sanitizedKey
                });
                return meta.HttpStatusCode == System.Net.HttpStatusCode.OK;
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[S3 Object Storage] ExistsAsync error for '{sanitizedKey}': {ex.Message}");
                return false;
            }
        }

        var localPath = Path.Combine(_fallbackLocalDir, sanitizedKey.Replace('/', '_'));
        return File.Exists(localPath);
    }

    public async Task<bool> TestConnectionAsync()
    {
        if (!IsCloudConfigured || _s3Client == null) return false;

        try
        {
            var response = await _s3Client.ListBucketsAsync();
            return response.Buckets != null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[S3 Object Storage] TestConnection notice: {ex.Message}");
            return false;
        }
    }

    private static string SanitizeKey(string key)
    {
        return key.TrimStart('/').Replace('\\', '/');
    }
}
