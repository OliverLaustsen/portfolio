namespace RayTracer.Entities
open Vector
open Point

module Ray = 

    type Ray =
    |Ray of Point * Vector

    let mkRay startVector startPoint = Ray(startPoint,startVector) 
    let getVector (Ray(_,sVector)) = sVector
    let getPoint (Ray(sPoint,_))= sPoint

    let getPosition (Ray(o,d)) t = o + (t * d)