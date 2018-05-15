using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace crawlernet
{
    class Program
    {
        static void Main(string[] args)
        {
            var key = args[0].ToString();
            var reg = new Regex($"<a[^<]*href=\"([^<\"]*)\"[^<]*({key}[^<]*)",RegexOptions.IgnoreCase|RegexOptions.Multiline|RegexOptions.Compiled|RegexOptions.IgnorePatternWhitespace);

            var client = new HttpClient();
            var token = new SemaphoreSlim(5);
            var links = new SortedSet<string>();
            var index = Enumerable.Range(1, 700).ToArray();
            var part = Partitioner.Create<int>(index);
            Parallel.ForEach(part, page => 
            {

                var task = client.GetStringAsync($"http://n2.lufi99.org/pw/thread.php?fid=22&page={page}");
                var matches = reg.Matches(task.Result);
                foreach (Match match in matches)
                {
                    if (!links.Contains(match.Groups[1].ToString()))
                    {
                        Console.WriteLine($"page ${page}: yes http://n2.lufi99.org/pw/{match.Groups[1]} {match.Groups[2]}");
                        links.Add(match.Groups[1].ToString());
                    }
                    
                }

            });
        }
    }
}
