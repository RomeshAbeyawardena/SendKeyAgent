using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace SendKeyAgent.App
{
    public sealed class Session : IDisposable
    {
        private readonly ILogger logger;

        public Session (
            ILogger logger,
            int sessionId,
            TcpClient tcpClient,
            IList<char> data = default)
        {
            this.logger = logger;
            Id = sessionId;
            DataStream = tcpClient.GetStream();
            Data = data ?? new List<char>();
            Client = tcpClient;
        }

        public int TimeoutCounter { get; set; }

        public bool SignedIn { get; set; }

        public  int Id { get; }

        public Stream DataStream { get; }

        public IList<char> Data { get; }

        public TcpClient Client { get; }

        public bool IsWelcomeMessageShown { get; set; }

        public string UserName { get; set; }

        public bool IsConnected => Client.Connected;

        public bool HasDataAvailable => Client.Available > 0;

        public void Close() => Client.Close();

        public void Dispose()
        {
            logger.LogInformation("Disposing of session #{0}", Id);
            Client.Dispose();
            DataStream.Dispose();
            logger.LogInformation("Disposing of session #{0} completed", Id);
        }
    }
}
