namespace RayTracer.Entities
open Vector
open Point

module Ray =


    type Ray
    
    val mkRay: Vector -> Point -> Ray
    val getVector: Ray -> Vector
    val getPoint: Ray -> Point
    val getPosition: Ray -> float -> Point