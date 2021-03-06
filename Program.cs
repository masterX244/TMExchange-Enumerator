using HtmlAgilityPack;
using MihaZupan;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace TM_ExchangeURLGrabber
{
    class Program
    {
        private static HttpClient client;
        private static readonly string magicXpath = "//input";
        private static readonly string magicXpath2 = "//select";
        private static readonly Dictionary<String, String> parameters = new Dictionary<string, string>
        {
            {"united","_ctl3$PageTracks" },
            {"tmnforever","ctl03$PageTracks" },
            {"nations","ctl03$PageTracks" },
            {"sunrise","ctl03$PageTracks" },
            {"original","ctl03$PageTracks" },
        };
        private static readonly string template = "goto|{0}|20";
        private static readonly int maxPages = Int32.MaxValue; //Int32.MaxValue on production run
        private static string[] prefixes = { "united", "tmnforever", "nations", "sunrise", "original" };


        static async Task Main(string[] args)
        {
            if(args.Length>2)
            {
                var proxy = new HttpToSocks5Proxy("127.0.0.1", 9050);
                var handler = new HttpClientHandler { Proxy = proxy };
                client = new HttpClient(handler, true);
            }
            else
            {
                client = new HttpClient();
            }

            
            var pageStart = 0;
            if(args.Length>1&&!(args[0]=="guess"))
            {
                pageStart = int.Parse(args[1]);
                Console.WriteLine(pageStart);
                Console.Error.WriteLine(pageStart);
            }
            if (args.Length>0&&(args[0] == "stage1"|| args[0] == "stage-1"))
            {
                foreach (var prefix in prefixes)
                {
                    if (File.Exists("./" + prefix + ".txt"))
                    {
                        //skip if exists for now, in future a incremental handler, used for recovery on a botched run
                        continue;
                    }
                    else
                    {
                        await Process(prefix, pageStart, args[0] == "stage-1");
                    }
                }
            }
            else if(args.Length > 0 && (args[0] == "guess"))
            {
                await GuessTrtkIdTMNF(args[1]);
            }


            else
            {
                foreach (var prefix in prefixes)
                {
                    if (File.Exists("./" + prefix + "-stage2.txt"))
                    {
                        //skip if exists for now, in future a incremental handler, used for recovery on a botched run
                        continue;
                    }
                    else
                    {
                        await ProcessStage2(prefix,pageStart);
                    }
                }
            }
        }


        /// <summary>
        /// Handles old TM-exchanges with only leaderboards on a track
        /// </summary>
        static async Task GuessTrtkIdTMNF(string rangestring)
        {
            var range = rangestring.Split("|");

            var from = int.Parse(range[0]);
            var to = int.Parse(range[1]);

            var trackinfostring = "Track Info";
            var foundUrls = new List<string>();
            for(int i=from;i<=to; i++)
            {
                var response = await client.GetAsync("https://tmnforever.tm-exchange.com/main.aspx?action=trackshow&id="+i+"#auto");
                var responseString = await response.Content.ReadAsStringAsync();
                if(responseString.Contains(trackinfostring))
                {
                    Console.WriteLine("https://tmnforever.tm-exchange.com/main.aspx?action=trackshow&id=" + i + "#auto");
                    foundUrls.Add("https://tmnforever.tm-exchange.com/main.aspx?action=trackshow&id=" + i + "#auto");
                }
                else
                {
                    Console.WriteLine("FAIL:" + i);
                }

            }
            File.WriteAllLines("tmnf-guessworks-" + from + "-" + to +".txt",foundUrls);
        }


        static async Task ProcessStage2(string prefix, int page)
        {
            var urls = new List<String>();
            urls.AddRange(File.ReadAllLines("./"+prefix + ".txt"));

            if (page > 0)
            {
                urls.RemoveRange(0, page);
            }
            var secondaryUrls = new List<String>();


            foreach (string url in urls)
            {
                var id = url.Split("id=")[1].Replace("#auto", "");
                Console.WriteLine("ID=" + id + ";" + page);
                page++;
                if (prefix == "united" || prefix == "tmnforever")
                {
                    var tmp = await ProcessStage2Modern(prefix, id,url.Replace("&amp;","&"));
                    secondaryUrls.AddRange(tmp);
                    Console.WriteLine("FIN MID=" + id);
                }
                else
                {
                    var tmp = await ProcessStage2Legacy(prefix, id, url.Replace("&amp;", "&"));
                    secondaryUrls.AddRange(tmp);
                    Console.WriteLine("FIN LID=" + id);
                }
                
            }
            File.WriteAllLines("./" + prefix + "-stage2.txt",secondaryUrls);
        }

        /// <summary>
        /// Handles old TM-exchanges with only leaderboards on a track
        /// </summary>
        static async Task<List<String>> ProcessStage2Legacy(string prefix, string trackid,string realurl)
        {
            //synthesizing 2 guaranteed URLs instead of hunting those down in the code
            List<String> returnUrls = new List<String> { "http://"+prefix+".tm-exchange.com/main.aspx?action=trackgbx&id="+trackid, "http://" + prefix + ".tm-exchange.com/get.aspx?action=trackgbx&id=" + trackid };
            // http://sunrise.tm-exchange.com/main.aspx?action=trackgbx&id=548627


            var response = await client.GetAsync(realurl);

            var responseString = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(responseString);

            returnUrls.AddRange(parseUrlMagicReplayLegacy(doc, prefix));
            return returnUrls;
        }


        private static List<string> parseUrlMagicReplayLegacy(HtmlDocument doc, string prefix)
        {
            var returnVal = new List<string>();
            // 
            var nodes = doc.DocumentNode.SelectNodes("//tr[contains(@class, 'WindowTableCell1') or contains(@class, 'WindowTableCell2')]/td[1]/a[1]");

            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    returnVal.Add("https://" + prefix + ".tm-exchange.com/" + node.Attributes["href"].Value.Replace("&amp;", "&"));
                    Console.WriteLine("https://" + prefix + ".tm-exchange.com/" + node.Attributes["href"].Value.Replace("&amp;", "&"));
                }
            }
            return returnVal;
        }


        /// <summary>
        /// Handles modern TM-Exchanges with replay searczh
        /// </summary>
        static async Task<List<String>> ProcessStage2Modern(string prefix, string trackid, string realurl)
        {
            // https://united.tm-exchange.com/main.aspx?action=trackreplayshow&id=3190144
            var replayurl = "http://" + prefix + ".tm-exchange.com/main.aspx?action=trackreplayshow&id=" + trackid;
            //synthesizing 2 guaranteed URLs instead of hunting those down in the code
            List<String> returnUrls = new List<String> {
                "http://" + prefix + ".tm-exchange.com/main.aspx?action=trackgbx&id=" + trackid, 
                "http://" + prefix + ".tm-exchange.com/get.aspx?action=trackgbx&id=" + trackid,
                replayurl
            };
            // http://sunrise.tm-exchange.com/main.aspx?action=trackgbx&id=548627


            var response = await client.GetAsync(replayurl);

            var responseString = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(responseString);

            returnUrls.AddRange(parseUrlMagicReplayModern(doc, prefix));
            return returnUrls;
        }

        private static List<string> parseUrlMagicReplayModern(HtmlDocument doc, string prefix)
        {
            Console.WriteLine("DeepDrill");
            var returnVal = new List<string>();
            // damnyou for hiding the data inside json....
            var node = doc.DocumentNode.SelectSingleNode("//input[@id=\"ctl03_ReplayData\"]") ?? doc.DocumentNode.SelectSingleNode("//input[@id=\"_ctl3_ReplayData\"]");
            // 

            if (node != null)
            {
                var json = node.Attributes["value"].Value.Replace("&quot;", "\"");
                JArray o = JArray.Parse(json);

                foreach (dynamic innerNode in o)
                {
                    int replayId = innerNode.ReplayId;
                    returnVal.Add("https://" + prefix + ".tm-exchange.com/get.aspx?action=recordgbx&id="+replayId);
                    Console.WriteLine("https://" + prefix + ".tm-exchange.com/get.aspx?action=recordgbx&id=" + replayId);
                }
            }
            else
            {
                Console.WriteLine("Shit");
            }
            return returnVal;
        }

        static async Task Process(string prefix,int page, bool revertSort)
        {
            string html = "";
            var urlsFound = new List<string>();
            //entrypoint: https://united.tm-exchange.com/main.aspx?action=tracksearch
            var response = await client.GetAsync("https://"+prefix+".tm-exchange.com/main.aspx?action=tracksearch");

            var responseString = await response.Content.ReadAsStringAsync();

            var doc = new HtmlDocument();
            doc.LoadHtml(responseString);
            var traxxetracks = int.Parse(doc.DocumentNode.SelectSingleNode("//*[contains(@id,'ShowMatches')]").InnerText.Replace(",", ""));

            File.WriteAllText("./" + prefix + "-Count.txt", traxxetracks+"");

            var postDict = parseForFormCrap(doc);
            Console.WriteLine(traxxetracks);
            var pages = traxxetracks / 20.0d;
            var pagesCeiled = (int)Math.Ceiling(pages); //decimals should be gone now;
            Console.WriteLine(pages + "|" + pagesCeiled);
            for(int i=page;i<Math.Min(pagesCeiled,maxPages);i++)
            {
                var temp = parseUrlMagic(doc,prefix);
                Console.WriteLine(temp.Count);
                
                urlsFound.AddRange(temp);
                var magic = string.Format(template, (i));
                Console.WriteLine(magic);
                //postDict.Add(parameter, magic);

                postDict.Add("__EVENTTARGET", parameters[prefix]);
                if (revertSort)
                {
                    i--;
                    revertSort = false;
                    postDict["__EVENTTARGET"] = "ctl03$DoSearch";
                }
                else
                {
                    postDict.Add("__EVENTARGUMENT", magic);
                }
                Console.WriteLine("Zapped: "+postDict.GetValueOrDefault("ctl03$GetOrderBy1", ""));

                postDict["ctl03$GetOrderBy1"] = "2";
                //postDict.Remove("ctl03$GetOrderBy1");
                postDict.Remove("ctl03$cblEnvironment$0");
                
                //postDict.Add("ctl03$GetOrderBy1", "2"); //HACK, reverse order
                Console.WriteLine("############ BEGIN P0ST################");
                
                foreach(var elem in postDict)
                {
                    Console.WriteLine(elem.Key+"|||||"+elem.Value);
                }

                Console.WriteLine("############ END P0ST################");
                if(i+1 == Math.Min(pagesCeiled, maxPages))
                {
                    break;
                }

                if (temp.Count==0)
                {
                    Console.WriteLine("############EARLY BREAK!!! (page:"+i+")################");
                    break;
                }

                response = await client.PostAsync("https://" + prefix + ".tm-exchange.com/main.aspx?action=auto#auto", new FormUrlEncodedContent(postDict));

                responseString = await response.Content.ReadAsStringAsync();

                doc.LoadHtml(responseString);
                Console.WriteLine(responseString);
                postDict = parseForFormCrap(doc);

            }

            Console.WriteLine(urlsFound.Count);

            foreach (var elem in urlsFound)
            {
                Console.WriteLine(elem);
            }
            File.WriteAllLines("./"+prefix+".txt",urlsFound);
        }


        private static List<string> parseUrlMagic(HtmlDocument doc, string prefix)
        {
            var returnVal = new List<string>();
            // 
            var nodes = doc.DocumentNode.SelectNodes("//tr[contains(@class, 'WindowTableCell1') or contains(@class, 'WindowTableCell2')]/td[1]/a[2]");

            if (nodes != null)
            {
                foreach (var node in nodes)
                {
                    returnVal.Add("https://" + prefix + ".tm-exchange.com/" + node.Attributes["href"].Value);
                    Console.WriteLine("https://" + prefix + ".tm-exchange.com/" + node.Attributes["href"].Value);
                }
            }
            return returnVal;
        }


        private static Dictionary<string,string> parseForFormCrap(HtmlDocument doc)
        {
            var nodes = doc.DocumentNode.SelectNodes(magicXpath);
            var postValues = new Dictionary<string, string>();

            foreach (var node in nodes)
            {
                postValues.Add(node.Attributes["name"].Value, node.Attributes["value"]?.Value ?? ((node.Attributes["checked"]?.Value) == "checked" ? "on" : ""));
                Console.WriteLine(node.Attributes["name"].Value);
                Console.WriteLine(node.Attributes["value"]?.Value ?? ((node.Attributes["checked"]?.Value) == "checked" ? "on" : ""));
            }

            nodes = doc.DocumentNode.SelectNodes(magicXpath2);
            foreach (var node in nodes)
            {
                Console.WriteLine(node.InnerHtml);
                var selection = node.SelectSingleNode(".//option[@selected]")?? node.SelectSingleNode(".//option[1]");

                postValues.Add(node.Attributes["name"].Value, selection.Attributes["value"]?.Value);
                Console.WriteLine(node.Attributes["name"].Value);
                Console.WriteLine(selection.Attributes["value"]?.Value);

            }
            return postValues;
        }
    }
}


/*
 var values = new Dictionary<string, string>
{
    { "thing1", "hello" },
    { "thing2", "world" }
};

var content = new FormUrlEncodedContent(values);

var response = await client.PostAsync("http://www.example.com/recepticle.aspx", content);

var responseString = await response.Content.ReadAsStringAsync();
 
 */