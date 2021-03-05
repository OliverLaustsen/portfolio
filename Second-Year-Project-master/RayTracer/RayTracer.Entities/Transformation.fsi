namespace RayTracer.Entities
open RayTracer.Entities.Ray
open RayTracer.Entities.Point
open RayTracer.Entities.Vector
open BoundingBox

module Transformation =


    type Transformation =
        
        abstract member Matrix : float[,]


    val mkTransformation : float[,] -> Transformation
    val transformPoint : Point -> float[,] -> Point
    val transformBoundingbox : BoundingBox -> Transformation -> BoundingBox
    val transformVector : Vector -> float[,] -> Vector
    val translate : float -> float -> float -> Transformation * Transformation
    val mirrorX : Transformation * Transformation
    val mirrorY : Transformation * Transformation
    val mirrorZ : Transformation * Transformation
    val scale : float -> float -> float -> Transformation * Transformation
    val shearxy : float -> Transformation * Transformation
    val shearxz : float -> Transformation * Transformation
    val shearyx : float -> Transformation * Transformation
    val shearyz : float -> Transformation * Transformation
    val shearzx : float -> Transformation * Transformation
    val shearzy : float -> Transformation * Transformation
    val rotateX : float -> Transformation * Transformation
    val rotateY : float -> Transformation * Transformation
    val rotateZ : float -> Transformation * Transformation   
    val mergeTransform : Transformation -> Transformation -> Transformation
    val mergeTransformList : Transformation list -> Transformation
    val mergeInverseMatrix : Transformation list -> Transformation
    val mergeTransformations : seq<Transformation * Transformation> -> Transformation * Transformation
