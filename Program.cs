using HtmlAgilityPack;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace TM_ExchangeURLGrabber
{
    class Program
    {
        private static readonly HttpClient client = new HttpClient();
        private static readonly string magicXpath = "//input";
        private static readonly string magicXpath2 = "//select";
        private static readonly string parameter = "_ctl3$PageTracks";
        private static readonly string template = "goto|{0}|20";
        private static readonly int maxPages = Int32.MaxValue; //MAXINT on production run
        private static string[] prefixes = { "united", "tmnforever", "nations", "sunrise", "original" };


        static async Task Main(string[] args)
        {
            foreach(var prefix in prefixes)
            {
                await Process(prefix);
            }
        }

        static async Task Process(string prefix)
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
            for(int i=1;i<Math.Min(pagesCeiled,maxPages);i++)
            {
                var temp = parseUrlMagic(doc,prefix);
                Console.WriteLine(temp.Count);
                
                urlsFound.AddRange(temp);
                var magic = string.Format(template, (i));
                Console.WriteLine(magic);
                //postDict.Add(parameter, magic);

                postDict.Add("__EVENTTARGET", parameter);
                postDict.Add("__EVENTARGUMENT", magic);

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