using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SendKeyAgent.App
{
    public class Result
    {
        public static Result Success(bool abort = false)
        {
            return new Result {
                Abort = abort,
                Processed = true,
                IsSuccessful = true
            };
        }

        public static Result Failed(bool processed = true)
        {
            return new Result {
                Processed = processed,
                IsSuccessful = false
            };
        }

        public bool Abort { get; set; }
        public bool Processed { get; set; }
        public bool IsSuccessful { get; set; }
    }
}
