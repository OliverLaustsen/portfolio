namespace RayTracer.Entities
module Material = 
    open Colour

    type Material = 
    |Material of Colour * float

    let mkMaterial colour reflect = Material(colour, reflect)
    let mkMaterialFromTuple (colour,reflect) = Material(colour, reflect)
    let getColour (Material(c,_)) = c
    let getReflect (Material(_,r)) = r