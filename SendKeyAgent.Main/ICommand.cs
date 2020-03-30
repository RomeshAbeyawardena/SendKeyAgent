using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SendKeyAgent.App
{
    public interface ICommand
    {
        string Name { get; set; }

        bool IsNode { get; set; }

        string CommandText { get; set; }

        IEnumerable<Command> Children { get; set; }
    }
}
