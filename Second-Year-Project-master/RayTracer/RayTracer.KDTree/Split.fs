namespace RayTracer.KDTree
open Axis

module Split =

    type Split = 
    | Split of float * Axis *(int*int)

    let getAxis (Split(_,a,_)) = a
    let getSplitPoint (Split(f,_,_)) = f