using Microsoft.Owin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OwinLight
{
    public abstract class BaseRoute
    {
        public Dictionary<string, Func<IOwinContext, Task>> Get { get { return Startup._verb_route["GET"]; } }
        public Dictionary<string, Func<IOwinContext, Task>> Post { get { return Startup._verb_route["POST"]; } }
        public Dictionary<string, Func<IOwinContext, Task>> Any { get { return Startup._all_route; } }

    }
}