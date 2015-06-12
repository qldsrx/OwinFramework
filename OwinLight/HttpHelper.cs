using Microsoft.Owin;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Reflection.Emit;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace OwinLight
{
    public static class HttpHelper
    {
        class PropInfo
        {
            public string Name { get; set; }
            public MethodInfo Setter { get; set; }
            public Type Type { get; set; }
        }

        static List<PropInfo> GetSettableProps(Type t)
        {
            return t
                  .GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                  .Select(p => new PropInfo
                  {
                      Name = p.Name,
                      Setter = p.DeclaringType == t ? p.GetSetMethod(true) : p.DeclaringType.GetProperty(p.Name).GetSetMethod(true),
                      Type = p.PropertyType
                  })
                  .Where(info => info.Setter != null)
                  .ToList();
        }

        static readonly MethodInfo getValues = typeof(IReadableStringCollection).GetMethod("GetValues"),
            getItem = typeof(IList<string>).GetProperties(BindingFlags.Instance | BindingFlags.Public)
                        .Where(p => p.GetIndexParameters().Any() && p.GetIndexParameters()[0].ParameterType == typeof(int))
                        .Select(p => p.GetGetMethod()).First();

        static DynamicMethod GetTypeDeserializer(Type type)
        {
            var dm = new DynamicMethod(string.Format("Deserialize{0}", Guid.NewGuid()), null, new[] { type, typeof(IReadableStringCollection) }, true);
            var properties = GetSettableProps(type);
            var il = dm.GetILGenerator();
            il.DeclareLocal(typeof(IList<string>)); //0
            il.DeclareLocal(typeof(string)); //1
            foreach (var item in properties)
            {
                var key = item.Name;
                Type memberType = item.Type;
                var nullUnderlyingType = Nullable.GetUnderlyingType(memberType);
                var unboxType = nullUnderlyingType != null ? nullUnderlyingType : memberType;

                MethodInfo getMethod = null;
                if (unboxType.IsEnum)
                {
                    getMethod = methods[typeof(Enum)];
                }
                else
                {
                    methods.TryGetValue(unboxType, out getMethod);
                }
                if (getMethod != null)
                {
                    var label1 = il.DefineLabel();
                    il.Emit(OpCodes.Ldarg_1);// [IReadableStringCollection]
                    il.Emit(OpCodes.Ldstr, key);// [IReadableStringCollection][key]
                    il.Emit(OpCodes.Callvirt, getValues);// [IList<string>]
                    il.Emit(OpCodes.Dup);// [IList<string>][IList<string>]
                    il.Emit(OpCodes.Stloc_0);// [IList<string>]
                    il.Emit(OpCodes.Brfalse_S, label1);// stack is empty                    
                    if (unboxType.IsArray)
                    {
                        il.Emit(OpCodes.Ldarg_0);// [target]
                        il.Emit(OpCodes.Ldloc_0);// [target][IList<string>]
                        il.Emit(OpCodes.Call, getMethod);// [target]([unbox-value]/[enum-object])
                    }
                    else
                    {
                        il.Emit(OpCodes.Ldloc_0);// [IList<string>]
                        il.Emit(OpCodes.Ldc_I4_0);
                        il.Emit(OpCodes.Callvirt, getItem);// [string]
                        il.Emit(OpCodes.Dup);
                        il.Emit(OpCodes.Stloc_1);
                        il.Emit(OpCodes.Brfalse_S, label1);// stack is empty
                        il.Emit(OpCodes.Ldloc_1);
                        il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length").GetGetMethod());
                        il.Emit(OpCodes.Brfalse_S, label1);// stack is empty
                        il.Emit(OpCodes.Ldarg_0);// [target]
                        il.Emit(OpCodes.Ldloc_1);// [target][string]
                        if (unboxType.IsEnum)
                        {
                            il.Emit(OpCodes.Ldtoken, unboxType); // [target][string][enum-type-token]
                            il.EmitCall(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle"), null);// [target][string][enum-type]
                        }
                        il.Emit(OpCodes.Call, getMethod);//[target]([unbox-value]/[enum-object])
                        if (unboxType.IsEnum)
                        {
                            il.Emit(OpCodes.Unbox_Any, unboxType); // [target][typed-value]
                        }
                        if (nullUnderlyingType != null)
                        {
                            il.Emit(OpCodes.Newobj, memberType.GetConstructor(new[] { nullUnderlyingType }));// [target][value]
                        }
                    }
                    il.Emit(OpCodes.Callvirt, item.Setter); // stack is now [target]

                    il.MarkLabel(label1);// stack is empty
                }
            }
            il.Emit(OpCodes.Ret);
            return dm;
        }

        static DynamicMethod GetTypeDeserializerFromArray(Type type, List<Tuple<string, int>> keys)
        {
            var dm = new DynamicMethod(string.Format("Deserialize{0}", Guid.NewGuid()), null, new[] { type, typeof(string[]) }, true);
            var properties = GetSettableProps(type);
            var setters = (
                from n in keys
                let prop = properties.FirstOrDefault(p => string.Equals(p.Name, n.Item1, StringComparison.OrdinalIgnoreCase))
                select new { Name = n, Property = prop }
              ).ToList();
            var il = dm.GetILGenerator();
            il.DeclareLocal(typeof(IList<string>)); //0
            il.DeclareLocal(typeof(string)); //1
            foreach (var item in setters)
            {
                Type memberType = item.Property.Type;
                var nullUnderlyingType = Nullable.GetUnderlyingType(memberType);
                var unboxType = nullUnderlyingType != null ? nullUnderlyingType : memberType;

                MethodInfo getMethod = null;
                if (unboxType.IsEnum)
                {
                    getMethod = methods[typeof(Enum)];
                }
                else if (!unboxType.IsArray) //不处理数组
                {
                    methods.TryGetValue(unboxType, out getMethod);
                }
                if (getMethod != null)
                {
                    var label1 = il.DefineLabel();
                    il.Emit(OpCodes.Ldarg_1);// [string[]]
                    il.Emit(OpCodes.Ldc_I4, item.Name.Item2);// [string[]][index]
                    il.Emit(OpCodes.Ldelem_Ref);//[string]
                    il.Emit(OpCodes.Dup);
                    il.Emit(OpCodes.Stloc_1);
                    il.Emit(OpCodes.Brfalse_S, label1);// stack is empty
                    il.Emit(OpCodes.Ldloc_1);
                    il.Emit(OpCodes.Callvirt, typeof(string).GetProperty("Length").GetGetMethod());
                    il.Emit(OpCodes.Brfalse_S, label1);// stack is empty
                    il.Emit(OpCodes.Ldarg_0);// [target]
                    il.Emit(OpCodes.Ldloc_1);// [target][string]
                    if (unboxType.IsEnum)
                    {
                        il.Emit(OpCodes.Ldtoken, unboxType); // [target][string][enum-type-token]
                        il.EmitCall(OpCodes.Call, typeof(Type).GetMethod("GetTypeFromHandle"), null);// [target][string][enum-type]
                    }
                    il.Emit(OpCodes.Call, getMethod);//[target]([unbox-value]/[enum-object])
                    if (unboxType.IsEnum)
                    {
                        il.Emit(OpCodes.Unbox_Any, unboxType); // [target][typed-value]
                    }
                    if (nullUnderlyingType != null)
                    {
                        il.Emit(OpCodes.Newobj, memberType.GetConstructor(new[] { nullUnderlyingType }));// [target][value]
                    }

                    il.Emit(OpCodes.Callvirt, item.Property.Setter); // stack is now [target]

                    il.MarkLabel(label1);// stack is empty
                }
            }
            il.Emit(OpCodes.Ret);
            return dm;
        }

        public static Func<IOwinContext, Task> GetOwinTask(Type type1, Type type2, Type type3, MethodInfo method, int maxlength, string headers)
        {
            var deserializer1 = GetTypeDeserializer(type2);
            var dm = new DynamicMethod(string.Format("OwinTask{0}", Guid.NewGuid()), typeof(Task), new[] { typeof(IOwinContext) }, true);
            ILGenerator il = dm.GetILGenerator();
            var alldone = il.DefineLabel();
            var next = il.DefineLabel();
            var retlabel = il.DefineLabel();
            var label1 = il.DefineLabel();
            var label2 = il.DefineLabel();
            var label3 = il.DefineLabel();
            var label4 = il.DefineLabel();
            var label5 = il.DefineLabel();
            var label6 = il.DefineLabel();
            var label7 = il.DefineLabel();
            var label8 = il.DefineLabel();
            var label9 = il.DefineLabel();
            il.DeclareLocal(typeof(IOwinRequest));//0
            il.DeclareLocal(typeof(IOwinResponse));//1
            il.DeclareLocal(typeof(string));//2
            il.DeclareLocal(typeof(int));//3
            il.DeclareLocal(typeof(byte[]));//4
            il.DeclareLocal(type1);//5
            il.DeclareLocal(type2);//6            
            il.DeclareLocal(typeof(ReadableStringCollection));//7
            il.DeclareLocal(typeof(List<HttpFile>));//8
            il.DeclareLocal(typeof(Stream));//9
            il.DeclareLocal(typeof(Task));//10
            il.DeclareLocal(typeof(String));//11
            il.DeclareLocal(typeof(int));//12
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Callvirt, typeof(IOwinContext).GetProperty("Request").GetGetMethod());
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IOwinContext).GetProperty("Response").GetGetMethod());
            il.Emit(OpCodes.Stloc_1);
            il.Emit(OpCodes.Newobj, type1.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null));
            il.Emit(OpCodes.Stloc_S, (byte)5);
            if (typeof(IDisposable).IsAssignableFrom(type1))
                il.BeginExceptionBlock();//stack is empty，to deal with IDisposable
            il.Emit(OpCodes.Ldloc_S, (byte)5);//[typed-object1]
            il.Emit(OpCodes.Dup);//[typed-object1][typed-object1]            
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IService).GetProperty("Request").GetSetMethod());
            il.Emit(OpCodes.Ldloc_1);
            il.Emit(OpCodes.Callvirt, typeof(IService).GetProperty("Response").GetSetMethod()); // stack is empty
            if (type2 != typeof(string) && type2 != typeof(Stream)) //添加string和Stream类型支持，特殊处理。
            {
                il.Emit(OpCodes.Newobj, type2.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null));//[typed-object2]
                il.Emit(OpCodes.Stloc_S, (byte)6);// stack is empty
            }
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("Method").GetGetMethod());
            il.Emit(OpCodes.Ldstr, "POST");
            il.Emit(OpCodes.Call, typeof(String).GetMethod("Equals", new[] { typeof(string), typeof(string) }));
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("Headers").GetGetMethod());
            il.Emit(OpCodes.Ldstr, "Content-Length");
            il.Emit(OpCodes.Callvirt, typeof(IReadableStringCollection).GetMethod("Get"));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stloc_2);
            il.Emit(OpCodes.Brfalse, label1);// stack is empty 判断是否为空，为空则取消任务
            il.Emit(OpCodes.Ldloc_2);
            il.Emit(OpCodes.Ldloca_S, (byte)3);
            il.Emit(OpCodes.Call, typeof(int).GetMethod("TryParse", new[] { typeof(string), typeof(Int32).MakeByRefType() }));
            il.Emit(OpCodes.Brfalse, label1);//stack is empty
            il.Emit(OpCodes.Ldloc_3);
            il.Emit(OpCodes.Brfalse, next);//stack is empty,if Content-Length == 0,then goto next
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("Body").GetGetMethod());
            il.Emit(OpCodes.Stloc_S, (byte)9); //stack is empty, store RequestStream
            if (type2 == typeof(Stream))
            {
                il.Emit(OpCodes.Ldloc_S, (byte)9);
                il.Emit(OpCodes.Stloc_S, (byte)6); // set RequestStream to User's Stream
                il.Emit(OpCodes.Br, label5);// stack is empty
            }
            else if (typeof(IHasRequestStream).IsAssignableFrom(type2))
            {
                il.Emit(OpCodes.Ldloc_S, (byte)6);
                il.Emit(OpCodes.Ldloc_S, (byte)9);
                il.Emit(OpCodes.Callvirt, typeof(IHasRequestStream).GetProperty("RequestStream").GetSetMethod());
                il.Emit(OpCodes.Br, next);// stack is empty,下面去读取url部分参数
            }
            il.Emit(OpCodes.Ldloc_3);
            il.Emit(OpCodes.Ldc_I4, maxlength);
            il.Emit(OpCodes.Cgt);
            il.Emit(OpCodes.Brtrue, label1);//stack is empty,超过限制后不处理请求            
            il.Emit(OpCodes.Ldloc_3);
            il.Emit(OpCodes.Newarr, typeof(byte));
            il.Emit(OpCodes.Stloc_S, (byte)4);//stack is empty

            il.MarkLabel(label9); // loop flag
            il.Emit(OpCodes.Ldloc_S, (byte)9);
            il.Emit(OpCodes.Ldloc_S, (byte)4);
            il.Emit(OpCodes.Ldloc_S, (byte)12);//初始为0，以后为offset
            il.Emit(OpCodes.Ldloc_3);
            il.Emit(OpCodes.Ldloc_S, (byte)12);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Callvirt, typeof(Stream).GetMethod("Read", new[] { typeof(byte[]), typeof(int), typeof(int) }));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, label8);// stack is empty
            il.Emit(OpCodes.Ldloc_S, (byte)12);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stloc_S, (byte)12);//[int]
            il.Emit(OpCodes.Ldloc_3);
            il.Emit(OpCodes.Clt);
            il.Emit(OpCodes.Brtrue_S, label9); // loop start

            il.Emit(OpCodes.Ldloc_3); //No meaning,only fit the stack
            il.MarkLabel(label8);//[int]
            il.Emit(OpCodes.Pop);// stack is empty
            il.Emit(OpCodes.Ldloc_3);
            il.Emit(OpCodes.Ldloc_S, (byte)12);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brfalse, label1);// stack is empty
            if (type2 == typeof(string))
            {
                il.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8").GetGetMethod());
                il.Emit(OpCodes.Ldloc_S, (byte)4);
                il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetString", new[] { typeof(byte[]) }));
                il.Emit(OpCodes.Stloc_S, (byte)6);
                il.Emit(OpCodes.Br, label5);// stack is empty
            }
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("ContentType").GetGetMethod());
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stloc_2);
            il.Emit(OpCodes.Brfalse, label4);
            il.Emit(OpCodes.Ldloc_2);
            il.Emit(OpCodes.Ldstr, "x-www-form-urlencoded");
            il.Emit(OpCodes.Callvirt, typeof(String).GetMethod("Contains"));
            il.Emit(OpCodes.Brfalse_S, label6);// stack is empty
            il.Emit(OpCodes.Ldloc_S, (byte)6);
            il.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8").GetGetMethod());
            il.Emit(OpCodes.Ldloc_S, (byte)4);
            il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetString", new[] { typeof(byte[]) }));
            il.Emit(OpCodes.Call, typeof(Microsoft.Owin.Helpers.WebHelpers).GetMethod("ParseForm", new[] { typeof(string) }));
            il.Emit(OpCodes.Call, deserializer1);
            il.Emit(OpCodes.Leave, next);// stack is empty

            il.MarkLabel(label6);// stack is empty,处理multipart/form-data
            il.Emit(OpCodes.Ldloc_2);
            il.Emit(OpCodes.Ldstr, "multipart/form-data");
            il.Emit(OpCodes.Callvirt, typeof(String).GetMethod("Contains"));
            il.Emit(OpCodes.Brfalse_S, label2);// stack is empty
            il.Emit(OpCodes.Ldloc_S, (byte)4);
            il.Emit(OpCodes.Newobj, typeof(MemoryStream).GetConstructor(new[] { typeof(byte[]) }));
            il.Emit(OpCodes.Ldloc_2);
            il.Emit(OpCodes.Ldloca_S, (byte)7);
            il.Emit(OpCodes.Ldloca_S, (byte)8);
            il.Emit(OpCodes.Call, typeof(HttpHelper).GetMethod("ParseFormData"));
            il.Emit(OpCodes.Ldloc_S, (byte)6);
            il.Emit(OpCodes.Ldloc_S, (byte)7);
            il.Emit(OpCodes.Call, deserializer1);
            if (typeof(IHasHttpFiles).IsAssignableFrom(type2))
            {
                il.Emit(OpCodes.Ldloc_S, (byte)8);
                il.Emit(OpCodes.Callvirt, typeof(List<HttpFile>).GetProperty("Count").GetGetMethod());
                il.Emit(OpCodes.Brfalse_S, label7);
                il.Emit(OpCodes.Ldloc_S, (byte)6);
                il.Emit(OpCodes.Ldloc_S, (byte)8);
                il.Emit(OpCodes.Callvirt, typeof(IHasHttpFiles).GetProperty("HttpFiles").GetSetMethod());
                il.MarkLabel(label7);
            }
            il.Emit(OpCodes.Leave_S, next);// stack is empty

            il.MarkLabel(label2);// stack is empty,处理xml反序列化
            il.Emit(OpCodes.Ldloc_2);
            il.Emit(OpCodes.Ldstr, "xml");
            il.Emit(OpCodes.Callvirt, typeof(String).GetMethod("Contains"));
            il.Emit(OpCodes.Brtrue_S, label3);// stack is empty
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("Headers").GetGetMethod());
            il.Emit(OpCodes.Ldstr, "format");
            il.Emit(OpCodes.Callvirt, typeof(IReadableStringCollection).GetMethod("Get"));
            il.Emit(OpCodes.Ldstr, "xml");
            il.Emit(OpCodes.Call, typeof(String).GetMethod("Equals", new[] { typeof(string), typeof(string) }));
            il.Emit(OpCodes.Brfalse_S, label4);

            il.MarkLabel(label3);// stack is empty,处理json反序列化
            il.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8").GetGetMethod());
            il.Emit(OpCodes.Ldloc_S, (byte)4);
            il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetString", new[] { typeof(byte[]) }));
            il.Emit(OpCodes.Call, typeof(ServiceStack.Text.StringExtensions).GetMethod("FromXml", new[] { typeof(string) }).MakeGenericMethod(type2));
            il.Emit(OpCodes.Stloc_S, (byte)6);
            il.Emit(OpCodes.Leave_S, next);// stack is empty

            il.MarkLabel(label4);// stack is empty
            il.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8").GetGetMethod());
            il.Emit(OpCodes.Ldloc_S, (byte)4);
            il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetString", new[] { typeof(byte[]) }));
            il.Emit(OpCodes.Call, typeof(ServiceStack.Text.StringExtensions).GetMethod("FromJson", new[] { typeof(string) }).MakeGenericMethod(type2));
            il.Emit(OpCodes.Stloc_S, (byte)6);
            il.Emit(OpCodes.Leave_S, next);// stack is empty

            il.BeginCatchBlock(typeof(Exception)); // stack is Exception
