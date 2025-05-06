// See https://aka.ms/new-console-template for more information
using System.Diagnostics.Metrics;
using System.Text.Json;

args = Environment.GetCommandLineArgs();
Console.WriteLine(string.Join(" ", args));
SharePointConfig config = SharePointConfig.GetConfig();
bool downloadFiles = bool.Parse(args.SkipWhile(x => x != "--downloadFiles").Skip(1).FirstOrDefault(config.downloadFiles.ToString()).ToString());
bool foldersOnly = bool.Parse(args.SkipWhile(x => x != "--foldersOnly").Skip(1).FirstOrDefault(config.foldersOnly.ToString()).ToString());
bool recursive = bool.Parse(args.SkipWhile(x => x != "--recursive").Skip(1).FirstOrDefault(config.recursive.ToString()).ToString());
Console.WriteLine($"Download Files: {downloadFiles}, Folders Only: {foldersOnly}, Recursive: {recursive}");
List<SharePointObject> files = SharePoint.GetItems(foldersOnly, recursive, downloadFiles);
Console.WriteLine($"Total files: {files.Count}, size of all files: {files.Sum(x => x.size)}");