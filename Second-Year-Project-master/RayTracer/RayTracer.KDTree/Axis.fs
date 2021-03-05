namespace RayTracer.KDTree


module Axis =

    type Axis =
    | X
    | Y
    | Z

    let getNextAxis axis =
        match axis with
        | X -> Y
        | Y -> Z
        | Z -> X
