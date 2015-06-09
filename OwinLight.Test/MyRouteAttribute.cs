using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OwinLight.Test
{
    /// <summary>
    /// 全局配置了自定义响应头，但我想某些路由下不使用全局的配置，为了方便设置，可以派生一个RouteAttribute，固定某些属性。
    /// </summary>
    public class MyRouteAttribute : RouteAttribute
    {
        public MyRouteAttribute(string path)
            : base(path)
        {
            Headers = String.Empty;
        }
        public MyRouteAttribute(string path, string verbs)
            : base(path, verbs)
        {
            Headers = String.Empty;
        }

        public MyRouteAttribute(string path, int maxlength)
            : base(path, maxlength)
        {
            Headers = String.Empty;
        }

        public MyRouteAttribute(string path, string verbs, int maxlength)
            : base(path, verbs, maxlength)
        {
            Headers = String.Empty;
        }
    }
}
