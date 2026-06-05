using Minio;
using Minio.DataModel.Args;

namespace LEGO_Inventory.Services;

public class ImageStorageService
{
    private readonly IMinioClient _minio;
    private readonly HttpClient _http;
    private readonly string _bucket;
    private readonly string? _publicEndpoint;
    private readonly ILogger<ImageStorageService> _logger;

    public ImageStorageService(IHttpClientFactory httpClientFactory, IConfiguration config, ILogger<ImageStorageService> logger)
    {
        var endpoint  = config["MINIO_ENDPOINT"] ?? "http://minio:9000";
        _bucket           = config["MINIO_BUCKET"] ?? "lego-images";
        _publicEndpoint   = config["MINIO_PUBLIC_ENDPOINT"];

        var uri = new Uri(endpoint);
        _minio = new MinioClient()
            .WithEndpoint(uri.Host, uri.Port)
            .WithCredentials(
                config["MINIO_ROOT_USER"]     ?? "minioadmin",
                config["MINIO_ROOT_PASSWORD"] ?? "minioadmin")
            .WithSSL(uri.Scheme == "https")
            .Build();

        _http   = httpClientFactory.CreateClient();
        _logger = logger;
    }

    public async Task InitializeAsync()
    {
        var exists = await _minio.BucketExistsAsync(new BucketExistsArgs().WithBucket(_bucket));
        if (!exists)
        {
            await _minio.MakeBucketAsync(new MakeBucketArgs().WithBucket(_bucket));
            _logger.LogInformation("Created MinIO bucket '{Bucket}'", _bucket);
        }

        // Allow public read on all objects in this bucket
        var policy = $$"""
            {
              "Version": "2012-10-17",
              "Statement": [{
                "Effect": "Allow",
                "Principal": {"AWS": ["*"]},
                "Action": ["s3:GetObject"],
                "Resource": ["arn:aws:s3:::{{_bucket}}/*"]
              }]
            }
            """;
        await _minio.SetPolicyAsync(new SetPolicyArgs().WithBucket(_bucket).WithPolicy(policy));
    }

    /// <summary>
    /// Downloads the image at <paramref name="sourceUrl"/> and stores it in MinIO under
    /// <paramref name="objectKey"/>. Returns the object key slug, or the original URL on failure.
    /// Use <see cref="ResolveUrl"/> to convert the returned slug to a browser-accessible URL.
    /// </summary>
    public async Task<string?> StoreImageAsync(string? sourceUrl, string objectKey)
    {
        if (string.IsNullOrEmpty(sourceUrl)) return null;

        try
        {
            var bytes = await _http.GetByteArrayAsync(sourceUrl);
            var contentType = GuessContentType(sourceUrl);

            using var stream = new MemoryStream(bytes);
            await _minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(objectKey)
                .WithStreamData(stream)
                .WithObjectSize(bytes.Length)
                .WithContentType(contentType));

            return $"{_bucket}/{objectKey}";
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Failed to store image '{Key}' from {Url}: {Message}", objectKey, sourceUrl, ex.Message);
            return sourceUrl;
        }
    }

    public string? ResolveUrl(string? stored) =>
        stored is null ? null :
        stored.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? stored :
        string.IsNullOrEmpty(_publicEndpoint)
            ? $"/media/{stored}"
            : $"{_publicEndpoint.TrimEnd('/')}/{stored}";

    private static string GuessContentType(string url)
    {
        var ext = Path.GetExtension(url.Split('?')[0]).ToLowerInvariant();
        return ext switch
        {
            ".png"  => "image/png",
            ".gif"  => "image/gif",
            ".webp" => "image/webp",
            _       => "image/jpeg"
        };
    }
}
