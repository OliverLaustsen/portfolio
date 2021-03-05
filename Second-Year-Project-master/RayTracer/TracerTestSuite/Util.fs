namespace TracerTestSuite

open System.IO
open Tracer.API
open System

type Render = 
  { scene : scene;
    camera : camera}

type Target =
  { render : unit -> Render;
    group : string;
    name : string }

module Util =
  let mkTarget (group : string) (render : unit -> Render, name : string) : Target = 
    {render = render; group = group; name = name}



  let degrees_to_radians (d : float) = d * Math.PI / 180.0

  let private source_path = "../../.."
  let private result_path = source_path + "/result"
  let private timings = result_path + "/runtime.csv"

  let mutable private timings_wr = null

  let init () = 
    Directory.CreateDirectory result_path |> ignore
    timings_wr <- new StreamWriter(timings, false)
    timings_wr.WriteLine("test name, construction, rendering, total")


  let finalize () = timings_wr.Close()

  let render (renderIt : unit -> Render) : unit =
    let render = renderIt ()
    renderToScreen render.scene render.camera

  let renderTarget (toScreen : bool) (tgt : Target) : unit =
    try 
      let stopWatch = System.Diagnostics.Stopwatch.StartNew();
      let render = tgt.render();
      stopWatch.Stop();
      let timeConstruct = stopWatch.Elapsed.TotalMilliseconds / 1000.0
      if toScreen
      then renderToScreen render.scene render.camera
      else 
        let path = if tgt.group = "" then result_path else result_path + "/" + tgt.group
        Directory.CreateDirectory path |> ignore
        let stopWatch = System.Diagnostics.Stopwatch.StartNew()
        renderToFile render.scene render.camera (path + "/" + tgt.name + ".png")
        stopWatch.Stop();
        let timeRender = stopWatch.Elapsed.TotalMilliseconds / 1000.0
        timings_wr.WriteLine("{0}/{1}, {2}, {3}, {4}", tgt.group, tgt.name, timeConstruct, timeRender, timeConstruct+timeRender)
        timings_wr.Flush()
    with | e -> 
      printfn "rendering of %s/%s failed: %s" tgt.group tgt.name (e.ToString())
      if not toScreen then
        timings_wr.WriteLine("{0}/{1}, crashed, {2}", tgt.group, tgt.name, e.ToString())
        timings_wr.Flush()

  let renderGroups (toScreen : bool) (targets : Target list) (groups : string list) : unit =
    for group in groups do
      match List.filter (fun tgt -> tgt.group = group) targets with
      | [] -> failwith ("cannot find group " + group)
      | tgts -> List.iter (renderTarget toScreen) tgts

  let renderTests (toScreen : bool) (targets : Target list) (group : string) (tests : string list) : unit =
    match List.filter (fun tgt -> tgt.group = group) targets with
    | [] -> failwith ("cannot find group " + group)
    | tgts -> 
      for name in tests do
        match List.tryFind (fun tgt -> tgt.name = name) tgts with
        | None -> failwith ("cannot find test " + name + " in group " + group)
        | Some tgt -> renderTarget toScreen tgt