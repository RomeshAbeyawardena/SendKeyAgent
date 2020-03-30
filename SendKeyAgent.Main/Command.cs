using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SendKeyAgent.App
{
    public class Command : ICommand
    {
        public string Name { get; set; }

        public bool IsNode { get; set; }

        public string CommandText { get; set; }

        public IEnumerable<Command> Children { get; set; }
    }
}
