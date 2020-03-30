using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace SendKeyAgent.App
{
    public interface IInputListener : IDisposable
    {
        IInputListener Start(int port = 4000, int backlog = 10);
        IInputListener Stop();
        Task InitConnections(CancellationToken cancellationToken);
    }
}
