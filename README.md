# OwinFramework
Owin轻量型框架

## 框架的特色
  我用过ServiceStack，看过Nancy的代码风格，这两个框架广受好评（但性能不行），特别是编程风格上面，于是我效仿了它们。目前框架中的序列化部分，还有使用ServiceStack_V3的dll，但由于ServiceStack非常不厚道，自从收费后，免费版本的最后一个版本删了几个常用方法，收费版本直接命名空间大变样，升级直接导致项目出错，因此最终会全部自己实现，不用ServiceStack的类库。

　　该框架最低要求.net4.0环境，因为需要Task类的支持。
## 如何启动
  调试的时候可以用自承载方式，方法和Nancy的一样，但win7以上系统需要管理员权限启动，代码如下：
  ```cs
        static void Main(string[] args)
        {
            var url = "http://+:8080";
            using (WebApp.Start<OwinLight.Startup>(url))
            {
                Console.WriteLine("Running on {0}", url);
                Console.WriteLine("Press enter to exit");
                Console.ReadLine();
            }
        }
  ```
项目最终部署推荐使用Linux+Jexus，因为Jexus会优先处理静态资源，而我的这个框架是不处理静态资源的，如果用IIS部署，需要设置经典模式并自行配置静态路径的处理映射。

* jexus部署方法：
* 1、编译得到OwinLight.dll（也可以自行改名）。
* 2、将编译得到的dll连同Owin.dll、Microsoft.Owin.dll等文件
* 一同放置到网站的bin文件夹中
* 3、在对应网站的jws网站配置文件中加入一句，声明要使用的适配器：
* OwinMain=OwinLight.dll,OwinLight.Startup
* 4、重启Jexus让配置生效。

 

