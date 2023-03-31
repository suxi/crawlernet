using System;
using System.Linq;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks.Dataflow;
using Microsoft.Data.Sqlite;
using System.Collections.Generic;

var HOST = "https://mk2207.work/pw/";//"http://b11.hj97zhx837.xyz/pw/";
var key = "";
var FID = 22;
var page = 1;
var skip = 0;
var force = false;

var client = new HttpClient(new HttpClientHandler { AllowAutoRedirect = true }) { Timeout = new TimeSpan(0, 0, 30) };
client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/15.0 Safari/605.1.15");
var hitExists = false;

foreach (var arg in args)
{
    if (arg.ToLower().StartsWith("-f"))
    {
        force = true;
    }
    else if (arg.ToLower().StartsWith("--ln"))
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
    else if (arg.ToLower().StartsWith("-k"))
    {
        key = arg.Substring(2);

    }
    else
    {
        var p = int.Parse(arg.ToString());
        skip = (p - 1);
    }

}

var TIMEOUT = new TimeSpan(0, 3, 0);
var urls = new Stack<Tuple<string, string>>();

var save = new ActionBlock<Tuple<string, string>>(async link =>
{
    if (link.Item1 == "")
    {
        return;
    }
    using (var sqlite = new SqliteConnection("Data Source=1024.db"))
    {
        sqlite.Open();
        using (var comm = sqlite.CreateCommand())
        {
            comm.CommandText = @"INSERT INTO url(link,title,creatd_at) VALUES($link,$title,$date) ON CONFLICT(link) DO UPDATE SET title = $title";
            comm.Parameters.AddWithValue("$link", link.Item1);
            comm.Parameters.AddWithValue("$title", link.Item2);
            comm.Parameters.AddWithValue("$date", DateTime.Now.ToShortDateString());
            await comm.ExecuteNonQueryAsync();
        }
    }
}, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 8 });

var dump = new TransformBlock<Tuple<string, string>, Tuple<string, string>>(async link =>
{

    var text = await client.GetStringAsync($"{HOST}{link.Item1}");
    var htmlDoc = new HtmlAgilityPack.HtmlDocument();
    htmlDoc.LoadHtml(text);
    var pic = htmlDoc.DocumentNode.SelectNodes("//img[@onload]");
    if (pic == null)
    {
        return Tuple.Create("", "");
    }
    var filePath = "./img/" + link.Item1.Substring(link.Item1.LastIndexOf('/') + 1).Replace(".html", ".jpg");
    if (!File.Exists(filePath))
    {
        try
        {
            using (var st = await client.GetStreamAsync(pic[0].Attributes["src"].Value))
            {
                using (var fs = new FileStream("./img/" + link.Item1.Substring(link.Item1.LastIndexOf('/') + 1).Replace(".html", ".jpg"), FileMode.OpenOrCreate))
                {

                    await st.CopyToAsync(fs);
                    fs.Close();
                    Console.WriteLine("dump " + pic[0].Attributes["src"].Value);



                }
            }
        }
        catch (System.Exception e)
        {
            Console.WriteLine(e.Message);
        }
    }
    else
    {
        Console.WriteLine("found " + filePath);
    }
    return link;
}, new ExecutionDataflowBlockOptions { MaxDegreeOfParallelism = 3 });

var download = new TransformBlock<string, string>(async uri =>
{
    try
    {
        Thread.Sleep(1 * 1000);
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
                Array.ForEach(items.ToArray(), async item =>
                {
                    var link = $"{item.Attributes["href"].Value}".Replace("&amp;", "&");
                    var title = item.InnerText.Replace("&nbsp;", " ");
                    using (var comm = sqlite.CreateCommand())
                    {
                        comm.CommandText = @"SELECT count(*) from url where link = $link";
                        comm.Parameters.AddWithValue("$link", link);
                        if ((long)(await comm.ExecuteScalarAsync())! == 0 || force)
                        {
                            if (item.Attributes["href"].Value.StartsWith("html_data/"))
                            {
                                // dump.Post(Tuple.Create(link,title));
                                urls.Push(Tuple.Create(link, title));
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
        if ((!hitExists || force) && page < 35)
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
dump.LinkTo(save, new DataflowLinkOptions { PropagateCompletion = true });

Console.WriteLine($"fetch page1");
download.Post($"{HOST}thread.php?fid={FID}&page={page}");
fetch.Completion.Wait();

while (urls.Count > 0)
{
    dump.Post(urls.Pop());
}
dump.Complete();
dump.Completion.Wait();
save.Completion.Wait();

using (var sqlite = new SqliteConnection("Data Source=1024.db"))
{
    var p = key.Length > 0 ? 50 : 200;
    sqlite.Open();
    var comm = sqlite.CreateCommand();
    comm.CommandText = $"select * from url where title like '%{key}%' or title like '%{key.Replace("-", "")}%' order by link desc limit {skip * p},{p}";
    using (var reader = await comm.ExecuteReaderAsync())
    {
        while (await reader.ReadAsync())
        {
            var date =  (reader.IsDBNull(2) ? "" : reader.GetString(2));
            Console.WriteLine($"【 {HOST}{reader.GetString(0)} 】({date}){reader.GetString(1)}");
        };
    }
}

Console.WriteLine($"[搜索完成]");

