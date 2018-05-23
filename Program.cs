using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
            var reg = new Regex($"<a[^<]*href=\"([^<\"]*)\"[^<]*>([^<]*{key}[^<]*)",
            RegexOptions.IgnoreCase|
            RegexOptions.Multiline|
            RegexOptions.Compiled|
            RegexOptions.IgnorePatternWhitespace);

            var client = new HttpClient();
            var links = new SortedSet<string>();
            var index = Enumerable.Range(1, 700).ToArray();
            var part = Partitioner.Create<int>(index);
            var download = new TransformBlock<string, string>(async uri =>
            {
                return await client.GetStringAsync(uri);
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 20 });

            var grep = new ActionBlock<string>(text => 
            {
                var matches = reg.Matches(text);
                foreach (Match match in matches)
                {
                    if (!links.Contains(match.Groups[1].ToString()))
                    {
                        Console.WriteLine($"http://n2.lufi99.org/pw/{match.Groups[1]} {match.Groups[2]}");
                        links.Add(match.Groups[1].ToString());
                    }

                }
            }, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 100 });

            download.LinkTo(grep, new DataflowLinkOptions { PropagateCompletion = true });
            Parallel.ForEach(part, page => 
            {
                download.Post($"http://n2.lufi99.org/pw/thread.php?fid=22&page={page}");
            });

            grep.Completion.Wait();
            return;
        }
    }
}
