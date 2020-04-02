using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Subjects;
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
        private readonly ISubject<ServerState> serverState;
        private readonly ILogger<InputListener> logger;

        private readonly IInputSimulator inputSimulator;
        private readonly ICommandParser commandParser;
        private ServerState CurrentState;
        private const int EnterKey = 13;
        private const int EndOfText = 3;
        private const int EndOfTransmission = 4;
        private const int Quit = 17;

        public InputListener(ISubject<ServerState> serverState, ILogger<InputListener> logger,
            IInputSimulator inputSimulator, ICommandParser commandParser)
        {
            this.serverState = serverState;
            serverState.Subscribe(OnNext);
            this.logger = logger;
            this.inputSimulator = inputSimulator;
            this.commandParser = commandParser;
        }

        private void OnNext(ServerState state)
        {
            CurrentState = state;
            if(!state.IsRunning)
                tcpListener.Stop();
        }

        public IInputListener Start(int port = 4000, int backlog = 10)
        {
            tcpListener = new TcpListener(IPAddress.Any, port);
            serverState.OnNext(new ServerState { IsRunning = true });
            tcpListener.Start(backlog);
            logger.LogInformation("Starting Input Listener on port: {0}. Accepting a maximum of {1} connections.",
                port, backlog);
            return this;
        }

        public async Task InitConnections(CancellationToken cancellationToken)
        {

            logger.LogInformation("Next connection: {0}", connectionId);

            if (!CurrentState.IsRunning || cancellationToken.IsCancellationRequested)
            {
                return;
            }

            while (!tcpListener.Pending())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }

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
            using var session = new Session(logger, connectionId++, await tcpClientTask);

            while (session.IsConnected)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    TerminateSession("OK, Goodbye!", session);

                    //Session has been terminated exit loop.
                    break;
                }

                ShowWelcomeText("Welcome friend B-), start typing your message to send (CTRL + Q to quit)\r\nMessage: ", session);

                if (session.HasDataAvailable)
                {
                    if (ProcessData(session))
                    {
                        //Request has been processed, wait on next request from client.
                        continue;
                    }

                    //Session has been terminated exit loop.
                    break;
                }

                await Task.Delay(500);
            }

            logger.LogInformation("Completed");
        }

        private void TerminateSession(string terminateSessionText, Session session)
        {
            WriteText(session.DataStream, terminateSessionText, Encoding.ASCII);
            logger.LogInformation("Connection termination requested");
            session.Close();
        }

        private void ShowWelcomeText(string welcomeText, Session session)
        {

            if (!session.IsWelcomeMessageShown)
            {
                WriteText(session.DataStream, welcomeText, Encoding.ASCII);
                session.IsWelcomeMessageShown = true;
            }

        }

        private bool ProcessData(Session session)
        {
            logger.LogDebug("Receiving data...");
            var data = GetData(session.DataStream);
            if (data != -1)
            {

                if (data == Quit)
                {
                    TerminateSession("OK, Bye!", session);
                    FlushTextBuffer(session.Data.ToArray());
                    return false;
                }

                if (data == EnterKey)
                {
                    var result = FlushTextBuffer(session.Data.ToArray());
                    session.Data.Clear();
                    WriteText(session.DataStream, "Message received.\r\nMessage: ", Encoding.ASCII);

                    if (!result)
                    {
                        session.Client.Close();
                        //tcpListener.Stop();
                        return false;
                    }
                }
                else
                {
                    session.Data.Add((char)data);
                }
            }

            return true;
        }

        private void WriteText(Stream dataStream, string text, Encoding encoding)
        {
            var message = Encoding.ASCII.GetBytes(text);
            dataStream.Write(message, 0, message.Length);
        }

        private bool FlushTextBuffer(IEnumerable<char> buffer)
        {
            var input = string.Join(string.Empty, buffer);
            ICommand command;
            if ((command = commandParser
                .ParseCommand(CultureInfo.InvariantCulture, input, out var processedInput)) != null
                    && !string.IsNullOrEmpty(processedInput))
            {
                input = processedInput;
            }

            if (input.Trim() == "system.shutdown")
            {
                serverState.OnNext(new ServerState { IsRunning = false });
                return false;
            }

            inputSimulator.Keyboard
                .TextEntry(input);

            return true;
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
