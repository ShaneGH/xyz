using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Moq;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static ShinyHttpCache.CachingHttpClient;
using AppTestUtils = ShinyHttpCache.TestUtils;

namespace ShinyHttpCache.Tests.TestUtils
{
    class TestState
    {
        public const string UserHeader = "x-test-user";
        public readonly Mock<ICachingHttpClientDependencies> Dependencies = BuildDependencies();

        public Task<HttpResponseMessage> ExecuteRequest(string url = "http://www.com", string user = null)
        {
            var request = new HttpRequestMessage();
            request.RequestUri = new Uri(url);
            if (user != null)
            {
                request.Headers.Add(UserHeader, user);
            }

            var reader = client(request, default(CancellationToken));
            var response = reader.Item.Invoke(Dependencies.Object);
            return FSharpAsync.StartAsTask(response, FSharpOption<TaskCreationOptions>.None, FSharpOption<CancellationToken>.None);
        }

        private static Mock<ICachingHttpClientDependencies> BuildDependencies()
        {
            var dependencies = new Mock<ICachingHttpClientDependencies>();

            dependencies
                .Setup(x => x.Cache.Get(It.IsAny<string>()))
                .Returns(FSharpAsync.AwaitTask(Task.FromResult(FSharpOption<CachedValues>.None)));

            dependencies
                .Setup(x => x.Cache.Put(It.IsAny<Tuple<string, CachedValues>>()))
                .Returns(FSharpAsync.AwaitTask(Task.FromResult(AppTestUtils.unit)));

            dependencies
                .Setup(x => x.Cache.Delete(It.IsAny<string>()))
                .Returns(FSharpAsync.AwaitTask(Task.FromResult(AppTestUtils.unit)));

            dependencies
                .Setup(x => x.Cache.BuildUserKey(It.IsAny<CachedRequest.CachedRequest>()))
                .Returns<CachedRequest.CachedRequest>(GetUserKey);

            return dependencies;
        }

        private static FSharpOption<string> GetUserKey(CachedRequest.CachedRequest msg)
        {
            if (!msg.Headers.Any(x => x.Key == UserHeader))
            {
                return FSharpOption<string>.None;
            }

            return msg.Headers
                .Where(x => x.Key == UserHeader)
                .Select(x => x.Value.FirstOrDefault())
                .FirstOrDefault() ?? FSharpOption<string>.None;
        }

        public HttpResponseMessage AddHttpRequest(
            byte addResponseContent,
            string url = "http://www.com")
        {
            var response = new HttpResponseMessage();
            response.Content = new SingleByteContent(addResponseContent);

            var lck = new object();
            bool first = true;

            Predicate<Tuple<HttpRequestMessage, CancellationToken>> assertUrl = AssertUrl;
            Dependencies
                .Setup(x => x.Send(Match.Create(assertUrl)))
                .Returns<Tuple<HttpRequestMessage, CancellationToken>>(Returns);

            return response;

            bool AssertUrl(Tuple<HttpRequestMessage, CancellationToken> input)
            {
                return input.Item1.RequestUri == new Uri(url);
            }

            FSharpAsync<HttpResponseMessage> Returns(Tuple<HttpRequestMessage, CancellationToken> req)
            {
                lock (lck)
                {
                    if (first) first = false;
                    else throw new NotSupportedException();
                }

                response.RequestMessage = req.Item1;
                var result = Task.FromResult(response);
                return FSharpAsync.AwaitTask(result);
            }
        }

        public HttpResponseMessage AddToCache(
            DateTime cahcedUntil,
            string url = "http://www.com",
            string user = null,
            byte? addRequestContent = null,
            byte? addResponseContent = null,
            HttpMethod method = null,
            Headers.CacheSettings.ExpirySettings expiry = null)
        {
            expiry = expiry ?? Headers.CacheSettings.ExpirySettings.NewHardUtc(DateTime.UtcNow.AddDays(10));
            cahcedUntil = new DateTime(cahcedUntil.Ticks, DateTimeKind.Utc);
            var response = new HttpResponseMessage();
            response.RequestMessage = new HttpRequestMessage();
            if (addRequestContent != null)
                response.RequestMessage.Content = new SingleByteContent(addRequestContent.Value);
            if (addResponseContent != null)
                response.Content = new SingleByteContent(addResponseContent.Value);

            var m = (method == null || method == HttpMethod.Get) ? "G" : null;
            if (m == null)
                throw new NotSupportedException(method?.ToString() ?? "null");

            user = user?.Replace("$", "$$");
            var key = $"{m}$:{user}$:{new Uri(url)}";

            Dependencies
                .Setup(x => x.Cache.Get(It.Is<string>(k => k == key)))
                .Returns(Returns);

            return response;

            FSharpAsync<FSharpOption<CachedValues>> Returns()
            {
                return FSharpAsync.AwaitTask(ReturnsAsync());
            }

            async Task<FSharpOption<CachedValues>> ReturnsAsync()
            {
                var resp = await FSharpAsync.StartAsTask(
                    CachedResponse.build(response), 
                    FSharpOption<TaskCreationOptions>.None, 
                    FSharpOption<CancellationToken>.None);

                return FSharpOption<CachedValues>.Some(new CachedValues(
                    resp, 
                    new Headers.CacheSettings.CacheSettings(expiry, true)));
            }
        }
    }
}