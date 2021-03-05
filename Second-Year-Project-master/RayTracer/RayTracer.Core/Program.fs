namespace RayTracer.Core
open RayTracer.Entities
open RayTracer.Expressions
open RayTracer.Helpers
open RayTracer.TriangleParser
open Point
open Vector
open RayTracer.Shapes
open Camera
open Colour
open Scene
open Render
open Light
open AmbientLight
open TriangleMesh
open TriangleParser
open Material
open ExprParse
open ExprToPoly
open Transformation
open EquationHelpers
open BoundingBox
open Texture
open System.Drawing
open BaseShape
open Implicit

module program = 
    open RayTracer.Shapes.Shape

    [<EntryPoint>]
    let main argv = 
        //Camera
        let camPos = mkPoint 0.0 0.0 4.0
        let camLook = mkPoint 0.0 0.0 0.0
        let upVector = mkVector 0.0 1.0 0.0
        let camera = mkCamera camPos camLook upVector 2.0 4.0 4.0 200 200

        //Colours
        let red = mkColour 1.0 0.0 0.0 
        let green = mkColour 0.0 1.0 0.0 
        let blue = mkColour 0.0 0.0 1.0
        let white = mkColour 1.0 1.0 1.0
        let grey = mkColour 0.26 0.26 0.26

        //Materials
        let redMaterial = mkMatTexture (mkMaterial red 0.0)
        let greenMaterial = mkMatTexture (mkMaterial green 0.0)
        let blueMaterial = mkMatTexture (mkMaterial blue 0.0)
        let whiteMaterial = mkMatTexture (mkMaterial white 0.5)
        let greyMaterial = mkMatTexture (mkMaterial grey 0.0)
        let yellowMaterial = mkMatTexture (mkMaterial (Colour.fromColor Color.Yellow) 0.0)

        //Textures
        let texture = mkTextureFromFile (fun x y -> (x,1.0-y)) "../../../textures/earth.jpg"

        let textureCube1 = mkTextureFromFile (fun x y -> (x,1.0-y)) "../../../textures/cube/1.jpg"
        let textureCube2 = mkTextureFromFile (fun x y -> (x,1.0-y)) "../../../textures/cube/2.jpg"
        let textureCube3 = mkTextureFromFile (fun x y -> (x,1.0-y)) "../../../textures/cube/3.jpg"
        let textureCube4 = mkTextureFromFile (fun x y -> (x,1.0-y)) "../../../textures/cube/4.jpg"
        let textureCube5 = mkTextureFromFile (fun x y -> (x,1.0-y)) "../../../textures/cube/5.jpg"
        let textureCube6 = mkTextureFromFile (fun x y -> (x,1.0-y)) "../../../textures/cube/6.jpg"

        //Lights
        let light1 = mkLight(mkPoint 4.0 2.0 4.0) white 0.5
        let light2 = mkLight(mkPoint -4.0 2.0 4.0) white 0.5
        let light3 = mkLight(mkPoint 1.0 1.0 -1.2) white 1.0
        let light4 = mkLight(mkPoint 0.0 0.0 1.1) white 1.0
        let light5 = mkLight(mkPoint 0.0 0.0 1.6) white 1.0
        let light6 = mkLight(mkPoint 0.0 0.0 4.0) white 1.0
        let ambilight = mkAmbientLight white 0.1

        //Points
        let zeroPoint = mkPoint 0.0 0.0 0.0

        //PLY
        let porsche = "../../../ply/porsche.ply"
      //  let horse = @"C:\Users\Defalt\Desktop\horse.ply"
