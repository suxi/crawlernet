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

            var key = args[0].ToString();

            var client = new HttpClient(){Timeout=new TimeSpan(0,3,0)};
            client.DefaultRequestHeaders.Add("User-Agent","Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/64.0.3282.140 Safari/537.36 Edge/17.17134");
            var links = new ConcurrentBag<string>();
            var index = Enumerable.Range(1, 500).ToArray();
            var part = Partitioner.Create<int>(index);

            var redis = ConnectionMultiplexer.Connect("localhost");
            var TIMEOUT = new TimeSpan(0,20,0);
            var download = new TransformBlock<string, string>(async uri =>
            {
                try
                {
                    IDatabase db = redis.GetDatabase();
                    var value = await db.StringGetAsync(uri);
                    if (value.IsNullOrEmpty)
                    {
                        Thread.Sleep(3*1000);
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
                catch (Exception e)
                {
                    Console.WriteLine($"{uri} {e.Message}");
                    return "";
                }
                
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });

            var grep = new TransformManyBlock<string,(string,string)>(async text => 
            {
                var l = new List<(string,string)>();
                if (text.Length == 0)
                {
                    return l;
                }

                var htmlDoc = new HtmlAgilityPack.HtmlDocument();
                htmlDoc.LoadHtml(text);
                var items = htmlDoc.DocumentNode.SelectNodes("//a[@href]/span/parent::*");
                var reg = new Regex(
                    $"{key}[-]?\\d{{0,4}}",
                    RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        if (item.InnerLength > 0 && item.InnerText.Length > 0)
                        {
                            var match = reg.Match(item.InnerText);
                            if (match.Success)
                            {
                                var link = $"http://n2.lufi99.org/{item.Attributes["href"].Value}";
                                var title = item.InnerText;
                                var name = match.Captures.First();
                                var pic = "";
                                if (!links.Contains(link))
                                {
                                    links.Add(link);
                                    try
                                    {
                                        var res = await client.GetStringAsync(link);
                                        var imgDoc = new HtmlAgilityPack.HtmlDocument();
                                        imgDoc.LoadHtml(res);
                                        var img = imgDoc.DocumentNode.SelectSingleNode("//img[@class='zoom']");

                                        if (img != null)
                                        {
                                            pic = img.Attributes["src"].Value;
                                        }
                                    }
                                    finally
                                    {
                                        Console.WriteLine($"{link} {pic} {title}");
                                    }
                                }                                
                            }

                            
                        }
                    }
                }
                return l;
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 8});

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

            Parallel.ForEach(part, page => 
            {
                //              http://n2.lufi99.org/forum.php?mod=forumdisplay&fid=22&page=2
                download.Post($"http://n2.lufi99.org/forum.php?mod=forumdisplay&fid=22&page={page}");
            });
            download.Complete();
            grep.Completion.Wait();
            Console.WriteLine("搜索完成");
            return;
        }
    }
}
