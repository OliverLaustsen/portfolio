namespace RayTracer.Entities

open Vector
open Material
open Colour

module Hit = 
        type Hit = Hit of float * Vector * Material

        let mkHit t n m = Hit(t,n,m)
        let mkEmptyHit = Hit(0.0,(mkVector 0.0 0.0 0.0),mkMaterial (mkColour 0.0 0.0 0.0) 0.0)
        let getT (Hit(t,_,_)) = t 
        let getNormal (Hit(_,n,_)) = n 
        let getMaterial (Hit(_,_,m)) = m 