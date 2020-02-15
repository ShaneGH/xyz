using NUnit.Framework;
using System;
using System.Net.Http.Headers;
using Microsoft.FSharp.Core;
using static ShinyHttpCache.Headers.Parser;
using static ShinyHttpCache.Headers.CacheSettings;
using ShinyHttpCache.Tests.TestUtils;
using System.Net.Http;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using static ShinyHttpCache.CachingHttpClient;
using Microsoft.FSharp.Control;
using System.Threading;
using System.Linq;

namespace ShinyHttpCache.Tests
{
    public class SerializationTests
    {
        [Test]
        public async Task SerializeAndDeserializeCompressed()
        {
            // arrange
            var cacheHeaders = CacheSettingsTests.BuildHeadersInflexible(
                cacheControlIsNull: false,
                sharedCache: false, 
                noStore: false,
                immutable: true,
                maxAge: TimeSpan.FromDays(1),
                sMaxAge: TimeSpan.FromDays(1),
                pragma: new FSharpOption<string>("abc"),
                eTag: new EntityTagHeaderValue("\"def\""),
                exipiresUtc: DateTime.UtcNow.AddDays(1), 
                lasModifiedUtc: new FSharpOption<DateTime>(DateTime.UtcNow.AddDays(-1)), 
                vary: new FSharpOption<string>("very vary"));

            var httpResponse = new HttpResponseMessage
            {
                RequestMessage = new HttpRequestMessage
                {
                    RequestUri = new Uri("http://www.com"),
                    Content = new SingleByteContent(3),
                    Method = HttpMethod.Post,
                    Version = new Version(2, 0)
                },
                Content = new SingleByteContent(7),
                ReasonPhrase = "OK",
                StatusCode  = System.Net.HttpStatusCode.OK,
                Version = new Version(2, 0)
            };

            httpResponse.RequestMessage.Headers.Add("x-a-header", "h1");
            httpResponse.RequestMessage.Headers.IfUnmodifiedSince = DateTimeOffset.UtcNow;
            httpResponse.RequestMessage.Content.Headers.ContentLanguage.Add("en-us");

            httpResponse.Headers.Add("x-a-header", "h1");
            httpResponse.Headers.ETag = new EntityTagHeaderValue("\"asdas\"");
            httpResponse.Content.Headers.ContentLanguage.Add("en-us");

            var cachedResponse = await FSharpAsync.StartAsTask(CachedResponse.build(httpResponse), null, default(CancellationToken));
            var cacheSettings = build(cacheHeaders).Value;

            // act
            using (var str = await Serialization.serialize(new CachedValues(cachedResponse, cacheSettings)).ToTask())
            {
                var stream = Streams.getStream(str);
                var result = new List<byte>(1000);
                var buffer = new byte[1000];
                var read = 0;
                do
                {
                    read = await stream.ReadAsync(buffer, 0, 1000);
                    result.AddRange(buffer.Take(read));
                } while (read > 0);

              //  Assert.Fail("############# " + result.Count.ToString());

                using (var str2 = new MemoryStream(result.ToArray()))
                {
                    var backAgain = await Serialization.deserialize<CachedValues>(str2).ToTask();
                }
            }

            // assert
      //      Assert.True(FSharpOption<CacheSettings>.get_IsNone(result));
        }
    }
}