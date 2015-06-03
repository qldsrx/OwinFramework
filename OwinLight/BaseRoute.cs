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
        public Dictionary<string, Func<IOwinContext, Task>> Get { get { return Adapter._verb_route["GET"]; } }
        public Dictionary<string, Func<IOwinContext, Task>> Post { get { return Adapter._verb_route["POST"]; } }
        //public Dictionary<string, Func<IOwinContext, Task>> Put { get { return Adapter._verb_route["PUT"]; } }
        //public Dictionary<string, Func<IOwinContext, Task>> Delete { get { return Adapter._verb_route["DELETE"]; } }
        //public Dictionary<string, Func<IOwinContext, Task>> Head { get { return Adapter._verb_route["HEAD"]; } }
        public Dictionary<string, Func<IOwinContext, Task>> Any { get { return Adapter._all_route; } }
        public virtual void AddRoute(List<Tuple<Regex, Func<IOwinContext, Match, Task>>> routeRegex) { }

    }
}