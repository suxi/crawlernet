using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks.Dataflow;

var HOST = "https://m3.g126eaf0.link/pw/";
var key = "";
var FID = 22;

var client = new HttpClient { Timeout = new TimeSpan(0, 0, 30) };
client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/15.0 Safari/605.1.15");
var links = new ConcurrentBag<string>();
var range = 1000;

var redis = StackExchange.Redis.ConnectionMultiplexer.Connect("localhost");
foreach (var arg in args)
{
    if (arg.ToLower().StartsWith("--ln"))
    {
        FID = 188;
    }
    else if (arg.ToLower().StartsWith("--wt"))
    {
        FID = 30;
    }
    else if (arg.ToLower().StartsWith("--p"))
    {
        FID = 21;
    }
    else if (arg.ToLower().StartsWith("-p"))
    {
        int.TryParse(arg.Substring(2), out range);
    }
    else
    {
        key = arg.ToString();
    }

}

var index = Enumerable.Range(1, range).ToArray();
var part = Partitioner.Create<int>(index);

var TIMEOUT = new TimeSpan(0, 3, 0);

var download = new TransformBlock<string, string>(async uri =>
{
    try
    {

        var db = redis.GetDatabase();
        if (!db.KeyExists(uri))
        {
            Thread.Sleep(2 * 1000);
            var text = await client.GetStringAsync(uri);
            if (text.IndexOf("ROBOTS") > 0)
            {
                Console.WriteLine($"{uri} anti-robots");
                return "";
            }
            else
            {
                db.StringSet(uri, text);
                db.KeyExpire(uri, new TimeSpan(3, 0, 0));
                return text;
            }
        }
        else
        {
            return db.StringGet(uri);
        }
    }
    catch (Exception e)
    {
        Console.WriteLine($"{uri} {e.Message}");
        return "";
    }

}, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });

var grep = new ActionBlock<string>(text =>
{
    if (!String.IsNullOrWhiteSpace(text))
    {
        var htmlDoc = new HtmlAgilityPack.HtmlDocument();
        htmlDoc.LoadHtml(text);
        var items = htmlDoc.DocumentNode.SelectNodes("//a[@href and parent::h3]");
        var searchPattern = key == "" ? "" : $"{key}[-]?\\d{{0,4}}";
        if (FID == 21)
        {
            searchPattern = key == "" ? "" : $"{key}";
        }
        var reg = new Regex(
            searchPattern,
            RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);
        var bangoReg = new Regex(@"[a-zA-Z]{2,5}-?\d{3,5}[ABab]?", RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);
        if (items != null)
        {
            Array.ForEach(items.ToArray(), item =>
           {
               if (item.InnerLength > 0 && item.InnerText.Length > 0)
               {
                   var match = reg.Match(item.InnerText);
                   if (match.Success)
                   {
                       var link = $"{HOST}{item.Attributes["href"].Value}".Replace("&amp;", "&");
                       var title = item.InnerText.Replace("&nbsp;", " ");
                       var name = bangoReg.Match(title).Value;
                       var cover = $"http://www.dmm.co.jp/search/=/searchstr={name}";
                       if (!links.Contains(link))
                       {
                           links.Add(link);
                           Console.WriteLine($"【 {link} 】{title}");
                       }
                   }
               }
           });
        }
    }
}, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });

download.LinkTo(grep, new DataflowLinkOptions { PropagateCompletion = true });


var stop = Stopwatch.StartNew();
// Parallel.ForEach(part, page => 
foreach (var page in Enumerable.Range(1, range))
{
    download.Post($"{HOST}thread.php?fid={FID}&page={page}");
};
download.Complete();
grep.Completion.Wait();
stop.Stop();
Console.WriteLine($"[搜索完成(by {links.Count} in {stop.ElapsedMilliseconds:N}ms)]");

