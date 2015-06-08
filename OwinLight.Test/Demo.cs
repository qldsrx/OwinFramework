using Microsoft.Owin;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.IO;
using System.Data;
using ServiceStack;
using System.Collections.Specialized;
using System.Reflection.Emit;
using System.Reflection;

namespace OwinLight.Test
{
    public enum ddd : byte
    {
        aa = 1,
        bb = 2,
        cc = 3,
    }

    /// <summary>
    /// 普通属性接收来自URL或FORM表单的数据，接口属性存储文件，若非POST请求或文件个数为0，则接口属性为空。
    /// </summary>
    [Route("/api/dog1", 65536)]
    public class DOG1 : IHasHttpFiles
    {
        public int? id { get; set; }
        public string name { get; set; }
        public Guid token { get; set; }
        public ddd dsc { get; set; }

        /// <summary>
        /// 实现IHasHttpFiles，接收来自Form表单提交的文件，可能为空
        /// </summary>
        public List<HttpFile> HttpFiles { get; set; }
    }

    /// <summary>
    /// 普通属性接收来自URL的数据，POST的内容存入接口属性，若非POST请求或POST内容为空，则接口属性也为空
    /// </summary>
    [Route("/api/dog2", 65536)]
    public class DOG2 : IHasRequestStream
    {
        public int? id { get; set; }
        public string name { get; set; }
        public Guid token { get; set; }
        public ddd dsc { get; set; }

        /// <summary>
        /// 实现IHasRequestStream，接收来自POST请求的数据
        /// </summary>
        public Stream RequestStream { get; set; }
    }
    public class Demo1 : BaseRoute
    {
        public Task GetRoot(IOwinContext context)
        {
            var x = new TaskCompletionSource<object>();
            x.SetResult(null); //调用SetResult后，这个服务即转为完成状态
            //context.Response.ContentType = "text/html; charset=utf-8";
            HttpHelper.WritePart(context, "<h1 style='color:red'>您好，Jexus是全球首款直接支持MS OWIN标准的WEB服务器！</h1>");
            context.WritePart(context.Request.Cookies["api"] ?? "null");
            return x.Task;
        }

        /// <summary>
        /// 静态页提供示例，未做任何缓存和304响应处理，仅演示用。
        /// </summary>
        public Task GetStaticFile(IOwinContext context)
        {
            if (context.Request.Method == "GET" && context.Request.Path.HasValue)
            {                
                string path = HttpHelper.GetMapPath(context.Request.Path.Value);
                FileInfo fi = new FileInfo(path);
                var response = context.Response;
                if (fi.Exists)
                {
                    response.ContentType = MimeTypes.GetMimeType(fi.Extension);
                    response.ContentLength = fi.Length;
                    response.StatusCode = 200;
                    using (FileStream fs = fi.OpenRead())
                    {
                        fs.CopyTo(response.Body);
                    }
                }
                else
                {
                    response.StatusCode = 404;
                    response.Write("<h1 style='color:red'>很抱歉，出现了404错误。</h1>");
                }
                return HttpHelper.completeTask;
            }
            else
            {
                return HttpHelper.cancelTask;
            }
        }
        public Demo1()
        {
            //定义静态路径处理函数，Any表示任意请求类型，Get表示只对GET请求处理，Post表示只对POST请求处理
            Any["/"] = GetRoot;
            //添加默认路由处理函数，该构造函数只会在网站启动时运行一次，因此此处添加不会有问题。
            Startup.NotFountFun = GetStaticFile;
        }
    }

    /// <summary>
    /// BaseService基类有两个属性，可以访问到运行时的请求响应原始对象，需要高级控制时可以直接使用，主要是对Headers和HttpStatus的控制
    /// </summary>
    public class Demo2 : BaseService, IDisposable
    {
        /// <summary>
        /// 自定义类接收GET或POST请求，当请求为POST时，若继承IHasHttpFiles接口，则会将表单提交的文件数据捕获传入到接口属性。路由来自参数类的特性定义。
        /// </summary>
        public object Any(DOG1 request)
        {
            return request;
        }

        /// <summary>
        /// 自定义类接收来自POST的请求，所有POST的原始流将封装到接口属性中，而url附加参数将封装到类的其他属性中，方便调用。
        /// </summary>
        public object Any(DOG2 request)
        {
            if (request.RequestStream == null) return "1";
            StreamReader sr = new StreamReader(request.RequestStream);
            return sr.ReadToEnd();
        }

        public void Rewrite(FileGet request)
        {
            Response.ContentType = "text/html; charset=utf-8";
            Response.Write(string.Format("<h1 style='color:red'>fileid:{0}<br/>filename:{1}</h1>", request.fileid, request.filename));
        }

        /// <summary>
        /// 测试字符串参数，通过函数路由添加，这里请求不区分GET还是POST，GET时，参数为空，路由限制最多接收1024字节内容
        /// </summary>
        /// <param name="request">参数为string时，传入的是POST的字符串数据，UTF8解码</param>
        /// <returns>返回值自动识别类型，也可以强类型，强类型便于自动产生接口文档（下个版本支持）</returns>
        [Route("/api/test1", 1024)]
        public object testString(String request)
        {
            return request;
        }

        /// <summary>
        /// 测试流参数和返回值，通过函数添加路由，指定POST请求，因为流只对POST有意义。
        /// </summary>
        /// <param name="request">参数为Stream时,传入的是原始网络流，不缓存，可以用于大数据流处理</param>
        /// <returns>返回值为Stream时，必须是可以检索长度的，需要访问Length和Position两个属性来推断文档长度</returns>
        [Route("/api/test2", "POST", int.MaxValue)]
        public Stream testStream(Stream request)
        {
            if (request == null) return null;
            MemoryStream ms = new MemoryStream();
            request.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }

        public void Dispose()
        {
            Debug.Write("Dispose Demo2！");
        }
    }
}
