using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reactive.Subjects;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WindowsInput;

namespace SendKeyAgent.App
{
    public class Worker : BackgroundService
    {
        private readonly ISubject<ServerState> serverStateSubject;
        private readonly ILogger<Worker> logger;
        private readonly IInputListener inputListener;

        public Worker(ISubject<ServerState> serverStateSubject, ILogger<Worker> logger, IInputListener inputListener)
        {
            this.serverStateSubject = serverStateSubject;
            this.logger = logger;
            this.inputListener = inputListener;
        }

        private void OnNext(ServerState obj)
        {
            logger.LogInformation("Server state object has changed");
            if(!obj.IsRunning)
            {
                logger.LogInformation("Server state object has requested termination, terminating processes.");
                Dispose();
            }
        }

        public override void Dispose()
        {
            inputListener.Dispose();
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            serverStateSubject.Subscribe(OnNext, stoppingToken);
            inputListener.Start();
            await inputListener.InitConnections(stoppingToken);
        }
    }
}
