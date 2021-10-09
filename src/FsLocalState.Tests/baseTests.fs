
#if INTERACTIVE
#r "./bin/Debug/netcoreapp3.1/FsLocalState.dll"
#r "nuget: Xunit"
#r "nuget: FsCheck.Xunit"
#load "./testHelper.fs"
#else
module BaseTests
#endif

open FsLocalState

open Xunit

[<AutoOpen>]
module General =
    /// The type of the reader state fot the tests - here, unit.
    type Env = unit

    /// An 1-incremental counter with min (seed) and max, written in "feedback" notation.
    /// When max is reached, counting begins with min again.
    let counterGen exclMin inclMax =
        exclMin => fun state -> gen {
            let newValue = (if state = inclMax then exclMin else state) + 1
            return newValue, newValue
        }

    /// An accumulator function summing up incoming values, starting with the given seed.
    let accuFx value seed =
        seed => fun state ->
            gen {
                let newValue = state + value
                return newValue, newValue
            }

module CounterTest =

    let counterMin = 0
    let counterMax = 20
    let accuSeed = 0
    let sampleCount = 1000

    let counted =
        gen {
            let! i = counterGen counterMin counterMax
            return i
        }
        |> Gen.toList sampleCount

    [<Fact>]
    let ``Sample count`` () =
        Assert.Equal(sampleCount, counted.Length)

    [<Fact>]
    let ``Min is exclusive`` () =
        Assert.Equal(counterMin + 1, counted |> List.min)

    [<Fact>]
    let ``Max is inclusive`` () =
        Assert.Equal(counterMax, counted |> List.max)

    [<Fact>]
    let ``Incremental and reset`` () =

        let lastAndCurrent (l: 'a list) =
            l.Tail
            |> Seq.zip l
            |> Seq.toList

        counted
        |> lastAndCurrent
        |> List.map (fun (last, current) -> current = last + 1 || current = counterMin + 1 && last = counterMax)
        |> List.forall (fun x -> x = true)
        |> Assert.True

module CounterAndAccu =

    let counterMin = 0
    let counterMax = 20
    let accuSeed = 0
    let sampleCount = 1000

    let accumulated =
        gen {
            let! i = counterGen counterMin counterMax
            let! acc = accuFx i accuSeed
            return acc
        }
        |> Gen.toList sampleCount

    [<Fact>]
    let ``Sample count`` () =
        Assert.Equal(sampleCount, accumulated.Length)

    [<Fact>]
    let ``Gradient between counter min/max`` () =

        let lastAndCurrent (l: 'a list) =
            l.Tail
            |> Seq.zip l
            |> Seq.toList

        accumulated
        |> lastAndCurrent
        |> List.map (fun (last, current) -> current >= last + counterMin + 1 && current <= last + counterMax)
        |> List.forall (fun x -> x = true)
        |> Assert.True

module DiscardingNone =
    
    [<Fact>]
    let filterByNone () =
        let onlyEvenValues =
            fun input -> gen {
                if input % 2 = 0 then
                    return input
            }
            |> Gen.toSeqFx
                            
        let res = [ 1; 2; 3; 4; 5; 6 ] |> onlyEvenValues |> Seq.toList
        let isTrue = res = [ 2; 4; 6 ]
        Assert.True(isTrue)
