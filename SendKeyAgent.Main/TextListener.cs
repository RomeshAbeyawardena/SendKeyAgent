using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
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
