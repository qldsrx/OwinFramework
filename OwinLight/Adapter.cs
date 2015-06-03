/***********************************************************
 * jexus部署方法：
 * 1、编译得到OwinLight.dll（也可以自行改名）。
 * 2、将编译得到的dll连同Owin.dll、Microsoft.Owin.dll等文件
 *    一同放置到网站的bin文件夹中
 * 3、在对应网站的jws网站配置文件中加入一句，声明要使用的适配器：
 *    OwinMain=OwinLight.dll,OwinLight.Adapter
 * 4、重启Jexus让配置生效。
 *************************************************************************/

using Microsoft.Owin;
using Microsoft.Owin.Builder;
using Owin;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace OwinLight
{
    public class Adapter
    {
        Func<IDictionary<string, object>, Task> _owinApp;//owin容器
        public static readonly Dictionary<string, Func<IOwinContext, Task>> _all_route = new Dictionary<string, Func<IOwinContext, Task>>();//path处理容器，处理任意版本标准路径
        public static readonly Dictionary<string, Dictionary<string, Func<IOwinContext, Task>>> _verb_route;//path处理容器，处理带版本的标准路径
        static readonly List<Tuple<Regex, Func<IOwinContext, Match, Task>>> _routeRegex = new List<Tuple<Regex, Func<IOwinContext, Match, Task>>>();//path正则处理容器，处理伪静态路径,慎用，影响性能的。

        static Adapter()
        {
            _verb_route = new Dictionary<string, Dictionary<string, Func<IOwinContext, Task>>>(5);
            _verb_route.Add("GET", new Dictionary<string, Func<IOwinContext, Task>>());
            _verb_route.Add("POST", new Dictionary<string, Func<IOwinContext, Task>>());
            //_verb_route.Add("PUT", new Dictionary<string, Func<IOwinContext, Task>>());
            //_verb_route.Add("DELETE", new Dictionary<string, Func<IOwinContext, Task>>());
            //_verb_route.Add("HEAD", new Dictionary<string, Func<IOwinContext, Task>>());
        }
        /// <summary>
        /// 适配器构造函数
        /// </summary>
        public Adapter()
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
                    foreach (var f in files)
                    {
                        Assembly assembly = null;
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
                                        ir.AddRoute(_routeRegex);
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
                                        IEnumerable<RouteAttribute> arrts;
                                        Func<IOwinContext, Task> func;
                                        bool isdone = false;
                                        arrts = m.GetCustomAttributes<RouteAttribute>();
                                        param = m.GetParameters();
                                        if (param.Length == 1)
                                        {
                                            foreach (var routeattr in arrts)
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
                                                        Debug.Write("Any路径重复注册：" + routeattr.Path);
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
                                                        Debug.Write("Get路径重复注册：" + routeattr.Path);
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
                                                        Debug.Write("Post路径重复注册：" + routeattr.Path);
                                                    }
                                                }
                                            }
                                            if (!isdone)
                                            {
                                                switch (m.Name)
                                                {
                                                    case "Any":
                                                        paramtype = param[0].ParameterType;
                                                        arrts = paramtype.GetCustomAttributes<RouteAttribute>();
                                                        foreach (var routeattr in arrts)
                                                        {
                                                            if (!_all_route.ContainsKey(routeattr.Path))
                                                            {
                                                                func = HttpHelper.GetOwinTask(type, paramtype, m.ReturnType, m, routeattr.MaxLength);
                                                                _all_route.Add(routeattr.Path, func);
                                                            }
                                                            else
                                                            {
                                                                Debug.Write("Any路径重复注册：" + routeattr.Path);
                                                            }
                                                        }
                                                        break;
                                                    case "Get":
                                                        paramtype = param[0].ParameterType;
                                                        arrts = paramtype.GetCustomAttributes<RouteAttribute>();
                                                        foreach (var routeattr in arrts)
                                                        {
                                                            var get_route = _verb_route["GET"];
                                                            if (!get_route.ContainsKey(routeattr.Path))
                                                            {
                                                                func = HttpHelper.GetOwinTask(type, paramtype, m.ReturnType, m, routeattr.MaxLength);
                                                                get_route.Add(routeattr.Path, func);
                                                            }
                                                            else
                                                            {
                                                                Debug.Write("Get路径重复注册：" + routeattr.Path);
                                                            }
                                                        }
                                                        break;
                                                    case "Post":
                                                        paramtype = param[0].ParameterType;
                                                        arrts = paramtype.GetCustomAttributes<RouteAttribute>();
                                                        foreach (var routeattr in arrts)
                                                        {
                                                            var post_route = _verb_route["POST"];
                                                            if (!post_route.ContainsKey(routeattr.Path))
                                                            {
                                                                func = HttpHelper.GetOwinTask(type, paramtype, m.ReturnType, m, routeattr.MaxLength);
                                                                post_route.Add(routeattr.Path, func);
                                                            }
                                                            else
                                                            {
                                                                Debug.Write("Post路径重复注册：" + routeattr.Path);
                                                            }
                                                        }
                                                        break;
                                                    default:
                                                        break;
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
                        //最后进行正则匹配，处理伪静态路径，此功能尚未优化处理，不建议多使用。
                        foreach (var item in _routeRegex)
                        {
                            var match = item.Item1.Match(path);
                            if (match.Success)
                            {
                                return item.Item2(c, match);
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
