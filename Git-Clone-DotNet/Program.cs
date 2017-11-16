using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.XPath;
using HtmlAgilityPack;
using Newtonsoft.Json;

public class Program
{
    static string _workingDirectory = @"c:\projects\repos";
    public static void Main()
    {


        var program = new Program();



        IEnumerable<string> repoNames = new List<string>();
        var task = Task.Run(() =>
        {
            repoNames = program.GetRepoNamesFromGitLab().Result;
        });

        Task.WaitAll(task);

        var ignoreRepos = program.GetIgnoredRepos().ToList();



        var reposToProcess = repoNames
            .Where(c => !ignoreRepos.Any(d => c.Contains(d)))
            .Where(c => !c.ToUpper().Contains("DEPRECATED"))
            .Where(c => !c.ToUpper().Contains("ARCHIVE"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/CONFIG"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/AUTHENTICATION"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/COMMON"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/DIRECTORY"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/DOMAIN"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/GWT"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/IDM"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/LEGACY"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/MAVEN"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/PROVISIONING"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/SHIBBOLETH"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/WEBAPPS"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/USERS_"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/PORTAL"))
            .Where(c => !c.ToUpper().StartsWith("IDMAN/OPENSHIFT-STARTER"))
            .OrderBy(c => c)
            .ToList();




        foreach (var project in reposToProcess)
        {
            program.ExecuteCloneRepo(project);
            Console.WriteLine(project);


        }
        Console.WriteLine("DONE: Press Enter To Exit");

        Console.ReadLine();


    }

    private IEnumerable<string> GetIgnoredRepos()
    {
        var jsonList = GetType().Assembly.GetManifestResourceStream("Git-Clone-DotNet.IgnoreList.json");
        using (var reader = new StreamReader(jsonList, Encoding.UTF8))
        {
            var result = reader.ReadToEnd();
            return JsonConvert.DeserializeObject<List<string>>(result);
        }
    }


    private string GetJavaAppsCommaDelimited()
    {
        var javaDirectories = GetReposWithJavaContent();
        var sb = new StringBuilder();
        foreach (var javaDirectory in javaDirectories.Distinct())
        {
            sb.Append($"\"{javaDirectory}\",");
            sb.Append("\n");
        }

        return sb.ToString();
    }
    private IEnumerable<string> GetReposWithJavaContent()
    {
        var directorInfo = new DirectoryInfo(_workingDirectory);
        var directories = directorInfo.GetDirectories("java*", SearchOption.AllDirectories);

        foreach (var directoryInfo in directories)
        {
            var val = new DirectoryInfo(directoryInfo.Parent.Parent.Parent.FullName);
            if (val.GetDirectories("src").Any())
            {
                yield return directoryInfo.Parent.Parent.Parent.Name;
            }
        }




    }
    private void ExecuteCloneRepo(string repo)
    {
        var startInfo = new ProcessStartInfo
        {
            WindowStyle = ProcessWindowStyle.Normal,
            FileName = "cmd.exe",
            WorkingDirectory = _workingDirectory,
            Arguments = "/c git clone https://sc.its.unc.edu/" + repo
        };

        //Argument list
        //https://www.microsoft.com/resources/documentation/windows/xp/all/proddocs/en-us/cmd.mspx?mfr=true
        //git clone https://bdarley@sc.its.unc.edu/idman/webapps-improvapi-gwt

        Process.Start(startInfo);


    }

    private async Task<IEnumerable<string>> GetRepoNamesFromGitLab()
    {
        var cookies = new CookieContainer();
        var handler = new HttpClientHandler { CookieContainer = cookies };
        var projects = new List<string>();
        var page = 1;
        var currentProjectCount = 0;
        using (var client = new HttpClient(handler))
        {
            var webRequest = await AuthenticateToSourceControl(client);

            while (!string.IsNullOrEmpty(webRequest))
            {

                if (page != 1)
                {
                    webRequest = await GetRepositoryForPage(client, page);
                }

                var projectList = GetProjects(webRequest);


                foreach (var project in projectList)
                {
                    if (!string.IsNullOrEmpty(project))
                    {
                        projects.Add(project.Replace("\n", ""));
                    }

                }
                if (currentProjectCount == projects.Count)
                {
                    break;
                }
                currentProjectCount = projects.Count;

                page++;
            }

            return projects;
        }
    }

    private static async Task<string> AuthenticateToSourceControl(HttpClient client)
    {
        try
        {
            string username, password;
            Console.WriteLine("Enter GIT Username");
            username = Console.ReadLine();
            Console.WriteLine("Enter GIT Password");
            password = Console.ReadLine();

            var response = await client.GetAsync("https://sc.its.unc.edu");
            var webData = await response.Content.ReadAsStringAsync();

            var index1 = webData.IndexOf("name=\"authenticity_token");
            var index2 = webData.IndexOf(" />", index1);
            var fieldValue = webData.Substring(index1, index2 - index1);
            fieldValue = Regex.Match(fieldValue, "value=\"([^\"]+)").Groups[1].Value;


            var pairs = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("utf8", "✓"),
                new KeyValuePair<string, string>("username", username),
                new KeyValuePair<string, string>("password", password),
                new KeyValuePair<string, string>("authenticity_token", fieldValue)
            };

            var content = new FormUrlEncodedContent(pairs);

            response = await client.PostAsync("https://sc.its.unc.edu/users/auth/ldapmain/callback", content);
            response.EnsureSuccessStatusCode();
            var result = await response.Content.ReadAsStringAsync();
            return result;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }
    private static async Task<string> GetRepositoryForPage(HttpClient client, int page)
    {
        try
        {
            var response = await client.GetAsync($"https://sc.its.unc.edu/?non_archived=true&page={page}&sort=latest_activity_desc");
            response.EnsureSuccessStatusCode();

            var result = await response.Content.ReadAsStringAsync();
            return result;
        }
        catch (Exception ex) when (ex.Message.Contains("404"))
        {
            return string.Empty;
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex);
            throw;
        }
    }
    private IEnumerable<string> GetProjects(string html)
    {

        //var list = Regex.Match(result, )
        //project-full-name
        var doc = new HtmlDocument();
        doc.LoadHtml(html);
        //var value = doc.DocumentNode
        //    .SelectNodes("//span[class=\"project-full-name\"]");
        HtmlNodeNavigator navigator = (HtmlNodeNavigator)doc.CreateNavigator();

        //Get value from given xpath
        var xpath = "//span[@class='project-full-name']";
        var nodes = navigator.Select(xpath);

        foreach (XPathNavigator node in nodes)
        {
            yield return (string)node.TypedValue;
            //yield return node
        }
        yield return string.Empty;


    }
}