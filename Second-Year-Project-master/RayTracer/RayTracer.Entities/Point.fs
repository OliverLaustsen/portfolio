namespace RayTracer.Entities
module Point = 
    open Vector
    type Vector = Vector.Vector

    type Point =
      | P of float * float * float
      override p.ToString() =
        match p with
          P(x,y,z) -> "("+x.ToString()+","+y.ToString()+","+z.ToString()+")"

    let mkPoint x y z                           = P(x,y,z)
    let getX (P(x,_,_))                         = x
    let getY (P(_,y,_))                         = y
    let getZ (P(_,_,z))                         = z
    let getCoord (P(x,y,z))                     = (x,y,z)
    let move (P(x,y,z)) (v:Vector)              = P(x+Vector.getX(v),y+Vector.getY(v),z+Vector.getZ(v))
    let distance (P(px,py,pz)) (P(qx,qy,qz))    = Vector.mkVector (qx-px) (qy-py) (qz-pz)
    let direction p q                           = Vector.normalise(distance p q)
    let round (P(px,py,pz)) (d:int)             = P(System.Math.Round(px,d),System.Math.Round(py,d),System.Math.Round(pz,d))

    type Point with
      static member ( + ) (P(x,y,z),v: Vector) : Point = P(x+Vector.getX(v),y+Vector.getY(v),z+Vector.getZ(v))
      static member ( ++ ) (P(x,y,z),(f:float)) = P(x+f,y+f,z+f)
      static member ( - ) (P(x,y,z),P(x2,y2,z2))       = Vector.mkVector (x-x2) (y-y2) (z-z2)
      static member ( -- ) (P(x,y,z),(f:float)) = P(x-f,y-f,z-f)
