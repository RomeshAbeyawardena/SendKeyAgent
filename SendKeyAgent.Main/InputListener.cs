using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using WindowsInput;

namespace SendKeyAgent.App
{
    public class InputListener : IInputListener
    {
        private TcpListener tcpListener;
        private int connectionId;
        private readonly ILogger<InputListener> logger;

        private readonly IInputSimulator inputSimulator;
        private readonly ICommandParser commandParser;

        private const int EnterKey = 13;
        private const int EndOfText = 3;
        private const int EndOfTransmission = 4;
        private const int Quit = 17;

        public InputListener(ILogger<InputListener> logger,
            IInputSimulator inputSimulator, ICommandParser commandParser)
        {
            this.logger = logger;
            this.inputSimulator = inputSimulator;
            this.commandParser = commandParser;
        }

        public IInputListener Start(int port = 4000, int backlog = 10)
        {
            tcpListener = new TcpListener(IPAddress.Any, port);
            tcpListener.Start(backlog);
            logger.LogInformation("Starting Input Listener on port: {0}. Accepting a maximum of {1} connections.",
                port, backlog);
            return this;
        }

        public async Task InitConnections(CancellationToken cancellationToken)
        {

            logger.LogInformation("Current Connection: {0}", connectionId);

            while (!tcpListener.Pending())
            {
                if (cancellationToken.IsCancellationRequested)
                    return;

                await Task.Delay(1000);
                continue;
            }

            var currentConnectionId = connectionId++;
            logger.LogInformation("Connection #{0} initated...", currentConnectionId);

            await AcceptTcpClient(tcpListener.AcceptTcpClientAsync(), cancellationToken);
            await InitConnections(cancellationToken);
        }

        private async Task AcceptTcpClient(Task<TcpClient> tcpClientTask, CancellationToken cancellationToken)
        {
            bool welcomeMessageShown = false;
            var sessionData = new List<char>();
            var client = await tcpClientTask;
            while (client.Connected)
            {
                var dataStream = client.GetStream();

                if (!welcomeMessageShown)
                {
                    WriteText(dataStream, "Welcome friend B-), start typing your message to send (CTRL + Q to quit)\r\nMessage: ", Encoding.ASCII);
                    welcomeMessageShown = true;
                }

                if (cancellationToken.IsCancellationRequested)
                {
                    WriteText(dataStream, "OK, Goodbye!", Encoding.ASCII);
                    logger.LogInformation("Connection termination requested");
                    client.Close();
                    break;
                }

                if (client.Available < 1)
                {
                    await Task.Delay(500);
                    continue;
                }

                var data = GetData(dataStream);
                logger.LogDebug("Receiving data...");

                if (data != -1)
                {

                    if (data == Quit)
                    {
                        WriteText(dataStream, "OK, Goodbye!", Encoding.ASCII);
                        logger.LogInformation("Connection termination requested");
                        client.Close();
                        FlushTextBuffer(sessionData.ToArray());
                        break;
                    }

                    if (data == EnterKey)
                    {
                        FlushTextBuffer(sessionData.ToArray());
                        sessionData.Clear();
                        WriteText(dataStream, "Message received.\r\nMessage: ", Encoding.ASCII);
                    }
                    else
                    {
                        sessionData.Add((char)data);
                    }
                }
            }
            logger.LogInformation("Completed");
        }

        private void WriteText(Stream dataStream, string text, Encoding encoding)
        {
            var message = Encoding.ASCII.GetBytes(text);
            dataStream.Write(message, 0, message.Length);
        }

        private void FlushTextBuffer(IEnumerable<char> buffer)
        {
            var input = string.Join(string.Empty, buffer);
            ICommand command;
            if ((command = commandParser
                .ParseCommand(CultureInfo.InvariantCulture, input, out var processedInput)) != null
                    && !string.IsNullOrEmpty(processedInput))
                input = processedInput;

            inputSimulator.Keyboard
                .TextEntry(input);
        }

        private int GetData(Stream stream)
        {
            var getBytes = stream.ReadByte();

            stream.WriteByte(0);

            return getBytes;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool gc)
        {
            Stop();
        }

        public IInputListener Stop()
        {
            logger.LogInformation("Stopping Input listener...");
            tcpListener.Stop();
            return this;
        }
    }
}
