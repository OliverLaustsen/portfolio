namespace RayTracer.Entities
module Vector = 
    [<Sealed>]
    type Vector =
      static member ( ~- ) : Vector -> Vector
      static member ( + ) : Vector * Vector -> Vector
      static member ( - ) : Vector * Vector -> Vector
      static member ( * ) : float * Vector -> Vector
      static member ( * ) : Vector * Vector -> float
      static member ( % ) : Vector * Vector -> Vector
      static member ( / ) : Vector * float -> Vector
      static member Zero : Vector
  
    val mkVector : x:float -> y:float -> z:float -> Vector
    val getX : Vector -> float
    val getY : Vector -> float
    val getZ : Vector -> float
    val getCoord: Vector -> float * float * float
    val multScalar : float -> Vector -> Vector
    val magnitude : Vector -> float
    val dotProduct : Vector -> Vector -> float
    val crossProduct : Vector -> Vector -> Vector
    val normalise : Vector -> Vector
    val round : Vector -> int -> Vector
