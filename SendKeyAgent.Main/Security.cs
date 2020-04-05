using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SendKeyAgent.App
{
    public class Security
    {
        private string password;
        private Encoding encoding;
        public string Password { 
            get => string.IsNullOrEmpty(password) 
                ? string.Empty 
                : Encoding.GetString(Convert.FromBase64String(password)); 
            set => password = value; }
        public Encoding Encoding { get => encoding == null ? (Encoding = Encoding.ASCII) : encoding; set => encoding = value;  }
        public int TimeoutInterval { get; set; }
    }
}
