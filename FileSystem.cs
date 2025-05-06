public class FileSystemDownload
{
    private static HttpClient fileDownloads;

    static FileSystemDownload()
    {
        fileDownloads = new HttpClient() { Timeout = TimeSpan.FromMinutes(15) };
    }

    public static async Task<CopyResult> DownloadToFileSystem(string fullPath, SharePointObject file, object fileLock, string logFilePath)
    {
        CopyResult result = new CopyResult();
        if (!File.Exists(fullPath) && File.GetLastWriteTime(fullPath) != file.lastModifiedDateTime)
        {
            using (HttpResponseMessage response = await fileDownloads.GetAsync(file.downloadUrl))
            {
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    using (Stream contentStream = await response.Content.ReadAsStreamAsync(),
                    fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                    {
                        await contentStream.CopyToAsync(fileStream);
                    }
                }
                else
                {
                    lock (fileLock)
                    {
                        File.AppendAllText(logFilePath, $"Paused: API Throttled on Download, try again in 2 minutes: {response.StatusCode} {file.name} attempted to {fullPath}{Environment.NewLine}");
                    }
                    await Task.Delay(120000);
                    using (HttpResponseMessage attempt2 = await fileDownloads.GetAsync(file.downloadUrl))
                    {

                        attempt2.EnsureSuccessStatusCode();
                        using (Stream contentStream = await attempt2.Content.ReadAsStreamAsync(),
                        fileStream = new FileStream(fullPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, true))
                        {
                            await contentStream.CopyToAsync(fileStream);
                        }
                    }
                }
                result.sourcePath = file.downloadUrl;
                result.targetPath = fullPath;
                result.result = "OK";
                lock (fileLock)
                {
                    File.AppendAllText(logFilePath, $"Downloaded Success: {file.name} downloaded to {fullPath}{Environment.NewLine}");
                }
            }
        }
        else
        {
            result.sourcePath = file.downloadUrl;
            result.targetPath = fullPath;
            result.result = "FileExists";
            lock (fileLock)
            {
                File.AppendAllText(logFilePath, $"File Existed: {file.name} existed at {fullPath}{Environment.NewLine}");
            }
        }
        await Task.Delay(100);
        File.SetCreationTime(fullPath, file.createdDateTime);
        File.SetLastWriteTime(fullPath, file.lastModifiedDateTime);
        return result;
    }
}