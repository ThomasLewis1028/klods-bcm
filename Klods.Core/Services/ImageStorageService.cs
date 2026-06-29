using System.Security.Cryptography;
using System.Text;
using Minio;
using Minio.DataModel.Args;

namespace Klods.Services;

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

    // Hosts whose images we lazily pull into MinIO (read-through). Other http URLs (e.g. avatars) pass through.
    private static readonly string[] CacheableHosts = ["cdn.rebrickable.com", "rebrickable.com"];

    public static bool IsCacheableImageHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) &&
        CacheableHosts.Any(h =>
            uri.Host.Equals(h, StringComparison.OrdinalIgnoreCase) ||
            uri.Host.EndsWith("." + h, StringComparison.OrdinalIgnoreCase));

    public string? ResolveUrl(string? stored)
    {
        if (stored is null) return null;
        if (!stored.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return string.IsNullOrEmpty(_publicEndpoint) ? $"/media/{stored}" : $"{_publicEndpoint.TrimEnd('/')}/{stored}";
        // Rebrickable CDN images are cached on first view via the /img read-through endpoint.
        return IsCacheableImageHost(stored) ? $"/img?u={Uri.EscapeDataString(stored)}" : stored;
    }

    /// <summary>
    /// Read-through cache: returns the bytes for <paramref name="sourceUrl"/>, fetching from the source
    /// and storing in MinIO on the first request, then serving from MinIO thereafter. Returns null if the
    /// host isn't cacheable or the fetch fails.
    /// </summary>
    public async Task<(byte[] Bytes, string ContentType)?> GetThroughCacheAsync(string sourceUrl, CancellationToken ct = default)
    {
        if (!IsCacheableImageHost(sourceUrl)) return null;

        var ext = Path.GetExtension(sourceUrl.Split('?')[0]);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        var key = $"cache/{Sha256Hex(sourceUrl)}{ext}";
        var contentType = GuessContentType(sourceUrl);

        var cached = await TryGetObjectAsync(key, ct);
        if (cached != null) return (cached, contentType);

        try
        {
            var bytes = await _http.GetByteArrayAsync(sourceUrl, ct);
            using var stream = new MemoryStream(bytes);
            await _minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(key)
                .WithStreamData(stream)
                .WithObjectSize(bytes.Length)
                .WithContentType(contentType), ct);
            return (bytes, contentType);
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Read-through cache failed for {Url}: {Message}", sourceUrl, ex.Message);
            return null;
        }
    }

    private async Task<byte[]?> TryGetObjectAsync(string key, CancellationToken ct)
    {
        try
        {
            using var ms = new MemoryStream();
            await _minio.GetObjectAsync(new GetObjectArgs()
                .WithBucket(_bucket)
                .WithObject(key)
                .WithCallbackStream(s => s.CopyTo(ms)), ct);
            return ms.ToArray();
        }
        catch
        {
            return null; // not cached yet
        }
    }

    private static string Sha256Hex(string input) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input))).ToLowerInvariant();

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
