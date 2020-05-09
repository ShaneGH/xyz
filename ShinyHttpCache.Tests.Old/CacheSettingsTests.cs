using NUnit.Framework;
using System;
using System.Net.Http.Headers;
using Microsoft.FSharp.Core;
using static ShinyHttpCache.Headers.Parser;
using static ShinyHttpCache.Headers.CacheSettings;
using ShinyHttpCache.Tests.TestUtils;

namespace ShinyHttpCache.Tests
{
    public class CacheSettingsTests
    {
        public static HttpServerCacheHeaders BuildHeaders(
            bool cacheControlIsNull = false, 
            bool sharedCache = true, 
            bool noStore = false,
            bool immutable = false,
            TimeSpan? maxAge = null,
            TimeSpan? sMaxAge = null,
            FSharpOption<string> pragma = null,
            EntityTagHeaderValue eTag = null,
            FSharpOption<DateTime> exipiresUtc = null, 
            FSharpOption<DateTime> lasModifiedUtc = null, 
            FSharpOption<string> vary = null)
        {
            return BuildHeadersInflexible(
                cacheControlIsNull, 
                sharedCache, 
                noStore,
                immutable,
                maxAge,
                sMaxAge,
                pragma,
                eTag,
                exipiresUtc, 
                lasModifiedUtc, 
                vary);
        }

        public static HttpServerCacheHeaders BuildHeadersInflexible(
            bool cacheControlIsNull, 
            bool sharedCache, 
            bool noStore,
            bool immutable,
            TimeSpan? maxAge,
            TimeSpan? sMaxAge,
            FSharpOption<string> pragma,
            EntityTagHeaderValue eTag,
            FSharpOption<DateTime> exipiresUtc, 
            FSharpOption<DateTime> lasModifiedUtc, 
            FSharpOption<string> vary)
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
                cacheControl.Private = !sharedCache;
            }

            return new HttpServerCacheHeaders(
                cacheControl ?? FSharpOption<CacheControlHeaderValue>.None, 
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
            var result = build(cacheHeaders);

            // assert
            Assert.True(FSharpOption<CacheSettings>.get_IsNone(result));
        }

        [Test]
        public void GetCacheTime_WithAllHeadersToNoStore_RespectsCorrectHeaders()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                noStore: true,
                immutable: true,
                sharedCache: true,
                eTag: new EntityTagHeaderValue("\"an etag\"", false),
                maxAge: TimeSpan.FromDays(1),
                sMaxAge: TimeSpan.FromDays(2),
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = build(cacheHeaders);