#if DEBUG
            il.Emit(OpCodes.Callvirt, typeof(Object).GetMethod("ToString"));
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, typeof(Debug).GetMethod("Write"));
#endif
            il.EndExceptionBlock();

            il.MarkLabel(label1);// stack is empty
            il.Emit(OpCodes.Ldsfld, typeof(HttpHelper).GetField("cancelTask"));
            il.Emit(OpCodes.Stloc_S, (byte)10);//store in local variable
            il.Emit(OpCodes.Br, retlabel);

            il.MarkLabel(next);// stack is empty
            if (type2 != typeof(string) && type2 != typeof(Stream))
            {
                il.Emit(OpCodes.Ldloc_S, (byte)6);
                il.Emit(OpCodes.Ldloc_0);
                il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("Query").GetGetMethod());
                il.Emit(OpCodes.Call, deserializer1);
            }
            il.MarkLabel(label5);// stack is empty
            il.Emit(OpCodes.Ldloc_S, (byte)5);
            il.Emit(OpCodes.Ldloc_S, (byte)6);
            il.Emit(OpCodes.Callvirt, method);
            if (type3 != typeof(void))
            {
                var nullLabel = il.DefineLabel();
                var streamLabel = il.DefineLabel();
                var stringLabel = il.DefineLabel();
                var label10 = il.DefineLabel();
                var headsend1 = il.DefineLabel();
                var headsend2 = il.DefineLabel();
                var nocallback = il.DefineLabel();
                il.Emit(OpCodes.Dup);//[object][object]
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Brtrue, nullLabel);//[object] 如果对象为空引用，则直接返回0字节响应。
                il.Emit(OpCodes.Dup);//[object][object]
                il.Emit(OpCodes.Isinst, typeof(string)); //测试是否是字符串，字符串就不需要序列化了。
                il.Emit(OpCodes.Brtrue_S, stringLabel);//[object]
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Isinst, typeof(Stream));//[object][Stream or null]
                il.Emit(OpCodes.Brtrue, streamLabel);//[object]
                il.Emit(OpCodes.Call, typeof(ServiceStack.Text.StringExtensions).GetMethod("ToJson").MakeGenericMethod(type3));

                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentType").GetGetMethod());
                il.Emit(OpCodes.Brtrue_S, label10);
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldstr, "text/json; charset=utf-8");
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentType").GetSetMethod());
                il.Emit(OpCodes.Br_S, label10);

                il.MarkLabel(stringLabel);//[object]
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentType").GetGetMethod());
                il.Emit(OpCodes.Brtrue_S, label10);
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldstr, "text/plain; charset=utf-8");
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentType").GetSetMethod());
                il.MarkLabel(label10);
                il.Emit(OpCodes.Castclass, typeof(string));
                il.Emit(OpCodes.Stloc_2);
                il.Emit(OpCodes.Ldloc_0);//[IOwinRequest],添加jsonp处理
                il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("Query").GetGetMethod());
                il.Emit(OpCodes.Ldstr, "callback");
                il.Emit(OpCodes.Callvirt, typeof(IReadableStringCollection).GetMethod("Get"));
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Stloc_S, (byte)11);
                il.Emit(OpCodes.Brfalse_S, nocallback);//stack is empty
                il.Emit(OpCodes.Ldloc_S, (byte)11);
                il.Emit(OpCodes.Ldstr, "(");
                il.Emit(OpCodes.Ldloc_2);
                il.Emit(OpCodes.Ldstr, ")");
                il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string), typeof(string), typeof(string) }));
                il.Emit(OpCodes.Stloc_2);
                il.MarkLabel(nocallback);
                il.Emit(OpCodes.Ldloc_1);//[IOwinResponse]
                il.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8").GetGetMethod());
                il.Emit(OpCodes.Ldloc_2);
                il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetBytes", new[] { typeof(string) }));//[IOwinResponse][byte[]]
                il.Emit(OpCodes.Dup);//[IOwinResponse][byte[]][byte[]]
                il.Emit(OpCodes.Stloc_S, (byte)4);//[IOwinResponse][byte[]]
                il.Emit(OpCodes.Ldloc_S, (byte)5);
                il.Emit(OpCodes.Callvirt, typeof(IService).GetProperty("IsHeadersSended").GetGetMethod());
                il.Emit(OpCodes.Brtrue_S, headsend1);//已经发送过http头的话，就不设置文档长度
                il.Emit(OpCodes.Ldloc_1);//[IOwinResponse][byte[]][IOwinResponse]
                il.Emit(OpCodes.Ldloc_S, (byte)4);//[IOwinResponse][byte[]][IOwinResponse][byte[]]
                il.Emit(OpCodes.Callvirt, typeof(byte[]).GetProperty("LongLength").GetGetMethod());
                il.Emit(OpCodes.Newobj, typeof(long?).GetConstructor(new[] { typeof(long) }));//[IOwinResponse][byte[]][IOwinResponse][long?]
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentLength").GetSetMethod());
                if (headers != null && headers.Contains(":"))//如果有自定义响应头，则添加，但前提是响应头尚未被发送过
                {
                    il.Emit(OpCodes.Ldloc_1);
                    il.Emit(OpCodes.Ldstr, headers);
                    il.Emit(OpCodes.Call, typeof(HttpHelper).GetMethod("SetResponseHeaders"));
                }
                il.MarkLabel(headsend1);
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetMethod("Write", new[] { typeof(byte[]) }));
                il.Emit(OpCodes.Br, alldone);// stack is empty

                il.MarkLabel(streamLabel);//[object]
                il.Emit(OpCodes.Castclass, typeof(Stream));
                il.Emit(OpCodes.Stloc_S, (byte)9);
                il.Emit(OpCodes.Ldloc_S, (byte)5);
                il.Emit(OpCodes.Callvirt, typeof(IService).GetProperty("IsHeadersSended").GetGetMethod());
                il.Emit(OpCodes.Brtrue_S, headsend2);//已经发送过http头的话，就不设置文档长度
                il.BeginExceptionBlock();//获取流长度，设置ContentLength，失败也无所谓，忽略异常
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldloc_S, (byte)9);
                il.Emit(OpCodes.Callvirt, typeof(Stream).GetProperty("Length").GetGetMethod());
                il.Emit(OpCodes.Ldloc_S, (byte)9);
                il.Emit(OpCodes.Callvirt, typeof(Stream).GetProperty("Position").GetGetMethod());
                il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Newobj, typeof(long?).GetConstructor(new[] { typeof(long) }));
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentLength").GetSetMethod());
                il.BeginCatchBlock(typeof(object));
                il.EndExceptionBlock();
                if (headers != null && headers.Contains(":"))//如果有自定义响应头，则添加，但前提是响应头尚未被发送过
                {
                    il.Emit(OpCodes.Ldloc_1);
                    il.Emit(OpCodes.Ldstr, headers);
                    il.Emit(OpCodes.Call, typeof(HttpHelper).GetMethod("SetResponseHeaders"));
                }
                il.MarkLabel(headsend2);
                il.BeginExceptionBlock();//捕获输出异常，最终处理IDisposable                
                il.Emit(OpCodes.Ldloc_S, (byte)9);
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("Body").GetGetMethod());
                il.Emit(OpCodes.Callvirt, typeof(Stream).GetMethod("CopyTo", new[] { typeof(Stream) }));
                il.BeginFinallyBlock();
                il.Emit(OpCodes.Ldloc_S, (byte)9);
                il.Emit(OpCodes.Callvirt, typeof(Stream).GetMethod("Close"));
                il.Emit(OpCodes.Ldloc_S, (byte)9);
                il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose"));
                il.EndExceptionBlock();
                il.Emit(OpCodes.Br_S, alldone);// stack is empty

                il.MarkLabel(nullLabel);//[object]
                il.Emit(OpCodes.Pop);// stack is empty
                il.Emit(OpCodes.Ldloc_S, (byte)5);
                il.Emit(OpCodes.Callvirt, typeof(IService).GetProperty("IsHeadersSended").GetGetMethod());
                il.Emit(OpCodes.Brtrue_S, alldone);//已经发送过http头的话，就不设置文档长度
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldc_I8, 0L);
                il.Emit(OpCodes.Newobj, typeof(long?).GetConstructor(new[] { typeof(long) }));
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentLength").GetSetMethod());
                if (headers != null && headers.Contains(":"))//如果有自定义响应头，则添加，但前提是响应头尚未被发送过
                {
                    il.Emit(OpCodes.Ldloc_1);
                    il.Emit(OpCodes.Ldstr, headers);
                    il.Emit(OpCodes.Call, typeof(HttpHelper).GetMethod("SetResponseHeaders"));
                }
            }
            else
            {
                il.Emit(OpCodes.Ldloc_S, (byte)5);
                il.Emit(OpCodes.Callvirt, typeof(IService).GetProperty("IsHeadersSended").GetGetMethod());
                il.Emit(OpCodes.Brtrue_S, alldone);//已经发送过http头的话，就不设置文档长度
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldc_I8, 0L);
                il.Emit(OpCodes.Newobj, typeof(long?).GetConstructor(new[] { typeof(long) }));
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentLength").GetSetMethod());
                if (headers != null && headers.Contains(":"))//如果有自定义响应头，则添加，但前提是响应头尚未被发送过
                {
                    il.Emit(OpCodes.Ldloc_1);
                    il.Emit(OpCodes.Ldstr, headers);
                    il.Emit(OpCodes.Call, typeof(HttpHelper).GetMethod("SetResponseHeaders"));
                }
            }
            il.MarkLabel(alldone);
            il.Emit(OpCodes.Ldsfld, typeof(HttpHelper).GetField("completeTask"));
            il.Emit(OpCodes.Stloc_S, (byte)10);
            il.MarkLabel(retlabel);
            if (typeof(IDisposable).IsAssignableFrom(type1))
            {
                il.BeginFinallyBlock();
                il.Emit(OpCodes.Ldloc_S, (byte)5);
                il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose"));
                il.EndExceptionBlock();
            }
            il.Emit(OpCodes.Ldloc_S, (byte)10);
            il.Emit(OpCodes.Ret);//[Task]            
            return (Func<IOwinContext, Task>)dm.CreateDelegate(typeof(Func<IOwinContext, Task>));
        }

        public static Func<IOwinContext, string[], Task> GetOwinRewriteTask(Type type1, Type type2, Type type3, MethodInfo method, int maxlength, string headers, List<Tuple<string, int>> keys)
        {
            var deserializer1 = GetTypeDeserializer(type2);
            var deserializer2 = GetTypeDeserializerFromArray(type2, keys);
            var dm = new DynamicMethod(string.Format("OwinRegexTask{0}", Guid.NewGuid()), typeof(Task), new[] { typeof(IOwinContext), typeof(string[]) }, true);
            ILGenerator il = dm.GetILGenerator();
            var alldone = il.DefineLabel();
            var next = il.DefineLabel();
            var retlabel = il.DefineLabel();
            var label1 = il.DefineLabel();
            var label2 = il.DefineLabel();
            var label3 = il.DefineLabel();
            var label4 = il.DefineLabel();
            var label5 = il.DefineLabel();
            var label6 = il.DefineLabel();
            var label7 = il.DefineLabel();
            var label8 = il.DefineLabel();
            var label9 = il.DefineLabel();
            il.DeclareLocal(typeof(IOwinRequest));//0
            il.DeclareLocal(typeof(IOwinResponse));//1
            il.DeclareLocal(typeof(string));//2
            il.DeclareLocal(typeof(int));//3
            il.DeclareLocal(typeof(byte[]));//4
            il.DeclareLocal(type1);//5
            il.DeclareLocal(type2);//6            
            il.DeclareLocal(typeof(ReadableStringCollection));//7
            il.DeclareLocal(typeof(List<HttpFile>));//8
            il.DeclareLocal(typeof(Stream));//9
            il.DeclareLocal(typeof(Task));//10
            il.DeclareLocal(typeof(string));//11
            il.DeclareLocal(typeof(int));//12
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Callvirt, typeof(IOwinContext).GetProperty("Request").GetGetMethod());
            il.Emit(OpCodes.Stloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IOwinContext).GetProperty("Response").GetGetMethod());
            il.Emit(OpCodes.Stloc_1);
            il.Emit(OpCodes.Newobj, type1.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null));
            il.Emit(OpCodes.Stloc_S, (byte)5);
            if (typeof(IDisposable).IsAssignableFrom(type1))
                il.BeginExceptionBlock();//stack is empty，to deal with IDisposable
            il.Emit(OpCodes.Ldloc_S, (byte)5);//[typed-object1]
            il.Emit(OpCodes.Dup);//[typed-object1][typed-object1]            
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IService).GetProperty("Request").GetSetMethod());
            il.Emit(OpCodes.Ldloc_1);
            il.Emit(OpCodes.Callvirt, typeof(IService).GetProperty("Response").GetSetMethod()); // stack is empty
            il.Emit(OpCodes.Newobj, type2.GetConstructor(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stloc_S, (byte)6);//store type2
            il.Emit(OpCodes.Ldarg_1);//[type2][string[]]
            il.Emit(OpCodes.Call, deserializer2);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("Method").GetGetMethod());
            il.Emit(OpCodes.Ldstr, "POST");
            il.Emit(OpCodes.Call, typeof(String).GetMethod("Equals", new[] { typeof(string), typeof(string) }));
            il.Emit(OpCodes.Brfalse, next);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("Headers").GetGetMethod());
            il.Emit(OpCodes.Ldstr, "Content-Length");
            il.Emit(OpCodes.Callvirt, typeof(IReadableStringCollection).GetMethod("Get"));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stloc_2);
            il.Emit(OpCodes.Brfalse, label1);// stack is empty 判断是否为空，为空则取消任务
            il.Emit(OpCodes.Ldloc_2);
            il.Emit(OpCodes.Ldloca_S, (byte)3);
            il.Emit(OpCodes.Call, typeof(int).GetMethod("TryParse", new[] { typeof(string), typeof(Int32).MakeByRefType() }));
            il.Emit(OpCodes.Brfalse, label1);//stack is empty
            il.Emit(OpCodes.Ldloc_3);
            il.Emit(OpCodes.Brfalse, next);//stack is empty,if Content-Length == 0,then goto next
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("Body").GetGetMethod());
            il.Emit(OpCodes.Stloc_S, (byte)9); //stack is empty, store RequestStream
            if (typeof(IHasRequestStream).IsAssignableFrom(type2))
            {
                il.Emit(OpCodes.Ldloc_S, (byte)6);
                il.Emit(OpCodes.Ldloc_S, (byte)9);
                il.Emit(OpCodes.Callvirt, typeof(IHasRequestStream).GetProperty("RequestStream").GetSetMethod());
                il.Emit(OpCodes.Br, next);// stack is empty,下面去读取url部分参数
            }
            il.Emit(OpCodes.Ldloc_3);
            il.Emit(OpCodes.Ldc_I4, maxlength);
            il.Emit(OpCodes.Cgt);
            il.Emit(OpCodes.Brtrue, label1);//stack is empty,超过限制后不处理请求            
            il.Emit(OpCodes.Ldloc_3);
            il.Emit(OpCodes.Newarr, typeof(byte));
            il.Emit(OpCodes.Stloc_S, (byte)4);//stack is empty

            il.MarkLabel(label9); // loop flag
            il.Emit(OpCodes.Ldloc_S, (byte)9);
            il.Emit(OpCodes.Ldloc_S, (byte)4);
            il.Emit(OpCodes.Ldloc_S, (byte)12);//初始为0，以后为offset
            il.Emit(OpCodes.Ldloc_3);
            il.Emit(OpCodes.Ldloc_S, (byte)12);
            il.Emit(OpCodes.Sub);
            il.Emit(OpCodes.Callvirt, typeof(Stream).GetMethod("Read", new[] { typeof(byte[]), typeof(int), typeof(int) }));
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Brfalse, label8);// stack is empty
            il.Emit(OpCodes.Ldloc_S, (byte)12);
            il.Emit(OpCodes.Add);
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stloc_S, (byte)12);//[int]
            il.Emit(OpCodes.Ldloc_3);
            il.Emit(OpCodes.Clt);
            il.Emit(OpCodes.Brtrue_S, label9); // loop start

            il.Emit(OpCodes.Ldloc_3); //No meaning,only fit the stack
            il.MarkLabel(label8);//[int]
            il.Emit(OpCodes.Pop);// stack is empty
            il.Emit(OpCodes.Ldloc_3);
            il.Emit(OpCodes.Ldloc_S, (byte)12);
            il.Emit(OpCodes.Ceq);
            il.Emit(OpCodes.Brfalse, label1);// stack is empty
            il.BeginExceptionBlock();
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("ContentType").GetGetMethod());
            il.Emit(OpCodes.Dup);
            il.Emit(OpCodes.Stloc_2);
            il.Emit(OpCodes.Brfalse, label4);
            il.Emit(OpCodes.Ldloc_2);
            il.Emit(OpCodes.Ldstr, "x-www-form-urlencoded");
            il.Emit(OpCodes.Callvirt, typeof(String).GetMethod("Contains"));
            il.Emit(OpCodes.Brfalse_S, label6);// stack is empty
            il.Emit(OpCodes.Ldloc_S, (byte)6);
            il.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8").GetGetMethod());
            il.Emit(OpCodes.Ldloc_S, (byte)4);
            il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetString", new[] { typeof(byte[]) }));
            il.Emit(OpCodes.Call, typeof(Microsoft.Owin.Helpers.WebHelpers).GetMethod("ParseForm", new[] { typeof(string) }));
            il.Emit(OpCodes.Call, deserializer1);
            il.Emit(OpCodes.Leave, next);// stack is empty

            il.MarkLabel(label6);// stack is empty,处理multipart/form-data
            il.Emit(OpCodes.Ldloc_2);
            il.Emit(OpCodes.Ldstr, "multipart/form-data");
            il.Emit(OpCodes.Callvirt, typeof(String).GetMethod("Contains"));
            il.Emit(OpCodes.Brfalse_S, label2);// stack is empty
            il.Emit(OpCodes.Ldloc_S, (byte)4);
            il.Emit(OpCodes.Newobj, typeof(MemoryStream).GetConstructor(new[] { typeof(byte[]) }));
            il.Emit(OpCodes.Ldloc_2);
            il.Emit(OpCodes.Ldloca_S, (byte)7);
            il.Emit(OpCodes.Ldloca_S, (byte)8);
            il.Emit(OpCodes.Call, typeof(HttpHelper).GetMethod("ParseFormData"));
            il.Emit(OpCodes.Ldloc_S, (byte)6);
            il.Emit(OpCodes.Ldloc_S, (byte)7);
            il.Emit(OpCodes.Call, deserializer1);
            if (typeof(IHasHttpFiles).IsAssignableFrom(type2))
            {
                il.Emit(OpCodes.Ldloc_S, (byte)8);
                il.Emit(OpCodes.Callvirt, typeof(List<HttpFile>).GetProperty("Count").GetGetMethod());
                il.Emit(OpCodes.Brfalse_S, label7);
                il.Emit(OpCodes.Ldloc_S, (byte)6);
                il.Emit(OpCodes.Ldloc_S, (byte)8);
                il.Emit(OpCodes.Callvirt, typeof(IHasHttpFiles).GetProperty("HttpFiles").GetSetMethod());
                il.MarkLabel(label7);
            }
            il.Emit(OpCodes.Leave_S, next);// stack is empty

            il.MarkLabel(label2);// stack is empty,处理xml反序列化
            il.Emit(OpCodes.Ldloc_2);
            il.Emit(OpCodes.Ldstr, "xml");
            il.Emit(OpCodes.Callvirt, typeof(String).GetMethod("Contains"));
            il.Emit(OpCodes.Brtrue_S, label3);// stack is empty
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("Headers").GetGetMethod());
            il.Emit(OpCodes.Ldstr, "format");
            il.Emit(OpCodes.Callvirt, typeof(IReadableStringCollection).GetMethod("Get"));
            il.Emit(OpCodes.Ldstr, "xml");
            il.Emit(OpCodes.Call, typeof(String).GetMethod("Equals", new[] { typeof(string), typeof(string) }));
            il.Emit(OpCodes.Brfalse_S, label4);

            il.MarkLabel(label3);// stack is empty,处理json反序列化
            il.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8").GetGetMethod());
            il.Emit(OpCodes.Ldloc_S, (byte)4);
            il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetString", new[] { typeof(byte[]) }));
            il.Emit(OpCodes.Call, typeof(ServiceStack.Text.StringExtensions).GetMethod("FromXml", new[] { typeof(string) }).MakeGenericMethod(type2));
            il.Emit(OpCodes.Stloc_S, (byte)6);
            il.Emit(OpCodes.Leave_S, next);// stack is empty

            il.MarkLabel(label4);// stack is empty
            il.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8").GetGetMethod());
            il.Emit(OpCodes.Ldloc_S, (byte)4);
            il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetString", new[] { typeof(byte[]) }));
            il.Emit(OpCodes.Call, typeof(ServiceStack.Text.StringExtensions).GetMethod("FromJson", new[] { typeof(string) }).MakeGenericMethod(type2));
            il.Emit(OpCodes.Stloc_S, (byte)6);
            il.Emit(OpCodes.Leave_S, next);// stack is empty

            il.BeginCatchBlock(typeof(Exception)); // stack is Exception
