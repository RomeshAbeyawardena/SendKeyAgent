using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SendKeyAgent.App
{
    public interface IInputMessageQueue
    {
        bool TryAdd(string item);
        bool TryGetNext(out string next);
    }
}
