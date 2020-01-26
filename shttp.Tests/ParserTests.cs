using NUnit.Framework;
using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using static ShinyHttp.Headers.Parser;

namespace shttp.Tests
{
    public class ParserTests
    {
        [Test]
        public void Parse_WithEmptyResponse_ReturnsEmptyHeaders()
        {
            // arrange
            var response = new HttpResponseMessage();

            // act
            // assert
            parse(response);
        }

        [Test]
        public void Parse_WithCacheControl_ReturnsCacheControl()
        {
            // arrange
            var response = new HttpResponseMessage();
            response.Headers.CacheControl = new CacheControlHeaderValue
            {
                MaxAge = TimeSpan.FromSeconds(123)
            };

            // act
            var result = parse(response);

            // assert
            Assert.AreEqual(result.CacheControl.Value, response.Headers.CacheControl);
        }

        [Test]
        public void Parse_WithETag_ReturnsETag()
        {
            // arrange
            var response = new HttpResponseMessage();
            response.Headers.ETag = new EntityTagHeaderValue("\"the tag\"", true);

            // act
            var result = parse(response);

            // assert
            Assert.AreEqual(result.ETag.Value, response.Headers.ETag);
        }

        [Test]
        public void Parse_WithExpires_ReturnsExpires()
        {
            // arrange
            var response = new HttpResponseMessage();
            response.Content = new HttpContentX();
            response.Content.Headers.Expires = DateTimeOffset.Now;

            // act
            var result = parse(response);

            // assert
            Assert.AreEqual(response.Content.Headers.Expires.Value.UtcDateTime, result.ExipiresUtc.Value);
        }

        private class HttpContentX : HttpContent
        {
            protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            {
                return Task.CompletedTask;
            }

            protected override bool TryComputeLength(out long length)
            {
                length = 0;
                return true;
            }
        }

        [Test]
        public void Parse_WithLastModified_ReturnsLastModified()
        {
            // arrange
            var response = new HttpResponseMessage();
            response.Content = new HttpContentX();
            response.Content.Headers.LastModified = DateTimeOffset.Now;

            // act
            var result = parse(response);

            // assert
            Assert.AreEqual(response.Content.Headers.LastModified.Value.UtcDateTime, result.LasModifiedUtc.Value);
        }
    }
}