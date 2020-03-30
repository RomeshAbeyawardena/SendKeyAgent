using SendKeyAgent.App.Extensions;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SendKeyAgent.App
{
    public class CommandParser : ICommandParser
    {
        private readonly ApplicationSettings applicationSettings;

        public CommandParser(ApplicationSettings applicationSettings)
        {
            this.applicationSettings = applicationSettings;
        }

        public ICommand ParseCommand(CultureInfo cultureInfo, string commandText, 
            out string completeCommandText, ICommand startingNode = default)
        {
            completeCommandText = string.Empty;

            var commandTextParameters = commandText.ToLower(cultureInfo).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            
            if(startingNode == null)
            {
                startingNode = applicationSettings.Commands
                    .SingleOrDefault(cmd => cmd.Name == commandTextParameters.FirstOrDefault());

                if(startingNode == null)
                    return default;

                commandTextParameters = commandTextParameters
                    .RemoveAt(0)
                    .ToArray();
            }

            ICommand currentNode = startingNode;
            completeCommandText = currentNode.CommandText;
            foreach(var parameter in commandTextParameters)
            {
                var foundNode = currentNode.Children
                    .SingleOrDefault(a => a.Name == parameter);

                if(currentNode == null 
                    || !currentNode.Children.Any())
                    return currentNode;

                completeCommandText = string.Concat(completeCommandText,
                    ' ',foundNode.CommandText);

                currentNode = foundNode;
            }

            return currentNode;
        }
    }
}
