public class AzureBlobDownload
{
    private readonly static HttpClient blobClient;
    private readonly static SharePointConfig config;

    static AzureBlobDownload()
    {
        config = SharePointConfig.GetConfig();
        blobClient = new HttpClient() { BaseAddress = new Uri(config.blobEndpointUrl), Timeout = TimeSpan.FromMinutes(10) };
    }


    public static async Task<CopyResult> UploadBlob(string fullPath, SharePointObject file, object fileLock, string logFilePath, string token)
    {
        string requestUri = $"/{config.blobContainer}{fullPath}";
        string dateInRfc1123Format = DateTime.UtcNow.ToString("R");
        string blobType = "BlockBlob";
        CopyResult result = new CopyResult();
        try
        {
            HttpRequestMessage checkOnBlob = new HttpRequestMessage(HttpMethod.Head, requestUri);
            checkOnBlob.Headers.Add("x-ms-date", dateInRfc1123Format);
            checkOnBlob.Headers.Add("x-ms-version", "2020-10-02");
            checkOnBlob.Headers.Add("Authorization", $"Bearer {token}");
            string lastModified = "";
            try
            {
                HttpResponseMessage metaCheck = await blobClient.SendAsync(checkOnBlob);
                lastModified = metaCheck.Headers.FirstOrDefault(x => x.Key == "x-ms-meta-lastmodifieddatetime").Value.FirstOrDefault("");
            }
            catch
            {
            }
            if (lastModified != file.lastModifiedDateTime.ToString())
            {
                using (HttpResponseMessage response = await blobClient.GetAsync(file.downloadUrl))
                {
                    response.EnsureSuccessStatusCode();
                    HttpRequestMessage request = new HttpRequestMessage(HttpMethod.Put, requestUri);
                    request.Content = new StreamContent(await response.Content.ReadAsStreamAsync());
                    request.Headers.Add("x-ms-date", dateInRfc1123Format);
                    request.Headers.Add("x-ms-version", "2020-10-02");
                    request.Headers.Add("x-ms-blob-type", blobType);
                    request.Headers.Add("Authorization", $"Bearer {token}");
                    request.Headers.Add("x-ms-meta-createdate", file.createdDateTime.ToString());
                    request.Headers.Add("x-ms-meta-lastmodifieddatetime", file.lastModifiedDateTime.ToString());
                    HttpResponseMessage blobResponse = await blobClient.SendAsync(request);
                    blobResponse.EnsureSuccessStatusCode();
                }
                result.sourcePath = file.downloadUrl;
                result.targetPath = $"{config.blobEndpointUrl}{config.blobContainer}{fullPath}";
                result.result = "OK";
                lock (fileLock)
                {
                    File.AppendAllText(logFilePath, $"Upload Blob Success: {file.name} uploaded to {config.blobEndpointUrl}{config.blobContainer}{fullPath}{Environment.NewLine}");
                }
            }
            else
            {
                result.sourcePath = file.downloadUrl;
                result.targetPath = $"{config.blobEndpointUrl}{config.blobContainer}{fullPath}";
                result.result = "Exists";
                lock (fileLock)
                {
                    File.AppendAllText(logFilePath, $"Blob Exists: {file.name} exists at {config.blobEndpointUrl}{config.blobContainer}{fullPath}{Environment.NewLine}");
                }
            }
        }
        catch
        {
            result.sourcePath = file.downloadUrl;
            result.targetPath = $"{config.blobEndpointUrl}{config.blobContainer}{fullPath}";
            result.result = "FAILED";
            lock (fileLock)
            {
                File.AppendAllText(logFilePath, $"Upload Blob Failure: {file.name} uploaded to {config.blobEndpointUrl}{config.blobContainer}{fullPath}{Environment.NewLine}");
            }
        }

        return result;
    }
}