namespace Tracer
open RayTracer.Entities
open RayTracer.Core
open RayTracer.Shapes
open RayTracer.TriangleParser

module API = 
  type dummy = unit

  type vector = Vector.Vector
  type point = Point.Point
  type colour = Colour.Colour
  type material = Material.Material
  type shape = Shape.Shape
  type baseShape = BaseShape.BaseShape
  type texture = Texture.Texture
  type camera = Camera.Camera
  type scene = Scene.Scene
  type light = Light.Light
  type ambientLight = AmbientLight.AmbientLight
  type transformation = Transformation.Transformation * Transformation.Transformation

  let mkVector (x : float) (y : float) (z : float) : vector = Vector.mkVector x y z
  let mkPoint (x : float) (y : float) (z : float) : point = Point.mkPoint x y z
  let fromColor (c : System.Drawing.Color) : colour = Colour.fromColor c
  let mkColour (r : float) (g : float) (b : float) : colour = Colour.mkColour r g b

  let mkMaterial (c : colour) (r : float) : material = Material.mkMaterial c r
  let mkTexture (f : float -> float -> material) : texture = Texture.mkTexture f
  let mkMatTexture (m : material) : texture = Texture.mkMatTexture m

  let mkShape (b : baseShape) (t : texture) : shape = Shape.mkShape b t
  let mkSphere (p : point) (r : float) (m : texture) : shape = let (x,y,z) = Point.getX p, Point.getY p, Point.getZ p
                                                               let t = Transformation.translate x y z 
                                                               let s = Shape.mkTransformed (Shape.mkSphere m r p) t
                                                               s
  let mkRectangle (bottomLeft : point) (topLeft : point) (bottomRight : point)  (t : texture) : shape
    = Shape.mkRectangle t bottomLeft topLeft bottomRight 
  let mkTriangle (a:point) (b:point) (c:point) (m : texture) : shape = Triangle.mkTriangle a b c m true
  let mkPlane (m : texture) : shape = Shape.mkPlane m
  let mkImplicit (s : string) : baseShape = Implicit.mkImplicit s
  let mkPLY (filename : string) (smooth : bool) : baseShape = TriangleMesh.mkTriangleMesh filename smooth

  let mkHollowCylinder (c : point) (r : float) (h : float) (t : texture) : shape = let (x,y,z) = Point.getX c, Point.getY c, Point.getZ c
                                                                                   let tr = Transformation.translate x y z 
                                                                                   let s = Shape.mkTransformed (Shape.mkOpenCylinder t r h) tr
                                                                                   s
  let mkSolidCylinder (c : point) (r : float) (h : float) (t : texture) (top : texture) (bottom : texture) : shape
      = let (x,y,z) = Point.getX c, Point.getY c, Point.getZ c
        let tr = Transformation.translate x y z 
        let s = Shape.mkTransformed (Shape.mkClosedCylinder t top bottom r h c) tr
        s
  let mkDisc (c : point) (r : float) (t : texture) : shape = let (x,y,z) = Point.getX c, Point.getY c, Point.getZ c
                                                             let tr = Transformation.translate x y z 
                                                             let s = Shape.mkTransformed (Shape.mkDisk t r) tr
                                                             s
  let mkBox (low : point) (high : point) (front : texture) (back : texture) (top : texture) (bottom : texture) (left : texture) (right : texture) : shape
      = Shape.mkBox low high front back top bottom left right
 

  let group (s1 : shape) (s2 : shape) : shape = Shape.mkGroup s1 s2
  let union (s1 : shape) (s2 : shape) : shape = Shape.mkUnion s1 s2
  let intersection (s1 : shape) (s2 : shape) : shape = Shape.mkIntersect s1 s2
  let subtraction (s1 : shape) (s2 : shape) : shape = Shape.mkSubtraction s1 s2

  let mkCamera (pos : point) (look : point) (up : vector) (zoom : float) (width : float)
    (height : float) (pwidth : int) (pheight : int) : camera = Camera.mkCamera pos look up zoom width height pwidth pheight
  let mkThinLensCamera (pos : point) (look : point) (up : vector) (zoom : float) (width : float)
    (height : float) (pwidth : int) (pheight : int) (lensRadius : float) (fpDistance : float) : camera = failwith "mkCamera not implemented"
  
  let mkLight (p : point) (c : colour) (i : float) : light = Light.mkLight p c i
  let mkAmbientLight (c : colour) (i : float) : ambientLight = AmbientLight.mkAmbientLight c i

  let mkScene (s : shape list) (l : light list) (a : ambientLight) (m : int) : scene = Scene.mkScene (List.toArray s) m l a
  let renderToScreen (sc : scene) (c : camera) : unit = Render.renderToScreen sc c
  let renderToFile (sc : scene) (c : camera) (path : string) : unit = Render.renderToFile sc c path

  let translate (x : float) (y : float) (z : float) : transformation = Transformation.translate x y z
  let rotateX (angle : float) : transformation = Transformation.rotateX angle
  let rotateY (angle : float) : transformation = Transformation.rotateY angle
  let rotateZ (angle : float) : transformation = Transformation.rotateZ angle
  let sheareXY (distance : float) : transformation = Transformation.shearxy distance
  let sheareXZ (distance : float) : transformation = Transformation.shearxz distance
  let sheareYX (distance : float) : transformation = Transformation.shearyx distance
  let sheareYZ (distance : float) : transformation = Transformation.shearyz distance
  let sheareZX (distance : float) : transformation = Transformation.shearzx distance
  let sheareZY (distance : float) : transformation = Transformation.shearzy distance
  let scale (x : float) (y : float) (z : float) : transformation = Transformation.scale x y z
  let mirrorX () : transformation = Transformation.mirrorX
  let mirrorY () : transformation = Transformation.mirrorY
  let mirrorZ () : transformation = Transformation.mirrorZ
  let mergeTransformations (ts : transformation list) : transformation = Transformation.mergeTransformations ts
  let transform (sh : shape) (tr : transformation) : shape = Shape.mkTransformed sh tr
