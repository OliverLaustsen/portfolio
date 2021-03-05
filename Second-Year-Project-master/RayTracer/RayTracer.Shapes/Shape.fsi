namespace RayTracer.Shapes

open RayTracer.Entities
open RayTracer.Expressions
open RayTracer.Entities.Material
open ExprParse
open Ray
open Point
open Material
open ExprParse
open Ray
open Point
open Vector
open ExprToPoly
open Material
open Transformation
open BoundingBox
open Texture
open Hit

module Shape =
    open RayTracer.Entities
    open RayTracer.Expressions
    open RayTracer.Helpers
    open RayTracer.Entities.BoundingBox
    open RayTracer.Entities.Material
    open RayTracer.Entities.Vector
    open RayTracer.Entities.Ray
    open RayTracer.Entities.Point
    open RayTracer.Entities.Texture
    open RayTracer.Entities.Transformation
    open BaseShape
    open Bounded

    [<Interface>]
    type Shape = 
        inherit Bounded
        //static member ( <= ) : Shape * Ray -> Material option
        //Material of the Shape
        abstract member Texture : Texture
        //Polynomium of the Shape
        //abstract member Poly : poly
        
        //Inside function for shapes
        abstract member Inside : (Point -> bool) option

        //Hit function of the Shape
        abstract member Hit : Ray -> Hit option
        //Texture of the Shape
        //abstract member Texture : texture

    [<Interface>]
    type Vertice =
        abstract member Point : Point
        abstract member Normal : Vector
        abstract member TextureCoordinate : TextureCoordinate 

    val mkVertice : x:float -> y:float -> z:float -> nx:float -> ny:float -> nz:float -> u:float -> v:float -> Vertice
    val mkVertice3 : x:float -> y:float -> z:float -> Vertice
    val mkVertice5 : x:float -> y:float -> z:float -> u:float -> v:float -> Vertice
    val mkVertice6 : x:float -> y:float -> z:float -> nx:float -> ny:float -> nz:float -> Vertice
    val mkVerticeTypes : Point -> Vector -> TextureCoordinate -> Vertice
    
    [<Interface>]
    type TriangleMesh = 
        inherit Shape
        abstract member Vertices : Map<int,Vertice>
        abstract member Triangles : Map<int,Vertice*Vertice*Vertice>

    val mkSphere : Texture -> float -> Point -> Shape

    val mkRectangle : Texture -> Point -> Point -> Point -> Shape

    val mkDisk : Texture -> float -> Shape

    val mkPlane : Texture -> Shape


    //val mkImplicit : Texture -> string -> Shape

    val mkShape : BaseShape -> Texture -> Shape

    val mkOpenCylinder : Texture -> float -> float -> Shape

    val mkClosedCylinder : Texture -> Texture -> Texture -> float -> float -> Point -> Shape

    val mkTransformed : Shape -> Transformation * Transformation -> Shape

    val mkBox : Point -> Point -> Texture -> Texture -> Texture -> Texture -> Texture -> Texture -> Shape
    
    val mkUnion : Shape -> Shape -> Shape

    val mkIntersect : Shape -> Shape -> Shape
    
    val mkSubtraction : Shape -> Shape -> Shape
    
    val mkGroup : Shape -> Shape -> Shape