namespace RayTracer.KDTree
open Axis

module Split =

    type Split = 
    | Split of float * Axis *(int*int)

    val getAxis : Split -> Axis
    val getSplitPoint : Split -> float