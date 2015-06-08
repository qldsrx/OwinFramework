/***********************************************************
 * jexus部署方法：
 * 1、编译得到OwinLight.dll（也可以自行改名）。
 * 2、将编译得到的dll连同Owin.dll、Microsoft.Owin.dll等文件
 *    一同放置到网站的bin文件夹中
 * 3、在对应网站的jws网站配置文件中加入一句，声明要使用的适配器：
 *    OwinMain=OwinLight.dll,OwinLight.Startup
 * 4、重启Jexus让配置生效。
 *************************************************************************/

using Microsoft.Owin;
using Microsoft.Owin.Builder;
using Owin;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OwinLight
{
    public class Startup
    {
        Func<IDictionary<string, object>, Task> _owinApp;//owin容器
        public static readonly Dictionary<string, Func<IOwinContext, Task>> _all_route = new Dictionary<string, Func<IOwinContext, Task>>();//path处理容器，处理任意版本标准路径
        public static readonly Dictionary<string, Dictionary<string, Func<IOwinContext, Task>>> _verb_route;//path处理容器，处理带版本的标准路径
        static readonly RewritePathNode[] _rewrite_route;
        static int maxdepth = 10;//伪静态最大深度，深度是指有/分割的子串数量

        public static Func<IOwinContext, Task> NotFountFun; //处理路由未匹配的场景。

        static Startup()
        {
            _verb_route = new Dictionary<string, Dictionary<string, Func<IOwinContext, Task>>>(2);
            _verb_route.Add("GET", new Dictionary<string, Func<IOwinContext, Task>>());
            _verb_route.Add("POST", new Dictionary<string, Func<IOwinContext, Task>>());
            _rewrite_route = new RewritePathNode[maxdepth];
        }
        /// <summary>
        /// 适配器构造函数
        /// </summary>
        public Startup()
        {
            var bin = AppDomain.CurrentDomain.SetupInformation.PrivateBinPath;
            if (string.IsNullOrEmpty(bin))
            {
                bin = AppDomain.CurrentDomain.BaseDirectory;
            }
            if (Directory.Exists(bin))
            {
                var ds = new DirectoryInfo(bin);
                var files = ds.GetFiles("*.dll", SearchOption.AllDirectories);
                if (files != null)
                {
                    Assembly assembly = null;
                    foreach (var f in files)
                    {
                        try
                        {
                            assembly = AppDomain.CurrentDomain.Load(AssemblyName.GetAssemblyName(f.FullName));
                        }
                        catch (Exception e)
                        {
                            Debug.Write(e.Message);
                            continue;
                        }

                        var assemblyTypes = assembly.GetTypes();
                        if (assemblyTypes != null)
                        {
                            foreach (var type in assemblyTypes)
                            {
                                if (type.IsSubclassOf(typeof(BaseRoute)))
                                {
                                    try
                                    {
                                        BaseRoute ir = (BaseRoute)Activator.CreateInstance(type);
                                    }
                                    catch (Exception e)
                                    {
                                        Debug.Write(e.ToString());
                                    }
                                }
                                else if (typeof(IService).IsAssignableFrom(type))
                                {
                                    var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance);
                                    foreach (var m in methods)
                                    {
                                        ParameterInfo[] param;
                                        Type paramtype;
                                        IEnumerable<RouteAttribute> arrts1;
                                        IEnumerable<RewriteAttribute> arrts2;
                                        Func<IOwinContext, Task> func;
                                        bool isdone = false;
                                        arrts1 = m.GetCustomAttributes<RouteAttribute>(false);//处理函数直接注册的路由
                                        param = m.GetParameters();
                                        if (param.Length == 1)
                                        {
                                            foreach (var routeattr in arrts1)
                                            {
                                                if (routeattr.Verbs == null)
                                                {
                                                    if (!_all_route.ContainsKey(routeattr.Path))
                                                    {
                                                        func = HttpHelper.GetOwinTask(type, param[0].ParameterType, m.ReturnType, m, routeattr.MaxLength);
                                                        _all_route.Add(routeattr.Path, func);
                                                        isdone = true;
                                                    }
                                                    else
                                                    {
                                                        Debug.Write(string.Format("Any路径重复注册. path:{0};param:{1};func:{2}", routeattr.Path, param[0].ParameterType.Name, m.Name));
                                                    }
                                                }
                                                else if (routeattr.Verbs == "GET")
                                                {
                                                    var get_route = _verb_route["GET"];
                                                    if (!get_route.ContainsKey(routeattr.Path))
                                                    {
                                                        func = HttpHelper.GetOwinTask(type, param[0].ParameterType, m.ReturnType, m, routeattr.MaxLength);
                                                        get_route.Add(routeattr.Path, func);
                                                    }
                                                    else
                                                    {
                                                        Debug.Write(string.Format("Get路径重复注册. path:{0};param:{1};func:{2}", routeattr.Path, param[0].ParameterType.Name, m.Name));
                                                    }
                                                }
                                                else if (routeattr.Verbs == "POST")
                                                {
                                                    var post_route = _verb_route["POST"];
                                                    if (!post_route.ContainsKey(routeattr.Path))
                                                    {
                                                        func = HttpHelper.GetOwinTask(type, param[0].ParameterType, m.ReturnType, m, routeattr.MaxLength);
                                                        post_route.Add(routeattr.Path, func);
                                                    }
                                                    else
                                                    {
                                                        Debug.Write(string.Format("Post路径重复注册. path:{0};param:{1};func:{2}", routeattr.Path, param[0].ParameterType.Name, m.Name));
                                                    }
                                                }
                                            }
                                            if (!isdone)//处理参数类路由
                                            {
                                                switch (m.Name)
                                                {
                                                    case "Any":
                                                        paramtype = param[0].ParameterType;
                                                        arrts1 = paramtype.GetCustomAttributes<RouteAttribute>(false);
                                                        foreach (var routeattr in arrts1)
                                                        {
                                                            if (!_all_route.ContainsKey(routeattr.Path))
                                                            {
                                                                func = HttpHelper.GetOwinTask(type, paramtype, m.ReturnType, m, routeattr.MaxLength);
                                                                _all_route.Add(routeattr.Path, func);
                                                            }
                                                            else
                                                            {
                                                                Debug.Write(string.Format("Any路径重复注册. path:{0};param:{1};func:{2}", routeattr.Path, paramtype.Name, m.Name));
                                                            }
                                                        }
                                                        break;
                                                    case "Get":
                                                        paramtype = param[0].ParameterType;
                                                        arrts1 = paramtype.GetCustomAttributes<RouteAttribute>(false);
                                                        foreach (var routeattr in arrts1)
                                                        {
                                                            var get_route = _verb_route["GET"];
                                                            if (!get_route.ContainsKey(routeattr.Path))
                                                            {
                                                                func = HttpHelper.GetOwinTask(type, paramtype, m.ReturnType, m, routeattr.MaxLength);
                                                                get_route.Add(routeattr.Path, func);
                                                            }
                                                            else
                                                            {
                                                                Debug.Write(string.Format("Get路径重复注册. path:{0};param:{1};func:{2}", routeattr.Path, paramtype.Name, m.Name));
                                                            }
                                                        }
                                                        break;
                                                    case "Post":
                                                        paramtype = param[0].ParameterType;
                                                        arrts1 = paramtype.GetCustomAttributes<RouteAttribute>(false);
                                                        foreach (var routeattr in arrts1)
                                                        {
                                                            var post_route = _verb_route["POST"];
                                                            if (!post_route.ContainsKey(routeattr.Path))
                                                            {
                                                                func = HttpHelper.GetOwinTask(type, paramtype, m.ReturnType, m, routeattr.MaxLength);
                                                                post_route.Add(routeattr.Path, func);
                                                            }
                                                            else
                                                            {
                                                                Debug.Write(string.Format("Post路径重复注册. path:{0};param:{1};func:{2}", routeattr.Path, paramtype.Name, m.Name));
                                                            }
                                                        }
                                                        break;
                                                    default:
                                                        break;
                                                }
                                            }
                                            //处理伪静态路径（只判断POCO的参数类）
                                            if (m.Name == "Rewrite")
                                            {
                                                paramtype = param[0].ParameterType;
                                                arrts2 = paramtype.GetCustomAttributes<RewriteAttribute>(false);
                                                foreach (var rewriteattr in arrts2)
                                                {
                                                    string[] subpaths = rewriteattr.Path.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                                                    int length = subpaths.Length;
                                                    string pathnode;
                                                    bool isgoing = true;
                                                    RewritePathNode n1, n2;
                                                    n1 = _rewrite_route[length - 1];
                                                    if (n1 == null)
                                                    {
                                                        n1 = new RewritePathNode() { chindren = new Dictionary<string, RewritePathNode>() };
                                                        _rewrite_route[length - 1] = n1;
                                                    }
                                                    List<Tuple<string, int>> keys = new List<Tuple<string, int>>();
                                                    for (int i = 0; i < length; i++)
                                                    {
                                                        pathnode = subpaths[i];
                                                        if (pathnode[0] == '{' && pathnode[pathnode.Length - 1] == '}')
                                                        {
                                                            isgoing = false;
                                                            keys.Add(new Tuple<string, int>(pathnode.Substring(1, pathnode.Length - 2), i));
                                                        }
                                                        else
                                                        {
                                                            if (isgoing)
                                                            {
                                                                if (!n1.chindren.TryGetValue(pathnode, out n2))
                                                                {
                                                                    n2 = new RewritePathNode() { chindren = new Dictionary<string, RewritePathNode>() };
                                                                    n1.chindren.Add(pathnode, n2);
                                                                }
                                                                n1 = n2;
                                                            }
                                                        }
                                                    }
                                                    if (n1.func == null)
                                                    {
                                                        n1.func = HttpHelper.GetOwinRewriteTask(type, paramtype, m.ReturnType, m, rewriteattr.MaxLength, keys);
                                                    }
                                                    else
                                                    {
                                                        Debug.Write(string.Format("Rewrite路径重复注册. path:{0};param:{1};func:{2}", rewriteattr.Path, paramtype.Name, m.Name));
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            var builder = new AppBuilder();
            Configuration(builder);
            _owinApp = builder.Build();
        }

        /// <summary>
        /// JWS所需要的主调函数
        /// <para>每个请求到来，JWS都会调用这个函数</para>
        /// </summary>
        /// <param name="env">新请求的环境字典，具体内容参见OWIN标准</param>
        /// <returns>返回一个正在运行或已经完成的任务</returns>
        public Task OwinMain(IDictionary<string, object> env)
        {
            return _owinApp(env);
        }
        public void Configuration(IAppBuilder builder)
        {
            builder.Run(c =>
            {
                Func<IOwinContext, Task> d;
                var request = c.Request;
                var response = c.Response;
                if (request.Path.HasValue)
                {
                    string path = request.Path.Value;
                    try
                    {
                        //先检索是否有通用版本的路径
                        if (_all_route.TryGetValue(path, out d))
                        {
                            return d(c);
                        }
                        //再检索是否有特定版本的路径
                        Dictionary<string, Func<IOwinContext, Task>> r;
                        if (_verb_route.TryGetValue(request.Method, out r))
                        {
                            if (r.TryGetValue(path, out d))
                            {
                                return d(c);
                            }
                        }
                        //最后处理伪静态路径，maxdpth大于1时才处理，要关闭伪静态处理，可以设置maxdepth为0。
                        if (maxdepth > 0)
                        {
                            RewritePathNode n1, n2;
                            string[] subpaths = path.Split(new char[] { '/' }, maxdepth + 1, StringSplitOptions.RemoveEmptyEntries);
                            int length = subpaths.Length;
                            if (length > 0 && length <= maxdepth)
                            {
                                n1 = _rewrite_route[length - 1];
                                if (n1 != null)
                                {
                                    for (int i = 0; i < length - 1; i++)
                                    {
                                        if (n1.chindren.TryGetValue(subpaths[i], out n2))
                                        {
                                            n1 = n2;
                                        }
                                        else
                                        {
                                            break;
                                        }
                                    }
                                    if (n1.func != null) return n1.func(c, subpaths);
                                }
                            }
                        }
                    }
                    catch (Exception e)
                    {
                        Debug.Write(e.ToString());
                        response.StatusCode = 500;
                        response.ContentLength = 0;
                        return HttpHelper.completeTask;
                    }
                }
                if (NotFountFun != null) return NotFountFun(c);//添加默认处理函数，可以后期注册404响应之类的页面。
                //如果上面未处理，GET返回404,POST取消响应
                if (request.Method == "GET")
                {
                    response.StatusCode = 404;
                    response.ContentLength = 0;
                    return HttpHelper.completeTask;
                }
                else
                {
                    return HttpHelper.cancelTask;
                }
            });
        }

        
    }
}
