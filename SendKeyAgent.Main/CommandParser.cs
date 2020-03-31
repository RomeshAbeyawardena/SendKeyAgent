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

        public ICommand GetCommand(IEnumerable<ICommand> commands, string name)
        {
            return commands.SingleOrDefault(child => child.Name == name);
        }

        public ICommand GetChildCommand(ICommand startingNode, string name)
        {
            if (startingNode.Children == null || !startingNode.Children.Any())
                return null;

            return GetCommand(startingNode.Children, name);
        }

        public ICommand ParseCommand(CultureInfo cultureInfo, string commandText,
            out string completeCommandText, ICommand startingNode = default)
        {
            completeCommandText = string.Empty;

            var commandTextParameters = commandText
                .Trim()
                .Replace("\n", string.Empty)
                .ToLower(cultureInfo)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);

            if (startingNode == null)
            {
                startingNode = GetCommand(
                    applicationSettings.Commands,
                    commandTextParameters.FirstOrDefault());

                if (startingNode == null)
                {
                    return default;
                }

                commandTextParameters = commandTextParameters
                    .RemoveAt(0)
                    .ToArray();
            }

            ICommand currentNode = startingNode;
            completeCommandText = currentNode.CommandText;
            foreach (var parameter in commandTextParameters)
            {
                
                var foundNode = GetChildCommand(currentNode, parameter);

                if (foundNode == null)
                    return currentNode;

                completeCommandText = string.Concat(
                    completeCommandText,
                    ' ',
                    foundNode.CommandText);

                currentNode = foundNode;
            }

            return currentNode;
        }
    }
}
