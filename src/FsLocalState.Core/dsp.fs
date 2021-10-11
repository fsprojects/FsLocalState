﻿module FsLocalState.Dsp.Gen

open FsLocalState
open System

module Const =
    let pi = Math.PI
    let pi2 = 2.0 * pi
    let sqrt2 = 1.4142135623730951

type Env =
    { samplePos: int
      sampleRate: int }

let toSeconds env = (double env.samplePos) / (double env.sampleRate)

module Envelopes =
    
    // we need the "releasing" phase to prevent hearable cracks
    type FollowerMode =
        | Releasing of int
        | Following

    /// An Envelope follower (tc: [0.0 .. 1.0])
    let follow timeConstant release (input: float) =
        (0.0, Following)
        => fun state -> gen {
            let lastValue, lastMode = state
            let lastMode' = if release then Releasing 1000 else lastMode
                
            let target,newMode =
                match lastMode' with
                | Following -> input, Following
                | Releasing remaining ->
                    let x = remaining - 1
                    (0.0, if x = 0 then Following else Releasing x)

            let diff = lastValue - target
            let out = lastValue - diff * timeConstant
                
            return out, (out, newMode)
        }

    /// An Attack-Release envelope (a, r: [0.0 .. 1.0])
    let ar attack release trigger resetTrigger =
        let target, tc =
            if trigger then 1.0, attack
            else 0.0, release
        follow tc resetTrigger target 


