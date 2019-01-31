using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using StackExchange.Redis;

namespace crawlernet
{
    class Program
    {
        static void Main(string[] args)
        {
            var HOST = "http://m2.5y1rsxmzh.xyz/pw/";
            var key = "";

            var client = new HttpClient(){Timeout=new TimeSpan(0,3,0)};
            client.DefaultRequestHeaders.Add("User-Agent","Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/64.0.3282.140 Safari/537.36 Edge/17.17134");
            var links = new ConcurrentBag<string>();
            var range = 500;

            if (args.Length > 1)
            {
                key = args[0].ToString();
                int.TryParse(args[1],out range);
            }
            else if (args.Length == 1){
                if (!int.TryParse(args[0], out range))
                {
                    key = args[0];
                    range = 500;
                }
            }
            var index = Enumerable.Range(1, range).ToArray();
            var part = Partitioner.Create<int>(index);

            var TIMEOUT = new TimeSpan(0,30,0);
            ConnectionMultiplexer redis = null;
            try
            {
                redis = ConnectionMultiplexer.Connect("localhost,connectRetry=0");
            }
            catch (System.Exception){}

            var download = new TransformBlock<string, string>(async uri =>
            {
                try
                {
                    if (redis != null)
                    {
                        IDatabase db = redis.GetDatabase();
                        var value = await db.StringGetAsync(uri);
                        if (value.IsNullOrEmpty)
                        {
                            Thread.Sleep(1 * 200);
                            var text = await client.GetStringAsync(uri);
                            if (text.IndexOf("ROBOTS") > 0)
                            {
                                Console.WriteLine($"{uri} anti-robots");
                                return "";
                            }
                            else
                            {
                                if (await db.StringSetAsync(uri,text))
                                {
                                    await db.KeyExpireAsync(uri, TIMEOUT);
                                }
                                return text;
                            }
                        }
                        else
                        {
                            return value.ToString();
                        }
                    }
                    else{
                        Thread.Sleep(1*1000);
                        var text = await client.GetStringAsync(uri);
                        if (text.IndexOf("ROBOTS") > 0)
                        {
                            Console.WriteLine($"{uri} anti-robots");
                            return "";
                        }
                        else
                        {
                            return text;
                        }
                    }


                }
                catch (Exception e)
                {
                    Console.WriteLine($"{uri} {e.Message}");
                    return "";
                }
                
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });

            var grep = new TransformManyBlock<string,(string,string)>(text => 
            {
                var l = new List<(string,string)>();
                if (text.Length == 0)
                {
                    return l;
                }

                var htmlDoc = new HtmlAgilityPack.HtmlDocument();
                htmlDoc.LoadHtml(text);
                var items = htmlDoc.DocumentNode.SelectNodes("//a[@href and parent::h3]");
                var searchPattern = key == "" ? "" : $"{key}[-]?\\d{{0,4}}";
                var reg = new Regex(
                    searchPattern,
                    RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);
                var bangoReg = new Regex(@"[a-zA-Z]{2,5}-?\d{3,5}[ABab]?",RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (item.InnerLength > 0 && item.InnerText.Length > 0)
                        {
                            var match = reg.Match(item.InnerText);
                            if (match.Success)
                            {
                                var link = $"{HOST}{item.Attributes["href"].Value}".Replace("&amp;","&");
                                var title = item.InnerText.Replace("&nbsp;"," ");
                                var name = bangoReg.Match(title).Value;
                                var cover = $"http://www.dmm.co.jp/search/=/searchstr={name}";
                                if (!links.Contains(link))
                                {
                                    links.Add(link);
                                    Console.WriteLine($"{link} {title}");
                                    // try
                                    // {
                                    //     var res = await client.GetStringAsync(link);
                                    //     var imgDoc = new HtmlAgilityPack.HtmlDocument();
                                    //     imgDoc.LoadHtml(res);
                                    //     var img = imgDoc.DocumentNode.SelectSingleNode("//img[@onload and @onclick and @border]");

                                    //     if (img != null)b
                                    //     {
                                    //         pic = img.Attributes["src"].Value;
                                    //     }
                                    // }
                                    // catch(Exception) {}
                                    // finally
                                    // {
                                    //     Console.WriteLine($"{link} {title} {pic}");
                                    // }
                                }                                
                            }

                            
                        }
                    }
                }
                return l;
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1});

            var save = new ActionBlock<(string,string)>(async item => {
                
                try
                {
                    var fileP = Path.Combine(Directory.GetCurrentDirectory(),"img",item.Item2 + ".jpg");
                    if (File.Exists(fileP))
                    {
                        if (new FileInfo(fileP).Length > 0)
                        return;
                    }
                    var uri = new Uri(item.Item1);
                    var res = await client.GetAsync(uri);
                    if (res.IsSuccessStatusCode)
                    {
                        using (var file = new FileStream(fileP, FileMode.OpenOrCreate))
                        {
                            using (var s = await res.Content.ReadAsStreamAsync())
                            {
                                await s.CopyToAsync(file);
                            }
                        }
                    }
                }
                catch (Exception e)
                {
                    Debug.WriteLine(e.Message);
                }
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 8 });
            download.LinkTo(grep, new DataflowLinkOptions { PropagateCompletion = true });
            grep.LinkTo(save, new DataflowLinkOptions { PropagateCompletion = true });

            
            var stop = Stopwatch.StartNew();
            // Parallel.ForEach(part, page => 
            foreach (var page in Enumerable.Range(1, range))
            {
                //              http://s3.97xzl.com/pw/thread.php?fid=22
                // http://n2.lufi99.club/pw/thread-htm-fid-22-page-1.html
                download.Post($"{HOST}thread-htm-fid-22-page-{page}.html");
            };
            download.Complete();
            grep.Completion.Wait();
            stop.Stop();
            Console.WriteLine($"[搜索完成(by {links.Count} in {stop.ElapsedMilliseconds:N}ms)]");
            return;
        }
    }
}