* TinyFox(可以不需要安装mono）部署方法：
* 1、编译得到OwinLight.dll（也可以自行改名）。
* 2、将编译得到的dll连同Owin.dll、Microsoft.Owin.dll等文件
* 一同放置到网站的bin文件夹中(site\wwwroot\bin)
* 3、默认配置文件为TinyFox.exe.config
* 4、运行fox.bat或fox.sh（linux），端口在启动脚本里。
## 各项功能
 ### 静态路由的3种写法
首先来看下这样的写法：
```cs
public Demo1()
{
    //定义静态路径处理函数，Any表示任意请求类型，Get表示只对GET请求处理，Post表示只对POST请求处理
    Any["/"] = GetRoot;
}
```
这和Nancy里面里面是类似的，提供了Any、Get、Post三种属性来定义路径处理函数，不同的是，我的这个函数必须是Func<IOwinContext, Task>形式的，而不接受任何dynamic的参数。这里IOwinContext是web请求响应上下文，包含了请求和响应的各种信息。

下面就看下GetRoot函数的示例： 
```cs
public Task GetRoot(IOwinContext context)
{
    var x = new TaskCompletionSource<object>();
    x.SetResult(null); //调用SetResult后，这个服务即转为完成状态
    context.Response.ContentType = "text/html; charset=utf-8";
    HttpHelper.WritePart(context, "<h1 style='color:red'>您好，Jexus是全球首款直接支持MS OWIN标准的WEB服务器！</h1>");
    return x.Task;
}
```
看到这里，你也许会觉得，这样写太麻烦了。是的，但是某些特殊场合会用的到，因此提供了这样的写法。下面介绍POCO参数方式的编程，和ServiceStack很像，但是更实用。

```cs
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
```
这里我们通过Route这个特性，来告诉框架，有这么一个地址/api/dog1的路由请求，请求内容要被自动封装到这个DOG1里面，这里DOG1还继承了IHasHttpFiles接口，这样还可以接受来自Form表单上传的文件。如果不继承IHasHttpFiles接口，文件内容则不会被接收。DOG1里面的其它属性，可以来自Query也可以来自Form，或者来自Xml和Json的序列化（此时不存在HttpFiles数据）。另外这里的Route特性里，定义了一个65536的数字，意思是，如果POST的数据超过了65536字节数，就忽略请求不处理，用来防攻击的。默认最多接收4M字节。

下面看下这样一个参数类型定义：
```cs
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
```
　也许我们会想自己处理POST请求的数据，同时又要判断URL附带的参数，那么就可以定义这样的类型，集成IHasRequestStream接口后，所有的POST数据不再处理，而是直接将网络原始流设置到这个RequestStream属性上面，注意，它不是缓存的，因此无法访问Length属性，也不能修改Position，更不能直接原样返回，因为Http请求数据不接收完毕，是无法写入响应的。
　
　另外这个Route不单单可以对自定义类进行设置，还可以设置到某个函数上面，来看下这个例子：
　```cs
　 public class Demo2 : BaseService
    {
        public object Any(DOG1 request)
        {
            return request;
        }

        public object Post(DOG2 request)
        {
            if (request.RequestStream == null) return null;
            StreamReader sr = new StreamReader(request.RequestStream);
            return sr.ReadToEnd();
        }

        [Route("/api/test1", 1024)]
        public object testString(String request)
        {
            return request;
        }

        [Route("/api/test2", "POST", int.MaxValue)]
        public Stream testStream(Stream request)
        {
            if (request == null) return null;
            MemoryStream ms = new MemoryStream();
            request.CopyTo(ms);
            ms.Position = 0;
            return ms;
        }
    }
　```
DOG1和DOG2就是前面提到的POCO类型，这里Any方法匹配任意请求，而Post方法则只处理POST请求（还有Get方法类同）。而testString和testStream函数上面也出现了Route定义，这里是针对非自定义类型参数做的路由处理，String和Stream是系统自带的，却往往非常有用。有时候，我们会忽略任何参数，定义一个路径请求，那么直接用testString函数的形式即可，不处理GET请求的URL参数。如果是POST请求，则会封送为String传入函数，Stream的场合，则直接传递原始POST请求的网络流，不缓存。响应也可以为一个不缓存的流，例如FileStream。

### 伪静态路由的支持
```cs
[Rewrite("/api/FileGet/{fileid}/{filename}")]
public class FileGet
{
    public int fileid { get; set; }
    public string filename { get; set; }
    public bool download { get; set; }
}
```
Rewrite特性代替Route，用来定义伪静态响应。伪静态一般用在对url格式有非常高的要求的场合，例如linux下面的wget命令下载文件，只能通过url来识别要保存的文件名。不建议用伪静态，但一定要用的话，这里提供一个非常高效率的支持，处理速度相当快，因为没有用正则。很多框架都是烂在伪静态下面的，正则处理到一些莫名其妙的请求，导致CPU负荷很重，响应变慢。

　　我所支持的伪静态，只做完全匹配，不支持使用通配符*，且一旦遇到了{xxx}的结构，那么后面的内容都作为参数处理，而不做匹配要求。例如这样的写法：[Rewrite("/api/FileGet/{fileid}/{filename}/Get")]，虽然最后还有一个Get的固定路径，但我也认为它是参数，只是一个不需要传递的参数，只有前面的/api/FileGet/才用来区分请求地址用。这样做的好处是，匹配速度非常快。

　　伪静态的实现，通过函数名Rewrite，如果函数名不对，将不加载。
　　
```cs
public class Demo2 : BaseService, IDisposable
{
    public string Rewrite(FileGet request)
    {
        Response.ContentType = "text/html; charset=utf-8";
        return string.Format("<h1 style='color:red'>fileid:{0}<br/>filename:{1}</h1>", request.fileid, request.filename);
    }
}
```
### 处理Form表单提交的文件
　　在前面静态路由的示例中已经出现过，你定义的POCO类型，继承IHasHttpFiles接口的情况下，才会自动帮你解析请求进来的文件，否则，你只能用Stream作为参数，自己处理Post请求的原始数据流。IHasHttpFiles接口有个HttpFiles属性，存放Form表单所提交的全部文件。

 

### 流式处理Post请求的数据
　　在前面静态路由的示例中已经出现过，你定义的POCO类型，继承IHasRequestStream接口的情况下，其接口属性RequestStream将为Post的原始网络流，这对于大数据的流式接收很有利。POCO类型的其它属性来自url附属的参数，这个ServiceStack是不支持的，曾经为了这个网上搜索了下答案，官方给的答案是“没必要支持”，自己从请求的参数里面抓——垃圾。

　　如果没有url的附属参数，你可以直接用Stream作为函数参数，Route特性定义在函数上面，前面已经给出过示例，也可以到项目代码里看。

 

### 多种请求类型自动识别
　　该框架能处理的请求类型有：application/x-www-form-urlencoded、multipart/form-data、application/xml、application/json，如果请求不指定Content-Type，则会读取url的参数format，但format只处理xml、json类型，如果都没有指定，则视为json数据处理。而相应的内容目前只能是json格式，如果希望更改为xml或其他格式，请设置相应为String或Stream类型，并自行序列化。响应文档格式的多样化会在今后的版本考虑添加。

 

### 响应处理
　　对于使用静态或伪静态路由特性的返回值，支持任意类型，可以是object。如果是继承Stream的类型，还会在处理结束时，自动调用Close和Dispose方法，所以不用担心直接返回FileStream后的文件关闭问题。对于其他非字符串类型，会进行Json序列化后输出，目前不提供多种输出，但你可以自行序列化后，以字符串返回，这样就不会再次为你序列化了。

　　响应的文档类型，如果用户设置，则采用用户设置的文档类型，否则自动按照函数返回值类型自动添加。用户还可以自行设置IService接口的IsHeadersSended属性为true，来阻止框架最后自行设置文档长度。默认的文档长度是计算返回值对象的产生的字节长度，但是如果在返回对象之前，用户已经有往响应里写入内容了，那文档长度就不可预知了，因为这里的响应流是不缓存的，这也是为了提高效率，不然输出一个大文件也缓存，服务器吃不消。备注：如果是IIS下面跑，响应只能是缓存的，微软提供的IIS下使用的Owin适配器带输出缓存，还无法关闭。
　　
### 请求响应上下文
框架中定义了这样一个基类，但你也可以自己重新定义，继承IService接口即可：
```cs
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
```
　这个基类中的请求、响应上下文属性，将会在请求时自动赋值，我们可以直接访问最原始的请求和响应对象，用来控制Headers。而那个IsHeadersSended属性，也会在响应首次输出后，变为true，框架在自动处理函数返回值时，会用来判断是否还需要设置文档长度了。当然，你也可以强制设置为true，另外IIS下面带缓存，因此你只能自己设置true，否则永远不会变为true。
　
### 自定义默认处理函数
　当没有路由规则匹配时，默认的处理函数，可以为空，系统在GET请求是自动返回404，而POST请求则直接取消任务，取消任务的场合，看不同的Owin服务器是如何处理的，TinyFox已经可以配合中断Socket了，而微软的几个宿主则没有这么做。取消任务是非常重要的功能，对于攻击防护很有意义，如果你不打算处理POST请求了，那么网络I/O就应该立刻释放，而不是等到数据接收完毕，再给一个响应，这样I/O的占用对服务器来说是个不小的损失。通过设置Startup.NotFountFun委托，你可以自己定义匹配不到时的处理方式，后面在框架的扩展里，会给出一个静态内容的支持示例，用的就是这个默认处理函数支持。
　
### 内置各种便捷函数
　HttpHelper类里面：

GetMapPath，获得当前绝对路径，同时兼容windows和linux，.NET自带的Path.Combine方法，到了linux下面的表现就和windows不同了，因为linux 的"/"代表了系统的根，和网站的根冲突了，但是我这里是要代表网站的根，所以Owin下面没有一个可用的函数，只能自己写。这个函数还支持“../”的写法，查找上级，这样我们在使用相对路径时会很方便。

Escape，Unescape，对url参数进行编码或解码，由于Owin不需要System.Web，所以我们没必要再引用过多的dll进来，于是我封装了这两个方法，支持参数为null的情况，这两个方法本身也设置为了扩展方法，支持null时的调用，调用更方便。补充：对象在调用方法时，如果为空，一般会报“未将对象引用设置到对象实例”的错误，但是扩展方法可以避免这样的报错。

AddHttpRangeResponseHeaders，设置分段响应头，当我们要提供断点续传时，这个函数可以方便的设置响应头。

ParseFormData，解析Form表单数据，如果你要以Stream或String接收Post响应，可以调用这个函数来解析Form表单数据。 

### 复合类型的请求处理
除了简单的POCO类型外，自定义类的属性可以是某个Model类型，这种就是复合类型，此时用来接收POST请求提交的Json数据，同时，url参数也会在Json数据接收后，被添加到自定义类型的简单属性上，确保Query的参数也被自动填充。

　　你也可以用Dictionary<string,object>类型作为请求的参数，此时将Route特性定义到处理函数上即可，POST传递的Json数据，将自动反序列化到这个字典类型上。
　　
## 框架的扩展
　　框架是开源的，可以随便更改。如果希望有后期的支持，请提交需求让我来修改，或者记住自己的改动部分，以便下次合并修改。

　　HttpHelper类是框架的核心处理类，其静态函数里面定义了大量的类型转换函数，这里可以自己添加不存在的类型，或者修改已经存在的类型处理。类型分为两类，数组和非数组，其传递的参数是不同的，源码中有示例，这里不展开了。源码中有段“#if DEBUG”代码，用来设置是否对参数处理过程发生的异常做记录，如果项目是在Debug下面编译的，就会记录，此时如果POST请求的数据不是xml或json格式，却要强制进行转换，就会记录到异常。应用层的异常自行决定是否要捕获，前面的是框架处理过程的异常。另外还有一个Debug类，里面就一个方法Write，框架中用来输入各种异常都会调用它。你可以修改输出的路径，也可以对日志输出进行一些缓存处理，再或者直接不做任何处理，屏蔽掉处理代码。
　　
　　### 静态内容的支持
　　下面给一个通过默认处理函数，添加静态页响应的示例，仅供测试用
　　```cs
　　public class Class1:BaseRoute
{
    public Class1()
    {
        Startup.NotFountFun = GetStaticFile;
    }
    /// <summary>
    /// 静态页提供示例，未做任何缓存和304响应处理，仅测试用。
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
}
　　```
　### 跨域Post的支持
　　　IE8+、谷歌、火狐等浏览器均支持，不考虑IE6的话，可以使用这个功能
　　　```xml
　　　 <appSettings>
    <!--伪静态路径最大深度-->
    <add key="rewritedepth" value="10"/>
    <!--自定义响应头，key-value用冒号隔开，多个头用封号隔开-->
    <add key="responseheaders" value="Access-Control-Allow-Origin:*;Access-Control-Allow-Methods:GET,POST;Access-Control-Allow-Headers:Content-Type"/>
  </appSettings>
　　　```
　　在配置文件里添加如下代码，注释已经很清楚了。而Owin本身没要求配置文件，所以，你的宿主可能不提供配置文件，那么你只是在你的网站根目录下面放一个web.config，我框架会优先查找这个文件的配置，查找不到的情况才会去用应用程序默认的配置文件。HttpHelper.AppSettings用来访问web.config下面的appSettings节点，HttpHelper.ConnectionStrings用来访问web.config下面的connectionStrings节点。
　　
　　### 基础类型继承灵活处理
　　BaseRoute类一般很少会用，也没什么好说的。BaseService类用的最多，用它可以极大的提高开发效率，同时你也可以继承该类实现更多的便利，下面我再给一段示例，演示如何自己继承BaseService或IService来减少编码。可以将这个MyService作为基类，再派生出相关处理下面的类。
　　```cs
　　/// <summary>
/// 自定义服务类示例，添加令牌、数据库支持。
/// </summary>
public class MyService : BaseService, IDisposable
{
    static readonly string connectionString = HttpHelper.ConnectionStrings["demo"].ConnectionString;

    private IDbConnection db;
    public virtual IDbConnection Db
    {
        get
        {
            if (db == null)
            {
                db = new MySql.Data.MySqlClient.MySqlConnection(connectionString);
                db.Open();
            }
            return db;
        }
    }

    private Guid token;
    public Guid Token
    {
        get
        {
            if (token != Guid.Empty)
                return token;
            string loginheader = Request.Headers.Get("token");
            if (loginheader != null)
                return token = new Guid(loginheader);
            else if ((loginheader = Request.Cookies["token"]) != null)
            {
                return token = new Guid(loginheader);
            }
            else
                throw new Exception("用户登录信息未传递");

        }
    }

    public void Dispose()
    {
        if (db != null)
            db.Dispose();
    }
}
　　```

　ORM操作推荐用Dapper，性能最高。这里Db属性有了，Dapper的使用就非常简单了，因为Dapper的操作都是基于连接类的扩展方法。

　　RouteAttribute类也是可以继承的，当你希望某些路由下用特定的响应头，你可以派生出自己的RouteAttribute继承类，将Headers属性定死。框架在检测到你设置过Headers属性后，就不再读取配置文件里面的响应头设置了。
