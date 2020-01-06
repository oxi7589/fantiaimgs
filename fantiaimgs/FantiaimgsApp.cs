// 2019-2020, by Oxi

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace fantiaimgs
{
    class FantiaimgsApp
    {
        public static class StringConstants
        {
            public const string ProductName = "fantiaimgs/1.7 by Oxi";

            public static readonly IList<String> QualityPostfix = new ReadOnlyCollection<string>(
                new List<String> {
                    "original", "main", "large", "medium", "thumb"
                });
        }

        class AppConfig
        {
            public bool RenamePolicyRemoveHash = false;
            public bool RenamePolicyPrependGroupIdx = false;
            public bool RenamePolicyPrependImageIdx = false;
            public bool RenamePolicyRenameThumbs = true;
            public bool DownloadPolicyGetMetaPics = true;
            public string FanclubId = "";
            public string FanclubName = "";
            public string CookieFile = "_session_id.txt";
        }

        static AppConfig CurrentAppConfig = new AppConfig();
        
        static NetworkState CurrentNetworkState = new NetworkState();

        static string GetBestUrl(Dictionary<string, object> node)
        {
            foreach (string qualifier in StringConstants.QualityPostfix)
            {
                if (node.ContainsKey(qualifier))
                {
                    Console.WriteLine("    type: " + qualifier);
                    string url = (string)node[qualifier];
                    Console.WriteLine("     url: " + url);
                    return url;
                }
            }
            return null;
        }

        static void ProcessPost(string postId)
        {
            var fnameTransorms = new List<NetworkState.TransformFilenameFunction>();

            if (CurrentAppConfig.RenamePolicyRemoveHash)
            {
                fnameTransorms.Add((string s) => Regex.Replace(s, "^........_", ""));
            }

            var rootrelpath = CurrentNetworkState.GetRoot();
            var postpath = Path.Combine(Path.Combine("./", rootrelpath != null ? rootrelpath : "unknown"), postId);
            if (System.IO.Directory.Exists(postpath))
            {
                Console.WriteLine($"Skipping: {postId} (folder exists)");
                return;
            }
            string postJsonPage = CurrentNetworkState.GetPage(String.Format("https://fantia.jp/api/v1/posts/{0}", postId));
            if (postJsonPage == null) return;

            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            serializer.MaxJsonLength = 1024 * 1024 * 10; // 10 MiB should be enough
            
            var postInfoJsonRoot = (Dictionary<string, object>)serializer.DeserializeObject(postJsonPage);
            if (postInfoJsonRoot.Count == 0)
            {
                Console.WriteLine("Error: unexpected JSON response structure");
                //continue;
            }
            postInfoJsonRoot = (Dictionary<string, object>)postInfoJsonRoot.First().Value;

            if (postInfoJsonRoot.ContainsKey("thumb"))
            {                
                var thumb = (Dictionary<string, object>)postInfoJsonRoot["thumb"];
                if (thumb != null)
                {
                    Console.WriteLine("Found thumbnail.");
                    var url = GetBestUrl(thumb);
                    if (CurrentAppConfig.RenamePolicyRenameThumbs)
                        fnameTransorms.Add((string s) => Regex.Replace(s, "^........-....-....-....-............", "_thumb"));
                    try
                    {
                        CurrentNetworkState.DownloadFile(url, postId, fnameTransorms);
                    }
                    finally
                    {
                        if (CurrentAppConfig.RenamePolicyRenameThumbs)
                            fnameTransorms.RemoveAt(fnameTransorms.Count - 1);
                    }
                }
            }
            else
            {
                Console.WriteLine("NOTE :: thumb not found");
            }

            if (postInfoJsonRoot.ContainsKey("post_contents"))
            {
                var contents = (object[])postInfoJsonRoot["post_contents"];
                Console.WriteLine("contents count:" + contents.Count().ToString());
                int subPostCounter = 0;
                int imageCounter = 0;
                foreach (Dictionary<string, object> contentsObject in contents)
                {
                    subPostCounter++;
                    bool foundSomethingWorthy = false;
                    if (contentsObject.ContainsKey("post_content_photos")) // photo gallery
                    {
                        foundSomethingWorthy = true;
                        var photosArray = (object[])contentsObject["post_content_photos"];
                        Console.WriteLine("photos count:" + photosArray.Count().ToString());
                        foreach (Dictionary<string, object> photoItem in photosArray)
                        {
                            imageCounter++;
                            if (!photoItem.ContainsKey("url"))
                            {
                                Console.WriteLine("Error: url key not found, yet photo node exists");
                                continue;
                            }
                            var urls = (Dictionary<string, object>)photoItem["url"];
                            var url = GetBestUrl(urls);

                            if (CurrentAppConfig.RenamePolicyPrependImageIdx)
                                fnameTransorms.Add(NetworkState.FilenameTransormPrependIdx(imageCounter));
                            if (CurrentAppConfig.RenamePolicyPrependGroupIdx)
                                fnameTransorms.Add(NetworkState.FilenameTransormPrependIdx(subPostCounter));

                            try
                            {
                                CurrentNetworkState.DownloadFile(url, postId, fnameTransorms);
                            }
                            finally
                            {
                                if (CurrentAppConfig.RenamePolicyPrependGroupIdx)
                                    fnameTransorms.RemoveAt(fnameTransorms.Count - 1);
                                if (CurrentAppConfig.RenamePolicyPrependImageIdx)
                                    fnameTransorms.RemoveAt(fnameTransorms.Count - 1);
                            }
                        }
                    }
                    if (contentsObject.ContainsKey("download_uri")) // downloadable file
                    {
                        foundSomethingWorthy = true;
                        var downloadUrl = "https://fantia.jp" + (string)contentsObject["download_uri"];
                        CurrentNetworkState.DownloadFile(downloadUrl, postId, fnameTransorms);
                    }
                    if (!foundSomethingWorthy)
                    {
                        Console.WriteLine("Found post with no downloadables, skipping it");
                    }
                }
            }
            else
            {
                Console.WriteLine("Post contents not found!");
            }
        }

        static void ProcessIconAndHeader(string fanclubId)
        {
            string clubJsonPage = CurrentNetworkState.GetPage(String.Format("https://fantia.jp/api/v1/fanclubs/{0}", fanclubId));
            if (clubJsonPage == null)
            {
                Console.WriteLine("Error :: got no valid response from the 'fanclubs' API endpoint");
                Console.WriteLine("         fanclub icon and header image will not be downloaded");
                return;
            }

            var serializer = new System.Web.Script.Serialization.JavaScriptSerializer();
            serializer.MaxJsonLength = 1024 * 1024 * 10; // 10 MiB should be more than enough

            var jsonRoot = (Dictionary<string, object>)serializer.DeserializeObject(clubJsonPage);
            if (jsonRoot.Count == 0)
            {
                Console.WriteLine("Error: unexpected JSON response structure");
                return;
            }
            jsonRoot = (Dictionary<string, object>)jsonRoot.First().Value;
            
            List<string> metaPics = new List<string> { "icon", "cover" };
            foreach (var metaPic in metaPics)
            {
                if (jsonRoot.ContainsKey(metaPic))
                {
                    var thumb = (Dictionary<string, object>)jsonRoot[metaPic];
                    if (thumb != null)
                    {
                        Console.WriteLine("Found {0}.", metaPic);
                        var url = GetBestUrl(thumb);
                        if (url.Contains("fallback"))
                        {
                            Console.WriteLine("Fallback URL detected, skipping");
                            continue;
                        }
                        CurrentNetworkState.DownloadFile(url, ".", null, true);
                    }
                }
                else
                {
                    Console.WriteLine("NOTE :: {0} not found", metaPic);
                }
            }

            if (jsonRoot.ContainsKey("background"))
            {
                var back = jsonRoot["background"];
                if (back is string)
                {
                    var url = back as string;
                    Console.WriteLine("Found background.");
                    CurrentNetworkState.DownloadFile(url, ".", null, true);
                }
                else
                {
                    Console.WriteLine("This fanclub has no custom background");
                }
            }
            else
            {
                Console.WriteLine("NOTE :: background not found");
            }
        }

        static void ProcessFanclub(string fanclubId)
        {
            CurrentNetworkState.SetRootDestIfNull(fanclubId);

            // process icon and header
            if (CurrentAppConfig.DownloadPolicyGetMetaPics)
            {
                ProcessIconAndHeader(fanclubId);
            }

            // process posts
            List<string> posts = new List<string>();
            for (int pageNumber = 1; ; pageNumber++)
            {
                Console.WriteLine("Processing page: " + pageNumber.ToString());
                int pgPostCnt = 0;
                string page = CurrentNetworkState.GetPage(
                    String.Format("https://fantia.jp/fanclubs/{0}/posts?page={1}", fanclubId, pageNumber)
                    );
                if (page != null)
                {
                    while (page.Contains("data-post_id"))
                    {
                        page = page.Remove(0, page.IndexOf("data-post_id"));
                        page = page.Remove(0, page.IndexOf("\"") + "\"".Count());
                        posts.Add(page.Substring(0, page.IndexOf("\"")));
                        pgPostCnt++;
                    }
                }
                if (pgPostCnt == 0)
                {
                    Console.WriteLine("Last page reached");
                    break;
                }
                else
                {
                    Console.WriteLine("Posts found: " + pgPostCnt.ToString());
                }
            }
            Console.WriteLine("Total posts: " + posts.Count.ToString());
            foreach (string post in posts)
            {
                ProcessPost(post);
            }
        }

        static void Main(string[] args)
        {
            Console.WriteLine(StringConstants.ProductName);

            // check subarguments count and handle help requests
            for (int i = 0; i < args.Count(); i++)
            {
                switch (args[i])
                {
                    case "-club":
                    case "-name":
                    case "-cookiefile":
                        if (i + 1 >= args.Count() || (args[i + 1] != "" && args[i + 1][0] == '-'))
                        {
                            Console.WriteLine(string.Format("Error: \"{0}\" must be followed by its argument.", args[i]));
                            return;
                        }
                        break;
                    case "/?":
                    case "-help":
                    case "--help":
                    case "-h":
                        try
                        {
                            using (var stream = System.Reflection.Assembly.
                                                GetExecutingAssembly().GetManifestResourceStream("fantiaimgs.helpfile.txt"))
                            {
                                using (var reader = new StreamReader(stream))
                                {
                                    string text = reader.ReadToEnd();
                                    Console.WriteLine(text);
                                }
                            }
                        }
                        catch (Exception)
                        {
                            // no-op
                        }
                        finally
                        {
                            Console.WriteLine("For the most up to date information please visit:");
                            Console.WriteLine("https://yupdates.neocities.org/tools/fantiaimgs/");
                        }
                        return;
                }
            }
            // apply all the useful arguments
            for (int i = 0; i < args.Count(); i++)
            {
                if (args[i] == "" || args[i][0] != '-')
                {
                    Console.WriteLine(string.Format("Error: \"{0}\" is not a recognized argument.", args[i]));
                    Console.WriteLine("Use \"-h\" to get a list of all supported parameters.");
                    return;
                }
                switch (args[i])
                {
                    case "-club":
                        CurrentAppConfig.FanclubId = args[++i];
                        break;
                    case "-name":
                        CurrentAppConfig.FanclubName = args[++i];
                        break;
                    case "-cookiefile":
                        CurrentAppConfig.CookieFile = args[++i];
                        break;
                    case "-nometa":
                        CurrentAppConfig.DownloadPolicyGetMetaPics = false;
                        break;
                    case "-imgnum":
                        CurrentAppConfig.RenamePolicyPrependImageIdx = true;
                        break;
                    case "-subp":
                        CurrentAppConfig.RenamePolicyPrependGroupIdx = true;
                        break;
                    case "-nh":
                        CurrentAppConfig.RenamePolicyRemoveHash = true;
                        break;
                    case "-keepthumbnames":
                        CurrentAppConfig.RenamePolicyRenameThumbs = false;
                        break;
                    default:
                        Console.WriteLine(string.Format("What did you mean by {0}?", args[i]));
                        Console.WriteLine("Use \"-h\" to get a list of all supported parameters.");
                        return;
                }
            }

            if (!File.Exists(CurrentAppConfig.CookieFile))
            {
                Console.WriteLine("_session_id value?");
                string cookie = Console.ReadLine().Trim();
                Console.WriteLine("Note: you can put this value in the file passed after \"-cookiefile\" argument");
                Console.WriteLine("(or \"_session_id.txt\") to avoid entering it manually each time");
                CurrentNetworkState.Init(cookie);
            }
            else
            {
                var sessionfile = File.ReadAllLines(CurrentAppConfig.CookieFile);
                if (sessionfile.Count() > 0)
                {
                    string cookie = sessionfile[0].Trim();
                    CurrentNetworkState.Init(cookie);
                }
                else
                {
                    Console.WriteLine("Bad _session_id. File must contain one line with the cookie value");
                    return;
                }
            }

            string fanclub = "";
            if (!string.IsNullOrEmpty(CurrentAppConfig.FanclubId))
            {
                fanclub = CurrentAppConfig.FanclubId;
            }
            else
            {
                Console.WriteLine("fanclub id (digital)?");
                fanclub = Console.ReadLine().Trim();
            }
            try
            {
                Console.WriteLine(string.Format("Fanclub: {0}", int.Parse(fanclub)));
            }
            catch (Exception)
            {
                Console.WriteLine("Bad Fanclub id. It's supposed to be a number, you see~");
                return;
            }

            string hrname = "";
            if (!string.IsNullOrEmpty(CurrentAppConfig.FanclubName))
            {
                hrname = StaticTools.SafeFilename(CurrentAppConfig.FanclubName);
            }
            else if (string.IsNullOrEmpty(CurrentAppConfig.FanclubId))
            {
                Console.WriteLine("human-readable name? (leave blank to use id)");
                hrname = StaticTools.SafeFilename(Console.ReadLine().Trim());
            }
            if (hrname != "") CurrentNetworkState.SetRootDest(hrname);

            ProcessFanclub(fanclub);
            Console.WriteLine("Done!");
        }
    }
}
