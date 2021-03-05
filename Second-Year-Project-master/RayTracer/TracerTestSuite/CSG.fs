namespace TracerTestSuite

open Tracer.API
open System
open System.Drawing

module CSG =
 
  let mkColourTexture c r = mkMatTexture (mkMaterial (fromColor c) r)
  let mkUnitBox t = mkBox (mkPoint -1.0 -1.0 -1.0) (mkPoint 1.0 1.0 1.0) t t t t t t
  let mkUnitCylinder t = mkSolidCylinder (mkPoint 0.0 0.0 0.0) 1.0 2.0 t t t
  let l1 () = mkLight (mkPoint 4.0 0.0 4.0) (fromColor Color.White) 1.0 in
  let l2 () = mkLight (mkPoint -4.0 0.0 4.0) (fromColor Color.White) 1.0 in
  let l3 () = mkLight (mkPoint 0.0 0.0 0.0) (fromColor Color.White) 1.0 in
  let l4 () = mkLight (mkPoint 2.0 4.0 4.0) (fromColor Color.White) 1.0 in
  let l5 () = mkLight (mkPoint 0.0 -4.0 0.0) (fromColor Color.White) 1.0 in
  let ambientLight () = mkAmbientLight (fromColor Color.White) 0.2 in
  let camera = mkCamera (mkPoint 16.0 16.0 16.0) (mkPoint 0.0 0.0 0.0) (mkVector -1.0 1.0 -1.0) 8.0 1.4 1.4 500 500 in
  let camera2 = mkCamera (mkPoint 0.0 0.0 4.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 2.0 2.0 2.0 500 500 in

  let cube () = mkUnitBox (mkMatTexture (mkMaterial (fromColor Color.Red) 0.0))
  let sphere () = mkSphere (mkPoint 0.0 0.0 0.0) 1.3 (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0))
  let sphere1 () = mkSphere (mkPoint 0.5 0.0 0.0) 1.0 (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0))
  let sphere2 () = mkSphere (mkPoint -0.5 0.0 0.0) 1.0 (mkMatTexture (mkMaterial (fromColor Color.Red) 0.0))
  let sphere3 () = mkSphere (mkPoint -0.5 0.0 0.0) 0.2 (mkMatTexture (mkMaterial (fromColor Color.Yellow) 0.0))

  let cross () =
    let cy = transform (mkUnitCylinder (mkColourTexture Color.Yellow 0.0)) (scale 0.7 1.5 0.7)  in
    let cx = transform cy (rotateX (Util.degrees_to_radians 90.0)) in
    let cz = transform cy (rotateZ (Util.degrees_to_radians 90.0)) in 
      union cy (union cz cx)

  let renderUnion () =
    { scene = mkScene [union (sphere1 ()) (sphere2 ())] [l1 (); l2 ()] (ambientLight ()) 0;
      camera = camera2 }

  let renderUnion2 () =
    { camera = mkCamera (mkPoint 0.9 0.0 0.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 0.4 0.9 0.9 500 500;
      scene = mkScene [union (sphere1 ()) (sphere2 ()); sphere3 ()] [l3 ()] (ambientLight ()) 0 }

  let renderUnion3 () =
    { camera = mkCamera (mkPoint 0.9 0.0 0.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 0.4 0.9 0.9 500 500;
      scene = mkScene [group (sphere1 ()) (sphere2 ()); sphere3 ()] [l3 ()] (ambientLight ()) 0 }
    
  let renderIntersection () =
    let cube = mkUnitBox (mkMatTexture (mkMaterial (fromColor Color.Red) 0.0)) in
    { scene = mkScene [intersection (sphere1 ()) (sphere2 ())] [l1 (); l2 ()] (ambientLight ()) 0 ;
      camera = camera2}
    
  let renderSubtraction () =
    let cube = mkUnitBox (mkMatTexture (mkMaterial (fromColor Color.Red) 0.0)) in
    { scene = mkScene [subtraction (sphere2 ()) (sphere1 ())] [l1 (); l2 ()] (ambientLight ()) 0 ;
      camera = camera}

  let renderCross () =
    { scene = mkScene [cross ()] [l1 (); l2 ()] (ambientLight ()) 0;
      camera = camera}

  let renderIntersection2 () =
    { scene = mkScene [intersection (cube ()) (sphere ())] [l1 (); l2 ()] (ambientLight ()) 0
      camera = camera}

  let renderLantern () =
    { scene = mkScene [subtraction (intersection (cube ()) (sphere())) (cross ())] [l1 (); l2 (); l3 ()] (ambientLight ()) 0
      camera = camera}

  let renderLantern2 () =
    let sphere2 = mkSphere (mkPoint 1.3 1.3 1.3) 1.4 (mkMatTexture (mkMaterial (fromColor Color.AntiqueWhite) 0.0))
    let sphere3 = mkSphere (mkPoint -1.3 -1.3 -1.3) 1.4 (mkMatTexture (mkMaterial (fromColor Color.AntiqueWhite) 0.0))

    { scene = mkScene [subtraction (subtraction (subtraction (intersection (cube ()) (sphere ())) (cross ())) sphere2) sphere3] [l1 (); l2 (); l3 ()] (ambientLight ()) 0
      camera = camera}

  let renderUnionTorus (R : float) (r : float) () =
    let s1 = mkShape (mkImplicit ("(((x^2 + y^2)_2 - " + (string R) + ")^2 + z^2)_2 - " + (string r))) (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0))
    let s2 = mkShape (mkImplicit ("(((x^2 + y^2)_2 - " + (string (R - (3.0 * r))) + ")^2 + z^2)_2 - " + (string r))) (mkMatTexture (mkMaterial (fromColor Color.Red) 0.0))
    let s3 = union (transform s2 (translate 0.0 (3.0 * r) 0.0)) s1
    { camera = mkCamera (mkPoint 0.0 0.0 4.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 2.0 4.0 4.0 500 500
      scene = mkScene [s3] [l4 ()] (ambientLight ()) 0}

  let renderIntersectionTorus (R : float) (r : float) () =
    let s1 = mkShape (mkImplicit ("(((x^2 + y^2)_2 - " + (string R) + ")^2 + z^2)_2 - " + (string r))) (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0))
    let s2 = mkShape (mkImplicit ("(((x^2 + y^2)_2 - " + (string (R - (3.0 * r))) + ")^2 + z^2)_2 - " + (string r))) (mkMatTexture (mkMaterial (fromColor Color.Red) 0.0))
    let s3 = intersection (transform s2 (translate 0.0 (3.0 * r) 0.0)) s1
    { camera = mkCamera (mkPoint 0.0 0.0 4.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 2.0 4.0 4.0 500 500
      scene = mkScene [s3] [l4 ()] (ambientLight ()) 0}

  let renderSubtractionTorus (R : float) (r : float) () =
    let s1 = mkShape (mkImplicit ("(((x^2 + y^2)_2 - " + (string R) + ")^2 + z^2)_2 - " + (string r))) (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0))
    let s2 = mkShape (mkImplicit ("(((x^2 + y^2)_2 - " + (string (R - (3.0 * r))) + ")^2 + z^2)_2 - " + (string r))) (mkMatTexture (mkMaterial (fromColor Color.Red) 0.0))
    let s3 = subtraction s1 (transform s2 (translate 0.0 (3.0 * r) 0.0))
    { camera = mkCamera (mkPoint 0.0 0.0 4.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 2.0 4.0 4.0 500 500
      scene = mkScene [s3] [l4 ()] (ambientLight ()) 0}

  let renderUnionTorus2 (R : float) (r : float) () =
    let s1 = mkShape (mkImplicit ("(((x^2 + y^2)_2 - " + (string R) + ")^2 + z^2)_2 - " + (string r))) (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0))
    let s2 = mkShape (mkImplicit ("(((x^2 + y^2)_2 - " + (string (R - (3.0 * r))) + ")^2 + z^2)_2 - " + (string r))) (mkMatTexture (mkMaterial (fromColor Color.Red) 0.0))
    let s3 = union (transform s2 (translate 0.0 (3.0 * r) 0.0)) s1
    { camera = mkCamera (mkPoint 0.0 -4.0 0.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 0.0 1.0) 2.0 4.0 4.0 500 500
      scene = mkScene [s3] [l5 ()] (ambientLight ()) 0}

  let renderIntersectionTorus2 (R : float) (r : float) () =
    let s1 = mkShape (mkImplicit ("(((x^2 + y^2)_2 - " + (string R) + ")^2 + z^2)_2 - " + (string r))) (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0))
    let s2 = mkShape (mkImplicit ("(((x^2 + y^2)_2 - " + (string (R - (3.0 * r))) + ")^2 + z^2)_2 - " + (string r))) (mkMatTexture (mkMaterial (fromColor Color.Red) 0.0))
    let s3 = intersection (transform s2 (translate 0.0 (3.0 * r) 0.0)) s1
    { camera = mkCamera (mkPoint 0.0 -2.0 0.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 0.0 1.0) 2.0 4.0 4.0 500 500
      scene = mkScene [s3] [l5 ()] (ambientLight ()) 0}

  let render =
    List.map (Util.mkTarget "csg")
      [(renderUnion, "union");
       (renderUnion2, "union2");
       (renderUnion3, "union3");
       (renderIntersection, "intersection");
       (renderSubtraction, "subtraction");
       (renderCross, "cross");
       (renderIntersection2, "intersection2");
       (renderLantern, "lantern");
       (renderLantern2, "lantern2");
       (renderUnionTorus 3.0 0.5, "unionTorus");
       (renderIntersectionTorus 3.0 0.5, "intersectTorus");
       (renderSubtractionTorus 3.0 0.5, "subtractTorus");
       (renderUnionTorus2 3.0 0.5, "unionTorus2");
       (renderIntersectionTorus2 3.0 0.5, "intersectTorus2")]