//        let baseCar = mkTriangleMesh porsche true
//        let redCar = mkShape baseCar redMaterial
        //let greenCar = mkShape baseCar greenMaterial
       // let parsed = Option.get (mkTriangleMesh ico red)

        //Spheres
        let sphere1 = mkSphere blueMaterial 1.0 (mkPoint 0.0 0.0 0.0)
        let sphere2 = mkSphere greenMaterial 1.0 (mkPoint 0.0 0.0 0.0)
        let sphere3 = mkSphere redMaterial 0.3 (mkPoint 0.0 0.0 0.0)
        let earth = mkSphere texture 1.5 (mkPoint 0.0 0.0 0.0)

        //Rectangles
        let rectangle1 = mkRectangle greenMaterial (mkPoint -1.0 -1.0 0.0) (mkPoint -1.0 1.0 0.0) (mkPoint 1.0 -1.0 0.0)
        let rectangle2 = mkRectangle whiteMaterial (mkPoint -1.0 -1.0 0.0) (mkPoint -1.0 1.0 0.0) (mkPoint 1.0 -1.0 0.0)
        let rectangle3 = mkRectangle redMaterial (mkPoint -1.0 -1.0 0.0) (mkPoint -1.0 1.0 0.0) (mkPoint 1.0 -1.0 0.0)
        //let rectangle3 = mkRectangle texture (mkPoint 0.0 0.0 0.0) 2.0 2.0

        //Cylinders
        let cylinder1 = mkOpenCylinder yellowMaterial 2.0 1.0
        let cylinder2 = mkClosedCylinder texture textureCube1 textureCube2 1.0 2.0
        let cylinder3 = mkClosedCylinder yellowMaterial yellowMaterial yellowMaterial 1.0 2.0 (mkPoint 0.0 0.0 0.0)

        //Disks
        let disk1 = mkDisk redMaterial 2.0
        let disk2 = mkDisk greenMaterial 1.5

        //Planes
        let plane1 = mkPlane blueMaterial
        let plane2 = mkPlane whiteMaterial

        //Boxes
        let box1 = mkBox (mkPoint -1.0 -1.0 -2.5) (mkPoint 1.0 1.0 2.5)  greenMaterial greenMaterial greenMaterial greenMaterial greenMaterial greenMaterial
        let box2 = mkBox (mkPoint -1.5 -1.5 -1.5) (mkPoint 0.0 0.5 0.0) redMaterial redMaterial redMaterial redMaterial redMaterial redMaterial

        //Implicit
        //let implicit1 = mkShape (mkImplicit ("x^2 + y^2 + z^2 - " + (string(1.0 * 1.0)))) redMaterial
        let implicit2 = mkShape (mkImplicit ("x")) blueMaterial
        //let implicit2 = mkImplicit blueMaterial "(x - 2)^2(x+2)^2 + (y - 2)^2(y+2)^2 + (z - 2)^2(z+2)^2 + 3(x^2*y^2 + x^2z^2 + y^2z^2) + 6x y z - 10(x^2 + y^2 + z^2) + 22"
        //let heart = mkImplicit redMaterial "(x^2 + (0.444444444)*y^2 + z^2 - 1)^3 - x^2 * z^3 - (0.1125)*y^2*z^3"
//        let heart = mkImplicit redMaterial "(x^2 + (4.0/9.0)*y^2 + z^2 - 1)^3 - x^2 * z^3 - (9.0/80.0)*y^2*z^3"
        //let implicit4 = mkImplicit redMaterial "2x^3+y^2-15z^4*-15"
        
        //Transformations
        let move = translate 0.0 0.0 -2.0
        let move2 = translate -2.0 -2.0 0.0
        let movedown = translate 2.0 0.0 0.0
        let moveandrotate = mergeTransformations [rotateY (System.Math.PI*1.0);rotateX (System.Math.PI/4.0);move]
        let rotate = mergeTransformations [rotateY (System.Math.PI*1.0);rotateX (System.Math.PI/4.0)]
        let scale = scale 0.7 1.5 0.7
        let rotate1 = (rotateX (System.Math.PI/2.0))
        let rotate2 = (rotateZ (System.Math.PI/2.0))

        //Implicit Surfaces
