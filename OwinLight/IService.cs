using Microsoft.Owin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OwinLight
{
    public interface IService
    {
        IOwinRequest Request { get; set; }
        IOwinResponse Response { get; set; }
        bool IsHeadersSended { get; set; }
    }
}
