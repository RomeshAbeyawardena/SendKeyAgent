using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using WindowsInput;

namespace SendKeyAgent.App
{
    public class Worker : BackgroundService
    {
        private readonly ILogger<Worker> logger;
        private readonly IInputListener inputListener;

        public Worker(ILogger<Worker> logger, IInputListener inputListener)
        {
            this.logger = logger;
            this.inputListener = inputListener;
        }

        public override async Task StartAsync(CancellationToken cancellationToken)
        {
            inputListener.Start();
            await inputListener.InitConnections(cancellationToken);
        }

        public override Task StopAsync(CancellationToken cancellationToken)
        {
            inputListener.Stop();
            return Task.CompletedTask;
        }
        
        public override void Dispose()
        {
            inputListener.Dispose();
        }
        
        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            inputListener.Start();
            await inputListener.InitConnections(stoppingToken);
            inputListener.Stop();
        }
    }
}
