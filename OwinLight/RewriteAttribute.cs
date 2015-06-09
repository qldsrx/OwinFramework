using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace OwinLight
{
    // 摘要: 
    //     Used to decorate Request DTO's to associate a RESTful request path mapping
    //     with a service. Multiple attributes can be applied to each request DTO, to
    //     map multiple paths to the service.
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
    public class RewriteAttribute : Attribute
    {
        /// <summary>
        /// 添加HTTP响应路径
        /// </summary>
        public RewriteAttribute(string path)
        {
            if (path == null || !path.StartsWith("/")) throw new Exception("路径有误");
            Path = path;
            MaxLength = 4 * 1024 * 1024;//默认限制4M请求字节数
        }

        /// <summary>
        /// 指定http版本添加路径
        /// </summary>
        /// <param name="path">路径</param>
        /// <param name="verbs">http版本，如："GET","POST"</param>
        public RewriteAttribute(string path, string verbs)
        {
            if (path == null || !path.StartsWith("/")) throw new Exception("路径有误");
            Path = path;
            Verbs = verbs;
            MaxLength = 4 * 1024 * 1024;//默认限制4M请求字节数
        }

        public RewriteAttribute(string path, int maxlength)
        {
            if (path == null || !path.StartsWith("/")) throw new Exception("路径有误");
            Path = path;
            MaxLength = maxlength;
        }

        public RewriteAttribute(string path, string verbs, int maxlength)
        {
            if (path == null || !path.StartsWith("/")) throw new Exception("路径有误");
            Path = path;
            Verbs = verbs;
            MaxLength = maxlength;
        }

        /// <summary>
        /// Gets or sets longer text to explain the behaviour of the route.
        /// </summary>
        public string Notes { get; set; }
        /// <summary>
        /// 请勿使用伪静态，这里不做处理
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// Gets or sets short summary of what the route does.
        /// </summary>
        public string Summary { get; set; }

        public string Verbs { get; set; }
        /// <summary>
        /// 最大POST数据长度，超出时自动忽略POST数据。
        /// </summary>
        public int MaxLength { get; set; }

        /// <summary>
        /// 自定义响应头，key-value用冒号隔开，多个头用封号隔开
        /// </summary>
        public string Headers { get; set; }
    }
}
