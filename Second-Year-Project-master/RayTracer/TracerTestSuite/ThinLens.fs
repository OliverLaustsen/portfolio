namespace TracerTestSuite

open System
open System.Drawing
open Tracer.API

module ThinLens =

  let renderPlaneFocal fpDistance () =
    let white = mkMaterial (fromColor Color.White) 0.0
    let black = mkMaterial (fromColor Color.Black) 0.0
    let red = mkMaterial (fromColor Color.Red) 0.0
    let blue = mkMaterial (fromColor Color.Blue) 0.0
    let gold = mkMaterial (fromColor Color.Gold) 0.0

    let checker c1 c2 x y =
        let abs' s f = if f < 0.0 then 1.0 - (f * s) else f * s
        if (int (abs' 10.0 x) + int (abs' 15.0 y)) % 2 = 0
        then c1
        else c2
    
    let reflect = mkMaterial (fromColor Color.White) 0.8
    let notreflect = mkMaterial (fromColor Color.Green) 0.0
    let checker2 x y =
        let abs' f = if f < 0.0 then 1.0 - (f*2.0) else f * 2.0
        if (int (abs' x) + int (abs' y)) % 2 = 0
        then reflect
        else notreflect
    let l1 = mkLight (mkPoint 0.0 1.0 4.0) (fromColor Color.White) 0.9 in
    let l2 = mkLight (mkPoint 0.0 1.0 0.0) (fromColor Color.White) 0.9 in
    let l3 = mkLight (mkPoint 0.0 1.0 -4.0) (fromColor Color.White) 0.9 in
    let ambientLight = mkAmbientLight (fromColor Color.White) 0.1 in

    let cbox x z m1 m2 = 
      let t1 = mkTexture (checker m1 m2) in
      let t2 = mkMatTexture m2 in
        mkBox (mkPoint x 0.0 z) (mkPoint (x + 0.4) 0.8 (z + 0.4)) t2 t1 t2 t2 t2 t2
     

    let b1 = cbox -0.2 -0.2 white red
    let b2 = cbox -1.2 -2.2 white blue
    let b3 = cbox 1.2 1.8 white gold
    let p = transform (mkPlane (mkTexture checker2)) (rotateX (System.Math.PI/2.0))
    { camera = mkThinLensCamera (mkPoint 0.0 1.0 -8.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 4.0 2.0 2.0 1000 1000 0.05 fpDistance;
      scene = mkScene [p; b1; b2; b3] [l1; l2; l3] ambientLight 3}

  let renderPlaneClose = renderPlaneFocal 5.8
  let renderPlaneCenter = renderPlaneFocal 7.8
  let renderPlaneFar = renderPlaneFocal 9.8

  let render : Target list = 
    List.map (Util.mkTarget "thinLens")
      [(renderPlaneClose, "renderPlaneClose"); 
       (renderPlaneCenter, "renderPlaneCenter"); 
       (renderPlaneFar, "renderPlaneFar")]


