using System.Collections;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.CompilerServices;
using System.Reflection;

public class SharePoint
{
    private static HttpClient sharePointClient;
    private static HttpClient tokenEndpoint;
    private static FormUrlEncodedContent blobTokenBody;
    private static string tokenEndpointUrl;
    private static FormUrlEncodedContent tokenBody;
    private static readonly object fileLock = new object();
    private static string logFilePath;
    readonly private static SharePointConfig sharePointConfig;
    private static SemaphoreSlim semaphore = new SemaphoreSlim(10);

    static SharePoint()
    {
        sharePointConfig = SharePointConfig.GetConfig();
        sharePointClient = new HttpClient() { BaseAddress = new Uri("https://graph.microsoft.com/"), Timeout = TimeSpan.FromMinutes(5) };
        tokenEndpoint = new HttpClient() { BaseAddress = new Uri(sharePointConfig.entra.tokenBaseAddress) };
        tokenEndpointUrl = $"/{sharePointConfig.entra.tenantId}/oauth2/v2.0/token";
        tokenBody = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", $"{sharePointConfig.entra.client_id}"),
            new KeyValuePair<string, string>("client_secret", $"{sharePointConfig.entra.client_secret}"),
            new KeyValuePair<string, string>("scope", "https://graph.microsoft.com/.default"),
        });
        logFilePath = Directory.GetCurrentDirectory() + $"/{sharePointConfig.logFileName}";
        if (File.Exists(logFilePath))
        {
            File.Delete(logFilePath);
        }
    }

    private static TokenResponse GetToken()
    {
        HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, tokenEndpointUrl);
        message.Headers.Add("ContentType", "application/x-www-form-urlencoded");
        message.Content = tokenBody;
        TokenResponse token;
        HttpContent responseContent = tokenEndpoint.SendAsync(message).Result.Content;
        token = JsonSerializer.Deserialize<TokenResponse>(responseContent.ReadAsStringAsync().Result);
        return token;
    }

    private static TokenResponse GetTokenBlob()
    {
        HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Post, tokenEndpointUrl);
        blobTokenBody = new FormUrlEncodedContent(new[]
        {
            new KeyValuePair<string, string>("grant_type", "client_credentials"),
            new KeyValuePair<string, string>("client_id", $"{sharePointConfig.entra.client_id}"),
            new KeyValuePair<string, string>("client_secret", $"{sharePointConfig.entra.client_secret}"),
            new KeyValuePair<string, string>("scope", $"{sharePointConfig.blobBaseAddress}.default"),
        });
        message.Headers.Add("ContentType", "application/x-www-form-urlencoded");
        message.Content = blobTokenBody;
        TokenResponse token;
        HttpContent responseContent = tokenEndpoint.SendAsync(message).Result.Content;
        token = JsonSerializer.Deserialize<TokenResponse>(responseContent.ReadAsStringAsync().Result);
        return token;
    }

    public static List<SharePointObject> GetItems(bool foldersOnly, bool recursive, bool downloadFiles)
    {
        TokenResponse token;
        try
        {
            token = GetToken();
        }
        catch
        {
            Console.WriteLine("Failed to get token");
            return new List<SharePointObject>();
        }
        string path = "/v1.0/sites/" + sharePointConfig.siteId + "/drives/" + sharePointConfig.driveId + "/root:" + sharePointConfig.sharePointBaseFolder + ":/children";
        HttpRequestMessage sharepointMessage = new HttpRequestMessage(HttpMethod.Get, path);
        sharepointMessage.Headers.Add("Authorization", "Bearer " + token.access_token);
        sharepointMessage.Headers.Add("ContentType", "application/json");
        SharePointObjectReturn sharePointResponse;
        List<SharePointObject> returnData = new List<SharePointObject>();
        HttpContent sharePointResponseContent;
        if (recursive == true)
        {
            try
            {
                ConcurrentBag<SharePointObject> allFiles = new ConcurrentBag<SharePointObject>();
                ProcessPathsInParrallel(token.access_token, path, sharePointConfig.siteId, allFiles, logFilePath, downloadFiles);
                if (foldersOnly)
                {
                    returnData = allFiles.ToList().Where(x => x.downloadUrl == null).ToList();
                }
                else
                {
                    returnData = allFiles.ToList().Where(x => x.downloadUrl != null).ToList();
                }
            }
            catch
            {
                lock (fileLock)
                {
                    File.AppendAllText(logFilePath, $"Failure: {path}{Environment.NewLine}");
                }
            }
        }
        else
        {
            try
            {
                sharePointResponseContent = sharePointClient.SendAsync(sharepointMessage).Result.Content;
                string test = sharePointResponseContent.ReadAsStringAsync().Result;
                sharePointResponse = JsonSerializer.Deserialize<SharePointObjectReturn>(sharePointResponseContent.ReadAsStringAsync().Result);
                if (foldersOnly)
                {
                    returnData = sharePointResponse.value.Where(x => x.downloadUrl == null).ToList();
                }
                else
                {
                    returnData = sharePointResponse.value.Where(x => x.downloadUrl != null).ToList();
                }
            }
            catch
            {
                lock (fileLock)
                {
                    File.AppendAllText(logFilePath, $"Failure: {path}{Environment.NewLine}");
                }
            }

        }
        return returnData;
    }

    static async Task ProcessPathsInParrallel(string token, string path, string siteid, ConcurrentBag<SharePointObject> allObjects, string logFilePath, bool downloadFiles)
    {
        List<SharePointBatchRequestBody> bodies = new List<SharePointBatchRequestBody>() { new SharePointBatchRequestBody() };
        HttpRequestMessage sharepointMessage = new HttpRequestMessage(HttpMethod.Get, path);
        sharepointMessage.Headers.Add("Authorization", "Bearer " + token);
        sharepointMessage.Headers.Add("ContentType", "application/json");
        HttpContent sharePointResponseContent;
        SharePointObjectReturn sharePointResponse;
        List<Task> tasks = new List<Task>();
        try
        {
            sharePointResponseContent = sharePointClient.SendAsync(sharepointMessage).Result.Content;
            sharePointResponse = JsonSerializer.Deserialize<SharePointObjectReturn>(sharePointResponseContent.ReadAsStringAsync().Result);
            List<SharePointObject> sharePointObjects = new List<SharePointObject>();
            sharePointObjects.AddRange(sharePointResponse.value);
            while (sharePointResponse.nextLink != null)
            {
                sharepointMessage = new HttpRequestMessage(HttpMethod.Get, sharePointResponse.nextLink);
                sharepointMessage.Headers.Add("Authorization", "Bearer " + token);
                sharepointMessage.Headers.Add("ContentType", "application/json");
                sharePointResponseContent = sharePointClient.SendAsync(sharepointMessage).Result.Content;
                sharePointObjects.AddRange(sharePointResponse.value);
            }
            ConcurrentBag<CopyResult> downloadedFiles = new ConcurrentBag<CopyResult>();
            if (downloadFiles)
            {
                tasks.Add(DownloadFiles(sharePointObjects.Where(x => x.downloadUrl != null).ToList(), logFilePath, downloadedFiles));
            }
            Task.WaitAll(tasks.ToArray());
            foreach (SharePointObject file in sharePointObjects)
            {
                allObjects.Add(file);
                lock (fileLock)
                {
                    if (file.downloadUrl != null)
                    {
                        File.AppendAllText(logFilePath, $"File added: {file.parentReference.path}/{file.name}{Environment.NewLine}");
                    }
                    else
                    {
                        File.AppendAllText(logFilePath, $"Folder: {file.parentReference.path}/{file.name}{Environment.NewLine}");
                    }
                }
            }
            int folderCount = 1;
            int bodyIndex = 0;
            foreach (SharePointObject folder in sharePointObjects.Where(x => x.downloadUrl == null))
            {
                bodies[bodyIndex].requests.Add(new SharePointBatchRequest() { id = folderCount.ToString(), method = "GET", url = $"/sites/{siteid}{folder.parentReference.path}/{folder.name}:/children" });
                folderCount++;
                if (folderCount == 11)
                {
                    bodies.Add(new SharePointBatchRequestBody());
                    bodyIndex++;
                    folderCount = 1;
                }
            }
            string batchUrl = "/v1.0/$batch";
            string bodiesJson = JsonSerializer.Serialize<List<SharePointBatchRequestBody>>(bodies);
            Parallel.ForEach(bodies, body =>
            {
                ProcessDirectory(batchUrl, token, siteid, body, allObjects, logFilePath, downloadFiles, downloadedFiles);
            });
        }
        catch
        {
            lock (fileLock)
            {
                File.AppendAllText(logFilePath, $"Failure: {path}{Environment.NewLine}");
            }
        }
    }

    static void ProcessDirectory(string rootPath, string token, string siteid, SharePointBatchRequestBody body, ConcurrentBag<SharePointObject> allObjects, string logFilePath, bool downloadFiles, ConcurrentBag<CopyResult> downloadedFiles)
    {
        HttpRequestMessage sharepointMessage = new HttpRequestMessage(HttpMethod.Post, rootPath);
        sharepointMessage.Headers.Add("Authorization", "Bearer " + token);
        sharepointMessage.Headers.Add("ContentType", "application/json");
        string testBody = JsonSerializer.Serialize<SharePointBatchRequestBody>(body);
        sharepointMessage.Content = new StringContent(JsonSerializer.Serialize<SharePointBatchRequestBody>(body), Encoding.UTF8, "application/json");
        HttpContent sharePointResponseContent;
        SharePointBatch sharePointResponse;
        try
        {
            sharePointResponseContent = sharePointClient.SendAsync(sharepointMessage).Result.Content;
            sharePointResponse = JsonSerializer.Deserialize<SharePointBatch>(sharePointResponseContent.ReadAsStringAsync().Result);
            while (sharePointResponse.responses.Where(y => y.status == 429).ToList().Count > 0)
            {
                lock (fileLock)
                {
                    File.AppendAllText(logFilePath, $"Paused: API Throttle, will try request again in 2 minutes.");
                }
                Task.Delay(120000).Wait();
                sharepointMessage = new HttpRequestMessage(HttpMethod.Post, rootPath);
                sharepointMessage.Headers.Add("Authorization", "Bearer " + token);
                sharepointMessage.Headers.Add("ContentType", "application/json");
                sharepointMessage.Content = new StringContent(JsonSerializer.Serialize<SharePointBatchRequestBody>(body), Encoding.UTF8, "application/json");
                sharePointResponseContent = sharePointClient.SendAsync(sharepointMessage).Result.Content;
                sharePointResponse = JsonSerializer.Deserialize<SharePointBatch>(sharePointResponseContent.ReadAsStringAsync().Result);
            }
            List<SharePointObject> sharePointObjects = new List<SharePointObject>();
            List<SharePointBatchRequestBody> nextLinkBodies = new List<SharePointBatchRequestBody>() { };
            int nextLinkCount = 1;
            int folderCount = 1;
            int bodyIndex = 0;
            List<Task> tasks = new List<Task>();
            foreach (SharePointBatchResponse response in sharePointResponse.responses)
            {
                sharePointObjects.AddRange(response.body.value);
                if (downloadFiles && response.body.value.Where(x => x.downloadUrl != null).ToList().Count > 0)
                {
                    List<SharePointObject> downloadObjects = response.body.value.Where(x => x.downloadUrl != null).ToList();
                    tasks.Add(DownloadFiles(downloadObjects, logFilePath, downloadedFiles));
                }
                Parallel.ForEach(response.body.value, item =>
                {
                    allObjects.Add(item);
                    lock (fileLock)
                    {
                        if (item.downloadUrl != null)
                        {
                            File.AppendAllText(logFilePath, $"File added: {item.parentReference.path}/{item.name}{Environment.NewLine}");
                        }
                        else
                        {
                            File.AppendAllText(logFilePath, $"Folder: {item.parentReference.path}/{item.name}{Environment.NewLine}");
                        }
                    }
                });
                if (response.body.nextLink != null)
                {
                    if (nextLinkBodies.Count == 0)
                    {
                        nextLinkBodies.Add(new SharePointBatchRequestBody());
                    }
                    nextLinkBodies[bodyIndex].requests.Add(new SharePointBatchRequest() { id = nextLinkCount.ToString(), method = "GET", url = response.body.nextLink });
                    nextLinkCount++;
                    if (nextLinkCount == 11)
                    {
                        nextLinkBodies.Add(new SharePointBatchRequestBody());
                        bodyIndex++;
                        nextLinkCount = 1;
                    }
                }
            }
            Task.WaitAll(tasks.ToArray());
            foreach (SharePointBatchRequestBody newBody in nextLinkBodies)
            {
                ProcessDirectory(rootPath, token, siteid, newBody, allObjects, logFilePath, downloadFiles, downloadedFiles);
            }
            List<SharePointBatchRequestBody> bodies = new List<SharePointBatchRequestBody>() { };
            folderCount = 1;
            bodyIndex = 0;
            foreach (SharePointObject folder in sharePointObjects.Where(x => x.downloadUrl == null))
            {
                if (bodies.Count == 0)
                {
                    bodies.Add(new SharePointBatchRequestBody());
                }
                bodies[bodyIndex].requests.Add(new SharePointBatchRequest() { id = folderCount.ToString(), method = "GET", url = $"/sites/{siteid}{folder.parentReference.path}/{folder.name}:/children" });
                folderCount++;
                if (folderCount == 11)
                {
                    bodies.Add(new SharePointBatchRequestBody());
                    bodyIndex++;
                    folderCount = 1;
                }
            }
            if (bodies.Count != 0)
            {
                if (bodies[0].requests.Count > 0)
                {
                    foreach (SharePointBatchRequestBody newBody in bodies)
                    {
                        ProcessDirectory(rootPath, token, siteid, newBody, allObjects, logFilePath, downloadFiles, downloadedFiles);
                    }
                }
            }
        }
        catch
        {
            lock (fileLock)
            {
                File.AppendAllText(logFilePath, $"Failure: Who Knows{Environment.NewLine}");
            }
        }
    }

    static async Task DownloadFiles(List<SharePointObject> files, string logFilePath, ConcurrentBag<CopyResult> downloadedFiles)
    {
        TokenResponse token;
        try
        {
            if(sharePointConfig.targetType == "FileSystem")
            {
                token = new TokenResponse();
            }
            else
            {
                token = GetTokenBlob();
            }
        }
        catch
        {
            lock (fileLock)
            {
                File.AppendAllText(logFilePath, $"Downloads Not Started, Failed to Get Token, trying again{Environment.NewLine}");
            }
            await Task.Delay(2000);
            token = GetTokenBlob();
            if (token.access_token == null)
            {
                lock (fileLock)
                {
                    File.AppendAllText(logFilePath, $"Downloads Not Started, no Token{Environment.NewLine}");
                }
            }
            else
            {
                lock (fileLock)
                {
                    File.AppendAllText(logFilePath, $"Download token, {Environment.NewLine}");
                }
            }
        }
        CopyResult result;
        string path;
        string drive;
        foreach (SharePointObject file in files)
        {
            await semaphore.WaitAsync();
            try
            {
                string fullPath = $"{sharePointConfig.targetBase}/{sharePointConfig.sharePointBaseFolder.Split("/")[^1]}{file.parentReference.path.Split(":")[1].Split(sharePointConfig.sharePointBaseFolder)[1]}/{file.name}";
                result = new CopyResult();
                try
                {
                    if (sharePointConfig.targetType == "FileSystem")
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
                        result = await FileSystemDownload.DownloadToFileSystem(fullPath, file, fileLock, logFilePath);
                    }
                    else if (sharePointConfig.targetType == "AzureBlob")
                    {
                        result = await AzureBlobDownload.UploadBlob(fullPath, file, fileLock, logFilePath, token.access_token);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"An error occurred: {ex.Message}");
                    result.sourcePath = file.downloadUrl;
                    result.targetPath = fullPath;
                    result.result = "FAILED";
                    lock (fileLock)
                    {
                        File.AppendAllText(logFilePath, $"Downloaded Failed: {file.name} from {file.downloadUrl}{Environment.NewLine}");
                    }
                }
            }
            finally
            {
                semaphore.Release();
            }
            downloadedFiles.Add(result);
        }
    }

    public static SharePointSiteResponse GetSharePointSites()
    {
        TokenResponse token = GetToken();
        HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, "/v1.0/sites");
        message.Headers.Add("Authorization", "Bearer " + token.access_token);
        message.Headers.Add("ContentType", "application/json");
        return JsonSerializer.Deserialize<SharePointSiteResponse>(sharePointClient.SendAsync(message).Result.Content.ReadAsStringAsync().Result);
    }

    public static SharePointSiteResponse GetSharePointDrives()
    {
        TokenResponse token = GetToken();
        HttpRequestMessage message = new HttpRequestMessage(HttpMethod.Get, $"/v1.0/sites/{sharePointConfig.siteId}/drives");
        message.Headers.Add("Authorization", "Bearer " + token.access_token);
        message.Headers.Add("ContentType", "application/json");
        return JsonSerializer.Deserialize<SharePointSiteResponse>(sharePointClient.SendAsync(message).Result.Content.ReadAsStringAsync().Result);
    }
}

