// See https://aka.ms/new-console-template for more information
using System.Diagnostics.Metrics;
using System.Net.Security;
using System.Text.Json;

args = Environment.GetCommandLineArgs();
Console.WriteLine(string.Join(" ", args));
SharePointConfig config = SharePointConfig.GetConfig();
bool downloadFiles = bool.Parse(args.SkipWhile(x => x != "--downloadFiles").Skip(1).FirstOrDefault(config.downloadFiles.ToString()).ToString());
bool foldersOnly = bool.Parse(args.SkipWhile(x => x != "--foldersOnly").Skip(1).FirstOrDefault(config.foldersOnly.ToString()).ToString());
bool recursive = bool.Parse(args.SkipWhile(x => x != "--recursive").Skip(1).FirstOrDefault(config.recursive.ToString()).ToString());
bool findDriveId = bool.Parse(args.SkipWhile(x => x != "--findDriveId").Skip(1).FirstOrDefault("false").ToString());
if (findDriveId)
{
    List<SharePointSite> sites = SharePoint.GetSharePointSites();
    foreach (SharePointSite site in sites)
    {
        Console.WriteLine($"Site: {site.name}, SiteId: {site.id}, WebUrl: {site.webUrl}");
        List<SharePointDrive> drives = SharePoint.GetSharePointDrives(site.id);
        foreach(SharePointDrive  drive in drives)
        {
            Console.WriteLine($"DriveName: {drive.name}, DriveId: {drive.id}, WebUrl: {drive.webUrl}");
        }
    }
}
else
{
    Console.WriteLine($"Download Files: {downloadFiles}, Folders Only: {foldersOnly}, Recursive: {recursive}");
    List<SharePointObject> files = SharePoint.GetItems(foldersOnly, recursive, downloadFiles);
    Console.WriteLine($"Total files: {files.Count}, size of all files: {files.Sum(x => x.size)}");
}