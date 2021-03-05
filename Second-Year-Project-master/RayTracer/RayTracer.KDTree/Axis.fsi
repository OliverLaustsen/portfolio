namespace RayTracer.KDTree

module Axis =

    type Axis =
    | X
    | Y
    | Z

    val getNextAxis : Axis -> Axis