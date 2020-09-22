
using System;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Collections.Generic;
using System.IO;
using System.Collections.Concurrent;

namespace HTTPServer {
    class Constants {
        public const string RequestRegex = @"^GET\s+([^\s\?]+)[^\s]*\s+HTTP/.*";
        public const string ServerHeader = "topkek";
        public const string ConnectionHeader = "close";
        public const int JoinSignal = 1;
        public const string CPUInConfig = "cpu_limit";
        public const string ThreadsInConfig = "thread_limit";
        public const string RootInConfig = "document_root";
        // protected const string RequestRegex = @"^(./){0,1}";
    }

    class Config {
        public int Cores = -1;
        public int Threads = -1;
        public static string Root = "";

        public Config() {}

        public bool Read() {
            string text = File.ReadAllText("./config.conf");
            string[] textSplited = text.Split(new Char[] {' ', '\n'});
            for (int i = 0; i < textSplited.Length; i++) {
                switch (textSplited[i]) {
                    case Constants.CPUInConfig:
                        if (i + 1 >= textSplited.Length) {
                            return false;
                        }
                        if (!Int32.TryParse(textSplited[i + 1], out this.Cores)) {
                            return false;
                        }
                        i++;
                        break;
                    case Constants.ThreadsInConfig:
                        if (i + 1 >= textSplited.Length) {
                            return false;
                        }
                        if (!Int32.TryParse(textSplited[i + 1], out this.Threads)) {
                            return false;
                        }
                        i++;
                        break;
                    case Constants.RootInConfig:
                        if (i + 1 >= textSplited.Length) {
                            return false;
                        }
                        // Match PathMatch = Regex.Match(textSplited[i + 1], Constants.PathRegex);
                        // if (RequestMatch == Match.Empty) {
                            // return false;
                        // }
                        Root = textSplited[i + 1];
                        i++;
                        break;
                }
            }
            return this.Cores != -1 && this.Threads != -1 && Root != "";
        }
    }

    class ThreadPool {
        private Thread[] Pool;
        private ConcurrentQueue<TcpClient> ClientsQueue;
        private ConcurrentQueue<int> JoinQueue;

        public ThreadPool(int ThreadsCount) {
            this.Pool = new Thread[ThreadsCount];
            this.ClientsQueue = new ConcurrentQueue<TcpClient>();
            this.JoinQueue = new ConcurrentQueue<int>();
            for (int i = 0; i < ThreadsCount; i++) {
                this.Pool[i] = new Thread(this.StartThread);
                this.Pool[i].IsBackground = true;
                this.Pool[i].Name = "Thread_" + i;
                this.Pool[i].Start();
            }
        }

        ~ThreadPool() {
            this.JoinQueue.Enqueue(Constants.JoinSignal);
        }

        public void Enqueue(TcpClient c) {
            this.ClientsQueue.Enqueue(c);
        }

        private void StartThread() {
            // Console.WriteLine("  T>" + Thread.CurrentThread.Name + " started!!");
            TcpClient c;
            int j;
            while (true) {
                if (JoinQueue.TryPeek(out j)) {
                    // Console.WriteLine("  T>" + Thread.CurrentThread.Name + " finishing");
                    Thread.CurrentThread.Join();
                }
                if (ClientsQueue.TryDequeue(out c)) {
                    // Console.WriteLine("  T>" + Thread.CurrentThread.Name + " got client");
                    new Client(c);
                }
                Thread.Sleep(1);
            }
        }
    }

    class Server {
        private TcpListener Listener;
        private ThreadPool ThreadPool;
        static private Config Config;

        public Server(int Port, int ThreadsCount) {
            this.Listener = new TcpListener(IPAddress.Any, Port);
            this.Listener.Start();
            this.ThreadPool = new ThreadPool(ThreadsCount);
            while (true) {
                this.ThreadPool.Enqueue(this.Listener.AcceptTcpClient());
            }
        }

        ~Server() {
            if (this.Listener != null) {
                this.Listener.Stop();
            }
        }

        public static void Main(string[] args) {
            Config = new Config();
            if (!Config.Read()) {
                Environment.Exit(1);
            }
            int ThreadsCount = Math.Min(Config.Cores * 4, Config.Threads);
            int Port = 9018;
            Console.WriteLine("Started multithreaded server on port " + Port + "\nMax threads count: " + ThreadsCount + "\n");
            Console.CancelKeyPress += new ConsoleCancelEventHandler(Exit);
            new Server(Port, ThreadsCount);
        }

        private static void Exit(object sender, ConsoleCancelEventArgs args) {
            Environment.Exit(1);
        }
    }

    class Client {
        public Client(TcpClient Client) {
            // Console.WriteLine("New client");
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
            // Console.WriteLine("RequestMatch: " + RequestMatch);
            if (RequestMatch == Match.Empty) {
                // Console.WriteLine("Method not allowed. Sending error...");
                this.SendError(Client, 405);
                return;
            }
            string RequestUri = RequestMatch.Groups[1].Value;
            RequestUri = Uri.UnescapeDataString(RequestUri);
            // Console.WriteLine("Request uri: " + RequestUri);
            if (RequestUri.IndexOf("/..") >= 0) {
                // Console.WriteLine("Request contains \'..\'. Sending error...");
                SendError(Client, 403);
                return;
            }
            bool IndexAdded = false;
            if (RequestUri.EndsWith("/")) {
                RequestUri += "index.html";
                IndexAdded = true;
            }
            string FilePath = Config.Root + RequestUri;
            // Console.WriteLine("File path: " + FilePath);
            if (!File.Exists(FilePath)) {
                // Console.WriteLine("File doesn't exist. Sending error...");
                SendError(Client, IndexAdded ? 403 : 404);
                // SendError(Client, 404);
                return;
            }
            string ContentType = this.GetContentType(RequestUri.Substring(RequestUri.LastIndexOf('.')));
            FileStream FS;
            try {
                FS = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read);
            } catch (Exception) {
                // Console.WriteLine("Sending 500...");
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
            // Console.WriteLine("Connection closed");
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
            // Console.WriteLine("Connection closed");
        }
    }
}
