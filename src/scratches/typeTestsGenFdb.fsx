
#r "../LocSta/bin/Debug/netstandard2.0/LocSta.dll"
open LocSta

let a =
    fdb {
        let! c1 = count 0 1
        let! c2 = count 0 1
        return Control.Emit (c1 + c2)
        //return Control.Feedback (c1 + c2, None)
    }
    |> Gen.toListn 5