#if DEBUG
            il.Emit(OpCodes.Callvirt, typeof(Object).GetMethod("ToString"));
            il.Emit(OpCodes.Ldnull);
            il.Emit(OpCodes.Call, typeof(Debug).GetMethod("Write"));
#endif
            il.EndExceptionBlock();

            il.MarkLabel(label1);// stack is empty
            il.Emit(OpCodes.Ldsfld, typeof(HttpHelper).GetField("cancelTask"));
            il.Emit(OpCodes.Stloc_S, (byte)10);//store in local variable
            il.Emit(OpCodes.Br, retlabel);

            il.MarkLabel(next);// stack is empty
            il.Emit(OpCodes.Ldloc_S, (byte)6);
            il.Emit(OpCodes.Ldloc_0);
            il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("Query").GetGetMethod());
            il.Emit(OpCodes.Call, deserializer1);
            il.MarkLabel(label5);// stack is empty
            il.Emit(OpCodes.Ldloc_S, (byte)5);
            il.Emit(OpCodes.Ldloc_S, (byte)6);
            il.Emit(OpCodes.Callvirt, method);
            if (type3 != typeof(void))
            {
                var nullLabel = il.DefineLabel();
                var streamLabel = il.DefineLabel();
                var stringLabel = il.DefineLabel();
                var label10 = il.DefineLabel();
                var headsend1 = il.DefineLabel();
                var headsend2 = il.DefineLabel();
                var nocallback = il.DefineLabel();
                il.Emit(OpCodes.Dup);//[object][object]
                il.Emit(OpCodes.Ldnull);
                il.Emit(OpCodes.Ceq);
                il.Emit(OpCodes.Brtrue, nullLabel);//[object] 如果对象为空引用，则直接返回0字节响应。
                il.Emit(OpCodes.Dup);//[object][object]
                il.Emit(OpCodes.Isinst, typeof(string)); //测试是否是字符串，字符串就不需要序列化了。
                il.Emit(OpCodes.Brtrue_S, stringLabel);//[object]
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Isinst, typeof(Stream));//[object][Stream or null]
                il.Emit(OpCodes.Brtrue, streamLabel);//[object]
                il.Emit(OpCodes.Call, typeof(ServiceStack.Text.StringExtensions).GetMethod("ToJson").MakeGenericMethod(type3));
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentType").GetGetMethod());
                il.Emit(OpCodes.Brtrue_S, label10);
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldstr, "text/json; charset=utf-8");
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentType").GetSetMethod());
                il.Emit(OpCodes.Br_S, label10);

                il.MarkLabel(stringLabel);//[object]
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentType").GetGetMethod());
                il.Emit(OpCodes.Brtrue_S, label10);
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldstr, "text/plain; charset=utf-8");
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentType").GetSetMethod());
                il.MarkLabel(label10);
                il.Emit(OpCodes.Castclass, typeof(string));
                il.Emit(OpCodes.Stloc_2);
                il.Emit(OpCodes.Ldloc_0);//[IOwinRequest],添加jsonp处理
                il.Emit(OpCodes.Callvirt, typeof(IOwinRequest).GetProperty("Query").GetGetMethod());
                il.Emit(OpCodes.Ldstr, "callback");
                il.Emit(OpCodes.Callvirt, typeof(IReadableStringCollection).GetMethod("Get"));
                il.Emit(OpCodes.Dup);
                il.Emit(OpCodes.Stloc_S, (byte)11);
                il.Emit(OpCodes.Brfalse_S, nocallback);//stack is empty
                il.Emit(OpCodes.Ldloc_S, (byte)11);
                il.Emit(OpCodes.Ldstr, "(");
                il.Emit(OpCodes.Ldloc_2);
                il.Emit(OpCodes.Ldstr, ")");
                il.Emit(OpCodes.Call, typeof(string).GetMethod("Concat", new[] { typeof(string), typeof(string), typeof(string), typeof(string) }));
                il.Emit(OpCodes.Stloc_2);
                il.MarkLabel(nocallback);
                il.Emit(OpCodes.Ldloc_1);//[IOwinResponse]
                il.Emit(OpCodes.Call, typeof(Encoding).GetProperty("UTF8").GetGetMethod());
                il.Emit(OpCodes.Ldloc_2);
                il.Emit(OpCodes.Callvirt, typeof(Encoding).GetMethod("GetBytes", new[] { typeof(string) }));//[IOwinResponse][byte[]]
                il.Emit(OpCodes.Dup);//[IOwinResponse][byte[]][byte[]]
                il.Emit(OpCodes.Stloc_S, (byte)4);//[IOwinResponse][byte[]]
                il.Emit(OpCodes.Ldloc_S, (byte)5);
                il.Emit(OpCodes.Callvirt, typeof(IService).GetProperty("IsHeadersSended").GetGetMethod());
                il.Emit(OpCodes.Brtrue_S, headsend1);//已经发送过http头的话，就不设置文档长度
                il.Emit(OpCodes.Ldloc_1);//[IOwinResponse][byte[]][IOwinResponse]
                il.Emit(OpCodes.Ldloc_S, (byte)4);//[IOwinResponse][byte[]][IOwinResponse][byte[]]
                il.Emit(OpCodes.Callvirt, typeof(byte[]).GetProperty("LongLength").GetGetMethod());
                il.Emit(OpCodes.Newobj, typeof(long?).GetConstructor(new[] { typeof(long) }));//[IOwinResponse][byte[]][IOwinResponse][long?]
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentLength").GetSetMethod());
                if (headers != null && headers.Contains(":"))//如果有自定义响应头，则添加，但前提是响应头尚未被发送过
                {
                    il.Emit(OpCodes.Ldloc_1);
                    il.Emit(OpCodes.Ldstr, headers);
                    il.Emit(OpCodes.Call, typeof(HttpHelper).GetMethod("SetResponseHeaders"));
                }
                il.MarkLabel(headsend1);
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetMethod("Write", new[] { typeof(byte[]) }));
                il.Emit(OpCodes.Br, alldone);// stack is empty

                il.MarkLabel(streamLabel);//[object]
                il.Emit(OpCodes.Castclass, typeof(Stream));
                il.Emit(OpCodes.Stloc_S, (byte)9);
                il.Emit(OpCodes.Ldloc_S, (byte)5);
                il.Emit(OpCodes.Callvirt, typeof(IService).GetProperty("IsHeadersSended").GetGetMethod());
                il.Emit(OpCodes.Brtrue_S, headsend2);//已经发送过http头的话，就不设置文档长度
                il.BeginExceptionBlock();//获取流长度，设置ContentLength，失败也无所谓，忽略异常
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldloc_S, (byte)9);
                il.Emit(OpCodes.Callvirt, typeof(Stream).GetProperty("Length").GetGetMethod());
                il.Emit(OpCodes.Ldloc_S, (byte)9);
                il.Emit(OpCodes.Callvirt, typeof(Stream).GetProperty("Position").GetGetMethod());
                il.Emit(OpCodes.Sub);
                il.Emit(OpCodes.Newobj, typeof(long?).GetConstructor(new[] { typeof(long) }));
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentLength").GetSetMethod());
                il.BeginCatchBlock(typeof(object));
                il.EndExceptionBlock();
                if (headers != null && headers.Contains(":"))//如果有自定义响应头，则添加，但前提是响应头尚未被发送过
                {
                    il.Emit(OpCodes.Ldloc_1);
                    il.Emit(OpCodes.Ldstr, headers);
                    il.Emit(OpCodes.Call, typeof(HttpHelper).GetMethod("SetResponseHeaders"));
                }
                il.MarkLabel(headsend2);
                il.BeginExceptionBlock();//捕获输出异常，最终处理IDisposable
                il.Emit(OpCodes.Ldloc_S, (byte)9);
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("Body").GetGetMethod());
                il.Emit(OpCodes.Callvirt, typeof(Stream).GetMethod("CopyTo", new[] { typeof(Stream) }));
                il.BeginFinallyBlock();
                il.Emit(OpCodes.Ldloc_S, (byte)9);
                il.Emit(OpCodes.Callvirt, typeof(Stream).GetMethod("Close"));
                il.Emit(OpCodes.Ldloc_S, (byte)9);
                il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose"));
                il.EndExceptionBlock();
                il.Emit(OpCodes.Br_S, alldone);// stack is empty

                il.MarkLabel(nullLabel);//[object]
                il.Emit(OpCodes.Pop);// stack is empty
                il.Emit(OpCodes.Ldloc_S, (byte)5);
                il.Emit(OpCodes.Callvirt, typeof(IService).GetProperty("IsHeadersSended").GetGetMethod());
                il.Emit(OpCodes.Brtrue_S, alldone);//已经发送过http头的话，就不设置文档长度
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldc_I8, 0L);
                il.Emit(OpCodes.Newobj, typeof(long?).GetConstructor(new[] { typeof(long) }));
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentLength").GetSetMethod());
                if (headers != null && headers.Contains(":"))//如果有自定义响应头，则添加，但前提是响应头尚未被发送过
                {
                    il.Emit(OpCodes.Ldloc_1);
                    il.Emit(OpCodes.Ldstr, headers);
                    il.Emit(OpCodes.Call, typeof(HttpHelper).GetMethod("SetResponseHeaders"));
                }
            }
            else
            {
                il.Emit(OpCodes.Ldloc_S, (byte)5);
                il.Emit(OpCodes.Callvirt, typeof(IService).GetProperty("IsHeadersSended").GetGetMethod());
                il.Emit(OpCodes.Brtrue_S, alldone);//已经发送过http头的话，就不设置文档长度
                il.Emit(OpCodes.Ldloc_1);
                il.Emit(OpCodes.Ldc_I8, 0L);
                il.Emit(OpCodes.Newobj, typeof(long?).GetConstructor(new[] { typeof(long) }));
                il.Emit(OpCodes.Callvirt, typeof(IOwinResponse).GetProperty("ContentLength").GetSetMethod());
                if (headers != null && headers.Contains(":"))//如果有自定义响应头，则添加，但前提是响应头尚未被发送过
                {
                    il.Emit(OpCodes.Ldloc_1);
                    il.Emit(OpCodes.Ldstr, headers);
                    il.Emit(OpCodes.Call, typeof(HttpHelper).GetMethod("SetResponseHeaders"));
                }
            }
            il.MarkLabel(alldone);
            il.Emit(OpCodes.Ldsfld, typeof(HttpHelper).GetField("completeTask"));
            il.Emit(OpCodes.Stloc_S, (byte)10);
            il.MarkLabel(retlabel);
            if (typeof(IDisposable).IsAssignableFrom(type1))
            {
                il.BeginFinallyBlock();
                il.Emit(OpCodes.Ldloc_S, (byte)5);
                il.Emit(OpCodes.Callvirt, typeof(IDisposable).GetMethod("Dispose"));
                il.EndExceptionBlock();
            }
            il.Emit(OpCodes.Ldloc_S, (byte)10);
            il.Emit(OpCodes.Ret);//[Task]            
            return (Func<IOwinContext, string[], Task>)dm.CreateDelegate(typeof(Func<IOwinContext, string[], Task>));
        }

        static Dictionary<Type, MethodInfo> methods = new Dictionary<Type, MethodInfo>();

        public static Task completeTask;
        public static Task cancelTask;
        static HttpHelper()
        {
            var x = new TaskCompletionSource<object>();
            x.SetResult(null);
            completeTask = x.Task;

            x = new TaskCompletionSource<object>();
            x.SetCanceled();
            cancelTask = x.Task;

            methods[typeof(string)] = new Func<string, string>(t => t).Method;
            methods[typeof(char)] = new Func<string, char>(t => t.Length > 0 ? t[0] : default(char)).Method;
            methods[typeof(bool)] = new Func<string, bool>(t =>
                {
                    int tmp;
                    if (int.TryParse(t, out tmp))
                    {
                        return tmp != 0;
                    }
                    return String.Compare(t, "true", true) == 0;
                }).Method;
            methods[typeof(byte)] = typeof(byte).GetMethod("Parse", new[] { typeof(string) });
            methods[typeof(sbyte)] = typeof(sbyte).GetMethod("Parse", new[] { typeof(string) });
            methods[typeof(short)] = typeof(short).GetMethod("Parse", new[] { typeof(string) });
            methods[typeof(ushort)] = typeof(ushort).GetMethod("Parse", new[] { typeof(string) });
            methods[typeof(int)] = typeof(int).GetMethod("Parse", new[] { typeof(string) });
            methods[typeof(uint)] = typeof(uint).GetMethod("Parse", new[] { typeof(string) });
            methods[typeof(long)] = typeof(long).GetMethod("Parse", new[] { typeof(string) });
            methods[typeof(ulong)] = typeof(ulong).GetMethod("Parse", new[] { typeof(string) });
            methods[typeof(float)] = typeof(float).GetMethod("Parse", new[] { typeof(string) });
            methods[typeof(double)] = typeof(double).GetMethod("Parse", new[] { typeof(string) });
            methods[typeof(decimal)] = typeof(decimal).GetMethod("Parse", new[] { typeof(string) });
            methods[typeof(DateTime)] = typeof(DateTime).GetMethod("Parse", new[] { typeof(string) });
            methods[typeof(Guid)] = typeof(Guid).GetMethod("Parse", new[] { typeof(string) });
            //这是枚举类型的支持，先处理整型值枚举，后处理字符串值的匹配
            methods[typeof(Enum)] = new Func<string, Type, object>((t1, t2) =>
            {
                try
                {
                    return Convert.ChangeType(t1, Enum.GetUnderlyingType(t2));
                }
                catch
                {
                    object obj = 0;
                    try
                    {
                        obj = Enum.Parse(t2, t1);
                    }
                    catch { }
                    return obj;
                }
            }).Method;
            //下面对基本数据类型的数组形式提供支持,根据需要自行增加，这里只提供了4个可能用到的。
            methods[typeof(bool[])] = new Func<IList<string>, bool[]>(t =>
            {
                int count = t.Count;
                bool[] result = new bool[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = bool.Parse(t[i]);
                }
                return result;
            }).Method;
            methods[typeof(byte[])] = new Func<IList<string>, byte[]>(t =>
            {
                int count = t.Count;
                byte[] result = new byte[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = byte.Parse(t[i]);
                }
                return result;
            }).Method;
            methods[typeof(string[])] = new Func<IList<string>, string[]>(t =>
            {
                int count = t.Count;
                string[] result = new string[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = t[i];
                }
                return result;
            }).Method;
            methods[typeof(int[])] = new Func<IList<string>, int[]>(t =>
            {
                int count = t.Count;
                int[] result = new int[count];
                for (int i = 0; i < count; i++)
                {
                    result[i] = int.Parse(t[i]);
                }
                return result;
            }).Method;
        }
        /// <summary>
        /// 一次性写入响应
        /// </summary>
        /// <param name="context">OWIN上下文</param>
        /// <param name="buffer">缓存</param>
        /// <param name="iscache">客户端是否允许缓存</param>
        /// <returns></returns>
        public static Task WriteTotal(this IOwinContext context, byte[] buffer, bool iscache = false)
        {
            if (buffer != null)
            {
                var response = context.Response;
                response.ContentLength = buffer.Length;
                if (!iscache)
                {
                    response.Headers["Cache-Control"] = "no-cache";
                    response.Headers["Pragma"] = "no-cache";
                }
                response.Write(buffer);
            }
            return completeTask;
        }

        /// <summary>
        /// 一次性写入响应
        /// </summary>
        /// <param name="context">OWIN上下文</param>
        /// <param name="text">缓存</param>
        /// <param name="iscache">客户端是否允许缓存</param>
        /// <returns></returns>
        public static Task WriteTotal(this IOwinContext context, string text, bool iscache = false)
        {
            if (text != null)
            {
                var buffer = Encoding.UTF8.GetBytes(text);
                return WriteTotal(context, buffer, iscache);
            }
            return completeTask;
        }

        /// <summary>
        /// 多次写入响应数据
        /// </summary>
        /// <param name="context">OWIN上下文</param>
        /// <param name="buffer">缓存</param>
        /// <param name="iscache">客户端是否允许缓存</param>
        /// <returns></returns>
        public static Task WritePart(this IOwinContext context, byte[] buffer, bool iscache = false)
        {
            if (buffer != null)
            {
                var response = context.Response;
                if (!iscache)
                {
                    response.Headers["Cache-Control"] = "no-cache";
                    response.Headers["Pragma"] = "no-cache";
                }
                response.Write(buffer);
            }
            return completeTask;
        }

        public static Task WritePart(this IOwinContext context, string text, bool iscache = false)
        {
            if (text != null)
            {
                var buffer = Encoding.UTF8.GetBytes(text);
                return WritePart(context, buffer, iscache);
            }
            return completeTask;
        }

        static Regex boundaryReg = new Regex(@"(?<=[:; ]boundary=)[^; ]*", RegexOptions.Compiled);
        public static void ParseFormData(MemoryStream ms, string ct, out ReadableStringCollection formValues, out List<HttpFile> files)
        {
            try
            {
                var boundary = boundaryReg.Match(ct).Value;
                var multipart = new HttpMultipart(ms, boundary);

                IDictionary<string, string[]> store = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
                Dictionary<string, List<string>> state = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

                List<string> list;
                files = new List<HttpFile>();
                foreach (var httpMultipartBoundary in multipart.GetBoundaries())
                {
                    if (string.IsNullOrEmpty(httpMultipartBoundary.Filename))
                    {
                        string name = httpMultipartBoundary.Name;
                        if (name != null)
                        {
                            string value = new StreamReader(httpMultipartBoundary.Value).ReadToEnd();
                            if (!state.TryGetValue(name, out list))
                            {
                                state.Add(name, new List<string>(1) { value });
                            }
                            else
                            {
                                list.Add(value);
                            }
                        }
                    }
                    else
                    {
                        files.Add(new HttpFile(httpMultipartBoundary));
                    }
                }
                foreach (KeyValuePair<string, List<string>> pair in state)
                {
                    store.Add(pair.Key, pair.Value.ToArray());
                }
                formValues = new ReadableStringCollection(store);
            }
            catch (Exception ex)
            {
                formValues = null;
                files = null;
                Debug.Write(ex.ToString());
            }
        }
        public static void AddHttpRangeResponseHeaders(this IOwinResponse response, long rangeStart, long rangeEnd, long contentLength)
        {
            response.Headers[HttpHeaders.ContentRange] = string.Format("bytes {0}-{1}/{2}", rangeStart, rangeEnd, contentLength);
            response.StatusCode = (int)HttpStatusCode.PartialContent;
            response.ContentLength = rangeEnd - rangeStart + 1;
        }

        /// <summary>
        /// 获得当前绝对路径，同时兼容windows和linux（系统自带的都不兼容）。
        /// </summary>
        /// <param name="strPath">指定的路径，支持/|./|../分割</param>
        /// <returns>绝对路径，不带/后缀</returns>
        public static string GetMapPath(string strPath)
        {
            if (strPath == null)
            {
                return AppDomain.CurrentDomain.BaseDirectory;
            }
            else
            {
                List<string> prePath = AppDomain.CurrentDomain.BaseDirectory.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                List<string> srcPath = strPath.Split(new char[] { '/', '\\' }, StringSplitOptions.RemoveEmptyEntries).ToList();
                ComputePath(prePath, srcPath);
                if (prePath.Count > 0 && prePath[0].Contains(":"))//windows
                {
                    if (prePath.Count == 1)
                    {
                        return prePath[0] + "/";
                    }
                    else
                    {
                        return String.Join("/", prePath);
                    }
                }
                else//linux
                {
                    return "/" + String.Join("/", prePath);
                }
            }
        }
        private static void ComputePath(List<string> prePath, List<string> srcPath)
        {
            var precount = prePath.Count;
            foreach (string src in srcPath)
            {
                if (src == "..")
                {
                    if (precount > 1 || (precount == 1 && !prePath[0].Contains(":")))
                    {
                        prePath.RemoveAt(--precount);
                    }
                }
                else if (src != ".")
                {
                    prePath.Add(src);
                    precount++;
                }
            }
        }

        /// <summary>
        /// 设置响应的headers
        /// </summary>
        /// <param name="response">响应</param>
        /// <param name="headers">key-value用冒号隔开，多个头用封号隔开</param>
        public static void SetResponseHeaders(this IOwinResponse response, string headers)
        {
            string[] tmp1 = headers.Split(';');
            int length = tmp1.Length;
            for (int i = 0; i < length; i++)
            {
                string[] tmp2 = tmp1[i].Split(':');
                if (tmp2.Length != 2) throw new Exception("自定义响应头格式有误");
                response.Headers.Set(tmp2[0], tmp2[1]);
            }
        }

        public static string Escape(string srcstr)
        {
            if (srcstr == null) return null;
            return Uri.EscapeDataString(srcstr);
        }

        public static string Unescape(string srcstr)
        {
            if (srcstr == null) return null;
            return Uri.UnescapeDataString(srcstr);
        }
    }
}
