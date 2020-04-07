using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Subjects;
using System.Reflection;
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
        private readonly List<Session> sessionList;
        private readonly IInputSimulator inputSimulator;
        private readonly ApplicationSettings applicationSettings;
        private readonly ICommandParser commandParser;
        private ServerState CurrentState;
        private const int TimeoutCounterTicksPerMinute = 120;
        private int TimeoutCounterMaximumTicks =>  TimeoutCounterTicksPerMinute * applicationSettings.Security.TimeoutInterval;
        private const int EnterKey = 13;
        private const int EndOfText = 3;
        private const int EndOfTransmission = 4;
        private const int Quit = 17;
        private const int KeyboardSleepTimeout = 500;
        private const string executorPrecursor = "./";
        private const string loginPrecursor = "$LOGIN:";
        private const string setUserNamePrecursor = "$USER_NAME:SET:";
        private const string getUserNamePrecursor = "$USER_NAME:GET";
        private const string namedPrompt = "${0}: ";
        private const string prompt = "$guest: ";
        private static readonly string WelcomeText = 
            "\t======Send Key Agent======\t\r\n"
            + $"\t======Version { Assembly.GetEntryAssembly().GetName().Version }======\t\r\n"
            + "Welcome!\r\n" 
            + $"\t* Set session user name with {setUserNamePrecursor}[username]\r\n"
            + $"\t* Get current session user name with {getUserNamePrecursor}\r\n"
            + $"\t* Send executable commands with {executorPrecursor}[command]\r\n" 
            + $"(CTRL + Q or :quit to close current session)\r\n{prompt}";

        public InputListener(ISubject<ServerState> serverState, ILogger<InputListener> logger,
            IInputSimulator inputSimulator, ApplicationSettings applicationSettings,
            ICommandParser commandParser)
        {
            this.serverState = serverState;
            serverState.Subscribe(OnNext);
            this.logger = logger;
            this.inputSimulator = inputSimulator;
            this.applicationSettings = applicationSettings;
            this.commandParser = commandParser;
            sessionList = new List<Session>();
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

            Task.Run(async () => await AcceptTcpClient(tcpListener.AcceptTcpClientAsync(), cancellationToken));
            await Task.Delay(1500);
            //logger.LogDebug("Session ended");
            await InitConnections(cancellationToken);
        }

        private string GetPrompt(Session session)
        {
            if(string.IsNullOrEmpty(session.UserName))
            { 
                return prompt;
            }

            return string.Format(namedPrompt, session.UserName);
        }

        private async Task AcceptTcpClient(Task<TcpClient> tcpClientTask, CancellationToken cancellationToken)
        {
            using (var session = new Session(logger, connectionId++, await tcpClientTask))
            {
                if(!sessionList.Contains(session))
                {
                    sessionList.Add(session);
                }

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
                        session.TimeoutCounter = 0;
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
                    else
                    {
                        if(!IsSessionIsValid(session))
                        {
                            break;
                        }
                    }

                    logger.LogDebug("Session in progress");
                    await Task.Delay(500);
                }
            }
            logger.LogInformation("Session Completed");
        }

        private bool IsSessionIsValid(Session session)
        {
            if (session.TimeoutCounter < TimeoutCounterMaximumTicks)
            {
                session.TimeoutCounter++;

                if (session.TimeoutCounter > 1 
                        && session.TimeoutCounter % 1000 == 1)
                {
                    WriteText(
                        session.DataStream,
                        $"Session has been idle for {session.TimeoutCounter} ticks " 
                            + $"and will be terminated after {TimeoutCounterMaximumTicks - session.TimeoutCounter} ticks\r\n\r\n{GetPrompt(session)}",
                        Encoding.ASCII);
                    logger.LogWarning("Session {0} has been idle for {1} ticks", session.Id, session.TimeoutCounter);
                }
            }
            else
            {
                session.TimeoutCounter = 0;
                logger.LogInformation(
                    "Session {0} expired (Timeout Counter: {1} ticks)",
                    session.Id,
                    session.TimeoutCounter);
                
                WriteText(
                        session.DataStream,
                        $"Session has been idle for {session.TimeoutCounter} ticks and will be terminated.",
                        Encoding.ASCII);

                return false;
            }

            return true;
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
                    FlushTextBuffer(session);
                    logger.LogDebug("Quit has completed processing");
                    return false;
                }

                if (data == EnterKey)
                {
                    var result = FlushTextBuffer(session);
                    if (!session.SignedIn && !result.IsSuccessful)
                    {
                        WriteText (
                            session.DataStream,
                            $"\r\n\r\nAccess Denied: You must be signed in to use this utility.\r\n\t" 
                            + $"To sign in type {loginPrecursor}[password]\r\n{GetPrompt(session)} ",
                            Encoding.ASCII);
                        return true;
                    }
                    else
                    {
                        WriteText(session.DataStream, $"Message received.\r\n{GetPrompt(session)} ", Encoding.ASCII);
                    }

                    if (result.Abort)
                    {
                        TerminateSession("OK, Bye!", session);
                        session.Client.Close();
                        logger.LogDebug("Quit has completed processing");
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
            var message = encoding.GetBytes(text);
            dataStream.Write(message, 0, message.Length);
        }

        private Result FlushTextBuffer(Session session)
        {
            bool isCommand = false;
            var input = string.Join(string.Empty, session.Data.ToArray());

            try
            {
                if (session.SignedIn)
                {
                    ICommand command;
                    if ((command = commandParser
                        .ParseCommand(CultureInfo.InvariantCulture, input, out var processedInput)) != null
                            && !string.IsNullOrEmpty(processedInput))
                    {
                        isCommand = true;
                        input = processedInput;
                    }
                }

                // do not send empty character arrays to the input simulator it throws an argument null exception
                if (!string.IsNullOrWhiteSpace(input))
                {
                    input = input.Trim();

                    if (input.Equals(":quit"))
                        return Result.Success(true);

                    if (input.StartsWith(setUserNamePrecursor))
                    {
                        session.UserName = input
                            .Replace(setUserNamePrecursor, string.Empty);
                        WriteText(session.DataStream, $"User name has been set to {session.UserName}, this will be reset on session termination.", Encoding.ASCII);

                        return Result.Success();
                    }

                    if (input.StartsWith(getUserNamePrecursor))
                    {
                        if (string.IsNullOrWhiteSpace(session.UserName))
                        {
                            WriteText (
                                session.DataStream, 
                                $"User name has not been set, you can set the user name at any time with {setUserNamePrecursor}", 
                                Encoding.ASCII);

                            return Result.Success();
                        }

                        WriteText(session.DataStream, $"User name has been set to {session.UserName}", Encoding.ASCII);
                        return Result.Success();
                    }

                    if (input.StartsWith(loginPrecursor))
                    {
                        WriteText(session.DataStream, "Please wait...", Encoding.ASCII);
                        Task.Delay(1000);
                        session.SignedIn = input
                            .Replace(loginPrecursor, string.Empty)
                            .Equals(applicationSettings.Security.Password);

                        WriteText(session.DataStream, "Done! If the password was valid you should have access to all areas.", Encoding.ASCII);
                        if (session.SignedIn)
                        {
                            logger.LogInformation("Remote utility sign-in successful");
                        }
                        else
                        {
                            logger.LogWarning("Remote utility sign-in unsuccessful");
                        }

                        return Result.Success();
                    }

                    if (session.SignedIn)
                    {
                        return ProcessWhenSignedIn(session, input, isCommand);
                    }
                }
            }
            finally
            {
                session.Data.Clear();
            }

            return Result.Failed();
        }
        private const string tabularSeparator = "\t|\t";
        private Result ProcessWhenSignedIn(Session currentSession, string input, bool isCommand)
        {
            if(input == "who")
            {
                var sessionListStringBuilder = new StringBuilder($"Id{tabularSeparator}User Name{tabularSeparator}Connected{tabularSeparator}Is Current\r\n");
                foreach(var session in sessionList)
                {
                    sessionListStringBuilder.AppendFormat("{1}{0}{2}{0}{3}{0}{4}\r\n", 
                        tabularSeparator, 
                        session.Id, 
                        session.UserName ?? "Guest", 
                        session.IsConnected, 
                        session.Id == currentSession.Id);
                }

                WriteText(currentSession.DataStream, sessionListStringBuilder.ToString(), Encoding.ASCII);
                return Result.Success();
            }

            if (input == "system.shutdown")
            {
                serverState.OnNext(new ServerState { IsRunning = false });
                return Result.Success(true);
            }

            if (input == "toggleConsole")
            {
                ToggleConsole();
                return Result.Success();
            }

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
            return Result.Success();
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