public class SharePointSiteResponse
{
    [JsonPropertyName("@odata.nextLink")]
    public string nextLink { get; set; }
    public List<SharePointSite> value { get; set; }
}

public class SharePointSite
{
    public string id { get; set; }
    public string name { get; set; }
    public string webUrl { get; set; }
    public SharePointCreatedBy user { get; set; }
    public DateTime createdDateTime { get; set; }
}

public class CopyResult
{
    public string targetPath;
    public string sourcePath;
    public string result;
}
public class TokenResponse
{
    public string access_token { get; set; }
}

public class SharePointObjectReturn
{
    public List<SharePointObject> value { get; set; }
    [JsonPropertyName("@odata.nextLink")]
    public string? nextLink { get; set; }

    public SharePointObjectReturn()
    {
        value = new List<SharePointObject>();
    }
}

public class SharePointBatchRequest
{
    public string id { get; set; }
    public string method { get; set; }
    public string url { get; set; }
}

public class SharePointBatchRequestBody
{
    public List<SharePointBatchRequest> requests { get; set; }
    public SharePointBatchRequestBody()
    {
        requests = new List<SharePointBatchRequest>();
    }
}

public class SharePointBatch
{
    public List<SharePointBatchResponse> responses { get; set; }
    public SharePointBatch()
    {
        responses = new List<SharePointBatchResponse>();
    }
}

public class SharePointBatchResponse
{
    public string id { get; set; }
    public int status { get; set; }
    public SharePointObjectReturn body { get; set; }
}

public class SharePointObject
{
    [JsonPropertyName("@microsoft.graph.downloadUrl")]
    public string? downloadUrl { get; set; }
    public string id { get; set; }
    public string name { get; set; }
    public SharePointCreatedBy createdBy { get; set; }
    public DateTime createdDateTime { get; set; }
    public DateTime lastModifiedDateTime { get; set; }
    public Int64 size { get; set; }
    public SharePointParrentReference parentReference { get; set; }

    public SharePointObject()
    {
        size = 0;
    }
}

public class SharePointParrentReference
{
    public string path { get; set; }
}

public class SharePointCreatedBy
{
    public SharePointUser user { get; set; }
}

public class SharePointUser
{
    public string email { get; set; }
    public string displayName { get; set; }
}