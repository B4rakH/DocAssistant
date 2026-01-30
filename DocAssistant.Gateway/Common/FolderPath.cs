namespace DocAssistant.Gateway.Common
{
    [Obsolete("Local file storage is deprecated. Use IMinioService for object storage instead.")]
    public static class FolderPath
    {
        public static string GetUploadsFolder()
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploaded_files");

            if (!Directory.Exists(uploadsFolder)) Directory.CreateDirectory(uploadsFolder);
            
            return uploadsFolder;
        }
    }
}
