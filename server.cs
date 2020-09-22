
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;
using System.IO;

namespace HTTPServer {
    class Constants {
        public const string RequestRegex = @"^GET\s+([^\s\?]+)[^\s]*\s+HTTP/.*";
        public const string ServerHeader = "topkek";
        public const string ConnectionHeader = "close";
    }

    class Server {
        TcpListener Listener;

        public Server(int Port) {
            this.Listener = new TcpListener(IPAddress.Any, Port);
            this.Listener.Start();
            while (true) {
                ThreadPool.QueueUserWorkItem(new WaitCallback(ClientThread), this.Listener.AcceptTcpClient());
            }
        }

        ~Server() {
            if (this.Listener != null) {
                this.Listener.Stop();
            }
        }

        static void Main(string[] args) {
            int MaxThreadsCount = Environment.ProcessorCount * 4;
            ThreadPool.SetMaxThreads(MaxThreadsCount, MaxThreadsCount);
            ThreadPool.SetMinThreads(2, 2);
            int Port = 9018;
            // Console.WriteLine("Started multithreaded server on port " + Port + "\nMax threads count: " + MaxThreadsCount + "\nMin threads count: 2\n");
            new Server(Port);
        }

        static void ClientThread(Object StateInfo) {
            new Client((TcpClient)StateInfo);
        }
    }

    class Client {
        public Client(TcpClient Client) {
            Console.WriteLine("New client");
            string Request = "";
            byte[] Buffer = new byte[1024];
            int Count;
            while ((Count = Client.GetStream().Read(Buffer, 0, Buffer.Length)) > 0) {
                Request += Encoding.UTF8.GetString(Buffer, 0, Count);
                if (Request.IndexOf("\r\n\r\n") >= 0 || Request.Length > 4096) {
                    break;
                }
            }
            // Console.WriteLine("Request: " + Request);
            Match RequestMatch = Regex.Match(Request, Constants.RequestRegex);
            Console.WriteLine("RequestMatch: " + RequestMatch);
            if (RequestMatch == Match.Empty) {
                Console.WriteLine("Method not allowed. Sending error...");
                this.SendError(Client, 405);
                return;
            }
            string RequestUri = RequestMatch.Groups[1].Value;
            RequestUri = Uri.UnescapeDataString(RequestUri);
            Console.WriteLine("Request uri: " + RequestUri);
            if (RequestUri.IndexOf("/..") >= 0) {
                Console.WriteLine("Request contains \'..\'. Sending error...");
                SendError(Client, 403);
                return;
            }
            bool IndexAdded = false;
            if (RequestUri.EndsWith("/")) {
                RequestUri += "index.html";
                IndexAdded = true;
            }
            string FilePath = "." + RequestUri;
            Console.WriteLine("File path: " + FilePath);
            if (!File.Exists(FilePath)) {
                Console.WriteLine("File doesn't exist. Sending error...");
                SendError(Client, IndexAdded ? 403 : 404);
                // SendError(Client, 404);
                return;
            }
            string ContentType = this.GetContentType(RequestUri.Substring(RequestUri.LastIndexOf('.')));
            FileStream FS;
            try {
                FS = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            } catch (Exception) {
                Console.WriteLine("Sending 500...");
                SendError(Client, 500);
                return;
            }
            string Headers = this.GenerateHeaders(200, ContentType, FS.Length.ToString());
            byte[] HeadersBuffer = Encoding.UTF8.GetBytes(Headers);
            Client.GetStream().Write(HeadersBuffer, 0, HeadersBuffer.Length);
            while (FS.Position < FS.Length) {
                Count = FS.Read(Buffer, 0, Buffer.Length);
                Client.GetStream().Write(Buffer, 0, Count);
            }
            Buffer = Encoding.UTF8.GetBytes("\r\n\r\n");
            Client.GetStream().Write(Buffer, 0, Buffer.Length);
            FS.Close();
            Client.Close();
            Console.WriteLine("Connection closed");
        }

        private string GetContentType(string Extension) {
            switch (Extension) {
                case ".html":
                    return "text/html";
                case ".css":
                    return "text/css";
                case ".js":
                    return "text/javascript";
                case ".jpg":
                    return "image/jpeg";
                case ".jpeg":
                case ".png":
                case ".gif":
                    return "image/" + Extension.Substring(1);
                case ".swf":
                    return "application/x-shockwave-flash";
                default:
                    if (Extension.Length > 1) {
                        return "application/" + Extension.Substring(1);
                    } else {
                        return "application/unknown";
                    }
            }
        }

        private string GenerateHeaders(int Code, string ContentType, string ContentLength) {
            string CodeString = Code.ToString() + " " + ((HttpStatusCode)Code).ToString();
            string DateHeader = DateTime.Now.ToUniversalTime().ToString("r");
            // Console.WriteLine("Response: " + );
            return "HTTP/1.1 " + CodeString +
                   "\nContent-type: " + ContentType +
                   "\nContent-Length: " + ContentLength +
                   "\nServer: " + Constants.ServerHeader +
                   "\nDate: " + DateHeader +
                   "\nConnection: " + Constants.ConnectionHeader + "\n\n";
        }

        private void SendError(TcpClient Client, int Code) {
            string Html = "<html><body><h1 style=\"margin: 0 10em 5em; text-align: center;\">" + ((HttpStatusCode)Code).ToString() + "</h1></body></html>\r\n\r\n";
            string Headers = this.GenerateHeaders(Code, "text/html", Html.Length.ToString());
            // string Response = Headers + Html;
            byte[] Buffer = Encoding.UTF8.GetBytes(Headers + Html);
            Client.GetStream().Write(Buffer, 0, Buffer.Length);
            Client.Close();
            Console.WriteLine("Connection closed");
        }
    }
}
