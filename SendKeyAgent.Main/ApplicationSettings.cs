using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SendKeyAgent.App
{
    public class ApplicationSettings
    {
        public ApplicationSettings(IConfiguration configuration)
        {
            configuration.Bind(this);
        }

        public IEnumerable<Command> Commands { get; set; }
    }
}
