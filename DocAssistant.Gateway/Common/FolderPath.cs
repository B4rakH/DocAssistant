namespace DocAssistant.Gateway.Common
{
    public static class FolderPath
    {
        public static string GetUploadsFolder()
        {
            var uploadsFolder = Path.Combine(Directory.GetCurrentDirectory(), "uploaded_files");
            if (!Directory.Exists(uploadsFolder))
            {
                Directory.CreateDirectory(uploadsFolder);
            }
            return uploadsFolder;
        }
    }
}
