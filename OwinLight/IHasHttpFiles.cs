using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OwinLight
{
    public interface IHasHttpFiles
    {
        List<HttpFile> HttpFiles { get; set; }
    }
}
