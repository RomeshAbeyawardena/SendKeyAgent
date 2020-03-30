using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SendKeyAgent.App
{
    public interface ICommandParser
    {
        ICommand ParseCommand(CultureInfo cultureInfo, string commandText, 
            out string completeCommandText, ICommand startingNode);
    }
}
