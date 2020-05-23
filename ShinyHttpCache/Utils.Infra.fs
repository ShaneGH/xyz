
[<RequireQualifiedAccess>]
module ShinyHttpCace.Utils.Infra
    
    module AsyncOpt =

        let traverse input =
            async {
                match input with
                | Some x -> 
                    let! x1 = x
                    return Some x1
                | None -> return None 
            }
            
    module Option =

        let squash = function
            | Some x -> 
                match x with
                | Some y -> Some y
                | None -> None
            | None -> None
            
    module Async =

        let map f x =
            async {
                let! x1 = x
                return f x1
            }

        let bind f x =
            async {
                let! x1 = x
                return! f x1
            }

        let retn x = async { return x }

        let unit = retn ()