namespace Tracer
open RayTracer.Entities
open RayTracer.Core
open RayTracer.Shapes


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

  val mkVector : x:float -> y:float -> z:float -> vector
  val mkPoint : x:float -> y:float -> c:float -> point
  val fromColor : c : System.Drawing.Color -> colour
  val mkColour : r:float -> g:float -> b:float -> colour

  /// Construct a material with the given colour and reflectivity. Reflectivity
  /// is the ratio of how much the material reflects (1.0 means it is a perfect
  /// mirror and 0.0 means that the material does not reflect anything).
  val mkMaterial : colour -> reflectivity : float -> material
  /// Textures are functions that take x-y coordinates and produce a material.
  /// The x-y coordinates range over the texture space specified for the
  /// individual basic shapes (mkSphere, mkPlane etc.).
  val mkTexture : (float -> float -> material) -> texture
  /// Construct a texture with a constant material for each point.
  val mkMatTexture : material -> texture


  /// Construct a textured shape from a base shape.
  /// Basic shapes are textured according to the texture space given.
  val mkShape : baseShape -> texture -> shape

  /// Construct a sphere.
  /// texture coordinates: [0,1] X [0,1]
  val mkSphere : center : point -> radius : float -> texture -> shape
  /// Construct a rectangle.
  /// texture coordinates: [0,1] X [0,1]
  val mkRectangle : bottomLeft : point -> topLeft : point -> bottomRight : point ->  t : texture -> shape
  /// Constructe a triangle.
  val mkTriangle : a:point -> b:point -> c:point -> texture -> shape
  /// Construct a plane with the equation z = 0,
  /// i.e. the x-y plane
  /// texture coordinates: R X R
  val mkPlane : texture -> shape
  /// Construct an implicit surface.
  /// texture coordinates: {(0,0)}, i.e. has only a single material
  /// The gramar for valid expressions are the following and you will need to create a parser for it
  /// x := string
  /// n := integer
  /// f := float
  /// e := e + e (addition)
  ///      e - e (subtraction)
  ///      -e    (negation)
  ///      e * e (multiplication)
  ///      e / e (division)
  ///      e^n   (exponent)
  ///      e_n   (root)
  ///      (e)   (parenthesis)
  ///      x     (variable)
  ///      n     (integer number)
  ///      f     (floating point number)
  ///
  /// Note that the expression can contain both floats and integers. The operators bind in the expected order
  /// (note that negation binds the hardest (-x^2) is (-x)^2 and not -(x^2)
  val mkImplicit : string -> baseShape
  /// Load a triangle mesh from a PLY file.
  /// texture coordinates: [0,1] X [0,1]
  val mkPLY : filename : string -> smoothShading : bool -> baseShape
  /// construct a hollow cylinder (i.e. open on both ends)
  /// texture coordinates: [0,1] X [0,1]
  val mkHollowCylinder : center : point -> radius : float -> height : float -> texture -> shape
  /// construct a solid cylinder (i.e. closed on either end by a disk)
  /// texture space: hollow cylinder part is textured like mkHollowCylinder;
  ///                top and bottom disk are textured like mkDisk
  val mkSolidCylinder : center : point -> radius : float -> height : float -> 
                        cylinder: texture -> top : texture -> bottom : texture -> shape
  /// construct a disk at point p in the plane parallel
  /// to the x-y plane
  /// texture coordinates: [0,1] X [0,1]
  val mkDisc : p : point -> radius : float -> texture -> shape
  /// construct an axis-aligned box with low being the lower left corner of the back face
  /// and high being the upper right corner of the front face
  /// textures: the six faces of the box are textured like mkRectangle
  val mkBox : low : point -> high : point -> front : texture -> back : texture ->
              top : texture -> bottom : texture -> left : texture -> right : texture  -> shape

  /// union produces the union of s1 and s2 in such a way that all internal edges are deleted.
  val union : s1: shape -> s2 : shape -> shape
  val intersection : shape -> shape -> shape
  /// subtracts s2 from s1 in such a way that s2's texture is maintained in the places where s1 is cut.
  val subtraction : s1 : shape -> s2 : shape -> shape
  /// group works like union, but does not remove internal edges
  val group : shape -> shape -> shape

  /// Construct a camera at 'position' pointed at 'lookat'. The 'up' vector describes which way is up.
  /// The viewplane is has dimensions 'unitWidth' and 'unitHeight', has a pixel resolution of 'pixelWidth' 
  /// times 'pixelHeight', and is 'distance' units in front of the camera.
  val mkCamera : position : point -> lookat : point -> up : vector -> distance : float -> 
    unitWidth : float -> unitHeight : float -> pixelWidth : int -> pixelHeight : int -> camera

  /// This only needs to be implemented by groups of size 7.
  val mkThinLensCamera : position : point -> lookat : point -> up : vector -> distance : float -> 
    unitWidth : float -> unitHeight : float -> pixelWidth : int -> pixelHeight : int -> 
    lensRadius : float -> fpDistance : float -> camera

  val mkLight : position : point -> colour : colour -> intensity : float -> light

  val mkAmbientLight : colour : colour -> intensity : float -> ambientLight

  val mkScene : shapes : shape list -> lights : light list -> ambientLight -> max_reflect : int -> scene
  val renderToScreen : scene -> camera -> unit
  val renderToFile : scene -> camera -> filename : string -> unit

  /// For rotations all angles are in radians. Note that angles greater than 2*pi and less than zero are possible.
  val rotateX : angle : float -> transformation
  val rotateY : angle : float -> transformation
  val rotateZ : angle : float -> transformation
  val sheareXY : distance : float -> transformation
  val sheareXZ : distance : float -> transformation
  val sheareYX : distance : float -> transformation
  val sheareYZ : distance : float -> transformation
  val sheareZX : distance : float -> transformation
  val sheareZY : distance : float -> transformation
  val scale : x : float -> y : float -> z : float -> transformation
  val translate : x : float -> y : float -> z : float -> transformation
  val mirrorX : unit -> transformation
  val mirrorY : unit -> transformation
  val mirrorZ : unit -> transformation
  /// Merge the givne list of transformations into one, such that the resulting
  /// transformation is equivalent to applying the individual transformations
  /// from left to right (i.e. starting with the first element in the list).
  val mergeTransformations : transformation list -> transformation
  val transform : shape -> transformation -> shape
  
