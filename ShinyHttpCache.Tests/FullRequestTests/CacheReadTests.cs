using Moq;
using NUnit.Framework;
using ShinyHttpCache.Tests.TestUtils;
using System;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace ShinyHttpCache.Tests.FullRequestTests
{
    public class CacheReadTests
    {
        [Test]
        public async Task ClientRequest_WithPreviouslyCachedValue_ReturnsCachedValue()
        {
            // arrange
            var state = new TestState();
            var cachedResponse = state.AddToCache(DateTime.UtcNow.AddDays(1), addResponseContent: 1);
            var serverResponse = state.AddHttpRequest(2);

            // act
            var response = await state.ExecuteRequest();

            // assert
            await CustomAssert.AssertResponse(1, response);
            state.Dependencies
                .Verify(x => x.Send(It.IsAny<Tuple<HttpRequestMessage, CancellationToken>>()), Times.Never);
        }
    }
}