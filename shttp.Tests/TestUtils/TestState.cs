using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Moq;
using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using static ShinyHttp.CachingHttpClient;
using AppTestUtils = ShinyHttp.TestUtils;

namespace shttp.Tests.TestUtils
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
                .Returns(FSharpAsync.AwaitTask(Task.FromResult(FSharpOption<Tuple<HttpResponseMessage, DateTime>>.None)));

            dependencies
                .Setup(x => x.Cache.Put(It.IsAny<Tuple<string, HttpResponseMessage, DateTime>>()))
                .Returns(FSharpAsync.AwaitTask(Task.FromResult(AppTestUtils.unit)));

            dependencies
                .Setup(x => x.Cache.Invalidate(It.IsAny<string>()))
                .Returns(FSharpAsync.AwaitTask(Task.FromResult(AppTestUtils.unit)));

            dependencies
                .Setup(x => x.Cache.BuildUserKey(It.IsAny<HttpRequestMessage>()))
                .Returns<HttpRequestMessage>(GetUserKey);

            return dependencies;
        }

        private static FSharpOption<string> GetUserKey(HttpRequestMessage msg)
        {
            if (!msg.Headers.Contains(UserHeader))
            {
                return FSharpOption<string>.None;
            }

            return msg.Headers
                .GetValues(UserHeader)
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
            byte? addResponseContent = null)
        {
            cahcedUntil = new DateTime(cahcedUntil.Ticks, DateTimeKind.Utc);
            var response = new HttpResponseMessage();
            response.RequestMessage = new HttpRequestMessage();
            if (addRequestContent != null)
                response.RequestMessage.Content = new SingleByteContent(addRequestContent.Value);
            if (addResponseContent != null)
                response.Content = new SingleByteContent(addResponseContent.Value);

            user = user?.Replace("$", "$$");
            var key = $"$:{user}$:{new Uri(url)}";

            Dependencies
                .Setup(x => x.Cache.Get(It.Is<string>(k => k == key)))
                .Returns(Returns);

            FSharpAsync<FSharpOption<Tuple<HttpResponseMessage, DateTime>>> Returns()
            {
                return FSharpAsync.AwaitTask(
                    Task.FromResult(
                        FSharpOption<Tuple<HttpResponseMessage, DateTime>>.Some(
                            Tuple.Create(response, cahcedUntil))));
            }

            return response;
        }
    }
}