//        let implicit1 = mkShape (mkImplicit ("x^2 + y^2 + z^2 - " + (string(1.0 * 1.0)))) greenMaterial
        //let implicit2 = mkImplicit "(x - 2)^2(x+2)^2 + (y - 2)^2(y+2)^2 + (z - 2)^2(z+2)^2 + 3(x^2*y^2 + x^2z^2 + y^2z^2) + 6x y z - 10(x^2 + y^2 + z^2) + 22"
        //let heart = mkImplicit "(x^2 + (0.444444444)*y^2 + z^2 - 1)^3 - x^2 * z^3 - (0.1125)*y^2*z^3"
//        let heart = mkImplicit "(x^2 + (4.0/9.0)*y^2 + z^2 - 1)^3 - x^2 * z^3 - (9.0/80.0)*y^2*z^3"
        //let heart =  mkImplicit "(x - 2)^2(x+2)^2 + (y - 2)^2(y+2)^2 + (z - 2)^2(z+2)^2 + 3(x^2*y^2 + x^2z^2 + y^2z^2) + 6x y z - 10(x^2 + y^2 + z^2) + 22"
        let torus = mkShape (mkImplicit "(((x^2 + y^2)_2 - 1.5)^2 + z^2)_2 - 0.5") blueMaterial
        let rs1 = "(" + (string 1.5) + "^2" + " + " + (string 1.0) + "^2)"
        let rs2 = "(" + (string 1.5) + "^2" + " - " + (string 1.0) + "^2)"
        let sx = "x^4 + 2x^2*y^2 + 2x^2*z^2 - 2*" + rs1 + "*x^2"
        let sy = "y^4 + 2y^2*z^2 + 2*" + rs2 + "*y^2"
        let sz = "z^4 - 2*" + rs1 + "*z^2"
        let sc = rs2 + "^2"
        let eqn = sx + " + " + sy + " + " + sz + " + " + sc 
//        let torus2 = mkShape (mkImplicit eqn) blueMaterial
        //let implicit4 = mkImplicit redMaterial "2x^3+y^2-15z^4*-15"
//        let SphereRoot = mkShape (mkImplicit "(x^2 + y^2 + z^2)_2 - 1") blueMaterial
//        let texturedHeart = mkShape heart redMaterial
        
        //Transformed shapes
        let shape = mkTransformed earth moveandrotate
//        let shape1 = mkTransformed rectangle1 (shearxy 1.0)
        let shape2 = mkTransformed cylinder3 scale
        let shape4 = mkTransformed shape2 rotate2
        let hs =     mkUnion shape2 (shape4)
        let shape3 = mkTransformed hs move
        let shape6 = mkTransformed cylinder3 rotate1
       // let shape5 = mkUnion shape2 (mkUnion shape3 shape4)
        let shape6 = mkSubtraction sphere1 (mkTransformed sphere2 move)
        let shape7 = mkSubtraction earth box1
        let shape8 = mkSubtraction sphere1 (mkTransformed sphere2 move)
        let shape9 = mkUnion sphere2 sphere3
       // let movedCar = mkTransformed greenCar movedown
        let box2 = mkTransformed box1 (rotateY (System.Math.PI))
        
        //Scene
        let sphere2 = mkTransformed (mkSphere (mkMatTexture (mkMaterial (Colour.mkColour 1.0 1.0 1.0) 0.0)) 1.4 (mkPoint 1.3 1.3 1.3)) (translate 1.3 1.3 1.3)
        let sphere3 = mkTransformed (mkSphere (mkMatTexture (mkMaterial (Colour.mkColour 1.0 1.0 1.0) 0.0)) 1.4 (mkPoint -1.3 -1.3 -1.3))  (translate -1.3 -1.3 -1.3)


        let shapes = [||]
        let lights = []


        let scene = mkScene shapes 0 lights (ambilight)
        let shape5 = mkUnion shape2 (mkUnion shape3 shape4)
