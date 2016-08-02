using CommandLine;
using CommandLine.Text;
using Grapevine;
using Grapevine.Client;
using Grapevine.Server;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Net;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PSScriptExecHttp
{
    public sealed class Context
    {
        private static readonly Lazy<Context> lazy = new Lazy<Context>(() => new Context());

        public static Context Instance { get { return lazy.Value; } }

        private Context() { }

        public Options Options { get; set; }
    }

    public sealed class PSScriptExecHttpResource : RESTResource
    {
        private List<string> RunScript(string script, Dictionary<string, string> param)
        {
            List<string> result = new List<string>();

            using (PowerShell PowerShellInstance = PowerShell.Create())
            {
                string ps1 = File.ReadAllText(script);

                PowerShellInstance.AddScript(ps1);

                foreach (KeyValuePair<string, string> entry in param)
                    PowerShellInstance.AddParameter(entry.Key, entry.Value);

                Collection<PSObject> PSOutput = PowerShellInstance.Invoke();

                foreach (PSObject outputItem in PSOutput)
                {
                    try
                    {
                        if (outputItem != null)
                        {
                            result.Add(outputItem.ToString());
                        }
                    }
                    catch (Exception e)
                    {
                        result.Add("Error: " + e.Message + "; StackTrace: " + e.StackTrace);
                        break;
                    }
                }

                if (PowerShellInstance.Streams.Error.Count > 0)
                {
                    foreach (ErrorRecord err in PowerShellInstance.Streams.Error)
                    {
                        try
                        {
                            if (err != null)
                            {
                                result.Add("Error (stream): " + err.ToString());
                            }
                        }
                        catch (Exception e)
                        {
                            result.Add("Error: " + e.Message + "; StackTrace: " + e.StackTrace);
                        }
                    }
                }

                return result;
            }
        }

        /// <summary>
        /// Run powershell script.
        /// </summary>
        /// <param name="context"></param>
        [RESTRoute(Method = HttpMethod.GET, PathInfo = @"^/runscript$")]
        public void RunScript(HttpListenerContext context)
        {
            Console.WriteLine("URL: {0}", context.Request.RawUrl);

            try
            {
                Dictionary<string, string> p = new Dictionary<string, string>();

                foreach (string k in context.Request.QueryString)
                {
                    Console.WriteLine("{0}: {1}", k, context.Request.QueryString[k]);
                    p.Add(k, context.Request.QueryString[k]);
                }

                if (Context.Instance.Options.LockExec)
                {
                    if (Context.Instance.Options.LockTimeout < 0)
                    {
                        System.Object lockObj = new object();

                        lock(lockObj)
                        {
                            SendTextResponse(context, String.Join("\n", RunScript("runscript.ps1", p).ToArray()));
                        }
                    }
                    else
                    {
                        System.Object lockObj = new object();

                        if (Monitor.TryEnter(lockObj, Context.Instance.Options.LockTimeout))
                        {
                            try
                            {
                                SendTextResponse(context, String.Join("\n", RunScript("runscript.ps1", p).ToArray()));
                            }
                            finally
                            {
                                Monitor.Exit(lockObj);
                            }
                        }
                        else
                        {
                            SendTextResponse(context, "Error: Cannot lock the script for execution.");
                        }
                    }

                    return;
                }

                // List<string> result = RunScript("runscript.ps1", p);

                SendTextResponse(context, String.Join("\n", RunScript("runscript.ps1", p).ToArray()));
            }
            catch (Exception e)
            {
                SendTextResponse(context, e.Message + "\n" + e.StackTrace);
            }
        }

        [RESTRoute]
        public void HandleAllGetRequests(HttpListenerContext context)
        {
            SendTextResponse(context, "No support");
        }
    }

    public class Options
    {
        [Option("server", DefaultValue = false, Required = false, HelpText = "Run as REST server.")]
        public bool RunAsServer { get; set; }

        [Option("host", DefaultValue = "localhost", Required = false, HelpText = "Set host IP.")]
        public string Host { get; set; }

        [Option("port", DefaultValue = "1234", Required = false, HelpText = "Set host port.")]
        public string Port { get; set; }

        [Option("url", DefaultValue = "/", Required = false,
            HelpText = @"URL after [host:port]. Should start with '/'.")]
        public string Url { get; set; }

        [Option("method", DefaultValue = "GET", Required = false, HelpText = "GET, POST.")]
        public string Method { get; set; }

        [Option("query", DefaultValue = "null", Required = false,
            HelpText = @"Key/value pairs as script parameters. Format: " +
            "[key1:val1^key2:val2[^keyn:valn]]. Enclose the whole query "+
            "input with double quotes.")]
        public string Query { get; set; }

        [Option("timeout", DefaultValue = -1, Required = false,
            HelpText = "Request timeout in milliseconds. When value is -1, client will use " +
            "the default timeout set in GrapeVine (1.21 seconds).")]
        public int Timeout { get; set; }

        [Option("maxthreads", DefaultValue = 10, Required = false,
            HelpText = "Maximum server threads when run as server.")]
        public int MaxThreads { get; set; }

        [Option("lock-exec", DefaultValue = false, Required = false,
            HelpText = "If true, script execution will be synchronized.")]
        public bool LockExec { get; set; }

        [Option("lock-timeout", DefaultValue = -1, Required = false,
            HelpText = "Timeout for 'lock-exec' option, in milliseconds. " + 
            "The default value -1 means infinite.")]
        public int LockTimeout { get; set; }

        [HelpOption]
        public string GetHelp()
        {
            return HelpText.AutoBuild(this, (HelpText current) => 
                HelpText.DefaultParsingErrorsHandler(this, current));
        }
    }

    class Program
    {
        static void Main(string[] args)
        {
            var exitEvent = new ManualResetEvent(false);
            var options = new Options();

            Context.Instance.Options = options;

            if (CommandLine.Parser.Default.ParseArgumentsStrict(args, options, () => { Environment.Exit(-2); }))
            {
                if (options.RunAsServer)
                {
                    // As server
                    Console.CancelKeyPress += (sender, eventArgs) => {
                        eventArgs.Cancel = true;
                        exitEvent.Set();
                    };

                    Console.WriteLine("Run server on " + options.Host + ":" + options.Port);
                    Console.WriteLine("Press CTRL+C to terminate server.\n");
                    Console.WriteLine("Host: {0}:{1}", options.Host, options.Port);

                    try
                    {
                        var server = new RESTServer();
                        server.Host = options.Host;
                        server.Port = options.Port;
                        server.MaxThreads = options.MaxThreads;
                        server.Start();

                        exitEvent.WaitOne();
                        server.Stop();
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message + "\n" + e.StackTrace);
                    }
                }
                else
                {
                    Dictionary<string, HttpMethod> method = new Dictionary<string, HttpMethod>()
                    {
                        { "GET", HttpMethod.GET }, { "POST", HttpMethod.POST }
                    };

                    // As client
                    try
                    {
                        var client = new RESTClient("http://" + options.Host + ":" + options.Port);

                        var request = new RESTRequest(options.Url);
                        request.Method = method[options.Method];
                        
                        if (options.Query != "null")
                        {
                            string[] queries = options.Query.Split('^');

                            foreach (string q in queries)
                            {
                                string[] kv = q.Split(':');                                
                                request.AddQuery(kv[0], kv[1]);

                                Console.WriteLine("query = {0}", q);
                                Console.WriteLine("  key = {0}", kv[0]);
                                Console.WriteLine("  val = {0}", kv[1]);
                            }                            
                        }

                        if (options.Timeout > 0)
                        {
                            request.Timeout = options.Timeout;
                        }
                        
                        var response = client.Execute(request);

                        Console.WriteLine("Response: " + response.Content);
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.Message + "\n" + e.StackTrace);
                    }                    
                }                
            }
        }
    }
}
