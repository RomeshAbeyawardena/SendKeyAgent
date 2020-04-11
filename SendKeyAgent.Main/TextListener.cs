using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Reactive.Subjects;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SendKeyAgent.App
{
    public abstract class TextListener : IInputListener
    {
        private TcpListener tcpListener;
        private readonly ISubject<ServerState> serverState;
        private ServerState CurrentState;
        private readonly ILogger<IInputListener> logger;
        private int connectionId;
        private readonly List<Session> sessionList;
        private const int EnterKey = 13;
        private const int EndOfText = 3;
        private const int EndOfTransmission = 4;
        private const int Quit = 17;

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
                        OnTerminateSession(session);
                        logger.LogDebug("Session while loop ends here. Reason: Cancellation Token requested");
                        //Session has been terminated exit loop.
                        break;
                    }

                    if(!session.IsWelcomeMessageShown)
                    { 
                        OnIntroNotShown(session);
                        session.IsWelcomeMessageShown = true;
                    }

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
                        if(!IsSessionValid(session))
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

        private int GetData(Stream stream)
        {
            var getBytes = stream.ReadByte();

            stream.WriteByte(0);

            return getBytes;
        }

        /// <summary>
        /// Processes data from request
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        protected virtual bool ProcessData(Session session)
        {
            logger.LogDebug("Receiving data...");
            var data = GetData(session.DataStream);
            if (data != -1)
            {
                if (data == Quit || data == EndOfTransmission)
                {
                    OnTerminateSession(session);
                    FlushTextBuffer(session);
                    logger.LogDebug("Quit has completed processing");
                    return false;
                }

                if (data == EnterKey)
                {
                    var result = FlushTextBuffer(session);
                    if (!session.SignedIn && !result.IsSuccessful)
                    {
                        return OnUnauthenticatedRequest(session, result);
                    }
                    else
                    {
                        OnAuthenticatedRequest(session, result);
                    }

                    if (result.Abort)
                    {
                        OnTerminateSession(session);
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

        private Result FlushTextBuffer(Session session)
        {
            bool isCommand = false;
            var input = string.Join(string.Empty, session.Data.ToArray());

            try
            {
                if (session.SignedIn)
                {
                    input = OnGetAuthenticatedCommandRequest(session, input, out isCommand);
                }

                // do not send empty character arrays to the input simulator it throws an argument null exception
                if (!string.IsNullOrWhiteSpace(input))
                {
                    input = input.Trim();

                    return OnRequestProcess(session, input, isCommand);
                }
            }
            finally
            {
                session.Data.Clear();
            }

            return Result.Failed();
        }
        
        protected void WriteText(Stream dataStream, string text, Encoding encoding)
        {
            var message = encoding.GetBytes(text);
            dataStream.Write(message, 0, message.Length);
        }
        /// <summary>
        /// Callback method triggered when an unauthenticated request has been processed.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="result"></param>
        /// <returns></returns>
        protected abstract bool OnUnauthenticatedRequest(Session session, Result result);

        /// <summary>
        /// Callback method triggered when an authenticated request has been processed.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="result"></param>
        protected abstract void OnAuthenticatedRequest(Session session, Result result);

        /// <summary>
        /// Callback triggered when intro logic has not been fired for the first time when a client establishes a connection.
        /// </summary>
        /// <param name="session"></param>
        protected abstract void OnIntroNotShown(Session session);

        /// <summary>
        /// Callback triggered when a session has been terminated.
        /// </summary>
        /// <param name="session"></param>
        protected abstract void OnTerminateSession(Session session);

        /// <summary>
        /// Method to determine whether a session is currently valid.
        /// </summary>
        /// <param name="session"></param>
        /// <returns></returns>
        protected abstract bool IsSessionValid(Session session);
        
        /// <summary>
        /// Obtains an executable command, if the command is invalid should return the original request.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="isCommand"></param>
        /// <returns>Should return an executable command or the original value passed</returns>
        protected abstract string OnGetAuthenticatedCommandRequest(Session session, string request, out bool isCommand);

        /// <summary>
        /// Processes an executable command computed by OnGetAuthenticatedCommandRequest or passed in the original request.
        /// </summary>
        /// <param name="session"></param>
        /// <param name="request"></param>
        /// <param name="isCommand"></param>
        /// <returns></returns>
        protected abstract Result OnRequestProcess(Session session, string request, bool isCommand);
        
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
