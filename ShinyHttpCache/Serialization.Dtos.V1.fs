module ShinyHttpCache.Serialization.Dtos.V1
open System
open ShinyHttpCace.Utils
open ShinyHttpCache.Model
open ShinyHttpCache.Serialization.HttpResponseValues
open ShinyHttpCache.Utils

type EntityTagDto =
    {
        Type: char
        Value: string
    }    

let toEntityTagDto = function
    | CacheSettings.Strong x ->
        {
            Value = x
            Type = 's'
        }
    | CacheSettings.Weak x ->
        {
            Value = x
            Type = 'w'
        }

let fromEntityTagDto x =
    match x.Type with
    | 's' -> CacheSettings.Strong x.Value 
    | 'w' -> CacheSettings.Weak x.Value
    | x -> sprintf "Invalid EntityTag type: '%c'" x |> invalidOp

type ValidatorDto =
    {
        Type: char
        ETag: EntityTagDto option
        ExpirationDateUtc: Nullable<DateTime>
    }

let toValidatorDto = function
    | CacheSettings.ETag x ->
        {
            Type = 't'
            ETag = toEntityTagDto x |> Some
            ExpirationDateUtc = Nullable<DateTime>()
        }
    | CacheSettings.ExpirationDateUtc x ->
        {
            Type = 'd'
            ETag = None
            ExpirationDateUtc = Nullable<DateTime> x
        }
    | CacheSettings.Both (x, y) ->
        {
            Type = 'b'
            ETag = toEntityTagDto x |> Some
            ExpirationDateUtc = Nullable<DateTime> y
        }

let fromValidatorDto x =
    match (x.Type, x.ETag) with
    | 't', Some etag -> fromEntityTagDto etag |> CacheSettings.ETag
    | 't', None -> "Expected etag to have a value" |> invalidOp
    | 'd', _ -> x.ExpirationDateUtc.Value |> CacheSettings.ExpirationDateUtc
    | 'b', Some etag -> (fromEntityTagDto etag, x.ExpirationDateUtc.Value) |> CacheSettings.Both
    | 'b', None -> "Expected etag to have a value" |> invalidOp
    | x, _ -> sprintf "Invalid Validator type: '%c'" x |> invalidOp

type ReValidationSettingsDto = 
    {
        MustRevalidateAtUtc: DateTime
        Validator: ValidatorDto
    }

let toReValidationSettingsDto (x: CacheSettings.ReValidationSettings) =
    {
        MustRevalidateAtUtc = x.MustRevalidateAtUtc
        Validator = toValidatorDto x.Validator
    }

let fromReValidationSettingsDto (x: ReValidationSettingsDto) =
    {
        MustRevalidateAtUtc = x.MustRevalidateAtUtc
        Validator = fromValidatorDto x.Validator
    } : CacheSettings.ReValidationSettings

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

let toCacheSettingsDto (x: CacheSettings.Value) = 
    {
        SharedCache = x.SharedCache
        ExpirySettings = Option.map toReValidationSettingsDto x.ExpirySettings
    }

let fromCacheSettingsDto (x: CacheSettingsDto) = 
    {
        SharedCache = x.SharedCache
        ExpirySettings = Option.map fromReValidationSettingsDto x.ExpirySettings
    } : CacheSettings.Value

let private version = (typedefof<CacheValuesDto>).Assembly.GetName().Version
let toDto (x: CachedValues) =
    buildCachedResponse x.HttpResponse
    |> Infra.Async.map (fun resp ->
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