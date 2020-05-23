module ShinyHttpCache.Serialization.Dtos.V1
open System
open ShinyHttpCache.Model
open ShinyHttpCache.Serialization.HttpResponseValues
open ShinyHttpCache.Utils

module Private =
    let asyncMap f x = async {
        let! x' = x
        return f x'
    }
open Private

type EntityTagDto =
    {
        Type: char
        Value: string
    }    

let toEntityTagDto = function
    | Strong x ->
        {
            Value = x
            Type = 's'
        }
    | Weak x ->
        {
            Value = x
            Type = 'w'
        }

let fromEntityTagDto x =
    match x.Type with
    | 's' -> Strong x.Value 
    | 'w' -> Weak x.Value
    | x -> sprintf "Invalid EntityTag type: '%c'" x |> invalidOp

type ValidatorDto =
    {
        Type: char
        ETag: EntityTagDto option
        ExpirationDateUtc: Nullable<DateTime>
    }

let toValidatorDto = function
    | ETag x ->
        {
            Type = 't'
            ETag = toEntityTagDto x |> Some
            ExpirationDateUtc = Nullable<DateTime>()
        }
    | ExpirationDateUtc x ->
        {
            Type = 'd'
            ETag = None
            ExpirationDateUtc = Nullable<DateTime> x
        }
    | Both (x, y) ->
        {
            Type = 'b'
            ETag = toEntityTagDto x |> Some
            ExpirationDateUtc = Nullable<DateTime> y
        }

let fromValidatorDto x =
    match (x.Type, x.ETag) with
    | 't', Some etag -> fromEntityTagDto etag |> ETag
    | 't', None -> "Expected etag to have a value" |> invalidOp
    | 'd', _ -> x.ExpirationDateUtc.Value |> ExpirationDateUtc
    | 'b', Some etag -> (fromEntityTagDto etag, x.ExpirationDateUtc.Value) |> Both
    | 'b', None -> "Expected etag to have a value" |> invalidOp
    | x, _ -> sprintf "Invalid Validator type: '%c'" x |> invalidOp

type ReValidationSettingsDto = 
    {
        MustRevalidateAtUtc: DateTime
        Validator: ValidatorDto
    }

let toReValidationSettingsDto (x: RevalidationSettings) =
    {
        MustRevalidateAtUtc = x.MustRevalidateAtUtc
        Validator = toValidatorDto x.Validator
    }

let fromReValidationSettingsDto (x: ReValidationSettingsDto) =
    {
        MustRevalidateAtUtc = x.MustRevalidateAtUtc
        Validator = fromValidatorDto x.Validator
    } : RevalidationSettings

type CacheSettingsDto =
    {
        ExpirySettings: ReValidationSettingsDto option
        SharedCache: bool
    }

type CacheValuesDto =
    {
        ShinyHttpCacheVersion: System.Collections.Generic.List<int>
        HttpResponse: CachedResponse
        CacheSettings: CacheSettingsDto
    }

let toCacheSettingsDto (x: CacheSettings) = 
    {
        SharedCache = x.SharedCache
        ExpirySettings = Option.map toReValidationSettingsDto x.ExpirySettings
    }

let fromCacheSettingsDto (x: CacheSettingsDto) = 
    {
        SharedCache = x.SharedCache
        ExpirySettings = Option.map fromReValidationSettingsDto x.ExpirySettings
    } : CacheSettings

let private version = (typedefof<CacheValuesDto>).Assembly.GetName().Version
let toDto (x: CachedValues) =
    buildCachedResponse x.HttpResponse
    |> asyncMap (fun resp ->
        {
            ShinyHttpCacheVersion = SerializableVersion.fromSemanticVersion version
            HttpResponse = resp
            CacheSettings = toCacheSettingsDto x.CacheSettings
        })

let fromDto (x: CacheValuesDto) =
    {
        HttpResponse = toHttpResponseMessage x.HttpResponse
        CacheSettings = fromCacheSettingsDto x.CacheSettings
    } : CachedValues