//
//        let movedCar = mkTransformed greenCar movedown
//        let box2 = mkTransformed box1 (rotateY (System.Math.PI))
//        
//        let r1 = mkRectangle texture (mkPoint -1. -1. 1.) (mkPoint -1. 1. 1.) (mkPoint 0.9999 -1. 1.)
//        let r2 = mkRectangle (mkMatTexture (mkMaterial (fromColor Color.Red) 0.)) (mkPoint -1. -1. 1.) (mkPoint -1. 1. 1.) (mkPoint -1. -1. -0.9999)
//        let r3 = mkRectangle (mkMatTexture (mkMaterial (fromColor Color.Yellow) 0.)) (mkPoint 1. -1. 1.) (mkPoint 1. 1. 1.) (mkPoint 1. -1. -1.)
//        let sphere = mkTransformed (mkSphere texture 0.5) (mergeTransformations [rotateY (System.Math.PI*1.0);rotateX (System.Math.PI/4.0)])
//        let light = mkLight (mkPoint 0.0 1.0 4.0) (fromColor Color.White) 0.9 
//        let light2 = mkLight (mkPoint 0.0 0.9 0.9) (fromColor Color.White) 0.7 

        let shape6 = mkTransformed sphere2 move
        let shape7 = mkSubtraction sphere1 shape6
        
        let test1 = mkTransformed cylinder3 (translate 0.5 0.0 0.0)
        let test2 = mkTransformed cylinder3 (translate -0.5 0.0 0.0)
        let test3 = mkUnion test1 test2
        let l1 () = mkLight (mkPoint 4.0 0.0 4.0) (fromColor Color.White) 1.0 in
        let l2 () = mkLight (mkPoint -4.0 0.0 4.0) (fromColor Color.White) 1.0 in
        let l3 () = mkLight (mkPoint 0.0 0.0 0.0) (fromColor Color.White) 1.0 in
        let mkUnitBox t = mkBox (mkPoint -1.0 -1.0 -1.0) (mkPoint 1.0 1.0 1.0) t t t t t t
        let cube () = mkUnitBox (mkMatTexture (mkMaterial (fromColor Color.Red) 0.0))
        let sphere () = mkSphere (mkMatTexture (mkMaterial (fromColor Color.Blue) 0.0)) 1.3 (mkPoint 0.0 0.0 0.0)  
        
        let ambientLight () = mkAmbientLight (fromColor Color.White) 0.2 in
        
        //Scene
        let shapes = [|torus|]
        let lights = [light1;light2]
        
        (* Sweet scene
        let shapes = [plane1;rectangle2]
        let lights = [light3]
        *)
//        let scene = mkScene shapes 3 lights ambilight
//        let sphere2 = mkTransformed (mkSphere (mkMatTexture (mkMaterial (Colour.mkColour 1.0 1.0 1.0) 0.0)) 1.4 (mkPoint 1.3 1.3 1.3)) (translate 1.3 1.3 1.3)
//        let sphere3 = mkTransformed (mkSphere (mkMatTexture (mkMaterial (Colour.mkColour 1.0 1.0 1.0) 0.0)) 1.4 (mkPoint -1.3 -1.3 -1.3))  (translate -1.3 -1.3 -1.3)
//        let shapes = [mkSubtraction (mkSubtraction (mkIntersect (cube ()) (sphere ())) (shape5)) sphere2]
//        let lights = [l1 (); l2 (); l3 ()]
//
        let scene = mkScene shapes 0 lights (ambientLight())
    
        //Render
        //renderToFile scene camera "../../../output.png"
        let stopWatch = System.Diagnostics.Stopwatch.StartNew()
        printf "Started drawing"
        renderToScreen scene camera
        printfn "Drew image in: %f ms" stopWatch.Elapsed.TotalMilliseconds
        stopWatch.Stop()
        0