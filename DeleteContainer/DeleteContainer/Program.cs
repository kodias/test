using System;
using System.Collections;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Xml;
namespace CloudFiles_MassDelete
{
    internal class Program
    {
        public class RequestState
        {
            public HttpWebRequest request;
            public object data;
            public RequestState(HttpWebRequest request, object data)
            {
                this.request = request;
                this.data = data;
            }
        }
        private static Queue object_names_unsync = new Queue();
        private static Queue object_names = Queue.Synchronized(Program.object_names_unsync);
        private static Queue rate_history_unsync = new Queue();
        private static Queue rate_history = Queue.Synchronized(Program.rate_history_unsync);
        private static bool endoflist = false;
        private static string username = "";
        private static string apikey = "";
        private static bool UK = false;
        private static string auth_key = "";
        private static string storage_url = "";
        private static string container = "";
        private static int rate = 1;
        private static int threads = 100;
        private static int initial_count = 0;
        private static int current_count = 0;
        private static int count_diff = 0;
        private static int deleted = 0;
        private static Mutex deleted_mutex = new Mutex();
        private static int max_pending = 300;
        private static int pending_requests = 0;
        private static Mutex pending_requests_mutex = new Mutex();
        private static int rate_allowance = 0;
        private static int max_allowance = 20;
        private static Mutex rate_check_mutex = new Mutex();
        public static void die()
        {
            Console.WriteLine("Usage: " + AppDomain.CurrentDomain.FriendlyName + " username apikey container [UK]");
            Environment.Exit(1);
        }
        public static void Main(string[] args)
        {
            if (args.Length < 3 || args.Length > 4)
            {
                Program.die();
                return;
            }
            Console.Clear();
            int num;
            int num2;
            ThreadPool.GetMaxThreads(out num, out num2);
            if (num < Program.threads + 5)
            {
                num = Program.threads + 5;
            }
            if (num2 < Program.threads + 5)
            {
                num2 = Program.threads + 5;
            }
            ThreadPool.SetMaxThreads(num, num2);
            ServicePointManager.ServerCertificateValidationCallback = ((object obj, X509Certificate x509Certificate, X509Chain x509Chain, SslPolicyErrors sslPolicyErrors) => true);
            Console.WriteLine("Authenticating using rackspace API");
            Program.username = args[0];
            Program.apikey = args[1];
            if (args.Length == 3)
            {
                Program.UK = false;
            }
            else
            {
                if (args.Length == 4)
                {
                    Program.UK = true;
                }
            }
            Program.get_auth_key();
            Program.container = args[2];
            Console.WriteLine("Authenticated!");
            Program.reset_servicepoint();
            Thread thread = new Thread(new ParameterizedThreadStart(Program.status));
            thread.Start();
            Thread.Sleep(100);
            Thread thread2 = new Thread(new ParameterizedThreadStart(Program.ratelimit_manager));
            thread2.Start();
            Thread.Sleep(100);
            Thread thread3 = new Thread(new ParameterizedThreadStart(Program.get_object_list));
            thread3.Start();
            Thread.Sleep(100);
            Console.WriteLine("Deleting objects...");
            while (Program.object_names.Count > 0 || !Program.endoflist || Program.pending_requests > 0)
            {
                if (Program.object_names.Count > 0 && Program.pending_requests < Program.threads)
                {
                    while (Program.rate != 1 && (Program.object_names.Count > 0 || !Program.endoflist))
                    {
                        Program.rate_check_mutex.WaitOne();
                        if (Program.rate_allowance > 0)
                        {
                            Program.rate_allowance--;
                            Program.rate_check_mutex.ReleaseMutex();
                            break;
                        }
                        Program.rate_check_mutex.ReleaseMutex();
                        Thread.Sleep(10);
                    }
                    Program.delete_object(Program.object_names.Dequeue());
                }
                else
                {
                    Thread.Sleep(10);
                }
            }
            thread3.Join();
            thread2.Join();
            thread.Join();
        }
        public static void reset_servicepoint()
        {
            ServicePoint servicePoint = ServicePointManager.FindServicePoint(new Uri(Program.storage_url));
            servicePoint.ConnectionLimit = 2147483647;
        }
        public static void get_object_list(object threadContext)
        {
            Console.WriteLine("Grabing object list...");
            while (true)
            {
                HttpWebRequest httpWebRequest = WebRequest.Create(Program.storage_url + "/" + Program.container) as HttpWebRequest;
                httpWebRequest.Method = "HEAD";
                httpWebRequest.Headers.Add("X-Storage-Token: " + Program.auth_key);
                try
                {
                    using (WebResponse response = httpWebRequest.GetResponse())
                    {
                        response.Close();
                        Program.initial_count = int.Parse(response.Headers["X-Container-Object-Count"]);
                        break;
                    }
                }
                catch (Exception ex)
                {
                    if (ex.ToString().Contains("The request timed out"))
                    {
                        Console.WriteLine("Timeout while checking object count!");
                    }
                    else
                    {
                        if (ex.ToString().Contains("404"))
                        {
                            Console.WriteLine("404 error while checking object count!");
                            Environment.Exit(1);
                        }
                        else
                        {
                            if (ex.ToString().Contains("500"))
                            {
                                Console.WriteLine("500 error while checking object count!");
                            }
                            else
                            {
                                Console.WriteLine("HTTP request error while checking object count!");
                                Console.WriteLine("Error: " + ex.ToString());
                            }
                        }
                    }
                }
            }
            string text = "";
            while (true)
            {
                HttpWebRequest httpWebRequest = WebRequest.Create(string.Concat(new string[]
				{
					Program.storage_url,
					"/",
					Program.container,
					"?marker=",
					text,
					"&format=xml"
				})) as HttpWebRequest;
                httpWebRequest.Headers.Add("X-Storage-Token: " + Program.auth_key);
                httpWebRequest.Timeout = 3600000;
                try
                {
                    using (WebResponse response2 = httpWebRequest.GetResponse())
                    {
                        Stream responseStream = response2.GetResponseStream();
                        StreamReader streamReader = new StreamReader(responseStream);
                        XmlDocument xmlDocument = new XmlDocument();
                        xmlDocument.Load(streamReader);
                        XmlNodeList elementsByTagName = xmlDocument.GetElementsByTagName("name");
                        if (elementsByTagName.Count == 0)
                        {
                            Program.endoflist = true;
                            break;
                        }
                        foreach (XmlNode xmlNode in elementsByTagName)
                        {
                            string text2 = xmlNode.InnerText;
                            text2 = Uri.EscapeUriString(text2);
                            Program.object_names.Enqueue(text2);
                            text = text2;
                        }
                        streamReader.Close();
                        response2.Close();
                    }
                }
                catch (Exception ex2)
                {
                    if (ex2.ToString().Contains("404"))
                    {
                        Console.WriteLine("404 error while listing container: " + Program.container);
                        Program.endoflist = true;
                        break;
                    }
                    Console.WriteLine("HTTP request error while listing: " + Program.container);
                    Console.WriteLine("Error: " + ex2.ToString());
                }
            }
            Console.WriteLine("Finished grabing object list!");
        }
        public static void status(object threadContext)
        {
            Console.WriteLine("Starting to delete...");
            int count = Program.object_names.Count;
            int num = Environment.TickCount;
            HttpWebRequest httpWebRequest;
            while (true)
            {
                Program.reset_servicepoint();
                httpWebRequest = (WebRequest.Create(Program.storage_url + "/" + Program.container) as HttpWebRequest);
                httpWebRequest.Method = "HEAD";
                httpWebRequest.Headers.Add("X-Storage-Token: " + Program.auth_key);
                httpWebRequest.Timeout = 60000;
                try
                {
                    using (WebResponse response = httpWebRequest.GetResponse())
                    {
                        Program.current_count = int.Parse(response.Headers["X-Container-Object-Count"]);
                        response.Close();
                    }
                }
                catch (Exception ex)
                {
                    if (ex.ToString().Contains("The request timed out"))
                    {
                        Console.WriteLine("Timeout while checking object count!");
                    }
                    else
                    {
                        if (ex.ToString().Contains("400"))
                        {
                            Console.WriteLine("400 error while checking object count!");
                        }
                        else
                        {
                            if (ex.ToString().Contains("401"))
                            {
                                Console.WriteLine("401 error while checking object count!");
                                Program.get_auth_key();
                            }
                            else
                            {
                                if (ex.ToString().Contains("404"))
                                {
                                    Console.WriteLine("404 error while checking object count!");
                                }
                                else
                                {
                                    if (ex.ToString().Contains("500"))
                                    {
                                        Console.WriteLine("500 error while checking object count!");
                                    }
                                    else
                                    {
                                        Console.WriteLine("HTTP request error while checking object count!");
                                        Console.WriteLine("Error: " + ex.ToString());
                                    }
                                }
                            }
                        }
                    }
                }
                Program.deleted_mutex.WaitOne();
                Program.count_diff = Program.deleted - (Program.initial_count - Program.current_count);
                if (Program.count_diff > Program.max_pending)
                {
                    Program.rate *= 2;
                }
                if (Program.count_diff < Program.max_pending)
                {
                    Program.rate = (int)((double)Program.rate * 0.5);
                }
                if (Program.rate < 1)
                {
                    Program.rate = 1;
                }
                if (Program.rate > 1000)
                {
                    Program.rate = 1000;
                }
                int tickCount = Environment.TickCount;
                if (Program.object_names.Count == 0 && Program.endoflist && Program.pending_requests == 0)
                {
                    break;
                }
                int num2 = (Program.deleted - count) * 1000 / (tickCount - num);
                Program.rate_history.Enqueue(num2);
                while (Program.rate_history.Count > 20)
                {
                    Program.rate_history.Dequeue();
                }
                int num3 = 0;
                for (int i = 0; i < Program.rate_history.Count; i++)
                {
                    num3 += (int)Program.rate_history.ToArray()[i];
                }
                num3 /= Program.rate_history.Count;
                DateTime dateTime = (num3 <= 0) ? DateTime.MaxValue : DateTime.Now.AddSeconds((double)((Program.initial_count - Program.deleted) / num3));
                Console.WriteLine(new string('-', Console.BufferWidth - 1));
                Console.WriteLine(string.Concat(new object[]
				{
					"Deleted: ",
					Program.deleted,
					"/",
					Program.initial_count,

				}));
                Console.WriteLine(string.Concat(new object[]
				{
					"Req/s (curr/avg): ",
					num2,
					"/",
					num3,
				}));
                count = Program.deleted;
                num = tickCount;
                Program.deleted_mutex.ReleaseMutex();
                Thread.Sleep(5000);
            }
            Program.deleted_mutex.ReleaseMutex();
            Console.WriteLine("Finished deleting objects!");
            Thread.Sleep(2000);
            Console.WriteLine("Deleting container " + Program.container + "...");
            httpWebRequest = (WebRequest.Create(Program.storage_url + "/" + Program.container) as HttpWebRequest);
            httpWebRequest.Headers.Add("X-Storage-Token: " + Program.auth_key);
            httpWebRequest.Method = "DELETE";
            httpWebRequest.Timeout = 30000;
            try
            {
                using (WebResponse response2 = httpWebRequest.GetResponse())
                {
                    response2.Close();
                }
            }
            catch (Exception ex2)
            {
                if (ex2.ToString().Contains("404"))
                {
                    Console.WriteLine("404 error while deleting container: " + Program.container);
                }
                else
                {
                    if (ex2.ToString().Contains("409"))
                    {
                        Console.WriteLine("409 error while deleting container: " + Program.container);
                    }
                    else
                    {
                        Console.WriteLine("HTTP request error while deleting: " + Program.container);
                        Console.WriteLine("Error: " + ex2.ToString());
                    }
                }
            }
            Console.WriteLine("Done, you should now pay me a beer!");
        }
        public static void ratelimit_manager(object threadContext)
        {
            
            while (Program.object_names.Count > 0 || !Program.endoflist || Program.pending_requests > 0)
            {
                Program.rate_check_mutex.WaitOne();
                if (Program.rate > 1)
                {
                    Program.rate_allowance++;
                }
                if (Program.rate_allowance > Program.max_allowance)
                {
                    Program.rate_allowance = Program.max_allowance;
                }
                Program.rate_check_mutex.ReleaseMutex();
                Thread.Sleep((Program.rate <= 1) ? 100 : Program.rate);
            }
            
        }
        public static void delete_object(object threadContext)
        {
            Program.pending_requests_mutex.WaitOne();
            Program.pending_requests++;
            Program.pending_requests_mutex.ReleaseMutex();
            string text = (string)threadContext;
            HttpWebRequest httpWebRequest = WebRequest.Create(string.Concat(new string[]
			{
				Program.storage_url,
				"/",
				Program.container,
				"/",
				text
			})) as HttpWebRequest;
            httpWebRequest.Headers.Add("X-Storage-Token: " + Program.auth_key);
            httpWebRequest.Method = "DELETE";
            Program.RequestState state = new Program.RequestState(httpWebRequest, text);
            Program.deleted_mutex.WaitOne();
            Program.deleted++;
            Program.deleted_mutex.ReleaseMutex();
            httpWebRequest.BeginGetResponse(new AsyncCallback(Program.delete_object_callback), state);
        }
        private static void delete_object_callback(IAsyncResult result)
        {
            Program.pending_requests_mutex.WaitOne();
            Program.pending_requests--;
            Program.pending_requests_mutex.ReleaseMutex();
            Program.RequestState requestState = (Program.RequestState)result.AsyncState;
            string text = (string)requestState.data;
            HttpWebResponse httpWebResponse = (HttpWebResponse)requestState.request.EndGetResponse(result);
            httpWebResponse.Close();
            if (httpWebResponse.StatusCode != HttpStatusCode.OK && httpWebResponse.StatusCode != HttpStatusCode.NoContent)
            {
                if (httpWebResponse.StatusCode == HttpStatusCode.GatewayTimeout || httpWebResponse.StatusCode == HttpStatusCode.RequestTimeout)
                {
                    Console.WriteLine("Timeout while deleting:   " + text);
                    Program.object_names.Enqueue(text);
                    Program.deleted_mutex.WaitOne();
                    Program.deleted--;
                    Program.deleted_mutex.ReleaseMutex();
                }
                else
                {
                    if (httpWebResponse.StatusCode == HttpStatusCode.BadRequest)
                    {
                        Console.WriteLine("400 error while deleting: " + text);
                    }
                    else
                    {
                        if (httpWebResponse.StatusCode == HttpStatusCode.Forbidden)
                        {
                            Console.WriteLine("401 error while deleting: " + text);
                            Program.get_auth_key();
                        }
                        else
                        {
                            if (httpWebResponse.StatusCode == HttpStatusCode.NotFound)
                            {
                                Console.WriteLine("404 error while deleting: " + text);
                            }
                            else
                            {
                                if (httpWebResponse.StatusCode == HttpStatusCode.InternalServerError)
                                {
                                    Console.WriteLine("500 error while deleting: " + text);
                                }
                                else
                                {
                                    Console.WriteLine("HTTP request error while deleting: " + text);
                                    Console.WriteLine(string.Concat(new object[]
									{
										"Error ",
										httpWebResponse.StatusCode,
										": ",
										httpWebResponse.StatusDescription
									}));
                                }
                            }
                        }
                    }
                }
            }
        }
        public static void get_auth_key()
        {
            HttpWebRequest httpWebRequest = WebRequest.Create((!Program.UK) ? "https://auth.api.rackspacecloud.com/v1.0" : "https://lon.auth.api.rackspacecloud.com/v1.0") as HttpWebRequest;
            httpWebRequest.Headers.Add("X-Auth-User: " + Program.username);
            httpWebRequest.Headers.Add("X-Auth-Key:  " + Program.apikey);
            WebHeaderCollection webHeaderCollection = new WebHeaderCollection();
            try
            {
                using (WebResponse response = httpWebRequest.GetResponse())
                {
                    webHeaderCollection = response.Headers;
                    response.Close();
                }
            }
            catch
            {
                Console.WriteLine("Authentication error! Please check your username and API key.");
                Environment.Exit(1);
            }
            Program.auth_key = webHeaderCollection["X-Storage-Token"];
            Program.storage_url = webHeaderCollection["X-Storage-Url"];
            Console.WriteLine("Storage Token: " + Program.auth_key);
            Console.WriteLine("Storage URL: " + Program.storage_url);
        }
    }
}
