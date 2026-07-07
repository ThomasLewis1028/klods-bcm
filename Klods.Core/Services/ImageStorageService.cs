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
                config["MINIO_ROOT_USER"]     ?? throw new InvalidOperationException("MINIO_ROOT_USER is not configured."),
                config["MINIO_ROOT_PASSWORD"] ?? throw new InvalidOperationException("MINIO_ROOT_PASSWORD is not configured."))
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

    // Cacheable hosts serve static CDN images with stable paths — a query string never changes which
    // image comes back, so an attacker appending junk query params (?a=1, ?a=2, ...) to a real image
    // URL can't be used to force unbounded distinct cache entries; only the path (not the query
    // string) feeds the cache key.
    private const long MaxImageBytes = 15L * 1024 * 1024;

    /// <summary>
    /// Read-through cache: returns the bytes for <paramref name="sourceUrl"/>, fetching from the source
    /// and storing in MinIO on the first request, then serving from MinIO thereafter. Returns null if the
    /// host isn't cacheable, the fetch fails, or the image exceeds <see cref="MaxImageBytes"/>.
    /// </summary>
    public async Task<(byte[] Bytes, string ContentType)?> GetThroughCacheAsync(string sourceUrl, CancellationToken ct = default)
    {
        if (!IsCacheableImageHost(sourceUrl)) return null;

        var basePath = sourceUrl.Split('?')[0];
        var ext = Path.GetExtension(basePath);
        if (string.IsNullOrEmpty(ext)) ext = ".jpg";
        var key = $"cache/{Sha256Hex(basePath)}{ext}";
        var contentType = GuessContentType(sourceUrl);

        var cached = await TryGetObjectAsync(key, ct);
        if (cached != null) return (cached, contentType);

        byte[] bytes;
        try
        {
            using var response = await _http.GetAsync(sourceUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            if (response.Content.Headers.ContentLength > MaxImageBytes)
            {
                _logger.LogWarning("Read-through fetch rejected for {Url}: reported size exceeds cap", sourceUrl);
                return null;
            }

            await using var responseStream = await response.Content.ReadAsStreamAsync(ct);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            long total = 0;
            int read;
            while ((read = await responseStream.ReadAsync(chunk, ct)) > 0)
            {
                total += read;
                if (total > MaxImageBytes)
                {
                    _logger.LogWarning("Read-through fetch aborted for {Url}: exceeded size cap", sourceUrl);
                    return null;
                }
                await buffer.WriteAsync(chunk.AsMemory(0, read), ct);
            }
            bytes = buffer.ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogWarning("Read-through fetch failed for {Url}: {Message}", sourceUrl, ex.Message);
            return null;
        }

        try
        {
            using var stream = new MemoryStream(bytes);
            await _minio.PutObjectAsync(new PutObjectArgs()
                .WithBucket(_bucket)
                .WithObject(key)
                .WithStreamData(stream)
                .WithObjectSize(bytes.Length)
                .WithContentType(contentType), ct);
        }
        catch (Exception ex)
        {
            // Serving the freshly-fetched bytes matters more than caching them; a failed
            // write just means the next request for this image fetches it again.
            _logger.LogWarning("Cache write failed for {Url}: {Message}", sourceUrl, ex.Message);
        }

        return (bytes, contentType);
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
