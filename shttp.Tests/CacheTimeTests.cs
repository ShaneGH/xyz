using NUnit.Framework;
using System;
using System.Net.Http.Headers;
using Microsoft.FSharp.Core;
using static ShinyHttp.Headers.Parser;
using static ShinyHttp.Headers.CacheTime;

namespace shttp.Tests
{
    public class CacheTimeTests
    {
        private HttpServerCacheHeaders BuildHeaders(
                bool cacheControlIsNull = false, 
                bool sharedCache = true, 
                bool noStore = false,
                bool immutable = false,
                TimeSpan? maxAge = null,
                TimeSpan? sMaxAge = null,
                FSharpOption<string> pragma = null,
                FSharpOption<EntityTagHeaderValue> eTag = null,
                FSharpOption<DateTime> exipiresUtc = null, 
                FSharpOption<DateTime> lasModifiedUtc = null, 
                FSharpOption<string> vary = null)
        {
            CacheControlHeaderValue cacheControl = null;
            if (!cacheControlIsNull)
            {
                cacheControl = immutable
                    ? CacheControlHeaderValue.Parse("immutable")
                    : new CacheControlHeaderValue();
                cacheControl.NoStore = noStore;
                cacheControl.MaxAge = maxAge;
                cacheControl.SharedMaxAge = sMaxAge;
            }

            return new HttpServerCacheHeaders(
                cacheControl ?? FSharpOption<CacheControlHeaderValue>.None, 
                sharedCache,
                pragma ?? FSharpOption<string>.None,
                eTag ?? FSharpOption<EntityTagHeaderValue>.None,
                exipiresUtc ?? FSharpOption<DateTime>.None, 
                lasModifiedUtc ?? FSharpOption<DateTime>.None, 
                vary ?? FSharpOption<string>.None);
        }

        [Test]
        public void GetCacheTime_WithNoHeaders_ReturnsNone()
        {
            // arrange
            var cacheHeaders = BuildHeaders(cacheControlIsNull: true);

            // act
            var result = getCacheTime(cacheHeaders);

            // assert
            Assert.True(FSharpOption<TimeSpan>.get_IsNone(result));
        }

        [Test]
        public void GetCacheTime_WithAllHeaders_ReturnsNoStoreValue()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                noStore: true,
                immutable: true,
                maxAge: TimeSpan.FromDays(1),
                sMaxAge: TimeSpan.FromDays(2),
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = getCacheTime(cacheHeaders);

            // assert
            Assert.AreEqual(TimeSpan.Zero, result.Value);
        }

        [Test]
        public void GetCacheTime_WithoutNoStore_ReturnsSMaxAgeValue()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                immutable: true,
                maxAge: TimeSpan.FromDays(1),
                sMaxAge: TimeSpan.FromDays(2),
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = getCacheTime(cacheHeaders);

            // assert
            Assert.AreEqual(TimeSpan.FromDays(2), result.Value);
        }

        [Test]
        public void GetCacheTime_WithoutSharedCache_ReturnsMaxAgeValue()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                immutable: true,
                sharedCache: false,
                maxAge: TimeSpan.FromDays(1),
                sMaxAge: TimeSpan.FromDays(2),
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = getCacheTime(cacheHeaders);

            // assert
            Assert.AreEqual(TimeSpan.FromDays(1), result.Value);
        }

        [Test]
        public void GetCacheTime_WithoutSMaxAge_ReturnsMaxAgeValue()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                immutable: true,
                maxAge: TimeSpan.FromDays(1),
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = getCacheTime(cacheHeaders);

            // assert
            Assert.AreEqual(TimeSpan.FromDays(1), result.Value);
        }

        [Test]
        public void GetCacheTime_WithoutMaxAge_ReturnsImmutableValue()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                immutable: true,
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = getCacheTime(cacheHeaders);

            // assert
            Assert.AreEqual(TimeSpan.MaxValue, result.Value);
        }

        [Test]
        public void GetCacheTime_WithoutImmutable_ReturnsExpiresValue()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = getCacheTime(cacheHeaders);

            // assert (rough value)
            Assert.Less(result.Value, DateTime.UtcNow.AddDays(3.1) - DateTime.UtcNow);
            Assert.Greater(result.Value, DateTime.UtcNow.AddDays(2.9) - DateTime.UtcNow);
        }
    }
}