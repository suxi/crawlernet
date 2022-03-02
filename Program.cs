using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using Microsoft.Data.Sqlite;

var HOST = "http://b11.hj97zhx837.xyz/pw/";
var key = "";
var FID = 22;
var page = 1;
var skip = 0;

var client = new HttpClient { Timeout = new TimeSpan(0, 0, 30) };
client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/15.0 Safari/605.1.15");
var hitExists = false;

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
        var p = int.Parse(arg.Substring(2));
        skip = (p-1);
    }
    else
    {
        key = arg.ToString();
    }

}

var TIMEOUT = new TimeSpan(0, 3, 0);

var download = new TransformBlock<string, string>(async uri =>
{
    try
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
            return text;
        }

    }
    catch (Exception e)
    {
        Console.WriteLine($"{uri} {e.Message}");
        return "";
    }

}, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });

var fetch = new ActionBlock<string>(html =>
{
    if (!String.IsNullOrWhiteSpace(html))
    {
        var htmlDoc = new HtmlAgilityPack.HtmlDocument();
        htmlDoc.LoadHtml(html);
        var items = htmlDoc.DocumentNode.SelectNodes("//a[@href and parent::h3]");
        if (items != null)
        {
            using (var sqlite = new SqliteConnection("Data Source=1024.db"))
            {
                sqlite.Open();
                Array.ForEach(items.ToArray(), item =>
                {
                    var link = $"{item.Attributes["href"].Value}".Replace("&amp;", "&");
                    var title = item.InnerText.Replace("&nbsp;", " ");
                    using(var comm = sqlite.CreateCommand())
                    {
                        comm.CommandText = @"SELECT count(*) from url where link = $link";
                        comm.Parameters.AddWithValue("$link",link);
                        if ((long)comm.ExecuteScalar()! == 0)
                        {
                            if (item.Attributes["href"].Value.StartsWith("html_data/"))
                            {
                                comm.CommandText = @"INSERT INTO url(link,title) VALUES($link,$title)";
                                // comm.Parameters.AddWithValue("$link", link);
                                comm.Parameters.AddWithValue("$title", title);
                                comm.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            hitExists = true;
                        }
                    }

                });
            }

        }
        if (!hitExists && page <= 500)
        {
            download.Post($"{HOST}thread.php?fid={FID}&page={++page}");
            Console.WriteLine($"fetch page{page}");
        }
        else
        {
            download.Complete();
        }
    }

}, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 1 });

download.LinkTo(fetch, new DataflowLinkOptions { PropagateCompletion = true });

download.Post($"{HOST}thread.php?fid={FID}&page={page}");
fetch.Completion.Wait();

var searchPattern = key == "" ? "" : $"{key}[-]?\\d{{0,4}}";
var reg = new Regex(
    searchPattern,
    RegexOptions.IgnoreCase | RegexOptions.IgnorePatternWhitespace | RegexOptions.Compiled);

using (var sqlite = new SqliteConnection("Data Source=1024.db"))
{
    sqlite.Open();
    var comm = sqlite.CreateCommand();
    comm.CommandText = $"select * from url order by link desc limit {skip*50},50";
    using (var reader = comm.ExecuteReader())
    {
        while(reader.Read())
        {
            if (!String.IsNullOrWhiteSpace(reader.GetString(1)))
            {
                var match = reg.Match(reader.GetString(1));
                if (match.Success)
                {
                    Console.WriteLine($"【 {HOST}{reader.GetString(0)} 】{reader.GetString(1)}");
                }
            }
        };
    }    
}

Console.WriteLine($"[搜索完成]");

