using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace DnxWebAppTest.Middleware
{
    public abstract class TraceListener
    {
        public virtual bool IsThreadSafe { get; }
    }
}
