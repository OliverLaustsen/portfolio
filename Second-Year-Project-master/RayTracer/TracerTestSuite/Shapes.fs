namespace TracerTestSuite

open Tracer.API
open System
open System.Drawing
open Util

module Shapes =

  let renderSphere () =
    let light = mkLight (mkPoint 0.0 0.0 4.0) (fromColor Color.White) 1.0 in
    let ambientLight = mkAmbientLight (fromColor Color.White) 0.1 in
    let sphere = mkSphere (mkPoint 0.0 0.0 0.0) 1.0 (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0)) in
    { camera = mkCamera (mkPoint 0.0 0.0 4.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 1.0 2.0 2.0 500 500;
      scene = mkScene [sphere] [light] ambientLight 0}

  let renderReflectiveSpheres () =
    let light = mkLight (mkPoint 0.0 0.0 2.0) (mkColour 1. 1. 1.) 1.0
    let ambientLight = mkAmbientLight (mkColour 1. 1. 1.) 0.2
    let sphere = mkSphere (mkPoint -0.8 0.0 0.0) 0.7 (mkMatTexture (mkMaterial (mkColour 0. 0. 1.) 0.7))
    let sphere2 = mkSphere (mkPoint 0.8 0.0 0.0) 0.7 (mkMatTexture (mkMaterial (mkColour 1. 0. 0.) 0.7))
    { camera = mkCamera (mkPoint 0.0 0.0 2.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 1.0 2.0 2.0 500 500;
      scene = mkScene [sphere;sphere2] [light] ambientLight 4}

  let renderHollowCylinder () =
    let light = mkLight (mkPoint 2.0 3.0 4.0) (fromColor Color.White) 1.0 in
    let ambientLight = mkAmbientLight (fromColor Color.White) 0.1 in
    let cylinder = mkHollowCylinder (mkPoint 0.0 0.0 0.0) 2.0 1.0 (mkMatTexture (mkMaterial (fromColor Color.Yellow) 0.0)) in
    { camera = mkCamera (mkPoint 0.0 10.0 20.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 -0.5) 18.0 4.0 4.0 500 500;
      scene = mkScene [cylinder] [light] ambientLight 0}

  let renderSolidCylinder () =
    let light = mkLight (mkPoint 2.0 3.0 4.0) (fromColor Color.White) 1.0 in
    let ambientLight = mkAmbientLight (fromColor Color.White) 0.1 in
    let cylinder = 
      mkSolidCylinder (mkPoint 0.0 0.0 0.0) 2.0 1.0 (mkMatTexture (mkMaterial (fromColor Color.Yellow) 0.0))
        (mkMatTexture (mkMaterial (fromColor Color.Red) 0.0)) (mkMatTexture (mkMaterial (fromColor Color.Red) 0.0)) in
    { camera = mkCamera (mkPoint 0.0 10.0 20.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 -0.5) 18.0 4.0 4.0 500 500;
      scene = mkScene [cylinder] [light] ambientLight 0}
    
  let renderInsideSphere () =
    let light = mkLight (mkPoint 0.0 0.0 0.0) (fromColor Color.White) 1.0 in
    let ambientLight = mkAmbientLight (fromColor Color.White) 0.1 in
    let sphere = mkSphere (mkPoint 0.0 0.0 0.0) 1.0 (mkMatTexture (mkMaterial (fromColor Color.Red) 0.0)) in
    { camera = mkCamera (mkPoint 0.0 0.0 0.0) (mkPoint 0.0 0.0 4.0) (mkVector 0.0 1.0 0.0) 1.0 2.0 2.0 500 500;
     scene = mkScene [sphere] [light] ambientLight 0}

  let render : Target list = 
    List.map (mkTarget "shapes") 
      [(renderSphere,"sphere");
       (renderReflectiveSpheres,"reflectiveSpheres");
       (renderHollowCylinder, "hollowCylinder");
       (renderSolidCylinder, "solidCylinder");
       (renderInsideSphere, "insideSphere")]