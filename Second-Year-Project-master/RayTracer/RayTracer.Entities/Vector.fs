namespace RayTracer.Entities
module Vector = 
    open System.Globalization

    type Vector =
      | V of float * float * float
      override v.ToString() =
        match v with
          V(x,y,z) -> "["+x.ToString()+","+y.ToString()+","+z.ToString()+"]"

    let mkVector x y z = V(x, y, z)
    let getX (V(x,_,_))                         = x
    let getY (V(_,y,_))                         = y
    let getZ (V(_,_,z))                         = z
    let getCoord (V(x,y,z))                     = (x,y,z)
    let multScalar s (V(x,y,z))                 = V(x*s,y*s,z*s)
    let magnitude (V(x,y,z))                    = sqrt(x*x+y*y+z*z)
    let dotProduct (V(x,y,z)) (V(x2,y2,z2))     = (x*x2)+(y*y2)+(z*z2)
    let crossProduct (V(x,y,z)) (V(x2,y2,z2))   = V(y*z2-z*y2,z*x2-x*z2,x*y2-y*x2)
    let normalise (V(x,y,z) as v)               = let vM = magnitude v
                                                  V(x/vM, y/vM, z/vM)
    let round (V(x,y,z)) (d:int)                = V(System.Math.Round(x,d),System.Math.Round(y,d),System.Math.Round(z,d))

    type Vector with
      static member ( ~- ) (V(x,y,z))                               = V(-x,-y,-z)
      static member ( + ) (V(ux,uy,uz),V(vx,vy,vz))                 = V(ux+vx,uy+vy,uz+vz)
      static member ( - ) (V(ux,uy,uz),V(vx,vy,vz))                 = V(ux-vx,uy-vy,uz-vz)
      static member ( * ) (s,(V(x,y,z) as v))                       = multScalar s v
      static member ( * ) ((V(ux,uy,uz) as u),(V(vx,vy,vz) as v))   = dotProduct u v
      static member ( % ) ((V(ux,uy,uz) as u),(V(vx,vy,vz) as v))   = crossProduct u v
      static member ( / ) (V(ux,uy,uz) as v, f:float)               = multScalar f v
      static member Zero = mkVector 0.0 0.0 0.0
