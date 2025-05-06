using System.Formats.Tar;
using System.Text;
using System.Text.Json;

public class SharePointConfig {
    public EntraInfo entra {get;set;}
    public string logFileName {get;set;}
    public string siteId {get;set;}
    public string driveId {get;set;}
    public string sharePointBaseFolder {get;set;}
    public string targetType {get;set;}
    public string targetBase {get;set;}
    public string blobEndpointUrl {get;set;}
    public string blobContainer {get;set;}
    public string blobBaseAddress {get;set;}
    public bool downloadFiles {get;set;}
    public bool recursive {get;set;}
    public bool foldersOnly {get;set;}

    static public SharePointConfig GetConfig() {        
        SharePointConfig sharePointConfig = JsonSerializer.Deserialize<SharePointConfig>(string.Join("",File.ReadAllLines("settings.json",Encoding.UTF8)));
        switch(sharePointConfig.targetType) {
            case "FileSystem":
                sharePointConfig.targetType = "FileSystem";
                break;
            case "AzureBlob":
                sharePointConfig.targetType = "AzureBlob";
                break;
            default: {
                sharePointConfig.targetType = "FileSystem";
                break;
            }    
        }
        return sharePointConfig;
    }
}

public class EntraInfo {
    public string client_id {get;set;}
    public string client_secret {get;set;}
    public string tenantId {get;set;}
    public string tokenBaseAddress {get;set;}
}