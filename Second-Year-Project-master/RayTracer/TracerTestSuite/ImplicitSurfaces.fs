namespace TracerTestSuite
open System.Drawing
open Tracer.API

module ImplicitSurfaces =

  let mkScene' s = 
    let light = mkLight (mkPoint 4.0 2.0 4.0) (fromColor Color.White) 0.5
    let light2 = mkLight (mkPoint -4.0 2.0 4.0) (fromColor Color.White) 0.5
    let lights = [light; light2]
    let ambientLight = mkAmbientLight (fromColor Color.White) 0.1 in
    mkScene [s] lights ambientLight 0

  let sphere1 (r : float) () =
    let s = mkShape (mkImplicit ("x^2 + y^2 + z^2 - " + (string (r * r)))) (mkMatTexture (mkMaterial (fromColor Color.Aqua) 0.0))
    { camera = mkCamera (mkPoint 0.0 0.0 4.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 2.0 4.0 4.0 500 500
      scene = mkScene' s }

  let sphere2 (r : float) () =
    let s = mkShape (mkImplicit ("(x^2 + y^2 + z^2)_2 - " + (string r))) (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0))
    { camera = mkCamera (mkPoint 0.0 0.0 4.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 2.0 4.0 4.0 500 500
      scene = mkScene' s}

  let planeX () =
    let s = mkShape (mkImplicit "x") (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0))
    { camera = mkCamera (mkPoint 1.0 0.0 4.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 2.0 16.0 16.0 500 500
      scene = mkScene' s}

  let planeY () =
    let s = mkShape (mkImplicit "y") (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0))
    { camera = mkCamera (mkPoint 0.0 1.0 4.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 2.0 4.0 4.0 500 500
      scene = mkScene' s}

  let planeZ () =
    let s = mkShape (mkImplicit "z") (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0))
    { camera = mkCamera (mkPoint 0.0 0.0 4.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 2.0 4.0 4.0 500 500
      scene = mkScene' s}

// A torus takes two arguments, the inner and the outer radius of the torus (r and R respectively). 
//  The outer radius is the distance from the center of the torus to the center of the tube
//  The inner radius is the radius of the tube
  let torus (R : float) (r : float) () =
    let s = mkShape (mkImplicit ("(((x^2 + y^2)_2 - " + (string R) + ")^2 + z^2)_2 - " + (string r))) (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0))
    { camera = mkCamera (mkPoint 0.0 0.0 4.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 1.0 0.0) 2.0 4.0 4.0 500 500
      scene = mkScene' s}

  let torus2 (R : float) (r : float) () =
    let rs1 = "(" + (string R) + "^2" + " + " + (string r) + "^2)"
    let rs2 = "(" + (string R) + "^2" + " - " + (string r) + "^2)"
    let sx = "x^4 + 2x^2*y^2 + 2x^2*z^2 - 2*" + rs1 + "*x^2"
    let sy = "y^4 + 2y^2*z^2 + 2*" + rs2 + "*y^2"
    let sz = "z^4 - 2*" + rs1 + "*z^2"
    let sc = rs2 + "^2"
    let eqn = sx + " + " + sy + " + " + sz + " + " + sc 
    let _ = printf "torus equation %s\n" eqn
    let s = mkShape (mkImplicit eqn) (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0))
    { camera = mkCamera (mkPoint 0.0 4.0 0.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 0.0 1.0) 2.0 4.0 4.0 500 500
      scene = mkScene' s}
      
  let testShape () =
    let is = mkImplicit "(x - 2)^2(x+2)^2 + (y - 2)^2(y+2)^2 + (z - 2)^2(z+2)^2 + 3(x^2*y^2 + x^2z^2 + y^2z^2) + 6x y z - 10(x^2 + y^2 + z^2) + 22"
    let s = mkShape is (mkMatTexture (mkMaterial (fromColor Color.Gold) 0.0))
    { camera = mkCamera (mkPoint 6.0 6.0 8.0) (mkPoint 0.0 0.0 0.0) (mkVector -1.0 -1.0 0.0) 2.0 4.0 4.0 500 500;
      scene = mkScene' s}
  
  let heart () =
    let is = mkImplicit "(x^2 + (4.0/9.0)*y^2 + z^2 - 1)^3 - x^2 * z^3 - (9.0/80.0)*y^2*z^3"
    let s = mkShape is (mkMatTexture (mkMaterial (fromColor Color.DarkRed) 0.0))
    { camera = mkCamera (mkPoint 0.0 3.0 1.0) (mkPoint 0.0 0.0 0.0) (mkVector 0.0 0.0 1.0) 2.0 4.0 4.0 500 500
      scene = mkScene' s}

  let factorial x = 
    if x = 0 then 1 else
    let rec fac_aux a acc =
      if a >= x then
        a * acc
      else
        fac_aux (a + 1) (a * acc)
    fac_aux 1 x

  let comb a b = 
    let x = float (factorial a) in
    let y = float (factorial b) in
    let z = float (factorial (a - b)) in
      x / (y * z)

  let rec strSum n f : string =
    if n = 0 then
      f 0
    else
      f n + " + " + (strSum (n - 1) f)

  let chmutov degree () =       
    let T x = strSum (degree / 2) (fun (k : int) -> (string (comb degree (2 * k))) + " * (" + x + "^2 + -1.0)^" + (string k) + " * " + x + "^" + (string (degree - (2 * k))))
    let is = mkImplicit (T "x" + " + " + T "y" + " + " + T "z")
    let s = mkShape is (mkMatTexture (mkMaterial (fromColor Color.Gold) 0.0))
    { camera = mkCamera (mkPoint 16.0 16.0 16.0) (mkPoint 0.0 -0.5 0.0) (mkVector -1.0 1.0 -1.0) 16.0 4.0 4.0 500 500
      scene = mkScene' s}
  
  let render =
    List.map (Util.mkTarget "implicitSurfaces")
      [(heart, "heart");
       (sphere1 1.0, "sphere1");
       (sphere2 1.0, "sphere2");
       (planeX, "planeX");
       (planeY, "planeY");
       (planeZ, "planeZ");
       (torus 1.5 0.5, "torus");
       (torus2 1.5 0.5, "torus2");
       (testShape, "testShape");
       (chmutov 2, "chmutov2");
       (chmutov 3, "chmutov3");
       (chmutov 4, "chmutov4");
       (chmutov 5, "chmutov5");
       (chmutov 6, "chmutov6")]
