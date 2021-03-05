namespace RayTracer.Core
open RayTracer.Entities

module Camera = 

    type Camera
    
    val mkCamera : position:Point.Point -> lookAt:Point.Point -> upVector:Vector.Vector -> zoom:float -> unitX:float -> unitY:float -> resWidth:int -> resHeight:int -> Camera
    val getPos : Camera -> Point.Point
    val getLookAt : Camera -> Point.Point
    val getUpVector : Camera -> Vector.Vector
    val getZoom : Camera -> float
    val getUnitX : Camera -> float
    val getUnitY : Camera -> float
    val getResWidth : Camera -> int
    val getResHeight : Camera -> int