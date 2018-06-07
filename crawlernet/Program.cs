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

namespace crawlernet
{
    class Program
    {
        static void Main(string[] args)
        {

            var key = args[0].ToString();

            var client = new HttpClient(){Timeout=new TimeSpan(0,3,0)};
            var links = new ConcurrentBag<string>();
            var index = Enumerable.Range(1, 1000).ToArray();
            var part = Partitioner.Create<int>(index);
            var download = new TransformBlock<string, string>(async uri =>
            {
                try
                {
                    return await client.GetStringAsync(uri);
                }
                catch (Exception)
                {
                    return "";
                }
                
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 8 });

            var grep = new TransformManyBlock<string,(string,string)>(async text => 
            {
                var l = new List<(string,string)>();
                var reg = new Regex($"<a[^<]*href=\"([^<\"]*)\"[^<]*>([^<]*({key}[-]?\\d{{0,4}})[^<]*)",
                    RegexOptions.IgnoreCase|
                    RegexOptions.Multiline|
                    RegexOptions.Compiled|
                    RegexOptions.IgnorePatternWhitespace);
                var matches = reg.Matches(text);
                foreach (Match match in matches)
                {
                    if (!links.Contains(match.Groups[1].ToString()))
                    {
                        links.Add(match.Groups[1].ToString());
                        var picker = "";
                        try
                        {
                            var res = await client.GetStringAsync($"http://n2.lufi99.org/pw/{match.Groups[1]}");
                            var reg2 = new Regex("width>=1024[^(]*\\('([^']*)[^>]*>");
                            var match2 = reg2.Match(res);
                            if (match2.Success)
                            {
                                picker = match2.Groups[1].Value;
                                l.Add((picker,match.Groups[3].ToString()));
                            }
                        }
                        catch (Exception){}
                        finally
                        {
                            Console.WriteLine($"http://n2.lufi99.org/pw/{match.Groups[1]} {picker} {match.Groups[2]}");
                        }
                        
                        
                    }

                }
                return l;
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 4 });

            var save = new ActionBlock<(string,string)>(async item => {
                
                try
                {
                    var name = item.Item2 + ".jpg";
                    var uri = new Uri(item.Item1);
                    var res = await client.GetAsync(uri);
                    if (res.IsSuccessStatusCode)
                    {
                        using (var file = new FileStream($"{Directory.GetCurrentDirectory()}\\img\\{name}", FileMode.OpenOrCreate))
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
                download.Post($"http://n2.lufi99.org/pw/thread.php?fid=22&page={page}");
            });
            download.Complete();
            save.Completion.Wait();
            Console.WriteLine("搜索完成");
            return;
        }
    }
}
