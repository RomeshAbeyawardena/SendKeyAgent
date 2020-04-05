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
using WindowsInput.Native;

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
        private const int KeyboardSleepTimeout = 500;
        private const string executorPrecursor = "./";
        private static string WelcomeText = $"Welcome!\r\nUse {executorPrecursor}[command] to treat message as an executable command\r\n(CTRL + Q to quit)\r\nMessage: ";

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
            if (!state.IsRunning)
                Stop();
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
                logger.LogDebug("Input listener is suspended");
                return;
            }

            while (!tcpListener.Pending())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                logger.LogDebug("No pending connections waiting 500ms before retry");
                await Task.Delay(1000);
            }

            var currentConnectionId = connectionId++;
            logger.LogInformation("Connection #{0} initated...", currentConnectionId);

            await AcceptTcpClient(tcpListener.AcceptTcpClientAsync(), cancellationToken);
            logger.LogDebug("Session ended");
            await InitConnections(cancellationToken);
        }

        private async Task AcceptTcpClient(Task<TcpClient> tcpClientTask, CancellationToken cancellationToken)
        {
            using (var session = new Session(logger, connectionId++, await tcpClientTask))
                while (session.IsConnected)
                {
                    logger.LogDebug("Session connected");
                    if (cancellationToken.IsCancellationRequested)
                    {
                        TerminateSession("OK, Goodbye!", session);
                        logger.LogDebug("Session while loop ends here. Reason: Cancellation Token requested");
                        //Session has been terminated exit loop.
                        break;
                    }

                    ShowWelcomeText(WelcomeText, session);

                    if (session.HasDataAvailable)
                    {
                        logger.LogDebug("Session has data");
                        if (ProcessData(session))
                        {
                            //Request has been processed, wait on next request from client.
                            continue;
                        }
                        else
                        {
                            logger.LogDebug("Session while loop ends here. Reason: User aborted connection");
                            //Session has terminated exit loop.
                            break;
                        }
                    }
                    logger.LogDebug("Session in progress");
                    await Task.Delay(500);
                }

            logger.LogInformation("Completed");
        }

        private void TerminateSession(string terminateSessionText, Session session)
        {
            WriteText(session.DataStream, terminateSessionText, Encoding.ASCII);
            logger.LogInformation("Connection termination requested");
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
                    logger.LogDebug("Quit has completed processing");
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
            bool isCommand = false;
            var input = string.Join(string.Empty, buffer);
            ICommand command;
            if ((command = commandParser
                .ParseCommand(CultureInfo.InvariantCulture, input, out var processedInput)) != null
                    && !string.IsNullOrEmpty(processedInput))
            {
                isCommand = true;
                input = processedInput;
            }

            input = input.Trim();

            if (input.Trim() == "system.shutdown")
            {
                serverState.OnNext(new ServerState { IsRunning = false });
                return false;
            }

            if (input == "toggleConsole")
            {
                ToggleConsole();
                return true;
            }

            // do not send empty character arrays to the input simulator it throws an argument null exception
            if (!string.IsNullOrWhiteSpace(input))
            {
                if (isCommand)
                {
                    ToggleConsole();
                    //Wait for console on host machine - increase timeout if required.
                    inputSimulator.Keyboard.Sleep(KeyboardSleepTimeout);

                    inputSimulator.Keyboard
                    .TextEntry(input);

                    KeyboardSleep(KeyboardSleepTimeout);

                    inputSimulator.Keyboard.KeyPress(VirtualKeyCode.RETURN);

                    KeyboardSleep(KeyboardSleepTimeout);

                    ToggleConsole();
                }
                else
                {
                    bool executable = input.StartsWith(executorPrecursor);

                    inputSimulator.Keyboard
                        .TextEntry(executable ? input.Replace(executorPrecursor, string.Empty) : input);

                    if (executable)
                    {
                        KeyboardSleep(KeyboardSleepTimeout);
                        inputSimulator.Keyboard.KeyPress(VirtualKeyCode.RETURN);
                    }
                }
            }

            return true;
        }

        private void KeyboardSleep(int keyboardSleepTimeout)
        {
            inputSimulator.Keyboard.Sleep(keyboardSleepTimeout);
        }

        private void ToggleConsole()
        {
            inputSimulator.Keyboard
                .ModifiedKeyStroke(new[] { VirtualKeyCode.CONTROL, VirtualKeyCode.SHIFT }, VirtualKeyCode.VK_C);
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
