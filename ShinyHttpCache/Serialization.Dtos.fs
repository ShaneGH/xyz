module ShinyHttpCache.Serialization.Dtos
open ShinyHttpCache.FSharp.CachingHttpClient
open System
open ShinyHttpCache.Headers.CacheSettings
open ShinyHttpCache.Serialization.HttpResponseMessage
open ShinyHttpCache

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
        ETag: EntityTagDto
        ExpirationDateUtc: Nullable<DateTime>
    }

let toValidatorDto = function
    | ETag x ->
        {
            Type = 't'
            ETag = toEntityTagDto x
            ExpirationDateUtc = Nullable<DateTime>()
        }
    | ExpirationDateUtc x ->
        {
            Type = 'd'
            ETag = null :> obj :?> EntityTagDto
            ExpirationDateUtc = Nullable<DateTime> x
        }
    | Both (x, y) ->
        {
            Type = 'b'
            ETag = toEntityTagDto x
            ExpirationDateUtc = Nullable<DateTime> y
        }

let fromValidarorDto x =
    match x.Type with
    | 't' -> fromEntityTagDto x.ETag |> ETag 
    | 'd' -> x.ExpirationDateUtc.Value |> ExpirationDateUtc
    | 'b' -> (fromEntityTagDto x.ETag, x.ExpirationDateUtc.Value) |> Both
    | x -> sprintf "Invalid Validator type: '%c'" x |> invalidOp

type RevalidationSettingsDto = 
    {
        MustRevalidateAtUtc: DateTime
        Validator: ValidatorDto
    }

type ExpirySettingsDto =
    {
        Type: char
        Soft: RevalidationSettingsDto
        HardUtc: Nullable<DateTime>
    }

let toExpirySettingsDto = function
    | NoExpiryDate ->
        {
            Soft = null :> obj :?> RevalidationSettingsDto
            HardUtc = Nullable<DateTime>()
            Type = 'n'
        }
    | HardUtc x -> 
        {
            Soft = null :> obj :?> RevalidationSettingsDto
            HardUtc = Nullable<DateTime> x
            Type = 'h'
        }
    | Soft x -> 
        {
            Soft = 
                {
                    MustRevalidateAtUtc = x.MustRevalidateAtUtc
                    Validator = toValidatorDto x.Validator
                }
            HardUtc = Nullable<DateTime>()
            Type = 's'
        }

let fromExpirySettingsDto (x: ExpirySettingsDto) =
    match x.Type with
    | 'n' -> NoExpiryDate
    | 'h' -> x.HardUtc.Value |> HardUtc
    | 's' ->
        let settings =
            {
                MustRevalidateAtUtc = x.Soft.MustRevalidateAtUtc
                Validator = fromValidarorDto x.Soft.Validator
            }: RevalidationSettings
            
        Soft settings
    | x -> sprintf "Invalid Validator type: '%c'" x |> invalidOp

type CacheSettingsDto =
    {
        ExpirySettings: ExpirySettingsDto
        SharedCache: bool
    }

type CacheValuesDto =
    {
        ShinyHttpCacheVersion: Version
        HttpResponse: CachedResponse.CachedResponse
        CacheSettings: CacheSettingsDto
    }

let toCacheSettingsDto (x: Headers.CacheSettings.CacheSettings) = 
    {
        SharedCache = x.SharedCache
        ExpirySettings = toExpirySettingsDto x.ExpirySettings
    }

let fromCacheSettingsDto (x: CacheSettingsDto) = 
    {
        SharedCache = x.SharedCache
        ExpirySettings = fromExpirySettingsDto x.ExpirySettings
    } : Headers.CacheSettings.CacheSettings

let private version = (typedefof<CacheValuesDto>).Assembly.GetName().Version
let toDto (x: CachedValues) = 
    {
        ShinyHttpCacheVersion = version
        HttpResponse = x.HttpResponse
        CacheSettings = toCacheSettingsDto x.CacheSettings
    }

let fromDto (x: CacheValuesDto) = 
    {
        HttpResponse = x.HttpResponse
        CacheSettings = fromCacheSettingsDto x.CacheSettings
    } : CachedValues