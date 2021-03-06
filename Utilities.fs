﻿//
// FSound - F# Sound Processing Library
// Copyright (c) 2015 by Albert Pang <albert.pang@me.com> 
// All rights reserved.
//
// This file is a part of FSound
//
// FSound is free software: you can redistribute it and/or modify
// it under the terms of the GNU General Public License as published by
// the Free Software Foundation, either version 3 of the License, or
// (at your option) any later version.
//
// FSound is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
// GNU General Public License for more details.
//
// You should have received a copy of the GNU General Public License
// along with this program.  If not, see <http://www.gnu.org/licenses/>.
//
namespace FSound

module Utilities =

  open MathNet.Numerics.IntegralTransforms
  open FSound.IO
  open FSound.Signal
  open FSound.Filter
  open FSound.Play

  /// <summary>Transpose a pitch by a semitone. For example, +12.0 semitones multiplies the pitch by 2.</summary>
  /// <param name="semitone">number of semitones to transpose</param>
  /// <returns>Returns a ratio to multiply a pitch by.
  ///          For +0.0 semitones, it would return 1.0.
  ///          For -12.0 semitones, it would return 0.5   </returns>
  ///
  let transpose semitone:float =
    2.0 ** (semitone / 12.0)

  /// <summary>Sequence multiple sound generators, playing one at a time, looping</summary>
  /// <param name="generators">list of generators of float->float</param>
  /// <param name="loopTime">The total length in seconds before loop begins again</param>
  /// <param name="t">current playback time in seconds.</param>
  /// <returns></returns>
  ///

  let sequencer (generators : (float -> float) array) (loopTime:float) (t:float) =
    let noteTime = loopTime / float(generators.Length)
    let timeIntoLoop = t % loopTime
    let noteSelect = int(timeIntoLoop/noteTime)
    let timeToLastNoteBegin = float(noteSelect)*noteTime
    let timeIntoNote = timeIntoLoop - timeToLastNoteBegin
    generators.[noteSelect] timeIntoNote


  /// <summary>Sequence multiple sound generators, playing one at a time for as long as specified in seconds, looping</summary>
  /// <param name="generators">list of generators of float* float->float</param>
  /// <param name="t">current playback time in seconds.</param>
  /// <returns></returns>
  ///
  let sequencerWeighted (timeGens:(float*(float->float))[]) (t:float) =
    let noteTimes = timeGens |> Array.map (fun (time,_) -> time)
    let gens = timeGens |> Array.map (fun (_,gen) -> gen)
    let cumTimes = noteTimes |> (Array.scan (+) 0.0)
    let totalTime = cumTimes |> Array.last
    let timeIntoLoop = t % totalTime
    let noteIndex = cumTimes |> Array.findIndexBack (fun cumTime -> cumTime <= timeIntoLoop)
    let currentNoteBeginTime = cumTimes.[noteIndex]
    let timeIntoCurrentNote = timeIntoLoop - currentNoteBeginTime
    (gens.[noteIndex] timeIntoCurrentNote)

  

  /// Combines array of pattern into a song length in seconds and a single wave generator
  let songToWaveGen song =
    let infiniteOf repeatedList = 
        Seq.initInfinite (fun _ -> repeatedList) 
            |> Seq.concat
    let tracksToWaveGen timeAndTracks=
      let (time, tracks) = timeAndTracks
      let waveGen =
        tracks
        |> List.map (fun (gen, n, pArr) -> infiniteOf (pArr) |> Seq.take n |> Seq.map (float >> transpose >> gen))
        |> List.map (fun gen -> sequencer (Array.ofSeq gen) time )
        |> sum
      (time, waveGen)

    (
      song |> Array.map (fun (time,_) -> time) |> Array.sum, //Total Song Time in seconds
      song |> Array.map tracksToWaveGen |> sequencerWeighted  // Wave Generator
    )
 


   


  ///
  /// <summary>Folding with an index</summary>
  /// <param name="f">function which takes a state, an integer which is the
  /// index of the element in the sequence, the element itself and returns a new
  /// state</param>
  /// <param name="acc">initial state</param>
  /// <param name="xs">list of elements to be folded</param>
  /// <returns></returns>
  ///
  let foldi f acc xs = 
    let rec foldi' f i acc xs =
      match xs with
      | [] -> acc
      | h::t -> foldi' f (i+1) (f acc i h) t
    foldi' f 0 acc xs

  ///
  /// <summary>Naive implementation of the discrete fourier transform. Use at
  /// your own peril - it does not perform well and only amplitude is calculated
  /// </summary>
  /// <param name="samples">list of samples</param>
  /// <returns>list of frequency component amplitudes</returns>
  ///
  let naiveDft samples =
 
    let dftComponent k s =
      let N = Seq.length s
      let w = 2.0*System.Math.PI*(float k)/(float N)
      foldi (fun (re,im) i x-> (re + x*cos(w*(float i)), 
                                im + x*sin(w*(float i)))) 
                                (0.0, 0.0) s
    
    Seq.mapi (fun i _ -> dftComponent i samples) samples

  ///
  /// <summary>Wrapper for the MathNet.Numerics (3.7.0) fourier transform.
  /// First convert the float samples to System.Numerics.Complex.  Then
  /// call MathNet.Numerics.IntegralTransforms.Fourier.Forward which modifies
  /// the input inline</summary>
  /// <param name="samples">sequence of real float samples</param>
  /// <returns>complex array</returns>
  ///
  let fft samples =

    let cmplxSamples = 
      samples 
      |> Seq.map (fun x -> System.Numerics.Complex(x, 0.0))
      |> Seq.toArray

    Fourier.Forward(cmplxSamples)
    cmplxSamples

  ///
  /// <summary>Returns magnitude of a complex number</summary>
  /// <param name="c">a complex number</param>
  /// <returns>Magnitude of the given complex number</returns>
  ///
  let magnitude (c:System.Numerics.Complex) = c.Magnitude

  ///
  /// <summary>Returns the seq of magnitudes of a seq of complex numbers
  /// </summary>
  /// <param name="cs">sequence of complex numbers</param>
  /// <returns>sequence of magnitudes of sequence of complex numbers</returns>
  ///
  let magnitudes (cs:seq<System.Numerics.Complex>) = Seq.map magnitude cs

  ///
  /// <summary>Returns the phase of a complex number</summary>
  /// <param name="c">a complex number</param>
  /// <returns>Phase of the given complex number</returns>
  ///
  let phase (c:System.Numerics.Complex) = c.Phase

  ///
  /// <summary>Returns the seq of phases of a seq of complex numbers
  /// </summary>
  /// <param name="cs">sequence of complex numbers</param>
  /// <returns>sequence of phases of sequence of complex numbers</returns>
  ///
  let phases (cs:seq<System.Numerics.Complex>) = Seq.map phase cs

  ///
  /// <summary>Returns the magnitude and phase of a complex number</summary>
  /// <param name="c">a complex number</param>
  /// <returns>A pair containing the magnitude and phase of the given complex
  /// number</returns>
  ///
  let toPolar (c:System.Numerics.Complex) = (magnitude c, phase c)
  
  ///
  /// <summary>Convenience function to generate a wav file with the supplied 
  /// wave function which is of compact disc parameters i.e. 44100Hz sampling 
  /// rate and 16-bit sample. Only one channel is created</summary>
  /// <param name="duration">number of seconds</param>
  /// <param name="filename">filename of the output wav file</param>
  /// <param name="waveform">the waveform function</param>
  ///
  let wavCd1 duration filename waveform =
    waveform
    |> generate 44100.0 duration
    |> floatTo16
    |> makeSoundFile 44100.0 1 16 true
    |> toWav filename

  ///
  /// <summary>Yet another convenience function to play a wave function for a
  /// given duration in seconds, just to save some typing</summary>
  /// <param name="sf">sampling frequency</param>
  /// <param name="duration">duration in number of seconds</param>
  /// <param name="waveFunc">the waveform function which takes a time t as
  /// argument and return a sample</param>
  /// <returns>unit</returns>
  ///
  let playWave sf duration waveFunc =
    waveFunc
    |> generate sf duration
    |> floatTo16
    |> makeSoundFile sf 1 16 true
    |> playSoundFile

  ///
  /// <summary>Implements a very crude model of the sound of waves by modulating
  /// white noise waveform with a LFO</summary>
  /// <param name="a">amplitude</param>
  /// <param name="f">LFO frequency</param>
  /// <param name="sf">sampling frequency</param>
  /// <param name="tau">duration of the samples to be generated</param>
  /// <returns>Sequence of samples</returns>
  ///
  let waveGenerator sf tau =
    // let delay = simpleDelay 1 0.0
    let comb = filter [1.0; 0.0; 0.0; 0.5**3.0] [0.0; 0.0; 0.0; 0.0; 0.9**5.0]
    let wf t = (whiteNoise 10000.0 t) * (lfo 0.05 0.0 0.8 t)
    wf >> comb
    |> generate sf tau

  ///
  /// <summary>Wind simulator</summary>
  /// <param name="a">amplitude</param>
  /// <returns>function returning the value of the sample at time t</returns>
  ///
  let windSimulator a =
    ((modulate (whiteNoise 20000.0) (lfo 0.05 0.0 0.8)) 
    >> smithAngell 44100.0 880.0 10.0)

  let makeDir path =
    let create = System.IO.Directory.CreateDirectory
    let getName = System.IO.Path.GetFileName
    let getDir = System.IO.Path.GetDirectoryName
    path |> getDir |> getName |> create |> ignore

  ///
  /// <summary>Save a generator to disk.</summary>
  /// <returns>unit</returns>
  ///
  let makeWavFileFromWaveformGen path sampleRate (lengthOfTime, waveformGen)=
    makeDir(path)
    waveformGen
    |> generate sampleRate lengthOfTime
    |> floatTo16
    |> makeSoundFile sampleRate 1 16 true
    |> toWav path