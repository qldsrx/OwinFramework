using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OwinLight
{
    public interface IHasRequestStream
    {
        Stream RequestStream { get; set; }
    }
}
