using System;
using System.Net.Http.Headers;
using System.Reflection;
using System.Linq;
using System.Linq.Expressions;

namespace shttp.Tests.TestUtils
{
    static class HttpObjectBuilder
    {
        public static Func<HttpResponseHeaders> BuildHttpResponseHeaders = HttpResponseHeadersFunc();

        private static Func<HttpResponseHeaders> HttpResponseHeadersFunc()
        {
            var constructor = typeof(HttpResponseHeaders)
                .GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance)
                .First(x => x.GetParameters().Length == 0);

            return Expression
                .Lambda<Func<HttpResponseHeaders>>(
                    Expression.New(constructor))
                .Compile();
        }
    }
}
