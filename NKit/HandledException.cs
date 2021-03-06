using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Nanook.NKit
{
    public class HandledException : Exception
    {
        public HandledException(Exception innerException, string message, params string[] args)
            : base(string.Format(message, args) + (innerException == null || innerException is HandledException ? "" : (": " + innerException.Message)), innerException) { }
        public HandledException(string message, params string[] args) : this(null, message, args) { }

        public string FriendlyErrorMessage
        {
            get
            {
                StringBuilder sb = new StringBuilder();
                sb.AppendLine(this.Message);
                Exception ex = this.InnerException;
                while (ex is HandledException)
                {
                    sb.AppendLine(ex.Message);
                    ex = ex.InnerException;
                    if (ex is AggregateException)
                        ex = ((AggregateException)ex).InnerExceptions?.FirstOrDefault(a => a is HandledException);
                }
                return sb.ToString();
            }
        }
    }

}