            // assert
            CustomAssert.IsNone(result);
        }

        [Test]
        public void GetCacheTime_WithAllHeadersToSMaxAge_RespectsCorrectHeaders()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                immutable: true,
                sharedCache: true,
                eTag: new EntityTagHeaderValue("\"an etag\"", false),
                maxAge: TimeSpan.FromDays(1),
                sMaxAge: TimeSpan.FromDays(2),
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = ((ExpirySettings.Soft)build(cacheHeaders).Value.ExpirySettings).Item;

            // assert
            var both = ((Validator.Both)result.Validator).Item;
            CustomAssert.Roughly(DateTime.UtcNow.AddDays(3), both.Item2);

            var etag = ((EntityTag.Strong)both.Item1).Item;
            Assert.AreEqual("\"an etag\"", etag);
        }

        [Test]
        public void GetCacheTime_WithWeakETag_SetsCorectETag()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                immutable: true,
                sharedCache: true,
                eTag: new EntityTagHeaderValue("\"an etag\"", true),
                maxAge: TimeSpan.FromDays(1),
                sMaxAge: TimeSpan.FromDays(2),
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = ((ExpirySettings.Soft)build(cacheHeaders).Value.ExpirySettings).Item;

            // assert
            var both = ((Validator.Both)result.Validator).Item;
            var etag = ((EntityTag.Weak)both.Item1).Item;
            Assert.AreEqual("\"an etag\"", etag);
        }

        [Test]
        public void GetCacheTime_WithPrivateCache_IgnoresSMaxAge()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                immutable: true,
                sharedCache: false,
                eTag: new EntityTagHeaderValue("\"an etag\"", false),
                maxAge: TimeSpan.FromDays(1),
                sMaxAge: TimeSpan.FromDays(2),
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = ((ExpirySettings.Soft)build(cacheHeaders).Value.ExpirySettings).Item;

            // assert
            CustomAssert.Roughly(DateTime.UtcNow + TimeSpan.FromDays(1), result.MustRevalidateAtUtc);
        }

        [Test]
        public void GetCacheTime_WithPublicCache_UsesSMaxAge()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                immutable: true,
                sharedCache: true,
                eTag: new EntityTagHeaderValue("\"an etag\"", false),
                maxAge: TimeSpan.FromDays(1),
                sMaxAge: TimeSpan.FromDays(2),
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = ((ExpirySettings.Soft)build(cacheHeaders).Value.ExpirySettings).Item;

            // assert
            CustomAssert.Roughly(DateTime.UtcNow + TimeSpan.FromDays(2), result.MustRevalidateAtUtc);
        }

        [Test]
        public void GetCacheTime_WithPublicCacheAndNoSMaxAge_UsesMaxAge()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                immutable: true,
                sharedCache: true,
                eTag: new EntityTagHeaderValue("\"an etag\"", false),
                maxAge: TimeSpan.FromDays(1),
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = ((ExpirySettings.Soft)build(cacheHeaders).Value.ExpirySettings).Item;

            // assert
            CustomAssert.Roughly(DateTime.UtcNow + TimeSpan.FromDays(1), result.MustRevalidateAtUtc);
        }

        [Test]
        public void GetCacheTime_WithNoMaxAge_UsesExpiresInstead()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                immutable: true,
                sharedCache: true,
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = ((ExpirySettings.Soft)build(cacheHeaders).Value.ExpirySettings).Item;

            // assert
            CustomAssert.Roughly(DateTime.UtcNow.AddDays(3), result.MustRevalidateAtUtc);
        }

        [Test]
        public void GetCacheTime_WithNoETag_SetsCorectValidator()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                immutable: true,
                sharedCache: true,
                maxAge: TimeSpan.FromDays(1),
                sMaxAge: TimeSpan.FromDays(2),
                exipiresUtc: DateTime.UtcNow.AddDays(3));

            // act
            var result = ((ExpirySettings.Soft)build(cacheHeaders).Value.ExpirySettings).Item;

            // assert
            var exDate = ((Validator.ExpirationDateUtc)result.Validator).Item;
            CustomAssert.AssertDateAlmost(DateTime.UtcNow.AddDays(3), exDate);
        }

        [Test]
        public void GetCacheTime_WithExpires_SetsCorectValidator()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                immutable: true,
                sharedCache: true,
                eTag: new EntityTagHeaderValue("\"an etag\"", false),
                maxAge: TimeSpan.FromDays(1),
                sMaxAge: TimeSpan.FromDays(2));

            // act
            var result = ((ExpirySettings.Soft)build(cacheHeaders).Value.ExpirySettings).Item;

            // assert
            var etag1 = ((Validator.ETag)result.Validator).Item;
            var etag2 = ((EntityTag.Strong)etag1).Item;
            Assert.AreEqual("\"an etag\"", etag2);
        }

        [Test]
        public void GetCacheTime_WithoutValidate_ReturnsHardExpiryBasedOnMaxAge()
        {
            // arrange
            var cacheHeaders = BuildHeaders(
                immutable: true,
                maxAge: TimeSpan.FromDays(1));

            // act
            var result = ((ExpirySettings.HardUtc)build(cacheHeaders).Value.ExpirySettings).Item;

            // assert
            CustomAssert.AssertDateAlmost(DateTime.UtcNow.AddDays(1), result);
        }

        [Test]
        public void GetCacheTime_WithAllHeadersToImmutable_RespectsCorrectHeaders()
        {
            // arrange
            var cacheHeaders = BuildHeaders(immutable: true);

            // act
            var result = build(cacheHeaders);

            // assert
            Assert.True(result.Value.ExpirySettings.IsNoExpiryDate);
        }

        [Test]
        public void GetCacheTime_WithPublicImmutableCache_SetsSharedCacheFlagCorrectly()
        {
            // arrange
            var cacheHeaders = BuildHeaders(immutable: true, sharedCache: true);

            // act
            var result = build(cacheHeaders);

            // assert
            Assert.True(result.Value.SharedCache);
        }

        [Test]
        public void GetCacheTime_WithPrivateImmutableCache_SetsSharedCacheFlagCorrectly()
        {
            // arrange
            var cacheHeaders = BuildHeaders(immutable: true, sharedCache: false);

            // act
            var result = build(cacheHeaders);

            // assert
            Assert.False(result.Value.SharedCache);
        }

        [Test]
        public void GetCacheTime_WithPublicCacheControlCache_SetsSharedCacheFlagCorrectly()
        {
            // arrange
            var cacheHeaders = BuildHeaders(maxAge: TimeSpan.FromDays(1), sharedCache: true);

            // act
            var result = build(cacheHeaders);

            // assert
            Assert.True(result.Value.SharedCache);
        }

        [Test]
        public void GetCacheTime_WithPrivateCacheControlCache_SetsSharedCacheFlagCorrectly()
        {
            // arrange
            var cacheHeaders = BuildHeaders(maxAge: TimeSpan.FromDays(1), sharedCache: false);

            // act
            var result = build(cacheHeaders);

            // assert
            Assert.False(result.Value.SharedCache);
        }
    }
}