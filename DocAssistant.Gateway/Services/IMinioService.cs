namespace DocAssistant.Gateway.Services
{
    public interface IMinioService
    {
        /// <summary>
        /// Uploads a file to MinIO bucket
        /// </summary>
        /// <param name="file">The file to upload</param>
        /// <param name="objectName">The name to store the file as (without path prefix)</param>
        /// <returns>The full object path in MinIO</returns>
        Task<string> UploadFileAsync(IFormFile file, string objectName);

        /// <summary>
        /// Deletes a file from MinIO bucket
        /// </summary>
        /// <param name="objectName">The object name/path to delete</param>
        Task DeleteFileAsync(string objectName);

        /// <summary>
        /// Deletes multiple files from MinIO bucket
        /// </summary>
        /// <param name="objectNames">List of object names/paths to delete</param>
        Task DeleteFilesAsync(List<string> objectNames);

        /// <summary>
        /// Gets a presigned URL for downloading a file
        /// </summary>
        /// <param name="objectName">The object name/path</param>
        /// <param name="expiryInSeconds">URL expiry time in seconds (default: 3600)</param>
        /// <returns>Presigned URL</returns>
        Task<string> GetPresignedUrlAsync(string objectName, int expiryInSeconds = 3600);

        /// <summary>
        /// Ensures the bucket exists, creates it if it doesn't
        /// </summary>
        Task EnsureBucketExistsAsync();
    }
}
