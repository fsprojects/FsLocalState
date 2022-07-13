// TODOs:
//  - Don't calc the whole tree when triggering Update
//  - first class task/async support (in gen)
//  - implement "for" in ChildBuilder
//  - hide all the crazy generic type signatures

module App

open System

open LocSta
open LocSta.Gen

open Browser
open Browser.Types

[<AutoOpen>]
module Application =
    type App(document: Document, appElement: Element, triggerUpdate: App -> Node) =
        member _.Document = document
        member this.Run() =
            let initialElement = triggerUpdate this
            appElement.appendChild initialElement |> ignore
        member this.TriggerUpdate() =
            printfn $"Trigger update"
            let element = triggerUpdate this
            // TODO: Sync returned element(s) with current
            ()
    let app = Gen (fun s (r: App) -> r,())

[<AutoOpen>]
module HelperAndExtensions =
    type NodeList with
        member this.elements = seq { for i in 0 .. this.length-1 do this.Item i }
    type Node with
        member this.clearChildren() = this.textContent <- "" // TODO: really?

[<AutoOpen>]
module Framework =

    type AppGen<'o,'s> = Gen<'o,'s,App>
    type RuntimeTypedAppGen<'o> = Type * AppGen<'o,obj>

    let inline boxGen (stateType: Type) (Gen g: AppGen<'o,'s>) : RuntimeTypedAppGen<'o> =
        // fable requires runtime-resolution and passing the stateType from callsite due to erasure
        let g = Gen <| fun s r ->
            let o,s = g (unbox s) r
            o, box s
        stateType, g

    // TODO: Add overloads for yield (string, int, etc.)
    type ChildrenBuilder<'o,'s>(run: RuntimeTypedAppGen<'o> list -> AppGen<'o,'s>) =
        member inline _.Yield<'o,'s1>(x: Gen<'o,'s1,App>) = [boxGen typeof<'s1> x]
        member inline _.YieldFrom<'o,'s1>(x: Gen<'o list,'s1,App>) = [boxGen typeof<'s1> x]
        member inline _.Delay([<InlineIfLambda>] f) = f ()
        member _.Combine(a, b) = List.append a b
        member _.Zero() = []
        member _.Run(children) = run children
        member inline _.For(sequence: seq<'a>, body: 'a -> RuntimeTypedAppGen<'o> list) : RuntimeTypedAppGen<'o> list =
            [ for x in sequence do yield! body x ]

    //type ViewBuilder() =
    //    inherit Gen.GenBuilder()
    type ViewBuilder() =
        member inline _.Bind(m, [<InlineIfLambda>] f) = Gen.bind m f
        member _.Yield(x: AppGen<'o,'s>) = x
        member inline _.Delay([<InlineIfLambda>] f) = f ()
        member inline _.Combine(a, b) : Gen<_,_,_> =
            printfn "COMBINE"
            loop {
                let! a = a
                let! b = b
                return [a;b]
            }
        member _.Zero() : AppGen<unit list, unit> = Gen.ofValue []
    let pview = ViewBuilder()


[<AutoOpen>]
module HtmlElementsApi =

    let inline elem name attributes children =
        let syncAttributes (elem: Node) =
            do for aname,avalue in attributes do
                let elemAttr = elem.attributes.getNamedItem aname
                if elemAttr.value <> avalue then
                    elemAttr.value <- avalue
        let syncChildren (elem: Node) = Gen <| fun s r ->
            let s = s |> Option.defaultWith (fun () -> ResizeArray())
            let newState = seq {
                for childType, (Gen childGen) in children do
                    let stateIdx = s |> Seq.tryFindIndex (fun (typ,_) -> typ = childType)
                    let newChildState =
                        match stateIdx with
                        | Some idx ->
                            let childState = s[idx]
                            do s.RemoveAt(idx)
                            childGen (childState |> snd |> Some) r |> snd
                        | None ->
                            let o,s = childGen None r
                            do elem.appendChild o |> ignore
                            s
                    yield childType,newChildState
            }
            (), ResizeArray newState
        loop {
            let! app = app
            let! elem = preserve (fun () -> app.Document.createElement name :> Node)
            printfn $"Eval: {name} ({elem.GetHashCode()})"
            do syncAttributes elem
            do! syncChildren elem
            return elem
        }

    let text text =
        loop {
            let! app = app
            let! elem = preserve (fun () -> app.Document.createTextNode text)
            do if elem.textContent <> text then
                elem.textContent <- text
            return elem :> Node
        }

    let div attributes = ChildrenBuilder(elem "div" attributes)

    let p attributes = ChildrenBuilder(elem "p" attributes)

    let button attributes click =
        ChildrenBuilder(fun children -> loop {
            let! app = app
            // TODO: Optimize the map afterwards; that's not necessary
            let! button =
                elem "button" attributes children
                |> Gen.map (fun x -> x :?> HTMLButtonElement)
            button.onclick <- fun _ ->
                printfn "-----CLICK"
                click ()
                app.TriggerUpdate()
            return button :> Node // TODO: It's crap that we have to cast everything to "Node"
        })

    let nothing = text ""


let comp =
    pview {
        let! count, setCount = Gen.ofMutable 0
        div [] {
            div []  {
                text $"BEGIN for ..."
                for x in 0..3 do
                    text $"count = {count}"
                    button [] (fun () -> setCount (count + 1)) { text "..." }
                    text $"    (another x = {x})"
                    text $"    (another x = {x})"
                text $"END for ..."
            }
        }
        text "xxxx"
    }


//let view() =
//    pview {
//        div [] {
//            comp
//            div [] {
//                text "Hurz"
//                comp
//            }
//        }
//    }
    

//do
//    App(
//        document,
//        document.querySelector("#app"),
//        view() |> Gen.toEvaluable
//    ).Run()
