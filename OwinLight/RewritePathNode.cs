using Microsoft.Owin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OwinLight
{
    public class RewritePathNode
    {
        public Func<IOwinContext, string[], Task> func;
        public Dictionary<string, RewritePathNode> chindren;
    }
}
