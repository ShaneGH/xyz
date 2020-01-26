module ReaderMonad

[<RequireQualifiedAccess>]
module Reader =

    type Reader<'a, 'b> = Reader of ('a -> 'b)

    let retn x =
        let newAction _ = x
        newAction |> Reader

    let run api (Reader action) =
        action api

    let map f action =
        let newAction api = 
            let x = run api action
            f x

        Reader newAction

    let apply fAction xAction =
        let newAction api =
            let f = run api fAction
            let x = run api xAction

            f x
        Reader newAction

    let bind f xAction =
        let newAction api =
            let x = run api xAction
            run api (f x)

        Reader newAction

[<RequireQualifiedAccess>]
module ReaderAsync =

    let asAsync x = async { return x }

    let retn x =
        let newAction _ = asAsync x
        newAction |> Reader.Reader

    let map f action =
        let newAction api = 
            async {
                let! x = Reader.run api action
                return f x
            }

        Reader.Reader newAction

    let bind f xAction =
        let newAction api =
            async {
                let! x = Reader.run api xAction
                return! Reader.run api (f x)
            }

        Reader.Reader newAction

[<RequireQualifiedAccess>]
module ReaderAsyncOption =

    let asAsync x = async { return x }

    let retn x =
        let newAction _ = x |> Some |> asAsync
        newAction |> Reader.Reader

    let map f action =
        let newAction api = 
            async {
                let! x = Reader.run api action
                return Option.map f x
            }

        Reader.Reader newAction

    let bind f xAction =

        let f = 
            Option.map f
            >> Option.defaultValue (ReaderAsync.retn None)

        let newAction api =
            async {
                let! x = Reader.run api xAction
                return! Reader.run api (f x)
            }

        Reader.Reader newAction

    let defaultWith f =
        Option.map ReaderAsync.retn
        >> Option.defaultWith f
        |> ReaderAsync.bind