module Filter =

    type BiQuadCoeffs =
        { a0: float
          a1: float
          a2: float
          b1: float
          b2: float
          z1: float
          z2: float } with
          static member Zero = Unchecked.defaultof<BiQuadCoeffs>

    type BiQuadParams =
        { q: float
          frq: float
          gain: float }

    let private biQuadCoeffsZero =
        { a0 = 0.0
          a1 = 0.0
          a2 = 0.0
          b1 = 0.0
          b2 = 0.0
          z1 = 0.0
          z2 = 0.0 }

    (*
        These implementations are based on http://www.earlevel.com/main/2011/01/02/biquad-formulas/
        and on https://raw.githubusercontent.com/filoe/cscore/master/CSCore/DSP
    *)
    
    let private biQuadBase (env: Env) (filterParams: BiQuadParams) (calcCoeffs: Env -> BiQuadCoeffs) input =
        // seed: if we are run the first time, use default values for lastParams+lastCoeffs
        (filterParams, BiQuadCoeffs.Zero)
        => fun (lastParams, lastCoeffs) -> gen {
            // calc the coeffs new if filter params have changed
            let coeffs =
                match lastParams = filterParams with
                | true -> lastCoeffs
                | false -> calcCoeffs env

            let o = input * coeffs.a0 + coeffs.z1
            let z1 = input * coeffs.a1 + coeffs.z2 - coeffs.b1 * o
            let z2 = input * coeffs.a2 - coeffs.b2 * o
            let newCoeffs = { coeffs with z1 = z1;  z2 = z2 }
            return o, (filterParams, newCoeffs)
        }


    let lowPassDef =
        { frq = 1000.0
          q = 1.0
          gain = 0.0 }

    let lowPass (env: Env) (p: BiQuadParams) input =
        let calcCoeffs (env: Env) =
            let k = Math.Tan(Const.pi * p.frq / float env.sampleRate)
            let norm = 1.0 / (1.0 + k / p.q + k * k)
            let a0 = k * k * norm
            let a1 = 2.0 * a0
            let a2 = a0
            let b1 = 2.0 * (k * k - 1.0) * norm
            let b2 = (1.0 - k / p.q + k * k) * norm
            { biQuadCoeffsZero with
                  a0 = a0
                  a1 = a1
                  a2 = a2
                  b1 = b1
                  b2 = b2 }
        biQuadBase env p calcCoeffs input


    let bandPassDef =
        { frq = 1000.0
          q = 1.0
          gain = 0.0 }

    let bandPass (env: Env) (p: BiQuadParams) input =
        let calcCoeffs (env: Env) =
            let k = Math.Tan(Const.pi * p.frq / float env.sampleRate)
            let norm = 1.0 / (1.0 + k / p.q + k * k)
            let a0 = k / p.q * norm
            let a1 = 0.0
            let a2 = -a0
            let b1 = 2.0 * (k * k - 1.0) * norm
            let b2 = (1.0 - k / p.q + k * k) * norm
            { biQuadCoeffsZero with
                  a0 = a0
                  a1 = a1
                  a2 = a2
                  b1 = b1
                  b2 = b2 }
        biQuadBase env p calcCoeffs input


    let highShelfDef =
        { frq = 1000.0
          q = 1.0
          gain = 0.0 }

    let highShelf (env: Env) (p: BiQuadParams) input =
        let calcCoeffs (env: Env) =
            let k = Math.Tan(Const.pi * p.frq / float env.sampleRate)
            let v = Math.Pow(10.0, Math.Abs(p.gain) / 20.0)
            match p.gain >= 0.0 with
            | true ->
                // boost
                let norm = 1.0 / (1.0 + Const.sqrt2 * k + k * k)
                { biQuadCoeffsZero with
                      a0 = (v + Math.Sqrt(2.0 * v) * k + k * k) * norm
                      a1 = 2.0 * (k * k - v) * norm
                      a2 = (v - Math.Sqrt(2.0 * v) * k + k * k) * norm
                      b1 = 2.0 * (k * k - 1.0) * norm
                      b2 = (1.0 - Const.sqrt2 * k + k * k) * norm }
            | false ->
                // cut
                let norm = 1.0 / (v + Math.Sqrt(2.0 * v) * k + k * k)
                { biQuadCoeffsZero with
                      a0 = (1.0 + Const.sqrt2 * k + k * k) * norm
                      a1 = 2.0 * (k * k - 1.0) * norm
                      a2 = (1.0 - Const.sqrt2 * k + k * k) * norm
                      b1 = 2.0 * (k * k - v) * norm
                      b2 = (v - Math.Sqrt(2.0 * v) * k + k * k) * norm }
        biQuadBase env p calcCoeffs input


    let hishPassDef =
        { frq = 1000.0
          q = 1.0
          gain = 0.0 }

    let highPass (env: Env) (p: BiQuadParams) input =
        let calcCoeffs (env: Env) =
            let k = Math.Tan(Const.pi * p.frq / float env.sampleRate)
            let norm = 1.0 / (1.0 + k / p.q + k * k)
            let a0 = norm
            let a1 = -2.0 * a0
            let a2 = a0
            let b1 = 2.0 * (k * k - 1.0) * norm
            let b2 = (1.0 - k / p.q + k * k) * norm
            { biQuadCoeffsZero with
                  a0 = a0
                  a1 = a1
                  a2 = a2
                  b1 = b1
                  b2 = b2 }
        biQuadBase env p calcCoeffs input


    let lowShelfDef =
        { frq = 1000.0
          q = 1.0
          gain = 0.0 }

    let lowShelf (env: Env) (p: BiQuadParams) input =
        let calcCoeffs (env: Env) =
            let k = Math.Tan(Const.pi * p.frq / float env.sampleRate)
            let v = Math.Pow(10.0, Math.Abs(p.gain) / 20.0)
            match p.gain >= 0.0 with
            | true ->
                // boost
                let norm = 1.0 / (1.0 + Const.sqrt2 * k + k * k)
                { biQuadCoeffsZero with
                      a0 = (1.0 + Math.Sqrt(2.0 * v) * k + v * k * k) * norm
                      a1 = 2.0 * (v * k * k - 1.0) * norm
                      a2 = (1.0 - Math.Sqrt(2.0 * v) * k + v * k * k) * norm
                      b1 = 2.0 * (k * k - 1.0) * norm
                      b2 = (1.0 - Const.sqrt2 * k + k * k) * norm }
            | false ->
                // cut
                let norm = 1.0 / (1.0 + Math.Sqrt(2.0 * v) * k + v * k * k)
                { biQuadCoeffsZero with
                      a0 = (1.0 + Const.sqrt2 * k + k * k) * norm
                      a1 = 2.0 * (k * k - 1.0) * norm
                      a2 = (1.0 - Const.sqrt2 * k + k * k) * norm
                      b1 = 2.0 * (v * k * k - 1.0) * norm
                      b2 = (1.0 - Math.Sqrt(2.0 * v) * k + v * k * k) * norm }
        biQuadBase env p calcCoeffs input


    let notchDef =
        { frq = 1000.0
          q = 1.0
          gain = 0.0 }

    let notch (env: Env) (p: BiQuadParams)  input=
        let calcCoeffs (env: Env) =
            let k = Math.Tan(Const.pi * p.frq / float env.sampleRate)
            let norm = 1.0 / (1.0 + k / p.q + k * k)
            let a0 = (1.0 + k * k) * norm
            let a1 = 2.0 * (k * k - 1.0) * norm
            let a2 = a0
            let b1 = a1
            let b2 = (1.0 - k / p.q + k * k) * norm
            { biQuadCoeffsZero with
                  a0 = a0
                  a1 = a1
                  a2 = a2
                  b1 = b1
                  b2 = b2 }
        biQuadBase env p calcCoeffs input


    let peakDef =
        { frq = 1000.0
          q = 1.0
          gain = 0.0 }

    let peak (env: Env) (p: BiQuadParams) input =
        let calcCoeffs (env: Env) =
            let v = Math.Pow(10.0, Math.Abs(p.gain) / 20.0)
            let k = Math.Tan(Const.pi * p.frq / float env.sampleRate)
            let l = p.q * k + k * k
            match p.gain >= 0.0 with
            | true ->
                // boost
                let norm = 1.0 / (1.0 + 1.0 / l)
                let a1 = 2.0 * (k * k - 1.0) * norm
                { biQuadCoeffsZero with
                      a0 = (1.0 + v / l) * norm
                      a1 = a1
                      a2 = (1.0 - v / l) * norm
                      b1 = a1
                      b2 = (1.0 - 1.0 / l) * norm }
            | false ->
                // cut
                let norm = 1.0 / (1.0 + v / l)
                let a1 = 2.0 * (k * k - 1.0) * norm
                { biQuadCoeffsZero with
                      a0 = (1.0 + 1.0 / l) * norm
                      a1 = a1
                      a2 = (1.0 - 1.0 / l) * norm
                      b1 = a1
                      b2 = (1.0 - v / l) * norm }
        biQuadBase env p calcCoeffs input


// TODO: phase
module Osc =

    let noise() =
        Random()
        |> Gen.ofSeed  (fun random ->
            let v = random.NextDouble()
            Value (v, random)
        )

    let private osc (env: Env) (frq: float) f =
        0.0
        |> Gen.ofSeed (fun angle ->
            let newAngle = (angle + Const.pi2 * frq / (float env.sampleRate)) % Const.pi2
            Value (f newAngle, newAngle)
        )
        

    let sin (env: Env) (frq: float) = osc env frq Math.Sin
    
    let saw (env: Env) (frq: float) = osc env frq (fun angle -> 1.0 - (1.0 / Const.pi * angle))

    let tri (env: Env) (frq: float) =
        osc env frq (fun angle ->
            if angle < Const.pi then -1.0 + (2.0 / Const.pi) * angle
            else 3.0 - (2.0 / Const.pi) * angle)

    let square (env: Env) (frq: float) =
        osc env frq (fun angle ->
            if angle < Const.pi then 1.0
            else -1.0)
