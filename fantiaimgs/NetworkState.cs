using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace fantiaimgs
{
    class NetworkState
    {
        private HttpClientHandler httpClientHandler = null;
        private HttpClient httpClient = null;
        private string rootdest = null;

        public string GetRoot()
        {
            return rootdest;
        }

        public void Init(string sessid)
        {
            httpClientHandler = new HttpClientHandler();
            httpClientHandler.UseCookies = false;
            httpClient = new HttpClient(httpClientHandler);
            httpClient.DefaultRequestHeaders.Clear();
            httpClient.DefaultRequestHeaders.Add("Cookie", String.Format("_session_id={0}", sessid));
        }

        public void SetRootDest(string rdest)
        {
            rootdest = rdest;
        }

        public void SetRootDestIfNull(string rdest)
        {
            if (rootdest == null) rootdest = rdest;
        }

        public string GetPage(string url)
        {
            int tries = 3;
            string pg = "";
        start:
            try
            {
                pg = httpClient.GetStringAsync(url).Result;
            }
            catch (Exception E)
            {
                Console.WriteLine("Error retrieving page " + "(" + url + "): " + E.Message);
                tries--;
                System.Threading.Thread.Sleep(5000);
                if (tries > 0)
                    goto start;
                else
                    return null;
            }
            return pg;
        }

        async private Task ReadNetworkStream(Stream from, Stream to, int timeout)
        {
            const int bufferSize = 2048;
            int receivedBytes = -1;
            var buffer = new byte[bufferSize];
            while (receivedBytes != 0) // read until no more data available (or timeout exception is thrown)
            {
                using (var cancellationTokenSource = new System.Threading.CancellationTokenSource(timeout))
                {
                    using (cancellationTokenSource.Token.Register(() => from.Close()))
                    {
                        receivedBytes = await from.ReadAsync(buffer, 0, bufferSize, cancellationTokenSource.Token);
                    }
                }
                if (receivedBytes > 0)
                {
                    await to.WriteAsync(buffer, 0, receivedBytes);
                }
            }
        }

        public delegate string TransformFilenameFunction(string fname);
        public static TransformFilenameFunction FilenameTransormPrependIdx(int idx)
        {
            return (string x) => idx.ToString("D3") + "_" + x;
        }

        /// network code for this largely stolen from furdown @ github.com/crouvpony47/furdown
        public void DownloadFile(string url, string dest,
                                 List<TransformFilenameFunction> fnts = null, bool abortOnExisting = false)
        {
            // this conversion might be not quite portable, but it's good enough, i suppose
            Uri uri = new Uri(url);
            string fname = StaticTools.SafeFilename(System.IO.Path.GetFileName(uri.LocalPath));
            if (fnts != null)
            {
                foreach (var fnt in fnts)
                    fname = fnt(fname);
            }

            string fnamefull = "";

        retryPickName:

            fnamefull = Path.Combine(Path.Combine(Path.Combine(
                "./",
                rootdest != null ? rootdest : "unknown"), dest), fname
            );

            Directory.CreateDirectory(Path.GetDirectoryName(fnamefull));

            // download file
            Console.WriteLine("Preparing to download " + fname);
            if (File.Exists(fnamefull))
            {
                if (abortOnExisting)
                {
                    Console.WriteLine("Already exists, skipped");
                    return;
                }
                fname = "+" + fname;
                goto retryPickName;
            }
            int fattempts = 3;
        fbeforeawait:
            try
            {
                Console.WriteLine("Downloading file... ");
                using (
                    HttpResponseMessage httpResponse = httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead).Result
                )
                {
                    // if redirected, get the new filename
                    if ((int)httpResponse.StatusCode >= 400)
                    {
                        throw new Exception(string.Format("Unexpected HTTP status code ({0})", httpResponse.StatusCode.ToString()));
                    }
                    var actualUrl = httpResponse.RequestMessage.RequestUri;
                    fname = StaticTools.SafeFilename(Path.GetFileName(actualUrl.LocalPath));
                    if (fnts != null)
                    {
                        foreach (var fnt in fnts)
                            fname = fnt(fname);
                    }
                retryPickName2:
                    fnamefull = Path.Combine(Path.Combine(Path.Combine(
                        "./",
                        rootdest != null ? rootdest : "unknown"), dest), fname
                    );
                    if (File.Exists(fnamefull))
                    {
                        if (abortOnExisting)
                        {
                            Console.WriteLine("Already exists, skipped");
                            return;
                        }
                        fname = "+" + fname;
                        goto retryPickName2;
                    }
                    Console.WriteLine("final filename: " + fname);
                    // actually download the files
                    using (
                        Stream contentStream = httpResponse.Content.ReadAsStreamAsync().Result,
                        stream = new FileStream(
                            fnamefull,
                            FileMode.Create, FileAccess.Write, FileShare.None, 1024 * 1024 /*Mb*/, true)
                    )
                    {
                        ReadNetworkStream(contentStream, stream, 5000).Wait();
                    }
                }
            }
            catch (Exception E)
            {
                // write error message
                if (E is ObjectDisposedException)
                {
                    Console.WriteLine("Network error (data receive timeout)");
                }
                else
                {
                    Console.WriteLine(string.Format("Http request error (file {0}): {1}", fname, E.Message));
                }
                // remove incomplete download
                if (File.Exists(fnamefull))
                {
                    File.Delete(fnamefull);
                }
                // try again or abort operation
                fattempts--;
                System.Threading.Thread.Sleep(2000);
                if (fattempts > 0)
                    goto fbeforeawait;
                {
                    Console.WriteLine("Giving up on downloading " + fname);
                    return;
                }
            }
            Console.WriteLine("Done: " + fname);
        }
    }
}
