# What does this do

This is still a work in progress

* Lists files and total file size in sharepoint folders
* Downloads files and writes to a local File System or Uploads to Azure Blob with correct directory structure

# How to use

Fill out the settings.json file:

```json
{
   "entra": {
      "client_id": "", //Application ID that has Sites.Read.All Application permissions in Entra ID
      "client_secret": "", //Secret of App ID
      "tenantId": "", //Tenant ID of the Entra Tenant that has App registered
      "tokenBaseAddress": "https://login.microsoftonline.com/" //Base endpoint for your Token URL (Change based on cloud you are in)
   },
   "logFileName": "Files.txt", //Name of log file that will be created in the root directory of the .exe
   "siteId": "", //Sharepoint Site ID
   "driveId": "", //Sharepoint Drive ID
   "sharePointBaseFolder": "", //Base folder that you want to count or copy. Example: /AnotherDepth/B2c-custom
   "targetType": "FileSystem", //Type of copy if you are downloading.  Accepted Values FileSystem and AzureBlob
   "targetBase": "", //Target base of where the files are to be coppied.  Example: FileSystem = C:\MyFiles Blob = /MyFiles
   "blobEndpointUrl": "", //Url to blob storage account in this format https://mystorageaccount.blob.core.windows.net/ DNS Suffix may be different depending on Cloud
   "blobContainer": "", //Blob container name, Example: sharepoint
   "blobBaseAddress": "https://storage.azure.com/", //App URI that we will be audience for the token to write to storage account.  This value will be different depending on Cloud.  
   "downloadFiles": true, //If you don't want to download files, set to false
   "recursive": true, //If you want to only get files in the specificed directory but not subdirectories, set to false
   "foldersOnly": false //If you only want to a log of folders and not files, set to true.
}
```

You can also override the settings.json behavior using console commands

```cmd
.\Sharepoint-Migrate-Console.exe --downloadFiles true --recursive false --foldersOnly false
```

# Known Issues and Future Features

Observed when running against Sharepoint environments that have a lot of files, threads seem to die without downloading the files.  I am working to understand what is causing this, but I do check to see if the file already exists before downloading.  I also check to see if the sharepoint has been updated since the last run, so you should be safe to continue running overagain and it will start downloading where it left off.  

Different Console outputs depending on Settings.json options.