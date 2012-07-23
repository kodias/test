using System;
using System.Collections;
using System.Linq;
using System.Text;
using System.IO;
using System.Net;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading;
using System.Xml;
using System.Dynamic;
using System.Web;


namespace CreateCloudServers
{
    class Program
    {
        private static string username = "";
        private static string apikey = "";
        private static bool UK = false;
        private static string auth_key = "";
        private static string server_url = "";
        private static string sname = "";
        private static string server = "";
        //private static string method = "POST";
        //private static string flavorid = "6";
        //private static string imageid = "30";
        //private static string meta = "lb_pool=POOL-DMZ-46.38.180.219-80;POOL-DMZ-46.38.180.219-81";
        
        public static void die()
                {
            Console.WriteLine("Usage: " + AppDomain.CurrentDomain.FriendlyName + " username apikey [UK]");
            Environment.Exit(1);
        }
        static void Main(string[] args)
        {
            if (args.Length < 3 || args.Length > 4)
            {
                Program.die();
                return;
            }
            Console.Clear();
            ServicePointManager.ServerCertificateValidationCallback = ((object obj, X509Certificate x509Certificate, X509Chain x509Chain, SslPolicyErrors sslPolicyErrors) => true);
            Console.WriteLine("Authenticating...");
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
            Program.sname = args[2];
            Console.WriteLine("Authenticated!");
            for (int i = 1; i <= 5; i++)
            {
                Program.server = Program.sname + i;

                Program.create_server();
                Console.WriteLine(Program.server);
                //string xtrXML2 = Program.sname+(i);
                //System.Threading.Thread.Sleep(10000);
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
            Program.auth_key = webHeaderCollection["X-Auth-Token"];
            Program.server_url = webHeaderCollection["X-Server-Management-Url"];
            Console.WriteLine("Auth Token: " + Program.auth_key);
            Console.WriteLine("Server Management URL: " + Program.server_url);
        }
        public static void create_server()
        {
            string strXML1 = "<?xml version='1.0' encoding='UTF-8'?><server xmlns='http://docs.rackspacecloud.com/servers/api/v1.0' name='";
            string strXML2 = Program.server;
            string strXML = strXML1 + strXML2 + "' imageId='30' flavorId='6'><metadata><meta key='RackConnectLBPool'>'POOL-DMZ-46.38.180.219-80;POOL-DMZ-46.38.180.219-81'</meta></metadata></server>";
            try
            {
                HttpWebRequest httpWebRequest = WebRequest.Create(Program.server_url + "/servers") as HttpWebRequest;
                httpWebRequest.Method = "POST";
                httpWebRequest.Headers.Add("X-Auth-Token: " + Program.auth_key);
                httpWebRequest.ContentType = "text/xml";
                httpWebRequest.Timeout = 10000;
                StreamWriter writer = new StreamWriter(httpWebRequest.GetRequestStream());
                writer.WriteLine(strXML);
                writer.Close();
                Console.WriteLine(strXML);
                HttpWebResponse rsp = (HttpWebResponse)httpWebRequest.GetResponse();
                System.IO.StreamReader reader = new System.IO.StreamReader(rsp.GetResponseStream());
                rsp.Close();
            }
            catch (Exception ex)
            {
                if (ex.ToString().Contains("The request timed out"))
                {
                    Console.WriteLine("Timeout!");
                }
                else
                {
                    if (ex.ToString().Contains("409"))
                    {
                        Console.WriteLine("Build in progress!");
                        Environment.Exit(1);
                    }
                    else
                    {
                        if (ex.ToString().Contains("503"))
                        {
                            Console.WriteLine("Service");
                        }
                        else
                        {
                            Console.WriteLine("HTTP request error!");
                            Console.WriteLine("Error: " + ex.ToString());
                        }
                    }
                }
            }
            
            
            
        }
        
    }

}