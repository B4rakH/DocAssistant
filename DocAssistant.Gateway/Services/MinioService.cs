using Minio;
using Minio.DataModel.Args;

namespace DocAssistant.Gateway.Services
{
    public class MinIOService : IMinIOService
    {
        private readonly IMinioClient _minioClient;
        private readonly string _bucketName;
        private readonly ILogger<MinIOService> _logger;

        public MinIOService(IConfiguration configuration, ILogger<MinIOService> logger)
        {
            _logger = logger;

            var endpoint = configuration["MINIO_ENDPOINT"] 
                ?? throw new InvalidOperationException("MINIO_ENDPOINT is not configured");
            var accessKey = configuration["MINIO_ACCESS_KEY"] 
                ?? throw new InvalidOperationException("MINIO_ACCESS_KEY is not configured");
            var secretKey = configuration["MINIO_SECRET_KEY"] 
                ?? throw new InvalidOperationException("MINIO_SECRET_KEY is not configured");
            _bucketName = configuration["MINIO_BUCKET_NAME"] 
                ?? throw new InvalidOperationException("MINIO_BUCKET_NAME is not configured");
            var useSSL = bool.Parse(configuration["MINIO_USE_SSL"] ?? "false");

            _minioClient = new MinioClient()
                .WithEndpoint(endpoint)
                .WithCredentials(accessKey, secretKey)
                .WithSSL(useSSL)
                .Build();
        }

        public async Task EnsureBucketExistsAsync()
        {
            try
            {
                var beArgs = new BucketExistsArgs()
                    .WithBucket(_bucketName);

                bool found = await _minioClient.BucketExistsAsync(beArgs);

                if (!found)
                {
                    var mbArgs = new MakeBucketArgs()
                        .WithBucket(_bucketName);
                    await _minioClient.MakeBucketAsync(mbArgs);
                    
                    _logger.LogInformation("Created MinIO bucket: {BucketName}", _bucketName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error ensuring bucket exists: {BucketName}", _bucketName);
                throw;
            }
        }

        public async Task<string> UploadFileAsync(IFormFile file, string objectName)
        {
            try
            {
                await using var stream = file.OpenReadStream();

                var putObjectArgs = new PutObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(objectName)
                    .WithStreamData(stream)
                    .WithObjectSize(file.Length)
                    .WithContentType(file.ContentType);

                await _minioClient.PutObjectAsync(putObjectArgs);

                _logger.LogInformation("Uploaded file to MinIO bucket '{BucketName}': {ObjectName}", _bucketName, objectName);

                return objectName;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to upload file to MinIO: {ObjectName}", objectName);
                throw;
            }
        }

        public async Task DeleteFileAsync(string objectName)
        {
            try
            {
                var removeObjectArgs = new RemoveObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(objectName);

                await _minioClient.RemoveObjectAsync(removeObjectArgs);

                _logger.LogInformation("Deleted file from MinIO: {ObjectName}", objectName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete file from MinIO: {ObjectName}", objectName);
                throw;
            }
        }

        public async Task DeleteFilesAsync(List<string> objectNames)
        {
            try
            {
                var removeObjectsArgs = new RemoveObjectsArgs()
                    .WithBucket(_bucketName)
                    .WithObjects(objectNames);

                var errors = await _minioClient.RemoveObjectsAsync(removeObjectsArgs);
                
                if (errors != null && errors.Count > 0)
                {
                    foreach (var error in errors)
                    {
                        _logger.LogError("Error deleting object {ObjectName}: {Message}", 
                            error.Key, error.Message);
                    }
                }

                _logger.LogInformation("Deleted {Count} files from MinIO", objectNames.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to delete files from MinIO");
                throw;
            }
        }

        public async Task<string> GetPresignedUrlAsync(string objectName, int expiryInSeconds = 3600)
        {
            try
            {
                var presignedGetObjectArgs = new PresignedGetObjectArgs()
                    .WithBucket(_bucketName)
                    .WithObject(objectName)
                    .WithExpiry(expiryInSeconds);

                string url = await _minioClient.PresignedGetObjectAsync(presignedGetObjectArgs);

                return url;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to generate presigned URL for: {ObjectName}", objectName);
                throw;
            }
        }
    }
}
