using Microsoft.Owin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OwinLight
{
    public abstract class BaseService : IService
    {
        public IOwinRequest Request { get; set; }
        public IOwinResponse Response { get; set; }
    }
}
