using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Subjects;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using WindowsInput;
using WindowsInput.Native;

namespace SendKeyAgent.App
{
    public class InputListenerV2 : TextListener
    {
        private readonly ISubject<ServerState> serverState;
        private readonly ILogger<InputListener> logger;
        private readonly List<Session> sessionList;
        private readonly IInputSimulator inputSimulator;
        private readonly ApplicationSettings applicationSettings;
        private readonly ICommandParser commandParser;

        private const int TimeoutCounterTicksPerMinute = 120;
        private int TimeoutCounterMaximumTicks => TimeoutCounterTicksPerMinute * applicationSettings.Security.TimeoutInterval;
        private const int KeyboardSleepTimeout = 500;
        private const string executorPrecursor = "./";
        private const string loginPrecursor = "$LOGIN:";
        private const string setUserNamePrecursor = "$USER_NAME:SET:";
        private const string getUserNamePrecursor = "$USER_NAME:GET";
        private const string namedPrompt = "${0}: ";
        private const string prompt = "$guest: ";
        private const string tabularSeparator = "\t|\t";
        private static readonly string WelcomeText =
            "\t======Send Key Agent======\t\r\n"
            + $"\t======Version { Assembly.GetEntryAssembly().GetName().Version }======\t\r\n"
            + "Welcome!\r\n"
            + $"\t* Set session user name with {setUserNamePrecursor}[username]\r\n"
            + $"\t* Get current session user name with {getUserNamePrecursor}\r\n"
            + $"\t* Send executable commands with {executorPrecursor}[command]\r\n"
            + $"(CTRL + Q or :quit to close current session)\r\n{prompt}";

        public InputListenerV2(
            ISubject<ServerState> serverState,
            ILogger<InputListener> logger,
            IInputSimulator inputSimulator,
            ApplicationSettings applicationSettings,
            ICommandParser commandParser)
        {
            this.serverState = serverState;
            this.logger = logger;
            this.inputSimulator = inputSimulator;
            this.applicationSettings = applicationSettings;
            this.commandParser = commandParser;
        }

        private string GetPrompt(Session session)
        {
            if (string.IsNullOrEmpty(session.UserName))
            {
                return prompt;
            }

            return string.Format(namedPrompt, session.UserName);
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

        protected override bool IsSessionValid(Session session)
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

        protected override void OnAuthenticatedRequest(Session session, Result result)
        {
            throw new NotImplementedException();
        }

        protected override string OnGetAuthenticatedCommandRequest(Session session, string request, out bool isCommand)
        {
            isCommand = false;

            ICommand command;
            if ((command = commandParser
                .ParseCommand(CultureInfo.InvariantCulture, request, out var processedInput)) != null
                    && !string.IsNullOrEmpty(processedInput))
            {
                isCommand = true;
                request = processedInput;
            }

            return request;
        }

        protected override void OnIntroNotShown(Session session)
        {
            WriteText(session.DataStream, WelcomeText, Encoding.ASCII);
        }

        protected override Result OnRequestProcess(Session session, string request, bool isCommand)
        {
            if (request.Equals(":quit"))
                return Result.Success(true);

            if (request.StartsWith(setUserNamePrecursor))
            {
                session.UserName = request
                    .Replace(setUserNamePrecursor, string.Empty);
                WriteText(session.DataStream, $"User name has been set to {session.UserName}, this will be reset on session termination.", Encoding.ASCII);

                return Result.Success();
            }

            if (request.StartsWith(getUserNamePrecursor))
            {
                if (string.IsNullOrWhiteSpace(session.UserName))
                {
                    WriteText(
                        session.DataStream,
                        $"User name has not been set, you can set the user name at any time with {setUserNamePrecursor}",
                        Encoding.ASCII);

                    return Result.Success();
                }

                WriteText(session.DataStream, $"User name has been set to {session.UserName}", Encoding.ASCII);
                return Result.Success();
            }

            if (request.StartsWith(loginPrecursor))
            {
                WriteText(session.DataStream, "Please wait...", Encoding.ASCII);
                Task.Delay(1000);
                session.SignedIn = request
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

            if(session.SignedIn)
            { 
                return ProcessWhenSignedIn(session, request, isCommand);
            }

            return Result.Success(true);
        }

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


        protected override void OnTerminateSession(Session session)
        {
            throw new NotImplementedException();
        }

        protected override bool OnUnauthenticatedRequest(Session session, Result result)
        {
            throw new NotImplementedException();
        }
    }
}
