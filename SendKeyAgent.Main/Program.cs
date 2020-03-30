using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using WindowsInput;

namespace SendKeyAgent.App
{
    public static class Program
    {
        public static async Task Main(string[] args)
        {
            //using var inputListener = new InputListener(new InputSimulator());
            //await inputListener
              //  .Start()
              //  .InitConnections(CancellationToken.None);

            await CreateHostBuilder(args).Build().RunAsync();
        }

        public static IHostBuilder CreateHostBuilder(string[] args) =>
            Host.CreateDefaultBuilder(args)
                .ConfigureServices((hostContext, services) =>
                {
                    services
                        .AddSingleton<ApplicationSettings>()
                        .AddSingleton<ICommandParser, CommandParser>()
                        .AddSingleton<IInputListener, InputListener>()
                        .AddSingleton<IInputSimulator, InputSimulator>()
                        .AddHostedService<Worker>();
                });
    }
}
