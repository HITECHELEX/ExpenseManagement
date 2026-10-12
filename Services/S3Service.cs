using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Amazon.S3.Transfer;

namespace ExpenseManagement.Services
{
    /// <summary>
    /// Handles all S3 operations: upload, presigned-URL generation, download, and delete.
    /// Receipts are stored as  receipts/{guid}.jpg  or  receipts/{guid}.pdf
    /// Only the key is persisted in the database — never a full URL.
    /// </summary>
    public class S3Service
    {
        private readonly string _bucketName;
        private readonly AmazonS3Client _client;

        public S3Service(IConfiguration config)
        {
            _bucketName = config["AWS:BucketName"]
                ?? throw new InvalidOperationException("AWS:BucketName is not configured.");

            var region = config["AWS:Region"] ?? "ap-south-1";
            var s3Config = new AmazonS3Config
            {
                // Use explicit ServiceURL to bypass endpoint resolution bugs in AWSSDK v4.x.
                // This guarantees requests go to the correct regional endpoint regardless
                // of SDK defaults (which often fall back to us-east-1).
                ServiceURL      = $"https://s3.{region}.amazonaws.com",
                ForcePathStyle  = true,
                AuthenticationRegion = region
            };
            _client = new AmazonS3Client(
                config["AWS:AccessKey"],
                config["AWS:SecretKey"],
                s3Config
            );
        }

        // ── Upload ────────────────────────────────────────────────────────────
        /// <summary>
        /// Uploads a stream to S3 and returns the stored key (e.g. "receipts/abc.jpg").
        /// </summary>
        public async Task<string> UploadAsync(Stream stream, string key, string contentType)
        {
            var request = new TransferUtilityUploadRequest
            {
                InputStream  = stream,
                BucketName   = _bucketName,
                Key          = key,
                ContentType  = contentType,
                // Bucket is private — do NOT set CannedACL to PublicRead
            };

            using var transferUtility = new TransferUtility(_client);
            await transferUtility.UploadAsync(request);
            return key;
        }

        // ── Presigned URL ────────────────────────────────────────────────────
        /// <summary>
        /// Generates a time-limited presigned URL for viewing a private S3 object.
        /// Default expiry is 60 minutes.
        /// </summary>
        public string GetPresignedUrl(string key, int expiryMinutes = 60)
        {
            var request = new GetPreSignedUrlRequest
            {
                BucketName = _bucketName,
                Key        = key,
                Expires    = DateTime.UtcNow.AddMinutes(expiryMinutes),
                Verb       = HttpVerb.GET,
            };
            return _client.GetPreSignedURL(request);
        }

        // ── Download Stream ──────────────────────────────────────────────────
        /// <summary>
        /// Downloads an S3 object and returns its content stream.
        /// Caller is responsible for disposing the stream.
        /// </summary>
        public async Task<(Stream stream, string contentType, string extension)> DownloadAsync(string key)
        {
            var response = await _client.GetObjectAsync(_bucketName, key);
            var ext = Path.GetExtension(key).ToLowerInvariant();
            return (response.ResponseStream, response.Headers.ContentType, ext);
        }

        // ── Delete ───────────────────────────────────────────────────────────
        /// <summary>
        /// Deletes an object from S3. Safe to call even if the key does not exist.
        /// </summary>
        public async Task DeleteAsync(string key)
        {
            if (string.IsNullOrWhiteSpace(key)) return;
            try
            {
                await _client.DeleteObjectAsync(_bucketName, key);
            }
            catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NoContent
                                             || ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // Object already gone — silently ignore
            }
        }
    }
}
