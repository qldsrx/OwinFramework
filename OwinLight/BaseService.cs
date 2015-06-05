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

        IOwinResponse _Response;
        public IOwinResponse Response
        {
            get { return _Response; }
            set
            {
                _Response = value;
                _Response.OnSendingHeaders(t =>
                {
                    IsHeadersSended = true;
                }, null);
            }
        }
        public bool IsHeadersSended { get; set; }
    }
}
